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

- 轮次: 10
- max: 70
- 当前任务: Task 3(`wf_node_execution` 实体 + `ExecutionKey` 唯一约束 + lease/fence CAS 领取)
- 当前阶段: exec 已完成,闸门已由协调者独立复跑
- 上一轮: Round 10 — Task 3 **exec**(executor/sonnet)。commit `20bfc9d`,**6 个文件 +658/-0**,与 Plan 改动清单精确一致、零偏差(4 新建 + `WfEnums.cs` 仅尾部追加 51 行 + 2 测试文件)。协调者**独立复核**:①`git show --stat` 限定 `Engine/Operations/`+`WorkflowSetup.cs`+`WorkflowEngine.cs`+`Abstractions/` → **输出为空**(零改动),`EnterNodeOp.cs:68-69` 的 `default:` → `WorkflowErrorCode.NodeTypeUnsupported` 原样保留;②`grep -rln WfNodeExecutionStore backend/src/ --include=*.cs` 排除 bin/obj 后只剩 **2 个文件**(自身 + 实体注释交叉引用)→ **零引擎调用点**,正确的中间状态;③新实体 `DefaultValue *=` 属性用法命中 **0**(仅注释里解释「为何不写」),基类 `public class WfNodeExecution : BaseEntity`(第 31 行),表名/两个索引与 Plan 一字不差;④重跑两条闸门:build **0 错误**、Workflow 贡献 **0 警告**(13 条全是 Core/Services 既有 CS1573/CS1574/CS8602),test **306/306 通过、失败 0**(291 + 15 精确吻合)。
- 下一步: Round 11 — Task 3 **review**。协调者派 `Agent(model="opus"`,不传 `subagent_type`,给全工具含 Edit)。prompt 要求:明确 declare「自审」;本 Task 的变异着力点在**领取 SQL 的每一条腿**——重点变异 (a) `ClaimAsync` 的 WHERE 三条腿逐条删掉/放宽(尤其把 `Status==Running && LeaseExpiresAtUtc<nowUtc` 那条删掉,看 #11 是否转红;把 `NextRetryAtUtc <= nowUtc` 改成 `(NextRetryAtUtc == null || NextRetryAtUtc <= nowUtc)`,看 #12 是否转红)、(b) `Fence + 1` 改成不递增或 `AttemptCount + 1` 挪走,看 #9/#11 是否转红、(c) `claimed != 1` 判定改成恒真,看 #10/#13 是否转红、(d) `WfExecutionKey.Compute` 的字段顺序/哨兵/分隔符各变一处,看 #1/#2/#3/#4 是否转红、(e) 唯一索引 `IsUnique = true` 去掉,看 #7 是否转红。并**重点审 15 条测试里有没有永远绿的空转**(Plan 已列出六类「不许写」的清单,核对 exec 有没有偷偷写进去),以及 `EnsureAsync` 有没有偷偷加 try/catch(Plan 定案是别写)。手法照旧「先 grep 确认改了 → 跑测试 → `git checkout` 单文件复原」。**仍不勾选**。

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
| attempt 记录 | append-only,重试**不覆盖**旧 attempt(AI 基石 §4.5) |
| outbox | 结果提交后可靠触发通知/外部副作用;短事务与 execution 结果同提交(AI 基石 §4.6) |
| Webhook 超时/重试分类 | 待 Task 8 plan 定案(哪些 HTTP 状态码/异常归 `RetryableFailure`,哪些归 `TerminalFailure`,是否有的场景该转 `ManualFallback`) |
| 与人工任务的关系 | `ManualFallback` 时如何创建人工 `wf_task`——待 Task 6(dispatcher)plan 定案,复用 `EnterNodeOp.CreateTaskAsync` 还是新路径 |
| 范围外 | 不建 AI Decision(`wf_ai_decision`、provider adapter、policy、shadow mode——留给 M3b);不建并行网关(`ParentTokenId`/`ForkId`/join 表);不新增审批动词;不 port React 工作流页;不抽 web/web-react 共享层 |

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段的 Agent 重写,协调者转写进本节)

> 本节为 **Task 3**(`wf_node_execution` 实体 + `ExecutionKey` 唯一约束 + lease/fence CAS 领取)的 plan,由 Round 9 的 Opus 子代理产出,协调者原文转写。Task 1/2 的历史 plan 已被覆盖,其定案沉淀在 `## 语义契约`。

### 决策点定案

**1. `ExecutionKey` 构成与拼接规则**

新增 `public static class WfExecutionKey`(`Engine/WfExecutionKey.cs`),照抄 `WfIdentityHash` 的姿势(`backend/src/TenonAdmin.Workflow/Engine/WfIdentityHash.cs:21,24`),**不复用它的 `Compute`**(那个签名被回执的 6 个维度和 `WfCommandType/WfTargetType` 焊死,追加参数会破坏已发包的回执 hash)。

- 参与字段、固定顺序:`ScopeKey → InstanceId → TokenId → NodeVisitId → NodeId → DefinitionVersionId`(评审 §六 6.1 原文「至少包含组织范围、实例、Token、`NodeVisitId`、节点和定义版本」,一字不减)。
- 分隔符 `'\n'`;数值 `InvariantCulture` 十进制;字符串 `Trim()` 保留大小写;含分隔符直接抛 `ArgumentException`。
- **`ScopeKey` 归一化直接复用 `WfIdentityHash.NormalizeScopeKey`**(public,`WfIdentityHash.cs:72`),来源是 `WfInstance.CreateOrgId?.ToString(InvariantCulture)`;无机构 → 哨兵 `"-"`。不新造第二套归一化规则。
- **`NodeVisitId` 进 key**,可空 → 同一个哨兵 `"-"`。语义边界写死:`NodeVisitId` 为 null 时 key 退化为「(scope, instance, token, node, defVer) 一次」,也就是 M3a 之前的旧语义——旧 token 永远停在原节点、不会被将来的 dispatcher 领取(dispatcher 是 Task 6 才有的东西),所以这个退化在本里程碑内**不可达**,但必须写清,否则 Task 6 会当成 bug 去"修"。**不做回填**(Task 1 契约)。
- 算法 SHA-256 → `Convert.ToHexStringLower`,**固定 64 位小写十六进制**,列 `Length = 64`、**非空**。
- 索引长度核算:SqlServer 因 `SqlServerCodeFirstNvarchar = true`(`backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs:293`)建成 `nvarchar(64)` = **128 字节 < 900**;MySQL utf8mb4 = 64×4 = **256 字节 < 3072**。选 hash 而非明文拼接的决定性理由就在这里:明文长度随 `NodeId`(64) + `ScopeKey`(64) 浮动,每加一个维度都要重算两次方言上限;hash 是常数 64,这个问题永远消失。组成字段全部保留为独立列,排查用,不参与唯一性(与 `WfOperationReceipt` 同款,`Entities/WfOperationReceipt.cs:20-22`)。

**2. `WfNodeExecutionStatus` 枚举值**(进 `Entities/WfEnums.cs` 尾部,不进 `Abstractions/`——它是持久化枚举,不是 SPI)

```
Pending = 1, Running = 2, Succeeded = 3, RetryScheduled = 4,
ManualFallback = 5, Cancelled = 6, Failed = 7
```

**刻意无 0 值**,理由同 `WfNodeExecutionResultType`(`Abstractions/IWorkflowNodeHandler.cs`):`default(WfNodeExecutionStatus)` 非法 → 漏赋值不会悄悄等于 `Pending`。注意与 `WfHistoryActorType.Unknown = 0`(`WfEnums.cs:236`)的差别是**有理由的**:那个 0 是给升级前旧行读的,本表是**新建表**,不存在旧行。

`Cancelled` 与 `Failed` 都要:`Failed` = handler 返回 `TerminalFailure` 或重试预算耗尽(永不再动);`Cancelled` = 实例被外部撤销/终止,execution 作废。两者对 Task 6 的后续动作完全不同(前者要转人工或终止实例,后者要静默丢弃),合并会让 dispatcher 分不出该不该报警。

状态转换图(Task 6 照它实现):

```
(insert) ──────────────► Pending
Pending ───── claim ───► Running
RetryScheduled ─claim──► Running        (NextRetryAtUtc <= now)
Running ─── 租约过期 ──► Running        (重新领取;Fence + 1,见决策 4)
Running ──────────────► Succeeded | RetryScheduled | ManualFallback | Failed | Cancelled
Pending | RetryScheduled ─────────────► Cancelled
Succeeded / ManualFallback / Failed / Cancelled = 终态,无出边
```

`Running → Running` 是合法自转移,**这正是 fence 存在的原因**:老 owner 可能还活着,它的回写必须靠 fence 被拒。

**3. lease/fence 字段**(逐个)

| 列 | 类型 | 可空 | 长度/默认 |
| --- | --- | --- | --- |
| `LeaseOwner` | `string?` | 是 | `Length = 128`,对齐 `SysJobLock.OwnerNodeName`。未领取 = null 而非空串(本列不进任何唯一索引,也不进领取 WHERE,NULL 无风险)。worker 标识由**调用方传参**决定,Task 3 不接 DI、不读 `AdminJobsOptions`;`JobTime.ResolveNodeName` 是 `internal` 的(`Jobs/JobTime.cs:16`),Workflow 包够不着,Task 6 自己算 `{MachineName}#{WorkerId}` |
| `LeaseExpiresAtUtc` | `DateTime?` | 是 | 无默认 |
| `Fence` | `long` | 否 | 从 **0** 起(新行),首次领取变 1。用 `long` 不用 `int`:fence 在 Task 5/8 会作为幂等/排序令牌交给外部系统(outbox/webhook),届时加宽是破坏性列变更;`Version` 从不出库,故 `int` 足够——两者不必一致 |
| `AttemptCount` | `int` | 否 | 从 0 起 |

**全表所有列一律不写 `DefaultValue`。** Task 1 那条契约管的是 `ADD COLUMN`:`DefaultValue` 唯一的作用是让 `DbMaintenanceProvider.AddColumn` 走「先加可空列 → 回填 → 改 NOT NULL」三步序列(`Entities/WfInstance.cs` 里 `Version` 的长注释已反编译核实)。**`CREATE TABLE` 路径根本不读它**,本表是新建表、四库都在建表时一次性造出全部列,写 `DefaultValue` 是噪音。这条要在实体注释里写明,否则机械套用 Task 1 契约的评审会误判成 P1。

**4. 领取的 SQL 形状**

```csharp
// nowUtc 由调用方传入(应用时间,见 ④)
var claimed = await db.Updateable<WfNodeExecution>()
    .SetColumns(e => new WfNodeExecution
    {
        Status         = WfNodeExecutionStatus.Running,
        LeaseOwner     = owner,
        LeaseExpiresAtUtc = leaseUntil,
        Fence          = e.Fence + 1,
        AttemptCount   = e.AttemptCount + 1,
    })
    .Where(e => e.Id == executionId)
    .Where(e => e.Status == WfNodeExecutionStatus.Pending
             || (e.Status == WfNodeExecutionStatus.RetryScheduled && e.NextRetryAtUtc <= nowUtc)
             || (e.Status == WfNodeExecutionStatus.Running       && e.LeaseExpiresAtUtc < nowUtc))
    .ExecuteCommandAsync();
if (claimed != 1) return null;
// 同一事务内读回(WfHistorySequence.NextAsync 同款,Engine/WfHistorySequence.cs:23)
return await db.Queryable<WfNodeExecution>().Where(e => e.Id == executionId).FirstAsync();
```

① **为什么只有一个 worker 领到**:领取成功即在该行取排他锁并持有到提交。并发的第二条 UPDATE 被阻塞,解锁后四库都对**新版本**重新求值 WHERE(PG 走 EPQ 重检查、MySQL RR 下 UPDATE 是 current read、SqlServer RC 取 U→X 锁后重读、SQLite 写事务本身串行),此时 `LeaseExpiresAtUtc` 已被推到未来 → 匹配 0 行。仓内先例:`JobSchedulerService.HeartbeatAsync` 的夺租(`Jobs/JobSchedulerService.cs:197-200`)逐字同型。

② **四库通用**:只有参数化 `UPDATE ... WHERE` + 影响行数判定,无 `RETURNING`、无 `SET @v = col = col+1`、无 `FOR UPDATE SKIP LOCKED`、无数据库时间函数。`Fence + 1` / `AttemptCount + 1` 用 Task 1 已落地的相对递增手法——**适用,直接复用**。

③ **影响行数**:`1` = 领到,读回值即真相;`0` = 该行不可领(已终态 / 租约仍有效 / 重试时间未到 / 别的 worker 抢先)。**不抛异常**,返回 `null` 让 dispatcher 跳过——与 `ClaimInstanceAsync`(`Engine/WfExecutionContext.cs:119`)抛 48004 的差别是有意的:那里是用户请求撞车、必须让用户看见;这里是 worker 扫到一行没抢到、下一拍再来,是正常运行状态。

④ **租约过期判定用应用时间**(`nowUtc` 作为参数传入,在 SQL 里是普通参数)。理由:四库时间函数名与精度各不相同,且 DB 时钟与应用时钟混用会让「谁算过期」在多实例下漂移。仓内先例同上(`l.LeaseUntil < now`,`now = time.GetLocalNow().DateTime`)。**Task 7 的手法因此成立**:测试直接把 `LeaseExpiresAtUtc` UPDATE 成过去时刻再调领取即可,无需操纵任何时钟;也可注入 `FakeTimeProvider` 从调用方那端推时间。两条路都通。

⑤ **必须先领取、再在同一事务内读回**——`SetColumns` 走条件更新路径,**不触发**只认 `UpdateByObject` 的审计 AOP(`WfExecutionContext.cs:119` 那段长注释已论证),所以本次领取不会刷新 `UpdateTime/UpdateUserId`,「审计字段不可变」的既有断言不受影响。读回的 Queryable 会吃软删除全局过滤器(`BaseEntity : ISoftDelete`),本表 `IsDelete` 永不置真,无害;**本表不是 `IOrgScoped`,故无需 `ClearFilter`**(见决策 7 的基类选型)。

**5. `AttemptCount` 精确语义**

领取的那条 UPDATE 里 **`AttemptCount + 1`,然后读回**。`Context.Attempt` = **读回后**的值 = 领取前的 `AttemptCount + 1` = Task 4 将写的 `AttemptNo`。首次领取:0 → 1,`Attempt = 1`。三处口径一次对齐,1 基。

**6. 索引**

```csharp
[SugarTable("wf_node_execution", TableDescription = "节点可靠执行记录")]
[SugarIndex("uk_wf_node_exec_key", nameof(ExecutionKey), OrderByType.Asc, IsUnique = true)]
[SugarIndex("idx_wf_node_exec_scan", nameof(Status), OrderByType.Asc, nameof(NextRetryAtUtc), OrderByType.Asc)]
```

- 唯一索引名 `uk_wf_node_exec_key`,命名对齐 `uk_wf_receipt_identity`。
- **SqlServer 唯一索引把多个 NULL 视为相等这个坑本表踩不到**:`ExecutionKey` 非空(`= ""` 初始化 + 计算值必然 64 位)。这正是选「哨兵归一化 + hash」而不是「可空组合唯一索引」的第二个理由,与评审 §五「不要直接依赖包含 nullable `CreateOrgId` 的组合唯一索引」同源。
- 扫描索引 `(Status, NextRetryAtUtc)` 取自评审 §八原文。dispatcher 未来的「找可领取的行」查询形状:`WHERE Status = Pending OR (Status = RetryScheduled AND NextRetryAtUtc <= now) OR (Status = Running AND LeaseExpiresAtUtc < now)`,三条腿都以 `Status` 打头,一个索引够用。
- **刻意不建** `(InstanceId)` / `(TokenId)`:今天没有任何查询用它们。等 Task 6 的扫描或详情 API 真出现再加。

**7. 交付边界与 API 形状**

`public static class WfNodeExecutionStore`,放 `Engine/WfNodeExecutionStore.cs`(与孪生的 `WfHistorySequence.cs` 同目录;**不预建 `Execution/` 目录**,Task 6 真长出 dispatcher 时再议——沿用 Task 2 的同款决定)。

- **`public` 而不是 `internal`**(`WfHistorySequence` 是 internal):本仓**没有任何 `InternalsVisibleTo`**(全仓 grep 命中 0),而 Task 3 里没有任何引擎路径调用本类(dispatcher 归 Task 6)——做成 internal 等于本轮「能领取」零直接证据。`WfIdentityHash` 同为 `public static`,先例一致。
- **零 DI 注册**(同 Task 2 的定案,第一条注册线仍归 Task 8)。`WorkflowSetup.cs` 本轮**零改动**,十件套仍 10 条。
- 两个方法:

```csharp
public static Task<WfNodeExecution> EnsureAsync(ISqlSugarClient db, WfNodeExecution row, CancellationToken ct);
public static Task<WfNodeExecution?> ClaimAsync(ISqlSugarClient db, long executionId, string owner,
                                                DateTime nowUtc, TimeSpan leaseDuration, CancellationToken ct);
```

`EnsureAsync` = 按 `ExecutionKey` 先查、没有则插、返回行。**唯一冲突的「认赢家」恢复本轮不做**,理由与射程见下(决策不是遗漏,是划界)。

**8. 时间列类型与 UTC 口径**

本表**四个业务时间列全部 UTC**,列名一律带 `Utc` 后缀(`DeadlineAtUtc` / `NextRetryAtUtc` / `LeaseExpiresAtUtc` / `CompletedTimeUtc`,取自评审 §6.1 原文),值来自 `TimeProvider.GetUtcNow().UtcDateTime`,由调用方算好传进来。

这是**刻意偏离**本仓「持久化业务时间戳走 `GetLocalNow().DateTime`」的惯例(隔壁 `WfInstance.CompletedTime` 就是 local,`WfExecutionContext.WriteInstanceTerminalStatusAsync`),依据是评审 §六 收尾那句「时间相关的新字段统一采用 UTC 语义……避免多实例时区和夏令时影响 deadline、lease 与 retry」。列名后缀就是唯一的护栏,必须写进实体注释。

**由此产生的硬约束(写进注释)**:本表基类审计列 `CreateTime`/`UpdateTime` 仍是 local(AOP 填的),**任何代码都不得把它们与 `*Utc` 列做比较或相减**。

Task 2 的 `DateTimeOffset DeadlineAtUtc` ↔ 本表 `DateTime DeadlineAtUtc` 的那次转换:`new DateTimeOffset(DateTime.SpecifyKind(row.DeadlineAtUtc, DateTimeKind.Utc), TimeSpan.Zero)`——**落点在 Task 6 的 dispatcher**,本 Task 不写。`SpecifyKind` 不可省:SqlSugar 读回的 `DateTime` 是 `Kind.Unspecified`,直接构造 `DateTimeOffset` 会按本机时区偏移,在非 UTC 机器上悄悄错 8 小时。

### 改动清单

| 路径 | 新建/改什么 |
| --- | --- |
| `backend/src/TenonAdmin.Workflow/Entities/WfNodeExecution.cs` | **新建**。实体 + `[SugarTable]` + 2 个 `[SugarIndex]`,19 列,继承 `BaseEntity` |
| `backend/src/TenonAdmin.Workflow/Entities/WfEnums.cs` | **改**。文件尾追加 `WfNodeExecutionStatus`(7 个成员 + 状态转换图注释),其余零改动 |
| `backend/src/TenonAdmin.Workflow/Engine/WfExecutionKey.cs` | **新建**。`public static`,`Compute` + 复用 `WfIdentityHash.NormalizeScopeKey` |
| `backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionStore.cs` | **新建**。`public static`,`EnsureAsync` + `ClaimAsync` |
| `backend/tests/TenonAdmin.Tests/WfExecutionKeyTests.cs` | **新建**。6 条快照/规则测试 |
| `backend/tests/TenonAdmin.Tests/WfNodeExecutionClaimTests.cs` | **新建**。9 条建表/领取/租约测试 |

**4 新建 + 1 改(仅追加)+ 2 测试文件。`WorkflowSetup.cs`、`Engine/Operations/**`、`WorkflowEngine.cs`、`Abstractions/**` 全部零改动。** 前端零改动。

### 实现步骤

**1. `WfEnums.cs` 尾部追加枚举**(`WfHistoryActorType` 之后)

```csharp
/// <summary>
/// 节点执行**行状态**(<c>wf_node_execution.Status</c>;AI 基石 §4.6)。
/// <para><b>与 <see cref="WfNodeExecutionResultType"/> 是两个类型,不许合并、不许共用数值</b>:
/// 那个是一次 attempt 的答复,本枚举是多次 attempt 聚合出的行状态。</para>
/// <para><b>刻意无 0 值</b>:理由同 <see cref="WfNodeExecutionResultType"/>。与
/// <see cref="WfHistoryActorType.Unknown"/> = 0 的差别有理由——那个 0 是给升级前旧行读的,
/// 本表是新建表,不存在旧行。</para>
/// <para>转换图(Task 6 照此实现):…… ← 把决策 2 那张图整个抄进来</para>
/// </summary>
public enum WfNodeExecutionStatus { Pending = 1, Running = 2, Succeeded = 3, RetryScheduled = 4, ManualFallback = 5, Cancelled = 6, Failed = 7 }
```

**2. `WfExecutionKey.cs`** — 结构逐字对照 `WfIdentityHash`:`private const char Separator = '\n'`、`NormalizeNodeId`(Trim + 拒绝分隔符 + 非空校验)、`VisitSentinel` 直接用 `WfIdentityHash.ScopeSentinel`(同一个 `"-"`,不新造常量)。

```csharp
public static string Compute(string? scopeKey, long instanceId, long tokenId,
                             long? nodeVisitId, string nodeId, long definitionVersionId)
{
    var scope = WfIdentityHash.NormalizeScopeKey(scopeKey);   // 复用,不复制规则
    var node  = NormalizeNodeId(nodeId);
    var visit = nodeVisitId?.ToString(CultureInfo.InvariantCulture) ?? WfIdentityHash.ScopeSentinel;
    var payload = string.Join(Separator, scope,
        instanceId.ToString(CultureInfo.InvariantCulture),
        tokenId.ToString(CultureInfo.InvariantCulture),
        visit, node,
        definitionVersionId.ToString(CultureInfo.InvariantCulture));
    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
```

类注释必写:发包后不可逆契约、只允许末尾追加(且必须同时给旧维度定哨兵)、`NodeVisitId` 为 null 的语义边界(决策 1 那段)、快照测试转红意味着契约被破坏而不是去改期望值——措辞对齐 `WfIdentityHash` 的开头。

**3. `WfNodeExecution.cs`** — `BaseEntity`,19 列。基类选型的注释是**必写项**,理由比回执那处更强:

> 刻意继承 `BaseEntity` 而非 `DataEntity`。`DataEntity` 带 `IOrgScoped` 全局数据范围过滤器(只作用于 SELECT),而本表的读写方是**没有 HTTP 请求上下文的后台 worker**——`IDataScopeContext` 是空的,`IOrgScoped` 过滤器会让扫描直接返回 0 行,症状是「调度器永远扫不到活干」而不是报错。机构维度由显式非空的 `ScopeKey` 承载,与 `WfOperationReceipt` 同源。

其余每列的注释要点:`ExecutionKey` 长度核算(决策 1 那两个字节数)、时间列 UTC 口径 + 「不得与 `CreateTime` 混算」、`Fence` 为何是 `long`、全表不写 `DefaultValue` 的机制解释(决策 3 末段)、`HandlerType`/`HandlerVersion`/`InputHash`/`OutputHash`/`CompletedTimeUtc` **零写入点、建表期预留**(同 `WfHistoryActorType.Worker/Ai` 的先例;一次建全比将来 ALTER 四方言便宜)。

**4. `WfNodeExecutionStore.cs`** — `ClaimAsync` 照决策 4 的代码形状;`EnsureAsync`:

```csharp
public static async Task<WfNodeExecution> EnsureAsync(ISqlSugarClient db, WfNodeExecution row, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    var existing = await db.Queryable<WfNodeExecution>()
        .Where(e => e.ExecutionKey == row.ExecutionKey).FirstAsync();
    if (existing is not null) return existing;
    await db.Insertable(row).ExecuteCommandAsync();   // Id 由审计 AOP 填雪花
    return row;
}
```

类注释必写「本轮**不做**唯一冲突的认赢家恢复,Task 6 加,PG 需 savepoint,抄 `Services/WfOperationReceiptService.cs` 的 `BeginNestedAsync/RollbackNestedAsync`」——把坑标在原地,别让 Task 6 重新踩一遍。

**5. 测试** — `WfNodeExecutionClaimTests` 用 `WorkflowAppFactory`,从 `f.Services.GetRequiredService<ISqlSugarClient>()` 直接读写(`WfPersistenceContractTests` 同款),不经引擎。

### 测试清单(基线 291 → **预期 306,新增 15 条**)

`WfExecutionKeyTests`(6 条,仿 `WfIdentityHashTests`)
1. 已知输入 → 已知 64 位小写 hex(**硬编码快照值**,锁死契约)
2. `NodeVisitId = null` 归一化为哨兵,且与真实 visitId 算出不同 hash
3. `ScopeKey` null / 空串 / 纯空白 三者同 hash,且等于显式传 `"-"`
4. 字段顺序生效:交换 `instanceId` 与 `tokenId` 的取值 → 不同 hash
5. `NodeId` 含 `'\n'` → `ArgumentException`;`NodeId` 空白 → `ArgumentException`
6. 输出恒为 64 位、全小写十六进制

`WfNodeExecutionClaimTests`(9 条)
7. **唯一索引真被建出来**:同 `ExecutionKey` 插第二行 → 抛(本轮"建表成功"的唯一硬证据)
8. 新行读到 `Status=Pending, Fence=0, AttemptCount=0, LeaseOwner=null, LeaseExpiresAtUtc=null`
9. 领取 `Pending` → 返回非 null,`Status=Running`、`Fence=1`、`AttemptCount=1`、owner/租约已写
10. 租约有效期内再领 → 返回 `null`,且行未被改动(`Fence` 仍 1、owner 未变)
11. **把 `LeaseExpiresAtUtc` 直接 UPDATE 成过去时刻后再领 → 成功,`Fence=2`、`AttemptCount=2`**(Task 7 崩溃恢复要用的手法,本轮先证明可行)
12. `RetryScheduled` + `NextRetryAtUtc` 在未来 → `null`;改成过去 → 领到
13. 终态行(`Succeeded`)→ `null`
14. `EnsureAsync` 按 `ExecutionKey` 幂等:同 key 第二次返回既有行,表内仍 1 行
15. 领取处在被回滚的事务里 → `Fence`/`AttemptCount` 不留痕(回滚契约,仿 `WfReceiptEngineTests` 同型断言)

**明确不值得写、不许拿来凑数的**
- 枚举成员数/数值断言 —— 套套逻辑;数值真被改动时 #1 的快照与 #9 的状态断言会先红。
- 「两个 worker 真并发抢同一行」—— **构造不出来**,与 `WfVersionCasTests` 开头那段射程声明逐字同型(单线程下第二次读必然读到最新值)。可达的是 0 行分支,#10/#12/#13 已覆盖。
- 列宽 / 中文往返 / 唯一索引在四库上真被建出 —— SQLite 类型亲和性下是**恒真断言**(`WfPersistenceContractTests` 已实测记录),归 Task 9。
- 「存量行 `ADD COLUMN NOT NULL`」升级契约 —— 本表是新建表,走不到那条路径;那条归 Task 9。
- 「本 Task 零 DI 注册」断言 —— 断言"什么都没发生"是噪音;十件套已有测试自会红。
- `ExecutionKey` 长度 ≤ 索引上限的运行时断言 —— 它是常数 64,#6 已钉住。

### 陷阱

1. **最大的越界风险:顺手接引擎/调度器。** `EnterNodeOp.ExecuteAsync` 今天对 `WfNodeType.Webhook` 走 `default:` 抛 48008(`Engine/Operations/EnterNodeOp.cs:68-70`)。Task 3 **不改这一行**,也不在任何 Op 里调 `WfNodeExecutionStore`——本表本轮**零引擎调用点**,这是正确的中间状态。一旦"顺便让它跑起来"就把 Task 6 干了。
2. **`DefaultValue` 会被机械套用。** Task 1 的契约在评审眼里很显眼,而本表一个 `DefaultValue` 都没有。实体注释必须把「`CREATE TABLE` 不读它」这个机制写死,否则会被误判成 P1 并被"修"成一堆无用的 `DefaultValue="0"`。
3. **`DataEntity` 的诱惑。** `WfInstance` 是 `DataEntity`,照抄它会给本表挂上 `IOrgScoped` → 后台扫描 0 行,而且**在有 HTTP 上下文的集成测试里可能仍然是绿的**,只在真实 worker 里才炸。基类必须是 `BaseEntity`。
4. **PG 唯一冲突中止整事务。** `EnsureAsync` 本轮不处理,但若 executor "顺手"加了 try/catch + 二次 SELECT,在 PG 上那次 SELECT 根本执行不了(`25P02`),且新异常会顶替原始冲突异常 —— 要么完整抄 savepoint 那套,要么就别写 catch。**本轮的定案是别写。**
5. **读回必须与 UPDATE 在同一事务内。** 两条裸自动提交语句之间,另一个 worker 的领取会让读回的 `Fence`/`AttemptCount` 是别人的值 —— `Attempt` 差一的静默 bug 就是这么来的。`ClaimAsync` 的注释要像 `WfHistorySequence` 那样明写「必须在事务内才成立」,调用方(Task 6)负责起事务。
6. **`SetColumns` 里内联 `DateTime` 表达式。** 台账已记录实测:SqlSugar 会按当前区域把内联表达式格式化成字面量拼进 SQL,zh-CN 下炸出 `near "下午"`。**先算进局部变量**(`ClaimInstanceAsync` 的注释里有原话),`nowUtc`/`leaseUntil`/`owner` 全部先落局部变量。
7. **`NextRetryAtUtc <= now` 在 `NextRetryAtUtc` 为 NULL 时是 false(四库一致)**——这是对的,靠的正是 `Status == RetryScheduled` 那条腿把它挡在外面。别有人为了"保险"改成 `(NextRetryAtUtc == null || NextRetryAtUtc <= now)`:那会让一条刚标记重试但还没算出时间的行被立刻领走。
8. **与 Task 4 的对齐风险**:`AttemptCount` 的 +1 时机(领取时,不是写 attempt 行时)。Task 4 建 `wf_node_execution_attempt` 时若在插行处再 +1 一次,就是经典差一。
9. **与 Task 6 的对齐风险**:`DateTimeOffset ↔ DateTime` 的 `SpecifyKind`(陷阱见决策 8),以及 lease 续期(长任务跑超租约会被别的 worker 抢走 → 那时 fence 拒写回,结果丢失)。**续租不在 Task 3 射程**,但 `LeaseExpiresAtUtc` 列的设计已支持,Task 6 决定策略。
10. **会波及的现有测试:预期为零。** `WorkflowSetup.cs` 零改动 → 十件套(`WorkflowReplaceabilityTests`)不受影响;`WfEnums.cs` 只在尾部追加 → 无既有枚举数值变动;新表由 CodeFirst 自动建,`WfPersistenceContractTests` 不查表清单。若任何既有测试转红,说明改动越界了,先查是不是碰了 `Engine/`。

### 射程限制

| 测不到的不变量 | 为什么 | 谁兜住 |
| --- | --- | --- |
| **R1｜真并发两个 worker 抢同一行只有一个赢** | 单线程集成测试构造不出「A 求值 WHERE → B 提交 → A 写」的交错(与 `WfVersionCasTests` 开头的射程声明同根)。本轮只能证明 0 行分支可达、以及机制确实做了条件更新 | 机制层面由 #10/#12/#13 钉住(退回无条件 UPDATE 立刻红);四库的锁语义由 **Task 9** 的四库套件在真库上问;崩溃后重新领取由 **Task 7** |
| **R2｜唯一索引在 MySQL / PG / SqlServer 上真被 CodeFirst 建出来** | 本机只有 SQLite 腿;#7 在 SQLite 上绿只说明 SQLite 建出来了 | **Task 9**(照抄 `WfPersistenceContractTests` B 段「唯一索引是否真被建出来」的姿势) |
| **R3｜PG 唯一冲突后 `EnsureAsync` 能认出赢家** | 本轮**根本没实现**(无并发创建方),且 PG 的事务中止语义在 SQLite 腿上完全不可见 | **Task 6**(加 savepoint,抄 `WfOperationReceiptService`);四库验证归 **Task 9** |
| **R4｜UTC/local Kind 不混用** | `DateTime` 类型层面无法强制,只有列名后缀 + 代码审查。SQLite 腿上 local 与 UTC 都能存能读,混用不会红 | **Task 6** 的 `DateTimeOffset` 转换点是唯一会暴露 Kind 错误的地方;`WfNodeExecutionContext.DeadlineAtUtc` 的断言在 Task 6 才有观测点 |
| **R5｜`Attempt` 三处口径真的对齐** | 本轮只有 `AttemptCount` 一处可观测(#9/#11),`AttemptNo` 列和 Context 的 `Attempt` 都还不存在 | **Task 4**(写 `AttemptNo`)+ **Task 6**(喂 Context),届时三处对照 |
| **R6｜租约到期后老 owner 的回写真的被 fence 拒掉** | 回写路径(保存 attempt/结果/推进 token)是 Task 6 的短事务,本轮不存在。#11 只证明「过期后能被重新领取且 fence 递增」,没证明「递增之后老 fence 写不进去」 | **Task 6**(回写 CAS `WHERE Fence == @myFence`)+ **Task 7**(崩溃恢复端到端) |

### 闸门

```bash
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test  backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"
```

预期:build **0 错误**;工作流过滤子集全绿。全量重跑确认 **306/306 通过、失败 0**(291 + 15)。

**不需要前端闸门**:本 Task 零控制器、零 DTO、零端点,`/openapi/v1.json` 不变 → `web/` 与 `web-react/` 的 `schema.d.ts` 不会漂移,`scripts/check-contract-drift.mjs` 无需跑。

## Tasks

> 任务顺序 = 依赖顺序。编号稳定;`## Log` 引用任务号。

- [x] **1. `NodeVisitId` 贯穿 + `wf_history` 补字段**:`WfToken`/`WfTask`/`WfHisTask`/`WfHistory`/`WfCc` 加 `NodeVisitId`(每次进新节点生成,停留期间不变,与 `EnterNodeOp` 的 token 级 CAS 同一事务写入);`wf_history` 补 `TokenId`/`Sequence`(实例内单调递增,并发写入方式待 plan 定案)/`ActorType`/`ActorUserId`/`PayloadVersion`(`RequestId` 已在 M2c 做完,不重做)。这是后续所有 execution 相关表「稳定身份」的地基,必须先做。
- [x] **2. `IWorkflowNodeHandler` SPI + Context/Result 类型**:定义最小 Interface(`ExecuteAsync(WfNodeExecutionContext, CancellationToken) -> WfNodeExecutionResult`);`WfNodeExecutionContext` 只含不可变快照(tenant/org、定义版本、实例、token、节点配置、变量/证据快照、`ExecutionKey`、attempt、deadline),不泄漏 SqlSugar 实体/DB session;`WfNodeExecutionResult` 是 `Succeeded`/`RetryableFailure`/`ManualFallback`/`TerminalFailure` 的显式判别联合或枚举+payload。附一个 `FakeNodeHandler` 参考实现(可配置返回哪种结果,供后续 Task 当测试替身)。**本 Task 不接入引擎**,纯类型/接口定义。
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
