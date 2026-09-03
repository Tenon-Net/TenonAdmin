# TenonAdmin.Workflow 数据库字段设计评审

> 文档入口：[`README.md`](./README.md)
> 日期：2026-09-03（Round 46，post-CI）
> 评审基线：`6bc895e`
> 范围：当前 9 张 `wf_*` 表，以及 M2c、M3a、M3b 对持久化模型的新增要求

## 一、结论

现有 9 表设计适合当前人工审批，定义、版本、实例、Token、活跃任务和历史记录的职责基本清楚，**不需要推倒重来**。它能通过增加字段和新表继续演进，但不能理解为“已经无需迁移地兼容可靠自动节点、并行网关和 AI 审批”。

分阶段判断如下：

| 能力 | 当前兼容性 | 判断 |
| --- | --- | --- |
| M1/M2b 人工审批 | 已兼容 | 现有模型足够，任务级 CAS 能防同一待办双批 |
| M2c 请求幂等与四库终态保护 | 部分兼容 | 需要 operation receipt，并补实例/Token 级并发保护 |
| M3a-1 Webhook/自动节点执行内核 | 已交付并通过最终四库 CI | 节点访问身份、execution、attempt、outbox、lease/fence 和 dispatcher 已落地；生产创建与后台 worker 接线仍待后续任务 |
| M3b AI Decision | 尚未兼容 | 需要独立 AI decision 审计表，不能复用人工意见字段 |
| 循环/并行网关 | 结构可扩展但未到位 | 多 Token 只是起点，尚缺节点访问、fork/join 身份 |

优先级最高的两个地基是：

1. `WfInstance.Version` 和 `WfToken.Version`，用于实例状态与执行指针的乐观并发保护；
2. `NodeVisitId`，用于区分同一个 Token 多次进入同一节点，成为任务、事件和自动执行的稳定关联键。

前者关系到当前审批、撤销、超时和重提的竞争正确性；后者决定后续自动节点、循环、并行和 AI 执行是否会把不同访问误判为同一次执行。

## 二、当前模型

当前冷启动模型为 9 张表：

| 表 | 当前职责 | 基类带来的字段 |
| --- | --- | --- |
| `wf_definition` | 流程定义元数据和当前发布版本 | 审计四件套、软删除、`CreateOrgId` |
| `wf_definition_version` | `Version=0` 草稿及发布后不可变版本快照 | 审计四件套、软删除 |
| `wf_instance` | 一次流程运行及其业务关联、变量和终态 | 审计四件套、软删除、`CreateOrgId` |
| `wf_token` | 实例在流程图上的执行指针 | 审计四件套、软删除 |
| `wf_task` | 当前活跃待办和任务级 CAS | 审计四件套、软删除 |
| `wf_task_actor` | 当前任务的候选/顺序办理人 | 审计四件套、软删除 |
| `wf_his_task` | 人工审批动作、意见和耗时 | 审计四件套、软删除 |
| `wf_history` | append-only 流程事件投影 | 审计四件套、软删除 |
| `wf_cc` | 抄送接收人与已读状态 | 审计四件套、软删除 |

所有表实际都具有：

```text
Id
CreateTime
CreateUserId
UpdateTime
UpdateUserId
IsDelete
```

`wf_definition` 和 `wf_instance` 额外具有 `CreateOrgId`，作为机构数据范围锚点。完整模型起点见 [`workflow-design-plan-2026-08-17.md` §三](./workflow-design-plan-2026-08-17.md#三数据模型9-表冷启动)。

## 三、建议保持不变的设计

### 3.1 定义与版本快照分离

`WfDefinitionVersion.Version=0` 是可编辑草稿；发布时复制为从 1 开始的版本快照，实例固定引用 `DefinitionVersionId`。这个设计保证已发起实例不受后续定义编辑影响，适合数据驱动审批，也不要求部署时保留旧代码版本。

保持以下不变量：

- 一个定义只能有一个 `Version=0` 草稿；
- `(DefinitionId, Version)` 保持唯一；
- `Version>0` 的发布快照只增不改；
- 实例只引用已发布版本。

### 3.2 业务单据状态留在消费方

`WfInstance.BusinessKey` 只关联消费方业务单据，工作流不保存通用 `BusinessStatus`。流程完结后通过 `IWorkflowFormBinder.OnInstanceCompletedAsync()` 通知消费方回写自己的状态。

这是正确的 Module Seam。不同业务的“草稿、待审、已入账、已作废”语义不应污染工作流内核。跨数据库或远程系统需要可靠回写时，应通过 outbox/Adapter 交付，不在 `wf_instance` 增加一个无法统一解释的业务状态字段。

### 3.3 `BusinessKey` 保持非唯一

同一业务单据可能合法地：

- 多次发起；
- 退回后重提；
- 同时进入不同定义；
- 形成主流程和补充流程。

因此不能把 `BusinessKey` 直接设为唯一。需要限制“一张单当前只能有一个运行实例”时，由消费方规则或包含定义、状态和业务范围的专用约束实现。

### 3.4 JSON 快照继续保留

`ModelJson`、`FormSchema` 和 `VariablesJson` 适合当前卫星包与四数据库目标。它们避免把流程变量扩张成通用表单/规则平台，也让发布版本能够完整自包含。

后续 AI 审计中的 proposal、证据和模型输出应进入独立表，不应继续堆入 `VariablesJson`。

### 3.5 人工任务与机器执行分开

`wf_task` 表达“等人处理”的 durable inbox。Webhook、AI、脚本和其他自动节点应使用独立 execution/attempt 模型。把机器执行状态塞入 `wf_task` 会让重试、lease、fence 和人工待办状态互相污染。

## 四、当前字段与约束问题

### 4.1 实例级并发保护不足

当前只有 `WfTask.Version`。同一任务上的同意、拒绝、转办或退回会先执行任务级 CAS，能够保证只有一个动作推进。

但以下竞争不都由同一个任务版本覆盖：

- 审批与撤销；
- 审批与超时动作；
- 超时 worker 与人工操作；
- 终态写入与重提；
- 未来自动节点结果与取消；
- 多 Token 对同一实例终态的竞争。

建议增加：

```text
wf_instance.Version int not null default 0
wf_token.Version    int not null default 0
```

状态推进统一采用期望状态和版本双重条件：

```text
WHERE Id = @id
  AND Status = @expectedStatus
  AND Version = @oldVersion
```

成功时 `Version = Version + 1`。两个字段在 M2b 收口时即落地（旧行回填 0，见 §十），M2b 刚交付的撤销、催办与超时正是这些竞争的高发区，竞争测试应直接建立在实例/Token 级 CAS 上；四库契约测试随 M2c 收口，而不是等到 M3 自动节点再补。

### 4.2 缺少明确完结时间

当前可直接取得：

- 发起时间：`wf_instance.CreateTime`；
- 人工动作时间：`wf_his_task.CreateTime`；
- 待办创建时间：`wf_task.CreateTime`；
- 到期时间：`wf_task.DueTime`。

实例没有 `CompletedTime`。现在只能从 `wf_history` 的 `InstanceCompleted` 事件推导，`UpdateTime` 也不能安全等同于完结时间，因为重提、修复或其他更新同样会刷新它。

建议增加：

```text
wf_instance.CompletedTime datetime nullable
```

首次进入 `Approved/Rejected/Cancelled/Terminated` 时与状态原子写入；终态保护保证它只确定一次。

### 4.3 办理耗时对转办和顺序会签不准确

当前 `WfHisTask.DurationMs` 的计算基准是 `WfTask.CreateTime`。这表示整张任务从创建到动作发生的时间，不一定是当前办理人的实际处理时间：

- 转办后的办理人会继承转办前的等待时间；
- 顺序会签后手会包含所有前手的处理时间；
- Waiting 状态等待轮到自己的时间与真正办理时间无法区分。

建议补充：

```text
wf_task_actor.AssignedTime   datetime
wf_task_actor.ActivatedTime  datetime nullable
wf_his_task.StartedTime      datetime nullable
```

语义写死：

- `AssignedTime`：成为候选办理人的时间；
- `ActivatedTime`：进入 Pending、用户真正可以处理的时间；
- `wf_his_task.CreateTime`：动作发生时间；
- `DurationMs`：`CreateTime - ActivatedTime`。

旧数据的 `StartedTime` 可保持空，避免伪造精度。

> **已实现（2026-09-01，M2c → M3a 过渡步骤）**：`wf_task_actor.ActivatedTime`、`wf_his_task.StartedTime` 均已落地（nullable，无 `DefaultValue`，升级策略同 `WfInstance.CompletedTime`）。**未新增 `AssignedTime` 列**——`wf_task_actor` 继承 `BaseEntity`，其审计字段 `CreateTime` 就是「成为候选办理人的时间」，与建议里的 `AssignedTime` 语义完全重合，建两个列是重复。`ActivatedTime` 在两处写入：①或签/会签全员与顺序首位建任务时立刻写（与 `CreateTime` 同一时刻）；②顺序会签的后位在 `CompleteTaskOp.TryPassAsync` 晋级为 Pending 那一刻写。`DurationMs` 改用 `now - (ActivatedTime ?? WfTask.CreateTime)`（`??` 兜底覆盖升级前已是 Pending 的旧行，永远读 `null`，优雅退化回旧公式，不需要回填）。落点：`CompleteTaskOp`/`ReturnTaskOp`（读值不改变既有 CAS 判定条件，只多一次快照读）、`ReassignTaskOpBase`（转办/委托新建的目标 actor 行同样立刻写 `ActivatedTime`）。测试：`WfTaskAssignmentHistoryTests.cs`。

### 4.4 办理人分配历史会丢失

任务关闭时，当前实现物理删除全部 `wf_task_actor`，`wf_his_task` 只记录真正执行动作的人。因此会丢失：

- 最初候选审批人；
- 或签中未行动的候选人；
- 顺序会签尚未轮到的人；
- 转办前后的完整分配链；
- 系统 fallback 到人工时最初分配给谁。

这会影响 SLA、责任追踪、流程诊断和 AI 人工接管率分析。

实现时二选一：

1. 保留 `wf_task_actor`，任务关闭后只更新 `Done/Skipped`，不物理删除；
2. 继续保持活跃表精简，但新增 append-only 的 `wf_task_assignment_history`。

若采用第二种，至少记录：

```text
TaskId
InstanceId
TokenId
NodeVisitId
UserId
ActorType
AssignmentAction
FromUserId
Sort
AssignedTime
ActivatedTime
EndedTime
EndReason
```

最终选择应在实现 M2c/M3a 前写回设计规划，避免前端、审计和统计分别猜测分配历史来源。

> **已实现（2026-09-01，M2c → M3a 过渡步骤）**：选了**方案一**——保留 `wf_task_actor`，任务关闭只把状态翻终态（`Done`/`Skipped`），不再物理删；不新增 `wf_task_assignment_history` 表。
> **决策依据**：全部读路径已逐一核对，`WfTaskService.PageTodoAsync`、`WfInstanceService`（3 处）、`WorkflowEngine.ResolvePendingActorsAsync`、`WfTimeoutJob` 无一例外都显式过滤 `Status == Pending`，没有任何地方靠「这行还在不在」判断是否活跃；保留下来对现有查询零风险，而新建一张表要多一套实体/仓储/可替换性面，对已经存在的信息纯属复制。
> **一并修的一个缺口**：`CompleteTaskOp.CloseTaskAsync` 原来关闭时只把 `Pending` 翻 `Skipped`，`Waiting`（顺序会签尚未轮到的候选人）被落下——物理删除年代这不要紧（反正整行都没了），但保留之后 `Waiting` 行会永远卡在「尚未轮到」，不给终态。已改成 `Pending`/`Waiting` 都翻 `Skipped`。`CancelInstanceOp`/`ReturnTaskOp` 原本就是无条件翻全部 actor（不限 `Pending`），不用改。
> **`wf_task` 本身仍然物理删**——它承担的是另一个职责（改派/超时等路径依赖的隐式不变量「终态动作必删活跃 `wf_task`」，见 `ReassignTaskOpBase` 源码注释），与本表的历史留存无关。
> 落点：`CompleteTaskOp`/`ReturnTaskOp`/`CancelInstanceOp`（删除 `Deleteable<WfTaskActor>()` 调用）。测试：`WfTaskAssignmentHistoryTests.cs`。

### 4.5 Token 节点访问身份已落地（M3a-1）

`WfToken` 现在保留 `InstanceId/NodeId/Status/Version/NodeVisitId`。`Version` 继续负责 token 级乐观锁；`NodeVisitId` 是一次节点访问的雪花 Id，不与版本号混用。`EnterNodeOp.ExecuteAsync` 在同一条更新中领取 token、生成新的 `NodeVisitId` 并写入 `NodeId`；停留期间的会签、转办和催办只推进 `Version`，不刷新访问 Id（[`WfToken.cs`](../../backend/src/TenonAdmin.Workflow/Entities/WfToken.cs):25-51、[`EnterNodeOp.cs`](../../backend/src/TenonAdmin.Workflow/Engine/Operations/EnterNodeOp.cs):27-47）。

以下记录在创建时复制该访问 Id：

```text
wf_task.NodeVisitId
wf_his_task.NodeVisitId
wf_history.NodeVisitId
wf_cc.NodeVisitId
wf_node_execution.NodeVisitId
```

`WfNodeExecution.NodeVisitId` 目前可空，以兼容旧行；`ExecutionKey` 对缺失值使用哨兵。并行网关仍未实现，未来再根据真实 fork/join 语义增加 `ParentTokenId/ForkId` 或独立 join 表。

### 4.6 `wf_history` 关联、顺序与载荷版本已落地（M3a-1）

`WfHistory` 当前字段为：

```text
InstanceId
EventType
NodeId             nullable
RequestId          nullable
PayloadJson        nullable
TokenId            nullable
NodeVisitId        nullable
Sequence           int not null
ActorType
ActorUserId        nullable
PayloadVersion     int not null
```

`Sequence` 由 `WfHistorySequence.NextAsync` 在同一事务内推进 `wf_instance.HistorySeq` 后分配，从 1 起；升级前的存量行读到 0。`ActorType` 的旧行也读到 `0 (Unknown)`。当前实体只有实例和事件两个普通索引，没有额外的 `UNIQUE(InstanceId, Sequence)`；顺序唯一性由同事务内的计数器更新保证（[`WfHistory.cs`](../../backend/src/TenonAdmin.Workflow/Entities/WfHistory.cs):10-87、[`WfHistorySequence.cs`](../../backend/src/TenonAdmin.Workflow/Engine/WfHistorySequence.cs):17-45）。

`PayloadVersion` 的语义要特别区分两个默认值：本次提交之后首次加列的迁移使用 `SugarColumn(DefaultValue = "0")`，所以该次迁移产生的 legacy 行读为 `0`；如果某个环境已经在父版本中加过这列，则保留数据库现有值，不用本规则重写。新建 `WfHistory` 实体使用 CLR initializer `= 1`。读取方必须按 `EventType + PayloadVersion` 解释，并同时接受既有值与 new `1`；任何环境都不能重新运行或覆盖 append-only 旧 history。

### 4.7 append-only 语义已由写入面收口（M3a-1）

`WfHistory` 和 `WfNodeExecutionAttempt` 仍继承 `BaseEntity`，因此保留审计列，但正常写入面只追加：历史由 `WfExecutionContext.AppendHistoryAsync`/`WfHistorySequence` 写入，attempt 只暴露 `WfNodeExecutionAttemptStore.AppendAsync`，没有通用更新/删除路径。attempt 的唯一约束是 `(ExecutionId, AttemptNo)`，重试新增行，不覆盖旧行；输出正文不进 attempt，只保留输出 hash 与最多 512 字符摘要（[`WfNodeExecutionAttempt.cs`](../../backend/src/TenonAdmin.Workflow/Entities/WfNodeExecutionAttempt.cs):38-76、[`WfNodeExecutionAttemptStore.cs`](../../backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionAttemptStore.cs):27-74）。

`WfNodeExecution` 是可更新的执行状态表；`WfOutbox` 也是可更新的投递状态机，不应与 append-only 事实表混为一谈。两者都不把 `IsDelete` 当业务状态，保留期清理应走明确策略，而不是普通软删除。

## 五、M2c：operation receipt

M2c 新增 `wf_operation_receipt`，解决“第一次事务已成功，但 HTTP 响应丢失，客户端重试只能得到 TaskConflict”的问题。

推荐字段：

```text
Id
ScopeKey          非空；无机构用户也归一化为稳定值
CommandType
TargetType
TargetId
ActorUserId
RequestKey
IdentityHash
ResultCode
ResultJson
CreateTime
```

唯一 identity 至少包含：

```text
ScopeKey + CommandType + TargetType + TargetId + ActorUserId + RequestKey
```

四数据库实现时优先对规范化后的 `IdentityHash` 建唯一索引，同时保留组成字段用于排查。不要直接依赖包含 nullable `CreateOrgId` 的组合唯一索引，因为不同数据库对 `NULL` 唯一性的行为会造成幂等差异。

`IdentityHash` 的构造规则是发包后不可逆的契约：本项目通过 NuGet 分发，消费者数据库里会留下按旧规则算出的 hash，任何后续调整都会让同一请求产生不同 identity、幂等静默失效。首个实现必须一次定死以下规则，并写进契约测试：

- 参与字段固定为 `ScopeKey、CommandType、TargetType、TargetId、ActorUserId、RequestKey`，按此顺序拼接，之后只增不改、不随索引调整重排；
- 分隔符使用不会出现在任何参与值中的固定字符（如 `\n`）；数值型 Id 用不变文化十进制序列化；
- 字符串字段 trim 后保持原大小写；`CommandType/TargetType` 使用固定枚举名，不用显示文案；
- 可空维度（如无机构用户的 `ScopeKey`）归一化为固定哨兵值，不允许 null 与空字符串产生两个不同 hash；
- 哈希算法固定（建议 SHA-256），输出格式固定（建议小写十六进制）；
- 用一组“已知输入 → 已知 hash”快照用例在四库共同锁定，保证任何数据库、任何运行时算出同一值。

完成标准：

- receipt 与领域状态在同一事务提交；
- 相同 identity 的串行、并发请求只推进一次；
- 重试返回第一次成功的 `WfEngineResult`；
- 事务回滚时 receipt 不残留；
- 相同输入在四库与任何运行时得到同一 `IdentityHash`（快照用例）；
- SQLite、MySQL、PostgreSQL、SQL Server 使用同一套契约用例。

## 六、M3a-1：可靠自动节点执行（已交付并通过最终四库 CI）

M3a-1 不扩充 `wf_task`，而是新增可靠执行 Module 的三张表，并把节点访问身份、handler SPI、领取、结果回写和 outbox 接到引擎事务边界上。实体与写入实现分别见 [`WfNodeExecution.cs`](../../backend/src/TenonAdmin.Workflow/Entities/WfNodeExecution.cs)、[`WfNodeExecutionAttempt.cs`](../../backend/src/TenonAdmin.Workflow/Entities/WfNodeExecutionAttempt.cs)、[`WfOutbox.cs`](../../backend/src/TenonAdmin.Workflow/Entities/WfOutbox.cs)、[`WfNodeExecutionStore.cs`](../../backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionStore.cs)、[`WfNodeExecutionDispatcher.cs`](../../backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionDispatcher.cs)。

### 6.1 `wf_node_execution`：一次逻辑执行

```text
ExecutionKey
ScopeKey
InstanceId
TokenId
NodeVisitId        nullable
NodeId
NodeType
DefinitionVersionId
Status
AttemptCount
MaxAttempts
NextRetryAtUtc      nullable
DeadlineAtUtc       nullable
LeaseOwner          nullable
LeaseExpiresAtUtc   nullable
Fence
HandlerType        nullable
HandlerVersion     nullable
InputHash           nullable
OutputHash          nullable
CompletedTimeUtc    nullable
ErrorCode          nullable
Summary            nullable
```

以上是业务字段，另有 `BaseEntity` 的 `Id/CreateTime/CreateUserId/UpdateTime/UpdateUserId/IsDelete`。表约束为：`ExecutionKey` 长度 64，唯一索引名 `uk_wf_node_exec_key`；扫描索引名 `idx_wf_node_exec_scan`，列为 `(Status, NextRetryAtUtc)`。这是新表，字段不设置 `DefaultValue`；`Status` 初始为 `Pending`，`AttemptCount` 与 `Fence` 从 0 开始，`ScopeKey` 必须由 `WfIdentityHash.NormalizeScopeKey` 归一化后落库。

`ExecutionKey` 是固定契约：`scopeKey` 为 null/空串/纯空白时归一化为 `"-"` 哨兵；非空值只做 `Trim()`，保留原大小写。`nodeId` 做 `Trim()`，空白值直接拒绝；`ScopeKey` 或 `nodeId` 含 LF 分隔符也拒绝。缺失的 `NodeVisitId` 使用 `"-"`，存在时与 `InstanceId`、`TokenId`、`DefinitionVersionId` 一样使用不变文化十进制；六个字段严格按 `ScopeKey → InstanceId → TokenId → NodeVisitId → NodeId → DefinitionVersionId` 排列，以 LF 拼接，UTF-8 编码后计算 SHA-256，输出 64 位小写 hex（[`WfExecutionKey.cs`](../../backend/src/TenonAdmin.Workflow/Engine/WfExecutionKey.cs):25-59）。同一节点访问只允许一个逻辑 execution，但可以产生多次 attempt。

`Status` 的实际枚举值和状态语义为：

```text
Pending | Running | Succeeded | RetryScheduled | ManualFallback | Cancelled | Failed
```

`Succeeded/ManualFallback/Cancelled/Failed` 是终态；`Running` 在租约过期后可自转移为 `Running`。其中 `RetryScheduled` 仅在 `NextRetryAtUtc` 到期后可重新领取。当前实体虽然有 `MaxAttempts`，但生产代码没有为它提供来源；在真实 worker 接线前必须补齐配置/模型来源，不能把它写成已经可用的生产预算。

### 6.2 `wf_node_execution_attempt`：每次调用的追加事实

```text
ExecutionId
AttemptNo
StartedAtUtc
EndedAtUtc
ResultType
OutputSummary      nullable
OutputHash         nullable
ErrorCode          nullable
ErrorSummary       nullable
```

以上是业务字段，另有 `BaseEntity` 审计字段。`uk_wf_node_exec_attempt_no` 对 `(ExecutionId, AttemptNo)` 建唯一约束；`AttemptNo` 为 1 基，直接取领取后 execution 的 `AttemptCount`，不得在追加时再次加 1。`ResultType` 只有 `Succeeded/RetryableFailure/ManualFallback/TerminalFailure` 四种，输出正文不落库，只写 `OutputHash` 与最多 512 字符摘要；失败/回退写 `ErrorCode/ErrorSummary`。attempt 的口径是“每个已返回或由 dispatcher 合成的结果一行”，不是“每次真实外部调用一行”：无 handler 或开 socket 前的配置错误都能产生一条无网络调用的 attempt；handler 在返回结果前崩溃，或外部取消在返回结果前传播时，不产生 attempt。

### 6.3 `wf_outbox`：终态通知

```text
ExecutionId
MessageType
MessageKey
PayloadJson         nullable
Status
AttemptCount
AvailableAtUtc
LastError           nullable
CompletedAtUtc     nullable
```

以上是业务字段，另有 `BaseEntity` 审计字段。`uk_wf_outbox_message_key` 对 `MessageKey` 建唯一约束；扫描索引名 `idx_wf_outbox_scan`，列为 `(Status, AvailableAtUtc)`。`MessageKey` 由 `{ExecutionKey}:{MessageType}` 派生，`MessageType` 先 trim，不能为空且不得含 `:`；`PayloadJson` 保存完整正文，不用 `PayloadHash` 替代。当前 `MessageType/MessageKey` 的大小写比较依赖数据库 collation，尚未作统一大小写归一化决定。

`WfOutboxStatus` 的四个值是 `Pending`、`Dispatching`、`Dispatched`、`Failed`。当前生产写入面只有 `WfOutboxStore.EnqueueAsync` 插入 `Pending`；`Dispatching` 的领取、`Dispatched`/`Failed` 的回写、重试退避和对应的 CAS 都是未来消费者任务，不能把状态枚举写成已实现的后台派发流程。

`wf_outbox` 不设置 `LeaseOwner/LeaseExpiresAtUtc/Fence`；`AvailableAtUtc` 同时表示可投递时刻和可见性租约，消费者领取次数 `AttemptCount` 作为单调 fence，迟到回写必须用 `WHERE AttemptCount = @myAttemptCount` CAS。这是实体已经定下的消费者契约；领取、重投和实际派发 worker 尚未实现，不能把字段齐全写成消费闭环已经上线。

引擎的执行结果 outbox payload 走 `WfModelJson.Options`；该配置启用 `WhenWritingNull`，所以 null 属性会被省略，不会序列化成 `"key": null`。Webhook 的出站请求体另有固定的 8 个字段：

```text
executionKey, instanceId, tokenId, nodeVisitId,
nodeId, definitionVersionId, businessKey, attempt
```

当前没有 `payloadVersion` 字段；首版按 YAGNI 保持 8 字段，未来若破坏请求契约必须显式版本化。

### 6.4 事务边界与 handler 契约

dispatcher 的执行路径是固定三段（[`WfNodeExecutionDispatcher.cs`](../../backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionDispatcher.cs):50-100）：

1. **tx1 领取**：`WfNodeExecutionStore.ClaimAsync` 在 `UseTranAsync` 内执行条件 UPDATE 与读回。领取条件是 `Pending`、到期的 `RetryScheduled`，或租约已过期的 `Running`；领取时写入 lease owner/expiry，同时 `Fence + 1`、`AttemptCount + 1`。
2. **事务外调用**：读取实例、token、定义版本和节点模型形成只读快照，构造 `IWorkflowNodeHandler` 的 context 后调用 handler。context 不包含 DB session 或 SqlSugar 实体；handler 只返回四种结果，不能推进 token、写任务状态或自行开事务（[`IWorkflowNodeHandler.cs`](../../backend/src/TenonAdmin.Workflow/Abstractions/IWorkflowNodeHandler.cs):81-147）。
3. **tx2 回写**：`NodeExecutionCompletedCmd` 进入引擎的一条命令一个事务路径。先用 `Id + Fence + Status == Running` CAS 更新 execution；再追加 attempt，并把 execution 结果、token 推进、相关 history 和终态 outbox 原子提交（[`WorkflowEngine.cs`](../../backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs):1196-1389）。CAS 影响行数不是 1 时，整笔 tx2 回滚，旧 owner 的迟到结果不会留下 attempt 或 outbox。

成功沿现有 `TakeTransitionOp` 离开节点；`ManualFallback` 计划人工兜底，但只有节点存在 `assignee` 配置且解析出用户时才创建人工任务；未配置办理人来源或解析出 0 人时，不建任务、不自动放行，execution 保持 `ManualFallback`、token 原地停住（[`WfManualFallbackOp.cs`](../../backend/src/TenonAdmin.Workflow/Engine/Operations/WfManualFallbackOp.cs):17-52）。`RetryScheduled`、`Failed`、`Cancelled` 不推进 token。自动节点生命周期本身不另写一条 history，节点进出和人工兜底沿既有操作写入，失败/重试事实以 attempt 为准。

### 6.5 Webhook 首个 handler 与验证边界

Webhook 配置字段是 `URL/method/headers/timeout/onFailure`。`2xx` 返回 `Succeeded`；`408/423/425/429`、除 `501` 外的 `5xx`，以及网络异常、`HttpRequestException`、`IOException` 和 handler 自身超时返回 `RetryableFailure`；`3xx`（不跟随重定向）、`501` 和其余大多数 `4xx` 返回 `TerminalFailure`。发送请求和读取响应体在同一个分类范围内，因此响应体读取阶段的自超时、`HttpRequestException` 或 `IOException` 也按上述规则重试；其中 `JobHttpFenceBlockedException` 即使包在嵌套的 `HttpRequestException` 中仍识别为 SSRF/安全围栏终态。外部取消令牌直接传播，不转换为业务结果。`onFailure=manual` 只把 `TerminalFailure` 转成 `ManualFallback`，不把 retryable 变成人工；重试预算耗尽由引擎独立判定，manual 配置不接管或重置该分支（[`WebhookNodeHandler.cs`](../../backend/src/TenonAdmin.Workflow/Providers/WebhookNodeHandler.cs):51-119、188-267；[`JobHttpFence.cs`](../../backend/src/TenonAdmin.Services/Jobs/JobHttpFence.cs):7-8、110-135）。除这些明确分类的异常外，handler 没有 `catch (Exception)` 兜底，未预期异常继续逸出。

`Retry-After` 只在 retryable response 上读取，支持 delta-seconds 和 HTTP-date；解析结果必须在 `(0, 24h]`，否则回退到引擎指数退避。消费者可前置注册同 `NodeType` 的 handler，前置实现胜出；内置 `WebhookNodeHandler` 保留为 fallback（[`WorkflowSetup.cs`](../../backend/src/TenonAdmin.Workflow/WorkflowSetup.cs):67-75）。

时间相关的新字段统一采用 UTC 语义。即使 CLR 仍使用 `DateTime`，也要在命名、写入和测试中保证 `Kind/转换` 一致，避免多实例时区和夏令时影响 deadline、lease 与 retry。

## 七、M3b：AI Decision

AI 决策不写入人的 `wf_his_task.Comment`，新增 `wf_ai_decision`：

```text
ExecutionId
ProposalJson
ProposalSchemaVersion
SchemaValid
PolicyVersion
PolicyOutcome
Confidence
RiskFlags
EvidenceRefsJson
InputHash
PromptVersion
Provider
Model
ShadowMode
HumanFallbackReason
HumanOverrideOutcome
CreateTime
```

保持以下安全不变量：

- 模型只生成 proposal；
- 服务端 schema/policy 决定路由；
- V0 只允许显式授权的低风险自动放行；
- 自动拒绝、低置信度、风险标记、证据不足和异常全部转人工；
- 回放使用已保存 proposal 和 policy version，不重新调用模型伪造历史；
- UI 将“系统执行/AI 决策”与“人的审批意见”分区展示，再按时间合并为完整审计视图。

目标 execution/attempt/decision/outbox 模型见 [`elsa3-slickflow-ai-reference-2026-08-23.md` §4.4–§4.8](./elsa3-slickflow-ai-reference-2026-08-23.md#_44-目标架构一个可靠执行-moduleai-只是-adapter)。

## 八、索引与唯一约束

现有索引与 M3a-1 新增约束以实体声明为准。当前关键项如下：

| 表 | 建议索引/约束 | 目的 |
| --- | --- | --- |
| `wf_operation_receipt` | `UNIQUE(IdentityHash)` | 请求幂等 |
| `wf_instance` | `(StarterUserId, Status, CreateTime)` | 我发起的分页与状态筛选 |
| `wf_history` | `(InstanceId, CreateTime)`、`(EventType)`；无 `UNIQUE(InstanceId, Sequence)` | 时间线与事件筛选；顺序由 `wf_instance.HistorySeq` 在事务内分配 |
| `wf_his_task` | `(UserId, CreateTime)`、`(InstanceId, CreateTime)` | 已办和详情时间线 |
| `wf_node_execution` | `UNIQUE(ExecutionKey)`、`(Status, NextRetryAtUtc)` | 防重复推进与领取扫描 |
| `wf_node_execution_attempt` | `UNIQUE(ExecutionId, AttemptNo)` | 防 attempt 编号重复 |
| `wf_outbox` | `UNIQUE(MessageKey)`、`(Status, AvailableAtUtc)` | 可靠派发与扫描 |

办理人和抄送的既有唯一约束不能只用 `(TaskId, UserId)` 或 `(InstanceId, NodeId, UserId)` 草率改写。连续多级主管允许同一人在不同顺序重复出现，流程也可能再次进入同一节点；`NodeVisitId` 已提供访问身份，但并行 fork/join 的最终唯一键仍留待 M3b/后续网关设计。

## 九、兼容升级策略

TenonAdmin 通过 NuGet 和 CodeFirst 分发，迁移应优先采用可回滚的增量方式：

1. 新增表优先于把不同职责塞入旧表；
2. 新增列先 nullable 或带跨数据库一致的默认值；
3. `Version` 从 `0` 开始，旧行可直接回填；
4. `CompletedTime` 对旧终态实例可从 `InstanceCompleted` 事件回填，无法确定时保持空；
5. `NodeVisitId` 对旧 token、任务、历史和抄送保持 nullable；升级后新进入节点时由 `EnterNodeOp` 生成，旧 token 不做后台回填；
6. `wf_history.Sequence`、`ActorType` 和 `PayloadVersion` 的存量默认值通常都是 `0`；`PayloadVersion` 的 `SugarColumn(DefaultValue = "0")` 只适用于本次提交之后环境首次添加该列的迁移。已经在父版本中添加列的环境保留现有值，不重新运行或覆盖 append-only 旧 history。新建 `WfHistory` 实体的 CLR initializer 是 `1`；不要把数据库加列默认值 `0` 改写成 legacy backfill `1`；
7. M3a-1 三张新表没有存量行升级，不设置 `DefaultValue`；新行按实体初始化值进入 `Pending` 等初始状态；
8. 枚举只追加数值，不重排已有值；
9. 不重命名或删除已发布字段；淘汰字段先停止写入，再跨版本处理；
10. 每项约束使用同一套 provider-neutral 契约测试跑四库。

## 十、推荐开发顺序

### M2b 收口（2026-08-24 提前项）

1. 增加 `WfInstance.Version`、`WfToken.Version`，旧行回填 0；
2. 撤销、催办、超时与人工动作的竞争测试直接建立在实例/Token 级 CAS 上，避免 M2c 重写。

字段本身是可回填的增量迁移，成本极低；提前一个里程碑落地的收益是 M2b 的竞争语义从一开始就正确，而不是先按任务级 CAS 写一批测试、M2c 再改写一遍。

### M2c

1. 增加 `WfInstance.CompletedTime`；
2. 新增 operation receipt，`IdentityHash` 构造规则按 §五 一次定死；
3. 将 `RequestId` 贯穿命令、receipt 和事件；
4. 用四库测试锁定重复请求、并发 CAS、回滚和终态保护。

### M2c 与 M3a 之间

1. 决定办理人分配历史是保留 actor 还是新增 assignment history；
2. 增加 `AssignedTime/ActivatedTime/StartedTime`；
3. 修正转办、会签和顺序审批的耗时语义。

### M3a-1（已交付，2026-09-03）

1. `NodeVisitId` 已贯穿 token、任务、历史、抄送和 execution；
2. `wf_history` 已具备 Token、访问 Id、实例序号、actor 和 payload version；
3. `wf_node_execution`、`wf_node_execution_attempt`、`wf_outbox` 已按 §6 建表；
4. execution key、retry、lease/fence、attempt 追加、outbox 幂等入队和 `NodeExecutionCompletedCmd` 原子回写已由 [`WfExecutionKeyTests.cs`](../../backend/tests/TenonAdmin.Tests/WfExecutionKeyTests.cs)、[`WfNodeExecutionContractTests.cs`](../../backend/tests/TenonAdmin.Tests/WfNodeExecutionContractTests.cs) 和 [`WfNodeExecutionDispatcherTests.cs`](../../backend/tests/TenonAdmin.Tests/WfNodeExecutionDispatcherTests.cs) 覆盖；
5. Webhook 是首个真实 handler，Fake handler 覆盖其他结果类型的同一 dispatcher 回写路径。

交付边界：当前没有生产代码在 `EnterNodeOp` 自动创建 `wf_node_execution` 行，也没有生产后台 worker 调用 dispatcher；这两项属于 Task 8b/后续里程碑。Webhook 配置设计 UI 属于 M3a-2，不在本轮数据库交付内。

验证范围：只有 Webhook `Succeeded` 路径具备完整 dispatcher E2E；其他结果持久化使用 Fake handler 走相同路径。当前工作树中的 Webhook P1 修订及其测试（[`WfWebhookNodeHandlerTests.cs`](../../backend/tests/TenonAdmin.Tests/WfWebhookNodeHandlerTests.cs):125-255）覆盖了 DNS callback fence、发送/响应体读取共用分类、IOException/响应体自超时重试和外部取消传播；未覆盖真实 TLS/HTTP2/chunking/proxy 环境。handler 未预期异常在未来 worker 存在后可能导致租约过期、重复领取的 livelock，兜底策略尚待该轮决定。

最终四库 CI 证据：[`run 33738099310`](https://github.com/Tenon-Net/TenonAdmin/actions/runs/33738099310)，HEAD `80f9c72`；SQLite `1116/1116`、MySQL `1116/1116`、PostgreSQL `1116/1116`，SQL Server 过滤子集 `118/118`。`contract-drift`、`docker-smoke`、`template-smoke` 同轮通过；该 run 已包含 DNS callback fence、响应体读取异常分类、外部取消与资源释放的最终 P1/P2 修订。Task 10 没有 API/DTO 变更。

### M3b

1. 新增 AI decision；
2. 先跑 shadow mode；
3. 达到评测阈值后开放低风险自动放行；
4. 将人工推翻、fallback、schema 失败和成本纳入产品指标。

### 真正开发并行网关时

根据已经实现的 fork/join 语义决定 `ParentTokenId/ForkId` 或 join 表，不在当前阶段建立通用并行执行 Seam。

## 十一、最终判断

现有模型的核心方向正确：人工审批状态是持久化事实，定义版本是不可变快照，业务状态留在消费方，M3a-1 的机器执行通过独立 execution/attempt/outbox 事实链落库，AI 仍通过独立 Adapter 接入。这些决定都应保留。

M3a-1 的可靠执行内核已经交付并通过四库 CI，但不能把它表述成完整的生产自动节点闭环：当前没有生产代码创建 `wf_node_execution` 行，也没有生产 worker 调用 dispatcher；`EnterNodeOp` 的 Webhook 接线和 worker 属于 Task 8b/未来里程碑。可靠演进仍需区分三类身份：

1. **请求身份**：`RequestId/operation receipt`，回答“这是不是同一次用户命令”；
2. **节点访问身份**：`NodeVisitId`，回答“这是不是同一次流程图访问”；
3. **机器执行身份**：`ExecutionKey/AttemptNo`，回答“这是不是同一次逻辑执行或同一次外部调用”。

三类身份分开后，人工审批、重试、循环、并行、Webhook 和 AI 才不会互相误判。按本文的增量迁移与接线边界继续推进，当前 9 表加 M3a-1 三张可靠执行表可以作为稳定地基，无需更换 Workflow Module 的外部 Interface。
