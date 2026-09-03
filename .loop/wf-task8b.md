# Task 8b：自动节点生产闭环

## Goal

在不改写 M3a-1 可靠执行内核的前提下，把 Webhook 自动节点接入真实流程入口和现有后台调度体系，形成可恢复、可审计、有限重试的生产闭环：

```text
EnterNodeOp 进入 Webhook
→ 同事务创建/复用 WfNodeExecution
→ 后台 worker 领取
→ 事务外调用 IWorkflowNodeHandler
→ 通过 fence/CAS 回写
→ Token 最多推进一次
```

## Done-condition

只有同时满足以下条件，才能把 `status` 改为 `DONE`：

1. `EnterNodeOp` 进入 Webhook 节点时，在同一工作流事务中创建或幂等复用唯一的 `WfNodeExecution`，但绝不在该事务中发送 HTTP 请求。
2. 生产后台任务复用仓库现有 `IAdminJob`/调度模式扫描并调用 `WfNodeExecutionDispatcher`，支持 `Pending`、到期 `RetryScheduled` 和租约过期 `Running`，不另造通用 worker fleet。
3. `MaxAttempts` 有明确、受校验、经过测试的生产来源，并在 execution 创建时固化；运行中的 execution 不随之后的配置变化漂移。
4. 外部 cancellation 保持传播且不伪装成业务 attempt；未知非取消 handler 异常会留下受控审计并消耗有限重试预算，最终不会因 lease 反复过期形成无限活锁。
5. handler 调用始终位于数据库事务外；旧 owner 的迟到结果无法写入 attempt/outbox 或推进 Token。
6. 完整 E2E 测试至少覆盖：成功只推进一次、先失败后重试成功、terminal/manual fallback、外呼后结果提交前崩溃并恢复。
7. 人工 Approval/CC/Branch、M2c 幂等、历史序号、`NodeVisitId`、`ExecutionKey`、attempt/outbox 既有契约无回归。
8. outbox 边界准确：本轮只保证终态 `Pending` 幂等入队；实际 `Dispatching/Dispatched/Failed` 消费闭环单列 Task 8c，不把状态枚举写成已交付的生产派发。
9. `docs/workflow` 权威文档准确记录生产闭环、配置来源、异常策略、at-least-once 外部副作用、下游幂等要求及延期边界。
10. 目标测试、原样 workflow 必跑过滤集、Release build 和适用静态检查通过；涉及数据库 CAS/扫描语义的 SQLite、MySQL、PostgreSQL、SQL Server 验证全部通过。
11. 独立 review 后没有未解决的 P1/P2，全部任务已勾选，最终 Git/CI/文档证据写入本台账。

若代码与本地验证已经完成，但四库 CI 需要 push 且用户尚未针对 Task 8b 明确授权，则把 `status` 改为 `WAITING_FOR_PUSH_AUTH`，不得把本地 SQLite 结果表述为四库已绿。

## State

- status: `DONE`
- round: `19 / 40`
- baseline: `961cf8dd77bca4d93c36718b6d71dbae7710490d`
- baseline branch: `dev`
- baseline remote divergence: `origin/dev...dev = 0 behind / 0 ahead`
- push-authorized: `true`
- protected untracked path: `backend/tests/TenonAdmin.Tests/TestResults/`
- current task: T19 最终收口
- next: 无；Task 8b 已完成，保留受保护的 `TestResults` 未跟踪目录。

## Fixed decisions

1. 复用现有 `WfNodeExecutionDispatcher`、execution store、claim/lease/fence/attempt/outbox 状态机，不复制第二套执行链。
2. Webhook/handler 调用必须位于数据库事务外。
3. 外部副作用语义是 **at-least-once**；`ExecutionKey` 是下游幂等身份，本地工作流状态只允许成功推进一次。不得宣称 exactly-once。
4. Task 8b 不包含 M3a-2、M3b、RAG、Agent、设计 Copilot 或外部 outbox transport。
5. 未知非取消异常必须有限收敛，不能依赖无限 lease 重领；外部 cancellation 不得被 catch-all 改写成业务失败。
6. `MaxAttempts` 在 execution 创建时固化。优先级目标为“节点级配置 → Module 全局配置 → 内置安全默认值”，具体字段和配置形状必须在计划轮结合现有模型决定，不能平行发明配置体系。
7. outbox 本轮只承诺终态 `Pending` 幂等入队；消费者状态机和真实 transport 归 Task 8c。若代码中已有可直接复用且无需新增抽象的消费者 Seam，必须先在计划中给出证据，再决定是否调整本边界。
8. 不修改已经发布的 `ExecutionKey` 契约：六个维度及顺序、LF 分隔、字符串 trim 后保留大小写、null/blank scope 与缺失 `NodeVisitId` 的 `"-"` 哨兵、UTF-8 SHA-256 小写 hex。
9. 不重写 append-only `WfHistory` 或 `WfNodeExecutionAttempt` 旧行；`PayloadVersion` 继续保持 legacy `0`、新事件 `1`。
10. 数据库差异封装在基础设施层；不在业务期望中按 `DbType` 分叉。
11. 不新增依赖；优先复用现有 `IAdminJob`、配置、日志、DI、测试基建和 helper。

## Scope exclusions

- M3a-2：动态表单、字段权限、加签、减签、拿回、比例票签、长期委托、并行网关、多 Token、React 工作流整体 port、Webhook 设计器 UI。
- M3b：AI Decision、模型 Provider、proposal/policy、shadow mode、AI 审计与 AI 配置 UI。
- M3+：RAG、Agent、设计 Copilot。
- 通用 worker fleet、第二套 workflow backend、外部消息中间件、供应商 SDK。
- 无关格式化、重命名、清理、抽象提取和全仓重构。

## Safety and Git rules

1. 每轮开始先运行 `git status --short --branch`，保护用户已有改动。
2. 不碰真实 `admin.db`。
3. 不删除、清理、提交或改写 `backend/tests/TenonAdmin.Tests/TestResults/`。
4. 未经用户针对本任务明确授权，不 commit、不 push。
5. 获得授权后只允许普通 push，禁止 force-push；每个 commit 必须遵循仓库 Lore trailer 协议。
6. 不把已有警告或无关基线失败伪装成本轮回归，也不以“基线问题”为由忽略本轮造成的失败。
7. 当前代码和测试高于规划文档；不能因文档描述目标能力就假设它已经实现。

## Tasks

- [x] **T1 基线与计划**：核对 Git、M3a-1 实现、真实缺口、现有 `IAdminJob` 调度范式、配置模式、测试入口和权威文档；输出文件级实施顺序、事务边界、测试矩阵和风险，不改产品代码。
- [x] **T2 入口红测**：先补失败测试，证明进入 Webhook 时应原子创建/复用 execution、回滚不残留、同一节点访问不重复创建，并且事务内不调用 handler。
- [x] **T3 入口实现**：最小修改 `EnterNodeOp` 及必要装配，复用稳定 `ExecutionKey` 与现有 execution store；保持人工节点行为不变。
- [x] **T4 重试预算红测**：先补 `MaxAttempts` 节点级/全局/默认来源、合法范围、非法配置和创建时快照语义测试。
- [x] **T5 重试预算实现**：实现最小生产配置来源及 execution 快照，不新造平行配置体系、不新增依赖。
- [x] **T6 worker 红测**：先补 `Pending`、到期 `RetryScheduled`、过期 `Running`、未到期跳过、批量上限、取消、单项失败隔离和重复扫描测试。
- [x] **T7 worker 实现**：复用现有 `IAdminJob`/调度范式实现生产 execution worker，仅负责扫描和调用现有 dispatcher。
- [x] **T8 未知异常红测**：证明未知非取消 handler 异常有限重试并审计；外部 cancellation 原样传播且不产生伪 attempt。
- [x] **T9 未知异常实现**：用独立受控错误码和摘要收敛未知异常，不泄漏敏感正文，不改变已分类 Webhook 错误码语义。
- [x] **T10 并发与恢复测试**：覆盖 lease/fence、迟到 owner、重复扫描、handler 返回前崩溃、外呼后提交前崩溃和 Token 最多推进一次。
- [x] **T11 完整 Webhook E2E**：覆盖成功、重试后成功、terminal/manual fallback、恢复路径，并证明外呼在事务外。
- [x] **T12 outbox 边界**：补契约测试/文档，确认终态 `Pending` 幂等入队；把真实消费闭环明确登记为 Task 8c。
- [x] **T13 本地全量验证**：运行目标测试、原样 workflow 过滤集、Release build 和适用静态检查；修复本任务引入的失败并记录精确计数。
- [x] **T14 文档回写**：更新 `docs/workflow` 权威文档，记录最终代码语义、配置来源、异常策略、at-least-once、幂等和延期项。
- [x] **T15 独立 review**：让独立 reviewer 对事务边界、重复副作用、lease/fence、异常/取消、配置快照、四库 SQL、兼容性和文档真实性做审查；新增 P1/P2 必须插入本列表并修复。
- [x] **T16 review 修复与复验**：修完所有 P1/P2，对承重测试做针对性 mutation/sabotage 验证并精确复原。
- [x] **T17 最终本地验收**：复跑全部必需验证；若需要远端四库 CI 且无授权，将状态改为 `WAITING_FOR_PUSH_AUTH` 并记录待推 HEAD、diff、已跑证据和待跑 workflow。
- [x] **T18 提交、推送与四库 CI（需明确授权）**：按 Lore 协议形成小而原子的 commit，普通 push，逐腿核对 SQLite/MySQL/PostgreSQL/SQL Server 及 companion checks；未授权时不得勾选。
- [x] **T19 最终收口**：确认任务全勾选、无 P1/P2、四库绿、文档准确、本地与远端同步、受跟踪工作树干净，保护 TestResults，并将状态改为 `DONE`。

## Per-round protocol

每轮必须严格执行：

1. 读取仓库根 `AGENTS.md`、`CLAUDE.md` 和本台账；以本台账的 `current task`、`next` 和首个未勾选任务恢复上下文。
2. 执行 Guard：
   - `status = DONE`：不再改文件，报告完成并停止。
   - `round >= 40`：停止，列出未完成任务和证据，不伪称完成。
   - `status = WAITING_FOR_PUSH_AUTH` 且没有新的明确授权：不 commit、不 push、不重复改代码，只报告待授权事项并停止。
3. 每轮只做一个可验证单元：plan、红测、实现、review、finding 修复或验收之一；不要同轮吞并后续任务。
4. 行为修改必须测试先行；先确认测试在缺少目标行为时失败，再做最小实现。
5. 对承重测试至少做一种合理错误实现的 mutation/sabotage，证明测试会转红，之后精确复原。
6. 运行与本轮风险匹配的最窄测试并读取真实输出；只有任务及验证完整结束才能勾选。
7. 更新 `State`、`Decisions`、`Findings`、`Verification evidence` 和 `Round log`；将 round 加 1，明确下一轮唯一 `NEXT`。
8. 停止本轮。下一轮只能依靠本文件恢复，不依赖对话记忆。

## Decisions

- 2026-09-03：M3a-1 已在 commit `961cf8d` 收口；Task 8b 只补生产入口、worker、有限重试和真实闭环，不重开 M3a-1 已验收内核。
- 2026-09-03：Task 8b 完成后主线进入 M3b；M3a-2 可并行，但不纳入本轮。
- 2026-09-03：outbox 实际消费闭环暂定 Task 8c；Task 8b 保留并验证终态 `Pending` 幂等入队。
- 2026-09-03：T1 计划确认 Webhook 入口必须在 `EnterNodeOp.ExecuteAsync` 已领取 token、生成并写入 `NodeVisitId` 之后，同一 `IWorkflowEngine.ExecuteAsync` 事务内调用 `WfNodeExecutionStore.EnsureAsync`；只创建/复用 `Pending` execution，不在该事务或该 Op 中调用 handler。`ExecutionKey` 沿已发布六维顺序，scope 取现有命令上下文的 `StarterOrgId` 经 `WfIdentityHash.NormalizeScopeKey` 归一化，节点访问取当前 token 的 `NodeVisitId`。
- 2026-09-03：T1 计划确定 `MaxAttempts` 形状为 `WfNodeProps.MaxAttempts`（节点级可空覆盖）和 `WorkflowOptions.MaxAttempts`（`TenonAdmin:Workflow:MaxAttempts` 全局值），解析优先级为节点 → 全局 → 内置安全默认值 3；总尝试次数合法范围暂定 `[1,100]`。全局在 `AddTenonAdminWorkflow` 绑定期校验，节点在发布校验和入口快照前校验，execution 只保存解析后的整数，之后配置变化不影响既有行。
- 2026-09-03：T1 计划确定生产 worker 命名为 `WfNodeExecutionJob`，实现 `IAdminJob` 并注册一个独立的固定编码 `sys_job` 种子；复用 `JobExecutor` 的进程实例 Id 作为 execution lease owner，按 `Status/NextRetryAtUtc/LeaseExpiresAtUtc` 扫描 `Pending`、到期 `RetryScheduled` 和过期 `Running`，逐项调用现有 dispatcher。扫描批量和 execution lease 时长进入 `WorkflowOptions`，取消直接传播，单项非取消基础设施故障隔离并记录日志。
- 2026-09-03：T1 计划确定未知非取消 handler 异常只在 dispatcher 的 handler 调用边界收敛为受控 `RetryableFailure`，新增独立 workflow 错误码 48032，审计摘要只写固定安全摘要/异常类型，不写异常正文；外部 `OperationCanceledException` 仍原样传播。由现有 `MaxAttempts` 判定最终 `Failed`，避免 lease 过期后无限活锁；Webhook 已分类的 48029/48030/48031 语义不改。

## T1 文件级实施计划

### 实施顺序

1. T2 在 `backend/tests/TenonAdmin.Tests/` 新增入口红测，使用现有 `WorkflowAppFactory`/真实 `IWorkflowEngine` 和可探针 handler，证明 Webhook 进入时 execution 的原子性、`ExecutionKey` 幂等性、回滚不残留以及 handler 尚未被调用。先运行并确认红，再进入 T3。
2. T3 修改 `backend/src/TenonAdmin.Workflow/Engine/Operations/EnterNodeOp.cs`，并在必要时修改 `WorkflowEngine.cs` 的入口辅助方法；放行 `WfNodeType.Webhook` 的发布校验位于 `backend/src/TenonAdmin.Workflow/Services/WfDefinitionService.cs`。入口只做 `EnsureAsync`，不复制 dispatcher，不发 HTTP；人工 `approval/cc/branch/start` 分支保持原路径。
3. T4/T5 在 `WorkflowOptions.cs`、`WfNode.cs`、`WorkflowSetup.cs` 和必要的校验文件中落预算来源与范围；把解析后的 `MaxAttempts` 写入新 execution。T4 先补节点/全局/默认、非法范围和配置快照红测，T5 再实现。节点模型已发布版本不能因为后续全局设置变化而漂移。
4. T6/T7 新增 `backend/src/TenonAdmin.Workflow/Jobs/WfNodeExecutionJob.cs` 与对应固定编码种子文件，必要时只在 `WorkflowSetup.cs` 注册 dispatcher/job/seed；复用 `IAdminJob`、`JobExecutor`、`JobSchedulerService` 和现有调度选主/领取 CAS，不新增通用 worker fleet。worker 查询只读候选 Id/行，真正领取仍由 dispatcher 的 tx1 完成。
5. T8/T9 修改 `WfNodeExecutionDispatcher.cs` 仅包住 handler 调用的异常边界，并更新 `WorkflowErrorCode.cs`；保持 OCE 传播、已分类 Webhook 错误码和 tx2 fence CAS 不变。为未知异常补 attempt、有限重试、终态审计和敏感正文不落库的红测。
6. T10/T11 在现有 `WfNodeExecutionDispatcherTests.cs`/`WfWebhookNodeHandlerTests.cs` 之外新增生产入口/worker E2E 测试文件（如 `WfNodeExecutionProductionTests.cs`），覆盖成功单推进、失败后重试成功、terminal/manual fallback、外呼后 tx2 前崩溃恢复；测试同时核对 Token、`NodeVisitId`、`ExecutionKey`、attempt 和 outbox。
7. T12 只补 `Pending` outbox 的终态幂等契约与 Task 8c 边界说明；不实现 `Dispatching/Dispatched/Failed` 消费者状态机。T13 更新适用的 `.github/workflows/backend-ci.yml` SqlServer 过滤项，运行目标类、原样 workflow 过滤集、Release build、静态检查，并逐项记录 SQLite 与四库 CI 证据边界。
8. T14 回写 `docs/workflow/workflow-design-plan-2026-08-17.md`、`workflow-database-design-review-2026-08-24.md`、`elsa3-slickflow-ai-reference-2026-08-23.md` 中当前仍宣称“无生产入口/worker/MaxAttempts 来源”的段落，写清 at-least-once 外部副作用、下游用 `ExecutionKey` 幂等、异常策略与 Task 8c 延期边界。T15/T16 独立 review、承重测试 mutation/sabotage 后精确复原，T17 完成本地验收。

### 事务与状态边界

```text
HTTP/引擎事务
  EnterNodeOp: token Version CAS → 新 NodeVisitId → NodeEnter history → Ensure execution(Pending)
  rollback: 上述所有行一起消失

事务外
  WfNodeExecutionJob 扫候选 → WfNodeExecutionDispatcher 读取快照 → handler/HTTP 外呼

tx1                         tx2
claim + lease + fence  →    fence/CAS → attempt → execution result
AttemptCount + 1             → token/history/outbox/人工 fallback 同事务
```

`RunAsync` 的 tx1、事务外 handler、tx2 仍是唯一执行链。旧 owner 的 tx2 必须在 `Id + Fence + Running` 任一条件不符时整体回滚，不能写 attempt/outbox，也不能推进 Token。`RetryScheduled` 只在到期后重新 claim；`Pending`/到期 retry/过期 running 是 worker 的候选集合，实际单赢家由 dispatcher claim CAS 决定。

### 测试矩阵与证据要求

| 阶段 | 必须先红/后绿的行为 | 主要文件 | 四库/静态关注点 |
| --- | --- | --- | --- |
| T2–T3 | Webhook 入口原子建行、同 key 复用、事务回滚无残留、入口不外呼；人工节点零回归 | 新入口测试、`EnterNodeOp.cs`、`WfDefinitionService.cs` | `NodeVisitId`/key 字段来自真实库行；不碰人工路径 |
| T4–T5 | 节点/全局/默认预算、`[1,100]` 校验、execution 创建快照 | 新预算测试、`WorkflowOptions.cs`、`WfNode.cs` | 不接受后续 options/model 修改漂移；不以 `Math.Max` 掩盖非法配置 |
| T6–T7 | 三类候选状态、未来 retry 跳过、批量上限、重复扫描、取消、单项隔离 | 新 worker 测试、worker/seed、`WorkflowSetup.cs` | SQL 只用参数和 provider-neutral 谓词；SqlServer 过滤集纳入关键扫描测试 |
| T8–T9 | 未知异常受控 attempt/重试/最终 Failed；OCE 原样传播；错误摘要不含 secret | dispatcher/error code/目标测试 | 已分类 48029/48030/48031 不变；无异常正文入库 |
| T10–T11 | 成功只推进一次、失败重试成功、terminal/manual fallback、外呼后提交前崩溃恢复 | 生产 E2E + 既有 dispatcher/Webhook 测试 | 事务外探针、旧 fence、Token/NodeVisitId/attempt/outbox 全链路 |
| T12–T14 | 终态 Pending 幂等入队；8c 消费延期准确；文档与代码一致 | outbox 测试、三份权威文档 | 不把枚举齐全写成消费闭环交付；prose lint |
| T13/T17 | 目标类、原样 workflow filter、Release、适用静态检查 | `.github/workflows/backend-ci.yml`、测试项目 | 本地只宣称实际跑到的 DB；四库依赖 push 授权则进入等待门 |

### T1 风险登记

- `EnterNodeOp` 的 token CAS、历史序号和 execution ensure 必须留在同一个引擎事务；先查后插在并发入口下可能撞唯一键，T2/T3 需按现有 `WfOperationReceiptService` 的 savepoint/认赢家模式决定是否补最小恢复，而不能直接吞 PostgreSQL 事务异常。
- 当前 `WfNodeExecution` 扫描索引覆盖 `(Status, NextRetryAtUtc)`，过期 `Running` 还依赖 `LeaseExpiresAtUtc` 谓词；先以 provider-neutral OR 查询和批量实测为基线，若四库计划证明需要索引再单独评审，不能提前引入方言 SQL。
- execution lease 必须覆盖 handler 的有效 deadline；默认配置、Webhook 自身 `[1,120]` 秒超时、JobExecutor 停机取消和自定义 handler 的实际耗时需要一起压测，避免 worker job 本身先失效造成重复外呼。
- `WfOutbox.MessageKey` 只对本地终态 Pending 幂等；外部 HTTP 副作用仍是 at-least-once。下游必须以 `ExecutionKey` 做幂等，不能把本地 fence 或 outbox 行数表述成 exactly-once。
- 当前 worker/入口相关测试尚不存在；既有 417 条过滤集只证明 M3a-1 基线，不证明 Task 8b。未知 handler 异常、真实 HTTP/数据库交错和四库结果必须在后续任务中逐项取得证据。

## Findings

- 当前权威文档明确承认：没有生产代码创建 `wf_node_execution`，没有生产 worker 调用 dispatcher，`EnterNodeOp` Webhook wiring 尚未交付。
- T1 曾发现 `MaxAttempts` 只有状态字段和判定、没有生产来源；T5 已补齐来源与快照，后续只需在 worker/E2E 中继续验证其运行时效果。
- 未知 handler exception 在真实 worker 上线后可能导致 `Running` 租约反复过期并形成 livelock，必须在本任务裁定。
- Webhook 设计器 UI 属于 M3a-2，不应借 Task 8b 扩入。
- T2 红测已经把入口缺口固定为可执行证据：真实引擎进入 Webhook 当前仍抛 48008；同一 `NodeVisitId` 的重复 `EnterNodeOp` 当前无法完成，且回滚路径必须不留下 execution/history/token 变更。
- T3 已补齐入口最小接线：`EnterNodeOp` 在既有 token CAS/`NodeVisitId`/`NodeEnter` 顺序之后调用 `WfNodeExecutionStore.EnsureAsync`；`WfDefinitionService` 已把 Webhook 加入发布类型白名单。T3 没有调用 handler、改变人工节点或修改 execution store/dispatcher 内核。
- T4 红测确认预算缺口可执行验证：当前没有 `WorkflowOptions.MaxAttempts` 或 `WfNodeProps.MaxAttempts`，入口创建的 execution `MaxAttempts` 仍为 0，绑定期不拒绝全局 0/101，节点 JSON 中的 0 也未阻断入口。
- T5 已补齐预算来源：`WorkflowOptions.MaxAttempts` 默认 3、全局绑定期校验 `[1,100]`，`WfNodeProps.MaxAttempts` 可空覆盖且在发布/入口均校验；`EnterNodeOp` 在写 execution 前解析并固化最终整数，后续 options/model 变化不改既有 execution。
- T6 红测确认生产 worker 尚不存在：预期 `WfNodeExecutionJob` 未在 Workflow 程序集中出现，也未注册为 `IAdminJob`；扫描批量配置 `WorkflowOptions.NodeExecutionScanBatchSize` 尚不存在。候选状态、批量、取消、单项隔离和重复扫描的行为已先锁定。
- T7 已接入生产 worker：`WfNodeExecutionJob` 注册为 `IAdminJob`，由固定 `sys_job` 行触发；扫描只负责候选读取，实际 claim/lease/fence/handler/回写全部复用 dispatcher。未知 handler 异常的受控收敛仍待 T8/T9。
- T8 红测确认当前未知 handler 异常会被 worker 的单项隔离吞到日志、execution 留在 `Running` 且 attempt 为 0；取消则仍可直接传播且不写 attempt/outbox。T9 必须把前者移到 dispatcher handler 边界收敛，不能靠 worker 伪造 attempt。
- T9 已将未知非 OCE handler 异常固定收敛为 `RetryableFailure/48032`，完整异常只进 logger，数据库摘要只含异常类型；OCE 仍原样传播。未知异常现在由现有 `MaxAttempts` 路径有限收敛，避免 Running lease 无限重领。
- T10 恢复测试确认 handler 返回前中断会保留 `Running`/领取次数、无 attempt，过期后新 worker 可继续；tx2 提交前崩溃后外部副作用可能重复，但相同 `ExecutionKey` 只让下游实际生效一次，Token/实例终态/本地 outbox 只推进一次。
- T11 已用真实服务层发布/发起路径和真实 `WebhookNodeHandler` 完成成功、retry、terminal、manual fallback、事务外外呼和提交前恢复；HTTP transport 仍是测试 fake，不把本地 E2E 表述成真实外部网络验证。
- T12 已将 outbox 边界固定为“终态结果在 tx2 内幂等入队为 `Pending`”；Task 8b 不注册 outbox consumer、不领取/重投、不写 `Dispatching/Dispatched/Failed`，真实消费闭环单列 Task 8c。
- T13 本地验收没有发现本任务引入的后端/前端失败；后端仍有 13 个基线警告，前端 build 只有既有 Rollup 注释/大 chunk 提示。契约 drift 首次运行因两份 schema 尚未包含新增 `maxAttempts` 返回非零，生成并核对后两份文件只新增同一字段且 hash 一致；为遵守 admin.db 保护边界未再次运行会连接示例数据库的脚本。
- T14 已将三份 `docs/workflow` 权威文档中的旧“无生产入口/worker/MaxAttempts 来源”描述更新为 Task 8b 当前事实，并明确本地/历史四库证据边界；同时保留 M3a-2 Webhook UI 和 Task 8c outbox consumer/transport 的延期。
- T15 独立 review 新增以下未关闭项：P1 为 dispatcher 非 handler 的永久数据/模型失败经 worker catch 后仍可反复 claim 的 poison livelock；P2 为测试直接解析 `IAdminJob`、未贯通现有 scheduler/JobExecutor 触发链，以及 SQL Server PR filter 未包含 Task 8b 新增入口/worker/E2E 类。P3 为部分新测试注释仍称 worker/配置“尚未实现”。

## Verification evidence

- 2026-09-03 初始状态：`dev` HEAD 为 `961cf8dd77bca4d93c36718b6d71dbae7710490d`，与 `origin/dev` 分歧 `0/0`。
- 2026-09-03 初始工作树：受跟踪文件无改动；仅有受保护的未跟踪目录 `backend/tests/TenonAdmin.Tests/TestResults/`。
- M3a-1 最终既有证据：Release build 0 errors/13 existing warnings；workflow filter 417/417；四库 CI run `33738099310` 成功。以上只是 Task 8b 的基线，不是 Task 8b 完成证据。
- 2026-09-03 T2 红测：新增 `backend/tests/TenonAdmin.Tests/WfNodeExecutionEntryTests.cs`，`dotnet test backend/TenonAdmin.slnx --no-restore --filter "FullyQualifiedName~WfNodeExecutionEntryTests"` → **2 failed / 1 passed / 0 skipped**；两条失败均为当前 `EnterNodeOp.cs:69` 的 48008 unsupported 分支，证明目标行为尚未实现；回滚测试验证当前事务失败后无 token/history/execution 残留。
- 2026-09-03 T3 验证：`dotnet test backend/TenonAdmin.slnx --no-restore --filter "FullyQualifiedName~WfNodeExecutionEntryTests|FullyQualifiedName~WfNodeVisitIdTests|FullyQualifiedName~WfBranchPublishValidationTests|FullyQualifiedName~LeaveWorkflowE2ETests"` → **15 passed / 0 failed / 0 skipped**。
- 2026-09-03 T3 mutation：临时将 `EnterNodeOp.EnterWebhookAsync` 的 `EnsureAsync` 替换为 `Task.CompletedTask`，同一入口目标集 → **2 failed / 1 passed**，两条失败均在 execution `Assert.Single`；随后恢复 `EnsureAsync` 并复跑目标集 **15/15**。产品文件恢复后仅保留预期改动。
- 2026-09-03 T4 红测：新增 `backend/tests/TenonAdmin.Tests/WfNodeExecutionRetryPolicyTests.cs`，`dotnet test backend/TenonAdmin.slnx --no-restore --filter "FullyQualifiedName~WfNodeExecutionRetryPolicyTests"` → **7 failed / 2 passed / 0 skipped**；失败分别落在默认 execution 预算为 0、`WorkflowOptions.MaxAttempts` 配置属性不存在、全局非法值未被拒绝、节点非法值未触发回滚，证明测试正在验证真实缺口。
- 2026-09-03 T5 验证：预算专项 `WfNodeExecutionRetryPolicyTests` → **9 passed / 0 failed / 0 skipped**；原样 workflow 过滤集 `FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow` → **429 passed / 0 failed / 0 skipped**。
- 2026-09-03 T5 mutation：临时让入口忽略 `Node.Props.MaxAttempts`，预算专项 → **3 failed / 6 passed**（节点覆盖、快照和非法节点三条分别转红）；恢复节点优先解析后，入口/预算/人工联合目标集 → **24 passed / 0 failed / 0 skipped**。
- 2026-09-03 T6 红测：新增 `backend/tests/TenonAdmin.Tests/WfNodeExecutionWorkerTests.cs`，`dotnet test backend/TenonAdmin.slnx --no-restore --filter "FullyQualifiedName~WfNodeExecutionWorkerTests"` → **5 failed / 0 passed / 0 skipped**；失败均因预期 worker 类型/DI 注册不存在，另批量测试明确落在预期 `NodeExecutionScanBatchSize` 配置属性不存在。
- 2026-09-03 T7 验证：worker 专项（含 seed 契约）→ **6 passed / 0 failed / 0 skipped**；worker/入口/预算/人工/调度相关联合目标集 → **98 passed / 0 failed / 0 skipped**；原样 workflow 过滤集 → **435 passed / 0 failed / 0 skipped**。
- 2026-09-03 T7 mutation：临时将 worker 过期 Running 谓词 `< nowUtc` 反转为 `> nowUtc`，worker 专项 → **1 failed / 4 passed**，失败精确落在 expired-running 应派发断言；恢复后 worker 专项 **6/6**，临时改动未保留。
- 2026-09-03 T8 红测：新增 `backend/tests/TenonAdmin.Tests/WfNodeExecutionExceptionTests.cs`，`dotnet test backend/TenonAdmin.slnx --no-restore --filter "FullyQualifiedName~WfNodeExecutionExceptionTests"` → **1 failed / 1 passed / 0 skipped**；未知异常用例在第一次执行状态断言处显示 `Expected RetryScheduled / Actual Running`，取消用例通过且无 xUnit 新警告。
- 2026-09-03 T9 验证：`WfNodeExecutionExceptionTests` + `WfNodeExecutionDispatcherTests` + `WfWebhookNodeHandlerTests` → **84 passed / 0 failed / 0 skipped**；未知异常首 attempt、预算耗尽和 OCE lease 恢复路径均通过。
- 2026-09-03 T9 mutation：临时将未知异常结果从 `RetryableFailure` 改为 `TerminalFailure`，`WfNodeExecutionExceptionTests` → **1 failed / 1 passed**，失败精确落在首轮应为 `RetryScheduled` 的断言；恢复后上述联合目标集 **84/84**。
- 2026-09-03 T10 验证：新增恢复测试 + 全部 `WfNodeExecutionDispatcherTests` → **27 passed / 0 failed / 0 skipped**；恢复测试覆盖 worker OCE 中断、lease 重领、tx2 提交前模拟崩溃、at-least-once 外部副作用和 Token/实例只推进一次。
- 2026-09-03 T10 mutation：临时移除 `WorkflowEngine.ClaimExecutionWritebackAsync` 终态分支的 `Fence == fence`，stale-owner 测试 → **1 failed / 0 passed**（预期 48004 未抛）；恢复 Fence CAS 后恢复测试 + dispatcher → **27/27**。
- 2026-09-03 T11 验证：新增 `backend/tests/TenonAdmin.Tests/WfNodeExecutionProductionE2ETests.cs`，发布/发起/worker/真实 Webhook handler 联合 → **5 passed / 0 failed / 0 skipped**；覆盖成功单推进、retry 后成功、terminal、manual fallback、事务外探针和 tx2 提交前恢复。
- 2026-09-03 T11 mutation：临时在 `WfNodeExecutionJob` 跳过 dispatcher，T11 E2E → **5 failed / 0 passed**；失败分别落在 retry/terminal/manual/恢复或事务外调用断言，恢复 dispatcher 调用后 T11 E2E **5/5**。
- 2026-09-03 T12 验证：`WfOutboxTests`（含边界契约）→ **10 passed / 0 failed / 0 skipped**；两份权威工作流文档已写明 Task 8c 的真实消费延期边界。
- 2026-09-03 T12 mutation：临时让 `WfOutboxStore.EnqueueAsync` 插入 `Dispatched` 而非默认 `Pending`，outbox 专项 → **1 failed / 9 passed**，失败精确落在初始状态断言；恢复后 outbox 专项 **10/10**。
- 2026-09-03 T13 后端 Release build：`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` → **0 errors / 13 existing warnings**。
- 2026-09-03 T13 Release 目标测试：入口/预算/worker/异常/恢复/生产 E2E/outbox/dispatcher/Webhook → **119 passed / 0 failed / 0 skipped**；原样过滤集 `FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow` → **445 passed / 0 failed / 0 skipped**。
- 2026-09-03 T13 frontend：Vue `typecheck`/`lint`/`test` → **127/127**，`build` 成功；React `typecheck`/`lint`/`test` → **808/808**，`build` 成功。React test 输出已有 `:3000 ECONNREFUSED` 探针噪声但退出码为 0；未新增前端源码告警。
- 2026-09-03 T13 API schema：`scripts/check-contract-drift.mjs` 首次退出 1，生成 diff 仅为两份 schema 同步新增 `WfNodeProps.maxAttempts`；两份生成文件 SHA-256 均为 `E0E20825BF78FF3F1935B6C8BD6DBF00B7A423815DA2354E43669…`。该脚本启动并使用了示例 MinimalHost 的 `backend/samples/MinimalHost/data/admin.db`，后续不再接触该数据库。
- 2026-09-03 T13 静态边界：`git diff --check` 通过；源码扫描未发现 Task 8b 提前写入 `WfOutboxStatus.Dispatching/Dispatched/Failed` 的产品路径。
- 2026-09-03 T14 文档核对：三份文档 diff 合计 `37 insertions / 28 deletions`，`git diff --check` 通过；关键词核对确认生产入口、`WfNodeExecutionJob`、`MaxAttempts`、48032、at-least-once、`ExecutionKey`、Task 8c 均有当前语义，旧的未交付入口表述只保留在历史基线说明中。
- 2026-09-03 T15 独立 review：架构 lane `01a0677b-be0e-7800-be30-ba2fd25e3669` 返回 **BLOCK**；确认生产链路结构正确，但发现 P1 poison-execution livelock（`WfNodeExecutionJob.cs:46-72` + dispatcher context load `:124-150`）、P2 scheduler/JobExecutor 真实触发链未测试、P2 SQL Server filter 未纳入 Task 8b 新测试。代码-reviewer lane 经两轮观察超时后无报告并关闭；上述发现由本地源/测试再次核对，已纳入 T16 修复清单。

## Round log

### Round 0 — 初始化 Task 8b 台账

- 创建目标、完成条件、固定决策、范围边界、安全规则和 T1–T19 任务清单。
- 尚未修改产品代码、测试或 `docs/workflow`。

### Round 1 — T1 基线与计划

- 已执行 Guard：`dev` HEAD `961cf8d`，与 `origin/dev` `0/0` 分歧；受跟踪文件无改动。保留未跟踪 `.loop/wf-task8b.md` 与受保护 `backend/tests/TenonAdmin.Tests/TestResults/`，未删除、清理或改写后者。
- 已核对 M3a-1 当前实现：`WfNodeExecutionStore`/`WfNodeExecutionDispatcher`/attempt/outbox 仍是可复用内核；`EnterNodeOp` 当前只处理 start/approval/cc/branch；`WfDefinitionService.ValidateNode` 当前仍拒绝 Webhook；`WorkflowSetup` 当前已有 handler 和超时 job，但没有 execution dispatcher/job 的生产注册。
- 已核对后台范式：`IAdminJob` 由 `JobExecutor` 在独立 scope 解析，`JobSchedulerService` 通过 `sys_job` Ready 行做选主、调度和触发 CAS；`WfTimeoutJob`/`WfTimeoutJobSeed` 是应复用的同包范式。
- 已核对测试与文档：现有 dispatcher 主要用手工 execution 行证明 lease/fence/attempt/outbox；`WorkflowAppFactory` 默认关闭真实 scheduler 防止集成测试互相干扰；`docs/workflow` 权威文档明确记录了当前生产入口、worker、MaxAttempts 来源和未知异常活锁缺口。
- 基线验证：`dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow" --no-restore` → **417 passed / 0 failed / 0 skipped**，耗时约 1m31s；`git diff --check` 通过；没有 Release build 或四库新证据，本轮不将基线扩大表述为 Task 8b 证据。
- 本轮只修改本台账，未修改产品代码、测试、CI 或 `docs/workflow`。T1 已勾选。
- NEXT：T2 入口红测；只添加能证明原子创建/复用、回滚无残留和入口不调用 handler 的失败测试，红测通过后停止。

### Round 2 — T2 入口红测

- 已执行 Guard：状态仍为 `ACTIVE`、round `1 / 40`，HEAD 未变化；保留台账和受保护 `TestResults` 未跟踪项，未执行删除/清理/提交/推送。
- 新增测试文件 `backend/tests/TenonAdmin.Tests/WfNodeExecutionEntryTests.cs`，只覆盖 T2：真实引擎 `start → webhook` 应创建单条 Pending execution；固定 `NodeVisitId` 的重复入口应复用同一 execution；入口完成后强制事务回滚不得留下 execution、history 或 token 改动；预注册 handler 的调用计数保持 0。
- 红测结果：`WfNodeExecutionEntryTests` **2 failed / 1 passed / 0 skipped**。失败栈均落到 `EnterNodeOp.ExecuteAsync` 的现有 48008 unsupported 分支（真实生产缺口），不是编译错误；回滚测试通过，说明测试脚手架和事务断言可运行。
- 本轮只修改测试文件和本台账，未修改产品代码、CI 或 `docs/workflow`。T2 已勾选。
- NEXT：T3 入口实现；只改 `EnterNodeOp` 及必要的 Webhook 发布校验/装配，先让 T2 红测转绿，再停止。

### Round 3 — T3 入口实现

- 已执行 Guard：状态为 `ACTIVE`、round `2 / 40`，HEAD 仍为 `961cf8d`；当前既有台账、T2 测试和受保护 `TestResults` 均保留，未提交、未推送。
- 修改 `backend/src/TenonAdmin.Workflow/Engine/Operations/EnterNodeOp.cs`：新增 Webhook 分支和 `EnterWebhookAsync`。该方法使用 `StarterOrgId` 的 invariant string + `WfIdentityHash.NormalizeScopeKey`，按既有六维规则计算 `WfExecutionKey`，以当前 instance/token/visit/node/version 建立 `WfNodeExecution`，通过 `EnsureAsync` 幂等写入；不调用 handler、不发 HTTP、不规划后继操作。
- 修改 `backend/src/TenonAdmin.Workflow/Services/WfDefinitionService.cs`：仅把 `WfNodeType.Webhook` 加入既有发布类型白名单，其他模型校验不变。
- 恢复态验证：入口与人工相关目标集 **15/15** 全绿（含 `WfNodeExecutionEntryTests` 3 条、`WfNodeVisitIdTests`、`WfBranchPublishValidationTests`、`LeaveWorkflowE2ETests`）。
- 承重 mutation：去除 `EnsureAsync` 后入口测试的两条 execution 断言均精确转红；恢复后同一目标集 15/15。该临时改动未保留。
- 本轮只修改两个产品源文件和台账；没有修改测试文件、CI、文档或受保护目录。T3 已勾选。
- NEXT：T4 重试预算红测；只补 `MaxAttempts` 来源/范围/快照的失败测试，先确认缺口可转红。

### Round 4 — T4 重试预算红测

- 已执行 Guard：状态为 `ACTIVE`、round `3 / 40`，HEAD 仍为 `961cf8d`；既有产品改动、T2 测试、台账和受保护 `TestResults` 均保留，未提交、未推送。
- 新增测试文件 `backend/tests/TenonAdmin.Tests/WfNodeExecutionRetryPolicyTests.cs`，只覆盖 T4：内置默认 3、全局配置、节点 JSON 覆盖、创建时快照、全局 `[1,100]` 边界、全局非法配置和节点非法配置回滚。
- 为保持行为测试先于产品接口，当前不存在的 `WorkflowOptions.MaxAttempts` 和 `WfNodeProps.MaxAttempts` 通过反射/原始 JSON 先锁定；T5 实现后这些测试必须改为真实配置路径并全部通过。
- 红测结果：**9 条测试中 7 failed / 2 passed / 0 skipped**。7 条失败均为预期产品缺口：默认 execution `MaxAttempts` 为 0、全局属性不存在、全局非法值不抛、节点非法值不阻断；两个合法边界值当前没有失败。
- 本轮只修改 T4 测试文件和本台账，未修改产品代码、CI 或 `docs/workflow`。T4 已勾选。
- NEXT：T5 重试预算实现；新增全局/节点配置与校验，写入 execution 快照，先让 `WfNodeExecutionRetryPolicyTests` 全部转绿。

### Round 5 — T5 重试预算实现

- 已执行 Guard：状态为 `ACTIVE`、round `4 / 40`，HEAD 仍为 `961cf8d`；保留此前产品/测试/台账改动和受保护 `TestResults`，未提交、未推送。
- 修改 `backend/src/TenonAdmin.Workflow/Abstractions/WorkflowOptions.cs`：加入公开 `MaxAttempts` 全局配置，默认 3；声明 `[1,100]` 合法范围；在 `WorkflowSetup.AddTenonAdminWorkflow` 绑定后调用内部校验，非法 `TenonAdmin:Workflow:MaxAttempts` 直接抛 `InvalidOperationException`。
- 修改 `backend/src/TenonAdmin.Workflow/Schema/WfNode.cs`：加入可空 `WfNodeProps.MaxAttempts`，JSON 形状为 `props.maxAttempts`，只对 Webhook 自动节点使用，空值继承全局配置。
- 修改 `backend/src/TenonAdmin.Workflow/Services/WfDefinitionService.cs`：Webhook 发布校验拒绝节点级越界值；修改 `EnterNodeOp.cs`：入口在 execution insert 前解析节点优先/全局回退并校验，写入 `WfNodeExecution.MaxAttempts` 快照。`WfNodeExecution` 注释同步为“最大执行次数（含首次）”。
- T4 专项 9/9、原样 workflow 过滤集 429/429、入口/预算/人工联合目标集 24/24 全绿；没有新增依赖，没有修改 dispatcher、execution store 或人工执行链。
- 承重 mutation 已完成并精确复原：忽略节点覆盖后 3 条预算测试转红，恢复后 24/24；临时产品改动未保留。
- 本轮修改预算相关产品文件和既有 T4 测试依赖的配置契约，未修改 CI、`docs/workflow` 或受保护目录。T5 已勾选。
- NEXT：T6 worker 红测；只添加 worker 扫描状态、批量、取消、隔离和重复扫描失败测试，先确认生产 worker 缺口可转红。

### Round 6 — T6 worker 红测

- 已执行 Guard：状态为 `ACTIVE`、round `5 / 40`，HEAD 仍为 `961cf8d`；此前产品/测试/台账改动和受保护 `TestResults` 均保留，未提交、未推送。
- 新增 `backend/tests/TenonAdmin.Tests/WfNodeExecutionWorkerTests.cs`，通过 `IAdminJob` 解析预期的 `TenonAdmin.Workflow.WfNodeExecutionJob`，锁定五组行为：`Pending`/到期 `RetryScheduled`/过期 `Running` 可派发，未来 retry 跳过；批量上限；外部取消传播；一项 dispatcher 失败不拖垮下一项；终态重复扫描不再调用 handler。
- 测试使用独立真实 workflow 表行和前置 fake handler，候选状态由数据库行控制，避免把 worker 行为退化成内存列表测试。批量配置名也由测试锁定为 `WorkflowOptions.NodeExecutionScanBatchSize`，后续 T7 必须按此契约实现。
- 红测结果：**5 failed / 0 passed / 0 skipped**。所有失败集中在当前缺口（`WfNodeExecutionJob` 类型/`IAdminJob` 注册缺失，以及批量配置属性缺失），无编译错误或基础设施错误。
- 本轮只修改 T6 测试文件和台账，未修改产品代码、CI、文档或受保护目录。T6 已勾选；红测阶段没有可安全 mutation 的生产 worker 实现，worker 错误实现的 mutation 留到 T7 绿测后立即执行。
- NEXT：T7 worker 实现；只新增 `WfNodeExecutionJob`、对应 `WorkflowOptions` 扫描/lease 配置、DI 注册和固定 sys_job seed，复用现有 `IAdminJob`/`JobExecutor`/`JobSchedulerService`，然后验证 T6。

### Round 7 — T7 worker 实现

- 已执行 Guard：状态为 `ACTIVE`、round `6 / 40`，HEAD 仍为 `961cf8d`；保留此前所有产品/测试/台账改动和受保护 `TestResults`，未提交、未推送。
- 修改 `backend/src/TenonAdmin.Workflow/Abstractions/WorkflowOptions.cs`：加入 `NodeExecutionScanBatchSize`（默认 20，最大 1000）和 `NodeExecutionLeaseSeconds`（默认 300，最大 3600），并纳入 Workflow 绑定校验。
- 新增 `backend/src/TenonAdmin.Workflow/Jobs/WfNodeExecutionJob.cs`：实现 `IAdminJob`，用 `JobExecutor.InstanceId` 作为短租约 owner；按应用 UTC 时间扫描 `Pending`、到期 `RetryScheduled`、过期 `Running`，按 Id 和批量上限逐项调用现有 `WfNodeExecutionDispatcher.RunAsync`。单项非取消异常只记录结构化日志/安全摘要并继续，取消直接传播。
- 新增 `backend/src/TenonAdmin.Workflow/Jobs/WfNodeExecutionJobSeed.cs`：固定编码 `wf-node-execution-scan`，`Interval=5s`、`SerialSkip`、`Ready`、`TimeoutSeconds=0`，不覆盖用户后续调整的运行态任务配置。
- 修改 `backend/src/TenonAdmin.Workflow/WorkflowSetup.cs`：注册 scoped dispatcher、worker `IAdminJob` 和 worker seed；不新增第二套 worker fleet，不改变已有 `JobSchedulerService`。
- 修正 `backend/tests/TenonAdmin.Tests/WfNodeExecutionWorkerTests.cs` 的状态查询为顺序读取，避免共享 SqlSugarScope 并发 reader 干扰；补充 seed 行契约测试。
- 验证结果：worker **6/6**、联合目标集 **98/98**、原样 workflow 过滤集 **435/435**；新 worker XML 注释无新增编译警告。
- 承重 mutation 已精确复原：过期 Running 谓词反转后 1 条测试转红，恢复后 worker 6/6。
- 本轮未修改 dispatcher、引擎回写状态机、人工节点或 outbox 消费逻辑；未修改 CI、`docs/workflow` 或受保护目录。T7 已勾选。
- NEXT：T8 未知异常红测；只添加未知非取消 handler 异常应有限收敛、记录安全审计并保持外部 cancellation 原样传播的测试。

### Round 8 — T8 未知异常红测

- 已执行 Guard：状态为 `ACTIVE`、round `7 / 40`，HEAD 仍为 `961cf8d`；保留此前所有产品/测试/台账改动和受保护 `TestResults`，未提交、未推送。
- 新增 `backend/tests/TenonAdmin.Tests/WfNodeExecutionExceptionTests.cs`，通过真实 `WfNodeExecutionJob` + 前置 fake handler 复现未知 `InvalidOperationException` 和外部 `OperationCanceledException`。
- 未知异常测试要求：第一次失败转 `RetryScheduled`、落一条 `RetryableFailure` attempt/48032/安全摘要；第二次耗尽 `MaxAttempts=2` 转 `Failed`；第三次扫描不再调用 handler；异常正文 `handler-secret-body` 不得进入 execution 或 attempt 摘要。
- 取消测试要求：外部取消原样抛出；execution 保持 `Running`、领取次数增加但 attempt/outbox 均为 0，证明取消不是伪业务结果。
- 红测结果：**1 failed / 1 passed / 0 skipped**。未知异常当前由 worker 隔离后 execution 仍为 `Running`，正是 T9 需要修复的真实缺口；取消路径现状通过。
- 本轮只修改 T8 测试文件和本台账，未修改 dispatcher 产品代码、CI、文档或受保护目录。T8 已勾选；异常实现 mutation 留到 T9 绿测后执行。
- NEXT：T9 未知异常实现；只在 dispatcher 的 handler 调用边界捕获非 OCE 未知异常，生成 48032 retryable 结果并安全记录，然后复跑 T8。

### Round 9 — T9 未知异常实现

- 已执行 Guard：状态为 `ACTIVE`、round `8 / 40`，HEAD 仍为 `961cf8d`；保留此前所有产品/测试/台账改动和受保护 `TestResults`，未提交、未推送。
- 修改 `backend/src/TenonAdmin.Workflow/Abstractions/WorkflowErrorCode.cs`：新增 `NodeHandlerUnhandled = 48032`。
- 修改 `backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionDispatcher.cs`：为保持原四参数源码兼容，新增可选 logger 参数；`InvokeHandlerAsync` 只捕获非 `OperationCanceledException` 的未知 handler 异常，记录结构化异常后返回 48032 retryable 结果，摘要不含异常正文并经 512 截断；OCE 继续原样传播。同步修正该类中过时的“零 DI/任何异常不 catch”注释。
- 修改 `backend/tests/TenonAdmin.Tests/WfNodeExecutionDispatcherTests.cs`：将旧的“未知异常 Running/无 attempt”期望更新为新的受控 retryable attempt 语义；保留 OCE 作为 lease 恢复/取消语义测试，避免丢失崩溃恢复覆盖。
- T8 异常测试、dispatcher 和 Webhook 联合目标集 **84/84** 全绿；无新增 xUnit 或编译警告。
- 承重 mutation 已精确复原：错误地改成 `TerminalFailure` 后有限重试测试转红，恢复为 `RetryableFailure` 后 84/84。
- 本轮未修改 worker 扫描、引擎 fence/CAS、attempt/outbox 状态机或已分类 Webhook 错误语义；未修改 CI、`docs/workflow` 或受保护目录。T9 已勾选。
- NEXT：T10 并发与恢复测试；只补/收紧旧 owner、重复扫描、handler 返回前崩溃、提交前崩溃和 Token 单推进验证，先不改产品代码。

### Round 10 — T10 并发与恢复测试

- 已执行 Guard：状态为 `ACTIVE`、round `9 / 40`，HEAD 仍为 `961cf8d`；保留此前所有产品/测试/台账改动和受保护 `TestResults`，未提交、未推送。
- 新增 `backend/tests/TenonAdmin.Tests/WfNodeExecutionRecoveryTests.cs`：第一条通过真实 `WfNodeExecutionJob` 复现 handler 返回前 OCE，中断后 execution 保持 Running/无 attempt，过期 lease 后新次尝试成功并写入 attempt 2；第二条注入只失败一次的 tx2 engine，复现外部副作用已经发生但结果提交前崩溃，随后新 owner 重跑。
- 恢复断言覆盖：`AttemptCount` 1→2、只保留成功返回的 attempt 2、终态 outbox 仅一条、Token 进入 Completed、实例只进入 Approved 一次、`InstanceCompleted` history 只一条；外部副作用调用次数允许 2 次，但 `ExecutionKey` 去重后的实际生效键只一条。
- T10 恢复测试 **2/2**，与全部 `WfNodeExecutionDispatcherTests` 联合 **27/27** 全绿；无新增测试警告。
- 并发承重 mutation 已精确复原：临时移除终态 tx2 的 `Fence == fence` 后 stale-owner 测试在“应抛 48004”处转红，恢复后联合集 27/27。
- 本轮只新增/修改测试和临时 mutation；未保留产品 mutation，未改业务产品代码、CI、`docs/workflow` 或受保护目录。T10 已勾选。
- NEXT：T11 完整 Webhook E2E；只补生产入口到 worker 的真实 Webhook 成功/重试/terminal/manual fallback、事务外外呼和恢复集成测试。

### Round 11 — T11 完整 Webhook E2E

- 已执行 Guard：状态为 `ACTIVE`、round `10 / 40`，HEAD 仍为 `961cf8d`；保留此前所有产品/测试/台账改动和受保护 `TestResults`，未提交、未推送。
- 新增 `backend/tests/TenonAdmin.Tests/WfNodeExecutionProductionE2ETests.cs`，测试真实服务链 `IWfDefinitionService.AddAsync → PublishAsync → IWfInstanceService.StartAsync → EnterNodeOp → WfNodeExecutionJob → WebhookNodeHandler → dispatcher`。HTTP 传输层仅替换为 fake `HttpMessageHandler`，不访问外部网络。
- 五条 E2E 覆盖：成功推进且 Token/实例只完成一次；503 retry 后 200 成功；404 terminal 停在 Failed 且不建人工任务；404 + `WebhookOnFailure=Manual` 在原节点建人工任务；Webhook 外呼后 tx2 提交前崩溃、lease 过期后新 owner 恢复。
- 成功用例在 fake transport 的 `SendAsync` 内检查 `db.Ado.IsAnyTran()` 与 `db.Ado.Transaction`，两者均为无活动事务；恢复用例证明外呼次数可达 2（at-least-once），本地 attempt/outbox/Token/实例终态保持单次推进。
- T11 E2E **5/5** 全绿；没有新增 xUnit 或编译警告。
- 承重 mutation 已精确复原：临时让 worker 跳过 dispatcher 后 T11 E2E **5 failed / 0 passed**，恢复 dispatcher 调用后 **5/5**。
- 本轮只新增完整 E2E 测试和台账，临时产品 mutation 未保留；未修改 outbox 消费逻辑、CI、`docs/workflow` 或受保护目录。T11 已勾选。
- NEXT：T12 outbox 边界；只补终态 `Pending` 幂等入队契约/文档边界，不实现 `Dispatching/Dispatched/Failed` 消费闭环。

### Round 12 — T12 outbox 边界

- 已执行 Guard：状态为 `ACTIVE`、round `11 / 40`，HEAD 仍为 `961cf8d`；保留此前产品/测试/台账改动和受保护 `TestResults`，未提交、未推送。
- 修改 `backend/tests/TenonAdmin.Tests/WfOutboxTests.cs`：新增反射契约，锁定 Task 8b 的 `WfOutboxStore` 对外只暴露 `EnqueueAsync`，不提前交付 consumer claim/dispatch 状态机。
- 修改 `docs/workflow/workflow-database-design-review-2026-08-24.md` 与 `docs/workflow/elsa3-slickflow-ai-reference-2026-08-23.md`：明确终态 outbox 只在 tx2 内幂等写入 `Pending`，`Dispatching/Dispatched/Failed` 的领取、重投、回写和 CAS 延期到 Task 8c；Task 8b execution worker 不消费 outbox。
- outbox 专项 **10/10** 全绿；边界 mutation 将初始状态改为 `Dispatched` 后 **1 failed / 9 passed**，失败精确落在初始状态断言，恢复后 10/10。临时产品 mutation 未保留。
- 本轮未新增依赖，未实现 Task 8c 消费闭环，未触碰受保护目录；T12 已勾选。
- NEXT：T13 本地全量验证；运行目标测试、原样 workflow filter、Release build 和适用静态检查，诚实记录 SQLite/四库证据边界。

### Round 13 — T13 本地全量验证

- 已执行 Guard：状态为 `ACTIVE`、round `12 / 40`，HEAD 仍为 `961cf8d`；此前产品/测试/文档/schema/台账改动和受保护 `TestResults` 均保留，未提交、未推送。
- 后端 Release build 通过 **0 errors / 13 existing warnings**；Task 8b 目标类 Release 测试 **119/119**；原样 workflow filter **445/445**。没有发现本任务引入的后端失败。
- 由于 `WfNodeProps.MaxAttempts` 是公开工作流模型字段，运行 `node scripts/check-contract-drift.mjs` 做真实 OpenAPI 生成。脚本首轮因 schema 过期退出 1，但输出 diff 精确显示两份 schema 只需同步增加 `maxAttempts`；生成后两份文件内容对应一致、SHA-256 一致。脚本已无 5101 监听进程；因它接触了示例 `data/admin.db`，不再重复执行。
- Vue typecheck/lint/test **127/127**、build 成功；React typecheck/lint/test **808/808**、build 成功。前端 build 的 Rollup 注释/大 chunk 提示均为既有依赖/体积提示，未形成错误。
- `git diff --check` 通过；outbox 产品源码仍只有 Task 8b 终态 `Pending` 入队，没有提前实现 Task 8c consumer 状态变更。
- 本轮完成了 T13 要求的后端、前端、schema 和静态验收；没有修复项需要追加。T13 已勾选。
- NEXT：T14 文档回写；只更新三份 `docs/workflow` 权威文档中的生产入口、worker、MaxAttempts、未知异常、at-least-once、下游幂等和 Task 8c 延期语义。

### Round 14 — T14 文档回写

- 已执行 Guard：状态为 `ACTIVE`、round `13 / 40`，HEAD 仍为 `961cf8d`；此前产品/测试/schema/文档/台账改动和受保护 `TestResults` 均保留，未提交、未推送。
- 按 `technical-writing` skill 的开发者/运维受众要求，将最终语义写回三份权威文档：`workflow-design-plan-2026-08-17.md` 新增 Task 8b 生产闭环、配置优先级和 at-least-once 说明；`workflow-database-design-review-2026-08-24.md` 更新兼容性、字段预算、worker、E2E 和四库证据边界；`elsa3-slickflow-ai-reference-2026-08-23.md` 更新生产入口、异常收敛、attempt 口径、outbox 延期和历史 CI 说明。
- 文档明确记录：`EnterNodeOp` 同事务创建/复用 Pending execution；`WfNodeExecutionJob` 复用 `IAdminJob`；`MaxAttempts` 节点 → 全局 → 默认 3、范围 `[1,100]` 且创建时快照；未知非取消异常为 `RetryableFailure/48032`，OCE 原样传播；外部副作用 at-least-once、下游用 `ExecutionKey` 幂等；Task 8b 仅 Pending outbox，Task 8c 才消费。
- 文档核对 diff 合计 `37 insertions / 28 deletions`，`git diff --check` 通过；关键词/旧表述核对无未解释的当前事实冲突。未改代码、测试、CI 或受保护目录。
- T14 已勾选。
- NEXT：T15 独立 review；对代码与文档做独立 P1/P2 审查，重点覆盖四库 SQL/扫描、旧 owner、异常/取消、MaxAttempts 快照、兼容性和文档真实性。

### Round 15 — T15 独立 review

- 已执行 Guard：状态为 `ACTIVE`、round `14 / 40`，HEAD 仍为 `961cf8d`；此前产品/测试/schema/文档/台账改动和受保护 `TestResults` 均保留，未提交、未推送。
- 按 `code-review` skill 先完成本地 review，再启动独立 `architect` 与 `code-reviewer` lane。architect lane 返回 **BLOCK**；code-reviewer lane 在两轮等待后无终态报告，已关闭，未写工作区。
- 已确认的 P1：`WfNodeExecutionJob` 在 `dispatcher.RunAsync` 的所有非取消异常处只记录日志并继续，dispatcher 在 tx1 之后的 instance/token/version/model 快照读取失败会使 execution 保持 `Running`；lease 过期后再次进入扫描，永久坏数据可无限消耗执行/数据库资源。这不同于已由 T9 收敛的未知 handler 异常。
- 已确认的 P2：①当前生产 E2E/worker 测试直接解析 `IAdminJob` 并调用 `ExecuteAsync`，`WorkflowAppFactory` 关闭真实 scheduler，尚无 `JobSchedulerService → JobExecutor → DefaultJobHandlerResolver → WfNodeExecutionJob` 的运行时贯通测试；② `.github/workflows/backend-ci.yml` 的 SqlServer PR filter 仍只列旧 `WfNodeExecutionContractTests`，未列 Task 8b 新增 worker/入口/异常/恢复/生产 E2E 类。
- P3：`WfNodeExecutionWorkerTests`/`WfNodeExecutionRetryPolicyTests` 顶部注释仍有“worker/配置尚未实现”的历史措辞；不阻塞功能但应在修复轮清理。
- Review 结论暂不通过；T15 已勾选但存在上述未关闭 P1/P2，不能进入最终验收。Finding 已写入本台账，后续由 T16 逐项修复并重新验证。
- NEXT：T16 review 修复与复验；为永久 context/data 失败设计受控终态/有限 quarantine 语义，补真实 scheduler integration test，扩展 SQL Server filter，并完成相关 mutation/sabotage 复验。

### Round 16 — T16 review 修复与复验

- 已执行 Guard：状态为 `ACTIVE`、round `15 / 40`，HEAD 仍为 `961cf8d`；保留用户既有工作树改动和受保护未跟踪 `backend/tests/TenonAdmin.Tests/TestResults/`，未执行 reset/clean/commit/push。
- 修复 P1 poison execution livelock：`WfNodeExecutionDispatcher` 只把可证明永久的 instance/token/definition version/model/node 上下文错误（既有 workflow 错误码及 `JsonException`）转换为 `NodeExecutionQuarantinedCmd`；未知的数据库/基础设施异常仍穿透 worker 的单项隔离，保留 lease 到期恢复。引擎在同一 tx2 内按旧 `Fence + Running` CAS 清 lease、置 `Failed`、追加 terminal attempt 和 `Pending` outbox，不读取缺失上下文、不推进 Token。
- 新增 `WfNodeExecutionWorkerTests` 覆盖 missing-instance quarantine、损坏模型 quarantine、重复扫描不再 claim，以及真实 `JobSchedulerService.TickAsync → JobExecutor → DefaultJobHandlerResolver → WfNodeExecutionJob → dispatcher/handler` 运行时触发；`WfNodeExecutionDispatcherTests` 新增 quarantine stale-fence 原子性测试。清理 worker/configuration stale comments，并同步三份 `docs/workflow` 权威文档。
- SQL Server PR `TEST_FILTER` 已纳入 `WfNodeExecutionEntryTests`、`WfNodeExecutionRetryPolicyTests`、`WfNodeExecutionWorkerTests`、`WfNodeExecutionExceptionTests`、`WfNodeExecutionRecoveryTests`、`WfNodeExecutionProductionE2ETests`；nightly 仍保持全套测试。sidecar 对 workflow YAML、filter term、YAML 解析和 whitespace 做了静态验证。
- 承重 mutation/sabotage 均精确转红并恢复：①永久错误不发 quarantine → missing-instance 测试 `Expected Failed / Actual Running`；②移除 quarantine `Fence == cmd.Fence` → stale-fence 测试预期 `AdminException` 但未抛；③移除 `WfNodeExecutionJob` 的 `IAdminJob` 注册 → scheduler log `Failed` 而非 `Success`；④移除 `Pending` 扫描谓词 → 6/8 worker 测试失败且行保持 `Pending`。所有临时 mutation 已恢复，源码扫描无 `[DEBUG-*]` 或 mutation 标记残留。
- 当前恢复态证据：T16 目标专项 **55 passed / 0 failed / 0 skipped**；原样 workflow filter `FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow` **448 passed / 0 failed / 0 skipped**；`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` **0 warnings / 0 errors**；`git diff --check` 无空白错误。未重跑前端/schema，因为 T16 没有 API/公开 schema 形状变化。
- T16 已勾选。四库当前 HEAD CI 尚未运行，本地证据仍主要为 SQLite；下一步进入 T17 最终本地验收，若所有本地门槛保持通过且四库 CI 需要 push、但仍无用户针对 Task 8b 的明确授权，则把状态改为 `WAITING_FOR_PUSH_AUTH`，不得声称 `DONE`。

### Round 17 — T17 最终本地验收

- 已执行 Guard：状态为 `ACTIVE`、round `16 / 40`，HEAD 仍为 `961cf8d`，`dev...origin/dev = 0/0`；保留所有用户/本任务脏改动和 `backend/tests/TenonAdmin.Tests/TestResults/`，未执行 reset/clean/commit/push。
- 后端最终复验：T16 目标专项 **55 passed / 0 failed / 0 skipped**；原样 workflow filter `FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow` **448 passed / 0 failed / 0 skipped**；`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` **0 warnings / 0 errors**。
- 双前端最终复验：`web/` typecheck、lint、Vitest **127/127**、production build 全部通过；`web-react/` typecheck、lint、Vitest **808/808**、production build 全部通过。构建输出仅有既有 Rollup chunk/dynamic-import 提示，无失败；T16 未产生 API 形状变化，因此未重新运行会启动 MinimalHost 并接触示例 `backend/samples/MinimalHost/data/admin.db` 的 contract-drift 脚本。
- 最终静态边界：`.github/workflows/backend-ci.yml` 的 SQL Server PR filter 含 21 个唯一 `FullyQualifiedName~...` term，并纳入 6 个 Task 8b 测试类；`git diff --check` 无空白错误；源码/测试无 `[DEBUG-*]`、临时 mutation、`throw ex` 或 stale worker/config 注释残留。受保护 `TestResults` 仍只保持未跟踪，未被清理或改写。
- T17 已勾选。由于当前 HEAD 尚未取得 SQLite/MySQL/PostgreSQL/SQL Server 远端 CI，且 `push-authorized: false`，按 Done-condition 将状态改为 `WAITING_FOR_PUSH_AUTH`。未授权期间不执行 T18/T19，不把本地 SQLite 与历史四库 run 表述为当前四库已绿。

### Round 18 — T18 提交、推送与四库 CI

- 用户在确认“推送之后等 CI”的流程后回复“继续”，视为对本任务执行 commit、普通 push 和 CI 等待的明确授权；不使用 force-push。
- 已按 Lore 协议创建并普通 push commit `7f44087`：`Close Task 8b automatic-node execution loop with bounded recovery`，包含 27 个 Task 8b 文件；受保护 `backend/tests/TenonAdmin.Tests/TestResults/` 未进入 commit。
- 对应 push 已触发并完成全部 workflow；backend run `33773751150` 的 `build-test (sqlite/mysql/postgres/sqlserver)` 与 `template-smoke` 全部 success；companion runs `33773751061`（web-ci）、`33773751068`（contract-drift）、`33773751166`（docker-smoke）、`33773751089`（web-react-ci）全部 success。远端结果已核对后进入 T19。

### Round 19 — T19 最终收口

- 已执行 Guard：状态为 `ACTIVE`、round `18 / 40`；已核对 `HEAD=7f44087` 已推送，远端 `dev` 与本地提交同步，未执行 force-push、reset 或 clean。
- T1–T19 全部勾选；T15 review 的 P1/P2 已修复并经 T16 mutation/sabotage 验证，文档、CI filter、生产调度链和本地/远端测试证据一致。
- 当前远端 CI 证据：SQLite、MySQL、PostgreSQL、SQL Server、template-smoke、web-ci、web-react-ci、contract-drift、docker-smoke 全部 `success`。SQL Server PR filter 已包含 Task 8b 六个关键测试类；nightly full-suite 规则未被削弱。
- 最终 Git 边界：受跟踪文件工作树干净；唯一保留的未跟踪项是明确受保护的 `backend/tests/TenonAdmin.Tests/TestResults/`，没有被提交、删除或改写。无未关闭 P1/P2，未引入与 Task 8b 无关的改动。
- T18/T19 已勾选，台账状态改为 `DONE`。后续不再修改本任务文件；Task 8c outbox consumer/transport 和 M3a-2 Webhook 设计器 UI 仍按既定边界延期。
