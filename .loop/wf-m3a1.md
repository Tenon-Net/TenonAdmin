# Loop: TenonAdmin.Workflow M3a-1 可靠自动节点执行层

## GOAL

在 M2c(已收口,commit `9589c4d`,过渡步骤「分配历史 + 耗时口径」已并入)基础上做 **M3a-1**:一个可靠的自动节点执行 Module——`NodeVisitId` 贯穿、`wf_history` 补齐关联字段、`wf_node_execution`/`wf_node_execution_attempt`/`wf_outbox` 三张新表、`IWorkflowNodeHandler` SPI、Fake Handler(验证闭环)与 Webhook Handler(首个真实 Adapter,一等功能)。这是 **M3b(AI Decision)的唯一前置**,本身即可独立发布。范围与定案见 `docs/workflow/workflow-design-plan-2026-08-17.md` **§十五 15.2–15.3**、`workflow-database-design-review-2026-08-24.md` **§4.5、§4.6、§六、§十(M3a)**、`elsa3-slickflow-ai-reference-2026-08-23.md` **§4.4–§4.8**。

**禁止做**:M3a-2 产品面(动态表单/动词封顶/并行分支/React 工作流页 port)、M3b(AI Decision/provider 适配/policy/shadow mode)、并行网关(`ParentTokenId`/`ForkId`/join 表——按数据库评审 §十「真正开发并行网关时」的说明,不在本阶段建)。**不改 `web-react/`**,除非某 Task 明确需要 `gen:api`(本轮预期大概率不需要——execution 是内部引擎能力,不必然新增 HTTP 端点;若某 Task 的 plan 阶段判断确实需要新端点,须在该 Task 的 Plan 里写明理由再做)。不抽 `web/` 与 `web-react/` 共享层。不新增审批动词、监控页、设计器能力。不照抄 Elsa/Slickflow 的模块边界——`IWorkflowNodeHandler` 是本仓量身定的最小 Seam,不是要移植一整套编排框架。

## Loop 纪律(硬约束,协调者与执行者共用)

每个 **Task** 必须走完 **plan → exec → review → (修 Findings) → 勾选**,**禁止跳过 review、禁止 plan+exec 同一轮勾选、禁止未跑闸门就勾选**。

**本台账在 M2c 纪律基础上新增一条:分析/审查用 Opus,执行用 Sonnet。** 协调者(当前这个 `/loop` 会话,不管自己跑在什么模型上)永远只做「读台账、判 GUARD、路由到哪个 Agent、把 Agent 的结果写回台账、跑 git、排下一轮」这些编排工作,**协调者自己不做 plan/review 的分析判断,也不做 exec/修 Findings 的产品代码改动** —— 那些活儿必须通过 `Agent` 工具委派给按下表指定 `model` 的子代理,协调者只负责转译结果、独立复跑一遍闸门做二次确认(不能只信子代理自报的「测试都过了」),然后落笔台账。

| 阶段 | 委派给谁 | `model` | 子代理拿到的权限/边界 | 协调者事后必做 |
|---|---|---|---|---|
| **plan** | `Agent`(不传 `subagent_type`,即 general-purpose) | `opus` | 全工具,但只读设计文档 + 代码,**不写产品代码**(prompt 里显式声明这条边界,不依赖工具限制强制) | 把子代理返回的决策点/改动清单/步骤/测试清单/陷阱**原样或精简**写进 `## Plan`;更新 Status |
| **exec** | `Agent(subagent_type: "oh-my-claudecode:executor")` | `sonnet` | 全工具,按上一轮 plan 阶段写定的 `## Plan` 实现,跑 Task 相关测试 | 协调者**亲自重跑一遍**闸门命令(不止信子代理报告);核对 `git diff --stat` 改动面与 Plan 的改动清单一致;**不勾选** |
| **review** | `Agent`(不传 `subagent_type`) | `opus` | 全工具(含 Edit,做变异测试要改代码再复原);须在返回内容里**明确声明「自审」**(会话规则禁止未经用户要求派子 agent 做「换人复核」的假象——这仍是自审,只是换了模型,不是换了独立视角) | 核对子代理是否真的跑了变异测试(`git log`/`git status` 交叉验证「文件真的改过又复原了」);把 P1/P2 写进 `## Findings`;**仍不勾选**(有未修 P1/P2) |
| **修 Findings** | `Agent(subagent_type: "oh-my-claudecode:executor")` | `sonnet` | 全工具,只修 review 列出的 P1/P2 | 协调者亲自重跑变异点转红后复原、重跑闸门 |
| **勾选** | 协调者自己(不派子代理) | — | — | 确认 0×P1、0×未修 P2、闸门已跑,才在 `## Tasks` 打勾 |

**派 Agent 时怎么写 prompt**(子代理是全新上下文,不共享协调者的记忆):prompt 里必须包含——本 Task 的编号与目标、`## 语义契约` 全文(复制进去,不要求子代理自己去读台账)、涉及的设计文档章节路径(子代理自己读)、**本轮到底要做哪一件事**(不许子代理自己决定「顺便也做下一个 Task」)、以及 exec/修Findings 阶段要跑哪几条闸门命令。子代理跑完后**协调者永远要独立验证**,不能把子代理的自我报告当唯一证据——这是 Agent 工具自身的使用守则(「Trust but verify」),在本台账里被具体化成上表最后一列。

**轮次记账**:每轮结束更新 `## Status` + `## Log` 一行。`max: 70`(比 M2c 更大——本里程碑涉及更重的并发/持久化设计,且每轮多一次 Agent 往返)是熔断线,不是建议跳过 review 的理由,协调者不许自行提高。

**Git**:commit message 英文 conventional commits;**默认不 push**,用户明确要求才 push。不提交 `TestResults/`。

## DONE-CONDITION

- 本账本 `## Tasks` 全部打勾
- `dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` 绿(**基线 264**,M3a-1 只增不减;过滤器写法沿用 M2c,不许改)
- 同一 `ExecutionKey` 串行/并发重放只推进一次(真实竞态多半构造不出来,参考 M2c Task 5/8 先例:能构造的部分用变异测试/SPI 注入钉,构造不出来的部分如实在 Findings 里写清射程,不许拿「测不到」当「不用测」的理由)
- worker 崩溃可恢复:lease 过期后可被重新领取,且不会对已经成功的 execution 重复推进
- 远程调用(Webhook HTTP 请求)发生在数据库事务之外,有测试证明(不是靠代码审查「看起来对」)
- **四库契约套件**在 CI 矩阵四腿各绿(execution 唯一约束 / CAS-fence 竞争 / 事务回滚 / outbox 契约,同一套用例经 `TestDb.DbType` 参数化,同 M2c Task 8 模式)
- Fake Handler 与 Webhook Handler 均有测试覆盖 `Succeeded`/`RetryableFailure`/`ManualFallback`/`TerminalFailure` 四条结果路径
- 若任一 Task 的 plan 阶段判定确实需要新 HTTP 端点或改了现有响应 DTO:`cd web && npm run typecheck && npm run lint` 绿,双模板 `gen:api` 后 `schema.d.ts` SHA256 一致。**若全程没有新增/改动任何 API 面,本条视为不适用,在 DONE 判定里如实注明「本条不适用,理由是……」,不是当作没跑**

> ⚠ 过滤器沿用 M2c 修正写法:`FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow`。不要回退成 `~Workflow` 或 `~Wf|~Workflow`。

## Status

- 轮次: 5
- max: 70
- 当前任务: Task 2(`IWorkflowNodeHandler` SPI + Context/Result 类型 + FakeNodeHandler)
- 当前阶段: plan 已完成
- 上一轮: Round 5 — Task 2 **plan**(Opus general-purpose 子代理,39 次工具调用,11 分钟)。7 个决策点全部拍板,含语义契约点名的那一项(`Cancelled` **不进**结果枚举)。产出改动清单 **3 个新文件、0 个既有文件改动**,预期 279 → 291(+12)。plan 明确点出本 Task 最大越界风险是「顺手接引擎」(`EnterNodeOp` 对 `Webhook` 仍应抛 48008,那是正确的中间状态)。
- 下一步: Round 6 — Task 2 **exec**。协调者派 `Agent(subagent_type="oh-my-claudecode:executor", model="sonnet")`,prompt 塞入本文件 `## Plan` 全文 + 两条闸门命令 + 十条陷阱(尤其陷阱 1「不改 `EnterNodeOp` 的 48008 分支」、陷阱 2「不碰 `WorkflowSetup.cs`」、陷阱 3「`FakeNodeHandler` 绝不进生产 DI」)。子代理跑完后协调者**亲自重跑**两条闸门(期望 291/291),并用 `git diff --stat` 核对**恰好 3 个新文件、0 deletion**。**不勾选**,不顺带做 Task 3。

> ⚠ 过滤器口径提醒:台账 DONE-CONDITION 规定的过滤器是 `FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow`。若某子代理报的数字比预期少 1,先确认它是不是用了带命名空间前缀的变体(那个口径少 1 条)。**勾选与验收一律以台账那条为准。**

## 已知起点(2026-09-01,M2c 收口 + 过渡步骤后)

- **M2c 已交付、M3a-1 直接复用、不重做的部分**:
  - `wf_operation_receipt` / `IdentityHash` / `RequestId` 贯穿命令与事件 / `WfInstance.CompletedTime` / 通知失败结构化日志 / 四库持久化契约套件先例(`WfPersistenceContractTests`,M3a-1 的四库套件应参照它的写法与 `TEST_FILTER` 纳入方式,不是从零发明)。
  - `wf_history.RequestId` **已经加了**(M2c Task 6)——本里程碑 §4.6 建议的 `wf_history` 扩字段里,`RequestId` 这一列不用再做,只需补 `TokenId`/`NodeVisitId`/`Sequence`/`ActorType`/`ActorUserId`/`PayloadVersion`。
  - 过渡步骤已交付 `wf_task_actor.ActivatedTime` / `wf_his_task.StartedTime`,与 `NodeVisitId` 无关,互不冲突。
- **今天零存在的 M3a-1 核心件(别去找)**:
  - `WfToken.NodeVisitId` / `WfTask.NodeVisitId` / `WfHisTask.NodeVisitId` / `WfHistory.NodeVisitId` / `WfCc.NodeVisitId` — **零字段**
  - `wf_history.TokenId`/`Sequence`/`ActorType`/`ActorUserId`/`PayloadVersion` — **零字段**
  - `IWorkflowNodeHandler` 及 `WfNodeExecutionContext`/`WfNodeExecutionResult` — **零文件**
  - `wf_node_execution` / `WfNodeExecution` 实体 — **零文件**
  - `wf_node_execution_attempt` / `WfNodeExecutionAttempt` 实体 — **零文件**
  - `wf_outbox` / `WfOutbox` 实体 — **零文件**
  - execution dispatcher / worker(领取 → 调 handler → 落结果)— **零文件**
  - Fake/Webhook Handler — **零文件**
- **现状读码要点(供 Task 1 plan 阶段核对,不要重新发现一遍)**:
  - `EnterNodeOp.ExecuteAsync` 是 token 每次进新节点的唯一入口(`ctx.Token.NodeId = Node.Id` 那一行紧跟在 token 级 CAS 之后)——`NodeVisitId` 的生成点应该就在这里,与 CAS 同一事务。
  - `WfExecutionContext.AppendHistoryAsync` 是 `wf_history` 写入的唯一收敛点(M2c Task 6 的读码结论,20 个调用点零改动地拿到了 `RequestId`)——`TokenId`/`NodeVisitId`/`Sequence`/`ActorType`/`ActorUserId`/`PayloadVersion` 大概率也能走同一条路,但 `Sequence`「实例内单调递增」需要额外设计(乐观递增易撞号,建议参考 `WfTask.Version`/`WfInstance.Version` 的 CAS 先例,或用数据库侧的原子自增手法——具体交给 Task 1 的 plan 阶段定案,这里只提醒别漏了这条不是「加个字段」那么简单)。
  - 绕开 `ctx`(不经 `WfExecutionContext`)直接写 `wf_history`/`wf_task_actor` 的路径(超时 ×3、催办 ×1,M2c Task 6 已经记录过)在 `ActorType`/`ActorUserId` 上要填 `System`/`null` 还是 `Timeout`/催办者 Id,同样交给 Task 1 plan 定案。
- **测试基线**:指定过滤器 **264/264**(259 M2c 基线 + 5 条过渡步骤新增);`web` typecheck/lint 绿;`src/workflow/` vitest 35/35(M3a-1 若不碰前端,这两条只在验收轮复核一次,不必每个 Task 都重跑)。

## 语义契约(跨任务长期有效;`## Plan` 被重写也不得丢)

> Task 1 的 plan 阶段必须把下面留白的行填实(设计文档给的是方向,不是可以直接抄的最终代码);已经写死的行以后不能推翻,推翻要走「新决策 + 说明为何推翻」而不是静默覆盖。

| 场景 | 定案 |
|---|---|
| `NodeVisitId`(Task 1 定案) | `long?` 雪花(内核 `IIdGenerator`),五张表(`WfToken`/`WfTask`/`WfHisTask`/`WfHistory`/`WfCc`)一律可空。唯一生成点 = `EnterNodeOp.ExecuteAsync` 中紧跟 `ClaimTokenAsync` 之后、与 `NodeId` 同一条 UPDATE。下游:`WfTask`/`WfCc` 从 token 拷,`WfHisTask` 从 **`Task.NodeVisitId`** 拷,`WfHistory` 在 `AppendHistoryAsync` 里从 token 拷。旧数据保持 `null`,**不做回填**(节点访问身份推不出来,编假的比留空更坏)。 |
| `wf_history.Sequence`(Task 1 定案) | `wf_instance.HistorySeq`(`int` NOT NULL `DefaultValue="0"`)做计数器,`SET HistorySeq = HistorySeq + 1` 原子递增后**在同一事务内**读回。逐行分配、无间隙、无重试循环。四库通用语法,不用 PG `RETURNING`/SqlServer 复合赋值。绕开 `ctx` 的 4 个路径(超时 x3、催办 x1)走 `WfHistorySequence.WriteSystemRowAsync` 的短事务。**本里程碑刻意不建 `UNIQUE(InstanceId, Sequence)`**:存量库旧行一律回填 0,同实例 >=2 条旧历史会让建索引当场失败;要建须先做分实例回填迁移——挂账给 Task 10 或专门迁移轮,不在 Task 1。 |
| `wf_history.ActorType`(Task 1 定案) | 新枚举 `WfHistoryActorType { Unknown=0, Human=1, System=2, Timeout=3, Worker=4, Ai=5 }`,**不复用**已被 `wf_task_actor` 占用的 `WfActorType`;只追加不重排(评审 §九 #6)。`Unknown=0` 专给升级后旧行。用户命令→`Human`+操作人;超时(引擎 `BeginTimeoutAsync` + `WfTimeoutJob` 三处)→`Timeout`+`null`;催办→`Human`+催办人(**不设 `Reminder`**:actor 维度答「谁」,「催办」由 `EventType=TaskUrged` 表达)。`AppendHistoryAsync` 签名不改,靠 `WfExecutionContext` 的 `required init` 属性传递(同 M2c `RequestId` 手法,20 个调用点零改动)。 |
| `wf_history.PayloadVersion`(Task 1 定案) | `int` NOT NULL,实体初始化器 `= 1` + 列 `DefaultValue="1"`。语义:读取方按 `EventType + PayloadVersion` 解释 `PayloadJson`。无人显式写;只有某个 `EventType` 的 payload 形状变了,才在那一个写入点抬到 2。 |
| 加列的四库方言(Task 1 定案) | 全走 CodeFirst `InitTables`,**不写迁移脚本、不写回填 HostedService**。可空列单步 `ADD COLUMN NULL`(四库皆可);非空列**必须**带 `DefaultValue`,否则 PG/SqlServer 在有行的表上 `ADD COLUMN ... NOT NULL` 直接被拒——而空库 CI 与本机 SQLite 腿**永远看不见**这个错误。 |
| `ExecutionKey` 构成 | 待 Task 3 plan 定案(至少含 tenant/scope、instanceId、tokenId、nodeId、definitionVersionId——具体参与字段与拼接规则须像 M2c 的 `IdentityHash` 一样一次写死、写快照测试) |
| Handler 结果枚举(Task 2 定案) | `WfNodeExecutionResultType { Succeeded=1, RetryableFailure=2, ManualFallback=3, TerminalFailure=4 }`,**刻意无 0 值**(`default(枚举)` 非法,dispatcher `default:` 臂抛异常,杜绝「零初始化悄悄等于成功」);数值将进评审 §6.2 的 `ResultType` 列,**只追加不重排**。**不设 `Cancelled` 成员**。承载类型是 `sealed class WfNodeExecutionResult`(私有构造 + 四个静态工厂 + `OutputJson`/`Summary`/`ErrorCode`/`RetryAfter` 扁平 payload),**不是类型层次**——理由:§6.2 存的是四个扁平列,1:1 映射;类层次到同样四列要在持久化边界写一梯子 `is` 下转型,而 C# 的类层次 `switch` 本来也没有穷尽性检查。用 `sealed class` 而非 `record`:`record` 的 `with` 会留一条绕过工厂的后门。handler **不得**推进 token、写任务状态、自开数据库事务(AI 基石 §4.5/§4.7 硬约束)。 |
| 取消语义(Task 2 定案) | handler 抛 `OperationCanceledException` = 「这次没跑完,**应可被重新领取**」,**语义上不等于 `TerminalFailure`**(后者 = 永不重试)。二者方向相反,合并会让 Task 6 再也分不出「该重试吗」。Task 6 的异常处理**不得**把 OCE 归进任何一个结果分支,须单独识别并让 lease 过期/释放,走 Task 7 的崩溃恢复路径。「实例被外部撤销」不是 handler 的返回值维度——handler 压根不知道,由 dispatcher 在回写短事务里靠 fence/CAS 发现并丢弃结果。 |
| 结果枚举 vs 状态机(Task 2 定案) | `WfNodeExecutionResultType`(一次 attempt 的**答复**)与 Task 3 的 `WfNodeExecutionStatus`(`wf_node_execution.Status` **行状态**)是**两个不同类型**,不许合并、不许共用数值。一次 execution 多次 attempt,每次 attempt 一个结果,行状态是它们的聚合。 |
| handler 键与分发(Task 2 定案) | `IWorkflowNodeHandler.NodeType` 是 **`WfNodeType` 枚举**(非字符串、非 keyed DI)——要匹配的对端 `WfNode.Type` 本来就是枚举,用 string 等于强插一次 camelCase 往返,漂移只会在运行时表现为「找不到 handler」。分发 = `TryAddEnumerable` 多实现 + `GetServices<IWorkflowNodeHandler>().FirstOrDefault(h => h.NodeType == node.Type)`,与 `IAdminJob`/`DefaultJobHandlerResolver` 同款,不发明新范式。M3b 的 AI 节点走「往 `WfNodeType` **追加**成员」(注意 `WfNodeType` 带 `allowIntegerValues: true`,存量 `ModelJson` 可能有整数值 → 同样只追加不重排)。**第一条 DI 注册线由 Task 8 加,Task 2 零注册**(注册一个零实现的空枚举面等于死代码,还会让 `WorkflowReplaceabilityTests` 的「十件套」注释失真)。 |
| `WfNodeExecutionContext` 形状(Task 2 定案) | 14 个 `init` 字段(`ExecutionKey`/`InstanceId`/`TokenId`/`NodeVisitId`/`NodeId`/`NodeType`/`DefinitionVersionId`/`OrgId`/`StarterUserId`/`BusinessKey`/`NodeProps`/`VariablesJson`/`Attempt`/`DeadlineAtUtc`),**不含** SqlSugar 实体、`ISqlSugarClient`、`TimeProvider`、evidence(M3b 再加,用非 required)。节点配置传既有 `WfNodeProps`(纯 POCO,Webhook 配置位已在里面)——**dispatcher 必须传自己反序列化的版本快照实例,不得共享引擎 `ctx.Model` 上的活树节点**;handler 只读。变量传原始 `string? VariablesJson`,**实现必须对烂 JSON 免疫**(同 `IWfConditionEvaluator` 既有约定,本仓刻意不在中心处理这件事)。租户维度**只有 `OrgId`**(本仓无 tenant 原语,不凭空造 `TenantId`)。`Attempt` **1 基**,= 将写的 attempt 行 `AttemptNo` = 领取时 `AttemptCount + 1`(三处口径必须对齐,差一是最典型的静默 bug)。`DeadlineAtUtc` 是**绝对** `DateTimeOffset`,非相对 `TimeSpan`、非 `DateTime`(相对超时会在「领取→排队→开跑」之间失真;`DateTimeOffset` 从类型上消灭 Kind 歧义)。SPI 的 `DateTimeOffset` 与 Task 3 列的 `DateTime` 之间有一次转换,落点在 dispatcher,**别为了「统一」把 SPI 改成 `DateTime`**。 |
| `HandlerVersion`(Task 2 挂账) | 评审 §6.1 有此列,但 Task 2 零写入点,**刻意不进接口**。将来以**默认接口成员**(`string HandlerVersion => "1";`)追加,对已有实现零破坏;由 Task 3(建列)/ Task 6(填值)决定何时加。 |
| execution 状态机 | `Pending → Running → Succeeded`;失败可进 `RetryScheduled → Running`;`ManualFallback`;`Cancelled`/`Failed`(AI 基石 §4.6,Task 3 plan 落地成具体枚举值) |
| 事务边界 | 短事务领取(CAS lease/fence)→ 事务外调用 handler → 短事务落 attempt/result/token 推进/outbox,**不得**让远程调用发生在数据库事务内(AI 基石 §4.6 步骤 1–5,验收线 §4.8 明文要求) |
| lease/fence | 待 Task 3 plan 定案具体字段与领取 SQL 形状(参照 M2c `WfOperationReceipt`/`WfInstance.Version` 的 CAS 先例,不是发明新范式) |
| attempt 记录 | append-only,重试**不覆盖**旧 attempt(AI 基石 §4.5) |
| outbox | 结果提交后可靠触发通知/外部副作用;短事务与 execution 结果同提交(AI 基石 §4.6) |
| Webhook 超时/重试分类 | 待 Task 8 plan 定案(哪些 HTTP 状态码/异常归 `RetryableFailure`,哪些归 `TerminalFailure`,是否有的场景该转 `ManualFallback`) |
| 与人工任务的关系 | `ManualFallback` 时如何创建人工 `wf_task`——待 Task 6(dispatcher)plan 定案,复用 `EnterNodeOp.CreateTaskAsync` 还是新路径 |
| 范围外 | 不建 AI Decision(`wf_ai_decision`、provider adapter、policy、shadow mode——留给 M3b);不建并行网关(`ParentTokenId`/`ForkId`/join 表);不新增审批动词;不 port React 工作流页;不抽 web/web-react 共享层 |

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段的 Agent 重写,协调者转写进本节)

### Task 2 决策点定案

**1. `TerminalFailure` 与 `Cancelled` 不合并——`Cancelled` 根本不进结果枚举,只有四个成员。**
取消在 .NET 里已有自带通道:`CancellationToken` + `OperationCanceledException`。handler 里每个 `await xxx(cancellationToken)` 都免费抛 OCE,加一个 `Cancelled` 返回值等于给同一件事开两条路,handler 作者会分裂(有的抛有的返),dispatcher 两条都得处理——净增复杂度、零收益。更关键的是**语义方向相反**:`TerminalFailure` = 「永远做不成,不许重试」,而取消(宿主停机 / deadline 触发)= 「这次没跑完,**应该被重新领取**」,恰是 Task 7 崩溃恢复要覆盖的那条路。塞进一个成员,Task 6 就再也分不出「该重试吗」——危险的是**合并** `Cancelled` 进 `TerminalFailure`,而不是不加它。至于「实例被外部撤销」,handler 压根不知道,是 dispatcher 在回写短事务里靠 fence/CAS 发现并丢弃结果的,不是 handler 的返回值维度。

**2. Result 用「枚举 + 私有构造 + 四个静态工厂」的单一 `sealed class`,不用类型层次。**
决定性证据来自评审 §6.2:`wf_node_execution_attempt` 存的是 `ResultType`/`OutputSummary`/`ErrorCode`/`ErrorSummary` 四个**扁平列**。扁平 class 到这四列是 1:1 映射;抽象基类 + 4 个 sealed 子类到同样四列需要在持久化边界写一梯子 `is` 下转型——为了一个 C# 里本来就没有穷尽性检查的「判别联合」(类层次 `switch` 照样要 `default:` 臂)付这个价,不值。四个成员的 payload 集合本来也几乎重合。第三方 `OneOf` 直接出局(运行时依赖只有 SqlSugarCore + Microsoft.*)。
用 `sealed class` 而非 `record`:不需要值相等,且 `record` 的 `with` 会留一条 `Succeeded() with { Type = TerminalFailure }` 绕过工厂的后门。私有构造 + 四个静态工厂 → 非法组合(`Succeeded` 带 `ErrorCode`)在正规路径上构造不出来。仓内既有形状(`ApproverResolveContext`/`WfFormBindContext` 都是 `sealed class` + `required init`)与之一致。
枚举 `WfNodeExecutionResultType { Succeeded=1, RetryableFailure=2, ManualFallback=3, TerminalFailure=4 }` — **刻意无 0 值**:数值将进 §6.2 的 `ResultType` 列(同 `WfHistoryActorType` 的只追加不重排);0 空缺意味着 `default(枚举)` 非法,dispatcher `default:` 臂抛异常,杜绝「零初始化悄悄等于成功」。

**3. `WfNodeExecutionContext` 字段清单**(`sealed class`,全 `init`,投影自实体不含实体本身;形状照 `Abstractions/IWorkflowFormBinder.cs:17-25` 的 `WfFormBindContext`):

| 字段 | 类型 | 来源 |
|---|---|---|
| `ExecutionKey` | `required string` | Task 3 定构成;本轮**不透明值**,handler 只可原样用作 provider 幂等键 |
| `InstanceId` | `required long` | `WfInstance.Id` |
| `TokenId` | `required long` | `WfToken.cs:16` |
| `NodeVisitId` | `long?` | `WfToken.cs:51`(Task 1);可空是 Task 1 定案,不在这里收紧 |
| `NodeId` | `required string` | `WfNode.Id` |
| `NodeType` | `required WfNodeType` | `WfNode.Type`(`Schema/WfSchemaEnums.cs:17-26`) |
| `DefinitionVersionId` | `required long` | `WfInstance.cs:18` |
| `OrgId` | `long?` | `WfInstance` 继承的 `DataEntity.CreateOrgId` |
| `StarterUserId` | `required long` | `WfInstance.cs:25` |
| `BusinessKey` | `string?` | `WfInstance.cs:22` |
| `NodeProps` | `WfNodeProps?` | `WfNode.Props`(`Schema/WfNode.cs:19`) |
| `VariablesJson` | `string?` | `WfInstance.cs:66` 原样透传 |
| `Attempt` | `required int` | **1 基**;= 即将写的 attempt 行 `AttemptNo` = 领取时 `AttemptCount + 1` |
| `DeadlineAtUtc` | `required DateTimeOffset` | 绝对时刻 |

关键选择:
- **租户维度只有 `OrgId`**。本仓没有 tenant 原语,隔离锚点就是 `CreateOrgId`。不为「设计文档写了 tenant/org」凭空造 `TenantId`。
- **节点配置传 `WfNodeProps?`,不传 JSON 字符串**。`WfNodeProps` 是 `Schema/` 下纯 POCO(只依赖 `System.Text.Json`,非 SqlSugar 实体),且 Webhook 配置位已在里面(`Schema/WfNode.cs:82-83` 的 `WebhookUrl`)——Task 8 只需往既有并集加字段。传 JSON 等于再造一套平行配置表示 + 每次执行多一轮序列化往返。代价:`WfNodeProps` setter 可写,与「不可变快照」字面不符 → 靠纪律兜:**dispatcher 必须传自己反序列化的版本快照实例,不得把引擎 `ctx.Model` 里那棵活树上的节点递进来**。
- **变量传原始 `string? VariablesJson`,不做字典**。两处既有先例:`IWfConditionEvaluator.Evaluate(WfConditionExpr?, string? variablesJson)`(`Abstractions/IWfConditionEvaluator.cs:22`,注释明写「前端原样提交、后端从不校验,实现必须对烂 JSON 免疫」)与 `WfFormBindContext.VariablesJson`。改成 `IReadOnlyDictionary` 会逼 dispatcher 决定「烂 JSON 怎么办」——本仓刻意不在中心处理这件事。
- **「证据快照」本轮不加字段**。evidence/RAG 是 M3b 的东西,现零消费者。往 `sealed class` 追加 `init` 属性是非破坏的,M3b 加即可。
- **`Deadline` 是绝对时刻、`DateTimeOffset`**。相对超时会在「领取 → 排队 → 真正开跑」之间失真;评审 §6.1 列名也是 `DeadlineAtUtc`。用 `DateTimeOffset` 而非 `DateTime`:评审 §六 收尾要求时间字段统一 UTC 语义、`Kind` 不许含糊;本仓持久化业务时间戳走 `GetLocalNow().DateTime`、技术性时刻走 `GetUtcNow()`。SPI 不是持久化列,不受 `DateTime` 列约定绑架,`DateTimeOffset` 从类型上消灭 Kind 歧义。handler 要相对超时自己算 `DeadlineAtUtc - TimeProvider.GetUtcNow()`。
- **不放 `TimeProvider`、不放 `ExecutionId`**。前者 handler 从 DI 拿(`WorkflowSetup.cs` 已 `TryAddSingleton(TimeProvider.System)`);后者是 Task 3 才存在的行主键,幂等键 `ExecutionKey` 已够用。

**4. 接口 = `WfNodeType NodeType { get; }` + 一个 `ExecuteAsync`,无 `CanHandle`、无 keyed DI、无抽象基类。**
```csharp
public interface IWorkflowNodeHandler
{
    WfNodeType NodeType { get; }
    Task<WfNodeExecutionResult> ExecuteAsync(WfNodeExecutionContext context, CancellationToken cancellationToken);
}
```
- **键用 `WfNodeType` 枚举而非 `string`**:要匹配的对端 `WfNode.Type` 本来就是枚举;用 string 等于强插一次 `WfNodeType.Webhook ↔ "webhook"` 往返,而那个 camelCase 是 `CamelCaseEnumConverter` 的 JSON 表示,悄悄漂移只会在运行时表现为「找不到 handler」。节点类型不是开放集合(不像 `IApproverProvider.Key` 那种消费者要加 HRBP 的场景)——消费者今天也无法在定义 JSON 里表达枚举外的节点类型,发布校验会拒。M3b 的 AI 节点走「往 `WfNodeType` **追加**成员」。
- **dispatcher 找 handler**:`TryAddEnumerable` 注册多实现 + `GetServices<IWorkflowNodeHandler>().FirstOrDefault(h => h.NodeType == node.Type)`,与 `IAdminJob` + `DefaultJobHandlerResolver.cs:15-16` 一模一样。`IAdminJob` 类注释里本仓已把这个选择写死过一次:「不用 keyed DI:`TryAddEnumerable` 自带按实现类型防重语义、六件套契约现成」。不发明新范式。
- **本轮不定义 `IWorkflowNodeHandlerResolver`**:零调用者的一层间接。Task 6 若真需要「整体替换分发策略」的缝,那时按 `IJobHandlerResolver` 先例加,非破坏。
- **`Task<>` 不用 `ValueTask<>`**(设计文档写 `ValueTask`,但明说「命名可随仓内规范调整」):仓内 SPI 无一例外返回 `Task`(`IApproverProvider`/`IWorkflowNotifier`/`IWorkflowEngine`/`IWorkflowFormBinder`/`IAdminJob`),而 handler 永远做真 I/O,`ValueTask` 省的分配是噪声。
- **`cancellationToken` 不给 `= default`**:同 `IAdminJob.ExecuteAsync`。这个 SPI 里 token 就是 deadline 通道,漏传是 bug,不该被默认值掩盖。
- **不做 `WorkflowNodeHandlerBase`**:今天零共享逻辑、一个实现。Task 8 的 Webhook 与 M3b 的 AI 真长出共同步骤(deadline 计算、错误分类)再抽。接口成员谈不上 `virtual`;可替换性纪律落在**实现类**上——Task 8 的 `WebhookNodeHandler` 方法必须 `virtual`。
- **刻意不加 `HandlerVersion`**(评审 §6.1 有此列):今天零写入点。将来作为**默认接口成员**(`string HandlerVersion => "1";`)追加,对已有实现零破坏——升级路径明确,所以现在不做。

**5. 本 Task 不碰 `WorkflowSetup.cs`,零 DI 注册。**
Task 2 交付零个生产 handler,`TryAddEnumerable` 一个空集合等于一行死代码。第一条注册线由 **Task 8**(Webhook)加。`FakeNodeHandler` 是测试替身,**绝不进生产 DI**。副作用是好的:`WorkflowReplaceabilityTests` 的「十件套」保持 10 条、类注释保持准确,本 Task 的 `git diff --stat` 只有 3 个新文件。

**6. `FakeNodeHandler` 放测试程序集。**
本里程碑消费它的地方(Task 6 dispatcher 测试、Task 7 全链路+崩溃恢复、Task 9 四库套件)全在 `TenonAdmin.Tests` 一个程序集内,跨文件共享免费,放产品包换不来任何东西。反向代价是实打实的:内核包里躺一个「可配置返回任意结果」的 handler,消费者一旦误注册就会在生产里静默短路掉某个节点类型——支持负担。升级路径:哪天真要给消费者用,它约 40 行、复制即可,或另发 `TenonAdmin.Workflow.Testing` 包。落点 `backend/tests/TenonAdmin.Tests/WfFakeNodeHandler.cs`,类名 `FakeNodeHandler`,`internal sealed`。

**7. 一个文件,放 `Abstractions/`,不新建 `Execution/` 目录。**
全包命名空间是平的 `TenonAdmin.Workflow`,所以只有目录要选。SPI 接口一律在 `Abstractions/`;且两个最近先例都把 SPI 与其上下文类型放在**同一文件**:`IWorkflowFormBinder.cs` 装 3 个类型,`IApproverProvider.cs` 装 4 个类型。照办:`Abstractions/IWorkflowNodeHandler.cs` = 接口 + Context + Result + ResultType 枚举(约 150 行含详注;`IApproverProvider.cs` 是 93 行/4 类型,量级一致)。`Execution/` 留给 Task 3/6 真长出 dispatcher 时再议——本轮不预建。

### 改动清单

新建 3 个文件,**改动 0 个既有文件**。

| 路径 | 内容 |
|---|---|
| `backend/src/TenonAdmin.Workflow/Abstractions/IWorkflowNodeHandler.cs` | **新建**。`IWorkflowNodeHandler` 接口、`WfNodeExecutionContext`(sealed class,14 个 init 属性)、`WfNodeExecutionResult`(sealed class,私有构造 + 4 静态工厂)、`WfNodeExecutionResultType`(枚举 1–4) |
| `backend/tests/TenonAdmin.Tests/WfFakeNodeHandler.cs` | **新建**。`internal sealed class FakeNodeHandler : IWorkflowNodeHandler`,可配置返回哪种结果 |
| `backend/tests/TenonAdmin.Tests/WfNodeHandlerContractTests.cs` | **新建**。12 条,见测试清单 |

**明确不改**:`WorkflowSetup.cs`、`EnterNodeOp.cs`、`WorkflowEngine.cs`、`WfExecutionContext.cs`、任何实体、任何 `Schema/` 文件(含 `WfNodeType` 枚举——`Webhook` 成员已存在,不加不改)、任何控制器、`web/`、`web-react/`、`site/`、任何 `docs/`。协调者核对:应为 **3 files changed,全部 insertion,0 deletion**。

### 实现步骤

**步骤 1 — 建 `Abstractions/IWorkflowNodeHandler.cs`。** 顺序:枚举 → Result → Context → 接口。
```csharp
namespace TenonAdmin.Workflow;

public enum WfNodeExecutionResultType
{
    Succeeded = 1, RetryableFailure = 2, ManualFallback = 3, TerminalFailure = 4,
}

public sealed class WfNodeExecutionResult
{
    private WfNodeExecutionResult() { }
    public required WfNodeExecutionResultType Type { get; init; }
    public string? OutputJson { get; init; }        // 仅 Succeeded 有意义
    public string? Summary { get; init; }           // 落 attempt.OutputSummary / ErrorSummary
    public int? ErrorCode { get; init; }            // 失败/回退时的 48xxx 或 handler 自有码
    public TimeSpan? RetryAfter { get; init; }      // 仅 RetryableFailure;null = 由 dispatcher 退避策略决定

    public static WfNodeExecutionResult Succeeded(string? outputJson = null, string? summary = null) => new()
        { Type = WfNodeExecutionResultType.Succeeded, OutputJson = outputJson, Summary = summary };
    public static WfNodeExecutionResult RetryableFailure(int? errorCode = null, string? summary = null, TimeSpan? retryAfter = null) => ...;
    public static WfNodeExecutionResult ManualFallback(int? errorCode = null, string? summary = null) => ...;
    public static WfNodeExecutionResult TerminalFailure(int? errorCode = null, string? summary = null) => ...;
}
```
注释必须写进的几条(它们是这个 Seam 的全部价值,不是装饰):
- handler **不得**推进 token、写任务状态、自开数据库事务(AI 基石 §4.5/§4.7);只返回结果,由 dispatcher 在短事务里落地。
- 取消走 `OperationCanceledException`,**没有** `Cancelled` 成员;OCE 不等于 `TerminalFailure`。
- `WfNodeExecutionResultType`(handler 的答复)与 Task 3 的 `WfNodeExecutionStatus`(行状态机)是**两个不同类型**,不许合并——一次 execution 多个 attempt,每个 attempt 一个结果,行状态是它们的聚合。
- 枚举数值将进 §6.2 的 `ResultType` 列 → 只追加不重排。

Context:
```csharp
public sealed class WfNodeExecutionContext
{
    public required string ExecutionKey { get; init; }
    public required long InstanceId { get; init; }
    public required long TokenId { get; init; }
    public long? NodeVisitId { get; init; }
    public required string NodeId { get; init; }
    public required WfNodeType NodeType { get; init; }
    public required long DefinitionVersionId { get; init; }
    public long? OrgId { get; init; }
    public required long StarterUserId { get; init; }
    public string? BusinessKey { get; init; }
    public WfNodeProps? NodeProps { get; init; }
    public string? VariablesJson { get; init; }
    public required int Attempt { get; init; }
    public required DateTimeOffset DeadlineAtUtc { get; init; }
}
```
类注释写:不含 SqlSugar 实体与 `ISqlSugarClient`(硬约束,有结构化断言守着);`VariablesJson` 原样透传前端提交、**实现必须对烂 JSON 免疫**(措辞对齐 `IWfConditionEvaluator`);`NodeProps` 是快照实例,handler 只读、dispatcher 不得共享引擎 `ctx.Model` 上的活实例;`Attempt` 1 基;`ExecutionKey` 是不透明值(构成 Task 3 定)。

**步骤 2 — 建 `WfFakeNodeHandler.cs`。**
```csharp
internal sealed class FakeNodeHandler(
    WfNodeExecutionResult result,
    WfNodeType nodeType = WfNodeType.Webhook) : IWorkflowNodeHandler
{
    public WfNodeType NodeType => nodeType;
    /// <summary>调用计数,供 Task 6/7 断言「同一 ExecutionKey 只调一次」。</summary>
    public int CallCount { get; private set; }
    /// <summary>最后一次收到的上下文,供 Task 6 断言快照投影正确。</summary>
    public WfNodeExecutionContext? LastContext { get; private set; }

    public Task<WfNodeExecutionResult> ExecuteAsync(WfNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();   // 取消走异常,不走返回值
        CallCount++;
        LastContext = context;
        return Task.FromResult(result);
    }
}
```
不做「延迟多久」「抛什么异常」之类旋钮——Task 7 真要模拟慢调用/异常时再加,那时才知道要什么形状。

**步骤 3 — 建 `WfNodeHandlerContractTests.cs`**(见测试清单)。
**步骤 4 — 跑闸门**,确认 291/291。

### 测试清单

**不值得写的**(明确列出,防止凑数):`Assert.NotNull(new WfNodeExecutionContext{...})`;「接口只有一个方法」的反射断言;逐个属性 `Assert.Equal(x, ctx.X)` 的 getter/setter 复读;给 `WfNodeExecutionResult` 写相等性测试(它不需要相等语义)。

新建 `backend/tests/TenonAdmin.Tests/WfNodeHandlerContractTests.cs`,**+12 条**:

| # | 测试 | 测什么 / 变异检验 | 条数 |
|---|---|---|---|
| 1 | `Context_exposes_no_sqlsugar_entity_or_session` | **本 Task 最有价值的一条**。反射 Context 全部公开属性,断言无一属性类型 (a) 派生自 `TenonAdmin.SqlSugar.PrimaryId`、(b) 可赋给 `SqlSugar.ISqlSugarClient`、(c) 命名空间以 `SqlSugar` 开头。变异:加 `public WfInstance Instance { get; init; }` → 红。守的正是 Task 6「顺手把实体塞进去」的腐化路径 | 1 |
| 2 | `Result_factories_set_the_matching_result_type` | 四个工厂各自 `Type` 与名字对上,且 `Succeeded()` 的 `ErrorCode`/`RetryAfter` 为 null。变异:把 `ManualFallback` 工厂的 `Type` 复制粘贴成 `TerminalFailure` → 红。静默、高爆炸半径的复制粘贴 bug,dispatcher 会忠实照做 | 1 |
| 3 | `ResultType_numeric_values_are_pinned` | `(int)Succeeded==1 … TerminalFailure==4`,且**不存在 0 值成员**。数值将进 §6.2 `ResultType` 列 → 重排就是破坏存量数据 | 1 |
| 4 | `Result_has_no_public_constructor` | `GetConstructors()` 为空 → 只能走工厂,非法组合构造不出来。变异:补个 public 无参构造 → 红 | 1 |
| 5 | `FakeNodeHandler_returns_the_configured_result`(`[Theory]` × 4) | 四种结果各一行,确认测试仪器本身可信——Task 6/7/9 全押在它上面,仪器坏了后面全是假绿 | 4 |
| 6 | `FakeNodeHandler_throws_when_token_already_cancelled` | 传已取消的 token → `OperationCanceledException`,**不是**返回某个结果。把决策点 1 的契约变成可执行断言,Task 6 写 catch 时有东西挡着 | 1 |
| 7 | `Handler_node_type_matches_by_enum` | 一组 `IEnumerable<IWorkflowNodeHandler>` 里按 `NodeType` 用 `FirstOrDefault` 能选中,选不中返回 null(不抛)。锁住 Task 6 的查找语义 | 1 |
| 8 | `Context_deadline_is_absolute_utc` | `DeadlineAtUtc` 是 `DateTimeOffset` 且构造后 `Offset == TimeSpan.Zero`;外加断言类型不是 `DateTime`。防「有人改成 `DateTime` 丢掉 Kind」与「有人改成 `TimeSpan` 相对超时」 | 1 |
| 9 | `Context_variables_json_is_raw_passthrough` | 传一段**非法 JSON** 进 `VariablesJson`,构造上下文不抛;确认契约是「原样透传、由 handler 自己免疫」,而非在这层解析 | 1 |

**基线 279 → 预期 291。**

不新增 `WorkflowReplaceabilityTests` 条目:本 Task 零 DI 注册,「十件套」保持 10。第一条 handler 可替换性用例归 Task 8(注意那时是 `TryAddEnumerable` 语义,消费者是**追加**而非**覆盖**,与 `IApproverProvider` 同款,不能照抄 `TryAddScoped` 那 10 条的写法)。

### 陷阱

1. **最大的越界风险:顺手接引擎。** `EnterNodeOp.ExecuteAsync` 的 `switch` 今天对 `WfNodeType.Webhook` 走 `default:` 抛 `NodeTypeUnsupported`(48008,`Engine/Operations/EnterNodeOp.cs:68-70`)。Task 2 **不改这一行**——webhook 节点跑起来仍然 48008,这是正确的中间状态。executor 一旦「顺便让它能跑」就是把 Task 3–8 全干了。
2. **不碰 `WorkflowSetup.cs`。** 加任何注册都会让 `WorkflowReplaceabilityTests` 的「十件套」类注释(`:8-14`)与实际不符,且给一个零实现的接口注册枚举面。
3. **`FakeNodeHandler` 绝不进生产 DI**,也不进 `TenonAdmin.Workflow` 程序集。
4. **`WfNodeType` 也要「只追加不重排」。** 它带 `[JsonConverter(CamelCaseEnumConverter)]` 且 `allowIntegerValues: true`(`Schema/WfSchemaEnums.cs:10,17-26`)——存量 `ModelJson` 里理论上可能有整数值。M3b 加 `AiDecision` 必须追加在 `Webhook` 之后。
5. **`DateTimeOffset`(SPI) ↔ `DateTime`(Task 3 的 `DeadlineAtUtc` 列)有一次转换**,落点在 dispatcher。别有人为了「统一」把 SPI 改成 `DateTime`——那正是评审 §六 收尾警告的 Kind 丢失。转换点在 Task 6 写明。
6. **`Attempt` 三处口径必须对齐**:context 的 `Attempt`(1 基) = §6.2 `AttemptNo` = 领取时 §6.1 `AttemptCount + 1`。三处差一是最典型的静默 bug,Task 3/4/6 plan 时回头核这条契约行。
7. **`NodeProps` 可变**,与「不可变快照」字面不符。已用「dispatcher 传自建快照 + handler 只读」兜住;review 若判定不够,升级路径是给 `WfNodeProps` 加只读投影——但那要改 `Schema/`,不在本 Task。
8. **`required` 属性的加减是破坏性的**:Context 是 `sealed class`,追加 `init` 属性非破坏,但追加 `required` 属性会打断 dispatcher 的对象初始化器(只有一个内部调用点,可控)。M3b 加 evidence 时用非 required。
9. **Task 8 的 `WebhookNodeHandler` 方法必须 `virtual`**(根 `CLAUDE.md` 可替换性纪律)。Task 2 的接口本身谈不上 virtual,别在 review 时误判成缺陷。
10. **序列化**:`WfNodeExecutionResult` 私有构造 + 无参数化构造 → `System.Text.Json` **反序列化不了**。这是有意的(它从不整体持久化,只投影进 §6.2 四个扁平列)。哪天真要序列化,加 `[JsonConstructor]` 而不是放开 public 构造。

### 闸门

```
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test  backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"
```
预期:build 0 错误;test **291/291 通过,失败 0**(基线 279 + 12)。
**不需要前端闸门**:零实体、零控制器、零 DTO、零端点 → OpenAPI 契约不变。也不需要四库腿——纯类型定义无 DDL、无 SQL。

## Tasks

> 任务顺序 = 依赖顺序。编号稳定;`## Log` 引用任务号。

- [x] **1. `NodeVisitId` 贯穿 + `wf_history` 补字段**:`WfToken`/`WfTask`/`WfHisTask`/`WfHistory`/`WfCc` 加 `NodeVisitId`(每次进新节点生成,停留期间不变,与 `EnterNodeOp` 的 token 级 CAS 同一事务写入);`wf_history` 补 `TokenId`/`Sequence`(实例内单调递增,并发写入方式待 plan 定案)/`ActorType`/`ActorUserId`/`PayloadVersion`(`RequestId` 已在 M2c 做完,不重做)。这是后续所有 execution 相关表「稳定身份」的地基,必须先做。
- [ ] **2. `IWorkflowNodeHandler` SPI + Context/Result 类型**:定义最小 Interface(`ExecuteAsync(WfNodeExecutionContext, CancellationToken) -> WfNodeExecutionResult`);`WfNodeExecutionContext` 只含不可变快照(tenant/org、定义版本、实例、token、节点配置、变量/证据快照、`ExecutionKey`、attempt、deadline),不泄漏 SqlSugar 实体/DB session;`WfNodeExecutionResult` 是 `Succeeded`/`RetryableFailure`/`ManualFallback`/`TerminalFailure` 的显式判别联合或枚举+payload。附一个 `FakeNodeHandler` 参考实现(可配置返回哪种结果,供后续 Task 当测试替身)。**本 Task 不接入引擎**,纯类型/接口定义。
- [ ] **3. `wf_node_execution` 实体 + `ExecutionKey` 唯一约束 + lease/fence CAS 领取**:新增实体与表(字段参照数据库评审 §六 6.1),`ExecutionKey` 唯一索引;短事务领取逻辑(CAS 更新 lease owner/expiry + fence token 递增),仿 M2c `WfOperationReceipt`/`WfInstance.Version` 的先例。本 Task 交付「能领取、能占位」,不接调度器。
- [ ] **4. `wf_node_execution_attempt` 实体 + append-only 记录**:新增实体与表(字段参照 §六 6.2),写入路径只增不改不删;至少一条测试证明「重试不覆盖旧 attempt,而是新增一行」。
- [ ] **5. `wf_outbox` 实体 + 可靠派发骨架**:新增实体与表(字段参照 §六 6.3);与 execution 结果同一短事务提交;本 Task 交付「写得进去、状态可查询」,实际派发消费逻辑视 Task 6 需要决定是否本 Task 一并做还是独立。
- [ ] **6. Execution dispatcher(领取 → 调 handler → 落结果)**:整合 Task 2–5——短事务领取 execution(lease/fence)→ 事务外调用 `IWorkflowNodeHandler` → 按结果短事务落 attempt + 推进 token(`Succeeded`)/ 安排重试(`RetryableFailure`,写 `NextRetryAt`)/ 建人工任务回退(`ManualFallback`,决定是否复用 `EnterNodeOp.CreateTaskAsync`)/ 终止(`TerminalFailure`)+ outbox。覆盖「远程调用不在事务内」「同 `ExecutionKey` 只推进一次」两条核心不变量的测试。
- [ ] **7. Fake Handler 全链路 + 崩溃恢复**:用 `FakeNodeHandler` 把 Task 6 的 dispatcher 端到端打通,覆盖四种结果路径各至少一条测试;崩溃恢复测试(lease 过期后可被重新领取,重新领取后不会对已经成功推进的 execution 重复推进——用直接操纵 DB 时间戳模拟过期,同 M2c `WfTimeoutTests` 的先例手法,不建 `FakeTimeProvider`)。
- [ ] **8. Webhook Handler(首个真实 Adapter)**:节点类型 `webhook`,配置 schema(URL/method/headers/超时);HTTP 调用在事务外发起(验证「远程调用不在数据库事务内」这条硬约束的具体落地);超时/网络异常/状态码分类映射到 `RetryableFailure`/`TerminalFailure`/`ManualFallback`(具体规则在 plan 阶段定案并写进语义契约表)。
- [ ] **9. 四库持久化契约套件**:新建 `WfNodeExecutionContractTests`(或同级),同一套用例经 `TestDb.DbType` 在四库 CI 腿各跑:①`ExecutionKey` 唯一性;②lease/fence CAS 竞争(仿 M2c Task 8 的 PG 方言陷阱排查方法,注意排查是否有类似问题);③事务回滚不残留半推进状态;④outbox 契约。目标条数与具体清单在 plan 阶段列(参照 M2c Task 8 的 12–20 条量级)。SqlServer PR 腿沿用 `TEST_FILTER` 机制评估是否纳入。
- [ ] **10. 验收 + 收尾**:核对 `## DONE-CONDITION` 全部条目(含「四库 CI 矩阵」——同 M2c Task 10 先例,本机可能仍需 push dev 换 CI 信号,届时用 `AskUserQuestion` 问用户,不擅自 push);若确有 API 面变化跑 `gen:api` 校验双模板 SHA256;把最终语义(`ExecutionKey` 构成、状态机、lease/fence 字段、Webhook 分类规则等)回写到 `docs/workflow/workflow-database-design-review-2026-08-24.md` 与 `elsa3-slickflow-ai-reference-2026-08-23.md`(README 维护规则要求的「契约性决定必须回写」)。

## Findings

> P1/P2 与跨任务约束。exec 修完打勾;P3 可挂账。

### Task 1 review(Round 3,Opus 自审 + 变异测试)

**P1:无。** 另主动排查三条最可能出 P1 的路,结论均干净:①计数器被整对象回写覆盖——全仓 `WfInstance` 的两处整对象更新都带 `UpdateColumns` 白名单(不含 `HistorySeq`),不存在陈旧回写撞号;②绕过分配器的 `wf_history` 插入点——`grep "new WfHistory"` 全仓仅 5 处,全部经 `NextAsync`/`WriteSystemRowAsync`,无漏网;③`WriteSystemRowAsync` 嵌套事务——4 个系统写入点均不在引擎事务内,不会内层提前提交外层。

- [x] **P2-1｜`His_task_carries_the_visit_id_of_the_task_it_closes` 名不副实**(Round 4 已修,commit `98c2837`:产品代码不动;`WfHisTask.NodeVisitId` 的 XML 注释改写为「今天两者在所有写入点都相等,取 `Task` 是为将来解耦,此区分目前无法用测试验证」;测试更名 `His_task_visit_id_matches_the_visit_that_created_the_task`,注释降级为实际可验证的表述)(`WfNodeVisitIdTests.cs:194-272`)。变异 M8 把三处 `WfHisTask.NodeVisitId` 全改成从 `ctx.Token` 拷,**全量 278 条全绿**。深查确认这不只是「没测到」而是**当下两种写法语义等价**:三个 Op 的 `wf_his_task` 插入都发生在本次 Agenda 的 `EnterNodeOp` 推进 token **之前**,且 `ctx.Token` 一律由 `task.TokenId` 加载,所以插入那一刻 `Task.NodeVisitId ≡ ctx.Token.NodeVisitId`(退回也不例外:`ReturnTaskOp` 的 token `NodeId` 更新在 :114,晚于 :82 的插入)。**修法(采纳 review 倾向的 (a) 案)**:代码保持从 `Task` 拷(两者中更安全的那个),但把 `WfHisTask.NodeVisitId` 的 XML 注释改成「今天两者等价,取 `Task` 是为了将来 token 与任务解耦时仍然正确」,并把该测试的命名/注释从「携带它关闭的那件待办的访问 Id」降级为「与当次访问一致」。**不要留下一个台账宣称、测试却无法验证的保证。**(b) 案「构造真能分叉的场景」已实测构造不出——当前引擎不产生「token 已移动而旧 task 仍可完成」的状态。
- [x] **P2-2｜`Sequence_starts_at_one_strictly_increases_and_never_repeats` 依赖 `ORDER BY CreateTime` 的并列顺序,四库 CI 有翻红风险、本机 SQLite 腿看不见**(`WfHistoryIdentityTests.cs:45-52` + helper `HistoryOf` :293-299)。`CreateTime` 是裸 `DateTime`(`BaseEntity.cs:49`,无 `ColumnDataType`),SqlSugar 在 MySQL 上映射成**秒精度** `datetime` → 同一条命令写的十几行全部并列 → 并列行返回顺序非确定。本机 SQLite 实测 `CreateTime` 全部互不相同,所以**这条腿永远绿**,风险只在 mysql/postgres/sqlserver 腿上偶发红。**修法**:把三条顺序敏感断言换成一条顺序无关且更强的 `Assert.Equal(Enumerable.Range(1, sequences.Count).ToList(), sequences.OrderBy(x => x).ToList());`(review 已实测通过),它**顺带补上目前完全没断言的「无间隙」不变量**——`Sequence` 的 XML 注释白纸黑字写了「无间隙」,现有断言只覆盖「单条命令内连续」。若还想保留「写入顺序」语义,改用 `.OrderBy(h => h.Id)`(雪花单进程内单调),**别用 `CreateTime`**。 **(Round 4 已修,commit `98c2837`:四条顺序敏感断言换成一条 `Assert.Equal(Enumerable.Range(1, n), sequences.OrderBy(x => x))`,一并钉住「从 1 起/无重复/无间隙」;helper `HistoryOf` 的排序键从 `CreateTime` 换成 `Id`。协调者独立变异验证:`NextAsync` 改成 `+ 3` → 该测试在 :49 转红,失败 3/通过 5;复原后闸门 279/279。)**

**P3(挂账,不阻塞勾选):**

- **P3-1｜exec 那处「自主弱化」判定为合理,非掩盖缺陷。** Plan 测试 2 原要求 `wf_cc` 值与 token 相等,但 `EnterCcAsync` 末尾**无条件** `Agenda.Plan(new TakeTransitionOp(Node))`(`EnterNodeOp.cs:166`)——抄送节点在结构上永不停留 token,**换任何模型原断言都不可能成立**。替代断言链保留了鉴别力(`Assert.NotEqual(cc1Enter.NodeVisitId, token.NodeVisitId)` 钉住「每进一个节点重新生成」,M1/M2 正因它转红)。此条记录在案,免得下一轮又被当成缩水重开。
- **P3-2｜`Cc_row_keeps_its_first_visit_id_across_resubmit` 目前不空转但没钉住前提。** 探针实测重提后 `cc1` 确有 2 条 `NodeEnter`、两次访问 Id 不同、`wf_cc` 仍单行。但测试只断言 `Assert.Single(ccRows)` + 值相等;将来若重提不再重走 cc 链,会静默变空转还照样绿。建议补 `Assert.Equal(2, ccEnters.Count)`。
- **P3-3｜「停留期间不变」只覆盖了会签一种停留。** `WfToken.NodeVisitId` 注释点名「会签未满票、转办、催办」三种,测试只测了会签。转办/催办由「不经过 `EnterNodeOp`」在结构上保证,风险低,但注释宣称三条只验了一条。
- **P3-4｜每条历史多两次往返,且把同实例并发命令整体串行化。** `AppendHistoryAsync` 每行多一条 UPDATE + 一条 SELECT(一条命令 3–20 条历史 → 多 6–40 次往返);`NextAsync` 从**第一条历史**起就对 `wf_instance` 取行排他锁持有到提交(此前只有终态转换才 `ClaimInstanceAsync` 锁实例)。这是「每实例无间隙序号」的固有代价,但注释**没写明它顺带把并行网关的多 token 命令串行化了**。建议补进 `WfInstance.HistorySeq` 的注释,作为已知天花板。
- **P3-5｜`NextAsync` 读回不到行时静默返回 0**,而 0 正是「升级前旧行」的哨兵值,两者不可区分。今天不可达(`wf_instance` 无删除路径 + 已加 `ClearFilter<IOrgScoped>`),但 `ClearFilter` 没清 `ISoftDelete`。廉价加固:读回 `<= 0` 直接抛。
- **P3-6｜`DefaultValue` 实证核对:四个非空新列全部写了,无遗漏。** `grep` 实证:`WfHistory.Sequence="0"`(:66)、`WfHistory.ActorType="0"`(:73)、`WfHistory.PayloadVersion="1"`(:86)、`WfInstance.HistorySeq="0"`(:91)。

**射程限制(如实记录,不拿「测不到」当「不用测」):**

- **R1｜并发交错(变异 M3/M5 均绿)。** 单进程 + SQLite 构造不出「两事务在同一实例上交错分配序号」。M3(原子递增改先读后写)与 M5(去掉短事务)都是**只在并发下才错**的变异,本轮必然绿——这两个不变量目前**没有自动化防线**,仅靠代码评审与注释。**兜底**:`docker-smoke.yml` 的 `multi` 腿是现成的两副本环境,Task 9 或 Task 10 应加「两副本并发同实例写历史 → 序号无重复」的真跑断言;否则到里程碑结束仍是纯人工保证,须在 DONE 判定里明写。
- **R2｜四库建列方言(变异 M6 绿)。** 本机与 CI 四条腿**全是空库** CodeFirst 建表,「`ADD COLUMN ... NOT NULL` 在有行的表上被 PG/SqlServer 拒绝」这个错误**四条腿全都看不见**。M6 实测:去掉 `Sequence` 的 `DefaultValue` 后 SQLite 29 测全绿。**兜底**:`WfPersistenceContractTests` 已有两条同型的「存量行」契约测试(`Legacy_instance_rows_read_null_for_completed_time`、`Legacy_history_rows_read_null_for_request_id_not_empty_string`),**Task 9 必须照抄那个姿势**加一条「先建旧结构 + 插行 → 再 `InitTables` → 四个非空新列读到 0/0/1/0」。不写这条,升级路径的正确性就完全押在人工评审上。
- **R3｜跨机构数据范围(变异 M4 绿,但原因与预判不同)。** 预判「没红=测试 11 没走后台路径」**是错的**:测试 11 确实走了后台路径,但 `HttpContextDataScopeContext.Current` 在无 HttpContext 时返回 `Unrestricted`(`HttpContextDataScopeContext.cs:21-23`),全局 `IOrgScoped` 过滤器整体恒真,后台路径上根本没有机构限制可被 `ClearFilter` 清掉——**`ClearFilter` 在这条路上是防御性的,不是承重的**。它真正承重的是 HTTP 路径上跨机构审批,而全仓 workflow 测试没有跨机构实例。不是 Task 1 新引入的空白(`WorkflowEngine` 既有 7 处 `ClearFilter` 同样没被钉住),但缺口大了一格。挂里程碑级别。
- **R4｜`WfHisTask` 取值来源(变异 M8 绿)。** 见 P2-1:当前引擎结构下两种写法等价,**不是测试没写好,是没有可观测差异可测**。将来 token 与 task 生命周期解耦后才变得可测。

**顺带核实的非问题**:`NextAsync` 的 UPDATE 没加 `ClearFilter` 是对的——仓内既有同形写法(`ClaimInstanceAsync:128`、`ClaimTokenAsync:163`、`WfCompletedTimeBackfill.cs:65`)全部如此且一直工作,SqlSugar 的全局 QueryFilter 不作用于 `Updateable`。

## Log

| 轮次 | 阶段 | 摘要 |
|---|---|---|
| 0 | draft | 起草台账。M2c + 过渡步骤已收口(commit `9589c4d`),基线 264/264。本台账新增「分析/审查用 Opus、执行用 Sonnet」的委派纪律(见 `## Loop 纪律` 表)。下一步 Round 1 Task 1 plan。 |
| 1 | plan | Task 1 plan 完成(Opus 子代理)。6 个决策点全部定案:NodeVisitId=可空雪花/唯一生成点 EnterNodeOp;Sequence=`wf_instance.HistorySeq` 事务内原子递增+读回(**本轮不建唯一索引**,理由入契约);新枚举 `WfHistoryActorType`(驳回 `Reminder`);PayloadVersion 默认 1 无人显式写;TokenId 不存在写不出的情况;非空列必须带 `DefaultValue` 否则 PG/MSSQL 存量升级炸。改动清单 15 产品文件 + 3 测试文件,预期 264→279。已知会红:`WorkflowMultiLeaderSnapshotTests.cs:657` 构造参数个数。下一步 Round 2 Task 1 exec。 |
| 2 | exec | Task 1 exec 完成(executor/sonnet)。commit `fd00b05` "feat(workflow): add node-visit identity and history sequence/actor columns",19 文件 +1344/-13,与 Plan 改动清单零偏差。协调者独立复跑闸门:build 0 错误(13 警告全在 Services/Rbac 的 XML cref,既有、非本次引入,Workflow 0 警告);test **279/279 通过、失败 0**(基线 264 + 15 精确吻合)。子代理自报的唯一判断项:测试 2 的抄送断言改为「`wf_cc` 行对自己那次访问的 `wf_history(NodeEnter+CcSent)` 自洽」而非「等于当前 token」——因为 HTTP 响应返回时 token 已越过 cc 节点。留给 review 阶段确认这个弱化是否掩盖了缺陷。**不勾选**,下一步 Round 3 Task 1 review。 |
| 3 | review | Task 1 review 完成(Opus 自审 + 8 变异点 + 2 自加探针,26 分钟)。**0×P1**,2×P2,6×P3,4 条射程限制。转红的:M1(不生成 NodeVisitId)5/15、M2(生成不落库)4/15、M7a(ActorType 写死 Unknown)2/15、M7b(TokenId 写死 null)2/15 —— 生成点/落库/传递链均被真实覆盖。仍绿的:M3(原子递增改先读后写)、M5(去短事务)——单进程测不到并发,记 R1;M6(去 DefaultValue)——空库测不到存量升级,记 R2 并要求 Task 9 兜底;M4(去 ClearFilter)——原因与预判不同(后台无 HttpContext 时数据范围本就 Unrestricted),记 R3;M8(WfHisTask 改从 token 拷)——当前引擎下两种写法语义等价,升为 P2-1。协调者交叉验证工作区复原干净(status 只剩 TestResults/,diff 空,HEAD 仍 ca175a2)。**不勾选**,下一步 Round 4 修 P2-1/P2-2。 |
| 4 | 修 Findings + 勾选 | P2-1/P2-2 均修完(executor/sonnet,commit `98c2837`,3 文件 +17/-9,**产品逻辑零改动**)。协调者独立复核:自己另做一次变异(`+1`→`+3`,刻意不同于子代理的 `+2`)确认 `Sequence_starts_at_one...` 在 :49 转红(失败 3/通过 5),单文件 checkout 复原后 `git diff` 空;重跑闸门 build **0 错误**、test **279/279 通过失败 0**。0×P1、0×未修 P2、闸门已跑 → **Task 1 勾选**。下一步 Round 5 Task 2 plan(IWorkflowNodeHandler SPI,纯类型定义不接引擎)。 |
| 5 | plan | Task 2 plan 完成(Opus 子代理,39 次工具调用)。7 个决策点拍板:①`Cancelled` **不进**结果枚举——取消走 OCE,与 `TerminalFailure` 语义方向相反(前者「应被重新领取」/后者「永不重试」),合并会让 Task 6 分不出该不该重试;②Result 用 sealed class + 私有构造 + 四静态工厂,不用类型层次(§6.2 存四个扁平列,1:1 映射);③Context 14 字段,不泄漏实体/DB session,变量传原始 JSON、节点配置传既有 `WfNodeProps`;④接口键用 `WfNodeType` 枚举 + `TryAddEnumerable` 分发(同 `IAdminJob` 先例),无 `CanHandle`/keyed DI/抽象基类;⑤本 Task **零 DI 注册**,第一条注册线归 Task 8;⑥`FakeNodeHandler` 放测试程序集,绝不进内核包;⑦一个文件放 `Abstractions/`,不预建 `Execution/`。改动清单 **3 个新文件、0 既有改动**,预期 279→291。6 条新契约行已入 `## 语义契约`。下一步 Round 6 Task 2 exec。 |
