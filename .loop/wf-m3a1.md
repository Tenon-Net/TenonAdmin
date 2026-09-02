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

- 轮次: 29
- max: 70
- 当前任务: Task 8(Webhook Handler:首个真实 Adapter)
- 当前阶段: plan 已完成并转写进 `## Plan`;D2 的两张分类表已另抄进 `## 语义契约`。**未写任何代码**
- 上一轮: Round 29 — Task 8 **plan**(`Agent(model="opus")`,不传 `subagent_type`)。**首次派出被 API 配额掐断、零产出**(见 Log 28b),按用户指示直接重试,重试成功。七节全文 413 行落 `scratchpad/plan8.md`,已转写进 `## Plan`。**头号交付物达成**:语义契约里空了整个里程碑的「Webhook 超时/重试分类」那一行**已填死**——两张表(状态码 / 异常与配置)逐字抄进 `## 语义契约` 的**「Webhook 分类规则」小节**(**不是留在 Plan 里指过去**:契约的规矩是「`## Plan` 被重写也不得丢」),并新增一行「Webhook 配置 schema 与外呼请求体」。
  **核心结论:零新包、零新安全代码。** `HttpClient` 与 SSRF 围栏**全部复用内核既有的 `JobHttpClient`/`JobHttpFence`**;超时与取消的区分**照抄仓内先例**。协调者**独立抽验四条承重主张全部属实**:①`JobHttpFence` 确为 `public static` 且分层可达(Workflow → AspNetCore → Services);②`Microsoft.Extensions.Http` **已在** `Directory.Packages.props:30`(v10.0.0);③`HttpAdminJob.cs:41` 逐字是 `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)`——**比协调者 prompt 里提示的 `ex.CancellationToken ==` 更贴本仓惯用法,子代理按代码事实选了更好的那个**;④`WfNodeProps` 确在 `web/src/api/schema.d.ts:11634-11651`(已有 `webhookUrl`),`web-react` 同。
  **⚠ 两件事需要记牢**:**(i) `## DONE-CONDITION` 的前端闸门由此被激活,不再是「不适用」** —— `WfNodeProps` 加 4 个字段会进 OpenAPI,exec 必跑 `cd web && npm run typecheck && npm run lint` + **双模板 `gen:api`** + `check-contract-drift.mjs`,`schema.d.ts` **不许手改**。**(ii) 协调者对 D4 的裁决**:同意「**不建 worker、不改 `EnterNodeOp`**」,**且不在本台账新增 Task 8b** —— `## DONE-CONDITION` 写的是「十项全部打勾」,加第 11 项等于改掉本 loop 自己的完成判据,不是协调者能自行决定的;GOAL 里本就没有 worker,而 Webhook 那条验收线不依赖它。**「`EnterNodeOp` 接线 + `WfNodeExecutionJob` + 种子行 + worker 标识来源」记为射程外挂账**(形状要求:`IAdminJob` + `sys_job` 种子行、复用 `JobSchedulerService` 选主与 `SysJobLock`,**不许新建第二套调度**),**交由用户决定**排新 Task 还是留给下一里程碑。R1 因此只**部分解除**:注册线与 handler 有了,execution 行的生产创建方与调度方仍不存在。
- 下一步: Round 30 — Task 8 **exec**。协调者派 `Agent(subagent_type="oh-my-claudecode:executor", model="sonnet")`,prompt 塞入 `## Plan` 全文 + 闸门命令。**exec 要盯的四点**:①**前端闸门已激活**(见上 (i)),两个模板都要跑,`schema.d.ts` 只许由 `gen:api` 生成;②**不许把 `Microsoft.Extensions.Http` 引进 Workflow 包、不许写第二份 SSRF 围栏**——D1 的全部价值就在复用 `JobHttpClient`/`JobHttpFence`;③**不许改 `EnterNodeOp`、不建任何 HostedService/`IAdminJob`/种子行**(D4 的裁决已定);④**不许改 `WfNodeExecutionDispatcher` 的租约处置、不许改 `ResolveRetryDelay` 的 `(0,24h]` 钳制**(§7 按住的两条;前者已由 Task 7 N4 的两条租约断言钉死,动它必红)。另:新增 3 个错误码(48029/48030/48031),`[Fact]`/`[Theory]` 预计新增见 §4「预计条数」,**346 只增不减**,十件套仍 **10** 条。协调者事后**亲自重跑闸门(含前端三条)**并核对 `git diff --stat` 与 §2 改动清单**文件级一致**,**不勾选**。

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
| Webhook 超时/重试分类(Task 8 定案) | **全文见下方小节「Webhook 分类规则」**(表 A 状态码 / 表 B 异常与配置)。要点:唯一判据是「原样重发有没有可能成功」;`408`/`423`/`425`/`429`/**除 501 外的 5xx** → `RetryableFailure`,`3xx`(不跟随重定向)/`401`/`403`/`404`/`409`/`422`/**501** 与其余 4xx → `TerminalFailure`;`429` 与带 `Retry-After` 的 5xx **读 `Retry-After`**(`Delta ?? (Date - now)`,`<=0` 或解析不出则传 `null`),**上界不在 handler 钳**——`WorkflowEngine.ResolveRetryDelay` 已是唯一钳制点,两处钳会各自漂移。**分类表里没有任何一格默认产出 `ManualFallback`**,它由节点配置 `props.webhookOnFailure = "manual"` 打开,打开后本 handler 的每一个 `TerminalFailure` 一律改成 `ManualFallback`(错误码与摘要照旧)。**OCE 必须分两种**:外部 `ct` 已取消 → **原样抛出、不产生结果**(契约「取消语义」:应可被重新领取);外部 `ct` 未取消(= handler 自己的超时)→ `RetryableFailure`/48031,判据逐字照抄仓内先例 `HttpAdminJob.cs:41` 的 `when (!cancellationToken.IsCancellationRequested)`。**围栏 `AdminException` 必须捕获并映射成 `TerminalFailure`/48030,不许逸出**——逸出则 tx2 从不开始、行停 `Running` 持租约、过期被重领再抛 → **无限活锁且 attempt 表一行都没有**。 |
| Webhook 配置 schema 与外呼请求体(Task 8 定案) | `WfNodeProps` **平铺追加** `WebhookMethod`/`WebhookHeaders`/`WebhookTimeoutSeconds`/`WebhookOnFailure`(全可空,存量 `ModelJson` **逐字兼容**;`WebhookUrl` 本来就有)。**这四个字段进 OpenAPI**——`WfNodeProps` 实测出现在两个模板的 `schema.d.ts`(`web/src/api/schema.d.ts:11634-11651`,`web-react` 同),故加/改它们**必跑双模板 `gen:api` + SHA256 一致 + `check-contract-drift.mjs`**,`## DONE-CONDITION` 那条前端闸门由此**被激活,不再是「不适用」**。超时钳 `[1,120]`s 后再取 `min(…, DeadlineAtUtc - now)`;header 拒 `Host`/`Content-Length` 且须过 `JobHttpFence.ValidateHeader`。**`HttpClient` 与 SSRF 围栏一律复用内核既有的 `JobHttpClient`/`JobHttpFence`**(`src/TenonAdmin.Services/Jobs/`,`JobHttpFence` 是 `public static`,Workflow → AspNetCore → Services 分层允许引用),配置源 `TenonAdmin:Jobs:Http`,**本包不引 `Microsoft.Extensions.Http`、不写第二份围栏代码**(该包已在 `Directory.Packages.props` v10.0.0,零新包)。外呼请求体是引擎生成的固定字段集,**刻意不含 `VariablesJson`**(理由同 outbox D6 的 PII 决策);`GET`/`HEAD` 不带体。 |
| 与人工任务的关系 | `ManualFallback` 时如何创建人工 `wf_task`——待 Task 6(dispatcher)plan 定案,复用 `EnterNodeOp.CreateTaskAsync` 还是新路径 |
| 范围外 | 不建 AI Decision(`wf_ai_decision`、provider adapter、policy、shadow mode——留给 M3b);不建并行网关(`ParentTokenId`/`ForkId`/join 表);不新增审批动词;不 port React 工作流页;不抽 web/web-react 共享层 |


### Webhook 分类规则(Task 8 定案 —— 本小节属 `## 语义契约`,`## Plan` 被重写也不得丢)

> 贯穿全表的**唯一判据**:*「过一会儿原样再发一次同一个请求,有没有可能成功?」* 有 → `RetryableFailure`;没有 → `TerminalFailure`(或按节点配置转 `ManualFallback`)。

#### 表 A:HTTP 状态码

| 状态码 | 结果 | `ErrorCode` | 理由 |
|---|---|---|---|
| **200–299** | `Succeeded` | — | |
| **300–399**(含 301/302/307/308) | `TerminalFailure` | 48029 | 围栏 handler `AllowAutoRedirect = false`(`JobHttpFence.cs:109`),重定向**不跟随**;拿到 3xx = 配的 URL 不对,再发一次永远还是同一个 3xx。修法是改流程定义,不是重试。 |
| **400 / 405 / 406 / 411 / 413 / 414 / 415 / 431** | `TerminalFailure` | 48029 | 请求本身不被接受。原样重发 ⇒ 原样的答复。 |
| **401 / 403** | `TerminalFailure` | 48029 | 凭据/权限。header 来自**静态节点配置**,下一次 attempt 发的是**逐字相同**的 header,不可能变好。**刻意不是 `ManualFallback`**:修法是改定义里的密钥,不是找个人来审批。 |
| **404 / 410** | `TerminalFailure` | 48029 | 端点不存在。 |
| **408** | `RetryableFailure` | 48029 | 服务端自己宣布「请求超时」——按定义就是瞬时的。 |
| **409** | `TerminalFailure` | 48029 | 冲突 = 远端说当前状态不对。**刻意不重试**:409 最常见的来源是对方的重复提交守卫,重试只会把同一个冲突撞 3 次。 |
| **423 / 425** | `RetryableFailure` | 48029 | Locked / Too Early,两者的 RFC 语义原文就是「稍后再试」。 |
| **429** | `RetryableFailure`,**读 `Retry-After`** | 48029 | 背压的规范形态。 |
| **其余 4xx**(402/407/418/422/451/…) | `TerminalFailure` | 48029 | 4xx 类的默认 = 请求方的问题,原样重发无意义。**422 明确归此**(我们发出去的载荷语义校验没过)。 |
| **501** | `TerminalFailure` | 48029 | Not Implemented——下一次 attempt 之前对方不会把它实现出来。**5xx 里唯一的例外,是定案不是遗漏。** |
| **其余 5xx**(500/502/503/504/505/…) | `RetryableFailure`,**若带 `Retry-After` 则读** | 48029 | 服务端侧故障,规范意义上的瞬时。 |

`Retry-After` 的读法(仅在**已判为 `RetryableFailure`** 的分支读,读到即填 `WfNodeExecutionResult.RetryAfter`):
`response.Headers.RetryAfter` 同时支持 delta-seconds 与 HTTP-date,取 `Delta ?? (Date - now)`;**结果 `<= 0` 或解析不出 ⇒ 传 `null`**(让引擎走自己的指数退避)。**上界不在 handler 钳**——`WorkflowEngine.ResolveRetryDelay`(`WorkflowEngine.cs:1452-1459`)已经把 `(0, 24h]` 之外的值一律退回 `30s << min(AttemptCount-1, 5)`,handler 再钳一次就是两个钳制点各自漂移。

#### 表 B:异常与配置

| 情形 | 结果 | `ErrorCode` | 判据 / 理由 |
|---|---|---|---|
| **`OperationCanceledException` 且外部 `cancellationToken.IsCancellationRequested`**(宿主停机 / dispatcher 取消) | **原样抛出,不产生任何结果** | — | 语义契约「取消语义(Task 2 定案)」:OCE = 「这次没跑完,**应可被重新领取**」。必须逸出到 dispatcher(`WfNodeExecutionDispatcher.cs:29-33`:全类零 `catch`,靠「没有 catch」落地)。 |
| **`OperationCanceledException`/`TaskCanceledException` 且外部 ct **未**取消**(= 本 handler 自己的超时) | `RetryableFailure` | **48031** | .NET 里 HTTP 超时抛的正是 `TaskCanceledException`(OCE 的子类),**必须与上一行区分开**。判据逐字照抄仓内既有先例 `HttpAdminJob.cs:41`:`catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)`。超时若当 OCE 逸出,attempt 预算不扣、行被反复重新领取 → **对一个永久很慢的端点就是无限循环**。 |
| **`HttpRequestException`**(DNS 解析失败、连接被拒、TLS 握手失败、连接被重置,**以及围栏 ConnectCallback 抛的「解析后地址全部命中黑名单」**,`JobHttpFence.cs:118`) | `RetryableFailure` | **48031** | 网络层,经典瞬时故障。**已知的不精确并如实记账**:围栏拒连其实是永久的,但 `HttpRequestException` 不带可判别的类型信息;代价是重试满预算后落 `Failed`(≈3 次多余请求,且请求根本没发出去)。**不做异常消息字符串匹配**——那是比多试两次更坏的脆弱。 |
| **`AdminException` 来自 `JobHttpFence.ValidateUrl`/`ValidateHeader`**(47009 `JobHttpUrlBlocked` / `JobPropsInvalid`;**在开 socket 之前抛**) | `TerminalFailure` | **48030** | 配置缺陷,重试不可能修好。**必须捕获并映射,不许逸出**:逸出 ⇒ tx2 从未开始 ⇒ 行停 `Running` 持租约 ⇒ 租约过期被重领 ⇒ 再抛 ⇒ **无限活锁且 attempt 表一行都没有**——这正是 `WfNodeExecutionDispatcher.cs:24-28` 花一整段论证的形状。原始 47009 放进 `Summary` 供排障。 |
| **`WebhookUrl` 空白 / 非绝对 URI / 非 http(s)** | `TerminalFailure` | 48030 | 由 `ValidateUrl` 一并覆盖(`JobHttpFence.cs:18`)。 |
| **字面内网/元数据 IP**(如 `169.254.169.254`) | `TerminalFailure` | 48030 | `ValidateUrl` 的 CIDR 黑名单命中(`JobHttpFence.cs:25-27`)。 |
| **`WebhookMethod` 不在白名单** | `TerminalFailure` | 48030 | 见 D5。 |
| **header 名/值含 CR/LF/控制字符,或名为 `Host`/`Content-Length`** | `TerminalFailure` | 48030 | 见 D5。 |
| **响应体过大** | **不是错误** | — | 只读前 `AdminJobsHttpOptions.MaxResponseLogBytes`(默认 4096)字节做摘要就停(照 `HttpAdminJob.ReadCappedAsync`,`HttpAdminJob.cs:100-114`,含 NUL 净化——PG 的 text 列不收 `\0`),分类仍只看状态码。 |
| **其它任何异常** | **不 catch,原样逸出** | — | 见 §6 R5:blanket `catch (Exception)` 会把「未预料的 bug」悄悄归成一种业务结果。本 Task 生产侧无 worker,活锁不可达;等真建 worker 那一轮再定「未知异常兜底」。 |

**`ManualFallback` 何时出现**:分类表里**没有任何一格**默认产出 `ManualFallback`。它由**节点配置**打开——`props.webhookOnFailure = "manual"` 时,**本 handler 本来要返回的每一个 `TerminalFailure` 一律改成 `ManualFallback`**(错误码与摘要照旧)。理由见 D3。

---

**已知的不精确,如实记账**:围栏 `ConnectCallback` 拒连(解析后地址命中黑名单)其实是**永久**失败,但它表现为 `HttpRequestException`、不带可判别类型信息,故被归入 `RetryableFailure`;代价是重试满预算后才落 `Failed`(约 3 次多余 attempt,且请求根本没发出去)。**刻意不做异常消息字符串匹配**——那比多试两次更脆弱。

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段的 Agent 重写,协调者转写进本节)

> **当前 Plan = Task 8**(Webhook Handler:首个真实 Adapter)。Round 29 由 `Agent(model="opus")` 产出,全文经 `scratchpad/plan8.md` 交付(413 行),协调者原样转写(仅标题层级下沉一级)。Task 1–7 的历史 Plan 已被覆盖,其定案沉淀在 `## 语义契约` 与 `## Findings` 里。**D2 的两张分类表已另行抄进 `## 语义契约` 的「Webhook 分类规则」小节** —— 那里才是它的长期归属,本节被重写也不会丢。
>
> **协调者独立抽验了四条承重主张,全部属实**:①`JobHttpClient`/`JobHttpFence` 确在 `src/TenonAdmin.Services/Jobs/`,`JobHttpFence` 是 **`public static`**(`ValidateUrl:16`/`ValidateHeader:36`/`IsBlocked:51`/`CreateHandler:107`),而 Workflow 的 `.csproj` 只引 `TenonAdmin.AspNetCore` → 经分层可达 Services,**复用可行**;②`Microsoft.Extensions.Http` **已在** `backend/Directory.Packages.props:30`(v10.0.0)——**零新包**属实;③**超时/取消的区分判据仓内确有先例**,`HttpAdminJob.cs:41` 逐字是 `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)`,**比协调者在 prompt 里提示的 `ex.CancellationToken ==` 形式更贴本仓惯用法——子代理按代码事实选了更好的那个,记一笔**;④`WfNodeProps` **确在** `web/src/api/schema.d.ts:11634-11651`(且已有 `webhookUrl` 字段),`web-react` 同,**前端闸门确会被激活**属实。
>
> **协调者对 D4 的裁决(plan 点名要的那件事)**:**同意 D4,且不在本台账新增 Task 8b。** 理由:`## Tasks` 是十项,`## DONE-CONDITION` 写的就是「十项全部打勾」——加第 11 项等于改掉本 loop 自己的完成判据,那不是协调者能自行决定的;而 GOAL 里本就没有 worker,DONE-CONDITION 关于 Webhook 的那条(「Fake Handler 与 Webhook Handler 均有测试覆盖四条结果路径」)**不依赖 worker 也能满足**。故:**「`EnterNodeOp` 接线 + `WfNodeExecutionJob` + 种子行 + worker 标识来源」作为射程外挂账记在此处**,形状要求(`IAdminJob` + `sys_job` 种子行,复用 `JobSchedulerService` 选主与 `SysJobLock`,**不许新建第二套调度或 `BackgroundService` 轮询**)一并留档,交由用户决定是排新 Task 还是留给下一个里程碑。**D4 否掉「`EnterNodeOp` 里直接同步跑 dispatcher」的理由协调者完全认可**——那会把 HTTP 调用放进引擎命令的同一个事务里,当场违反本里程碑最核心的硬约束,且 T1 会转红。
>
> **exec 阶段要盯的四点**:①**前端闸门已激活**——`WfNodeProps` 加字段必跑 `cd web && npm run typecheck && npm run lint` + **双模板 `gen:api`** + `node scripts/check-contract-drift.mjs`,`schema.d.ts` 不许手改;②**不许引入 `Microsoft.Extensions.Http` 到 Workflow 包、不许写第二份 SSRF 围栏**(D1 的全部价值就在复用);③**不许改 `EnterNodeOp`、不建任何 HostedService/种子行**(D4);④**不许改 `WfNodeExecutionDispatcher` 的租约处置、不许改 `ResolveRetryDelay` 的 `(0,24h]` 钳制**(§7 明确按住的两条,前者已由 Task 7 N4 的租约断言钉死)。

> 本 plan 的每条「某处已经如何」都注明了读到它的**文件与行号**(行号取自 2026-09-02 干净树 `dev` @ `4bfc988`,工作树只有 `backend/tests/.../TestResults/` 未跟踪)。
> **头号交付物**:`## 语义契约` 里「Webhook 超时/重试分类 | 待 Task 8 plan 定案」那一行,由本 plan 的 **D2 表格**原样填实。

---

### §1 决策点定案

#### D1 `HttpClient` 从哪来 = **复用既有的 `JobHttpClient` + `JobHttpFence`,零新包、零新客户端、零新围栏代码**

**问题**:本仓运行期依赖只有 SqlSugarCore + Microsoft.\*;`IHttpClientFactory` 在 `Microsoft.Extensions.Http`。要不要加包?怎么拿到一个带 SSRF 防护的 `HttpClient`?

**实测结论(三条,逐条给出处)**:
1. `Microsoft.Extensions.Http` **已经在** `backend/Directory.Packages.props:30`(`Version="10.0.0"`),但**没有任何 `.csproj` 引用它**——全仓 `AddHttpClient` 的调用方(`ExternalAuthSetup.cs:22`、`DingTalkSetup.cs:33`、`GitHubSetup.cs:26`、`WeChatSetup.cs:25`、`WeComSetup.cs:36`)靠的是 `<FrameworkReference Include="Microsoft.AspNetCore.App" />` 带进来的共享框架。`TenonAdmin.Workflow.csproj` 已有这条 `FrameworkReference`。**所以 `IHttpClientFactory`/`HttpClient` 本来就在依赖树里,本 Task 一个包都不用加,`Directory.Packages.props` 一行都不改。**
2. 本仓对 HTTP 客户端的成法是**明文拒绝** `IHttpClientFactory`:`JobHttpFence.cs:99` 逐字写着「连接池 5 分钟轮换(长命 HttpClient 的 DNS 陈旧问题由此解决,**不需要 IHttpClientFactory**)」,`ServicesSetup.cs:169` 也写着「HTTP 客户端单例带 SSRF 围栏(**不引 IHttpClientFactory**)」。
3. **已经有一个可直接复用的成品**:`JobHttpClient`(`src/TenonAdmin.Services/Jobs/JobHttpClient.cs`)是 `public class`,`ServicesSetup.cs:172` 以 `TryAddSingleton<JobHttpClient>()` 注册,持有一个 `Timeout = Timeout.InfiniteTimeSpan` 的 `HttpClient`,其 handler 来自 `JobHttpFence.CreateHandler`(`JobHttpFence.cs:107-131`):**禁跟随重定向 + 禁代理 + ConnectCallback 里对解析后的每个 IP 复检 CIDR 黑名单(防 DNS rebinding)+ 连接池 5 分钟轮换**。`TenonAdmin.Workflow` → `TenonAdmin.AspNetCore` → `TenonAdmin.Services`,够得着。

**定案**:
- 不加任何包,不改 `Directory.Packages.props`。
- `WebhookNodeHandler` 的构造签名取 **`HttpClient`**(不是 `JobHttpClient`),DI 注册时用泛型工厂重载从 `JobHttpClient.Client` 取:这样**测试可以塞一个假 `HttpMessageHandler`,而被测的类与生产的类是同一个**(见 D6/§4)。
- URL/header 校验直接调 `JobHttpFence.ValidateUrl(url, jobs.Http)`(`JobHttpFence.cs:16-29`)与 `JobHttpFence.ValidateHeader(name, value)`(`JobHttpFence.cs:36-48`,拦 CRLF 走私)。
- 配置源复用 `AdminJobsOptions.Http`(`TenonAdmin:Jobs:Http`,`src/TenonAdmin.Core/Options/AdminJobsOptions.cs:48-64`)。

**被否掉的方案**:
- ❌ **加 `Microsoft.Extensions.Http` 的 `PackageReference` 并用 `AddHttpClient`**:包已在共享框架里,加了是噪音;而且和仓内两处明文成法相反。
- ❌ **新建 `WfWebhookHttpClient` + `WorkflowOptions.Webhook.BlockedCidrs` 一套自己的围栏**:那是把 `JobHttpFence` 那 133 行(含 DNS rebinding 复检、代理陷阱、CIDR 解析)抄第二遍——**安全代码抄第二遍就是第二个会各自腐化的实现**。代价是消费者收紧 `Jobs:Http:AllowedHosts` 时会同时约束 Webhook;判定为**特性不是缺陷**(一个出网白名单比两个好),但必须写进类注释与文档(§3 步骤 6)。
- ❌ **`WebhookNodeHandler(JobHttpClient http, …)`**:`JobHttpClient.Client` 是只读自动属性、非 virtual,测试**没有任何缝**能塞假 transport,只能真联网。

---

#### D2 ★ 头号交付物:超时 / 网络异常 / 状态码 → 三种结果的分类规则

> **以下两张表按「可原样抄进 `## 语义契约`」的形式写出**,协调者转写时可整块搬运。
> 贯穿全表的**唯一判据**:*「过一会儿原样再发一次同一个请求,有没有可能成功?」* 有 → `RetryableFailure`;没有 → `TerminalFailure`(或按节点配置转 `ManualFallback`,见 D3)。

##### 表 A:HTTP 状态码

| 状态码 | 结果 | `ErrorCode` | 理由 |
|---|---|---|---|
| **200–299** | `Succeeded` | — | |
| **300–399**(含 301/302/307/308) | `TerminalFailure` | 48029 | 围栏 handler `AllowAutoRedirect = false`(`JobHttpFence.cs:109`),重定向**不跟随**;拿到 3xx = 配的 URL 不对,再发一次永远还是同一个 3xx。修法是改流程定义,不是重试。 |
| **400 / 405 / 406 / 411 / 413 / 414 / 415 / 431** | `TerminalFailure` | 48029 | 请求本身不被接受。原样重发 ⇒ 原样的答复。 |
| **401 / 403** | `TerminalFailure` | 48029 | 凭据/权限。header 来自**静态节点配置**,下一次 attempt 发的是**逐字相同**的 header,不可能变好。**刻意不是 `ManualFallback`**:修法是改定义里的密钥,不是找个人来审批。 |
| **404 / 410** | `TerminalFailure` | 48029 | 端点不存在。 |
| **408** | `RetryableFailure` | 48029 | 服务端自己宣布「请求超时」——按定义就是瞬时的。 |
| **409** | `TerminalFailure` | 48029 | 冲突 = 远端说当前状态不对。**刻意不重试**:409 最常见的来源是对方的重复提交守卫,重试只会把同一个冲突撞 3 次。 |
| **423 / 425** | `RetryableFailure` | 48029 | Locked / Too Early,两者的 RFC 语义原文就是「稍后再试」。 |
| **429** | `RetryableFailure`,**读 `Retry-After`** | 48029 | 背压的规范形态。 |
| **其余 4xx**(402/407/418/422/451/…) | `TerminalFailure` | 48029 | 4xx 类的默认 = 请求方的问题,原样重发无意义。**422 明确归此**(我们发出去的载荷语义校验没过)。 |
| **501** | `TerminalFailure` | 48029 | Not Implemented——下一次 attempt 之前对方不会把它实现出来。**5xx 里唯一的例外,是定案不是遗漏。** |
| **其余 5xx**(500/502/503/504/505/…) | `RetryableFailure`,**若带 `Retry-After` 则读** | 48029 | 服务端侧故障,规范意义上的瞬时。 |

`Retry-After` 的读法(仅在**已判为 `RetryableFailure`** 的分支读,读到即填 `WfNodeExecutionResult.RetryAfter`):
`response.Headers.RetryAfter` 同时支持 delta-seconds 与 HTTP-date,取 `Delta ?? (Date - now)`;**结果 `<= 0` 或解析不出 ⇒ 传 `null`**(让引擎走自己的指数退避)。**上界不在 handler 钳**——`WorkflowEngine.ResolveRetryDelay`(`WorkflowEngine.cs:1452-1459`)已经把 `(0, 24h]` 之外的值一律退回 `30s << min(AttemptCount-1, 5)`,handler 再钳一次就是两个钳制点各自漂移。

##### 表 B:异常与配置

| 情形 | 结果 | `ErrorCode` | 判据 / 理由 |
|---|---|---|---|
| **`OperationCanceledException` 且外部 `cancellationToken.IsCancellationRequested`**(宿主停机 / dispatcher 取消) | **原样抛出,不产生任何结果** | — | 语义契约「取消语义(Task 2 定案)」:OCE = 「这次没跑完,**应可被重新领取**」。必须逸出到 dispatcher(`WfNodeExecutionDispatcher.cs:29-33`:全类零 `catch`,靠「没有 catch」落地)。 |
| **`OperationCanceledException`/`TaskCanceledException` 且外部 ct **未**取消**(= 本 handler 自己的超时) | `RetryableFailure` | **48031** | .NET 里 HTTP 超时抛的正是 `TaskCanceledException`(OCE 的子类),**必须与上一行区分开**。判据逐字照抄仓内既有先例 `HttpAdminJob.cs:41`:`catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)`。超时若当 OCE 逸出,attempt 预算不扣、行被反复重新领取 → **对一个永久很慢的端点就是无限循环**。 |
| **`HttpRequestException`**(DNS 解析失败、连接被拒、TLS 握手失败、连接被重置,**以及围栏 ConnectCallback 抛的「解析后地址全部命中黑名单」**,`JobHttpFence.cs:118`) | `RetryableFailure` | **48031** | 网络层,经典瞬时故障。**已知的不精确并如实记账**:围栏拒连其实是永久的,但 `HttpRequestException` 不带可判别的类型信息;代价是重试满预算后落 `Failed`(≈3 次多余请求,且请求根本没发出去)。**不做异常消息字符串匹配**——那是比多试两次更坏的脆弱。 |
| **`AdminException` 来自 `JobHttpFence.ValidateUrl`/`ValidateHeader`**(47009 `JobHttpUrlBlocked` / `JobPropsInvalid`;**在开 socket 之前抛**) | `TerminalFailure` | **48030** | 配置缺陷,重试不可能修好。**必须捕获并映射,不许逸出**:逸出 ⇒ tx2 从未开始 ⇒ 行停 `Running` 持租约 ⇒ 租约过期被重领 ⇒ 再抛 ⇒ **无限活锁且 attempt 表一行都没有**——这正是 `WfNodeExecutionDispatcher.cs:24-28` 花一整段论证的形状。原始 47009 放进 `Summary` 供排障。 |
| **`WebhookUrl` 空白 / 非绝对 URI / 非 http(s)** | `TerminalFailure` | 48030 | 由 `ValidateUrl` 一并覆盖(`JobHttpFence.cs:18`)。 |
| **字面内网/元数据 IP**(如 `169.254.169.254`) | `TerminalFailure` | 48030 | `ValidateUrl` 的 CIDR 黑名单命中(`JobHttpFence.cs:25-27`)。 |
| **`WebhookMethod` 不在白名单** | `TerminalFailure` | 48030 | 见 D5。 |
| **header 名/值含 CR/LF/控制字符,或名为 `Host`/`Content-Length`** | `TerminalFailure` | 48030 | 见 D5。 |
| **响应体过大** | **不是错误** | — | 只读前 `AdminJobsHttpOptions.MaxResponseLogBytes`(默认 4096)字节做摘要就停(照 `HttpAdminJob.ReadCappedAsync`,`HttpAdminJob.cs:100-114`,含 NUL 净化——PG 的 text 列不收 `\0`),分类仍只看状态码。 |
| **其它任何异常** | **不 catch,原样逸出** | — | 见 §6 R5:blanket `catch (Exception)` 会把「未预料的 bug」悄悄归成一种业务结果。本 Task 生产侧无 worker,活锁不可达;等真建 worker 那一轮再定「未知异常兜底」。 |

**`ManualFallback` 何时出现**:分类表里**没有任何一格**默认产出 `ManualFallback`。它由**节点配置**打开——`props.webhookOnFailure = "manual"` 时,**本 handler 本来要返回的每一个 `TerminalFailure` 一律改成 `ManualFallback`**(错误码与摘要照旧)。理由见 D3。

---

#### D3 `ManualFallback` 走**节点配置开关**,默认 `TerminalFailure`

**问题**:D2 的分类表纯按 HTTP 语义推,得不出任何 `ManualFallback`。但 `## DONE-CONDITION` 明文要求「Fake Handler 与 **Webhook Handler** 均有测试覆盖 `Succeeded`/`RetryableFailure`/`ManualFallback`/`TerminalFailure` **四条**结果路径」,台账 Task 8 原文也点名「是否有的场景该 `ManualFallback`」要在 plan 定案。

**定案**:新增节点配置 `props.webhookOnFailure`(枚举 `fail`(默认) | `manual`)。`manual` 时 handler 所有 `TerminalFailure` 出口改吐 `ManualFallback`。

**理由**:
- 两条结局在引擎里的**后果差别是硬的**:`TerminalFailure → Failed`,token 原地停住、**无人被通知**(`WorkflowEngine.cs:1298-1302` 的 `case Failed:` 什么都不 `Plan`);`ManualFallback → WfManualFallbackOp`,在该节点建一件真人 `wf_task`。审批流里「自动调用打不通,交给人来推」是**真实且常见**的诉求,把它写死成「永远不给人」是产品缺陷。
- 反过来,**默认给人**也不对:URL 配错就给全公司刷待办。所以默认 `fail`,要人工兜底得显式配。
- 一条规则、无子情形:`manual` 覆盖 handler 的**全部** `TerminalFailure`(含配置缺陷)。不做「4xx 给人、配置错不给人」这种二级分叉——那是要维护两张表。

**被否掉的方案**:
- ❌ **硬编码某些状态码 → `ManualFallback`**(如 409/422 交给人):把「这个业务愿不愿意人工兜底」这件**消费者的**决定写死进内核。
- ❌ **不做 `ManualFallback`,把 DONE-CONDITION 那条挂账给 Task 10**:那是拿「不做」当「测不到」,正是台账纪律点名禁止的。

**已知不对称,如实记账(§6 R3)**:`webhookOnFailure = manual` **不覆盖「重试预算耗尽」**——那条路由 `WorkflowEngine.ResolveExecutionOutcome` 的 `budgetExhausted` 分支直接判 `Failed`(`WorkflowEngine.cs:1417-1423`),handler 完全不参与。于是同一个节点:404 给人工任务,而超时 ×3 后静默 `Failed`。修它要动引擎核心路径(本 Task 禁碰),**不在本 Task 做**。

---

#### D4 **不建后台 worker,不改 `EnterNodeOp`**;R1「生产侧零调用点」**只解除一半**

**问题**:语义契约写死「第一条 DI 注册线由 Task 8 加」,Task 6/7 的 R1 也指名由 Task 8 解除。要不要顺手把最后一环接上——`EnterNodeOp` 对 `Webhook` 建 execution 行 + 一个后台轮询 worker?

**定案**:
- ✅ **加 DI 注册线**(D6)——契约点名的那条,做。
- ❌ **不改 `EnterNodeOp`**:`EnterNodeOp.cs:68-70` 的 `default:` 对 `WfNodeType.Webhook` 继续抛 48008。
- ❌ **不建 HostedService / 不建 `IAdminJob` 扫描任务 / 不建种子行**。
- R1 如实记为「**部分解除**:注册线有了、handler 有了,execution 行的生产创建方与调度方仍不存在」(§6 R1)。

**理由(三条,都是硬的)**:
1. **台账没有这一项。** Task 8 原文只有「节点类型 / 配置 schema / 事务外调用 / 分类映射」四件事;Task 9 是四库契约、Task 10 是验收,`## Tasks` 里**没有**「建 worker」。协调者的 Round-29 交待也是「**给判断而不是默认要做**」。
2. **`EnterNodeOp` 与 worker 是一个包,拆开做更坏。** 只改 `EnterNodeOp`(建 `Pending` 行然后不 `Plan` 任何 Op)= 流程从「发起时当场 48008 报错」退化成「**发起成功,然后永远卡住,没有任何错误**」。**静默悬挂比响亮报错坏。**
3. **建 worker 是一个 Task 的量,不是一个脚注。** 它要照 `WfTimeoutJob`(`src/TenonAdmin.Workflow/Jobs/WfTimeoutJob.cs`,含分页游标、处理预算 `MaxScanRounds`、单条失败隔离 `MaxTaskFailures`)+ `WfTimeoutJobSeed`(种子行,`WfTimeoutJobSeed.cs:35-50`;**光注册不种子 = 装了包但永不触发**)做一整套;还要解决 **lease owner 标识从哪来**——语义契约已实测记明 `JobTime.ResolveNodeName` 是 `internal`(实读 `src/TenonAdmin.Services/Jobs/JobTime.cs:6,16`),Workflow 包够不着,而在 Task 8 里现编第二套 worker 标识正是「**别发明第二套调度**」点名要防的事。

**给协调者的明确建议(不是本 Task 的产出,只是判断的落点)**:把「`EnterNodeOp` 接线 + `WfNodeExecutionJob` + 种子行 + worker 标识来源」作为**新增 Task 8b** 排在 Task 9 之前,或并入 Task 10 收口。形状**必须**是 `IAdminJob` + `sys_job` 种子行(复用 `JobSchedulerService` 的选主 + `SysJobLock`,ADR-0004,见 `WfTimeoutJob` 类注释第 2 段),**不许**新建第二套调度或 `BackgroundService` 轮询。

**被否掉的方案**:
- ❌ **`EnterNodeOp` 里直接同步跑 dispatcher**:那会把 HTTP 调用放进引擎命令的那**一个**事务里,**当场违反本里程碑最核心的硬约束**,且会让 `WfNodeExecutionDispatcherTests` 的 T1 转红。这是最诱人的捷径,明确否掉。

---

#### D5 Webhook 配置 schema:在 `WfNodeProps` 上**平铺追加 4 个可空属性 + 1 个新枚举**

**问题**:`WfNodeProps` 现在只有 `WebhookUrl`(`Schema/WfNode.cs:83`),不够。加在哪、加什么、存量 `ModelJson` 兼不兼容、安全面做到哪。

**定案(全部可空、纯追加)**:

| 属性 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `WebhookUrl` | `string?` | — | **已存在,一个字不改** |
| `WebhookMethod` | `string?` | `POST` | 白名单 `GET/POST/PUT/PATCH/DELETE/HEAD`(Ordinal 大小写不敏感);不在表内 → `TerminalFailure`/48030 |
| `WebhookHeaders` | `Dictionary<string, string?>?` | `null` | 逐对过 `JobHttpFence.ValidateHeader`;另**拒绝** `Host` 与 `Content-Length` 两个名字 |
| `WebhookTimeoutSeconds` | `int?` | `30` | **钳到 `[1, 120]`**,不报错(先例 `EnterNodeOp.MaxTimeoutHours`,`EnterNodeOp.cs:12-18`) |
| `WebhookOnFailure` | `WfWebhookFailureAction?` | `Fail` | 新枚举 `{ Fail, Manual }`,带 `[JsonConverter(typeof(CamelCaseEnumConverter))]` → JSON `fail`/`manual`(同 `WfRejectAction`,`WfSchemaEnums.cs:40-48`) |

**平铺而不是嵌套 `WfWebhook`**:`WebhookUrl` 已经是平铺的、**已经进了两个前端的 `schema.d.ts`**(`web/src/api/schema.d.ts:11650`,`web-react/` 同行)。改成嵌套要么留下一个孤儿字段、要么破坏存量 `ModelJson`。

**存量 `ModelJson` 兼容**:全部可空 + `System.Text.Json` 反序列化对缺失键取默认值 ⇒ **旧的 `ModelJson` 逐字照读**,零迁移、零回填。`WfNodeProps` 只进 `WfDefinitionVersion.ModelJson` 这个 string 列,**不是数据库列**,不触发任何 DDL。

**安全面定案(本 Task 做的)**:
- SSRF:`JobHttpFence.ValidateUrl` + 围栏 handler 的 ConnectCallback 复检(D1)。**默认只封云元数据段,不封内网**——`AdminJobsOptions.cs:54-60` 的既定选择,Webhook 打内网服务同样是主用途,**不为 Webhook 单独收紧**(收紧了消费者第一个 webhook 就打不通,而且会和 Jobs 的行为不一致)。
- Header 注入:`ValidateHeader` 已拦 CRLF 走私。**额外拒 `Host`**(IP 围栏管的是连到哪台机,`Host` 决定连上之后落到哪个 vhost,不拒 = 围栏漏一格)、**拒 `Content-Length`**(`TryAddWithoutValidation` 会写出一个和实际 body 不符的长度,后果是请求走私)。**只拒这两个**,不发明更长的黑名单。
- 超时上限 120s:上界的作用是**不让一次 HTTP 调用活得比租约长**——真跑起来时租约到期会让第二个 worker 领走同一行、两个 worker 同时打同一个 webhook。

**明确写进射程、本 Task 不做的**:URL 里的 userinfo/query 脱敏落库(`HttpAdminJob.cs:50-51` 有先例,但本 Task 不落任何执行日志)、per-node 的 host 白名单、mTLS/签名头(HMAC)、请求体自定义模板。

**请求体(台账没点名,但没有它这个特性不成立)**:非 `GET`/`HEAD` 时发一个引擎生成的最小 JSON,`Content-Type: application/json`,字段 = `executionKey / executionId(无,取 InstanceId 等) / instanceId / tokenId / nodeVisitId / nodeId / definitionVersionId / businessKey / attempt`,序列化走 `WfModelJson.Options`(先例 `WorkflowEngine.BuildExecutionOutboxPayload`,`WorkflowEngine.cs:1468-1494`)。**刻意不含 `VariablesJson`**——理由逐字复用 outbox 的 D6:handler 输入/输出是 PII 与密钥泄漏面最大的一处,消费方要正文用 `executionKey` 回查。`GET`/`HEAD` **不带 body**。

---

#### D6 DI 注册线:`TryAddEnumerable` + **`Scoped`** + 泛型工厂重载,落在 `WorkflowSetup` 的 SPI 段

**定案**(`WorkflowSetup.AddTenonAdminWorkflow` 内,紧跟 8 条 `IApproverProvider` 之后、`IWorkflowFormBinder` 之前):

```csharp
// 节点执行 SPI 的第一条注册线(M3a-1 Task 8)。HttpClient 取自内核的 JobHttpClient(单例,
// 带 SSRF 围栏),故本包不引 Microsoft.Extensions.Http、不建第二个客户端。
services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowNodeHandler, WebhookNodeHandler>(
    sp => new WebhookNodeHandler(
        sp.GetRequiredService<TenonAdmin.Services.JobHttpClient>().Client,
        sp.GetRequiredService<AdminJobsOptions>(),
        sp.GetRequiredService<TimeProvider>())));
```

- **`Scoped` 而非 `Singleton`**:`IWorkflowNodeHandler.cs:133` 的 XML 注释逐字写的就是 `ServiceDescriptor.Scoped<IWorkflowNodeHandler, MyHandler>()`;仓内同款 SPI(`IApproverProvider` ×8,`WorkflowSetup.cs:57-64`;`IAdminJob`,`:95`)全是 `Scoped`。handler 自身无状态,底层 `HttpClient` 仍是单例,不产生 socket 抖动。
- **泛型工厂重载**(`ServiceDescriptor.Scoped<TService, TImplementation>(Func<IServiceProvider, TImplementation>)`)而不是纯类型重载:`TryAddEnumerable` 靠 descriptor 的 `ImplementationType` 去重,泛型工厂重载能提供它;同时让构造签名收 `HttpClient`(D1),测试才有缝。
- **对十件套零影响**:`WorkflowReplaceabilityTests` 的类注释(`:8-14`)锁的是**十个 `TryAddScoped` 单实现面**,`TryAddEnumerable` 多实现面(`IApproverProvider`/`IAdminJob`/`ISeedData`)从来就不在其中。**`WorkflowReplaceabilityTests.cs` 本轮零改动、`[Fact]` 保持 10 条**(实测现有 10 条)。
- **消费者怎么换掉它**:两条路,都靠**前置注册**(`AddTenonAdminWorkflow` **之前**)。①注册自己的 `IWorkflowNodeHandler` 实现(`NodeType => WfNodeType.Webhook`)——`TryAddEnumerable` 按实现类型去重,内置那条仍会被追加,但 `WfNodeExecutionDispatcher.ResolveHandler` 用的是 `FirstOrDefault(h => h.NodeType == …)`(`WfNodeExecutionDispatcher.cs:107-108`),**枚举顺序 = 注册顺序**,先注册的赢。②继承 `WebhookNodeHandler` 覆写单步(类不 `sealed`、分类方法 `virtual`)再前置注册子类。这条保证由 §4 的 **W-DI2** 钉住,**放在新测试文件里,不许进十件套**。

---

#### D7 错误码:新增 **3** 个(48029/48030/48031),48022 仍不填回

| 常量 | 值 | 语义 |
|---|---|---|
| `WebhookRequestFailed` | **48029** | 远端返回了非成功状态码;args 带 `status` |
| `WebhookConfigInvalid` | **48030** | URL / method / header / 围栏拒绝等配置缺陷(socket 未开) |
| `WebhookTransportFailed` | **48031** | 超时 / DNS / 连接 / TLS 等传输层故障 |

三个而不是一个:这些码会落进 `wf_node_execution_attempt.ErrorCode` 与 `wf_node_execution.ErrorCode`,是排障时**唯一**能区分「对方回了 500」/「压根没连上」/「配置就是错的」的东西。48022 是历史空号,按 `WorkflowErrorCode.cs:93` 的既定说明**不填回**。

---

#### D8 「HTTP 调用在事务外」怎么测:**加一条 Webhook 专属的,理由不是凑数**

`WfNodeExecutionDispatcherTests` 的 T1(`:31-70`)已用 `db.Ado.IsAnyTran()` 探针钉住这条,且经 Task 6 review 的 A1 变异验证转红。但 T1 的探针挂在 **`FakeNodeHandler.OnExecute`** 上,证明的是「**handler 被调用时**没有事务」。

**定案**:再加一条 **W-TX**,探针挪到**假 `HttpMessageHandler.SendAsync` 内部**——即「**socket 即将打开的那一刻**没有事务」,并且跑的是**真的 `WebhookNodeHandler` 经真的 `WfNodeExecutionDispatcher`**。这不是 T1 的复制:DONE-CONDITION 那句写的是「**远程调用(Webhook HTTP 请求)**发生在数据库事务之外」,W-TX 是唯一一条把探针放到字面所指位置的测试。**只加这一条**;四条结果路径的端到端重放不再做(§6 R4)。

---

### §2 改动清单(文件级)

#### 新增(2 个文件)

| 文件 | 量级 | 内容 |
|---|---|---|
| `backend/src/TenonAdmin.Workflow/Providers/WebhookNodeHandler.cs` | **新增 ~220–260 行** | `public class WebhookNodeHandler(HttpClient client, AdminJobsOptions jobs, TimeProvider time) : IWorkflowNodeHandler`。不 `sealed`,分类/组请求/读响应各自 `protected virtual`。含 `public static TimeSpan ResolveTimeout(int? configuredSeconds, DateTimeOffset deadlineAtUtc, DateTimeOffset nowUtc)`(`public` 的理由同 `WfNodeExecutionStore`:全仓无 `InternalsVisibleTo`)。**放 `Providers/` 不新建目录**(内置 SPI 实现的既有落点,`BuiltInApproverProviders.cs` 同目录;沿用 Task 2/3「不预建新目录」的定案)。 |
| `backend/tests/TenonAdmin.Tests/WfWebhookNodeHandlerTests.cs` | **新增 ~600–750 行** | 见 §4。含私有 `FakeTransport : HttpMessageHandler`(照 `GitHubWeChatAuthProviderTests.cs:16` 的 `SeqHandler`)。 |

#### 修改(5 个文件)

| 文件 | 量级 | 内容 |
|---|---|---|
| `backend/src/TenonAdmin.Workflow/Schema/WfNode.cs` | **+~40 / -0** | `WfNodeProps` 的 webhook 段追加 4 个属性;文件末尾追加 `WfWebhookFailureAction` 枚举 + `CamelCaseEnumConverter` 特性。**`WebhookUrl` 一个字不改。** |
| `backend/src/TenonAdmin.Workflow/Abstractions/WorkflowErrorCode.cs` | **+~15 / -0** | 3 个常量 + 注释 |
| `backend/src/TenonAdmin.Workflow/WorkflowSetup.cs` | **+~8 / -0** | D6 那一条注册 + 注释 |
| `web/src/api/schema.d.ts` | **+~8 / -0(生成物)** | `npm run gen:api` 重生成,**不许手改** |
| `web-react/src/api/schema.d.ts` | **+~8 / -0(生成物)** | 同上,两边 SHA256 必须一致 |

#### ⛔ 禁碰文件清单(本 Task 一行都不改)

- `Engine/WfNodeExecutionDispatcher.cs` —— **尤其是租约处置**。Task 7 P2-3 的变异已经证明:给 `InvokeHandlerAsync` 加一个「catch OCE 顺手释放租约」的分支会让 346 条全绿地引入静默 bug,现在由 N4 守门。语义契约里「OCE 要让 lease 过期/释放」那句话**不是让 Task 8 去实现「立刻释放」**。
- `Engine/WfNodeExecutionStore.cs` / `Engine/WfNodeExecutionAttemptStore.cs` / `Engine/WfOutboxStore.cs`
- `Engine/WorkflowEngine.cs` —— 含 `ResolveExecutionOutcome` / `ResolveRetryDelay` / `ClaimExecutionWritebackAsync`。D3 的不对称记账,**不在这里修**。
- `Engine/Operations/EnterNodeOp.cs`(D4)、`Engine/Operations/WfManualFallbackOp.cs`
- `Entities/WfNodeExecution.cs` / `WfNodeExecutionAttempt.cs` / `WfOutbox.cs` —— **本 Task 零 DDL、零新实体、零列变更**
- `Abstractions/IWorkflowNodeHandler.cs`(SPI 形状不动)
- `src/TenonAdmin.Services/Jobs/JobHttpFence.cs` / `JobHttpClient.cs` / `HttpAdminJob.cs` / `ServicesSetup.cs` —— 只调用,不修改
- `backend/Directory.Packages.props` / 任何 `.csproj`(D1)
- `tests/.../WorkflowReplaceabilityTests.cs`(必须保持 **10** 条 `[Fact]`)
- `tests/.../WfNodeExecutionDispatcherTests.cs` / `WfFakeNodeHandler.cs` / `WfNodeHandlerContractTests.cs` —— 现有 346 条一条不删不改
- `.loop/wf-m3a1.md` —— 由协调者落笔

---

### §3 实施步骤(有序,每步可独立验证)

1. **错误码**:`WorkflowErrorCode.cs` 加 48029/48030/48031。验证:`dotnet build backend/TenonAdmin.slnx -c Release` 通过。
2. **配置 schema**:`WfNode.cs` 加 4 个属性 + `WfWebhookFailureAction` 枚举。验证:build 通过;**临时**跑一次 `WfDefinitionService` 相关测试确认发布校验未被波及(不新增校验逻辑,`ValidateModelForPublish` 不动)。
3. **`ResolveTimeout` 先落地并单测**:纯静态函数,`min(clamp(configured ?? 30, 1, 120), deadlineAtUtc - nowUtc)`,下限 1s。先写 W-T1 那组 `[Theory]`(§4),**红 → 绿**。这一步没有 HTTP、没有 DB,是整个 Task 里唯一能纯函数验证的部分,先做能把差一/钳制的争议一次性关掉。
4. **`WebhookNodeHandler` 主体**:配置校验(URL/method/header)→ 组请求 → 发 → 分类。**分类逻辑写成一个独立的 `protected virtual WfNodeExecutionResult ClassifyStatus(HttpResponseMessage, string excerpt)`**,好让 §4 的状态码 `[Theory]` 与 §6 的可替换性都指向同一个点。验证:W-CFG / W-STATUS 全组绿。
5. **异常分类**:三个 `catch` 子句,**顺序写死**:`catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` → 超时;`catch (HttpRequestException)` → 传输;`catch (AdminException)` → 配置(注意这条要包住 `ValidateUrl`/`ValidateHeader` 的调用,而它们在发请求之前)。**不写 `catch (Exception)`。** 验证:W-OCE-* / W-NET 绿。
6. **DI 注册线** + 类注释(写明「围栏配置复用 `TenonAdmin:Jobs:Http`」,D1)。验证:W-DI1 / W-DI2 绿;**跑十件套确认仍 10 条且全绿**。
7. **W-TX**(dispatcher 端到端探针,D8)。验证:绿;并做一次即时变异——把 `handler.ExecuteAsync` 挪进 tx1 闭包 → W-TX 与 T1 一起红 → 复原。
8. **前端契约闸门(本 Task 必跑,DONE-CONDITION 明文)**。`WfNodeProps` **确实在两个前端的 OpenAPI schema 里**(`web/src/api/schema.d.ts:11634-11651`,`web-react/` 同行),加字段 = 改 API 面:
   ```bash
   dotnet run --project backend/samples/MinimalHost      # 另开一个窗口,Development
   cd web        && npm run gen:api && npm run typecheck && npm run lint
   cd web-react  && npm run gen:api
   # 两份 schema.d.ts 的 SHA256 必须一致
   node scripts/check-contract-drift.mjs                  # 它自己起 host,最终以它为准
   ```
   **不改任何前端页面/i18n**——纯追加的可空字段对 TS 是向后兼容的,设计器 UI 归 M3a-2(禁区)。
9. **全量闸门**:`dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"`,**346 → 预计 380–395,只增不减**,十件套仍 10。
10. **变异自证**(exec 阶段必做,报告里逐条列出):见 §4 每条测试的「变异」列,至少跑 §4 标 ★ 的 6 条。

---

### §4 测试清单

> **全部新测试进 `WfWebhookNodeHandlerTests.cs` 一个文件。** 除 W-TX 外**一律不碰数据库、不起 `WorkflowAppFactory`**——分类逻辑是纯函数 + HTTP,拉起整个 host 只会让 40 条测试各慢 100 倍还什么都不多证明。
> 假 transport:`private sealed class FakeTransport : HttpMessageHandler`,记录 `SendCount` 与 `LastRequest`,按构造参数返回状态/头/体,或抛指定异常,或 `await Task.Delay(Timeout.Infinite, ct)`。**零真实网络。不引任何新测试包(WireMock 等一律禁止)。**
> 上下文:一个 `private static WfNodeExecutionContext Ctx(WfNodeProps props, int attempt = 1, DateTimeOffset? deadline = null)` 辅助,`NodeType = WfNodeType.Webhook`。

#### A 组 — `ResolveTimeout` 纯函数(`[Theory]`,5 行)

| # | 名字 | 钉住什么 | 在什么变异下必须转红 |
|---|---|---|---|
| W-T1 | `Timeout_is_clamped_and_capped_by_the_deadline`(5 `[InlineData]`) | `(null, 远 deadline) → 30s`;`(500, 远) → 120s`;`(0, 远) → 1s`;`(-1, 远) → 1s`;`(120, deadline=now+3s) → 3s` | ★ 去掉上钳 → 第 2 行红;去掉下钳 → 第 3/4 行红;**去掉 deadline 参与 → 第 5 行红**;默认从 30 改 60 → 第 1 行红 |

断值一律**字面量**(`TimeSpan.FromSeconds(30)` 等),不引产品常量。

#### B 组 — 状态码分类(`[Theory]`,约 21 行,★ 本 Task 的头号交付物)

| # | 名字 | 钉住什么 | 在什么变异下必须转红 |
|---|---|---|---|
| W-S1 | `Http_status_maps_to_the_contracted_result_type` | 每行 `(int status, WfNodeExecutionResultType expected, int? expectedErrorCode)`,覆盖 **200/204/299/301/302/400/401/403/404/405/408/409/410/422/423/425/429/500/501/502/503/504**。断言 **`result.Type`** 与 **`result.ErrorCode`(字面 `48029` / `null`)** 两列 | ★ 任意一格改归属 → 恰好那一行红。特别标记:把 408 从 Retryable 改 Terminal、把 409 从 Terminal 改 Retryable、把 501 并回 5xx 的 Retryable、把 3xx 并进 2xx —— **四个变异各只红一行** |

> **禁写**:不许写成 `Assert.Equal(WorkflowErrorCode.WebhookRequestFailed, result.ErrorCode)`(常量自证,两边都来自产品代码)。**用字面 `48029`。** 状态码同样用字面 int,不用 `HttpStatusCode.NotFound`。

#### C 组 — `Retry-After`(3 条)

| # | 名字 | 钉住什么 | 变异 |
|---|---|---|---|
| W-R1 | `Retry_after_delta_seconds_is_passed_through` | 429 + `Retry-After: 120` → `Assert.Equal(TimeSpan.FromSeconds(120), result.RetryAfter)`(**量值,不是 `NotNull`,不是 `<=`**) | 不读该头 → 红 |
| W-R2 | `Retry_after_http_date_is_converted_to_a_delta` | 503 + `Retry-After: <now+60s 的 HTTP-date>` → `Assert.Equal(TimeSpan.FromSeconds(60), result.RetryAfter!.Value, TimeSpan.FromSeconds(5))` | 只处理 delta 分支 → 红 |
| W-R3 | `A_non_positive_retry_after_yields_null` | 503 + `Retry-After: 0` → `Assert.Null(result.RetryAfter)`,且 `result.Type == RetryableFailure` | 原样透传 0 → 红(0 会让引擎的 `> TimeSpan.Zero` 守门吞掉,行为对但语义脏;本条钉的是 handler 不吐 0) |

#### D 组 — 异常分类(5 条,★ 含本 Task 最要紧的 OCE 边界)

| # | 名字 | 钉住什么 | 变异 |
|---|---|---|---|
| W-X1 ★ | `A_request_timeout_becomes_a_retryable_failure_and_no_cancellation_escapes` | transport `await Task.Delay(Timeout.Infinite, ct)`,`WebhookTimeoutSeconds = 1`,外部 ct = `CancellationToken.None`。**先** `var ex = await Record.ExceptionAsync(...)` → `Assert.Null(ex)`(**没有 OCE 逸出**)→ 再断 `Type == RetryableFailure`、`ErrorCode == 48031` 字面 | ★ 把 `when (!cancellationToken.IsCancellationRequested)` 去掉并改成无条件 rethrow → 红;把超时归成 `TerminalFailure` → 红 |
| W-X2 ★ | `An_externally_cancelled_call_lets_the_cancellation_escape` | 外部 ct **已取消**。顺序:**先**断 `transport.SendCount == 0`(副作用先断)→ 再 `Assert.IsAssignableFrom<OperationCanceledException>(ex)` | ★ 把外部取消也归成 `RetryableFailure`(即漏掉 `when` 过滤器)→ 红。**W-X1 与 W-X2 合起来才是「两种 OCE 必须分开」的鉴别力,单独任何一条都不够** |
| W-X3 ★ | `A_cancellation_mid_flight_escapes_instead_of_becoming_a_result` | transport 挂在 ct 上,测试在飞行中 `cts.Cancel()`;超时配 120s(确保不是超时先到)。顺序:先断**没有产生任何 `WfNodeExecutionResult`**(用 `Record.ExceptionAsync` 拿到非 null)→ 再断类型 | ★ 同 W-X2,但形状不同:W-X2 是「进门前就取消」,W-X3 是「进门后取消」——只有 W-X3 能抓住「只在方法入口检查一次 ct」的实现 |
| W-X4 | `A_transport_failure_becomes_a_retryable_failure` | transport 抛 `HttpRequestException` → `Type == RetryableFailure`、`ErrorCode == 48031` 字面、`Summary` 非空且含异常信息(断 `Contains`,不是 `NotNull`) | 归成 `TerminalFailure` → 红;不填 `ErrorCode` → 红 |
| W-X5 | `An_unexpected_exception_is_not_swallowed` | transport 抛 `InvalidOperationException` → **原样逸出**(`Assert.IsType<InvalidOperationException>(ex)`),`SendCount == 1` | 加一个 `catch (Exception)` 兜底 → 红。这条是 D2 表 B 末行那条定案的守门 |

#### E 组 — 配置校验(6 条,全部「先断副作用为 0,再断结果」)

统一顺序:**① `Assert.Equal(0, transport.SendCount)` ② `Assert.Equal(WfNodeExecutionResultType.TerminalFailure, result.Type)` ③ `Assert.Equal(48030, result.ErrorCode)`**(禁写清单第 4 条:副作用先断)。

| # | 名字 | 输入 | 变异 |
|---|---|---|---|
| W-C1 | `A_blank_url_is_a_terminal_config_failure` | `WebhookUrl = null` | 空 URL 直接抛 → `SendCount` 断言前先炸,红 |
| W-C2 | `A_non_http_scheme_is_a_terminal_config_failure` | `file:///etc/passwd` | 跳过 `ValidateUrl` → `SendCount == 1`,红 |
| W-C3 ★ | `A_blocked_metadata_address_never_opens_a_socket` | `http://169.254.169.254/latest/meta-data` | ★ 跳过 `ValidateUrl` → `SendCount == 1`,红。**这是「SSRF 围栏真的接上了」的唯一直接证据** |
| W-C4 | `A_header_with_crlf_is_a_terminal_config_failure` | header 值含 `"\r\nX-Injected: 1"` | 跳过 `ValidateHeader` → 红 |
| W-C5 | `A_host_header_is_rejected` | header `Host: internal.svc` | 去掉 `Host` 拒绝 → 红 |
| W-C6 | `An_unsupported_method_is_a_terminal_config_failure` | `WebhookMethod = "TRACE"`;另一条 `[InlineData]` 用 `"BAD METHOD"`(含空格,`new HttpMethod` 会抛 `FormatException`) | 去掉白名单 → 前者 `SendCount == 1` 红、后者变成未捕获 `FormatException` 红 |

#### F 组 — 请求组装(3 条)

| # | 名字 | 钉住什么 | 变异 |
|---|---|---|---|
| W-Q1 ★ | `The_outgoing_request_carries_the_context_and_never_the_variables` | 断 `LastRequest.Method == HttpMethod.Post`(**默认值**,props 里不配 method)、`RequestUri` 字面、配置的 header 在 `LastRequest.Headers` 里、`Content-Type` 为 `application/json`;解析 body:`executionKey`/`instanceId`/`nodeId`/`attempt` **等于上下文里的那些值**;并 **`Assert.False(root.TryGetProperty("variablesJson", out _))` 与 `variables` 同款一条** | ★ 默认 method 改 GET → 红;把 `VariablesJson` 塞进 payload → 红(**这条是 D5 的 PII 决定的唯一守门,形状照抄 Task 6 review P2-2 的修法**);字段接错(如把 `TokenId` 填进 `instanceId`)→ 红 |
| W-Q2 | `A_get_webhook_sends_no_body` | `WebhookMethod = "get"`(小写,顺带钉大小写不敏感)→ `Assert.Null(LastRequest.Content)`,`Method == HttpMethod.Get` | 无条件带 body → 红;方法名大小写敏感 → 红 |
| W-Q3 | `An_oversized_response_body_is_capped_and_truncated` | 200 + 100KB 的 `'a'` → `Assert.Equal(512, result.Summary!.Length)`(**量值**);且 `result.OutputJson!.Length` 等于 `MaxResponseLogBytes` 默认 4096 的字面值 | 不做 capped read(整体 `ReadAsStringAsync`)→ `OutputJson` 长度断言红;不 `Truncate` → `Summary` 断言红 |

#### G 组 — `ManualFallback` 开关(3 条)

| # | 名字 | 钉住什么 | 变异 |
|---|---|---|---|
| W-M1 ★ | `On_failure_manual_turns_a_terminal_status_into_a_manual_fallback` | 404 + `WebhookOnFailure = Manual` → `Type == ManualFallback`,`ErrorCode == 48029`(**码不变**) | ★ 忽略该配置 → 红。**这是 DONE-CONDITION「Webhook 覆盖四条结果路径」里第四条的唯一来源** |
| W-M2 | `On_failure_manual_also_covers_config_failures` | 空 URL + `Manual` → `Type == ManualFallback`,`ErrorCode == 48030`,`SendCount == 0` | 只在状态码分支应用该配置 → 红(D3「一条规则、无子情形」的守门) |
| W-M3 | `On_failure_manual_does_not_touch_retryable_results` | 503 + `Manual` → `Type == RetryableFailure`(**仍是重试,不是人工**) | 把 `Manual` 无差别套到所有失败上 → 红 |

#### H 组 — DI 与可替换性(2 条,需 `WorkflowAppFactory`)

| # | 名字 | 钉住什么 | 变异 |
|---|---|---|---|
| W-DI1 | `The_webhook_handler_is_registered_and_resolvable` | 从 `WorkflowAppFactory` 的 scope 取 `GetServices<IWorkflowNodeHandler>()`,断**恰好一个** `WebhookNodeHandler` 且其 `NodeType == WfNodeType.Webhook` | 注册线被删 → 红;`JobHttpClient` 解析不到(工厂写错) → 红 |
| W-DI2 | `A_pre_registered_handler_wins_over_the_built_in_one` | 用 `ConfigureTestServices` **前置**注册一个 `NodeType == Webhook` 的假 handler;断 `GetServices<IWorkflowNodeHandler>().First(h => h.NodeType == Webhook)` 是那个假的 | `TryAddEnumerable` 改成无条件 `Add` 且顺序颠倒 → 红。**放本文件,绝不进 `WorkflowReplaceabilityTests`(十件套必须保持 10 条)** |

> ⚠ `WorkflowReplaceabilityTests` 的 `BuildProvider` 是**裸容器**(只调 `AddTenonAdminWorkflow`),里面**没有** `JobHttpClient`。W-DI1/W-DI2 必须走 `WorkflowAppFactory`(完整宿主),不要图省事套裸容器——会红在 `GetRequiredService<JobHttpClient>` 上,而那不是被测的东西。

#### I 组 — 事务边界(1 条,需 DB,D8)

| # | 名字 | 钉住什么 | 变异 |
|---|---|---|---|
| W-TX ★ | `The_webhook_http_call_happens_outside_any_database_transaction` | 复用 `WfNodeExecutionDispatcherTests` 同款脚手架(定义版本 → `StartInstanceCmd` → token → `WfNodeExecutionStore.EnsureAsync`),把**真** `WebhookNodeHandler`(transport 是假的)交给**真** `WfNodeExecutionDispatcher.RunAsync`。探针放在 `FakeTransport.SendAsync` 里:`sawTran = db.Ado.IsAnyTran(); sawTranObj = db.Ado.Transaction is not null;`,**两个初值都设 `true`**(照 T1 `:39-40`,否则探针没跑也绿)。再断 `SendCount == 1`、`status == Succeeded` | ★ 把 `handler.ExecuteAsync` 挪进 tx1 的 `UseTranAsync` 闭包 → W-TX **与 T1 一起**红 |

#### 预计条数

`[Theory]` 行按条计:A 组 5 + B 组 ~21 + C 组 3 + D 组 5 + E 组 7(W-C6 两行)+ F 组 3 + G 组 3 + H 组 2 + I 组 1 ≈ **50**。
保守区间:**346 → 385–400**,**只增不减**,十件套仍 **10**。若 exec 实际条数低于 380,须在报告里说明砍了哪组、为什么。

#### ⛔ 禁写清单

1. **不许用 `Assert.NotNull` 钉时间/摘要/延迟**——`RetryAfter`、`Summary` 一律断值(Task 6 P2-1、Task 7 P2-3 反复吃过的亏)。
2. **不许松散边界代替量值**——`Assert.True(delay <= 24h)` 这种形状被 Task 7 P2-2 实测证明会放行「被注释明确排除的实现」。C 组、A 组全部用 `Assert.Equal`(必要时带容差)。
3. **不许常量自证**——对外契约(48029/48030/48031、状态码、512、4096、30/120/1 秒)一律**字面值**。
4. **不许「只断抛了异常」**——E 组、D 组全部**先断 `SendCount`**,再断结果/异常。断言顺序错会让失败消息把人引向错误因果(Task 7 P2-1 的原形)。
5. **不许一个 `[Fact]` 串两段**——一个行为一条。C 组三条不许合并(Task 6 T6 的教训)。
6. **不许给 `WorkflowReplaceabilityTests` 加第 11 条 `[Fact]`。**
7. **不许为分类逻辑起 `WorkflowAppFactory`**——只有 W-TX / W-DI1 / W-DI2 三条允许碰宿主。
8. **不许真联网**,不许引 WireMock 或任何新测试包。
9. **不许再钉一次「dispatcher 层面 handler 无事务」**——T1 已经钉了且经 A1 变异验证;W-TX 钉的是**更下游的 socket 时刻**,不是同一个断言的复制。
10. **不许改动/删除现有 346 条中的任何一条。**

---

### §5 陷阱

1. **`HttpClient` 超时抛的是 `TaskCanceledException`,而它是 `OperationCanceledException` 的子类。** 语义契约「handler 抛 OCE = 应可被重新领取」与「HTTP 超时应算 `RetryableFailure`」在类型上**是同一个异常**。判据必须是 `when (!cancellationToken.IsCancellationRequested)`——出处:`src/TenonAdmin.Services/Jobs/HttpAdminJob.cs:41` 逐字就是这一句。**别用 `ex.InnerException is TimeoutException`**:那只在 `HttpClient.Timeout` 触发时成立,而我们用的是 `Timeout.InfiniteTimeSpan` + 自己的 `CancelAfter`(`JobHttpClient.cs:14-17`),`InnerException` 是 null。
2. **`WfNodeExecutionDispatcher` 的租约处置不归本 Task 改。** Task 7 review P2-3 实测:给 `InvokeHandlerAsync` 加「catch OCE 顺手释放租约」→ **346/346 全绿**(现由 N4 的两条租约断言守门)。语义契约「OCE 要让 lease 过期/释放」说的是**过期后自然可被重领**,不是让谁去主动清。`WfNodeExecutionDispatcher.cs` 在禁碰清单里。
3. **`JobHttpClient.Client.Timeout` 是 `Timeout.InfiniteTimeSpan`**(`JobHttpClient.cs:16`)。**不要**指望 `HttpClient` 帮你超时,必须自己 `CancellationTokenSource.CreateLinkedTokenSource(ct)` + `CancelAfter`(照 `HttpAdminJob.cs:32-34`)。忘了这一步 = webhook 永不超时,租约到期后被第二个 worker 领走,两个 worker 同时打同一个端点。
4. **围栏 handler 禁用了自动重定向和代理**(`JobHttpFence.cs:109-111`)。所以 3xx 会**原样返回**给我们(D2 表 A 因此把 3xx 判成 `TerminalFailure`,不是「HttpClient 会自己跟过去」)。**别把 `AllowAutoRedirect` 打开**——`JobHttpFence.cs:100-103` 有整段论证:开代理会让 ConnectCallback 只看见代理 IP,**IP 围栏整个归零(实测能经代理取回云元数据)**。
5. **响应体必须 capped read,不能 `ReadAsStringAsync()`。** 一个吐 2GB 的端点会把宿主打 OOM。照 `HttpAdminJob.ReadCappedAsync`(`HttpAdminJob.cs:100-114`),连同它的**控制字符净化**一起抄——那段注释记着一个实测过的坑:目标返回二进制含 `\0` 时 **PostgreSQL 的 text 列不接受 NUL(22021)**,写库会抛。摘要要落 `wf_node_execution_attempt.ErrorSummary`/`OutputSummary`,同一个坑照样成立。用 `HttpCompletionOption.ResponseHeadersRead`(`HttpAdminJob.cs:39`)。
6. **`WfNodeProps` 在 OpenAPI 里,加字段会激活前端闸门。** 实读:`web/src/api/schema.d.ts:11634-11651` 有完整的 `WfNodeProps` 块(`webhookUrl` 在 `:11650`),`web-react/src/api/schema.d.ts` 同行同内容。**不跑 `gen:api` 就提交 = `.githooks` 的 pre-push `check-contract-drift.mjs` 当场拦下。** §3 步骤 8 是必做项,不是可选项。
7. **`WfNodeType` 带 `allowIntegerValues: true`**(`Schema/WfSchemaEnums.cs:10`),存量 `ModelJson` 里可能是整数值。本 Task **不动 `WfNodeType`**(`Webhook` 早就存在,`:25`),但新枚举 `WfWebhookFailureAction` 必须挂 `[JsonConverter(typeof(CamelCaseEnumConverter))]`,否则 JSON 里会变成数字,和 `WfRejectAction`(`:40-48`)等既有枚举不一致。
8. **`SetColumns` 禁止内联 `DateTime`/`null`(zh-CN 会格式化出 `下午` 炸 SQL)。** 本 Task **一行 `SetColumns` 都不该写**——如果 exec 发现自己在 handler 里写数据库,**那就是越界了**:语义契约「handler 不得推进 token、不得写任务状态、不得自开数据库事务」,而 `WfNodeExecutionContext` 里根本没有 `ISqlSugarClient`(`IWorkflowNodeHandler.cs:83`「不含 SqlSugar 实体本身、不含 `ISqlSugarClient`」,有结构化断言守着)。
9. **`DeadlineAtUtc` 是 `DateTimeOffset` 且**已经**有一个读取点**(`WfNodeExecutionDispatcher.cs:156`:`execution.DeadlineAtUtc ?? nowUtc + leaseDuration`)。用它算相对超时时**不要**自己 `new DateTimeOffset(...)` 或 `.DateTime` 再比——dispatcher 已经 `SpecifyKind(Utc)` 过(`:175`),直接 `deadline - time.GetUtcNow()` 即可。本机是 **UTC+8**,任何一次多余的 Kind 转换都会悄悄偏 8 小时。
10. **本仓不存在「实体类型登记表」。** 本 Task 零新实体,但顺带记牢:CodeFirst 靠 `WorkflowSetup.cs:28` 的整程序集扫描,别去找也别去建任何 `typeof` 列表。
11. **`TryAddEnumerable` 按实现类型去重,不按接口。** 消费者注册 `MyWebhookHandler : IWorkflowNodeHandler` 是**新增**一条,不是替换;真正决定谁生效的是 `WfNodeExecutionDispatcher.ResolveHandler` 的 `FirstOrDefault`(`:107-108`)= **注册顺序**。这条陷阱有完整先例文档:`WfTimeoutJob` 类注释第 4 段(「覆写本类的方法不足以换掉行为」)讲的就是同一个机制在 `IAdminJob` 上的表现。D6/W-DI2 按这个真实机制写,别在注释里宣称「TryAdd 所以后注册的不生效」。
12. **`WorkflowReplaceabilityTests.BuildProvider` 是裸容器,里面没有 `JobHttpClient`。** 任何在裸容器里解析 `IWorkflowNodeHandler` 的尝试都会红在依赖解析上,而不是红在被测契约上(§4 H 组的注解)。
13. **`AdminJobsOptions` 从 DI 拿得到吗?** `HttpAdminJob` 的构造签名是 `(JobHttpClient http, AdminJobsOptions options)`(`HttpAdminJob.cs:15`),说明它**是**一个可解析的注册项。exec 第一步先用一条 W-DI1 验证这条依赖链在 `WorkflowAppFactory` 里成立,**别等写完 250 行才发现解析不了**。
14. **`Retry-After` 的 `Date` 分支要用 `TimeProvider`,不要 `DateTimeOffset.UtcNow`。** handler 已经注入了 `TimeProvider`(`WorkflowSetup.cs:53` 注册的 `TimeProvider.System`),混用两个时间源会让 W-R2 在慢机器上偶发。
15. **别为 Webhook 收紧 `BlockedCidrs` 默认值。** `AdminJobsOptions.cs:54-60` 明写「**不封内网**——调度器打内网服务是主用途」。Webhook 同理(企业内的审批回调多数就在内网)。改默认值会让所有既有 Jobs 消费者一起受影响,属于跨模块的破坏性变更。

---

### §6 射程限制

- **R1｜「生产侧零调用点」只解除一半。** ✅ DI 注册线有了、`WebhookNodeHandler` 有了、W-DI1 证明它可被解析。❌ **仍然没有任何生产代码创建 `wf_node_execution` 行,也没有任何生产代码调用 `WfNodeExecutionDispatcher`**(实测:`grep -rn WfNodeExecutionDispatcher backend/src/` 只命中 `Engine/WfNodeExecutionDispatcher.cs:39` 的类定义)。`EnterNodeOp.cs:68-70` 对 `WfNodeType.Webhook` 仍抛 48008。**「装了包就能跑自动节点」本轮依旧零证据**,归 D4 建议的 Task 8b / Task 10。
- **R2｜`MaxAttempts` 仍无生产写入方**(Task 6/7 的 R10 原样延续)。建 execution 行的人还是不存在,所有测试里的 `MaxAttempts` 靠手填。因此「重试 3 次后落 `Failed`」这条端到端行为**本轮零覆盖**(引擎侧由 Task 7 的既有测试覆盖,Webhook 侧不重复)。
- **R3｜`webhookOnFailure = manual` 不覆盖「重试预算耗尽」。** 超时/5xx 重试到预算耗尽时,`WorkflowEngine.ResolveExecutionOutcome` 的 `budgetExhausted` 分支直接判 `Failed`(`WorkflowEngine.cs:1417-1423`),handler 无从参与。于是同一节点上「404 → 人工任务」而「超时 ×3 → 静默 `Failed`」。**这是已知的不对称,不是遗漏**;修它要动引擎核心路径(禁碰),挂给 Task 10 定夺。
- **R4｜「Webhook 覆盖四条结果路径」是在 handler 层证明的,不是端到端。** 四条路径各有测试(W-S1 / W-M1 / W-X1 / W-X4),但**只有 `Succeeded` 一条**(W-TX)真的走完了 dispatcher → 引擎 → 落库。其余三条从 handler 结果到 `wf_node_execution.Status` 的那一段,靠的是 Task 7 已有的 N1–N7 + T1–T13(它们用 `FakeNodeHandler` 走的是**同一段结果无关的代码**)。**这是刻意的**——用 Webhook 把 Task 7 的路径重放一遍是 4 条慢测试换 0 条新信息。**如果 Task 10 认为 DONE-CONDITION 要求逐字的端到端四条,那要在 Task 10 补,本 plan 如实记账而不是偷偷算数。**
- **R5｜未预料异常不被捕获,生产上有活锁风险。** D2 表 B 末行的定案(不写 `catch (Exception)`)在**今天**是安全的——生产侧无 worker,没人会去重领那行。等 Task 8b 建了 worker,「handler 抛未知异常 → 行停 `Running` 持租约 → 重领 → 再抛」就会变成真实的无限活锁(形状见 `WfNodeExecutionDispatcher.cs:24-28`)。**建 worker 那一轮必须重新定这条**。
- **R6｜SSRF 围栏只在 SQLite 本机腿、只用字面 IP 验过。** W-C3 用的是字面 `169.254.169.254`,走的是 `ValidateUrl` 的静态分支(`JobHttpFence.cs:25`)。**DNS rebinding 那条真正的防线(`ConnectCallback` 里的解析后复检,`JobHttpFence.cs:113-131`)本轮零覆盖**——它需要一个能被控制解析结果的 DNS,测试环境里造不出来。既有的 Jobs 测试是否覆盖了它,本 plan 未核。
- **R7｜真实网络行为零覆盖。** 假 `HttpMessageHandler` 之后的一切——TLS 协商、连接池复用、HTTP/2、分块传输、服务端提前关连接、代理环境变量——本轮全部测不到。这是「不引 WireMock、不真联网」的必然代价,与 `GitHubWeChatAuthProviderTests` 的射程完全同款。
- **R8｜前端零改动。** 新增的 4 个配置项**在设计器里没有任何 UI**,消费者只能靠直接编辑 `ModelJson`(或后台接口)配置。设计器表单归 **M3a-2**(禁区)。本 Task 只保证 `schema.d.ts` 里有这些字段的**类型**。
- **R9｜四库未验。** 本 Task 零 DDL、零新列,理论上无方言面;但 W-TX 只在本机 SQLite 腿跑过。**不新增四库用例**——那是 Task 9。
- **R10｜Webhook 的请求体是一份新的对外契约,但没有版本号。** D5 定的字段集会被消费者的接收端依赖,而 `WfNodeExecutionResult`/outbox 那套有 `PayloadVersion` 的机制**没有**延伸到这里。本轮不建版本字段(YAGNI,首发无存量消费者),但 **Task 10 回写文档时必须把这份字段清单写进 `workflow-database-design-review-2026-08-24.md`**,否则它就是一份没人记得的隐式契约。
- **R11｜`OutputJson` 里放的是响应体原文(capped),而字段名叫 `Json`。** 没有任何代码解析它(只被 SHA-256 成 `attempt.OutputHash`,`WfNodeExecutionAttemptStore.cs`),所以非 JSON 的响应体不会炸;但字段名会误导后来人。不改 SPI(禁碰),记账。

---

### §7 与台账语义契约的对齐检查

> 逐条列出本 Task **触及**的契约行。**结论:一条都不推翻;需要新增/填实的只有点名留白的那一行(+ 一条派生记录)。**

| 契约行 | 本 Task 的关系 | 判定 |
|---|---|---|
| **Webhook 超时/重试分类** —— 原文「待 Task 8 plan 定案」 | **由 D2 的表 A + 表 B 整块填实。** 协调者转写时把这两张表(含 `Retry-After` 读法与 `ManualFallback` 的开关条款)搬进契约行 | **★ 填实(本 Task 的头号交付)**,无旧定案被推翻 |
| **取消语义(Task 2 定案)** —— handler 抛 OCE = 「这次没跑完,应可被重新领取」,**不等于 `TerminalFailure`**;dispatcher 不得把它归进任何结果分支 | **遵守且加强**:D2 表 B 第 1 行明写「外部 ct 取消 ⇒ 原样抛出,不产生任何结果」;第 2 行把**自己的超时**(同一异常类型!)转成 `RetryableFailure`,并在 §5 陷阱 1 写死判据。W-X1/X2/X3 三条从三个方向钉住这条边界 | **遵守**。注:这一行原本只约束 dispatcher,现在多了一条 handler 侧的落地细则(超时 ≠ 取消),**建议契约行末尾追加一句**指向 D2 表 B,不改原文 |
| **handler 键与分发(Task 2 定案)** —— `TryAddEnumerable` + `NodeType` 枚举匹配;「**第一条 DI 注册线由 Task 8 加**」 | **D6 逐字执行**:`TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowNodeHandler, WebhookNodeHandler>(工厂))`。W-DI1/W-DI2 守门 | **遵守**,契约点名的动作已完成 |
| **Handler 结果枚举(Task 2 定案)** —— 四个静态工厂;handler **不得**推进 token / 写任务状态 / 自开事务;不设 `Cancelled` 成员 | 本 Task 只**调用**四个工厂,不加枚举成员、不改 `WfNodeExecutionResult`。handler 构造签名里根本没有 `ISqlSugarClient`(§5 陷阱 8) | **遵守**(未触及类型) |
| **`WfNodeExecutionContext` 形状(Task 2 定案)** —— 14 个 `init` 字段;「实现必须对烂 JSON 免疫」;「节点配置传既有 `WfNodeProps`,**Webhook 配置位已在里面**」 | **形状零改动。** 「Webhook 配置位已在里面」实读为**只有 `WebhookUrl` 一个**(`Schema/WfNode.cs:83`),不足以承载 method/headers/timeout —— D5 在 `WfNodeProps` 上**纯追加** 4 个可空属性,`WfNodeExecutionContext` 本身一个字段都不加。「对烂 JSON 免疫」在本 Task 表现为:handler **不解析 `VariablesJson`**(压根不读它),以及所有配置读取都走可空 + 默认值 + 钳制 | **遵守**。**需要新增一条派生定案**:「Webhook 配置位」从 1 个字段扩到 5 个,且**这会改 OpenAPI schema**(见下一行) |
| **事务边界** —— 短事务领取 → 事务外调 handler → 短事务落结果,**不得**让远程调用发生在数据库事务内 | **本 Task 是这条契约的第一次真实兑现**(此前只有 `FakeNodeHandler`,不发任何网络包)。D8 的 W-TX 把探针放到假 transport 的 `SendAsync` 内部 | **遵守**,并首次有了「真 HTTP 调用点」层面的证据 |
| **Task 3 交付边界** —— `EnterNodeOp` 对 `WfNodeType.Webhook` 仍走 `default:` 抛 48008,「接上 dispatcher 归 Task 6」 | Task 6/7 都没接,**Task 8 经 D4 也不接**,如实记进 R1。**这不是推翻**——那句话说的是「归 Task 6」,而 Task 6 的 plan 已把它推到 Task 8;本 plan 把它进一步推到 D4 建议的 Task 8b,**带理由、带形状建议,不是静默拖延** | **遵守**,但**需要协调者裁决**:是接受 D4 的判断并新开 Task 8b,还是把 worker 塞进本 Task |
| **lease/fence(Task 3 定案)** / **execution 状态机(Task 3 定案)** / **`AttemptCount` 三处口径** | 本 Task 零改动、零新写入点。W-TX 走的是既有路径 | **遵守**(未触及) |
| **attempt 记录 / 不回写 execution / 不存输出正文(Task 4 定案)** | 本 Task 不改任何 Store。**「不存输出正文,存 hash + 截断摘要」这条被 D5/W-Q3 主动加强**:handler 侧就 capped read + `Truncate`,不把 100KB 正文交给写入方去截 | **遵守**,方向一致 |
| **实体如何进 CodeFirst 建表(Task 4 记录)** —— 整程序集扫描,不存在实体类型登记表 | 本 Task **零新实体、零列变更、零 DDL** | **遵守**(未触及) |
| **outbox** —— 结果提交后可靠触发;与 execution 结果同短事务 | 本 Task 不改 outbox。**注意**:`webhookOnFailure = manual` 走 `ManualFallback`,而 `ManualFallback` 是 `IsTerminal = true`(`WorkflowEngine.cs:1428-1431`),**会入队 outbox** —— 这是既有行为,本 plan 只是记明,不改 | **遵守** |
| **与人工任务的关系** —— 「`ManualFallback` 时如何创建人工 `wf_task`——待 Task 6 plan 定案」 | Task 6 已定(`WfManualFallbackOp`)。本 Task 只是**第一次让一个真实 handler 有可能返回 `ManualFallback`**(经 D3 的配置开关),不碰 `WfManualFallbackOp` 一行 | **遵守**,该行已由 Task 6 结账 |
| **范围外** —— 不建 AI Decision;不建并行网关;不新增审批动词;不 port React 工作流页;不抽 web/web-react 共享层 | 本 Task 只改 3 个 `backend/src` 文件 + 2 个**生成的** `schema.d.ts`,新增 1 个产品文件 + 1 个测试文件。**两个前端的 `schema.d.ts` 各自独立重新生成,零共享层**(它们内容相同是因为同一个 OpenAPI,不是因为共享代码) | **遵守** |

#### 需要协调者写进 `## 语义契约` 的**新增**内容(共 2 条)

1. **「Webhook 超时/重试分类」行** ← **D2 的表 A + 表 B 全文**(含 `Retry-After` 读法、`ManualFallback` 由 `props.webhookOnFailure` 开关、以及「已知不精确:围栏拒连表现为 `HttpRequestException` 故被判 `RetryableFailure`」这条自曝)。
2. **新增一行「Webhook 配置 schema 与外呼请求体(Task 8 定案)」**:`WfNodeProps` 平铺追加 `WebhookMethod`/`WebhookHeaders`/`WebhookTimeoutSeconds`/`WebhookOnFailure`(全可空,存量 `ModelJson` 逐字兼容);**这四个字段进 OpenAPI,加/改它们必跑双模板 `gen:api` + SHA256 一致**;超时钳 `[1,120]`s 且再取 `min(…, DeadlineAtUtc - now)`;header 拒 `Host`/`Content-Length` 且过 `JobHttpFence.ValidateHeader`;SSRF 围栏与 `HttpClient` **复用内核 `JobHttpClient`/`JobHttpFence`,配置源是 `TenonAdmin:Jobs:Http`,本包不引 `Microsoft.Extensions.Http`**;外呼请求体是引擎生成的固定字段集且**刻意不含 `VariablesJson`**(理由同 outbox D6),`GET`/`HEAD` 不带体。

#### 明确**不推翻**的两条(有人可能想动,先按住)

- **不改 `WfNodeExecutionDispatcher` 的租约处置**(§5 陷阱 2)。契约里「OCE 要让 lease 过期/释放」= 让它**自然过期**,已由 Task 7 N4 的两条租约断言钉死。
- **不改 `WorkflowEngine.ResolveRetryDelay` 的 `(0, 24h]` 钳制**。handler 只负责把 `Retry-After` 原样递上去,上下界由引擎唯一一处钳。两处钳 = 两处会各自漂移。

## Tasks

> 任务顺序 = 依赖顺序。编号稳定;`## Log` 引用任务号。

- [x] **1. `NodeVisitId` 贯穿 + `wf_history` 补字段**:`WfToken`/`WfTask`/`WfHisTask`/`WfHistory`/`WfCc` 加 `NodeVisitId`(每次进新节点生成,停留期间不变,与 `EnterNodeOp` 的 token 级 CAS 同一事务写入);`wf_history` 补 `TokenId`/`Sequence`(实例内单调递增,并发写入方式待 plan 定案)/`ActorType`/`ActorUserId`/`PayloadVersion`(`RequestId` 已在 M2c 做完,不重做)。这是后续所有 execution 相关表「稳定身份」的地基,必须先做。
- [x] **2. `IWorkflowNodeHandler` SPI + Context/Result 类型**:定义最小 Interface(`ExecuteAsync(WfNodeExecutionContext, CancellationToken) -> WfNodeExecutionResult`);`WfNodeExecutionContext` 只含不可变快照(tenant/org、定义版本、实例、token、节点配置、变量/证据快照、`ExecutionKey`、attempt、deadline),不泄漏 SqlSugar 实体/DB session;`WfNodeExecutionResult` 是 `Succeeded`/`RetryableFailure`/`ManualFallback`/`TerminalFailure` 的显式判别联合或枚举+payload。附一个 `FakeNodeHandler` 参考实现(可配置返回哪种结果,供后续 Task 当测试替身)。**本 Task 不接入引擎**,纯类型/接口定义。
- [x] **3. `wf_node_execution` 实体 + `ExecutionKey` 唯一约束 + lease/fence CAS 领取**:新增实体与表(字段参照数据库评审 §六 6.1),`ExecutionKey` 唯一索引;短事务领取逻辑(CAS 更新 lease owner/expiry + fence token 递增),仿 M2c `WfOperationReceipt`/`WfInstance.Version` 的先例。本 Task 交付「能领取、能占位」,不接调度器。
- [x] **4. `wf_node_execution_attempt` 实体 + append-only 记录**:新增实体与表(字段参照 §六 6.2),写入路径只增不改不删;至少一条测试证明「重试不覆盖旧 attempt,而是新增一行」。
- [x] **5. `wf_outbox` 实体 + 可靠派发骨架**:新增实体与表(字段参照 §六 6.3);与 execution 结果同一短事务提交;本 Task 交付「写得进去、状态可查询」,实际派发消费逻辑视 Task 6 需要决定是否本 Task 一并做还是独立。
- [x] **6. Execution dispatcher(领取 → 调 handler → 落结果)**:整合 Task 2–5——短事务领取 execution(lease/fence)→ 事务外调用 `IWorkflowNodeHandler` → 按结果短事务落 attempt + 推进 token(`Succeeded`)/ 安排重试(`RetryableFailure`,写 `NextRetryAt`)/ 建人工任务回退(`ManualFallback`,决定是否复用 `EnterNodeOp.CreateTaskAsync`)/ 终止(`TerminalFailure`)+ outbox。覆盖「远程调用不在事务内」「同 `ExecutionKey` 只推进一次」两条核心不变量的测试。
- [x] **7. Fake Handler 全链路 + 崩溃恢复**:用 `FakeNodeHandler` 把 Task 6 的 dispatcher 端到端打通,覆盖四种结果路径各至少一条测试;崩溃恢复测试(lease 过期后可被重新领取,重新领取后不会对已经成功推进的 execution 重复推进——用直接操纵 DB 时间戳模拟过期,同 M2c `WfTimeoutTests` 的先例手法,不建 `FakeTimeProvider`)。
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

### Task 5 review(Round 19,Opus 自审 + 11 变异点)

> 方法:变异测试,每点走完「Edit → grep 确认落盘 → 跑全量 → 单文件 `git checkout --` 复原 → `git diff --stat` 空」。协调者在 Round 19 的三次唤醒中**分别观察到 `WfOutbox.cs`、`WfOutboxStore.cs` 处于变异态、最终工作树干净**,与报告自述吻合,变异确实发生过且已逐个复原。

**9 个 Plan 预期变异全部按预期转红**:M1 丢 type 段 → T2+T7 红;M2 丢 execution 段 → T7+T2 红;M3 `AvailableAtUtc` 加 1 分钟 → T1 红;M4 初始 `Status` 改 `Dispatching` → T1 红;M5 拿掉 `IsUnique` → **仅 T4 红(T3 确实抓不到,证实 Plan「T4 必须独立存在」不是冗余)**;M6 删 ensure 分支 → T3 红;M7 漏赋 `PayloadJson` → T5+T3 红;M9 store 自开 `UseTranAsync` → T6 红**且红在「表内残留 1 行」那条断言上而非靠异常侥幸**。另 M1b(自设:key 算对存对、只把 ensure 查询改按 `ExecutionId` 命中,精确复刻 D2「第二种消息被静默吞掉」)→ T2 的 (b) 腿独立转红,**D2 最重要那条论据有真守门**。

**M8 实测确认 R5 的射程判断为真**:`CodeFirst_BigString` 改成 `Length = 512` 后本机 **320/320 照绿**——SQLite 不强制列长度,32KB 正文塞进 512 列照样往返。子代理另查实 `.github/workflows/backend-ci.yml:147` 的 sqlserver push/PR `TEST_FILTER` 里 Wf 相关只有 `WfPersistenceContractTests`,`WfOutboxTests` 确实不在子集内;而该文件已有同型的 `Result_json_round_trips_chinese_and_long_payloads`(`WfOperationReceipt.ResultJson`)可抄 → **挂账 Task 9 的去处成立,未为它改 CI、未硬凑测试**。

**Plan §5 的 13 条陷阱一条未踩**(逐条 grep 有据);**§6 的 R1–R7 如实、无偷偷补上也无夸大**;实体/store/枚举的类注释把 Plan 要求的每条理由都写到位(尤其「`AttemptCount` 即 fence,别再造 `Fence` 列」与「不写 `DefaultValue` 的理由」),**不会导致下一轮去造重复机制**。7 条测试**逐条被证明有鉴别力,无一空转**,零写入点的列没有被编造假覆盖。

- [x] **P1-1｜`NormalizeMessageType` 的三条校验(`Trim()`/拒空白/拒含 `':'`)零守门测试** —— 证据是 **M11**:把整个函数体退化成 `return messageType;`,grep 确认落盘后全量 **320/320 照绿**,**整段领域校验可以原地蒸发而无人发觉**。这是 Task 4 那类「断言缺失」的同款形状且更彻底(Task 4 至少还有别的测试路过那段代码,这里是零测试路过)。**不能豁免的三条理由**:①它是 **trust boundary**——D2 定案 `MessageType` 用 `string` 而非枚举**就是为了让消费者发自己的类型**,该形参将来由内核外代码提供;②它保护的是 `MessageKey` 的结构不变量(`':'` 是分隔符,放进含 `':'`/空白的类型就破坏 D2 论据 4「定宽 64 hex 前缀 + 无歧义分隔符」,而那是消费方去重的地基);③Plan §6 的 R1–R7 **没有把它列为射程限制**,即它不是「诚实测不到」而是**漏了**。**修法**:加 2 条测试(别为覆盖率凑 3 条)——`Message_type_containing_the_key_separator_is_rejected`(`Assert.ThrowsAsync<ArgumentException>` **并顺带断言表内该 `ExecutionId` 下 0 行**,证明抛在写库之前,同 T4「两者都断」的纪律);`Message_type_is_trimmed_before_it_joins_the_key`(传前后带空格的类型,**从库读回**后断言 `MessageKey` 无多余空格)。协调者独立核实:测试文件 `ThrowsAsync|ArgumentException` **0 命中**,属实。
- [x] **P2-1｜`MessageKey` 从未被从库读回验证(Plan §4 禁写清单第 2 条的字面违反)** —— 这正是协调者在 Round 18 标记、要求 review「设计能区分『算对但落库错』的变异」来判定的疑点,**判定:是真缺口**。证据是 **M10**(专门设计):让 store 把 key **算对、返回对**,只把**插库那一行**带上 `MUTANT-` 前缀 → **T2/T7 全绿**,尽管库里每一行的 key 都是错的;只有 T3 红,且红的是 SQLite `UNIQUE constraint failed` 方言异常,**与「key 存错了」毫无关系**,排查要绕一大圈。逐列盘查后 **`MessageKey` 是全表唯一一个从未被读回验证过的列**,而它恰恰是本表主契约(消费方去重就靠它)。**不评 P1 的理由**:key 的**构成**已被 M1/M2/M1b 三个方向证明有守门,落库错这类退化虽逃过 key 断言但仍有一条测试会红,不是完全盲区。**修法**:T2/T7 在现有断言之后各加一次读回(按 `Id` 查回该行),期望值仍**测试里手写字面拼接、不许调 store**;**保留**现有对返回值的断言(它证明「返回给 Task 6 调用方的对象是对的」,是另一件有价值的事),只补落库那一半。协调者独立核实:`MessageKey` 的断言全在内存对象上(`:53` `first`、`:173-175` `rowA`/`rowB`),唯一碰该列的库查询是 `:111` 的 `CountAsync`(数行数,非值读回),属实。
- [x] **P2-2｜对外契约常量 `MessageTypeNodeExecutionCompleted` 的字面值无快照钉死** —— 该常量是**发给进程外消费方的线上契约**(D2 明文),改它等于破坏所有消费者的路由与去重;但 T1 的断言两侧都是同一个常量(`:36`),把常量值改成任何别的串测试照绿。同型的对外契约 `ExecutionKey` 在本仓**是有**快照测试钉死的(`WfExecutionKeyTests`,语义契约写着「那条红了是撤回改动,不是改期望值」),这里缺同款保护。**修法**:一行把字面值写死的 `Assert.Equal` 加进 T1 或单列一条,注释写明「已发包的线上契约,红了是撤回改动」。**不建议**为此新建测试类。
- [ ] **P3-1(可选,且本轮明确不做)｜`LastError` 的 512 是裸字面量,无常量托底** —— attempt 的先例是列宽与 C# 侧截断共用 `WfNodeExecutionAttemptStore.SummaryMaxLength` 一个 token;`WfOutbox.LastError` 用裸 `Length = 512`,而注释要求「写入方必须 C# 侧截断到 512」。**本轮零写入点,现在加常量是为不存在的调用方服务(YAGNI),不改**;但**消费者任务落 `LastError` 写入点时必须同步补 `WfOutboxStore.LastErrorMaxLength` 并把列宽换成它**,否则 512 会在两处各写一遍。已挂账,见下方射程限制。
- [ ] **P3-2(可选,不做)｜`idx_wf_outbox_scan` 无测试** —— 纯性能索引,无可观测行为,写不出有鉴别力的测试(同 `idx_wf_node_exec_scan`,本仓无为扫描索引写测试的先例)。**如实记为射程限制,不硬凑。**

**Task 5 新增射程限制(并入台账,不许后续拿来当「测过了」)**:①`EnqueueAsync` 的参数守卫(`ArgumentNullException.ThrowIfNull` / `ThrowIfCancellationRequested`)零覆盖,与全仓姿势一致,**不补**——但**必须与 P1-1 区分**:那两条是框架级一行守卫,`NormalizeMessageType` 是本 Task 自己写的领域校验,不能一并豁免;②`idx_wf_outbox_scan` 无行为可观测;③`MessageKey` 落库值本轮无读回证据(P2-1 修完即消除);④R5 未变,`PayloadJson` 的 BigString 四库往返仍只有 sqlite 腿证据,**挂账 Task 9** 并入 `WfPersistenceContractTests`;⑤**挂账消费者任务**:落 `LastError` 写入点时补 `LastErrorMaxLength` 常量 + 一条「600 字错误文本 → 落库 512」的测试(形状照 attempt #5)。

### Task 6 review(Round 23,Opus 自审 + 15 变异 + 2 专项探针)

> 手法:每次变异走完「Edit → grep 确认落盘 → 跑全套 334 → 单文件 `git checkout --` 复原 → `git diff --stat` 空」。协调者在 Round 23 的唤醒中**观察到 `WorkflowEngine.cs` 处于变异态、最终工作树干净**,证据链完整。**注意行号**:review 报告里的 `:1357`/`:1374` 是它自己加了变异注释行造成的 +9 偏移,**干净树上两处 CAS 实为 `:1348`(重试分支)与 `:1365`(终态分支)**。

**产品代码本身未发现功能性错误**:13 个必做变异里 **11 个如期转红**——A1 挪 handler 进事务 → T1 红(**DONE-CONDITION「远程调用不在事务内」有直接守门**);A2/A3 终态分支两条谓词各自删 → T2/T3 红;C1 空 provider 早返回换成 `EnterApprovalAsync` → T8 红(`Expected: Running / Actual: Approved`,**精确命中「自动节点执行失败后被静默放行」这条最危险 bug 形状**);C2 换 `EnterNodeOp` → T7/T8 红(`NodeVisitId` 被重新生成);C3 不写 `NextRetryAtUtc` → T4/T6 红;C4 预算 `>=` 改 `>` → T5 红;C5 删实例状态前置判定 → T9 红;C6b `AttemptNo + 1` → T12 + 2 条 AttemptTests 红。**Plan §5 的 15 条陷阱一条未踩**(逐条核过最易踩的五条:`AttemptNo` 三处口径一致、CAS 在 `AppendAsync` 之前、`SetColumns` 全部先落局部变量、`ClearFilter<IOrgScoped>` 该带的带该不带的不带、`SpecifyKind` 写对了、dispatcher **零 `catch`**)。**R1「生产侧零调用点」实测属实**(`grep WfNodeExecutionDispatcher backend/src/` 只命中类定义一行、`WorkflowSetup.cs` 零命中、`EnterNodeOp.cs:68-70` 的 `default:` 仍抛 48008、十件套仍 10 条且不在 diff 里)。**`Truncate` 复用无第二份截断**(全仓无第二个 512 字面值),Task 5 P3-1 的教训被兑现。

**但测试层有两个真缺口 + 一组系统性断言缺失**,全是 checklist 看不出、只有变异能抓的形状——与 Task 4/5 review 抓到的是同一个病。

- [x] **P1-1｜`RetryScheduled` 分支的 fence CAS 两条谓词【都】零测试覆盖** —— **这正是协调者在 Round 22 点名担心并要求「对两个分支各做一次变异」的那件事,实测坐实**。证据:**A4**(删重试分支的 `e.Fence == fence`)→ `失败 0 / 通过 334` **全绿**;**A5**(删重试分支的 `e.Status == Running`)→ **同样全绿**。两次 grep 均确认落盘。而终态分支的同两条谓词各有守门(A2/A3 都红)。**为什么是 P1 而非 P2**:`RetryScheduled` 是**唯一一条「终态之外的回写」**,它把行推回**可再领取**状态并清空 `LeaseOwner`/`LeaseExpiresAtUtc`。缺 fence 的后果不是多写一行垃圾,而是:老 owner 租约过期 → 新 worker 已 claim(fence=2、`Running`、持租约)→ **老 owner 迟到的 `RetryableFailure` 把行打回 `RetryScheduled` 并清掉新 worker 的租约** → 第三个 worker 立刻能领走同一个 execution → **两个 worker 并发跑同一个自动节点**。终态分支上那两道后备防线在这条边上**都不起作用**(老 owner 与新 worker 的 `AttemptCount` 不同 → attempt 不撞唯一索引;实例仍 `Running` → 前置判定放行)。**同时这也是协调者留的「Plan 字面偏离」问题的裁定**:review 判定**算偏离且这次偏离产生了真实后果**——「只许一处 `Updateable`」这条规则的真正用处**正是让守门测试无法只覆盖一半**,拆成两处之后果然只覆盖了一半;**但不要求合并**(合并要在重试分支显式写 `CompletedTimeUtc = null`、终态分支显式写 `NextRetryAtUtc = null`,更易写错),**必须补测试**。修法:补两条,形状照抄 T2/T3 只把结果换成 `RetryableFailure` + `MaxAttempts = 3`——①重试分支 stale fence:断抛 48004 且 execution 仍 `Running`、`Fence == 2`、**`LeaseOwner` 仍是 worker-b(这一条最关键,是「租约没被老 owner 清掉」的直接证据)**、`NextRetryAtUtc` 仍 null、attempt 0 行、outbox 0 行;②重试分支同 fence 重放:第二次抛 48004,attempt 仍 1 行、`NextRetryAtUtc` 不变。另把 Plan §2/D5 的「唯一一处」措辞改成「唯一一个方法内的两个分支,且**两个分支都必须各有 stale-fence 与 replay 测试**」。协调者独立核实:干净树上两处 CAS 确在 `:1348`/`:1365`,属实。
- [x] **P1-2｜`WfManualFallbackOp` 的【第二条】自动放行出口(解析出 0 人)零测试覆盖** —— D4/陷阱 13 要求**两条**早返回都绝不能落到 `EnterApprovalAsync`/`ApplyNobodyAsync` 的 `autoPass`;T8 只覆盖了第一条(provider 空白)。证据:G bundle 的 `G-zeroUsers` 项(把 `users.Count == 0` 的早返回换成 `await EnterApprovalAsync(ctx, ct)`)→ **334 全绿**。**为什么是 P1**:与 C1 命中的是**同一个** hazard——台账与 Plan 都称之为「本里程碑最危险的静默 bug」(自动节点执行失败后被自动放行 → 实例被 `Approved`);而「配了 provider 但解析出 0 人」在生产上**比「压根没配 provider」更常见**(审批人被停用/删除、主管链断裂、`userIds` 指向已不存在的用户),恰恰是没守门的那一半。修法:给 T8 加一个姊妹用例(约 10 行,复用现有脚手架):传**存在的 provider + 指向不存在用户 Id 的 params**(如 `userIds = [999999999]`)让 resolver 正常返回 0 人;断言与 T8 相同(`Status == ManualFallback`、`wf_task` 不增、token `NodeId` 不变、**instance 仍 `Running` 而非 `Approved`**)。协调者独立核实:该文件恰有两条早返回(`:34` provider 空白、`:49` 解析 0 人),属实。
- [x] **P2-1｜本轮新填四列里 `HandlerType`/`Summary`/`CompletedTimeUtc`(值)从未从库读回验证** —— 违反 Plan §4 禁写清单第 1、2 条。实测:`HandlerType` 只在 T2/T3 作为**入参**出现过、**零次读回**(变异成永远写 null → 全绿);`Summary` 在测试文件里 `.Summary` **零命中**(变异成 null → 全绿),**连带后果是 Plan 唯一许可的那处改动(`Truncate` 改 public 供 `Summary` 复用)整条链路零测试**;`CompletedTimeUtc` 只有 `Assert.NotNull`(值偏 10 年照绿)——**正是禁写清单第 1 条明文禁止的形状**。四列里 `ErrorCode` 是唯一做对的(T5 真读回真断值)。证据:G bundle 的 `G-handlertype`/`G-summary`/`G-completed` → 334 全绿。修法:T5 追加三行断值(`HandlerType` 断类型全名、`Summary` 断具体串、`CompletedTimeUtc` 用容差 `Assert.Equal` **而非 `NotNull`**),再单加一条**截断**用例(handler 返回 600 字 summary → 断落库 `Length == 512`,这同时是 `Truncate` 复用的唯一证据、也是「SqlServer/PG 直接抛、SQLite 照单全收」那类四库差异的本地护栏)。协调者独立核实:`reloaded.HandlerType`、`.Summary` 在测试文件里均 **0 命中**,属实。
- [x] **P2-2｜outbox 行只数了行数,`MessageKey`/`MessageType`/`PayloadJson`/`AvailableAtUtc` 全部零断言** —— 五条测试都只写 `Assert.Equal(1, ...CountAsync())`,而禁写清单第 2 条**明文点名** outbox 的 `MessageKey`。实测把 `messageType` 换成字面量 `"probe.type"`(于是 `MessageKey` 整个变了)、payload 换成 `"{}"`、`AvailableAtUtc` 推到 10 年前 → **一条都没红**。要紧在于 `MessageKey = {ExecutionKey}:{MessageType}` 是**投给进程外消费方**的去重键与路由键,payload 字段集是消费者契约,现在只有「有一行」的证据。修法:把计数断言换成读回整行,断 `MessageType` 字面值、`MessageKey` 手写拼接、payload 解析后断 `status`/`executionId`,并**断 `TryGetProperty("outputJson", out _) == false`**——「`OutputJson` 正文绝不进 payload」是 D6 花一整段论证的 PII/密钥决策,**目前零测试**。
- [x] **P2-3｜`WfNodeExecutionContext` 的 14 字段投影整体零断言;`Attempt` 口径与 `SpecifyKind` 转换双双存活** —— `FakeNodeHandler.LastContext` 的 XML 注释白纸黑字写着「供 **Task 6** 断言快照投影正确」,而 Task 6 的 12 条测试**一次都没读过它**。实测把 `OrgId`/`StarterUserId`/`BusinessKey`/`NodeProps`/`VariablesJson` 全改假值、`Attempt` 改 `+ 1`、`DeadlineAtUtc` 去掉 `SpecifyKind(Utc)` → **全绿**。**两条特别硬**:①**`Attempt + 1` 存活**,直接架空语义契约「`AttemptCount` 三处口径必须同一个数」的**第三处**——T12 只验到「领取读回值」与「attempt 行 `AttemptNo`」两处,**handler 看见的那个数完全没验**,而契约原文管这个叫「最典型的静默 bug」;②**`SpecifyKind` 去掉后存活**,而这正是 dispatcher 类注释与 Plan 陷阱 6 花整段警告的 bug(「非 UTC 机器上悄悄错 8 小时」)——本机就是 UTC+8,变异后偏移量从 `+00:00` 变 `+08:00`、绝对时刻偏 8 小时,**无人察觉**。修法:T1 已有 `OnExecute` 钩子,在其后加一段对 `handler.LastContext` 的断言,其中 **`Assert.Equal(TimeSpan.Zero, ctx.DeadlineAtUtc.Offset)` 是关键**(任何时区机器上都为真,去掉 `SpecifyKind` 后在 UTC+8 开发机上立刻红),并断 `ctx.Attempt == 1`。协调者独立核实:`LastContext` 在测试文件里 **0 命中**,属实。
- [x] **P2-4｜`HandlerType` 写入无截断,列宽 256** —— 与 `Summary` 同款的四库不一致风险。`HandlerType = handler?.GetType().FullName` 直接写进 `[SugarColumn(Length = 256)]` 且**无截断**;Plan 陷阱 9 对 `Summary` 的论证逐字适用(超长在 SqlServer/PG **直接抛**、MySQL 非严格模式**静默截断**、SQLite 照单全收 → 本机永远绿、CI 三腿红)。类型全名由**消费者代码**提供,泛型 handler 很容易越过 256。修法:一行——按列宽截断(`cmd.HandlerType is { Length: > 256 } h ? h[..256] : cmd.HandlerType`),或给 `Truncate` 加 `int max` 重载。**不建议新建第二个截断 helper**(那正是 Task 5 P3-1 的教训)。
- [x] **P3-1(可选,便宜)｜T3 的断言顺序让禁写清单第 4 条「够不到」** —— 见下方 B1 判定。修法:改成先 `Record.ExceptionAsync` 捕获 → **先断副作用行数** → 最后断异常类型与 48004。这样删 `Status` 谓词时既红在错误面、也保住副作用断言的可执行性,失败消息不再是误导人的 `UNIQUE constraint failed`。
- [ ] **P3-2(可选)｜T6 两段串在一个 `[Fact]` 里,前段失败掩盖后段** —— 正是它把 exec 引到了「上界无鉴别力」的错误结论。拆成两条 `[Fact]` 或用 `Assert.Multiple`。
- [ ] **P3-3(挂账,不在本轮)｜`WfNodeExecution.cs` 四条 XML 注释已失真** —— `HandlerType`/`CompletedTimeUtc`/`ErrorCode`/`Summary` 的注释仍写「建表期预留,**本轮零写入点**」,类注释也仍把 8 列一并称作零写入点,而本轮已给其中 4 列接上写入点。**exec 无过错**(Plan §2「明确不改」把该文件列进了禁碰清单),但注释现在**主动说假话**。建议 Task 7 或收口轮一次性改掉(纯注释零行为)。
- [x] **P3-4(可选,便宜)｜T10 用常量而非字面值钉错误码** —— `Assert.Equal(WorkflowErrorCode.NodeTypeUnsupported, attempt.ErrorCode)`,产品代码写的也是同一个常量,**钉不住 48008 这个上线数字**(常量被改测试跟着走)。禁写清单第 10 条要的就是字面值(T2/T3 用的就是字面 `48004`)。
- [ ] **P3-5(挂账,只补文档)｜outbox payload 的空值键会整个消失** —— `WfModelJson.Options` 带 `DefaultIgnoreCondition = WhenWritingNull`,故 `Succeeded` 路径 payload 里 `errorCode`/`summary`/`outputHash` 三个键**不存在**而非 `null`,而 D6 的字段清单读起来像是恒定存在。**建议只补文档**,为一个 payload 新起一份 options 不值。
- [ ] **P3-6(挂账)｜T12 的 `EndedAtUtc > StartedAtUtc` 只靠时钟分辨率成立** —— 两次相邻 `GetUtcNow()` 中间只夹一个什么都不做的 `FakeNodeHandler`;产品代码没错、禁写清单第 3 条的意图已满足,但在低分辨率时钟平台上有 flaky 风险。

**B1 判定(协调者从 exec 报告挑出的疑点一)—— exec 的现象属实,但结论要修正。** review 设计了**两个探针**而非凭阅读下结论:**probe1**(保持 A3,把 T3 的 `ThrowsAsync<AdminException>` 放宽成 `ThrowsAnyAsync<Exception>`)→ **334 全绿** → 证明 A3 下 T3 唯一的红点就是异常类型不匹配那一行,**后面几条副作用断言从未执行**;**probe2**(在 A3 基础上再把 `AppendAsync` 的 `AttemptNo` 随机化以拆掉唯一索引这道后备防线,并让 T3 吞掉异常)→ **失败 4/通过 330**,T3 红在 `Assert.Equal(1, attempt 行数)` 上(`Expected 1 / Actual 2`)→ 证明**副作用断言不是虚设,对「真的写了第二行」有实打实的鉴别力**。综合判定:①「因为错误的原因而红」这个担心**成立**;②**但不变量本身没破**——「只推进一次」实测有**三道互相独立的防线**(CAS 的 `Status` 谓词 → attempt 表唯一索引整事务回滚 → `ResolveExecutionOutcome` 的实例状态前置判定),删掉第一道后两道仍让重复推进在可观测层面不发生(这也正是 probe1 全绿的原因);③**所以 `Status == Running` 谓词的真实职责是「错误面」而非「不变量」**:有它 → 干净的 48004;没它 → 原始 `SqliteException`(**PG 上更糟:唯一冲突会把事务打成 `25P02` aborted**)。**谓词不能删,但 T3 的证明力比台账措辞窄**——记 P3-1。

**B2 判定(疑点二)—— 上界钳制【有】独立鉴别力,不是缺口,exec 的结论是错的。** 单独变异上界(只删 `&& retryAfter <= MaxRetryAfter`、保留下界)→ **T6 转红**,失败点在**上界段**且消息打出了越界实际值(`NextRetryAtUtc=2036-08-29…越过了 24h 上界`)。**exec 错因可还原**:它做的是**合并变异**(整个方法体退化成 `return result.RetryAfter ?? 默认;`,上下界一起没了),而 T6 的下界段写在同一个 `[Fact]` 里、跑在上界段之前,**先失败就终止了方法,上界段根本没轮到执行**。这不是「上界无人守门」,是**同一条测试内两段串行、前段失败掩盖后段**——掩盖效应本身记为 P3-2,但**不构成 P2 缺口**。

**§6 射程限制漏记两条**(review 判定这两条**不是「诚实测不到」**,而是用现有脚手架就能测的东西,故写成 P1/P2 而非新增射程限制):①`RetryScheduled` 分支 CAS 无任何测试(P1-1);②`WfNodeExecutionContext` 字段投影无任何测试(P2-3)。另 **R4 实际比其自述更窄**——它写「只证明入队一行」,实测连那一行的四个列都没验(P2-2)。


### Task 7 review(Round 27,Opus 自审 + 11 次变异)

**P1:无。** 所有变异都没有暴露「产品有真 bug 而无人守门」的一级问题。7 条新测试(N1–N7)**全部从库读回并经变异证明有鉴别力**,无「断言缺失」型空转;`Assert.NotNull` 钉时间列的病没再犯;对外契约全用字面值;两处产品注释改动(P3-3/P3-5)**逐项核实都是真话**;R1「生产侧零调用点」**未被偷偷解除**;禁碰清单零违反;原有 16 条断言零删改(T6 拆分经 `36a8cc0^` 逐行比对,**方法体与断言一字未改**,只有 XML 注释新写)。

**★ 本轮最重要的产出是一条被推翻的因果 + 一份「防线拓扑」。**

**被推翻的因果(协调者已亲手复验,坐实 review 是对的)**:Round 26 台账 Status (i) 记的「M-N7 变异下重新领取后**没有真的重复调 handler**(被 `ResolveExecutionOutcome` 的实例终态前置判定挡成 `Cancelled`)」——**这是错的**,来自 exec 的自述。review 实测 **`CallCount == 1`**;协调者随后用**一个 review 没用过的形状**独立复验:把 P2-1 建议的重排**当探针先应用**(`Assert.Equal(0, handlerB.CallCount)` 提到 `Assert.Null(statusB)` 之前)再叠加 M-N7,失败消息变成 **`Expected: 0 / Actual: 1`** —— **handler 确实被第二次调用了**。变异态下的真实破坏面:`status: Succeeded→Cancelled`、`fence=2`、`AttemptCount=2`、`attempts=2 行`、`outbox=1`、`call=1`。**这条错误因果之所以没变成台账里的永久错账,唯一原因是协调者当时明确标注「exec 自报、不由协调者定性、留给 review 裁定」** —— 纪律生效的一次实例。

**防线拓扑(P3-4 要求补录,这里正式记进台账,Task 8 接 Webhook 时最该知道的事)**:「已完成的 execution 不会被重复推进」实测有**两道半**防线,射程各不相同:

| # | 防线 | 位置 | 拦住什么 | 守门测试 | 变异证据 |
|---|---|---|---|---|---|
| 1 | 领取谓词 `Status == Running` | `WfNodeExecutionStore.cs:75` | **handler 被重复调用**(唯一拦得住这件事的一道) | **N7**(全仓唯一) | M-N7 → 只 N7 红 |
| 2 | `ResolveExecutionOutcome` 实例/token 终态前置判定 | `WorkflowEngine.cs:1406` | 迟到结果**推进 token/实例**(handler 已经跑过了) | T13 | M-PRE → 只 T13 红 |
| 3 | 回写 CAS 双谓词 `Fence == fence && Status == Running` | `WorkflowEngine.cs:1361/1378` | 老 owner 的**陈旧回写** | T2/T3 + 两条重试分支 CAS | M-N6-STATUS → 只 T3 红 |

**关键限定**:防线 2 与 3 都**只在回写那一段起作用,都拦不住 handler 被重新调用一次**。所以 DONE-CONDITION 那句「不会对已经成功的 execution 重复推进」的**全部重量压在防线 1 这一条谓词上**,而它在 338 条基线时**零守门** —— N7 补的正是这个洞。协调者 Round 26 担心的「第二道防线零守门」**经 M-PRE 实测排除**(只 T13 红,有守门)。

**协调者疑点 (ii) 也被排除**:M-N1-ACTOR(把回写路径的 `ActorType` 从 `Worker` 改成 `System`)→ **只 N1 红**。exec 把 `NodeLeave` 断言收窄到 `NodeId == "node1"` **方向正确且未削掉鉴别力**,收窄后 N1 仍是全仓唯一钉住 `wf_history.ActorType` 的测试。

- [x] **P2-1｜N7 的断言顺序违反禁写清单第 4 条,且已经真的造成过一次错误结论** —— `Assert.Null(statusB)` 排在第二次 `RunAsync` 后的**第一条**,变异态下它先抛,**后面 7 条断言一次都没跑到**,失败消息只说「返回了 `Cancelled`」,把 exec 与协调者双双引向错误机理并写进了台账。**修法(具体到行)**:把 `Assert.Null(statusB);` 整行**移到方法体最后一条**(现 `Assert.Equal(1, instanceCompletedCount);` 之后),其余顺序不动、**断言内容一个字都不改**。review 与协调者**各自独立用这个重排跑过 M-N7,均稳定转红且失败点变成 `CallCount`**。
- [x] **P2-2｜`Handler_supplied_retry_delay_above_the_upper_bound_is_ignored` 的断言钉不住它自己宣称的语义** —— 测试名与注释白纸黑字写「超出 `(0, 24h]` 的值被忽略,**退回默认退避而不是钳到 24h**」,但唯一断言是**单边松界** `Assert.True(NextRetryAtUtc <= beforeHigh.AddHours(24).AddSeconds(5))` —— **「钳到 24h」这个被注释明确排除的实现恰好满足它**。证据:变异 **M-CLAMP24**(把 `ResolveRetryDelay` 改成 >24h 钳到 24h)→ **346/346 全绿**,语义被改成注释明文否定的那一种而零测试察觉。属禁写清单第 5 条(不许用松散边界代替量值断言)同族。**归责说明**:该断言是从原 T6 **逐字搬运**来的(禁写清单第 14 条要求如此),**不是 exec 引入的**;但 P3-2 拆分的目的正是让两个边界各自独立,现在下界是精确量值、上界是松界,**拆分只完成了一半**。**修法**:换成与下界同款的量值断言 `Assert.Equal(beforeHigh.AddSeconds(30), highReloaded.NextRetryAtUtc!.Value, TimeSpan.FromSeconds(5))`(语义契约期望值 `30s << min(AttemptCount-1,5)`,`AttemptCount == 1` → 30s);原 `Assert.True` 可留作兜底,但量值断言必须有。
- [x] **P2-3｜N4 漏断租约两列,「取消后租约怎么处置」这条契约面零守门** —— `A_cancelled_handler_is_not_folded_into_any_result_branch` 断了 `CallCount == 1`/`Status == Running`/`Fence == 1`/attempt **0 行**/outbox **0 行**,但**没有任何一条断言引用 `LeaseOwner` 或 `LeaseExpiresAtUtc`**,而它的兄弟 N3 两列都断了。协调者要求的「行仍 `Running` **且仍持租约**」只做到前半句。证据:变异 **M-OCE-LEASE**(给 `InvokeHandlerAsync` 加 `catch (OperationCanceledException) { 置空 LeaseOwner/LeaseExpiresAtUtc; throw; }` —— 不吞异常但顺手释放租约)→ **346/346 全绿**。**这不是牵强假想**:语义契约「取消语义(Task 2 定案)」原文就写着 OCE 要「让 lease 过期/释放、走崩溃恢复路径」,**Task 8 写 Webhook 分类规则的人照这句话去「优化成立刻释放租约」是最自然的下一步**,而现在全仓无一条测试会因此转红(N3 抓不到——它抛的不是 OCE)。**修法**:在 N4 的 `Assert.Equal(1, reloaded.Fence);` 之后补 N3 同款两行 `Assert.Equal("worker-a", reloaded.LeaseOwner);` 与 `Assert.Equal(beforeUtc.AddMinutes(5), reloaded.LeaseExpiresAtUtc!.Value, TimeSpan.FromSeconds(10));`(N4 需照 N3 在 `RunAsync` 前加一行 `var beforeUtc = DateTime.UtcNow;`)。
- [x] **P3-1(可选,便宜)｜N3/N4 的断言顺序与 N7 同病,危害小一档** —— 两条都是 `Assert.ThrowsAsync/ThrowsAnyAsync` 在最前、副作用断言在后;实测 M-OCE 下 N4 红在 `ThrowsAnyAsync`,后面「attempt 0 行/outbox 0 行/Status Running」一条没跑到。与 N7 的区别是:对 N3/N4 而言「抛出去了」本身就是被测契约的一半,不是纯副产品。修法:改成 `var ex = await Record.ExceptionAsync(…)` → 断副作用 → 最后 `Assert.IsType`/`Assert.IsAssignableFrom`。
- [x] **P3-2(可选,便宜)｜N4 里三条 `Assert.NotEqual` 是恒真死断言** —— `Assert.Equal(Running, reloaded.Status)` 之后又断 `NotEqual(Failed/Cancelled/RetryScheduled, …)`,前一条已把值钉死,后三条永不可能失败。删掉或换注释更诚实。
- [ ] **P3-3(记账,不改代码)｜N5 的 `Assert.Equal(1, reloaded.AttemptCount - attempts.Count)` 是前两条的算术推论** —— 上方已有 `Assert.Equal(2, reloaded.AttemptCount)` 与 `Assert.Single(attempts)`,`2 - 1 == 1` 恒成立。「崩溃次数 = 领取次数 − 返回次数」这条语义契约**确实首次被钉住**了,但钉住它的是那两条,不是这一条 —— **台账记账时别把它记成一条独立新保证**(本条即为该记账)。
- [x] **P3-4｜台账缺「防线拓扑」记录** —— **已在本区块上方正式补录**(含「防线 2/3 都拦不住 handler 被重复调用」这句关键限定)。
- [x] **P3-5(挂账,只改措辞)｜R7 的措辞可以精确一格** —— 「`DeadlineAtUtc`/`HandlerVersion`/`InputHash`/`OutputHash` 4 列仍零写入点」是**真话**(逐列 grep 已核),但 `DeadlineAtUtc` 现在有**读取点**(`WfNodeExecutionDispatcher.cs:156`),另三列才是零引用;R7 现在读起来像「四列都还没人用」。

**其余必做变异实测**(全部如期):M-OCE(吞 OCE 归终态)→ N4 红;M-LEASE(整条删领取谓词第三支)→ **6 条红**;**M-SPLIT-UPPER / M-SPLIT-LOWER 各自独立转红** → **P3-2「前段失败掩盖后段」确实被拆掉了**;M-SUCCEED(`case Succeeded` 不 `Plan(TakeTransitionOp)`)→ 4 条红,全部是本轮新增;M-N2(丢 `ErrorCode`/`Summary`)→ 只 N2 红。

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
| 17 | plan | Task 5 plan 完成(`Agent(model="opus")`,文件交付 `plan5.md` 245 行 34KB,零截断)。**四个硬问题全部拍板**:①状态机**不复用 lease/fence**,用 `Status + AvailableAtUtc` 可见性超时、`AttemptCount` 兼任 fence——理由是两种 fence 保护的东西不同(execution 挡领域状态损坏,outbox 最坏只是监控谎报),且投递本就 at-least-once、去重在消费方,再花三列买契约上不存在的保证不值;评审 §6.3/§八 本就没给 outbox lease/fence。②`MessageKey = {ExecutionKey}:{MessageType}` 明文派生、唯一索引单列——复用 `ExecutionKey` 会让第二种消息被 ensure 分支**静默吞掉**,独立生成会让崩溃重放造出两个 key 使去重失效,hash 则零收益(两个有界维度远低于索引键上限)。顺带定案 `MessageType` 用 **string 非枚举**(进程外线上契约 + 内核 NuGet 分发,枚举封闭会逼消费者 fork,撞可替换性第一原则)。③**payload 存全文**走 `CodeFirst_BigString`、不截断不加 hash——outbox **没有第二个正文来源**(只给 hash 消费方就没东西可发),且 Task 4 拒绝 BigString 的论据是「append-only 永不删除」而 outbox 行会进终态可清理,**生命周期不同结论不该相同**;截断 JSON = 损坏消息。④**派发消费逻辑本轮不做**——Task 6 只需要「回写短事务里入队」这一件事,零调用方的 `ClaimAsync` 没有任何测试能证明它对,等于死代码 + 空转测试。改动 **4 文件**(`WfEnums.cs` 纯追加)、测试 **7 条 313→320**(条条配反向变异 + 8 类禁写清单)、13 条陷阱、7 条射程限制(R5 把「BigString 四库往返」挂账 **Task 9**,因 `WfOutboxTests` 不在 sqlserver 腿 `TEST_FILTER` 子集而 `WfPersistenceContractTests` 在;**不改 `TEST_FILTER`**)。前端闸门明确判定**不适用**。**子代理纠正了协调者 prompt 的错误前提**:全仓禁的是裸 `"text"`(SqlServer 非 Unicode,中文变 `???`,nightly #25),`CodeFirst_BigString` 恰是**规定必须走**的路(`SysJobLog.cs:70-71`),Workflow 包内已 5 处在用;根因是我把 Task 4「attempt 一张表不存正文」的局部决定错推成仓库级取舍——**转述先例必须标注适用范围**。**不写代码、不勾选**,下一步 Round 18 Task 5 exec。 |
| 18 | exec | Task 5 exec 完成(executor/sonnet)。commit `cd356f4` "feat(workflow): add wf_outbox entity and reliable dispatch skeleton",**4 文件 +427/-0**,与 Plan §2 精确一致,零偏离。协调者独立复核:①**全 commit 零删除行**,`WfEnums.cs` 纯追加(`^-` 命中 0);②禁碰文件命中 **0**、十件套仍 **10** 条;③**三处可疑读数逐行深查**——`DefaultValue` 2、裸 `"text"` 1、`BigString` 2 **全在 XML 注释里**(正是 Plan 要求写明的理由与警告),真属性行 9 个、`DefaultValue` **实际 0 命中**、唯一 `BigString` 在 `:76` 的 `PayloadJson`;④store 的 `UtcNow` 1、`catch` 2 **同样全在注释**,方法体验证 `nowUtc` 走形参、无 try/catch、无自开事务、无 `messageKey` 形参;⑤`: BaseEntity`、唯一索引单列 + `IsUnique`、扫描索引两列、`AvailableAtUtc` 非空、枚举 1/2/3/4 无 0 值;⑥**第四处可疑读数深查**:`Assert.Equal(WfOutboxStore.` 命中 1,疑似 Plan 禁止的同义反复,查实为把**输入常量**回读比对,T2 关键断言确实手写字面拼接 → 合规;禁写清单抽查 0 命中;⑦重跑闸门 build **0 错误**(Workflow 包自身 **0** 警告;exec 自报「0 警告」系增量构建差异)、test **320/320 通过失败 0**(313+7 吻合)。**不勾选**,下一步 Round 19 Task 5 review(重点变异 `MessageKey` 两段的对偶、唯一索引、ensure 分支、自开事务;另**已标记两处疑点交 review 判**:T2 的 key 断言打在返回值而非库读回是否真缺口、7 条测试有无「断言缺失」型空转)。 |
| 19 | review | Task 5 review 完成(Opus 自审,**11 变异点**,报告 278 行落 `review5-report.md`)。**产品代码这一半质量高**:Plan §5 的 13 条陷阱**一条未踩**、9 列与定案逐列吻合、R1–R7 如实无夸大、类注释每条理由到位,**7 条测试逐条被变异证明有鉴别力、无一空转**(M1/M2 两段对偶双向转红、M5 证实「T4 必须独立存在」不是冗余、M9 证明 T6 红在该红的断言上而非靠异常侥幸、M1b 证明 D2 核心论据有真守门)。**M8 实测确认 R5 判断为真**(`BigString`→`Length=512` 在 SQLite 腿 320/320 照绿),未改 CI 未硬凑。**缺口全在测试边界:1×P1 + 2×P2**。**P1-1** `NormalizeMessageType` 三条校验零守门——M11 把函数体退化成 `return messageType;` 后 **320/320 照绿**,整段领域校验可原地蒸发;它是 trust boundary(D2 用 string 就是为了让消费者发自己的类型)且护着 `MessageKey` 结构不变量,Plan §6 没列为射程限制 → **是漏了不是测不到**。**P2-1** 正是协调者上轮标记的疑点,review 用专设的 **M10**(key 算对、返回对、**只把插库那行带 `MUTANT-` 前缀**)判定为**真缺口**:T2/T7 全绿而库里每行 key 都错,只有 T3 红且报的是方言异常;**`MessageKey` 是全表唯一从未被读回验证的列**,偏偏是主契约。**P2-2** 对外常量无快照钉死(同型 `ExecutionKey` 有 `WfExecutionKeyTests`)。协调者独立交叉验证:三次唤醒分别见到两个文件处于变异态、最终树干净;P1-1 的 `ThrowsAsync|ArgumentException` **0 命中**属实;P2-1 的 key 断言全在内存对象、唯一库查询是 `CountAsync` 数行数属实。**未勾选**,下一步 Round 20 修 P1-1/P2-1/P2-2(**P3-1/P3-2 明确不做**),要求产品代码零行为改动、原有断言一条不删。 |
| 20 | 修 Findings + 勾选 | P1-1/P2-1/P2-2 三条修完(executor/sonnet,commit `bc0dbb5`,**1 文件 +55/-0**,**产品代码零改动**、**删除的 `Assert.` 0 条**,P3-1/P3-2 按指示未做)。320 → **322**。协调者独立复核**两个变异都刻意换形状**:**A** 不用「整段蒸发」而**只删 `':'` 一条校验保留 `Trim`** → **只有 separator 那条转红、trim 那条保持绿**,证明**两条新测试彼此独立**(比 M11 更强的证据);**B** 不用显眼的 `MUTANT-` 前缀,而**落库时把 `:` 换成 `|`、返回前改回正确值**(等长、像字符清洗,更隐蔽)→ **T2/T7 双双转红**,而修复前 M10 下这两条**全绿** → P2-1 缺口确实堵上。**本轮遇一次基础设施故障并定位根因**:首次重跑出现 **119 条失败**,查明是 **C: 盘 100% 满(仅剩 127MB)**,`TestDb` 建不出 SQLite 库所致;temp 根下累积 **16,461 个 `tenon-wf-it-*.db`**(本 loop 上百轮测试的产物),清理释放 **9.6G**,**只删自己的一次性测试库、未碰用户数据与真实 `admin.db`**。重跑闸门 build **0 错误**、test **322/322 通过失败 0**。0×P1、0×未修 P2 → **Task 5 勾选(10 项已完成 5 项,过半)**。下一步 Round 21 Task 6 plan(dispatcher,本 milestone 枢纽,要拼起 Task 2–5 四块,两条核心不变量必须有测试)。 |
| 21 | plan | Task 6 plan 完成(`Agent(model="opus")`,294 行 46KB 落 `plan6.md`,零截断)。**枢纽 Task**——第一次把 Task 2/3/4/5 四块装配起来。**六个硬问题全部拍板,最关键的三个都选「复用既有先例」而非自搓**:**D1** 两短事务夹一次无事务 handler 调用,洞见是**回写事务就是引擎自己那一个事务**(`ExecuteAsync` 本就「一条 Cmd 一个事务」),新增 `NodeExecutionCompletedCmd` 仿 `TimeoutFireCmd`,§4.6「五件事同一短事务」自动成立;「handler 无活动事务」用 `db.Ado.IsAnyTran()` 在 handler 内断言。**D5** 全 Task 只许**一处** `Updateable<WfNodeExecution>`,**双谓词** `Fence` + `Status == Running`(前者挡老 owner 迟到回写、后者挡同 fence 重放,**少任一条都有一类漏网**,故各配一个变异);且 **CAS 必须在 `AppendAsync` 之前**,否则老 owner 会用被推高的 `AttemptCount` 插 attempt,**症状伪装成唯一键 bug**。**D4** `ManualFallback` **复用 `CreateTaskAsync` 但绕开 `EnterApprovalAsync`**——后者有**三条自动放行出口**,而「自动节点失败后自动放行」与 §4.7 正面冲突,是**最危险的静默 bug**;空 assignee 早返回不抛(抛会造成「回滚→租约过期→重跑→再抛」的**活锁**,D3 的「找不到 handler 合成 `TerminalFailure`」同理)。其余:dispatcher **零 DI 零接口零扫描循环**(十件套仍 10)、outbox **只终态入队**、payload **不含正文**(结掉 Task 4 挂账)、**不加 `WfHistoryEventType`** 故**前端闸门不适用**、handler 异常**一律不 catch**、引擎主构造函数不加参数。改动 **3 新增 + 4 修改**(含 `Truncate` 改 `public` 复用,兑现 Task 5 P3-1 教训),**测试 12 条 322 → 334**,**DONE-CONDITION 两条不变量各有直接测试**,禁写清单 **11 条全部由本 loop 实测教训反推**,15 条陷阱、10 条射程限制(R1 诚实写明生产侧零调用点)。协调者**独立抽验四条承重先例**:`TimeoutFireCmd` 在 `WfCommands.cs:150`、`EnterNodeOp` 三个方法均 `protected virtual` 且 `:81`/`:99` 印证三条放行出口、**`IsAnyTran()` 本仓 `WfReceiptEngineTests.cs:332` 已在用同款手法**(比子代理的反射核实更硬)、`WfHistoryEventType` 确在 `schema.d.ts` 里 —— **全部属实**。**不写代码、不勾选**,下一步 Round 22 exec。 |
| 22 | exec | Task 6 exec 完成(executor/sonnet)。commit `37b83da`,**7 文件 +1261/-1**,与 Plan §2 **文件级精确一致**。协调者独立复核:`WfCommands.cs` 纯追加(`^-` 0 命中);**全 commit 唯一 1 行删除**查实为 `Truncate` 的 `private`→`public`(行为零改动,注释写明「截断规则不许两处各写一遍」正是 Task 5 P3-1 的教训);禁碰文件 **0** 命中、十件套仍 **10**;`AttemptCount + 1` **0** 命中(差一陷阱未踩);重跑闸门 build **0 错误**(Workflow 包 0 警告)、test **334/334 通过失败 0**(322+12 吻合)。**⚠ 查出一处字面不符且 exec 自述不准确**:Plan/D5 写死「全仓只许一处 `Updateable<WfNodeExecution>`」、exec 也自称「唯一一处」,**实为两处**(`WorkflowEngine.cs:1348`/`:1365`);查实二者同在 `ClaimExecutionWritebackAsync` 的 if/else(重试分支 vs 终态分支,列集不同),**两条谓词逐字相同、三条齐全,共用同一个 `affected != 1`** → **规则要护的不变量守住了,分歧只在字面**;但**自述不准确这点必须记下**(纪律就是不采信子代理自报)。**留 Round 23 review 裁定**,并要求它**对两个分支各做一次删 `Fence` / 删 `Status == Running` 的变异(共 4 次)**,确认 T2/T3 不是只钉住了其中一条分支。另 exec 主动申报两处 Plan 留白的选择(协调者认可):测试脚手架不经 HTTP 发起但用户仍走 HTTP;T8 因「无 assignee 的节点会被 `ApplyNobodyAsync` 放行、token 停不住」,改为先用有 assignee 版本发起再 UPDATE 真实版本行的 `ModelJson`。**不勾选**,下一步 Round 23 review。 |
| 22b | exec 补记 | exec 完成通知里自报两处「值得注意的细微处」,协调者判定**两条都指向潜在缺口**,已补进 Status 的 review 着力点:**(i)** T3 的转红是以**唯一索引冲突**形式出现而非干净的 48004——正是 Plan §5 陷阱 2 预言的「症状伪装成唯一键 bug」形状;T3 是 DONE-CONDITION「只推进一次」的守门测试之一,**若它靠异常才红,可能是「因错误的原因而红」**,review 须判定在删 `Status == Running` 的变异下它的「attempt 仍 1 行/outbox 仍 1 行/token 未再前进」几条断言**是否真的执行到**(异常若抛在断言之前,证明力是虚的,违反禁写清单第 4 条)。**(ii)** T6 是被**下界**那段抓住的,**上界(24h 封顶)可能无独立鉴别力**;`RetryAfter` 是 handler 提供的 trust boundary,Plan D4 明写上界钳制「是必须实现的,不是优化」,review 须**单独变异上界**确认有无测试转红。另:exec 复原变异时用的是「从暂存基线 `git checkout --`」(因当时几个文件已有本 Task 的合法未提交改动),协调者已验证最终状态干净(commit 后工作树只剩未跟踪 `TestResults/`,且自跑闸门 334/334)。 |
| 23 | review | Task 6 review 完成(Opus 自审,**15 变异 + 2 专项探针**,报告 359 行)。**产品代码未发现功能性错误**:13 个必做变异 **11 个如期转红**(DONE-CONDITION 两条不变量的守门均在)、**15 条陷阱一条未踩**、**R1 生产侧零调用点属实**、**`Truncate` 复用无第二份截断**。**但测试层 2×P1 + 4×P2 + 6×P3**。**P1-1 = 协调者上轮点名担心的那件事,实测坐实**:重试分支 CAS **两条谓词各自删掉后 334 条全绿**(终态分支则都有守门);它是 P1 因为 `RetryScheduled` 是**唯一终态之外的回写**,缺 fence 会让**老 owner 迟到回写打回可领取并清掉新 worker 租约 → 两个 worker 并发跑同一节点**,而终态分支的两道后备防线**在这条边上都不起作用**。review 并裁定「Plan 字面偏离」**算偏离且有真实后果**——「只许一处」的用处**正是让守门测试无法只覆盖一半**;**不要求合并,要求补测试**。**P1-2** `WfManualFallbackOp` **第二条**自动放行出口零覆盖(「配了 provider 但解析 0 人」比「没配」更常见)。**P2-1/2/3 系统性断言缺失**:`HandlerType`/`Summary` 零读回、`CompletedTimeUtc` 只 `NotEqual`→值偏 10 年照绿、outbox 整行只数行数(**「正文绝不进 payload」这条 D6 决策零测试**)、`LastContext` 投影整体零断言(**`Attempt + 1` 与去掉 `SpecifyKind` 双双存活**,后者正是「非 UTC 机器悄悄错 8 小时」)。**P2-4** `HandlerType` 无截断写进 256 列。**两处疑点实测判定**:**B1** exec 现象属实但结论要修正(probe2 证明副作用断言并非虚设;「只推进一次」有**三道独立防线**,`Status` 谓词的真实职责是**错误面**);**B2 exec 的结论是错的**——单独变异上界 T6 确实红,它被**合并变异 + 同 `[Fact]` 内前段失败掩盖后段**误导。协调者独立核实四条主结论全部属实(两处 CAS 在 `:1348`/`:1365`、两条早返回在 `:34`/`:49`、`LastContext` 0 命中、`HandlerType`/`.Summary` 断言 0 命中)。**未勾选**,下一步 Round 24 修 P1-1/P1-2/P2-1..4 + P3-1/P3-4(**P3-2/3/5/6 不做**),产品代码只许改 P2-4 一处。 |
| 24 | 修 Findings + 勾选 | Task 6 的 **2×P1 + 4×P2 + 2 条便宜 P3 全修完**(executor/sonnet,commit `52fdef6`,**2 文件 +203/-5**),334 → **338**。新增 4 条测试(重试分支 stale-fence / 同 fence 重放 / `ManualFallback` 零解析人 / `Summary` 超长截断)+ T1/T3/T5/T10 断言补强。协调者逐条独立复核:**产品代码只动一处**(`WorkflowEngine.cs:1342` `HandlerType` 截 256,**全仓 `Truncate` 仍只一个**,没建第二个 helper);**4 处 `Assert.` 删除逐条查实无一真删**——两条是 P3-1 顺序重排后原样落回、一条是 outbox `Count==1` **升级**成 `Assert.Single`+整行读回(D6 的「`OutputJson` 绝不进 payload」终于有测试)、一条是 P3-4 换字面值 `48008`(已比对 `WorkflowErrorCode.cs:33`);16 `[Fact]`、十件套仍 10。**两个变异都刻意换形状**:**M-A** 不用「整条删谓词」而把重试分支 `e.Fence == fence` **放宽成 `>= fence`**(谓词还在、像「容忍追赶」)→ stale-fence 那条转红,证明钉的是**等值本身**;**M-B** 不用「换 `EnterApprovalAsync`」而**直接删 `users.Count == 0` 早返回让空列表落进 `CreateTaskAsync`**(更像真实失误)→ 零解析人那条转红,且红在「任务表出现 2 行」上,说明空列表真会建出多余任务。变异前 grep 确认、后单文件复原、`git diff --stat` 空。重跑闸门 build **0 错误**(13 警告全是 Services/Core 既有)、test **338/338 通过失败 0**。0×P1、0×未修 P2 → **Task 6 勾选(10 项已完成 6 项)**。P3-2/P3-3/P3-5/P3-6 四条挂账到 Task 7 或收口轮。下一步 Round 25 Task 7 plan(Fake Handler 全链路 + 崩溃恢复,硬核是 lease 过期重领且不重复推进,直接 UPDATE 时间戳、不建 `FakeTimeProvider`)。 |
| 25 | plan | Task 7 plan 完成(opus,七节 245 行落 `scratchpad/plan7.md`,已转写进 `## Plan`)。**核心结论:真增量比想象中窄**——Task 6 的 16 条里**已有 10 条走 `RunAsync` 全程**,四条结果路径都端到端跑过了,Task 7 只补三块(`Succeeded` 副作用 / handler 抛异常与 OCE 时「什么都不写」/ 跨两次 `RunAsync` 的崩溃恢复)。**产品代码零逻辑改动**,只改两处 XML 注释清掉挂账 P3-3/P3-5;新增 7 条 + T6 拆 1,338 → **约 346**。协调者独立抽验三条承重主张全部属实:**OCE 契约靠「没有 catch」落地**(`catch` 的 4 处 grep 命中**全在 XML 注释**,生产路径零 try/catch)、领取谓词确在 `WfNodeExecutionStore.cs:73-77`、`RunAsync` 测试命中 15 次与「10 条走全程」吻合。**协调者亲自跑它的头号结论并坐实**:删领取谓词第三支的 `e.Status == Running &&` → **338 全绿**,该守门**零鉴别力**;**另找到 plan 没写的加强证据**——回写**终态分支不清租约**(`WorkflowEngine.cs:1367-1374` 只写 5 列,重试分支 `:1350-1356` 才清),故 `Succeeded` 行会一直持有终将过期的租约,`Status == Running` 是**唯一**拦住「已完成行被重新领取、handler 重复调用」的谓词 → N7 补的是真能踩的路。变异前 grep、后单文件复原、`git diff --stat` 空。其余定案:**R1 不解除**(dispatcher 生产侧仍零调用点,DI 归 Task 8,与语义契约一致)、「远程调用不在事务内」本轮不重钉(与 T1 重复,不凑数)、`FakeNodeHandler` 不扩展、P3-6 继续挂账到 Task 10。**不写代码**,下一步 Round 26 exec。 |
| 26 | exec | Task 7 exec 完成(executor/sonnet)。commit `36a8cc0`,**3 文件 +455/-12**,与 Plan §2 **文件级精确一致**;新增 7 条 `[Fact]`(N1–N7)+ T6 拆 2,**338 → 346**。协调者独立复核:**`backend/src/` diff 里非 `///` 行数 = 0**(「产品代码只许改注释」实测守住);测试文件 5 行删除逐条查实、**`Assert.` 删除 0 条**(全是 T6 拆分带走的注释与签名行);**禁碰清单命中 0**;`[Fact]` **24**(16+7+1 吻合)、十件套仍 **10**;重跑闸门 build **0 错误**、test **346/346 通过失败 0**。**⚠ 查出两处需 review 裁定**:**(i)** exec 主动申报 **M-N7 变异下 N7 的转红形状与预期不同**——红在 `Assert.Null(statusB)`(方向其实是对的:「行被重新领取」正是要否证的事),**但第一条断言就抛,后面 6 条副作用断言一次没跑到**(Task 6 review B1 与禁写清单第 4 条的同一形状);**更要紧的是 exec 给的机理**:重新领取后只走到 `Cancelled` 而没真调 handler,是撞上了 `ResolveExecutionOutcome` 的「实例已 Approved」前置判定 → **「已完成 execution 不重复推进」可能有两道防线,而 N7 只钉住外面那道**,review 须在变异态下确认 `CallCount` 是否仍 0、并**单独变异那条前置判定**看有无守门。**(ii)** exec 申报一处计划外偏离:N1 原断言「**所有** `NodeLeave` 的 `ActorType == Worker`」首跑即红(`StartAsync` 自己那条 `NodeLeave` 是 `Human`),已**收窄到 `NodeId == "node1"`**;协调者判断方向正确,但**收窄会不会削成空转要 review 用变异确认**。另:exec **第三次**自报「build 0 warnings」,前几轮实测均为 **13**(全是 Services/Core 既有),继续记账不采信。**不勾选**,下一步 Round 27 review。 |
| 27 | review | Task 7 review 完成(opus 自审,**11 次变异**,报告 353 行落 `scratchpad/review7-report.md`)。**0×P1 / 3×P2 / 5×P3**。产品代码无功能性错误;**7 条新测试全部从库读回并经变异证明有鉴别力、无「断言缺失」空转**(本 loop 前四个 Task 反复吃亏的病,Task 7 首次没犯);两处产品注释**逐项核实是真话**;**R1 未被偷偷解除**;禁碰清单零违反;原有 16 条断言零删改(T6 拆分逐行比对**一字未改**)。**★ 最重要产出是一条被推翻的因果**:Round 26 记的「M-N7 下 handler 没被真的重复调用」**是错的**(来自 exec 自述),review 实测 `CallCount == 1`,**协调者用 review 没用过的形状独立复验坐实**(把 P2-1 的重排当探针先应用再叠加 M-N7 → `Expected: 0 / Actual: 1`);变异态真实破坏面 `Succeeded→Cancelled`/`fence=2`/`attempts=2`/`call=1`。**这条错账没落地的唯一原因是协调者当时标注了「exec 自报、留给 review 裁定」**。连同 **A-3「防线拓扑」表**(防线 1 领取谓词是**唯一**拦得住 handler 重复调用的一道,防线 2/3 只管回写段)一并补进 `## Findings`。协调者两条疑点**双双实测排除**(M-PRE → 只 T13 红,第二道防线有守门;M-N1-ACTOR → 只 N1 红,收窄未削鉴别力)。**三条 P2 全是「测试钉不住自己宣称的语义」**:P2-1 N7 断言顺序违反禁写清单第 4 条且已真的造成过错误结论;**P2-2 上界退避只有单边松界断言,M-CLAMP24 下 346 全绿**(该断言从原 T6 逐字搬运,非 exec 引入,但拆分只完成一半);**P2-3 N4 漏断租约两列,M-OCE-LEASE 下 346 全绿**,而语义契约原文就写 OCE 要释放 lease、Task 8 照做即触雷。工作树最终干净(11 变异全复原)。**不勾选**,下一步 Round 28 修 Findings。 |
| 28 | 修 Findings + 勾选 | Task 7 的 **3×P2 + 3 条便宜 P3 全修完**(executor/sonnet,commit `4e3798c`,**2 文件 +20/-7**),条数 **346 → 346**(全是断言强化/重排/删死断言,不增不减)。协调者逐条复核:**`backend/src/**` 零改动**;台账那 1 行查实**只改 Plan §6 的 R7 一句措辞**;测试文件 **6 行删除逐条对得上**(2 行 `Throws*`→`Record.ExceptionAsync`、3 行**已申报**的恒真死断言、1 行是被**移动**的 `Assert.Null(statusB)`);N7 的 `Assert.Null` 确已挪到最后并留注释;`[Fact]` **24**、十件套 **10**;闸门 build **0 错误/13 警告**、test **346/346**。**两个变异都换形状且都拿到比 fix 更强的证据**:**M-A** 不用「钳到 24h」而**只让 >24h 返回加倍退避 60s**(旧的 `<= 24h` 松界**完全放行**)→ 只上界那条红、差值正好 **30s**,证明红的是**新补的量值断言**;**M-B** 不用「两列置 null」而**只提前 `LeaseExpiresAtUtc`、保留 `LeaseOwner`**(像「让租约立刻可夺取」的优化)→ 只 N4 红**且红在 `LeaseExpiresAtUtc` 那条**——**fix 自己的变异是红在 `LeaseOwner` 上、根本没走到第二条**,协调者这个形状**单独证明了第二条断言也有独立鉴别力**。另:`fix-task7` **如实报了 13 个警告并解释增量构建缓存**,前三轮复现的「0 warnings」不准确自述在被点名后**已纠正**。0×P1、0×未修 P2 → **Task 7 勾选(10 项已完成 7 项)**。P3-3 与 Task 6 遗留的 P3-6 挂账到 Task 10 收口轮。下一步 Round 29 Task 8 plan(Webhook Handler,本 milestone **第一次接生产线**:DI 注册线与 R1 解除都指名归它,且「Webhook 超时/重试分类」这条语义契约至今空白,必须本轮填死)。 |
| 28b | (中断) | Round 29 的 Task 8 plan **未能开始**:`plan-task8`(opus)刚发出即被 API 掐断——**「拼车 5 小时额度已用完,约 59 分钟后重置」**(失败时刻 2026-09-01T16:00:57Z,请求 ID `req_xCu3Pve9sc2MEkM6t91MEX`),它只输出了一句「I will start by reading the ledger」,**零产出、零文件改动**。**这是基础设施配额问题,不是工作问题**——台账与代码均未受影响,工作树干净(仅未跟踪 `TestResults/`),346/346 仍绿,Task 7 的收口(Round 28,commit `99faa8b`)完好。**故意不给轮次 +1、不动 Status**:本轮没有推进任何阶段,`下一步` 仍正确指向 **Round 29 — Task 8 plan**,重试时原样照做即可。协调者已把下次唤醒排到配额重置之后(约 48 分钟),**不做立刻重试**(那只会再吃一次同样的失败)。**这条与 Round 20 的「磁盘写满伪装成 119 条测试失败」是同一类经验:先分辨故障是基础设施还是代码,再决定动作。** |
| 29 | plan | Task 8 plan 完成(opus,413 行落 `scratchpad/plan8.md`,已转写进 `## Plan`)。**首次派出被 API 配额掐断、零产出**(Log 28b),按用户指示直接重试即成功。**头号交付物达成**:空了整个里程碑的「Webhook 超时/重试分类」契约行**已填死**,两张分类表(状态码/异常与配置)**逐字抄进 `## 语义契约` 的「Webhook 分类规则」小节**——刻意不留在 Plan 里指过去,因为契约的规矩是「`## Plan` 被重写也不得丢」;另新增契约行「Webhook 配置 schema 与外呼请求体」。**核心结论:零新包、零新安全代码**——`HttpClient` 与 SSRF 围栏全部复用内核 `JobHttpClient`/`JobHttpFence`。协调者独立抽验四条承重主张**全部属实**:`JobHttpFence` 确为 `public static` 且分层可达;`Microsoft.Extensions.Http` **已在** `Directory.Packages.props:30`;`HttpAdminJob.cs:41` 逐字是 `when (!cancellationToken.IsCancellationRequested)`——**比协调者 prompt 提示的写法更贴本仓惯用法,子代理按代码事实选了更好的那个**;`WfNodeProps` 确在 `web/src/api/schema.d.ts:11634-11651`。**⚠ 两件事**:**(i) 前端闸门由此激活,不再是「不适用」**(加 4 个 props 字段会进 OpenAPI,exec 必跑双模板 `gen:api` + 漂移检查);**(ii) 协调者裁决 D4**:同意**不建 worker、不改 `EnterNodeOp`**,**且不新增 Task 8b**——`## DONE-CONDITION` 写的是「十项全部打勾」,加第 11 项等于改掉 loop 自己的完成判据,不是协调者能定的;GOAL 里本无 worker,Webhook 验收线也不依赖它。挂账内容与形状要求(`IAdminJob` + `sys_job` 种子行、复用 `JobSchedulerService` 选主,**不许第二套调度**)已留档,**交用户决定**。R1 因此只**部分解除**。**不写代码**,下一步 Round 30 exec。 |
