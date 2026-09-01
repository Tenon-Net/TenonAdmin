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

- 轮次: 16
- max: 70
- 当前任务: Task 4 **已收口**(`wf_node_execution_attempt` 实体 + append-only 记录)
- 当前阶段: 修 Findings 完成 → **已勾选**(按 Round 8/12 先例合并进同一轮)
- 上一轮: Round 16 — Task 4 **修 Findings + 勾选**(executor/sonnet)。commit `da1cf93`,**2 个文件 +6/-3**:测试 #1 改用 `var ended = started.AddSeconds(3);` 并加两行 `Assert.Equal`(P2-1),实体两处 `Length = 512` 改为引用 `SummaryMaxLength` 常量(顺带的可选 P3-1)。**原有断言全保留、未新增测试方法(条数仍 6/全量仍 313)、产品代码零行为改动**。协调者**独立复核**:①改动面逐行核对——测试侧只改了 `started, started → started, ended` 那一行加两行断言,产品侧只有两个 attribute 实参换成常量引用;②**刻意换成子代理没试过的变异形状**:不用它的「互换」也不用「写死 `2000-01-01`」,改成 `EndedAtUtc = startedAtUtc.AddSeconds(-3)`——**只坏 `EndedAtUtc`、`StartedAtUtc` 仍正确,造出负耗时**,比互换/写死更贴近真实失误 → **转红**,期望与实际相差恰好 6 秒(+3 应为 −3);③变异前 `git diff -U0` 确认落盘,后单文件 `git checkout` 复原,`git diff --stat` 空;④`DefaultValue`/`CodeFirst_BigString` 命中仍为 **0**;⑤重跑两条闸门:build **0 错误**,test **313/313 通过、失败 0**(条数未增,符合「纯补断言」要求)。0×P1、0×未修 P2、闸门已跑 → **勾选 Task 4**(10 项已完成 4 项)。
- 下一步: Round 17 — Task 5 **plan**(`wf_outbox` 实体 + 可靠派发骨架)。协调者派 `Agent(model="opus"`,不传 `subagent_type`)。**沿用 Round 13 起的文件交付法**:要求子代理把七节全文 `Write` 进 `scratchpad/plan5.md` 再回一句话确认(消息通道有长度上限,已吃过三次截断)。prompt 要点:①塞入 Task 5 台账原文(「新增实体与表(字段参照 §六 6.3);与 execution 结果同一短事务提交;本 Task 交付『写得进去、状态可查询』,实际派发消费逻辑视 Task 6 需要决定是否本 Task 一并做还是独立」)+ `## 语义契约` 全文;②设计文档 `workflow-database-design-review-2026-08-24.md` **§六 6.3**、§八(索引)、`elsa3-slickflow-ai-reference-2026-08-23.md` **§4.6**(结果、变量、历史和 outbox 在同一短事务提交);③**要求它先读 `WfNodeExecution.cs` 与 `WfNodeExecutionAttempt.cs` 两个已落地实体**,逐条沿用四条先例(`BaseEntity` 非 `DataEntity`、UTC 列名后缀、全表不写 `DefaultValue` 且理由写进注释、`uk_wf_*`/`idx_wf_*` 命名),别照抄我的转述;④**本 Task 必须自己拍板的硬问题**:outbox 的**状态机与领取方式**(是否复用 Task 3 的 lease/fence 那套,还是更简单的 `Pending → Dispatched` + 重试计数)、**幂等键**(消费方去重靠什么,与 `ExecutionKey`/`IdentityHash` 的关系)、**payload 存全文还是 hash+引用**(与 Task 4 的「不存正文」定案是否一致,outbox 的消费者在进程外,可能真需要正文——这一条要给出理由而不是照抄)、**「实际派发消费逻辑本轮做不做」**(台账原文把这个决定权交给了 plan,必须明确拍板并说明理由);⑤划界:不接 dispatcher(Task 6),零 DI 注册,`WorkflowSetup.cs` 零改动、十件套仍 10 条。**不勾选**,不顺带做 Task 6。

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
| `ExecutionKey` 构成(Task 3 定案) | `public static WfExecutionKey.Compute(scopeKey, instanceId, tokenId, nodeVisitId, nodeId, definitionVersionId)`(放 `Engine/WfExecutionKey.cs`):按此**固定顺序**用 `'\n'` 拼接 → UTF-8 → SHA-256 → 小写 hex,列 `Length=64` **非空**。`ScopeKey` 复用 `WfIdentityHash.NormalizeScopeKey`(无机构 → 哨兵 `"-"`),`NodeVisitId` 为 null 同样归一化为 `"-"`;数值 `InvariantCulture`,字符串 Trim 保留大小写,含分隔符抛 `ArgumentException`。**发包后不可逆契约**,只许末尾追加(且须同时给旧维度定哨兵),由 `WfExecutionKeyTests` 快照钉死——那条红了是撤回改动,不是改期望值。**不复用 `WfIdentityHash.Compute`**(其签名被回执 6 维度与 `WfCommandType/WfTargetType` 焊死,追加参数会破坏已发包的回执 hash)。选 hash 不选明文拼接:常数 64 位一次性绕开 MySQL utf8mb4 3072B(实占 256B)与 SqlServer 900B(`SqlServerCodeFirstNvarchar=true` → `nvarchar(64)` 实占 128B)两个上限,且非空 → 天然避开 SqlServer「多个 NULL 视为相等」。组成字段仍保留为独立列(排查用,不参与唯一性,同 `WfOperationReceipt`)。`NodeVisitId = null` 时 key 退化为 (scope,instance,token,node,defVer)——M3a 内**不可达**(旧 token 不会被 dispatcher 领取),是语义不是缺陷,**不做回填**。 |
| Handler 结果枚举(Task 2 定案) | `WfNodeExecutionResultType { Succeeded=1, RetryableFailure=2, ManualFallback=3, TerminalFailure=4 }`,**刻意无 0 值**(`default(枚举)` 非法,dispatcher `default:` 臂抛异常,杜绝「零初始化悄悄等于成功」);数值将进评审 §6.2 的 `ResultType` 列,**只追加不重排**。**不设 `Cancelled` 成员**。承载类型是 `sealed class WfNodeExecutionResult`(私有构造 + 四个静态工厂 + `OutputJson`/`Summary`/`ErrorCode`/`RetryAfter` 扁平 payload),**不是类型层次**——理由:§6.2 存的是四个扁平列,1:1 映射;类层次到同样四列要在持久化边界写一梯子 `is` 下转型,而 C# 的类层次 `switch` 本来也没有穷尽性检查。用 `sealed class` 而非 `record`:`record` 的 `with` 会留一条绕过工厂的后门。handler **不得**推进 token、写任务状态、自开数据库事务(AI 基石 §4.5/§4.7 硬约束)。 |
| 取消语义(Task 2 定案) | handler 抛 `OperationCanceledException` = 「这次没跑完,**应可被重新领取**」,**语义上不等于 `TerminalFailure`**(后者 = 永不重试)。二者方向相反,合并会让 Task 6 再也分不出「该重试吗」。Task 6 的异常处理**不得**把 OCE 归进任何一个结果分支,须单独识别并让 lease 过期/释放,走 Task 7 的崩溃恢复路径。「实例被外部撤销」不是 handler 的返回值维度——handler 压根不知道,由 dispatcher 在回写短事务里靠 fence/CAS 发现并丢弃结果。 |
| 结果枚举 vs 状态机(Task 2 定案) | `WfNodeExecutionResultType`(一次 attempt 的**答复**)与 Task 3 的 `WfNodeExecutionStatus`(`wf_node_execution.Status` **行状态**)是**两个不同类型**,不许合并、不许共用数值。一次 execution 多次 attempt,每次 attempt 一个结果,行状态是它们的聚合。 |
| handler 键与分发(Task 2 定案) | `IWorkflowNodeHandler.NodeType` 是 **`WfNodeType` 枚举**(非字符串、非 keyed DI)——要匹配的对端 `WfNode.Type` 本来就是枚举,用 string 等于强插一次 camelCase 往返,漂移只会在运行时表现为「找不到 handler」。分发 = `TryAddEnumerable` 多实现 + `GetServices<IWorkflowNodeHandler>().FirstOrDefault(h => h.NodeType == node.Type)`,与 `IAdminJob`/`DefaultJobHandlerResolver` 同款,不发明新范式。M3b 的 AI 节点走「往 `WfNodeType` **追加**成员」(注意 `WfNodeType` 带 `allowIntegerValues: true`,存量 `ModelJson` 可能有整数值 → 同样只追加不重排)。**第一条 DI 注册线由 Task 8 加,Task 2 零注册**(注册一个零实现的空枚举面等于死代码,还会让 `WorkflowReplaceabilityTests` 的「十件套」注释失真)。 |
| `WfNodeExecutionContext` 形状(Task 2 定案) | 14 个 `init` 字段(`ExecutionKey`/`InstanceId`/`TokenId`/`NodeVisitId`/`NodeId`/`NodeType`/`DefinitionVersionId`/`OrgId`/`StarterUserId`/`BusinessKey`/`NodeProps`/`VariablesJson`/`Attempt`/`DeadlineAtUtc`),**不含** SqlSugar 实体、`ISqlSugarClient`、`TimeProvider`、evidence(M3b 再加,用非 required)。节点配置传既有 `WfNodeProps`(纯 POCO,Webhook 配置位已在里面)——**dispatcher 必须传自己反序列化的版本快照实例,不得共享引擎 `ctx.Model` 上的活树节点**;handler 只读。变量传原始 `string? VariablesJson`,**实现必须对烂 JSON 免疫**(同 `IWfConditionEvaluator` 既有约定,本仓刻意不在中心处理这件事)。租户维度**只有 `OrgId`**(本仓无 tenant 原语,不凭空造 `TenantId`)。`Attempt` **1 基**,= 将写的 attempt 行 `AttemptNo` = 领取时 `AttemptCount + 1`(三处口径必须对齐,差一是最典型的静默 bug)。`DeadlineAtUtc` 是**绝对** `DateTimeOffset`,非相对 `TimeSpan`、非 `DateTime`(相对超时会在「领取→排队→开跑」之间失真;`DateTimeOffset` 从类型上消灭 Kind 歧义)。SPI 的 `DateTimeOffset` 与 Task 3 列的 `DateTime` 之间有一次转换,落点在 dispatcher,**别为了「统一」把 SPI 改成 `DateTime`**。 |
| `HandlerVersion` 等预留列(Task 2 挂账 → Task 3 结账) | 评审 §6.1 的 `HandlerType`/`HandlerVersion`/`InputHash`/`OutputHash`/`CompletedTimeUtc` 在 `wf_node_execution` **建表时一次造齐**,全部可空、Task 3 零写入点(Task 6 填值)。理由:建表期加 5 个可空列成本为 0,将来 `ADD COLUMN` 要在消费者的四种方言库上各走一遍;先例同 `WfHistoryActorType.Worker/Ai` 的预留。接口侧仍**刻意不进 `IWorkflowNodeHandler`**,将来以**默认接口成员**(`string HandlerVersion => "1";`)追加,对已有实现零破坏。 |
| execution 状态机(Task 3 定案) | `WfNodeExecutionStatus`(在 `Entities/WfEnums.cs`,**不在 `Abstractions/`**——它是持久化枚举不是 SPI):`Pending=1, Running=2, Succeeded=3, RetryScheduled=4, ManualFallback=5, Cancelled=6, Failed=7`。**刻意无 0 值**(同 `WfNodeExecutionResultType`;与 `WfHistoryActorType.Unknown=0` 的差别在于本表是新建表、无旧行)。`Cancelled`(外部撤销/终止,静默丢弃)与 `Failed`(`TerminalFailure` 或重试预算耗尽,永不再动)**都要**,合并会让 Task 6 分不出该不该报警。转换:`(insert)→Pending`;`Pending → Running`(claim);`RetryScheduled(NextRetryAtUtc<=now) → Running`;`Running →(租约过期)→ Running`(**合法自转移,fence 存在的全部理由**:老 owner 可能还活着,其回写必须靠 fence 被拒);`Running → Succeeded\|RetryScheduled\|ManualFallback\|Failed\|Cancelled`;`Pending\|RetryScheduled → Cancelled`;`Succeeded`/`ManualFallback`/`Failed`/`Cancelled` 是终态无出边。数值只追加不重排。 |
| 事务边界 | 短事务领取(CAS lease/fence)→ 事务外调用 handler → 短事务落 attempt/result/token 推进/outbox,**不得**让远程调用发生在数据库事务内(AI 基石 §4.6 步骤 1–5,验收线 §4.8 明文要求) |
| lease/fence(Task 3 定案) | 列:`LeaseOwner string?`(`Length=128`,对齐 `SysJobLock.OwnerNodeName`;未领取 = null 而非空串;worker 标识由**调用方传参**,Task 3 不接 DI——`JobTime.ResolveNodeName` 是 internal,Workflow 包够不着)、`LeaseExpiresAtUtc DateTime?`、`Fence long`(非空,从 **0** 起,首次领取变 1;用 `long` 不用 `int` 因为 Task 5/8 会把它当幂等/排序令牌交给外部系统,届时加宽是破坏性列变更)、`AttemptCount int`(非空,从 0 起)。**全表所有列一律不写 `DefaultValue`**——它只驱动 `DbMaintenanceProvider.AddColumn` 的三步序列,`CREATE TABLE` 路径根本不读它,新建表写它是噪音(Task 1 那条契约管的是**加列**,不是建表;这点必须写进实体注释,否则会被机械套用误判成 P1)。领取 = **一条条件 UPDATE**:`SET Status=Running, LeaseOwner=@owner, LeaseExpiresAtUtc=@until, Fence=Fence+1, AttemptCount=AttemptCount+1 WHERE Id=@id AND (Status=Pending OR (Status=RetryScheduled AND NextRetryAtUtc<=@now) OR (Status=Running AND LeaseExpiresAtUtc<@now))`,**随后在同一事务内读回**(`WfHistorySequence.NextAsync` 同款;四库通用:不用 PG `RETURNING`、不用 SqlServer 复合赋值、不用 `FOR UPDATE SKIP LOCKED`、不用任何数据库时间函数)。影响行数 1=领到、0=不可领 → **返回 `null` 不抛异常**(与 `ClaimInstanceAsync` 抛 48004 的差别是有意的:那是用户请求撞车必须可见,这是 worker 空跑一拍)。**租约过期用应用时间**(`nowUtc` 作参数传入;先例 `JobSchedulerService.HeartbeatAsync` 的夺租)——Task 7 因此可以直接把 `LeaseExpiresAtUtc` UPDATE 到过去来模拟崩溃,无需操纵时钟。单赢家由「领取即持行排他锁到提交 + 四库解锁后对新版本重检 WHERE」保证。`SetColumns` 不触发只认 `UpdateByObject` 的审计 AOP,故领取不刷新 `UpdateTime/UpdateUserId`。**`SetColumns` 里禁止内联 `DateTime` 表达式**(zh-CN 下会被格式化成 `下午` 字面量炸 SQL),`nowUtc`/`leaseUntil`/`owner` 全部先落局部变量。 |
| `wf_node_execution` 基类与时间口径(Task 3 定案) | 继承 **`BaseEntity` 而非 `DataEntity`**:`DataEntity` 的 `IOrgScoped` 全局过滤器会让**无 HTTP 请求上下文的后台 worker** 扫描返回 0 行(症状是「调度器永远没活干」而不是报错,且在有 HTTP 上下文的集成测试里可能仍是绿的),机构维度改由显式非空的 `ScopeKey` 承载——同 `WfOperationReceipt`,理由在这里更强。`IsDelete` 永不置真。四个业务时间列**一律 UTC**(`DeadlineAtUtc`/`NextRetryAtUtc`/`LeaseExpiresAtUtc`/`CompletedTimeUtc`,值取 `GetUtcNow().UtcDateTime`),这是**刻意偏离**本仓「业务时间戳走 `GetLocalNow()`」的惯例(依据评审 §六收尾),**列名 `Utc` 后缀是唯一护栏**。**硬约束:不得把基类的 local `CreateTime`/`UpdateTime` 与任何 `*Utc` 列比较或相减。** SPI 的 `DateTimeOffset` ↔ 本表 `DateTime` 的转换落点在 **Task 6**,必须先 `DateTime.SpecifyKind(x, DateTimeKind.Utc)`(SqlSugar 读回是 `Kind.Unspecified`,直接构造 `DateTimeOffset` 会按本机时区偏移,非 UTC 机器上悄悄错 8 小时)——别为了「统一」把 SPI 改成 `DateTime`。索引:`uk_wf_node_exec_key`(`ExecutionKey` 唯一,命名对齐 `uk_wf_receipt_identity`)+ `idx_wf_node_exec_scan`(`Status`,`NextRetryAtUtc`,取自评审 §八);**刻意不建** `(InstanceId)`/`(TokenId)`,等真有查询再加。 |
| `AttemptCount` 三处口径(Task 3 定案) | 领取的那条 UPDATE 里 `AttemptCount + 1` 后**读回**;`WfNodeExecutionContext.Attempt` = **读回后**的值 = 领取前 `AttemptCount + 1` = Task 4 将写的 `AttemptNo`。首次领取 0→1,`Attempt=1`(1 基)。三处对齐完成。**Task 4 在插 attempt 行时不得再 +1** —— 那是经典差一。 |
| Task 3 交付边界与 API(Task 3 定案) | `public static class WfNodeExecutionStore`(放 `Engine/`,与孪生的 `WfHistorySequence.cs` 同目录,**不预建 `Execution/` 目录**,沿用 Task 2 同款决定):`EnsureAsync(db, row, ct)` + `ClaimAsync(db, executionId, owner, nowUtc, leaseDuration, ct)`。**`public` 而非 `internal`**(`WfHistorySequence` 是 internal)——全仓无 `InternalsVisibleTo`,做成 internal 会让本轮「能领取」零直接证据;`WfIdentityHash` 同为 `public static`,先例一致。**本 Task 零 DI 注册**(同 Task 2;第一条注册线仍归 Task 8),`WorkflowSetup.cs` 零改动、十件套仍 10 条。**本表零引擎调用点**:`EnterNodeOp` 对 `WfNodeType.Webhook` 仍走 `default:` 抛 48008,那是正确的中间状态,接上 dispatcher 归 Task 6。`EnsureAsync` **不做唯一冲突的「认赢家」恢复**(Task 3 无并发创建方),归 Task 6——届时 PG 唯一冲突会中止整事务(`25P02`),必须抄 `Services/WfOperationReceiptService.cs` 的 `BeginNestedAsync/RollbackNestedAsync` savepoint,**本轮的定案是连 try/catch 都别写**。 |
| attempt 记录(Task 4 定案) | `wf_node_execution_attempt` / `WfNodeExecutionAttempt`,继承 **`BaseEntity`**(理由同 execution:`IOrgScoped` 只作用于 SELECT,后台 worker 无 `IDataScopeContext` 会静默返回 0 行、症状伪装成「调度器扫不到活」,**且在有 HTTP 上下文的集成测试里可能仍是绿的**)。9 个业务列:`ExecutionId`(long,非空)、`AttemptNo`(int,非空,**1 基**)、`StartedAtUtc`/`EndedAtUtc`(DateTime,**均非空**)、`ResultType`(`WfNodeExecutionResultType`,非空)、`OutputSummary`/`ErrorSummary`(string?,512)、`OutputHash`(string?,64)、`ErrorCode`(int?)。append-only 由**两道**保证:硬的是 `UNIQUE(ExecutionId, AttemptNo)`(§八原文;把「同一 attempt 写两行」从静默重复变成抛异常,也是「重试新增一行」那条测试有鉴别力的前提——没它则 `AttemptNo` 恒为 1 的 bug 也能插两行而测试照绿),软的是 Store **只暴露 `AppendAsync` 一个方法**、不提供任何 Update/Delete(§4.7 逐字沿用 `wf_history`/`wf_operation_receipt` 的姿势),`IsDelete` 永不置真。**`EndedAtUtc` 非空是有意的**:一行 attempt = 一次**已返回**的调用,由此 `execution.AttemptCount`(领取次数)− `count(attempt)`(返回次数)= **领了但没返回的次数,即崩溃次数**;允许插「只有开始时间」的行会毁掉这个口径。**不建 `DurationMs`**(相减即得)。**不带 `ScopeKey`**:attempt 行永远经 `ExecutionId` 到达、父行已有,为不存在的查询做反规范化等于永久多一个必须保持一致的写入点;若 review 坚持要,唯一可接受形态是**非空、由 execution 行拷贝**,绝不可空。索引只建 `uk_wf_node_exec_attempt_no`,**刻意不建**单列 `(ExecutionId)`(唯一索引首列已覆盖)与 `(ResultType)`/`(StartedAtUtc)`/`(ErrorCode)`(今天零查询)。 |
| `AttemptNo` 防差一的签名设计(Task 4 定案) | `AppendAsync(db, WfNodeExecution execution, WfNodeExecutionResult result, DateTime startedAtUtc, DateTime endedAtUtc, ct)` —— **签名里根本没有 `attemptNo` 形参**,方法体内 `AttemptNo = execution.AttemptCount;`(取 `ClaimAsync` 读回后的行),**看到 `+ 1` 就是错的;给它加一个 `int attemptNo` 形参也是错的**(那正是把差一的入口重新打开)。`ExecutionId` 与 `AttemptNo` 两个事实取自**同一个对象**,顺带杜绝「A 的 Id 配 B 的 count」。`WfNodeExecutionResult → 四个扁平列`的投影也收在方法内,Task 6 因此没机会把 `Summary` 映射丢掉(Task 2 review P2-2 的教训)。测试必须**同时**断言 `AttemptNo == 1` 与 `== claimed.AttemptCount`——只断前者则「永远写 1」的实现照绿,只断后者则「三处一起错」的实现照绿。 |
| attempt 不回写 execution(Task 4 定案) | `AppendAsync` **只插 attempt 一行,零 execution 写入**。把 `Status`/`NextRetryAtUtc`/`CompletedTimeUtc`/`OutputHash`/`ErrorCode`/`Summary` 回写 execution **属于 Task 6 的回写短事务**,理由是硬的:那次回写**必须带 `WHERE Fence == @myFence` 的 CAS**,否则租约过期后老 owner 的迟到回写会压过新 worker 的结果(Task 3 射程限制 **R6** 已记明此项归 Task 6)。本 Task 若顺手更新 execution,等于把**没有 CAS 保护**的写入提前散进来、且散在一个不知道 fence 的地方。注释写清:attempt 行与结果回写将在 Task 6 的**同一个短事务**里提交(§4.6),但**代码归属不同**——本类只管 attempt 那一行,事务由调用方起。 |
| attempt 不存输出正文(Task 4 定案) | §6.2 原文要求「输出正文、敏感字段和密钥不直接进入日志;保存必要摘要、hash 和受控引用」。定案:**存 hash + 截断摘要,不存全文**。`OutputHash` = `Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result.OutputJson)))`(与 `WfIdentityHash`/`WfExecutionKey` 最后一行逐字同型;本仓无通用 `string → sha256 hex` helper,**一行内联,不为此新建 helper、不扩 `WfIdentityHash` 的职责**);`OutputJson == null` → 本列 null。两个摘要列 512(对齐 `WfNodeExecution.Summary`),**由写入方在 C# 侧截断**。**不用 `StaticConfig.CodeFirst_BigString`**(仓内 `WfHistory.PayloadJson`/`WfOperationReceipt.ResultJson` 用了它,极易照抄):attempt 是 append-only、永不删除的表,把 handler 输出正文塞进去会让存储与备份成本由消费者长期承担,且这是 **PII/密钥泄漏面最大的一张表**(M3b 之后正文就是模型输出);正文去处由 Task 6 定。**截断是必须实现的、不是可选的**——摘要是外部输入(trust boundary),600 字的 summary 在 **SqlServer/PostgreSQL 上直接抛**、**MySQL 非严格模式静默截断**、**SQLite 照单全收**,正是本仓最典型的「本地 SQLite 全绿、CI 三条腿红」。截在 C# 侧则四库行为一致;两个摘要列与 hash 列都不进索引,零方言争议。 |
| §6.2 里刻意不建的 7 列(Task 4 定案) | `Provider`/`Model`/`PromptVersion`/`SchemaVersion`/`PolicyVersion`/`TokenUsage`/`Cost` **本轮不建**。理由不是「以后再说」,而是**它们在 §七已经有家**——`wf_ai_decision` 明文带前五个;同一事实两张表两个写入点是 bug 温床。`TokenUsage`/`Cost` 确属 attempt 维度,但只有 M3b 才有值可填,而它们是**可空列**——`WfHistory.RequestId` 注释已实测记录「可空、无默认值的 `ADD COLUMN` 四库都接受,不触发三步序列」,是本仓走熟的路。**与 Task 3「建表期一次造齐」不矛盾**:那 8 列是**同一里程碑内 Task 6** 要填的,这 7 列属于**下一个里程碑**(M3b,还是禁区点名的)。 |
| 实体如何进 CodeFirst 建表(Task 4 记录) | 靠 `WorkflowSetup.UseWorkflow` 的**整程序集扫描**(`WorkflowSetup.cs:28`,`options.ApplicationAssemblies.Add(asm)`)。**不需要在任何地方登记实体类型**——本仓**不存在**「实体类型列表」这种东西,任何「把新实体加进某个 `typeof` 列表」的动作都是错的,别去找、更别去建。 |
| outbox | 结果提交后可靠触发通知/外部副作用;短事务与 execution 结果同提交(AI 基石 §4.6) |
| Webhook 超时/重试分类 | 待 Task 8 plan 定案(哪些 HTTP 状态码/异常归 `RetryableFailure`,哪些归 `TerminalFailure`,是否有的场景该转 `ManualFallback`) |
| 与人工任务的关系 | `ManualFallback` 时如何创建人工 `wf_task`——待 Task 6(dispatcher)plan 定案,复用 `EnterNodeOp.CreateTaskAsync` 还是新路径 |
| 范围外 | 不建 AI Decision(`wf_ai_decision`、provider adapter、policy、shadow mode——留给 M3b);不建并行网关(`ParentTokenId`/`ForkId`/join 表);不新增审批动词;不 port React 工作流页;不抽 web/web-react 共享层 |

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段的 Agent 重写,协调者转写进本节)

> 本节为 **Task 4**(`wf_node_execution_attempt` 实体 + append-only 记录)的 plan,由 Round 13 的 Opus 子代理产出,协调者原文转写。Task 1/2/3 的历史 plan 已被覆盖,其定案沉淀在 `## 语义契约`。

### 1. 决策点定案

已实际读过:`Entities/WfNodeExecution.cs`、`Engine/WfNodeExecutionStore.cs`、`Entities/WfOperationReceipt.cs`、`Entities/WfHistory.cs`、`Entities/WfEnums.cs`、`Abstractions/IWorkflowNodeHandler.cs`、`WorkflowSetup.cs`、`tests/WfNodeExecutionClaimTests.cs`、`Engine/WfIdentityHash.cs` / `WfExecutionKey.cs`,以及设计评审 §4.7 / §六 6.1–6.3 / §七 / §八、AI 基石 §4.5、台账 `.loop/wf-m3a1.md`。

#### D1. 表名 / 实体名 / 字段全集

`wf_node_execution_attempt` / `WfNodeExecutionAttempt`,放 `Entities/WfNodeExecutionAttempt.cs`,继承 `BaseEntity`。

| 列 | C# 类型 | 可空 | 长度 | 说明 |
|---|---|---|---|---|
| `ExecutionId` | `long` | 否 | — | 指向 `wf_node_execution.Id`。本仓不建数据库外键(全仓无 FK 先例),靠唯一索引首列串联 |
| `AttemptNo` | `int` | 否 | — | **1 基**,直接取 execution 领取读回后的 `AttemptCount`,**不得再 +1**(见 D2)。类型跟 `WfNodeExecution.AttemptCount` 一致取 `int`,不取 `long`——它不像 `Fence` 那样会被交给外部系统当令牌 |
| `StartedAtUtc` | `DateTime` | 否 | — | UTC;值由调用方传入 |
| `EndedAtUtc` | `DateTime` | 否 | — | UTC;值由调用方传入。**非空是有意的**:一行 attempt = 一次**已返回**的调用(见下方「崩溃可见性」) |
| `ResultType` | `WfNodeExecutionResultType` | 否 | — | Task 2 定案的枚举(`Succeeded=1/RetryableFailure=2/ManualFallback=3/TerminalFailure=4`,刻意无 0 值) |
| `OutputSummary` | `string?` | 是 | 512 | 成功时的摘要;写入方截断(见 D8) |
| `OutputHash` | `string?` | 是 | 64 | `result.OutputJson` 的 SHA-256 小写 hex;`OutputJson == null` → 本列 `null` |
| `ErrorCode` | `int?` | 是 | — | 失败/回退时的错误码 |
| `ErrorSummary` | `string?` | 是 | 512 | 失败/回退时的摘要;同样截断 |

共 **9 个业务列** + `BaseEntity` 的审计列。

**§6.2 里刻意不建的列**:`Provider` / `Model` / `PromptVersion` / `SchemaVersion` / `PolicyVersion` / `TokenUsage` / `Cost`。

理由不是「以后再说」,而是**它们在 §七已经有家**:`wf_ai_decision` 明文带 `Provider` / `Model` / `PromptVersion` / `PolicyVersion` / `SchemaVersion`(还带 `InputHash`)。同一个事实两张表、两个写入点,是最典型的 bug 温床。`TokenUsage` / `Cost` 确实是 attempt 维度的,但**只有 M3b 才有值可填**,而它们都是可空数值列——`WfHistory.RequestId` 的注释里已实测记录:「可空、无默认值的 `ADD COLUMN` 四库都接受,不触发『先加可空列 → 回填 → 改 NOT NULL』三步路」。这是本仓走熟的路。

这与 Task 3 的「建表期一次造齐 8 个预留列」**不矛盾**:那 8 列是**同一个里程碑内 Task 6 就要填**的,这 7 列属于**下一个里程碑**,且 M3b 已被本轮禁区点名。本轮建它们 = 建 7 个本里程碑内永远没人填的列。

**本表没有 `ScopeKey`**(这条必须写进注释,否则下一轮 review 会当新发现重开):

- `wf_node_execution` / `wf_operation_receipt` 带 `ScopeKey`,是因为它们**被按身份直接查**(`ExecutionKey` / `IdentityHash` 唯一索引)、**被 worker 直接扫**(`(Status, NextRetryAtUtc)`),自身就是查询入口。
- attempt 行永远**经 `ExecutionId` 到达**,父行已经带着 `ScopeKey`。为一个今天不存在的查询做反规范化,等于永久多一个必须与父行保持一致的写入点。
- **但基类仍必须是 `BaseEntity`**——理由与 execution 逐字相同(见 D7),两件事不能混为一谈:「不要 `DataEntity`」是因为 `IOrgScoped` 全局过滤器会让后台 worker 静默读到 0 行;「不要 `ScopeKey`」是因为没有查询要用它。
- 若 review 坚持要 `ScopeKey`,**唯一可接受的形态是非空、由 execution 行拷贝**(对齐 `WfOperationReceipt` / Round 12 刚把 `WfNodeExecution.ScopeKey` 改非空的定案),**绝不可空**。

**崩溃可见性(顺带得到的性质,写进注释)**:`execution.AttemptCount`(领取次数)− `count(attempt)`(返回次数)= 领了但没返回的次数,即崩溃/被杀次数。`EndedAtUtc` 非空正是为了保住这个口径——若允许插「只有开始时间」的行,这个差额就失去意义了。Task 7 的崩溃恢复会用到这个观测点。

**刻意不建 `DurationMs`**:`EndedAtUtc − StartedAtUtc` 就是,§6.2 也没列它。本仓「办理耗时」那套(§4.3)是人工任务的口径,与机器 attempt 无关,不要顺手搬过来。

**刻意不建 `Fence` / `LeaseOwner` 副本**:记录「这次 attempt 跑在哪个 fence 下」看着有诊断价值,但拒绝迟到回写的逻辑在 execution 行的 CAS 上(Task 6),attempt 表存一份既不参与判定也不参与查询。

#### D2. `AttemptNo` 的来源 —— 签名里根本没有这个参数

```csharp
public static async Task<WfNodeExecutionAttempt> AppendAsync(
    ISqlSugarClient db,
    WfNodeExecution execution,          // ← 领取读回后的行(ClaimAsync 的返回值)
    WfNodeExecutionResult result,
    DateTime startedAtUtc,
    DateTime endedAtUtc,
    CancellationToken cancellationToken)
```

**差一在类型层面难以发生的机制**:方法签名里**没有 `int attemptNo` 形参可供调用方 +1**。方法体内:

```csharp
ExecutionId = execution.Id,
AttemptNo   = execution.AttemptCount,   // 直接取,绝不 +1
```

两个事实取自**同一个对象**,顺带也杜绝了「A 的 Id 配 B 的 count」这类错配。`WfNodeExecutionResult → 四个扁平列`的投影同样收在方法内部,Task 6 也就没机会把 `Summary` 的映射丢掉——这正是 Task 2 review **P2-2**(四个工厂的 payload 管线没被断言,一个掉了的 `Summary = summary` 会让 `attempt.ErrorSummary` 永远为空)的教训。

**三处口径**(Task 3 定案,逐字沿用):领取 UPDATE 里 `AttemptCount + 1` → 读回 → `WfNodeExecutionContext.Attempt` = 读回后的值 = **本列 `AttemptNo`**。首次领取 `0 → 1`,`Attempt = 1`,`AttemptNo = 1`。本轮第一次有**两处**可以互相对照(Task 3 射程限制 R5 的一半),第三处(Context)归 Task 6。

#### D3. append-only 怎么保证 —— 一硬一软两道

**硬(可验证的机制)**:`UNIQUE(ExecutionId, AttemptNo)`,索引名 `uk_wf_node_exec_attempt_no`。**建**。

- §八原文就推荐这一条(`wf_node_execution_attempt` → `UNIQUE(ExecutionId, AttemptNo)` → 「防 attempt 编号重复」)。
- 它把「同一次 attempt 写两行」从静默重复变成**抛异常**。
- 它同时是「重试新增一行」那条测试**有鉴别力的前提**:没有它,一个 `AttemptNo` 恒等于 1 的实现也能插两行、测试照样绿。
- 代价只有一个索引,而且它的首列天然服务「列出某 execution 的全部 attempt」这个必然会出现的查询。
- 两列都非空,踩不到 SqlServer「唯一索引把多个 NULL 视为相等」的坑。

**软(约定 + 注释 + API 面)**:

- `WfNodeExecutionAttemptStore` **只暴露 `AppendAsync` 一个方法**,不提供任何 Update / Delete。
- `BaseEntity` 带来的 `UpdateTime` / `UpdateUserId` / `IsDelete` **永不置真**——§4.7 已明确定调:「不删除现有字段;工作流 Module 不暴露历史记录的通用更新/删除 Interface;新建的 attempt/decision 记录从一开始采用只增写入路径;管理清理走明确的保留期策略,而不是普通软删除」。逐字沿用 `wf_history` / `wf_operation_receipt` 的姿势,写进类注释。

#### D4. 索引:建哪些、刻意不建哪些

| 索引 | 建? | 理由 |
|---|---|---|
| `uk_wf_node_exec_attempt_no` UNIQUE `(ExecutionId, AttemptNo)` | **建** | §八原文;把 attempt 编号重复变成硬错误;首列即「按 execution 列出 attempt」的查询 |
| 单列 `(ExecutionId)` | **不建** | 上面那个复合索引的首列已经覆盖,纯冗余 |
| `(ResultType)` | **不建** | 今天零查询。「按结果类型统计失败率」是 M3a-2 产品面的事,届时再加 |
| `(StartedAtUtc)` / `(EndedAtUtc)` | **不建** | 今天零查询。保留期清理走 `ExecutionId` 子查询,不需要时间索引 |
| `(ErrorCode)` | **不建** | 同上 |

命名沿用 `uk_wf_*`(唯一)/ `idx_wf_*`(普通),对齐 `uk_wf_receipt_identity` / `uk_wf_node_exec_key` / `idx_wf_node_exec_scan`。**刻意不建今天没有查询用得上的索引**,与 Task 3「刻意不建 `(InstanceId)` / `(TokenId)`」同一条纪律。

#### D5. API 形状:本轮就交付写入 API

`public static class WfNodeExecutionAttemptStore`,放 `Engine/WfNodeExecutionAttemptStore.cs`(与孪生的 `WfNodeExecutionStore.cs` / `WfHistorySequence.cs` 同目录;**不预建 `Execution/` 子目录**,沿用 Task 2/3 的同款决定)。

- **`public` 而非 `internal`**:全仓无 `InternalsVisibleTo`(Task 3 已 grep 命中 0),internal 会让本轮零直接证据。`WfIdentityHash` / `WfExecutionKey` / `WfNodeExecutionStore` 同为 `public static`,先例一致。
- **零 DI 注册**,`WorkflowSetup.cs` **零改动**,十件套仍 **10** 条(第一条注册线仍归 Task 8)。
- **本轮就给写入 API,不是只建实体**:只建实体不给写入路径,「append-only 写入路径只增不改不删」这一轮就**零直接证据**,台账那句「至少一条测试证明重试不覆盖旧 attempt」也无从落地——测试要么只能自己拼 `Insertable`(那测的是测试自己写的投影,不是产品代码),要么根本写不出来。所以给。
- **只给一个方法**,不给 `ListAsync` / `GetAsync`:测试直接 `db.Queryable<WfNodeExecutionAttempt>()`,与 `WfNodeExecutionClaimTests` 同款姿势。查询 API 等 Task 6/M3a-2 真有调用方时再长出来。

```csharp
public static class WfNodeExecutionAttemptStore
{
    public const int SummaryMaxLength = 512;

    public static Task<WfNodeExecutionAttempt> AppendAsync(
        ISqlSugarClient db,
        WfNodeExecution execution,
        WfNodeExecutionResult result,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        CancellationToken cancellationToken);
}
```

#### D6. 与 execution 行的关系:本 Task **零 execution 写入**

`AppendAsync` **只插 attempt 一行**,不碰 `wf_node_execution` 的任何列。

把 `Status` / `NextRetryAtUtc` / `CompletedTimeUtc` / `OutputHash` / `ErrorCode` / `Summary` 回写 execution,**属于 Task 6 的回写短事务**,理由是硬的:那次回写**必须带 `WHERE Fence == @myFence` 的 CAS**,否则租约过期后老 owner 的迟到回写会压过新 worker 的结果。fence 语义与 CAS 是 Task 6 的契约(Task 3 射程限制 **R6** 明确记着「租约到期后老 owner 的回写真的被 fence 拒掉 → Task 6 兜」)。本 Task 若顺手更新 execution,等于把**没有 CAS 保护**的写入提前散进来,而且散在一个不知道 fence 的地方。

注释里写清:attempt 行与 execution 结果回写将在 **Task 6 的同一个短事务**里提交(§4.6「结果、变量、历史和 outbox 在同一短事务提交」),但**代码归属不同**——本类只负责 attempt 那一行,事务由调用方起。

#### D7. 基类 / 时间口径 / `DefaultValue` / `ScopeKey` —— 逐条确认这张表具体怎么定

| 契约 | 本表具体怎么定 |
|---|---|
| **基类** | `BaseEntity`,**不是** `DataEntity`。`DataEntity` 带 `IOrgScoped` 全局数据范围过滤器(**只作用于 SELECT**),而本表的读写方是**没有 HTTP 请求上下文的后台 worker**——`IDataScopeContext` 为空会让查询静默返回 0 行,症状伪装成「调度器扫不到活」而不是报错,**且在有 HTTP 上下文的集成测试里可能仍然是绿的**(所以别指望测试发现这个,靠这条 checklist)。与 `WfNodeExecution` / `WfOperationReceipt` 同源。 |
| **机构维度** | 本表**不带** `ScopeKey`(D1 已论证:永远经 `ExecutionId` 到达,父行已有)。若非要带,必须**非空**。 |
| **时间口径** | 两个业务时间列 `StartedAtUtc` / `EndedAtUtc` **全部 UTC,列名一律带 `Utc` 后缀**,值由调用方算好传入(不在 SqlSugar 表达式里内联)。这是**刻意偏离**本仓「业务时间戳走 `GetLocalNow().DateTime`」的惯例,**列名后缀是唯一的护栏**。**硬约束(必写进注释)**:基类审计列 `CreateTime` / `UpdateTime` 仍是 local(AOP 填的),**任何代码都不得把它们与任何 `*Utc` 列做比较或相减**。 |
| **`DefaultValue`** | **全表一列都不写。** 并且必须把理由写进类注释:`DefaultValue` 唯一的作用是让 `DbMaintenanceProvider.AddColumn` 走「先加可空列 → 回填 → 改 NOT NULL」三步序列,**`CREATE TABLE` 路径根本不读它**;本表是本 Task 新建表,没有「存量行升级」这回事,写它只是噪音。**Task 1 那条「非空列必须带 `DefaultValue`」契约管的是加列,不是建表**——Task 3 就是差点在这里被机械套用误判成 P1。 |
| **索引命名** | `uk_wf_node_exec_attempt_no`,对齐 `uk_wf_receipt_identity` / `uk_wf_node_exec_key`。 |
| **交付形态** | `public static class` + 零 DI + `WorkflowSetup.cs` 零改动 + 十件套仍 10 条(与 Task 2/3 同款)。 |
| **实体如何进建表** | 靠 `WorkflowSetup.UseWorkflow` 的**整程序集扫描**(`WorkflowSetup.cs:28`,`options.ApplicationAssemblies.Add(asm)`)。**不需要在任何地方登记实体类型**——任何「把新实体加进某个 typeof 列表」的动作都是错的(那种列表在本仓不存在)。 |

#### D8. `OutputJson` 体积 —— attempt 行不存输出正文

§6.2 原文的要求是:「输出正文、敏感字段和密钥**不直接进入日志**;保存必要摘要、hash 和受控引用。」照做,**定案:存 hash + 截断摘要,不存全文**。

- **`OutputHash`**(`Length = 64`,可空)= `Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result.OutputJson)))`。与 `WfIdentityHash.Compute` / `WfExecutionKey.Compute` 的最后一行**逐字同型**(两处都是 `Convert.ToHexStringLower(SHA256.HashData(...))`)。本仓**没有**通用的 `string → sha256 hex` helper(两个既有 helper 都是结构化入参),**不为此新建 helper**——一行内联即可。`result.OutputJson == null` → 本列 `null`。
- **`OutputSummary` / `ErrorSummary`**:`Length = 512`,对齐 `WfNodeExecution.Summary` 的既有长度。**写入方截断**到 512。
- **不用 `StaticConfig.CodeFirst_BigString`**(仓内 `WfHistory.PayloadJson` / `WfOperationReceipt.ResultJson` 用了它)。理由:attempt 是 append-only、永不删除的表;把任意 handler 的输出正文塞进去,存储与备份成本全部由消费者长期承担,而且这是 PII / 密钥泄漏面最大的一张表(M3b 之后正文里就是模型输出)。正文的去处(流程变量 / execution 行 / 受控引用)由 **Task 6** 决定,不是本 Task 的事。
- **四库可行性与长度上限**:两个摘要列都是 `≤ 512` 的普通变长字符串(SqlSugar 生成 `nvarchar(512)` / `varchar(512)`,SQLite 是 `TEXT` 亲和),`OutputHash` 是定长 64 hex。**四库零方言争议、零大文本类型**(不碰 MySQL `LONGTEXT` / PG `text` / SqlServer `nvarchar(max)` / SQLite 无限 `TEXT` 之间的差异),也不碰任何索引键长上限(这两列都不进索引)。
- **截断是必须实现的,不是可选的**:handler 的摘要是**外部输入**(trust boundary)。600 字的 summary 在 **SqlServer / PostgreSQL 上直接抛**(超长即报错),**MySQL 非严格模式静默截断**,**SQLite 照单全收**——这是本仓最典型的「本地 SQLite 全绿、CI 三条腿红」。截在 C# 侧,四库行为一致。

---

### 2. 改动清单

| 路径 | 新建 / 改 | 改什么 |
|---|---|---|
| `backend/src/TenonAdmin.Workflow/Entities/WfNodeExecutionAttempt.cs` | **新建** | 实体类:`[SugarTable("wf_node_execution_attempt")]` + 1 个唯一索引 `[SugarIndex]` + 9 个业务列 + 类/成员注释(D7 与实现步骤点名的七条必写) |
| `backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionAttemptStore.cs` | **新建** | `public static class`,`SummaryMaxLength` 常量 + `AppendAsync` 单方法 + 私有 `Truncate` |
| `backend/tests/TenonAdmin.Tests/WfNodeExecutionAttemptTests.cs` | **新建** | 6 个 `[Fact]` + 脚手架(`Open` / `NewExecution` / `UniqueKey`,抄 `WfNodeExecutionClaimTests`) |

**三个文件,全部是新建,一个都不能多。** 以下文件本轮 **`git diff` 必须为空**:

- `WorkflowSetup.cs`(动了 → 十件套连锁红)
- `Entities/WfNodeExecution.cs`、`Entities/WfEnums.cs`、`Engine/WfNodeExecutionStore.cs`
- `Abstractions/**`(含 `IWorkflowNodeHandler.cs`)
- `Engine/Operations/**`、`Engine/WorkflowEngine.cs`、`Engine/WfExecutionContext.cs`
- `tests/TenonAdmin.Tests/WfNodeExecutionClaimTests.cs`(Task 3 的测试不许动)
- `backend/Directory.Packages.props`(不加任何依赖;`SHA256` 在 `System.Security.Cryptography`,BCL 自带)

exec 阶段自查:`git status --short` 只应出现上表三个 `??` 新文件;`git diff` 空。

---

### 3. 实现步骤

**步骤 1 — 实体** `backend/src/TenonAdmin.Workflow/Entities/WfNodeExecutionAttempt.cs`

```csharp
using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>(类注释见下方必写七条)</summary>
[SugarTable("wf_node_execution_attempt", TableDescription = "节点执行 attempt 记录")]
[SugarIndex("uk_wf_node_exec_attempt_no",
    nameof(ExecutionId), OrderByType.Asc,
    nameof(AttemptNo), OrderByType.Asc,
    IsUnique = true)]
public class WfNodeExecutionAttempt : BaseEntity
{
    [SugarColumn(ColumnDescription = "所属执行记录 Id")]
    public long ExecutionId { get; set; }

    /// <summary>本次 attempt 序号,1 基;= 领取读回后的 <c>execution.AttemptCount</c>,写入时不得再 +1。</summary>
    [SugarColumn(ColumnDescription = "attempt 序号(1 基)")]
    public int AttemptNo { get; set; }

    [SugarColumn(ColumnDescription = "开始时刻(UTC)")]
    public DateTime StartedAtUtc { get; set; }

    /// <summary>结束时刻(UTC)。非空:一行 = 一次已返回的调用。</summary>
    [SugarColumn(ColumnDescription = "结束时刻(UTC)")]
    public DateTime EndedAtUtc { get; set; }

    [SugarColumn(ColumnDescription = "本次 attempt 的结果类型")]
    public WfNodeExecutionResultType ResultType { get; set; }

    /// <summary>成功时的输出摘要(已截断至 512);失败/回退时为 null。</summary>
    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "输出摘要")]
    public string? OutputSummary { get; set; }

    /// <summary>输出正文的 SHA-256 小写 hex;正文本身不落库(§6.2)。</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "输出哈希(SHA-256 小写 hex)")]
    public string? OutputHash { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "失败错误码")]
    public int? ErrorCode { get; set; }

    /// <summary>失败/回退时的错误摘要(已截断至 512);成功时为 null。</summary>
    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "错误摘要")]
    public string? ErrorSummary { get; set; }
}
```

**类注释必写七条**(缺一条都会被下一轮 review 当成新发现重开):

1. **append-only**:每次真实调用一行,**重试新增、不覆盖旧 attempt**(AI 基石 §4.5「attempt 必须保留每次真实调用」);基类带来的 `UpdateTime` / `UpdateUserId` / `IsDelete` **永不置真**,清理走**保留期策略**而非普通软删除(评审 §4.7);`WfNodeExecutionAttemptStore` 只暴露 `AppendAsync`,不提供更新/删除 —— 与 `wf_history` / `wf_operation_receipt` 同源。
2. **基类为什么是 `BaseEntity` 而不是 `DataEntity`**:`DataEntity` 带 `IOrgScoped` 全局过滤器(只作用于 SELECT),后台 worker 无 `IDataScopeContext` → 静默返回 0 行,症状伪装成「调度器扫不到活」;**并说明为什么本表连 `ScopeKey` 都不带**(永远经 `ExecutionId` 到达,父行 `WfNodeExecution.ScopeKey` 已承载机构维度;没有查询要用它,反规范化只会多一个必须保持一致的写入点)。
3. **`AttemptNo` 1 基,= 领取读回后的 `execution.AttemptCount`,写入时不得再 +1**;点名三处口径(领取 UPDATE 的 `AttemptCount + 1` / `WfNodeExecutionContext.Attempt` / 本列),说明这是经典差一点。
4. **两个业务时间列全 UTC + `Utc` 后缀**,值由调用方传入;**硬约束:不得把基类 local 的 `CreateTime` / `UpdateTime` 与任何 `*Utc` 列做比较或相减**。
5. **全表一律不写 `DefaultValue`**:它只驱动 `DbMaintenanceProvider.AddColumn` 的三步序列,`CREATE TABLE` 路径根本不读它;本表是新建表,没有存量行升级这回事;**Task 1 那条契约管的是加列,不是建表**。
6. **不存输出正文**,只存 `OutputHash` + 512 截断摘要(§6.2「输出正文、敏感字段和密钥不直接进入日志」);§6.2 列的 `Provider` / `Model` / `PromptVersion` / `SchemaVersion` / `PolicyVersion` **归 §七 `wf_ai_decision`**(同一事实不设两个家),`TokenUsage` / `Cost` 待 M3b 以可空列 `ADD COLUMN` 补(四库都接受,`WfHistory.RequestId` 先例)。
7. **崩溃可见性**:`execution.AttemptCount − count(attempt)` = 领了但没返回的次数;`EndedAtUtc` 非空正是为了保住这个口径。

**步骤 2 — Store** `backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionAttemptStore.cs`

```csharp
using System.Security.Cryptography;
using System.Text;
using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>(类/方法注释见下方必写五条)</summary>
public static class WfNodeExecutionAttemptStore
{
    /// <summary>摘要列长度上限,与实体两个 512 列一致。</summary>
    public const int SummaryMaxLength = 512;

    public static async Task<WfNodeExecutionAttempt> AppendAsync(
        ISqlSugarClient db,
        WfNodeExecution execution,
        WfNodeExecutionResult result,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        var succeeded = result.Type == WfNodeExecutionResultType.Succeeded;
        var row = new WfNodeExecutionAttempt
        {
            ExecutionId   = execution.Id,
            AttemptNo     = execution.AttemptCount,   // ← 直接取,绝不 +1
            StartedAtUtc  = startedAtUtc,
            EndedAtUtc    = endedAtUtc,
            ResultType    = result.Type,
            OutputSummary = succeeded ? Truncate(result.Summary) : null,
            OutputHash    = result.OutputJson is null
                ? null
                : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result.OutputJson))),
            ErrorCode     = succeeded ? null : result.ErrorCode,
            ErrorSummary  = succeeded ? null : Truncate(result.Summary),
        };

        await db.Insertable(row).ExecuteCommandAsync();   // Id 由审计 AOP 填雪花
        return row;
    }

    private static string? Truncate(string? value) =>
        value is null || value.Length <= SummaryMaxLength ? value : value[..SummaryMaxLength];
}
```

`Succeeded` 的 `Summary` 落 `OutputSummary`、其余三种落 `ErrorSummary`——与 `WfNodeExecutionResult.Summary` 的既有注释(「落 attempt 的 `OutputSummary`(成功时)或 `ErrorSummary`(失败/回退时)」)**逐字对齐**,不许自创映射。

**方法/类注释必写五条**:

1. **签名里没有 `attemptNo` 形参是刻意的**——差一只可能来自「调用方自己算 attempt 号」,拿掉这个入口就拿掉了这一类 bug;`AttemptNo` 与 `ExecutionId` 取自**同一个 `execution` 对象**,也就杜绝了错配。
2. **本方法不碰 `wf_node_execution` 的任何列**:结果回写(`Status` / `NextRetryAtUtc` / `CompletedTimeUtc` / …)归 **Task 6** 的 fence CAS 短事务(`WHERE Fence == @myFence`),两者将在同一个短事务里提交(§4.6),但代码归属不同。
3. **唯一索引撞了原样抛出,不写 try/catch**——与 `WfNodeExecutionStore.EnsureAsync` 同款理由:半吊子的 catch 在 PostgreSQL 上更糟(事务已 aborted,`25P02`)。撞唯一键意味着「同一 attempt 号写了两次」,那是调用方的 bug,必须炸出来。
4. **摘要截断在 C# 侧**,四库一致(SqlServer/PG 超长直接抛、MySQL 非严格模式静默截断、SQLite 照单全收);handler 的摘要是外部输入,截断是 trust boundary 上的必要防护,不是可省的优化。
5. **零 DI 注册,`public static`**;调用方(Task 6 的 dispatcher)直接经 `ISqlSugarClient` 调用,事务由调用方起。

**步骤 3 — 测试** `backend/tests/TenonAdmin.Tests/WfNodeExecutionAttemptTests.cs`

脚手架逐字抄 `WfNodeExecutionClaimTests`:

```csharp
private static (IServiceScope Scope, ISqlSugarClient Db) Open(WorkflowAppFactory f)
{
    _ = f.CreateClient();                 // 触发宿主启动与 CodeFirst 建表
    var scope = f.Services.CreateScope();
    return (scope, scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
}

private static WfNodeExecution NewExecution(string executionKey) => new()
{
    ExecutionKey = executionKey, ScopeKey = "org-1",
    InstanceId = 1001L, TokenId = 2002L, NodeVisitId = 1L,
    NodeId = "node-1", NodeType = WfNodeType.Approval,
    DefinitionVersionId = 1L, MaxAttempts = 3,
};

private static string UniqueKey() => Guid.NewGuid().ToString("N");
```

**关键纪律**:测试 1/2/5/6 必须走 `WfNodeExecutionStore.ClaimAsync` 拿到读回后的 execution 行,再交给 `AppendAsync`——**不许手工设 `AttemptCount` 后直插**。真走领取才是「三处口径对齐」的真实证据(R5)。测试 3/4 为了构造唯一冲突可以直插 attempt 行(那是在测索引本身)。

---

### 4. 测试清单

新建 `WfNodeExecutionAttemptTests.cs`,**6 个 `[Fact]`**。基线 **307 → 313**。

1. **首次 attempt 的 `AttemptNo` = 1,且等于领取读回的 `AttemptCount`**
   路径:插 execution → `ClaimAsync` → `AppendAsync`。
   断言:`attempt.AttemptNo == 1` **且** `attempt.AttemptNo == claimed.AttemptCount` **且** `attempt.ExecutionId == execution.Id`。
   *为什么两个都要断*:只断 `== 1` 的话,一个「永远写 1」的实现照样绿;只断 `== claimed.AttemptCount` 的话,一个「三处一起错」的实现照样绿。两个一起才钉住 1 基口径。

2. **重试新增一行,不覆盖旧 attempt**(台账点名的那条)
   路径:领取 → `AppendAsync(RetryableFailure(errorCode: 48001, summary: "first"))` → 把 `LeaseExpiresAtUtc` UPDATE 到过去(`var past = now.AddMinutes(-1);`,先落局部变量)→ 再 `ClaimAsync`(`AttemptCount` 变 2)→ `AppendAsync(Succeeded(...))`。
   断言:该 `ExecutionId` 下 **2 行**;`AttemptNo` 分别是 **1、2**;**第 1 行的 `Id` 未变、`ResultType` 仍是 `RetryableFailure`、`ErrorCode` 仍是 48001、`ErrorSummary` 仍是 `"first"`**(证明是新增而非 upsert/覆盖)。

3. **唯一索引 `(ExecutionId, AttemptNo)` 真的挡住重复**
   同 `ExecutionId` 同 `AttemptNo` 直插第二行 → 抛异常;表内该组合仍 **1 行**。写法仿 Claim 测试 #7(`try/catch` 捕到 `Exception? failure`,`Assert.NotNull(failure)` + 计数)。

4. **两个不同 execution 各自都可以有 `AttemptNo = 1`**
   有鉴别力:**专挡把唯一索引写成 `AttemptNo` 单列**的实现。断言两次 append 都成功,两行 `AttemptNo` 都是 1、`ExecutionId` 不同。

5. **四种结果的列投影 + 超长摘要截断**(一个方法内,四次 append)
   参数**互不相同且有辨识性**(照 Task 2 review P2-2 的教训,防参数错位):
   - `Succeeded(outputJson: "{\"a\":1}", summary: "ok")` → `ResultType == Succeeded`、`OutputSummary == "ok"`、`ErrorCode == null`、`ErrorSummary == null`、`OutputHash` 等于**测试内现算**的 `Convert.ToHexStringLower(SHA256.HashData(...))`(不硬编码常量)。
   - `RetryableFailure(errorCode: 48001, summary: "r")` → `ErrorCode == 48001`、`ErrorSummary == "r"`、`OutputSummary == null`、`OutputHash == null`。
   - `ManualFallback(errorCode: 48002, summary: "m")` → 同型,码 48002。
   - `TerminalFailure(errorCode: 48003, summary: new string('x', 600))` → `ResultType == TerminalFailure`、`ErrorCode == 48003`、**`ErrorSummary!.Length == 512`**。
   截断断言的是**产品代码的 C# 行为**(不是数据库列宽强制),所以它在 SQLite 上**不是恒真断言**。

6. **`AppendAsync` 处在被回滚的事务里 → 一行不留**
   仿 Claim 测试 #15:`db.Ado.UseTranAsync` 内 append 后抛异常强制回滚,`Assert.False(tran.IsSuccess)`,回滚后按 `ExecutionId` 查 **0 行**。
   这是「attempt 与结果落在同一短事务」(§4.6)在本轮唯一可测的那一半。

#### 明确不值得写、不许拿来凑数的

- **枚举成员数 / 数值断言**(`WfNodeExecutionResultType` 有几个成员、值是几)——套套逻辑;Task 2 已钉住 C# 侧,测试 5 的投影断言真被改坏时会先红。
- **「两个 worker 真并发写同一个 `(ExecutionId, AttemptNo)`」**——单线程构造不出真并发,与 `WfVersionCasTests` 开头那段射程声明逐字同型。可达的是唯一冲突分支,测试 3 已覆盖。
- **列宽 / 中文往返 / 唯一索引在四库上真被建出来(查系统表元数据)**——SQLite 类型亲和性下是**恒真断言**(`WfPersistenceContractTests` 已实测记录),归 **Task 9**。测试 3 断言的是「插第二行会抛」这个**行为**,不是索引元数据长什么样,两回事。
- **「存量行 `ADD COLUMN NOT NULL`」升级契约**——本表是新建表,走不到那条路径;归 Task 9。
- **「本 Task 零 DI 注册 / 十件套仍 10 条」断言**——断言「什么都没发生」是噪音;既有十件套测试自己会红。
- **反射断言 `WfNodeExecutionAttemptStore` 没有 Update / Delete 方法**——API 面的复读,编译期就是防线;与 Task 2 review 已判 P3-1(「字段名单快照测试」收益低于噪声)同一类。
- **`IsDelete == false` / `UpdateTime` 未被写 / `CreateTime` 非默认值**——没人写过它们,恒真。append-only 的真实防线是「Store 根本不提供那些方法」+ 唯一索引,不是这类断言。
- **重测 `ClaimAsync` 的领取语义**(租约窗口、fence 递增、终态不可领)——Task 3 的 8 条已覆盖,本轮只把它当前置动作使用,不许在本文件里复读。
- **`OutputHash` 与硬编码 SHA-256 常量比对**——把实现抄进测试;用测试内现算值比对即可。
- **「`AppendAsync` 没有改 execution 行」的断言**——又一个「什么都没发生」;D6 是代码归属纪律,靠 review 和改动清单兜住。

---

### 5. 陷阱(按 exec 最可能踩的顺序)

1. **`AttemptNo` 差一。** `AttemptNo = execution.AttemptCount`,**句号**。看到 `+ 1` 就是错的;给 `AppendAsync` 加一个 `int attemptNo` 形参**也是错的**(那正是把差一的入口重新打开)。测试 1 必须同时断言 `== 1` 和 `== claimed.AttemptCount`,少一个都留后门。
2. **顺手接 dispatcher / 顺手更新 execution 行。** `AppendAsync` 只插一行。产品代码里出现任何 `db.Updateable<WfNodeExecution>()` 都是越界(测试里为造过期租约而 UPDATE `LeaseExpiresAtUtc` 是允许的,Task 3 测试已有先例)。回写归 Task 6,因为它必须带 fence CAS。
3. **误用 `DataEntity`。** 症状是「扫不到活」而不是报错,**且在有 HTTP 上下文的集成测试里可能仍是绿的**——别指望测试发现,靠这条 checklist。
4. **机械套 `DefaultValue`。** 非空列**不写** `DefaultValue`,并且**必须把理由写进类注释**,否则 review 会按 Task 1 的加列契约误判成 P1(Task 3 就差点)。
5. **把 `OutputJson` 正文塞进表**,尤其顺手用 `StaticConfig.CodeFirst_BigString`(仓内 `PayloadJson` / `ResultJson` 就是那么写的,很容易照抄)。只存 `OutputHash` + 512 截断摘要。
6. **唯一索引写成 `AttemptNo` 单列**,或漏掉 `IsUnique = true`。测试 4 专挡前者,测试 3 专挡后者。
7. **忘了截断摘要。** SQLite 上全绿,mysql / postgres / sqlserver 三条腿红——本仓最典型的「本地绿 CI 红」。
8. **在 SqlSugar 表达式里内联 `DateTime`**(zh-CN 下被格式化成 `下午` 字面量炸 SQL,`near "下午"`)。产品代码走 `Insertable` + 形参,天然安全;**测试里 UPDATE `LeaseExpiresAtUtc` 时必须先落局部变量**(抄 `var past = now.AddMinutes(-1);`)。
9. **把基类 local 的 `CreateTime` 与 `*Utc` 列比较 / 相减**(比如想断言「开始时间在创建时间之后」或算耗时)。注释里禁,测试里也别写。
10. **动了 `WorkflowSetup.cs` 或加了 DI 注册** → 十件套从 10 条变样,连锁红。实体进 CodeFirst 建表靠整程序集扫描,**什么都不用登记**;本仓不存在「实体类型列表」这种东西,别去找、更别去建。
11. **测试里绕过 `AppendAsync` 直接 `db.Insertable(attempt)` 造数据。** 测试 3/4 为测索引可以直插;测试 1/2/5/6 必须走 `AppendAsync`,否则测的是测试自己写的投影,不是产品代码。
12. **为 SHA-256 新建一个 helper 类 / 往 `WfIdentityHash` 里加通用方法。** 一行内联,与两个既有 helper 的最后一行同型即可;`WfIdentityHash` 是 Task 3 之前定案的结构化 helper,不要扩它的职责。
13. **把 `EndedAtUtc` 做成可空**(「万一崩溃了要记一行只有开始时间的」)。那会毁掉 `AttemptCount − count(attempt)` 的崩溃计数口径,而且本轮根本没有「开始时插行」的写入点。

---

### 6. 射程限制

| 测不到的不变量 | 为什么 | 谁兜住 |
|---|---|---|
| `AttemptNo` 三处口径的**第三处**(`WfNodeExecutionContext.Attempt`)真被喂成同一个值 | 本轮首次能对照**两处**(领取读回的 `AttemptCount` ↔ `AttemptNo`),Task 3 射程限制 R5 兑现一半;Context 的构造点在 dispatcher,本轮不存在 | **Task 6**(构造 Context 处) |
| attempt 与 execution 结果回写落在**同一个短事务**(§4.6) | 本轮只有 attempt 这一半;测试 6 只证明它参与外围事务,证不了「另一半也在同一个事务里」 | **Task 6**(回写短事务)/ **Task 7**(端到端) |
| 老 owner 的迟到 attempt / 迟到回写真被 fence 拒掉 | 本表不带 `Fence`,拒绝逻辑在 execution 行的 CAS 上;Task 3 的 R6 同一条 | **Task 6**(`WHERE Fence == @myFence`)/ **Task 7**(崩溃恢复端到端) |
| 唯一索引 / 列宽 / 中文 / `ResultType` 数值在 **MySQL / PostgreSQL / SqlServer** 上的真实行为 | 本地默认 SQLite,类型亲和性让列宽类断言恒真 | **Task 9**(四库契约)+ CI 矩阵三条全量腿 |
| 摘要截断在**真正会抛的库**上确实避免了异常 | SQLite 不抛,本地测不出「没截断会炸」 | **Task 9** + CI 的 mysql / postgres / sqlserver 腿 |
| 「远程调用不在数据库事务内」(§4.6 / §4.8 验收线) | 本轮没有远程调用,也没有 dispatcher | **Task 6** |
| append-only 在**引擎全链路**下成立(没有第二条路径去 UPDATE / DELETE attempt) | 本轮唯一写入点就是 `AppendAsync`,不存在第二条路径可违反 | **Task 6/7**(接线后)+ 保留期清理策略(M3a-2 之后) |
| `execution.AttemptCount − count(attempt)` 真能反映崩溃次数 | 需要真的崩一次;本轮没有 worker 进程 | **Task 7**(崩溃恢复) |
| `TokenUsage` / `Cost` 的可空 `ADD COLUMN` 升级路径 | 列还不存在(D1 定案推迟到 M3b) | **M3b** |
| handler 可替换性 / 第一条 DI 注册线 | 本 Task 零 DI 注册,十件套保持 10 条是正确的 | **Task 8** |

---

### 7. 闸门

过滤器写法不许改:

```
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test  backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"
```

预期数字:

- **build**:0 error / 0 warning。
- **test**:**313 通过 / 0 失败 / 0 跳过**(基线 307 + 新增 6)。
- `WorkflowReplaceabilityTests` 的十件套仍 **10** 条,且**本轮不应有任何改动触及它**——**如果它变了,说明有人动了 `WorkflowSetup.cs`,直接回退**。

exec 阶段收尾自查(三行):

```
git status --short          # 只应有 3 个 ?? 新文件(见改动清单)
git diff                    # 必须为空
```

## Tasks

> 任务顺序 = 依赖顺序。编号稳定;`## Log` 引用任务号。

- [x] **1. `NodeVisitId` 贯穿 + `wf_history` 补字段**:`WfToken`/`WfTask`/`WfHisTask`/`WfHistory`/`WfCc` 加 `NodeVisitId`(每次进新节点生成,停留期间不变,与 `EnterNodeOp` 的 token 级 CAS 同一事务写入);`wf_history` 补 `TokenId`/`Sequence`(实例内单调递增,并发写入方式待 plan 定案)/`ActorType`/`ActorUserId`/`PayloadVersion`(`RequestId` 已在 M2c 做完,不重做)。这是后续所有 execution 相关表「稳定身份」的地基,必须先做。
- [x] **2. `IWorkflowNodeHandler` SPI + Context/Result 类型**:定义最小 Interface(`ExecuteAsync(WfNodeExecutionContext, CancellationToken) -> WfNodeExecutionResult`);`WfNodeExecutionContext` 只含不可变快照(tenant/org、定义版本、实例、token、节点配置、变量/证据快照、`ExecutionKey`、attempt、deadline),不泄漏 SqlSugar 实体/DB session;`WfNodeExecutionResult` 是 `Succeeded`/`RetryableFailure`/`ManualFallback`/`TerminalFailure` 的显式判别联合或枚举+payload。附一个 `FakeNodeHandler` 参考实现(可配置返回哪种结果,供后续 Task 当测试替身)。**本 Task 不接入引擎**,纯类型/接口定义。
- [x] **3. `wf_node_execution` 实体 + `ExecutionKey` 唯一约束 + lease/fence CAS 领取**:新增实体与表(字段参照数据库评审 §六 6.1),`ExecutionKey` 唯一索引;短事务领取逻辑(CAS 更新 lease owner/expiry + fence token 递增),仿 M2c `WfOperationReceipt`/`WfInstance.Version` 的先例。本 Task 交付「能领取、能占位」,不接调度器。
- [x] **4. `wf_node_execution_attempt` 实体 + append-only 记录**:新增实体与表(字段参照 §六 6.2),写入路径只增不改不删;至少一条测试证明「重试不覆盖旧 attempt,而是新增一行」。
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

### Task 2 review(Round 7,Opus 自审 + 26 变异点)

**P1:无。** 最危险的两条路实测都有防线:M1 四个工厂的 `Type` 逐一改错**全部转红**(复制粘贴错位挡住了);M4a/M4b/M4d 三个分支(`PrimaryId` 派生 / `ISqlSugarClient` / `SqlSugar` 命名空间前缀)**全部转红**(Task 6「顺手把实体塞进去」的腐化路径挡住了)。三条越界风险另行核对均干净:`grep -rln FakeNodeHandler backend/src/` 命中 0;`backend/src/` 里 `IWorkflowNodeHandler` 只出现在自己的定义文件(**零 DI 注册**);`git diff 21085e1~1 21085e1 -- Engine/ WorkflowSetup.cs` 输出为空。Release build 0 错误,新增三文件**零警告**(XML cref 全部解析成功)。

- [x] **P2-1｜第 1 条实体守卫有泛型洞:`IReadOnlyList<WfInstance>` 能溜过去**(`WfNodeHandlerContractTests.cs:17-30`)。该断言只检查 `prop.PropertyType` 本身(外加 `Nullable.GetUnderlyingType` 一层剥壳),不看泛型实参、不看数组元素类型。M4c 实测:往 Context 加 `IReadOnlyList<WfHistory>? History` → **12/12 全绿**。为什么要紧:Plan 把这条称作「本 Task 最有价值的一条」,守的是 Task 6 的腐化路径;而 Task 6 真要「顺手塞实体」,塞**集合**(历史列表 / 同节点任务列表)恰恰比塞单个 `WfInstance` 更自然——现在这条断言只挡住了两种写法里比较笨的那种。**修法**(只改测试,约 5 行):把类型检查抽成递归本地函数,`type.IsGenericType` 时展开 `GetGenericArguments()`、`type.IsArray` 时展开 `GetElementType()`,对每个展开出的类型跑同样三条断言。 **(Round 8 已修,commit `39e9d90`:抽出递归本地函数 `AssertNoSqlSugarLeak`,展开 `GetGenericArguments()`/`GetElementType()`,带 `HashSet<Type> visited` 防自引用泛型无限递归。协调者独立验证——**刻意用子代理没试过的形状**:数组 `WfToken[]?` 与双层嵌套 `IReadOnlyDictionary<string, IReadOnlyList<WfTask>>?`,各自单独测试**均转红**,证明递归穿透数组与两层泛型。)**
- [x] **P2-2｜四个工厂的 payload 管线完全没有断言,只钉住了 `Type`**(`WfNodeHandlerContractTests.cs:33-49`)。M11(`RetryAfter`)、M12b(`OutputJson`)、M13(`Summary`)、M14(`ErrorCode`)四次「把工厂参数丢掉」**全部 12/12 绿**——测试 2 四个工厂全用无参调用,只比对 `Type`。为什么要紧:这与测试 2 想挡的是**同一类**复制粘贴静默 bug,只覆盖了一半。一个掉了的 `Summary = summary` 会让 Task 4 的 `attempt.ErrorSummary` 永远为空、Task 8 的 Webhook 错误原因整条丢失,而 dispatcher 一样忠实照做、测试一样全绿——爆炸半径与 `Type` 错位同一量级。**修法**(不新增测试条数,把测试 2 的四次调用改成带辨识性实参,约 8 行):`RetryableFailure(errorCode: 48001, summary: "s", retryAfter: TimeSpan.FromSeconds(7))` 后逐个断言四个 payload 字段;`Succeeded`/`ManualFallback`/`TerminalFailure` 同理。 **(Round 8 已修,commit `39e9d90`:四个工厂改用互不相同的辨识性实参(errorCode 48001/48002/48003 各异,防参数错位),每个 payload 字段都断言回传;保留 `Succeeded()` 的两条 null 检查。**测试方法数不变仍 12**。协调者独立验证:变异 `RetryAfter = retryAfter → null`(即 review 阶段全绿的 M11)**转红**,`Expected: 00:00:07 / Actual: null`。)**

**P3(挂账,不阻塞勾选):**

- **P3-3｜`ExecuteAsync` 返回 `null` 没有被排除。** NRT 只在开了 nullable 的消费者程序集里有效;SPI 注释没写「不得返回 `null`」。建议 Task 6 的 dispatcher 显式挡一下(或接口注释补一句),成本一行。
- **P3-4｜两个 handler 声明同一个 `NodeType` 时 `FirstOrDefault` 静默取先注册的那个。** 注释讲了 `TryAddEnumerable` 按实现类型防重,但没讲「不同实现类型抢同一节点类型」这种冲突。归 Task 8(第一条注册线)时点明。
- **P3-5｜两条 rationale 只留在台账、没进注释**:`HandlerVersion` 的挂账(将来以默认接口成员追加)与「用 `sealed class` 而非 `record`,因为 `with` 会留一条绕过工厂的后门」。这两条恰是下一个实现者最可能踩反的(有人会顺手改成 `record`,或直接往接口塞 `string HandlerVersion { get; }` 打断 Task 8)。各一行注释即可。
- **P3-1(review 明确建议「接受而非修」)｜14 字段名单本身没被钉住。** M10(删 `BusinessKey`)、M7a(`Attempt` 去掉 `required`)都保持全绿;只有 `required` 字段的**新增**被 `BuildContext` 的编译错误挡住(M7b 实测 CS9035)。写字段名单快照测试恰好落进 Plan「不值得写的」清单里那种复读,收益低于噪声。**记录在案,免得下一轮 review 当成新发现重开。**
- **P3-2｜测试 8 的反射断言在运行期永远不可能红。** M6a/M6b 证明 `DeadlineAtUtc` 类型一改,测试文件自己先编译不过(`.Offset` CS1061 / CS0029)。它是文档 + 编译期防线,不是运行期断言。**编译期也是真实防线**且这行是 Plan 明文要求的,不算凑数,但报告里不应把它算作「一条有鉴别力的运行期断言」。测试 8 的另一半(`Offset == TimeSpan.Zero`)经变异 V2 实测**有**鉴别力(挡「有人在 Context 里改写时区」)。

**核对结论(review 要求的第 2、3 项):**
- **注释不是敷衍摘要**,Plan 点名必须写进的四条**全部落地**(不得推进 token/写任务状态/自开事务 + §4.5/§4.7 引用;取消走 OCE、无 `Cancelled`、OCE ≠ `TerminalFailure`;结果枚举 vs Task 3 状态机是两个类型;数值只追加不重排 + §6.2 列名)。额外还写到位:不含实体/`ISqlSugarClient` 硬约束、烂 JSON 免疫且措辞对齐 `IWfConditionEvaluator`、`NodeProps` 快照纪律并**诚实标注「靠代码审查兜住,不是类型系统强制」**、`Attempt` 1 基三处口径、`ExecutionKey` 不透明、`DateTimeOffset` 而非 `DateTime` 的理由、私有构造 → 不可整体反序列化、`TryAddEnumerable` + `FirstOrDefault` 分发路径与「键用枚举不用字符串」的理由。
- **14 个字段与 Plan 表格逐一吻合**(名字、类型、可空性、required、**顺序**全对):`Attempt` 是 `required int`、`DeadlineAtUtc` 是 `required DateTimeOffset`、`NodeVisitId` 是 `long?` 非 required、`OrgId` 是 `long?`、**没有 `TenantId`**、没有 `TimeProvider`/`ISqlSugarClient`/evidence。
- **12 条测试无一空转**,逐条实测各自的转红点(T1→M4a/b/d,T2→M1a–d,T3→M2a/b,T4→M3,T5×4→M1/M9,T6→M5,T7→M8,T8→V2,T9→V1)。Plan 黑名单四项**一项都没被偷偷写进来**;T5 用 `Assert.Same` 而非相等性,写法正确;T1 开头的 `Assert.NotEmpty(props)` 是防「循环空转」的正确写法。

**Task 2 射程限制(本轮客观测不到,谁兜住):**

| 契约 | 为什么测不到 | 谁兜住 |
|---|---|---|
| `Attempt` **1 基**口径 | Task 2 只有一个 `required int` 自动属性,**零可观测差异**(M7a 证明连 `required` 都没人断言) | Task 3(`AttemptNo` 列)/ Task 4 / Task 6(领取处 +1),三处对齐时才有观测点 |
| `NodeProps` 快照纪律 | 本轮没有 dispatcher,无调用点可变异;注释已自认「靠代码审查兜住」 | Task 6 |
| handler 不得推进 token / 写任务状态 / 自开事务 | 纯注释约束,本轮零实现可违反 | Task 6(事务边界)/ Task 7(全链路) |
| dispatcher `switch` 的 `default:` 臂抛异常(「无 0 值」的下半句) | 本轮只钉住枚举侧(M2a 红),消费侧不存在 | Task 6 |
| OCE ⇒ 可被重新领取 | T6 只证明「fake handler 抛 OCE」,不能证明「抛了之后 lease 会释放并被重领」 | Task 6(异常不得归进结果分支)/ Task 7(崩溃恢复) |
| `ExecutionKey` 构成与不透明性 | Task 2 明确不对形状做假设 | Task 3 |
| `DateTimeOffset`(SPI) ↔ `DateTime`(列)的那次转换 | 列还不存在 | Task 6(转换点) |
| 枚举数值真的进了 `attempt.ResultType` 列 | 表还不存在,本轮只钉住 C# 侧数值 | Task 4;四库口径归 Task 9 |
| handler 可替换性(`TryAddEnumerable` 追加语义) | 本 Task 零 DI 注册,「十件套」保持 10 条是正确的 | Task 8(第一条注册线 + 可替换性用例) |


### Task 3 review(Round 11,Opus 自审 + 13 变异点)

> 手法:11 个必做变异点 + 子代理自加 2 个(M12/M13)。每次 `git diff -U0` 确认落盘 → 跑测试 → 只 `git checkout -- <单文件>` 复原(全程未用 `git checkout .`/`git stash`)。**11 个钉住、2 个存活**。协调者独立交叉验证:工作区干净、HEAD 未动、无 stray commit;两个存活点**用与子代理不同的手法**(读断言而非跑变异)各自坐实。

- [x] **P1-1｜`RetryScheduled` + `NextRetryAtUtc == null` 的不可领取性零覆盖**(产品代码 `WfNodeExecutionStore.cs:74`,缺口在 `WfNodeExecutionClaimTests.cs:139` 的 `#12`)。变异 M2 把 `NextRetryAtUtc <= nowUtc` 放宽成 `(NextRetryAtUtc == null || NextRetryAtUtc <= nowUtc)` → **15/15 全绿**。根因:`#12` 只造了 `now.AddMinutes(10)`(未来,`:147`)与 `past`(过去,`:156`)两种行,**从未构造过 `NextRetryAtUtc == null`**。协调者独立核实:`grep -n NextRetryAtUtc` 在整个测试文件只有这两处赋值,确认无 null 行。**为什么是 P1**:这正是 Plan 陷阱 7 点名的场景——「刚被标记 `RetryScheduled`、退避时间还没算出来」的行处于 `(RetryScheduled, null)`,产品代码今天正确地拒绝领取它,但无任何测试保护;Task 6 一次 `?? DateTime.MinValue` 或为「保险」放宽 null,就会让这类行被立刻抢跑,且 CI 全绿。**修法**:在 `#12` 追加一段或新增一条测试,插入 `Status = RetryScheduled, NextRetryAtUtc = null` 的行 → `ClaimAsync` 断言 `Assert.Null(...)`。约三行。 **(Round 12 已修,commit `68809c0`:新增独立测试 `Retry_scheduled_row_with_no_retry_time_is_never_claimable`,307 = 306 + 1。产品代码零改动。协调者独立复验——**刻意换成子代理没试过的形状**:不用 `(== null ||)`,改用 `(e.NextRetryAtUtc ?? DateTime.MinValue) <= nowUtc`,即台账原文预言的「Task 6 一次 `?? DateTime.MinValue`」那种真实失误 → **转红**,`Assert.Null() Failure: Value is not null`,失败 1/通过 9。)**
- [x] **P1-2｜`leaseDuration` 参数完全不被任何断言约束**(产品代码 `WfNodeExecutionStore.cs:61`,缺口在 `WfNodeExecutionClaimTests.cs:79` 的 `#9`)。变异 M12(子代理自加)把 `var leaseUntilUtc = nowUtc + leaseDuration;` 改成 `= nowUtc;`(租约在被领取的同一刻即到期,`leaseDuration` 彻底失效)→ **15/15 全绿**。三条相关测试各自漏掉它的原因**不同**,值得记牢:①`#9:79` 只写 `Assert.NotNull(claimed.LeaseExpiresAtUtc)`——只查非空、不查值;②`#10`(`:97-99`)第二次领取传的是**同一个 `now`**,而 WHERE 用严格小于,租约窗口被压成零时 `LeaseExpiresAtUtc == now`、`now < now` 为假 → 仍领不到 → 仍绿,**这条名叫 "within the lease window" 的测试其实从未依赖真实租约窗口**;③`#11`(`:121-125`)自己 UPDATE 覆盖掉了 `ClaimAsync` 写入的值。协调者独立核实:领取后对 `LeaseExpiresAtUtc` 的唯一断言就是 `:79` 那句 `NotNull`。**为什么是 P1**:与 M1 是同一机制的两条腿——M1 管「租约过期后必须能重新领取」(已钉住),本条管「租约有效期内必须领不走」(**没钉住**)。若 `leaseDuration` 被误改或被忽略(Task 6 传 `TimeSpan.Zero`、或有人重构掉这行),每行在被领取的同一毫秒即刻过期,任意 worker 都能立刻抢走同一个 execution,**lease 机制静默归零**;fence 只能事后拒绝老 owner 的迟到回写,**拦不住两个 worker 同时真正执行同一节点**——而「节点可靠执行、不重复执行」正是整个 M3a-1 的立项理由。**修法**(建议两条一起做):①把 `#9:79` 换成 `Assert.True(claimed.LeaseExpiresAtUtc > now)`(或用 `Assert.Equal(now.AddMinutes(5), ..., TimeSpan.FromSeconds(1))` 重载避开 SQLite 时间精度抖动);②把 `#10` 第二次领取的 `nowUtc` 从 `now` 改成 `now.AddMinutes(1)`(仍在 5 分钟租约内),让它名副其实。 **(Round 12 已修,commit `68809c0`:`#9` 的 `Assert.NotNull` 换成 `Assert.Equal(now.AddMinutes(5), claimed.LeaseExpiresAtUtc!.Value, TimeSpan.FromSeconds(1))`(用容差重载避开 SQLite 时间精度抖动);`#10` 第二次领取的 `nowUtc` 从 `now` 改成 `now.AddMinutes(1)`。产品代码零改动。协调者独立复验——**刻意用比子代理更强的形状**:不用 `= nowUtc`(租约压成 0),改用 `nowUtc + TimeSpan.FromSeconds(1)`(**正数但忽略 `leaseDuration` 量值**——若断言只写成 `> now` 这条会漏掉,只有查值才抓得住)→ **两条同时转红**:`#9` `Assert.Equal() Failure` 差 00:04:59 > 00:00:01 容差、`#10` `Assert.Null() Failure` 第二次领取成功(`AttemptCount = 2`)。这同时证明了 `#9` 查的是**值**而非仅仅非空/大于,且 `#10` 现在**真正依赖租约窗口**。)**
- [x] **P2-1｜`ScopeKey` 列可空,与同一文件里「显式非空」的承诺、以及「与 `WfOperationReceipt` 同源」的声明直接矛盾**(`WfNodeExecution.cs:43-44` vs 同文件 `:13`)。类注释写「机构维度改由本表**显式非空**的 `ScopeKey` 承载,与 `WfOperationReceipt` 同源」,但列实际是 `[SugarColumn(Length=64, IsNullable=true)] public string? ScopeKey`;被援引为同源的兄弟表(`WfOperationReceipt.cs:27-28`)恰恰是**非空** `public string ScopeKey { get; set; } = "";`,且其注释明说「不允许 null 与空串产生两个 identity」。协调者独立核实:三处原文均已比对,矛盾属实。**为什么要修**:`WfExecutionKey.Compute`(`:46`)把 null/空白 scope 归一化成哨兵 `"-"` 再参与哈希,而可空列允许把**原始 null** 落库 → 同一行里诊断列存 `null`、`ExecutionKey` 却按 `"-"` 算,排查时对不上——这正是 `WfIdentityHash.NormalizeScopeKey`(`:73-74`)注释白纸黑字警告过的情形,而这些注释的唯一目的就是给 Task 6 当护栏。**定为 P2 而非 P1**:本轮零写入点,尚无实际数据,Task 6 动工前修掉即可。**修法(推荐)**:对齐兄弟表——去掉 `IsNullable = true`,改成 `public string ScopeKey { get; set; } = "";`,属性注释点明「写入方必须用 `WfIdentityHash.NormalizeScopeKey` 的返回值落库」。(反过来改注释也行,但那要解释清楚诊断列与 identity 不一致时怎么排查,成本更高。) **(Round 12 已修,commit `68809c0`:改成 `[SugarColumn(Length = 64, ColumnDescription = "机构/租户范围键(无机构用哨兵)")] public string ScopeKey { get; set; } = "";`,与兄弟表逐字同款;注释改写为「非空——写入方必须用 `NormalizeScopeKey` 的返回值落库(无机构 → 哨兵),不允许 null 与空串产生两个 identity」。协调者独立复核:①**没有因为改成非空就顺手加 `DefaultValue`**(`grep -c 'DefaultValue *=' ` = **0**,本表建表契约守住);②`git show --stat` 限定 `Engine/` 为空 → `WfNodeExecutionStore` 一行未动;③P3-3 一并改准(类注释由「5 个预留列」改为列全 8 个),**列一个都没动**。)**

**点名四项的结论(三项无问题,均经协调者独立复核)**:①`EnsureAsync` **无** try/catch(`:20-33` 只有查→插→返回,唯一冲突时异常原样抛出,符合本轮定案);②`nowUtc`/`leaseUntilUtc`/`owner` **确实先落局部变量**(`:61`),`SetColumns` lambda 内无内联 `DateTime` 运算;③**无**多余的 `ClearFilter`——全文件唯一命中是 `:79` 的**注释**「本表非 IOrgScoped(BaseEntity),读回无需 ClearFilter」,不是真调用;④实体注释承诺的四件事**全部在位**(基类选型理由 `:10-14`、`*Utc` 不得与 local 审计列比较相减 `:19-24`、不写 `DefaultValue` 的机制解释 `:15-18`、预留列标注 `:25-26` + 每列各一遍)。

**P3 / 观察(不阻塞勾选,记录备查)**

- **P3-1｜`#4 Different_field_positions_do_not_collide` 名不副实。** M7 在产品代码里对调 `instanceId`/`tokenId` 的拼接位置后,只有快照测试转红,`#4` 依旧绿——它交换的是**入参取值**而非拼接顺序,「交换两个入参得到不同 hash」这个性质在产品代码顺序对调后同样成立。它能抓到字段被求和/被丢弃,但证明不了自己注释里写的「字段顺序生效」。
- **P3-2｜`#2 Missing_node_visit_id_normalizes_to_the_sentinel_...` 只兑现了后半句。** M8 把 null-visit 哨兵从 `"-"` 换成 `"_"`,同样只有快照转红。`#2` 只断言 `NotEqual`,任何哨兵取值都满足,测不到「归一化为**该**哨兵」。对照 `#3`(scope 那条)写法是对的——它显式断言 `Compute("-", ...) == Compute(null, ...)` 把哨兵钉死,`#2` 缺的正是这一手。
- **P3-1/P3-2 合起来的含义(重要)**:`WfExecutionKey` 这个「发包后不可逆的契约」目前是**快照单点保护**——顺序、哨兵、分隔符三项保证全压在 `Snapshot_of_a_known_tuple_is_frozen` 一条测试上(M7/M8/M9 都只有它转红)。快照是这类契约的标准手法、可以接受,但**一旦有人在它转红时「更新期望值」而不是撤回改动,就没有第二道防线**。文件头 `:8-9` 已把这条纪律写成注释。
- **P3-3｜类注释说「5 个预留列」,实际 8 个列标了「建表期预留」**(`DeadlineAtUtc:79`、`ErrorCode:120`、`Summary:124` 三列额外标了)。**协调者裁定:不是范围外增列**——这三列在 Round 10 exec prompt 的「19 列构成」里由协调者明文列出并批准,只有类级注释 `:25-26` 那句「5 个」措辞过时。**列不动,只改注释**(改成列全 8 个,或改述为「凡标注『建表期预留』的列」)。
- **P3-4｜本表在 SqlServer 腿的 PR 期不跑。** `.github/workflows/backend-ci.yml:147` 的 `TEST_FILTER` 白名单含 `WfPersistenceContractTests`,不含 `WfNodeExecutionClaimTests`。新表的 T-SQL DDL 与带 `DateTime` 参数的条件 UPDATE 只在 nightly 全量验证。既定策略下属正常;仅提示这个类只有 9 条轻量单表测试,加进白名单成本很低——**归 Task 9 一并决定**。
- **P3-5｜`ClaimAsync` 把事务责任外推给调用方,与同包兄弟不一致。** 注释 `:45-47` 声明「必须在事务内才成立」,而 `WfHistorySequence.WriteSystemRowAsync:41` 面对同款约束选择自己 `UseTranAsync` 包住。Plan 定案就是 Task 6 起事务,**不算偏离**;仅记录包内不一致供 Task 6 决定(考虑到 Task 6 大概率要把「领取 + 写 attempt 行」原子化,当前外推设计可能反而是对的)。
- **P3-6｜`CancellationToken` 只在入口检查、不下传 SqlSugar——不是 finding。** 与 `WfHistorySequence.WriteSystemRowAsync:41` 完全一致,是本仓既有约定,非本 commit 引入。

**B 组空转审查结论**:Plan 明令禁写的六类**一条都没犯**(无枚举成员数/数值断言;`#10` 是严格串行无真并发;无列宽/中文往返断言,`#7` 是真插两行看行为、M11 已证其有效;无「存量行 ADD COLUMN NOT NULL」升级契约;无「零 DI 注册」断言;`#6` 断的是 SHA-256 小写 hex **输出格式**而非「长度 ≤ 索引上限」那种恒真断言)。**无「删掉产品代码照样绿」的假断言测试**——15 条都真在测东西,M1/M3/M4/M5/M6/M7/M9/M10/M11/M13 共 10 个变异点分别被它们杀掉;另手工验了两条可疑的(`#14` 幂等由 M13 证真、`#8` 默认值依赖实体初始化器 + 枚举刻意无 0 值,删初始化器即红)。**无执行顺序依赖、无跨测试共享 DB 状态**(每条 `new WorkflowAppFactory()`,`DbPath` 是 `tenon-wf-it-{Guid:N}.db` 独立临时 SQLite,`Dispose` 时 `TestDb.Cleanup`;无 `IClassFixture`/`ICollectionFixture`/静态字段/`[Collection]`;`ExecutionKey` 用 `Guid.NewGuid()` 不撞唯一索引;后台调度器已在工厂 `:32` 关掉)。**值得记牢的一条元教训**:exec 老老实实避开了全部六类禁令,仍漏了两个核心行为——**缺口是「缺断言」而非「假断言」,是变异测试抓到的,清单核对抓不到。**

### Task 4 review(Round 15,Opus 自审 + 14 变异点)

> 手法:10 个必做 + 子代理**自加 4 个**(M11–M14)。每次 `git diff -U0` 确认落盘 → 跑测试 → 只 `git checkout -- <单文件>` 复原。**12 个钉住、2 个存活(同一条 P2)、1 个变异被判无效并换形状重做**。**无 P1。** 协调者独立交叉验证:工作区干净、`git diff` 空、HEAD 未动;P2-1 **刻意换手法**坐实(不跑变异,改读调用点与断言)。

- [x] **P2-1｜`StartedAtUtc` / `EndedAtUtc` 两个形参零覆盖**(产品代码 `WfNodeExecutionAttemptStore.cs:54-55`,缺口在 `WfNodeExecutionAttemptTests.cs` 全部 6 条)。自加变异 M11(两个形参赋值时**互换**)与 M12(两列写死成 `2000-01-01`,形参彻底被忽略)**都是 6 条全绿**。协调者独立复核(**读断言而非跑变异**):①`grep -c "StartedAtUtc\|EndedAtUtc"` 在测试文件命中 2,逐行查明**两处都在 `:241-242` 的 `NewAttempt` 构造辅助里**(为测试 3/4 直插造行),**没有任何一处是断言**;②6 个 `AppendAsync` 调用点全部传**相同的值两次**(`started, started` / `now, now` / `reclaimAt, reclaimAt`)——**连「开始 ≠ 结束」在语义上都从未被表达**,所以两形参互换在原理上不可能被察觉。**为什么是 P2 而非 P3**:这不是「没人用的列」——Plan D1 把 `EndedAtUtc` 非空写成了**崩溃计数口径**(`execution.AttemptCount − count(attempt)`)的支柱,又刻意不建 `DurationMs`,理由正是「`EndedAtUtc − StartedAtUtc` 就是耗时」;两列互换 = 耗时**永远为负**,两列写死 = 耗时**永远为 0**,两种都不抛异常、不撞索引、**四库全绿**,只在人去看诊断数据时才发现,而那时表已是 append-only 永不可改的历史。Task 6 接线时才第一次真传两个不同时刻,届时 bug 已在存储层躺了一整个 Task,而 Task 6 的测试会盯 dispatcher 不会盯投影。**且它不在 Plan 第 4 节的 9 类禁写清单里**:这不是「断言什么都没发生」,而是**列投影断言**,与测试 5 已有的四列投影完全同类——测试 5 标题就叫「四种结果的列投影」,九个业务列断到了七个,恰好漏掉这两个。**修法(最小改动,不新增测试方法)**:在测试 #1 里让两个时刻**取不同值**(`var ended = started.AddSeconds(3);` —— **必须不等,否则互换仍测不出**)再各断一次 `Assert.Equal(started, attempt.StartedAtUtc)` / `Assert.Equal(ended, attempt.EndedAtUtc)`。断 `AppendAsync` 返回的内存对象(与测试 5 同姿势),不经 DB 往返,因此没有 SQLite `DateTime` Kind/精度问题,也不碰 Plan 归给 Task 9 的四库列宽议题。两行断言同时钉死 M11 与 M12。 **(Round 16 已修,commit `da1cf93`:测试 #1 改为 `var ended = started.AddSeconds(3);`(注释写明「与 started 必须不等,否则测不出两个形参被互换」)并加两行 `Assert.Equal`;**原有三条断言全部保留、未新增测试方法(条数仍 6 / 全量仍 313)、产品代码零行为改动**。顺带做了可选的 P3-1:两处 `Length = 512` 改为引用 `WfNodeExecutionAttemptStore.SummaryMaxLength` 常量。协调者独立复验——**刻意换成子代理没试过的形状**:不用互换、也不用写死,改成 `EndedAtUtc = startedAtUtc.AddSeconds(-3)`(**只坏 `EndedAtUtc` 而 `StartedAtUtc` 仍正确,造负耗时**,比互换/写死更贴近真实失误)→ **转红**,`Assert.Equal() Failure`,期望与实际相差恰好 6 秒(+3 应为 −3)。复原后重跑闸门 build **0 错误**、test **313/313 通过失败 0**。)**

**无 P1。** 必做清单里 Plan 点名的每一条不变量都有实跑变异证明能转红:`AttemptNo` 口径(M1 让 #1 与 #2 **都**红,与 Plan「两条断言缺一不可」的预期逐字一致;M2 改成常量 1 → #2 因**撞唯一索引**而红,证明「恒写 1」是硬错误而非静默重复)、唯一索引两列(M3 → #4)与 `IsUnique`(M4 → #3,顺带证明索引在 CodeFirst 里**真被建出且真在生效**,不是元数据恒真断言)、C# 侧截断(M5)**及其长度被钉死在 512**(M6 把常量改成 1024 也红 → 不是「只要截了就行」)、四列投影(M7 摘要互换 / M8 `ErrorCode` 恒 null,**Task 2 review P2-2 那条「掉了的 `Summary` 映射」教训本轮真被覆盖**)、`OutputHash` 的 null 分支(M9,且期望值是测试内现算而非硬编码)、`ExecutionId` 来源(M13 自加)、参与外层事务(M10b)。

**P3 / 观察**

- **P3-1｜两个 `512` 是三处独立字面量,没有联动。** 协调者复核确认:`WfNodeExecutionAttempt.cs:64` 与 `:75` 各写一次 `Length = 512`,`WfNodeExecutionAttemptStore.cs:30` 写 `SummaryMaxLength = 512`。M6 证明改 Store 那侧会红;但**改实体那侧**(如 `Length = 256`)在 SQLite 上因类型亲和性**完全无感**,只会在 CI 的 mysql/postgres/sqlserver 三条腿炸——正是 Plan 陷阱 #7「本地绿 CI 红」的镜像版。`SummaryMaxLength` 已是 `const int`,attribute 里可直接引用做到一处一 token。**判 P3 而非 P2**:两者今天确实相等,漂移需要有人主动改列宽,且真漂移了 CI 会红(不是静默错数据)。可改可不改,不阻塞勾选。 **(Round 16 顺带已改,commit `da1cf93`:`WfNodeExecutionAttempt.cs:64`/`:75` 两处 `Length = 512` 改为 `Length = WfNodeExecutionAttemptStore.SummaryMaxLength`,三处字面量收敛为一处一 token。协调者复核:build 仍 0 错误、测试仍 313 全绿、未加 `DefaultValue`、未用 `CodeFirst_BigString`。)**
- **P3-2｜测试 5 断的是内存对象,`OutputSummary`/`OutputHash` 从未经 DB 往返。** #2/#3/#4 有 `Queryable` 往返但只覆盖 `AttemptNo`/`ResultType`/`ErrorCode`/`ErrorSummary`/`Id`/`ExecutionId`。鉴别力上不构成漏洞(M5/M6/M9 都红),且 SqlSugar 按属性名建列不会错配,故只作观察;**不建议为此加往返断言**——那会滑向 Plan 禁写清单里「列宽/往返归 Task 9」那条。
- **M10a 被判「变异无效」而非「#6 没鉴别力」(方法论,值得记牢)。** 子代理先用 `db.CopyNew().Insertable(...)` 令插入走独立连接以逃出外层事务,结果**一条没红但整轮耗时从 7s 飙到 5m4s**。它没有就此报「#6 没鉴别力」,而是诊断出:`WorkflowAppFactory` 用的是 **SQLite 文件库**,第二条连接写同一文件会撞上外层事务的写锁,阻塞到 busy timeout 后**抛异常**——异常同样让 `UseTranAsync` 回滚、表内 0 行,于是 #6 依然绿但走的是**完全不同的路径**(5m4s 就是六条测试各自在锁上空等的代价)。改用 M10b(方法内偷加 `CommitTranAsync`)后 #6 立刻红。**这是测试工具的限制,不是断言写松了**——区分「变异无效」与「测试没鉴别力」是变异测试里最容易糊弄过去的一步。

**射程限制(如实报,不硬凑测试)**

- **「`AppendAsync` 不碰 `wf_node_execution` 任何列」拦不住(M14 自加,全绿)。** 偷加一处**无 fence CAS** 的 execution 回写(写 `Summary`/`ErrorCode`/`OutputHash`),6 条测试全通过。**这与 Plan 一致而非违反**:D6 把回写归 Task 6 并要求带 `WHERE Fence == @myFence`,第 4 节禁写清单又明确写了「『`AppendAsync` 没有改 execution 行』的断言——又一个『什么都没发生』;靠 review 和改动清单兜住」。子代理认同该取舍并给出理由:加了也只能断「某几列没变」、枚举不全、给虚假安全感;真实防线是本轮已做的机械核实(产品代码 `Updateable<WfNodeExecution>` grep 命中 **0**)+ Task 6 的 fence CAS 测试。**⚠ 给 Task 6 review 的必查项**:产品代码里 `Updateable<WfNodeExecution>` 的**每一个**命中都必须带 `Fence` 谓词。
- 其余(Plan 第 6 节已列、本轮实测未推翻):`AttemptNo` 第三处口径(`WfNodeExecutionContext.Attempt`,归 Task 6)、attempt 与结果回写同一短事务的另一半(Task 6)、fence 拒绝迟到回写(Task 6/7)、四库真实行为与列宽(Task 9)、截断在真会抛的库上的效果(Task 9)、崩溃计数口径的端到端验证(Task 7)。

**静态审查结论**:6 条测试**无空转**——每条都有至少一个实跑变异点能打红(#1←M1/M13,#2←M1/M2/M7/M8/M13,#3←M4,#4←M3,#5←M3/M5/M6/M7/M8/M9/M13,#6←M10b);Plan 的 9 类禁写清单**一条都没变相写进去**;`OutputHash` 的 `OutputJson == null → null` 正确且被 M9 钉住;hash 算法与既有两处**逐字同型**(`WfExecutionKey.cs:59`/`WfIdentityHash.cs:68`/`Store.cs:60`),**没有新建 helper、没有扩 `WfIdentityHash` 职责**;Store **只有** `AppendAsync` 一个公开方法(`Truncate` 是 `private static`);实体注释承诺的**七条齐全**;**无执行顺序依赖、无跨测试共享 DB 状态**;`Truncate` 的 null 安全与 512/513 边界正确。

## Log

| 轮次 | 阶段 | 摘要 |
|---|---|---|
| 0 | draft | 起草台账。M2c + 过渡步骤已收口(commit `9589c4d`),基线 264/264。本台账新增「分析/审查用 Opus、执行用 Sonnet」的委派纪律(见 `## Loop 纪律` 表)。下一步 Round 1 Task 1 plan。 |
| 1 | plan | Task 1 plan 完成(Opus 子代理)。6 个决策点全部定案:NodeVisitId=可空雪花/唯一生成点 EnterNodeOp;Sequence=`wf_instance.HistorySeq` 事务内原子递增+读回(**本轮不建唯一索引**,理由入契约);新枚举 `WfHistoryActorType`(驳回 `Reminder`);PayloadVersion 默认 1 无人显式写;TokenId 不存在写不出的情况;非空列必须带 `DefaultValue` 否则 PG/MSSQL 存量升级炸。改动清单 15 产品文件 + 3 测试文件,预期 264→279。已知会红:`WorkflowMultiLeaderSnapshotTests.cs:657` 构造参数个数。下一步 Round 2 Task 1 exec。 |
| 2 | exec | Task 1 exec 完成(executor/sonnet)。commit `fd00b05` "feat(workflow): add node-visit identity and history sequence/actor columns",19 文件 +1344/-13,与 Plan 改动清单零偏差。协调者独立复跑闸门:build 0 错误(13 警告全在 Services/Rbac 的 XML cref,既有、非本次引入,Workflow 0 警告);test **279/279 通过、失败 0**(基线 264 + 15 精确吻合)。子代理自报的唯一判断项:测试 2 的抄送断言改为「`wf_cc` 行对自己那次访问的 `wf_history(NodeEnter+CcSent)` 自洽」而非「等于当前 token」——因为 HTTP 响应返回时 token 已越过 cc 节点。留给 review 阶段确认这个弱化是否掩盖了缺陷。**不勾选**,下一步 Round 3 Task 1 review。 |
| 3 | review | Task 1 review 完成(Opus 自审 + 8 变异点 + 2 自加探针,26 分钟)。**0×P1**,2×P2,6×P3,4 条射程限制。转红的:M1(不生成 NodeVisitId)5/15、M2(生成不落库)4/15、M7a(ActorType 写死 Unknown)2/15、M7b(TokenId 写死 null)2/15 —— 生成点/落库/传递链均被真实覆盖。仍绿的:M3(原子递增改先读后写)、M5(去短事务)——单进程测不到并发,记 R1;M6(去 DefaultValue)——空库测不到存量升级,记 R2 并要求 Task 9 兜底;M4(去 ClearFilter)——原因与预判不同(后台无 HttpContext 时数据范围本就 Unrestricted),记 R3;M8(WfHisTask 改从 token 拷)——当前引擎下两种写法语义等价,升为 P2-1。协调者交叉验证工作区复原干净(status 只剩 TestResults/,diff 空,HEAD 仍 ca175a2)。**不勾选**,下一步 Round 4 修 P2-1/P2-2。 |
| 4 | 修 Findings + 勾选 | P2-1/P2-2 均修完(executor/sonnet,commit `98c2837`,3 文件 +17/-9,**产品逻辑零改动**)。协调者独立复核:自己另做一次变异(`+1`→`+3`,刻意不同于子代理的 `+2`)确认 `Sequence_starts_at_one...` 在 :49 转红(失败 3/通过 5),单文件 checkout 复原后 `git diff` 空;重跑闸门 build **0 错误**、test **279/279 通过失败 0**。0×P1、0×未修 P2、闸门已跑 → **Task 1 勾选**。下一步 Round 5 Task 2 plan(IWorkflowNodeHandler SPI,纯类型定义不接引擎)。 |
| 5 | plan | Task 2 plan 完成(Opus 子代理,39 次工具调用)。7 个决策点拍板:①`Cancelled` **不进**结果枚举——取消走 OCE,与 `TerminalFailure` 语义方向相反(前者「应被重新领取」/后者「永不重试」),合并会让 Task 6 分不出该不该重试;②Result 用 sealed class + 私有构造 + 四静态工厂,不用类型层次(§6.2 存四个扁平列,1:1 映射);③Context 14 字段,不泄漏实体/DB session,变量传原始 JSON、节点配置传既有 `WfNodeProps`;④接口键用 `WfNodeType` 枚举 + `TryAddEnumerable` 分发(同 `IAdminJob` 先例),无 `CanHandle`/keyed DI/抽象基类;⑤本 Task **零 DI 注册**,第一条注册线归 Task 8;⑥`FakeNodeHandler` 放测试程序集,绝不进内核包;⑦一个文件放 `Abstractions/`,不预建 `Execution/`。改动清单 **3 个新文件、0 既有改动**,预期 279→291。6 条新契约行已入 `## 语义契约`。下一步 Round 6 Task 2 exec。 |
| 6 | exec | Task 2 exec 完成(executor/sonnet)。commit `21085e1` "feat(workflow): add IWorkflowNodeHandler SPI and node-execution types",**3 个新文件 +329/-0,0 既有文件改动**,零偏差。协调者独立复核:改动面精确;三条关键陷阱逐条验证均未踩(`Engine/` 零改动 → Webhook 仍走 48008;`WorkflowSetup.cs` 零改动 → 十件套仍 10 条;`FakeNodeHandler` 在 `backend/src/` 命中 0 文件 → 没漏进内核包);重跑闸门 build **0 错误**、test **291/291 通过失败 0**(279+12 精确吻合)。子代理另核实了三处 Plan 假设与实际代码一致(`WfNodeProps.WebhookUrl` 位置、`WfToken.NodeVisitId` 可空性、`ISqlSugarClient` 在 vendor `SqlSugar` 命名空间而 `PrimaryId` 在 `TenonAdmin.SqlSugar` —— 第 1 条反射断言两者都查)。**不勾选**,下一步 Round 7 Task 2 review。 |
| 7 | review | Task 2 review 完成(Opus 自审,**26 个变异点**,18 分钟)。**0×P1**,2×P2,5×P3,9 条射程限制。转红的:四个工厂 `Type` 逐一改错(M1a–d)各红 2/12、枚举改 0 值/重排(M2a/b)、加 public 构造(M3)、往 Context 塞 `WfInstance`/`ISqlSugarClient`/`SugarParameter`(M4a/b/d)、删 `ThrowIfCancellationRequested`(M5)、`NodeType` 恒返(M8)、删 `CallCount++`(M9)、Context 里加 JSON 校验(V1)、`DeadlineAtUtc` 改写时区(V2)。仍绿的两处升为 P2:**P2-1** 实体守卫有泛型洞(`IReadOnlyList<WfHistory>?` 溜过去 12/12 绿)、**P2-2** 四个工厂 payload 管线零断言(丢 `RetryAfter`/`OutputJson`/`Summary`/`ErrorCode` 全绿)。编译期挡住的如实归类(M6a/b 改 `DateTimeOffset` 类型 → 测试文件自己编译不过,是文档+编译期防线不是运行期断言,记 P3-2)。子代理给自己加的「diff 为空则中止」守卫两次拦下 sed 行号错位的空变异,避免了「绿=没覆盖」的误判。12 条测试逐条实测均有转红点,Plan 黑名单四项一项没被写进来。协调者交叉验证工作区复原干净(status 只剩 TestResults/,diff 空,HEAD 仍 4ce5c30)。**不勾选**,下一步 Round 8 修 P2-1/P2-2(都只改测试文件)。 |
| 8 | 修 Findings + 勾选 | P2-1/P2-2 均修完(executor/sonnet,commit `39e9d90`,**只改 1 个测试文件 +54/-15,产品代码零改动**,条数仍 12)。协调者独立复核**刻意换形状**再验:P2-1 用数组 `WfToken[]?` 与双层嵌套 `IReadOnlyDictionary<string, IReadOnlyList<WfTask>>?`(子代理只验过单层 `IReadOnlyList`),**两者各自单独均转红** → 递归确实穿透数组与两层泛型;P2-2 变异 `RetryAfter → null`(review 阶段全绿的 M11)**转红**。每次变异前 diff 确认落盘、后单文件 checkout 复原。重跑闸门 build **0 错误**、test **291/291 通过失败 0**。0×P1、0×未修 P2 → **Task 2 勾选**(10 项已完成 2 项)。下一步 Round 9 Task 3 plan(wf_node_execution 实体 + ExecutionKey 唯一约束 + lease/fence CAS 领取,**语义契约里三项待定案**)。 |
| 9 | plan | Task 3 plan 完成(Opus 子代理)。**首派挂死**:agent `ac8addd30aa5c04b1` 跑 52 分钟、转录文件 0 字节且 3187 秒无写入(此前每个子代理都持续写转录)→ 判定挂死而非慢,`TaskStop` 后收紧 prompt 重派 `a43181dfe110c3551`,10.4 分钟返回。plan 阶段本就不落笔,工作区全程干净,无工作损失。8 个决策点拍板:①`ExecutionKey` = SHA-256(`ScopeKey\|InstanceId\|TokenId\|NodeVisitId\|NodeId\|DefinitionVersionId`,`'\\n'` 分隔)→ 固定 64 位小写 hex 非空,**选 hash 不选明文**的决定性理由是常数长度一次绕开 MySQL 3072B 与 SqlServer 900B 两个索引上限、且非空天然避开 SqlServer「多 NULL 相等」;不复用 `WfIdentityHash.Compute`(签名被回执 6 维度焊死)但复用其 `NormalizeScopeKey`;②`WfNodeExecutionStatus{Pending=1..Failed=7}` 无 0 值,`Cancelled` 与 `Failed` **都要**(前者静默丢弃/后者要报警),`Running→Running` 是合法自转移且**正是 fence 存在的全部理由**;③lease/fence 四列 + 一条条件 UPDATE 领取(四库通用,无 `RETURNING`/`SKIP LOCKED`/DB 时间函数),影响行数 0 → 返回 `null` 不抛;租约过期判定用**应用时间**,Task 7 因此可直接 UPDATE 时间戳模拟崩溃;④基类必须 `BaseEntity` 不是 `DataEntity`(`IOrgScoped` 会让无 HTTP 上下文的 worker 扫描 0 行);⑤四个业务时间列一律 UTC,刻意偏离本仓 local 惯例,列名后缀是唯一护栏;⑥全表**不写 `DefaultValue`**(那只驱动 `ADD COLUMN`,`CREATE TABLE` 不读);⑦`AttemptCount` 领取时 +1 并读回,三处口径 1 基对齐;⑧`public static WfNodeExecutionStore`,零 DI 注册、零引擎调用点。7 条新契约行入 `## 语义契约`(含挂了三轮的三项全部结清 + `HandlerVersion` 挂账结账)。6 条射程限制 R1–R6 已记入 plan。改动清单 4 新建 + 1 追加 + 2 测试,预期 291→306。下一步 Round 10 Task 3 exec。 |
| 10 | exec | Task 3 exec 完成(executor/sonnet)。commit `20bfc9d` "feat(workflow): add wf_node_execution entity with lease/fence claim",**6 个文件 +658/-0**,与 Plan 改动清单零偏差。协调者独立复核:①限定 `Engine/Operations/`+`WorkflowSetup.cs`+`WorkflowEngine.cs`+`Abstractions/` 的 `git show --stat` **输出为空**;②`grep -rln WfNodeExecutionStore backend/src/ --include=*.cs` 初看 18 个命中,**排除 bin/obj 编译产物后只剩 2 个源文件**(自身 + 实体注释)→ 零引擎调用点;③`grep -c '48008'` 在 `EnterNodeOp.cs` 命中 0 一度可疑,查明是符号引用 `WorkflowErrorCode.NodeTypeUnsupported`(`:68-69`),`default:` 分支原样保留;④新实体 `DefaultValue *=` 命中 **0**、基类是 `BaseEntity`(`:31`)、表名 `wf_node_execution` + `uk_wf_node_exec_key`(唯一)+ `idx_wf_node_exec_scan(Status, NextRetryAtUtc)` 与 Plan 一字不差;⑤重跑闸门 build **0 错误**、**Workflow 贡献 0 警告**、test **306/306 通过失败 0**(291+15 精确吻合)。三条重点陷阱(接引擎 / 机械套 `DefaultValue` / 误用 `DataEntity`)**逐条机械验证均未踩**。**不勾选**,下一步 Round 11 Task 3 review。 |
| 11 | review | Task 3 review 完成(Opus 自审,13 变异点 = 11 必做 + 子代理自加 2)。**11 钉住、2 存活**。P1-1:M2 把 `NextRetryAtUtc <= now` 放宽成 `(== null || <= now)` → 15/15 全绿,因 `#12` 只造过「未来/过去」两种行、**从未构造 `(RetryScheduled, null)`**——正是 Plan 陷阱 7 点名的场景,Task 6 一次「保险」放宽就会让刚标记重试、退避时间还没算出的行被抢跑。P1-2(**子代理自加的 M12 抓到,不在必做清单里**):`leaseUntilUtc = nowUtc + leaseDuration` 改成 `= nowUtc` 仍全绿——三条相关测试各自漏掉的原因不同,其中 `#10` 名叫 "within the lease window" 却因两次领取传同一个 `now` + WHERE 用严格小于,而**从未依赖真实租约窗口**;此条失守则 lease 机制静默归零,fence 拦不住两个 worker 同时执行同一节点。P2-1:`ScopeKey` 列 `IsNullable=true` 与类注释「显式非空、与 `WfOperationReceipt` 同源」矛盾,而兄弟表确为非空——诊断列可能存原始 null 而 `ExecutionKey` 按哨兵 `"-"` 算,正是 `NormalizeScopeKey` 注释警告过的对不上。协调者独立交叉验证:变异全复原(工作区干净/HEAD 未动)、**刻意换手法**(读断言而非跑变异)坐实两个 P1、P2-1 三处原文逐一比对、点名四项中三项 + 四条承诺注释复核无问题。P3 六条入册,**P3-3 经协调者裁定不是范围外增列**(那三列是 Round 10 exec prompt 明文批准的),只需改注释措辞。元教训:exec 避开了全部六类禁令仍漏两个核心行为——**缺口是「缺断言」不是「假断言」,清单核对抓不到,只有变异测试能抓**。**不勾选**,下一步 Round 12 修 Findings。 |
| 12 | 修 Findings + 勾选 | P1-1/P1-2/P2-1 + P3-3 全部修完(executor/sonnet,commit `68809c0`,**2 文件 +31/-7,产品逻辑零改动**)。P1-1 新增独立测试 `Retry_scheduled_row_with_no_retry_time_is_never_claimable`(307 = 306+1);P1-2 `#9` 的 `NotNull` 换成带 1 秒容差的值断言、`#10` 第二次领取改传 `now.AddMinutes(1)`;P2-1 `ScopeKey` 改成非空 `= ""` 与兄弟表同款。协调者独立复核**两个变异都刻意换形状**:A 用 `?? DateTime.MinValue`(台账预言的 Task 6 真实失误,非子代理的 `== null ||`)→ 转红;B 用 `nowUtc + 1 秒`(**正数但忽略量值**,比子代理的 `= nowUtc` 更强,只写 `> now` 的断言会漏)→ `#9`/`#10` **双双转红**,额外证明 `#9` 查值、`#10` 真依赖租约窗口。另核实:`DefaultValue` 仍 0 命中(改非空未连带破坏建表契约)、`Engine/` 零改动、P3-3 只改注释未动列。重跑闸门 build **0 错误**、test **307/307 通过失败 0**。0×P1、0×未修 P2 → **Task 3 勾选**(10 项已完成 3 项)。下一步 Round 13 Task 4 plan(attempt 表,重点盯 `AttemptNo` 不得二次 +1 的差一陷阱)。 |
| 13 | plan | Task 4 plan 完成(Opus 子代理)。**消息通道连吃三次截断**(Task 3 review ×2 + 本轮 ×1)→ 改用「子代理 Write 进 scratchpad 文件 + 协调者转写」,`plan4.md` 36KB/421 行七节齐全,**后续沿用此法**。D1–D8 全拍板,改动清单 **3 全新文件、0 既有改动**,预期 307→313(+6)。关键定案:①`wf_node_execution_attempt` 9 列,`BaseEntity`、无 `ScopeKey`(永远经 `ExecutionId` 到达,父行已有;反规范化等于永久多一个要保持一致的写入点)、`EndedAtUtc` **非空**(一行 = 一次**已返回**的调用,由此 `AttemptCount − count(attempt)` = **崩溃次数**);②append-only **两道防线**:硬的 `UNIQUE(ExecutionId, AttemptNo)`(没它则「`AttemptNo` 恒为 1」的 bug 也能插两行而测试照绿)+ 软的「Store 只暴露 `AppendAsync`、不给 Update/Delete」;③**`AttemptNo` 防差一靠签名**——不收 `attemptNo` 形参,`AttemptNo = execution.AttemptCount`,两个事实取自同一对象;④**零 execution 回写**,因为那次回写必须带 `WHERE Fence == @myFence` 的 CAS(Task 3 射程限制 R6 已指派给 Task 6),提前散进来等于放一个无 CAS 保护、且不知道 fence 的写入点;⑤**不存输出正文**,只存 `OutputHash` + 512 截断摘要,**不用 `StaticConfig.CodeFirst_BigString`**(attempt 是永不删除表,且是 PII/密钥泄漏面最大的一张);截断**必须在 C# 侧**——600 字摘要在 SqlServer/PG **直接抛**、MySQL 非严格模式**静默截断**、SQLite **照单全收**,正是本仓最典型的「本地绿 CI 红」;⑥**拒建 §6.2 的 7 列**(它们在 §七的 `wf_ai_decision` 已有家,同一事实两个写入点是 bug 温床;与 Task 3「一次造齐」不矛盾——那 8 列属同一里程碑,这 7 列属 M3b)。6 条新契约行入册。下一步 Round 14 Task 4 exec。 |
| 14 | exec | Task 4 exec 完成(executor/sonnet)。commit `16719e6` "feat(workflow): add wf_node_execution_attempt append-only store",**3 全新文件 +404/-0、0 既有改动**,零偏差。**本轮起计划与长报告一律走文件交付**(`plan4.md` / `exec4-report.md`),彻底绕开消息通道截断(本轮此前已被截三次)。协调者独立复核:①七个既有文件的限定 `git show --stat` **输出为空**;②六条陷阱逐条 grep 有据——`AttemptNo = execution.AttemptCount`(`:53`,注释写着「直接取,绝不 +1」)且**签名无 `int attemptNo` 形参**(命中 0)、产品代码 `Updateable<WfNodeExecution>` **0**、`: BaseEntity`(`:43`)、`DefaultValue` **0**、`CodeFirst_BigString` **0**、`Truncate` 用 `value[..512]`;③唯一索引确为两列 + `IsUnique = true`,`EndedAtUtc` 非空;④**一处可疑读数深查**:测试文件 `Insertable` 命中 11 处远超预期,逐行核对确认直插 attempt 的只有测试 3 的 `:100/105` 与测试 4 的 `:131/132`(Plan 明确允许这两条为测索引而直插),测试 1/2/5/6 全走 `AppendAsync`,其余 7 处在造父行 execution → **合规**;⑤重跑闸门 build **0 错误**、test **313/313 通过失败 0**(307+6 吻合)。**不勾选**,下一步 Round 15 Task 4 review。 |
| 15 | review | Task 4 review 完成(Opus 自审,**14 变异点 = 10 必做 + 自加 4**)。**12 钉住、2 存活、1 判无效重做**。**无 P1**;P2-1 = `StartedAtUtc`/`EndedAtUtc` 两形参**零覆盖**(M11 互换、M12 写死成 `2000-01-01`,**均 6 条全绿**)——不是「没人用的列」:Plan D1 把 `EndedAtUtc` 非空写成崩溃计数口径的支柱、又因「相减即得耗时」刻意不建 `DurationMs`,故互换 = 耗时**永远为负**、写死 = **永远为 0**,且**四库全绿**、只在人看诊断数据时才发现,而那时表已是永不可改的历史。协调者**换手法**独立坐实(读断言而非跑变异):测试文件对这两列的 2 处命中**全在 `NewAttempt` 构造辅助(`:241-242`)、无一是断言**,且 6 个调用点**全传相同值两次**,连「开始 ≠ 结束」都没表达过。**方法论亮点**:M10a 用 `CopyNew()` 逃事务后一条没红但耗时 7s→**5m4s**,子代理**没有**就此报「#6 没鉴别力」,而是诊断出 SQLite 文件库上第二连接撞写锁 → busy timeout 抛异常 → 同样回滚,判**变异无效**并换 M10b(方法内偷加 `CommitTranAsync`)重做 → #6 立刻红。射程限制如实报 1 条:M14 偷加**无 fence CAS** 的 execution 回写全绿,**与 Plan D6 一致而非违反**(禁写清单明写这类「什么都没发生」的断言靠 review + 改动清单兜),**已给 Task 6 review 留必查项:产品代码里 `Updateable<WfNodeExecution>` 的每一个命中都必须带 `Fence` 谓词**。P3 两条:512 是三处独立字面量(改实体那侧 SQLite 无感、CI 三腿红)、测试 5 断内存对象未经 DB 往返(不建议补,会滑向归 Task 9 的议题)。**不勾选**,下一步 Round 16 修 P2-1 + 勾选。 |
| 16 | 修 Findings + 勾选 | P2-1 修完 + 顺带做了可选的 P3-1(executor/sonnet,commit `da1cf93`,**2 文件 +6/-3**)。P2-1:测试 #1 改用 `var ended = started.AddSeconds(3);`(注释写明「必须不等否则测不出互换」)并加两行 `Assert.Equal`,**原有三条断言全保留、未新增测试方法、产品代码零行为改动**;P3-1:两处 `Length = 512` 收敛为引用 `SummaryMaxLength` 常量。协调者独立复核**刻意换形状**:不用子代理的「互换」或「写死 `2000-01-01`」,改成 `EndedAtUtc = startedAtUtc.AddSeconds(-3)`——**只坏一列、造负耗时**,比互换/写死更贴近真实失误 → **转红**,期望与实际差恰好 6 秒。另核实 `DefaultValue`/`CodeFirst_BigString` 仍 0 命中、测试条数仍 6。重跑闸门 build **0 错误**、test **313/313 通过失败 0**(条数未增,符合纯补断言)。0×P1、0×未修 P2 → **Task 4 勾选**(10 项已完成 4 项)。下一步 Round 17 Task 5 plan(`wf_outbox`,四个硬问题待拍板:状态机与领取方式是否复用 lease/fence、幂等键、payload 存全文还是 hash、**以及台账把「派发消费逻辑本轮做不做」的决定权明确交给了 plan**)。 |
