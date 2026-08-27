# TenonAdmin.Workflow 数据库字段设计评审

> 文档入口：[`README.md`](./README.md)
> 日期：2026-08-24
> 评审基线：`dddf18f047a239c74752dac0879de333f01a9994`
> 范围：当前 9 张 `wf_*` 表，以及 M2c、M3a、M3b 对持久化模型的新增要求

## 一、结论

现有 9 表设计适合当前人工审批，定义、版本、实例、Token、活跃任务和历史记录的职责基本清楚，**不需要推倒重来**。它能通过增加字段和新表继续演进，但不能理解为“已经无需迁移地兼容可靠自动节点、并行网关和 AI 审批”。

分阶段判断如下：

| 能力 | 当前兼容性 | 判断 |
| --- | --- | --- |
| M1/M2b 人工审批 | 已兼容 | 现有模型足够，任务级 CAS 能防同一待办双批 |
| M2c 请求幂等与四库终态保护 | 部分兼容 | 需要 operation receipt，并补实例/Token 级并发保护 |
| M3a Webhook/自动节点 | 尚未兼容 | 需要节点访问身份、execution、attempt、outbox 和 fence |
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

### 4.5 Token 缺少节点访问身份

当前 `WfToken` 只有 `InstanceId/NodeId/Status`。即使允许同一实例存在多个 Token，也不能稳定区分：

- 同一 Token 第一次和第二次进入同一节点；
- 哪一次节点访问创建了某个任务、抄送或事件；
- 自动节点恢复时面对的是旧访问还是新访问；
- 并行分支属于哪次 fork、应在哪次 join 汇合。

建议增加：

```text
wf_token.NodeVisitId long
wf_token.Version     int
```

每次进入新节点时生成新的 `NodeVisitId`，在该节点停留期间保持不变。以下记录复制它：

```text
wf_task.NodeVisitId
wf_his_task.NodeVisitId
wf_history.NodeVisitId
wf_cc.NodeVisitId
wf_node_execution.NodeVisitId
```

`Version` 用于 CAS，`NodeVisitId` 用于稳定身份，两者职责不能混用。将来真正开发并行网关时，再根据已落地语义增加 `ParentTokenId/ForkId` 或独立 join 表。

### 4.6 `wf_history` 缺少可靠关联与顺序

当前事件只有：

```text
InstanceId
EventType
NodeId
PayloadJson
CreateTime
```

人工串行流程尚可使用；出现重复进节点、并行 Token、后台 worker 或 AI 决策后，只靠时间和雪花 ID 难以准确说明事件属于哪一次执行。

建议分阶段增加：

```text
TokenId          nullable，兼容实例级事件
NodeVisitId      nullable，兼容旧数据
Sequence         实例内单调递增
ActorType        Human/System/Timeout/AI/Worker
ActorUserId      nullable
RequestId        nullable
PayloadVersion   int not null default 1
```

新数据对 `(InstanceId, Sequence)` 建唯一约束。`PayloadJson` 继续承载不同事件的细节，但读取方按 `EventType + PayloadVersion` 解释，不能把不带版本的 JSON 当永久契约。

### 4.7 append-only 语义目前只靠代码约定

`wf_history` 和未来的 attempt/AI decision 都是只增事实，但当前 `WfHistory` 继承 `BaseEntity`，天然带 `UpdateTime/UpdateUserId/IsDelete`。这不是当前运行错误，却与 append-only 语义不完全一致。

考虑到本项目通过 NuGet + CodeFirst 分发，已有字段的删除和基类替换会扩大消费者升级风险。建议：

- 不删除现有字段；
- 工作流 Module 不暴露历史记录的通用更新/删除 Interface；
- 契约测试证明正常命令只追加历史；
- 新建的 attempt/decision 记录从一开始采用只增写入路径；
- 管理清理走明确的保留期策略，而不是普通软删除。

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

## 六、M3a：可靠自动节点执行

M3a 不扩充 `wf_task`，而新增可靠执行 Module 的持久化记录：

### 6.1 `wf_node_execution`

```text
ScopeKey/CreateOrgId
InstanceId
TokenId
NodeVisitId
NodeId
DefinitionVersionId
HandlerType
HandlerVersion
ExecutionKey
Status
AttemptCount
DeadlineAtUtc
NextRetryAtUtc
LeaseOwner
LeaseExpiresAtUtc
Fence
InputHash
OutputHash
CompletedTimeUtc
```

`ExecutionKey` 唯一。同一节点访问只允许一个逻辑 execution，但可以产生多次 attempt。稳定身份至少包含组织范围、实例、Token、`NodeVisitId`、节点和定义版本。

### 6.2 `wf_node_execution_attempt`

```text
ExecutionId
AttemptNo
StartedAtUtc
EndedAtUtc
Provider
Model
PromptVersion
SchemaVersion
PolicyVersion
ResultType
OutputSummary
ErrorCode
ErrorSummary
TokenUsage
Cost
```

attempt 必须保留每次真实调用，重试不能覆盖旧记录。输出正文、敏感字段和密钥不直接进入日志；保存必要摘要、hash 和受控引用。

### 6.3 `wf_outbox`

```text
ExecutionId
MessageType
MessageKey
PayloadJson/PayloadHash
Status
AttemptCount
AvailableAtUtc
LastError
CompletedAtUtc
```

结果、变量、历史和 outbox 在同一短事务提交。远程调用发生在事务外，worker 使用 lease/fence 防止过期 owner 覆盖新结果。

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

目标 execution/attempt/decision/outbox 模型见 [`elsa3-slickflow-ai-reference-2026-08-23.md` §4.4–§4.8](./elsa3-slickflow-ai-reference-2026-08-23.md#44-目标架构一个可靠执行-moduleai-只是-adapter)。

## 八、索引与唯一约束

现有索引能覆盖基本页面，但后续实现应按真实查询补以下约束：

| 表 | 建议索引/约束 | 目的 |
| --- | --- | --- |
| `wf_operation_receipt` | `UNIQUE(IdentityHash)` | 请求幂等 |
| `wf_instance` | `(StarterUserId, Status, CreateTime)` | 我发起的分页与状态筛选 |
| `wf_history` | `UNIQUE(InstanceId, Sequence)` | 实例内确定性事件顺序 |
| `wf_his_task` | `(UserId, CreateTime)`、`(InstanceId, CreateTime)` | 已办和详情时间线 |
| `wf_node_execution` | `UNIQUE(ExecutionKey)`、`(Status, NextRetryAtUtc)` | 防重复推进与 worker 领取 |
| `wf_node_execution_attempt` | `UNIQUE(ExecutionId, AttemptNo)` | 防 attempt 编号重复 |
| `wf_outbox` | `UNIQUE(MessageKey)`、`(Status, AvailableAtUtc)` | 可靠派发与扫描 |

办理人和抄送的唯一约束不能只用 `(TaskId, UserId)` 或 `(InstanceId, NodeId, UserId)` 草率实现。连续多级主管允许同一人在不同顺序重复出现，流程也可能再次进入同一节点；应先引入 assignment/node visit identity，再定义稳定唯一键。

## 九、兼容升级策略

TenonAdmin 通过 NuGet 和 CodeFirst 分发，迁移应优先采用可回滚的增量方式：

1. 新增表优先于把不同职责塞入旧表；
2. 新增列先 nullable 或带跨数据库一致的默认值；
3. `Version` 从 `0` 开始，旧行可直接回填；
4. `CompletedTime` 对旧终态实例可从 `InstanceCompleted` 事件回填，无法确定时保持空；
5. `NodeVisitId` 对旧历史保持 nullable，对升级后的新节点访问强制生成；
6. 枚举只追加数值，不重排已有值；
7. 不重命名或删除已发布字段；淘汰字段先停止写入，再跨版本处理；
8. 每项约束使用同一套 provider-neutral 契约测试跑四库。

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

### M3a

1. 引入 `NodeVisitId`，贯穿 Token、任务、历史、抄送和 execution；
2. 为 `wf_history` 增加 Token、序号、actor 和 payload version；
3. 新增 execution、attempt、outbox；
4. 以 Fake Handler 和 Webhook Handler 验证稳定 execution key、retry、lease/fence 和崩溃恢复。

### M3b

1. 新增 AI decision；
2. 先跑 shadow mode；
3. 达到评测阈值后开放低风险自动放行；
4. 将人工推翻、fallback、schema 失败和成本纳入产品指标。

### 真正开发并行网关时

根据已经实现的 fork/join 语义决定 `ParentTokenId/ForkId` 或 join 表，不在当前阶段建立通用并行执行 Seam。

## 十一、最终判断

现有模型的核心方向正确：人工审批状态是持久化事实，定义版本是不可变快照，业务状态留在消费方，AI 通过独立 Adapter 接入。这些决定都应保留。

需要修正的是“有 Token 表就已经兼容所有后续执行”的预期。可靠演进还需要三类身份：

1. **请求身份**：`RequestId/operation receipt`，回答“这是不是同一次用户命令”；
2. **节点访问身份**：`NodeVisitId`，回答“这是不是同一次流程图访问”；
3. **机器执行身份**：`ExecutionKey/AttemptNo`，回答“这是不是同一次逻辑执行或同一次外部调用”。

三类身份分开后，人工审批、重试、循环、并行、Webhook 和 AI 才不会互相误判。按本文顺序做增量迁移，当前 9 表可以继续作为稳定地基，无需更换 Workflow Module 的外部 Interface。
