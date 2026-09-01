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

- 轮次: 19
- max: 70
- 当前任务: Task 5(`wf_outbox` 实体 + 可靠派发骨架)
- 当前阶段: review 已完成,**1×P1 + 2×P2 待修**
- 上一轮: Round 19 — Task 5 **review**(`Agent(model="opus")`,全工具含 Edit)。已 declare 自审,**11 个变异点全部走完三步法并复原**,报告 278 行落 `scratchpad/review5-report.md`。**结论分两半**:产品代码这一半**质量高**——Plan §5 的 13 条陷阱**一条未踩**、9 列与 D5 定案逐列吻合、§6 的 R1–R7 如实无夸大、类注释把每条「为什么不做」都交代到位(尤其「`AttemptCount` 即 fence,别再造 `Fence` 列」),7 条测试**逐条被变异证明有鉴别力、无一空转**,零写入点的列没有被编造假覆盖;缺口全在**测试的边界**上,共 **1×P1 + 2×P2 + 2×P3**(详见 `## Findings`)。**P1-1**:`NormalizeMessageType` 的三条校验(`Trim()`/拒空白/拒含 `':'`)**零守门测试**——变异 M11 把整个函数体退化成 `return messageType;` 后 **320/320 照绿**,整段领域校验可原地蒸发而无人发觉;不能豁免是因为它是 **trust boundary**(D2 定案 `MessageType` 用 string 就是为了让消费者发自己的类型)且保护着 `MessageKey` 的结构不变量,而 Plan §6 **没把它列为射程限制**——是漏了不是测不到。**P2-1**:正是协调者 Round 18 标记的疑点,review 用专门设计的 **M10**(key 算对、返回对、**只把插库那行带 `MUTANT-` 前缀**)判定为**真缺口**——T2/T7 全绿而库里每行 key 都是错的,只有 T3 因唯一索引冲突红且报的是 SQLite 方言异常;逐列盘查后 **`MessageKey` 是全表唯一从未被读回验证过的列**,而它是本表主契约。**P2-2**:对外常量的字面值无快照钉死(同型的 `ExecutionKey` 在本仓**有** `WfExecutionKeyTests` 钉死)。另 **M8 实测确认 R5 判断为真**(`BigString` → `Length=512` 在本机 SQLite 腿 320/320 照绿),**未为它改 CI、未硬凑测试**。协调者**独立交叉验证**:①三次唤醒中分别观察到 `WfOutbox.cs`、`WfOutboxStore.cs` 处于变异态、最终工作树干净,**变异确实发生过且已逐个复原**(`git status` 只剩未跟踪 `TestResults/`);②P1-1 核实——测试文件 `ThrowsAsync|ArgumentException` **0 命中**,属实;③P2-1 核实——`MessageKey` 断言全在内存对象(`:53`/`:173-175`),唯一碰该列的库查询是 `:111` 的 `CountAsync`(数行数非值读回),属实。**未勾选**(有未修 P1/P2)。
- 下一步: Round 20 — Task 5 **修 Findings**。协调者派 `Agent(subagent_type="oh-my-claudecode:executor", model="sonnet")`,**只修 P1-1 / P2-1 / P2-2 三条,P3-1/P3-2 明确不做**(P3-1 是 YAGNI 且已挂账给消费者任务,P3-2 无可观测行为)。prompt 要点:①**产品代码零行为改动**——三条全是补测试(P2-2 是一行常量快照断言),`WfOutbox.cs`/`WfOutboxStore.cs`/`WfEnums.cs` **一行都不该动**;②P1-1 加 **2 条**测试别凑 3 条,含 `':'` 那条**必须同时断言表内 0 行**(证明抛在写库之前,同 T4 纪律),`Trim` 那条**必须从库读回**;③P2-1 在 T2/T7 **现有断言之后追加**读回断言,**保留**原有对返回值的断言(它证明的是另一件有价值的事),期望值仍**手写字面拼接不许调 store**;④**原有 7 条测试的断言一条都不许删改**,条数 320 → **322 或 323**(取决于 P1-1 合并与否),只增不减;⑤跑两条闸门。协调者事后**亲自重跑 M11 与 M10 两个变异确认转红**(**并刻意换一个子代理没用过的形状**)、复原、重跑闸门,再**勾选 Task 5**。

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

> **当前 Plan = Task 5**(`wf_outbox` 实体 + 可靠派发骨架)。Round 17 由 `Agent(model="opus")` 产出,全文经 `scratchpad/plan5.md` 交付,协调者原样转写(仅标题层级下沉一级)。Task 1–4 的历史 Plan 已被覆盖,其定案沉淀在 `## 语义契约` 与 `## Findings` 里。

### 1. 决策点定案

#### D1(硬问题 ①)outbox 的状态机与领取方式:**不复用 lease/fence CAS,用 `Status + AvailableAtUtc` 可见性超时**

- **决策**:`WfOutboxStatus { Pending=1, Dispatching=2, Dispatched=3, Failed=4 }`,**刻意无 0 值**(同 `WfNodeExecutionStatus`/`WfNodeExecutionResultType`,本表是新建表无旧行,`default(枚举)` 非法可以让 `switch` 的 `default:` 臂抛异常)。**不新增 `LeaseOwner`/`LeaseExpiresAtUtc`/`Fence` 三列**;租约由 **`AvailableAtUtc` 一列兼任**(领取 = 把它推到未来,即经典可见性超时),陈旧 owner 的迟到回写由**回写时 CAS `AttemptCount`** 挡住 —— `AttemptCount` 每次领取 +1、单调、已经在表里,它**就是** fence。

  状态转换图(本 Task 只落 `(insert) → Pending` 一条边,其余归消费者任务):
  ```
  (insert) ─────────────────────────► Pending          (AvailableAtUtc = nowUtc,立即可领)
  Pending        ─── claim ─────────► Dispatching       (AvailableAtUtc = now + 可见性超时;AttemptCount + 1)
  Dispatching    ─── 超时未回写 ────► Dispatching       (重新领取;AttemptCount + 1 —— 合法自转移,正是 CAS 存在的理由)
  Dispatching    ─── 投递成功 ──────► Dispatched        (终态;CompletedAtUtc)
  Dispatching    ─── 可重试失败 ────► Pending           (AvailableAtUtc = now + 退避;LastError)
  Dispatching    ─── 预算耗尽/永久失败 ► Failed          (终态;CompletedAtUtc + LastError)
  Dispatched / Failed = 终态,无出边
  ```
  领取谓词四库通用、一条:`WHERE Status IN (Pending, Dispatching) AND AvailableAtUtc <= @nowUtc`(**注意本轮不实现,见 D4**)。

- **备选**:(a) 逐字复用 `WfNodeExecutionStore.ClaimAsync` 的 lease/fence 三列;(b) 最朴素的 `Pending → Dispatched` 无租约(只靠 `AttemptCount` 和一个 `NextRetryAtUtc`)。

- **选它的理由**(**不是为了对称,也不是为了省事**):
  1. **execution 的 fence 是为了保护「流程状态推进」这件不可重放的事**。老 owner 的迟到回写会把 token 推到错误的下一节点、把任务状态写错 —— 那是**领域状态损坏**,必须被拒。outbox 的「回写」只有 `Status/CompletedAtUtc/LastError` 三个自述字段,老 owner 迟到回写最坏结果是**「行上写着 Failed,可消息其实已经发出去了」的监控谎报**,不是状态损坏。用一个专门的 `Fence long` 列 + 三列租约换一个监控谎报的修复,不值,而 `AttemptCount` CAS 零成本拿到同一效果。
  2. **投递本来就是 at-least-once,进程外消费方必须靠 `MessageKey` 去重(见 D2)**。既然重复投递在契约上是被允许的,再花三列去把重复投递压到 at-most-once 是在为一个**不存在的保证**付表结构的钱。反过来 execution 的「同一 execution 只推进一次」是 §4.8 的验收线明文,那里的 fence 不能省。
  3. **可靠性一分没丢**:进程崩在投递中途 → `AvailableAtUtc` 到期后行自动重新可领(可见性超时),这与 lease 过期重领是同一机制,只是租约字段换了个已经存在的列承载。
  4. **与设计文档一致**:评审 §6.3 给 outbox 列的字段里根本没有 lease/fence(`ExecutionId/MessageType/MessageKey/PayloadJson·PayloadHash/Status/AttemptCount/AvailableAtUtc/LastError/CompletedAtUtc`),§八 给的索引也只有 `UNIQUE(MessageKey)` + `(Status, AvailableAtUtc)` —— 文档已经把 outbox 设计得比 execution 轻,本决策是照做而不是发明。

- **一处刻意偏离 execution 先例并写进注释**:`AvailableAtUtc` **非空**(execution 的 `NextRetryAtUtc` 可空)。理由是硬的 —— 它同时承载「何时可重试」与「租约到期」两件事,非空让领取谓词退化成一次简单比较,天然绕开 Task 3 不得不为之写一条测试(#12b `NextRetryAtUtc == null` 永不可领)的 SQL 三值逻辑陷阱。入队时 `AvailableAtUtc = nowUtc`(立即可投)。

- **影响到谁**:Task 6(在回写短事务里入队,不关心状态机)、未来的消费者任务(照此图实现领取/退避/终态,**并且必须在回写时带 `WHERE AttemptCount = @myAttemptCount` 的 CAS** —— 这条要写进实体注释,否则下一个任务会去造一个多余的 `Fence` 列)。

#### D2(硬问题 ②)幂等键:`MessageKey` = **`{ExecutionKey}:{MessageType}` 明文派生**,唯一索引建在 `MessageKey` 单列

- **决策**:`MessageKey`(`string`,`Length=128`,非空)= `execution.ExecutionKey`(定长 64 hex)+ `':'` + `MessageType`。与 `ExecutionKey` 的关系是 **派生**,既不是同一个也不是独立生成。唯一索引 `uk_wf_outbox_message_key` 建在 **`MessageKey` 单列**(§八 原文)。构造收在 `WfOutboxStore.EnqueueAsync` 内部,**签名里没有 `messageKey` 形参**(同 Task 4 拿掉 `attemptNo` 形参的手法:调用方没机会传错一个)。

- **备选**:(a) 直接复用 `ExecutionKey` 当 `MessageKey`;(b) 入队时生成一个独立雪花/GUID;(c) 再做一次 SHA-256(`WfExecutionKey.Compute` 同型)。

- **选它的理由**:
  1. **不能复用 `ExecutionKey`**:一次 execution 允许产出**多种** `MessageType`(完结通知、外部回调……)。若 key 就是 `ExecutionKey`,第二种消息会与第一种撞唯一键,而 ensure-insert 的语义是「已存在就返回既有行」→ **第二条消息被静默吞掉**,一条日志都不留。这是本决策最重要的一条,也是测试 T2/T7 存在的全部理由。
  2. **不能独立生成**:独立 Id 意味着入队不幂等 —— 崩溃恢复后 execution 被重新领取、handler 重跑、回写事务重放,会产出**同一条消息的两个不同 key**,进程外消费方的去重当场失效,而去重正是这个键唯一的职责。
  3. **不做 hash,用明文拼接**:`WfExecutionKey` 之所以要 hash,是因为它要压缩**六个变长维度**,并绕开 MySQL utf8mb4 3072B / SqlServer 900B 的索引键上限。这里只有**两个定长/有界**维度(64 位 hex + 枚举式短字符串),`Length=128` 在四库上都远低于上限(SqlServer `SqlServerCodeFirstNvarchar=true` → `nvarchar(128)` 实占 256B < 900;MySQL utf8mb4 实占 512B < 3072)。压缩没有收益,而明文的收益是实的:**运维能一眼看出这条消息属于哪个 execution**,排查时不用反查。**不新建 hash helper、不扩 `WfIdentityHash` 职责。**
  4. 非空 + 定长前缀 → 天然避开 SqlServer「多个 NULL 视为相等」;`':'` 作分隔符无歧义,因为前缀是定宽 64 的十六进制。

- **已知天花板(刻意接受,写进注释)**:一个 (execution, messageType) 只能有一条消息。同一 execution 需要发**两条同类型**消息(例如配了两个 webhook 地址)时本形状不够;升级路径是在末尾追加一个 discriminator 段并给旧维度定哨兵 `"-"`(与 `ExecutionKey` 的追加规则同源)。M3a-1 内不可达 —— 一个 Webhook 节点一个地址。

- **`MessageType` 用 `string` 而不是枚举**(顺带定案):`Length=64` 非空。理由:(1) 它是给**进程外**消费方看的线上契约,枚举只会在边界上多一次名字↔数值的往返;(2) 内核以 NuGet 分发,消费者要发**自己的**消息类型,枚举是封闭的、消费者加不了成员而不 fork —— 这与本仓第一原则(可替换性)直接冲突;(3) Task 2 选枚举的理由是「对端 `WfNode.Type` 本来就是枚举」,这里根本没有那个对端,理由不成立。内核已知的取值以 `public const string` 挂在 `WfOutboxStore` 上,**不建枚举**。入队时 `Trim()` 并拒绝空白/含 `':'`(它是 key 分隔符)。

- **影响到谁**:Task 6(只传 `messageType` 常量与正文)、消费者任务(去重靠 `MessageKey`)、消费者产品文档。

#### D3(硬问题 ③)payload:**存全文**,`PayloadJson` 走 `StaticConfig.CodeFirst_BigString`,可空,**不截断、不加 `PayloadHash`**

- **决策**:`PayloadJson`(`string?`,`ColumnDataType = StaticConfig.CodeFirst_BigString`,可空)。**不建 `PayloadHash`** 列。

- **备选**:(a) 照抄 Task 4,只存 512 摘要 + SHA-256;(b) 全文 + hash 双写;(c) 全文但用 `Length=4000` 的普通列。

- **选它的独立理由**(**不引用「因为对称」,也不引用 Task 4 的结论**):
  1. **outbox 没有第二个正文来源。** 消费方在**另一个进程、另一台机器、可能在崩溃很久之后**才读这一行。handler 的输出只在派发短事务期间存在于内存里,而 Task 4 已经明确 attempt 表**不存正文**。若 outbox 也只存 hash,消费方拿到的是「一个键 + 一个校验和 + 无正文」——**它没有任何东西可以发出去**。一个存不下待发正文的 outbox 不是 outbox,是一张日志表。这条理由与 Task 4 无关,单独成立。
  2. **两张表回答的是不同问题。** attempt 是给**人**看的取证记录,问题是「发生了什么」,摘要 + hash 足以回答。outbox 是给**机器**读的传输记录,问题是「我该发什么出去」,只有正文能回答。同一句 §6.2「输出正文不直接进入日志」约束的是**日志/审计**面,outbox 不是审计面。
  3. **Task 4 拒绝 BigString 的核心论据在这里不成立。** 那条论据是「attempt 是 append-only、**永不删除**的表,把正文塞进去等于让消费者永久承担存储与 PII 泄漏面」。outbox 行**会被消费并进入带 `CompletedAtUtc` 的终态** —— 那正是保留期清理作业需要的钩子。生命周期不同,结论就不该相同。
  4. **仓库的既有取舍恰恰是相反方向的**(必须纠正协调者转述里的一个前提):本仓**并没有**「不用 `CodeFirst_BigString`」的全局取舍。Workflow 包内就有 5 处在用 —— `WfDefinitionVersion.ModelJson`/`FormSchemaJson`、`WfHistory.PayloadJson`、`WfInstance.VariablesJson` 等、`WfOperationReceipt.ResultJson`;内核里还有 `SysJobLog.MessageText`/`SysExceptionLog`/`SysJob.PropsJson`。全仓禁止的是**裸 `ColumnDataType = "text"`**(非 Unicode,SqlServer 上中文变 `???`,nightly #25;`SysNotice.cs:47` 的注释逐字记着这条)。Task 4 的「不用 BigString」是**只对 attempt 一张表**的局部决定,不是仓库取舍。`PayloadJson` 用 BigString 是走在既有大路上,不是例外。
  5. **不加 `PayloadHash`**:正文在手,hash 可随时算,是纯冗余的第二个必须保持一致的写入点。加它的唯一时机是将来把正文挪到库外(对象存储)、需要一个完整性校验时 —— 那时它是可空列 `ADD COLUMN`,四库都接受(`WfHistory.RequestId` 先例)。

- **长度上限与超长处理**:`CodeFirst_BigString` → SqlServer `nvarchar(max)` / MySQL `longtext` / PostgreSQL `text` / SQLite `TEXT`,**四库都没有实际上限**,因此**不设 C# 侧截断**。这一条与 Task 4 的截断决定**方向相反且必须如此**:截断一段 JSON 会产出**语法非法的 JSON**,消费方拿到的不是「短一点的消息」而是「发不出去的垃圾」——用损坏消息去换存储是纯亏。`LastError` 是另一回事,见下。
- **PII/密钥**:脱敏责任在**生产者**(dispatcher 决定什么进消息),不在本表;本表提供的是可清理的生命周期(终态 + `CompletedAtUtc`)。这一点写进实体注释。

#### D4(硬问题 ④)实际派发/消费逻辑:**本轮不做**

- **决策**:本 Task 只交付 **实体 + 表 + `WfOutboxStore.EnqueueAsync`(按 `MessageKey` 幂等的 ensure-insert)**。**不写** `ClaimAsync` / `MarkDispatchedAsync` / 退避计算 / 后台扫描 job / `IAdminJob` 注册 / 任何 HTTP 投递。

- **备选**:(a) 顺带把领取 + 标记完成两个方法做了;(b) 连后台消费 job 一起做。

- **选它的理由**:
  1. **Task 6 需要 outbox 的全部,就是「在回写短事务里入队」这一件事**(§4.6 步骤 5:「短事务用 fence/CAS 原子保存 attempt、proposal、变量、历史、outbox」)。`EnqueueAsync` 精确覆盖它,一分不多。做多了不会让 Task 6 更快,只会多出 Task 6 用不上的表面。
  2. **本 Task 的验收口径「写得进去、状态可查询」已经被完整满足**:入队后行可读回、`Status`/`AttemptCount`/`AvailableAtUtc` 全部可查询(`db.Queryable<WfOutbox>()` 即可,**不需要为「可查询」新写任何方法**)。
  3. **多做会造成返工,而且是有具体形状的返工**:消费者的领取签名取决于「消费方是谁」——是进程内 `IWorkflowNotifier`?是 HTTP 投递?是 `IAdminJob` 扫描?这些都由 Task 7/8(Webhook handler)与更后面的任务决定。现在写一个零调用方的 `ClaimAsync`,只能凭猜测定形状,而它**没有任何测试能证明它是对的**(没有消费者去驱动状态流转),等于把死代码 + 空转测试一起塞进来。这正是台账把决定权留给 plan 的原因。
  4. 与 Task 3/Task 4 的先例一致:两者都只交付「本里程碑下一个任务会立刻用到的那一个方法」,把接线留给 Task 6。
- **诚实代价**:`Dispatching`/`Dispatched`/`Failed` 三个状态、`LastError`/`CompletedAtUtc`/`AttemptCount > 0` 本轮**零写入点**,只保证「列存在、能读回」。这与 Task 3 的 8 个「建表期预留」列是同一模式,不为它们硬凑测试(见 §6)。

#### D5 字段清单(列名 / 类型 / 可空 / 长度 / 注释要点)

表 `wf_outbox`,`TableDescription = "可靠派发外发信箱"`。列顺序照 §6.3。

| 列名 | CLR 类型 | 可空 | 长度/类型 | 注释要点 |
|---|---|---|---|---|
| `ExecutionId` | `long` | 否 | — | 所属 `wf_node_execution.Id`。本仓无 DB 外键先例。 |
| `MessageType` | `string` | 否 | `Length = 64` | 给进程外消费方的消息契约名;**刻意是 string 不是枚举**(D2);内核已知取值见 `WfOutboxStore` 常量;不得含 `':'`。 |
| `MessageKey` | `string` | 否 | `Length = 128` | `{ExecutionKey}:{MessageType}`;唯一索引建在本列;**消费方去重就靠它**;天花板「一个 (execution,type) 一条消息」+ 追加 discriminator 的升级路径。 |
| `PayloadJson` | `string?` | 是 | `ColumnDataType = StaticConfig.CodeFirst_BigString` | 待投递正文全文,**不截断**(截断 JSON = 损坏消息);脱敏责任在生产者;禁止改成裸 `"text"`(SqlServer 非 Unicode,中文变 `???`)。 |
| `Status` | `WfOutboxStatus` | 否 | — | 初始化器 `= WfOutboxStatus.Pending`;状态图见 D1。 |
| `AttemptCount` | `int` | 否 | — | 已领取次数,从 0 起;**它就是 fence** —— 消费者回写必须 `WHERE AttemptCount = @myAttemptCount`。 |
| `AvailableAtUtc` | `DateTime` | **否** | — | UTC。兼任「下次可投时刻」与「租约到期」;入队 = `nowUtc`(立即可投)。**非空是刻意的**,理由见 D1 末段。 |
| `LastError` | `string?` | 是 | `Length = 512` | 最近一次投递失败摘要。**写入方必须在 C# 侧截断到 512**(外部错误文本是 trust boundary:SqlServer/PostgreSQL 超长直接抛、MySQL 非严格模式静默截断、SQLite 照单全收 → 典型的「本机 SQLite 全绿、CI 三腿红」)。本轮零写入点,注释里把这条责任交代清楚。 |
| `CompletedAtUtc` | `DateTime?` | 是 | — | 进入 `Dispatched`/`Failed` 终态的时刻(UTC);**保留期清理作业的钩子**(D3 理由 3)。本轮零写入点。 |

**基类**:`BaseEntity`,**不是 `DataEntity`** —— 逐条沿用 execution/attempt 的先例:`DataEntity` 带 `IOrgScoped` 全局过滤器(只作用于 SELECT),而 outbox 的读写方是**没有 HTTP 请求上下文的后台 worker**,`IDataScopeContext` 为空会让扫描**静默返回 0 行**,症状伪装成「消息永远不投递」而不是报错,且在有 HTTP 上下文的集成测试里可能仍是绿的。`IsDelete` 永不置真(清理走保留期策略)。

**不带 `ScopeKey`**(与 attempt 同,理由再核一遍而不是照抄):outbox 的扫描维度是 `(Status, AvailableAtUtc)` 全局队列,投递本身不需要机构维度;需要时经 `ExecutionId` 到父行取。反规范化只会多一个必须与父行保持一致的写入点。

**时间口径**:两个业务时间列一律 UTC、列名一律带 `Utc` 后缀,值由调用方传入。**硬约束**:基类 `CreateTime`/`UpdateTime` 是 local(AOP 填的),**任何代码都不得把它们与 `*Utc` 列比较或相减**。

**`DefaultValue`**:**全表一列都不写**。理由必须逐字写进类注释:`DefaultValue` 唯一作用是让 `DbMaintenanceProvider.AddColumn` 走「先加可空列 → 回填 → 改 NOT NULL」三步序列,**`CREATE TABLE` 路径根本不读它**;本表是本 Task 新建表,没有「存量行升级」这回事。Task 1 那条「非空列必须带 `DefaultValue`」的契约管的是**加列**,不是建表 —— 不写清楚会被机械套用误判成 P1。

#### D6 索引清单与命名

- `uk_wf_outbox_message_key` — `MessageKey` 单列,`IsUnique = true`(§八 原文 `UNIQUE(MessageKey)`;命名对齐 `uk_wf_node_exec_key`/`uk_wf_receipt_identity`)。
- `idx_wf_outbox_scan` — `(Status, AvailableAtUtc)`(§八 原文;命名对齐 `idx_wf_node_exec_scan`)。
- **刻意不建**:`(ExecutionId)`(今天零查询;要按 execution 反查是排查动作,全表扫可接受)、`(MessageType)`、`(CompletedAtUtc)`。等真有查询再加 —— 与 Task 3/4 同一纪律。

#### D7 交付 API 与归属

`public static class WfOutboxStore`,放 **`Engine/`**(与 `WfNodeExecutionStore`/`WfNodeExecutionAttemptStore`/`WfHistorySequence` 同目录,**不新建 `Outbox/` 目录**,沿用 Task 2/3/4 同款决定)。`public` 而非 `internal`(全仓无 `InternalsVisibleTo`,internal 会让本轮「能入队」零直接证据;`WfIdentityHash`/`WfNodeExecutionStore` 同为 `public static`)。

```csharp
public const string MessageTypeNodeExecutionCompleted = "wf.node-execution.completed";

public static Task<WfOutbox> EnqueueAsync(
    ISqlSugarClient db,
    WfNodeExecution execution,      // MessageKey 与 ExecutionId 取自同一个对象(Task 4 同款防错配)
    string messageType,
    string? payloadJson,
    DateTime nowUtc,                // AvailableAtUtc;不在方法体里读 DateTime.UtcNow
    CancellationToken cancellationToken);
```

语义:按 `MessageKey` 幂等 ensure-insert —— 先查,存在则**原样返回既有行**(既有 payload 胜出),否则插入并返回。**不写 try/catch**(与 `WfNodeExecutionStore.EnsureAsync` 逐字同款理由:半吊子的 catch 在 PostgreSQL 上更糟,事务已 aborted `25P02`;真正的「认赢家」恢复要 savepoint,归有并发创建方的那个任务)。**事务由调用方起**(Task 6),本方法不自开事务。

**零 DI 注册;`WorkflowSetup.cs` 零改动**(实体经 `UseWorkflow` 的整程序集扫描自动进 CodeFirst,本仓**不存在**「实体类型列表」这种东西);`WorkflowReplaceabilityTests`「十件套」**仍是 10 条**。三条硬约束全部满足,无需破例说明。

---

### 2. 改动清单

**共 4 个文件**(协调者可拿 `git diff --stat` 逐条核对;多一个文件都算偏差)。

1. **新增** `backend/src/TenonAdmin.Workflow/Entities/WfOutbox.cs`
   —— `WfOutbox : BaseEntity`,`[SugarTable("wf_outbox", ...)]` + 2 条 `[SugarIndex]`,9 个业务列(D5),类注释覆盖:BaseEntity 选型、UTC 后缀硬约束、全表不写 `DefaultValue` 的理由、`AttemptCount` 即 fence 且消费者回写必须 CAS、`AvailableAtUtc` 非空的理由、正文存全文的理由与脱敏责任、`LastError` 写入方必须 C# 侧截断、`MessageKey` 天花板与升级路径、本轮零写入点的列。

2. **修改** `backend/src/TenonAdmin.Workflow/Entities/WfEnums.cs`
   —— **仅在文件末尾(现 306 行之后)追加** `public enum WfOutboxStatus { Pending=1, Dispatching=2, Dispatched=3, Failed=4 }` 及其 XML 注释(含 D1 的状态转换图、「刻意无 0 值」、「只追加不重排」)。**不改动现有任何一行。**

3. **新增** `backend/src/TenonAdmin.Workflow/Engine/WfOutboxStore.cs`
   —— `public static class WfOutboxStore`,1 个 `const string` 消息类型常量 + `EnqueueAsync` + 一个私有 `NormalizeMessageType`。类注释覆盖:public 而非 internal 的理由、零 DI、事务由调用方起、不写 try/catch 的理由、签名里没有 `messageKey` 形参是刻意的。

4. **新增** `backend/tests/TenonAdmin.Tests/WfOutboxTests.cs`
   —— 7 条 `[Fact]`(见 §4)+ 脚手架(`NewExecution` / `UniqueKey` / `Open`,照抄 `WfNodeExecutionAttemptTests` 的同名私有方法形状)。

**明确不改**:`WorkflowSetup.cs`、`WorkflowReplaceabilityTests.cs`、`WfPersistenceContractTests.cs`、`WorkflowAppFactory.cs`、`WorkflowErrorCode.cs`、任何 controller/DTO、任何前端文件、`.github/workflows/**`、任何 `docs/**`(台账由协调者更新)。

---

### 3. 实现步骤

1. **`WfEnums.cs`** —— 在文件末尾追加 `WfOutboxStatus`(4 个成员 + 注释)。先做这一步,后两个文件都引用它。
2. **`WfOutbox.cs`** —— 新建实体:`using SqlSugar; using TenonAdmin.SqlSugar;`,`namespace TenonAdmin.Workflow;`(与兄弟实体同,**不是** `TenonAdmin.Workflow.Entities`)。写全 9 列 + 2 个索引 + 类注释。`Status` 用初始化器 `= WfOutboxStatus.Pending`;`MessageType`/`MessageKey` 用 `= "";`。
3. **`WfOutboxStore.cs`** —— 新建 store:
   - `MessageTypeNodeExecutionCompleted` 常量;
   - `EnqueueAsync`:`ArgumentNullException.ThrowIfNull(execution)` → `cancellationToken.ThrowIfCancellationRequested()` → `NormalizeMessageType`(`Trim()`;空白抛 `ArgumentException`;含 `':'` 抛 `ArgumentException`)→ 拼 `messageKey` → `Queryable` 按 `MessageKey` 查,命中直接返回 → 否则构造行(`ExecutionId = execution.Id`,`AvailableAtUtc = nowUtc`,`AttemptCount` 不显式赋值即 0)→ `db.Insertable(row).ExecuteCommandAsync()`(Id 由审计 AOP 填雪花)→ 返回 `row`。
4. **`WfOutboxTests.cs`** —— 7 条测试,逐条按 §4。
5. **跑闸门**(§7),确认 313 → 320 且零红。

---

### 4. 测试清单

文件 `backend/tests/TenonAdmin.Tests/WfOutboxTests.cs`。姿势逐条照 `WfNodeExecutionAttemptTests`:每个 `[Fact]` 自己 `new WorkflowAppFactory()`、`Open(f)` 拿 `ISqlSugarClient`、不经引擎。脚手架里 execution 的 `ExecutionKey` 必须 `UniqueKey()`(Guid N),否则同类多个 Fact 撞 `uk_wf_node_exec_key`。

| # | 名字 | 断言什么 | 反向变异(必须让它转红) |
|---|---|---|---|
| **T1** | `Enqueued_row_starts_pending_and_immediately_available` | 从 db **重新读回**该行后:`Status == Pending`、`AttemptCount == 0`、`AvailableAtUtc == nowUtc`(测试自己造的固定时刻,秒级容差)、`CompletedAtUtc == null`、`LastError == null`、`ExecutionId == execution.Id`、`MessageType` 等于传入值 | 把 store 里 `AvailableAtUtc = nowUtc` 改成 `nowUtc.AddMinutes(1)` → 行不再立即可投,断言转红。**另一个**:把初始 `Status` 写成 `Dispatching` → 转红。 |
| **T2** | `Message_key_is_the_execution_key_joined_with_the_message_type` | (a) `row.MessageKey` 等于测试里**手写拼接**的 `execution.ExecutionKey + ":" + messageType`(**不许调 store 的任何方法算期望值**,那是同义反复);(b) 同一 execution 换一个 `messageType` 再入队 → 返回**新行**(`Id` 不同),表内该 `ExecutionId` 下共 **2** 行 | 把 `MessageKey` 改成只用 `ExecutionKey`(丢掉 type 段)→ 第二次入队命中 ensure 分支返回第一行、count 仍为 1 → (b) 转红。**这是 D2「第二种消息被静默吞掉」那条论据的守门测试。** |
| **T3** | `Enqueue_is_idempotent_by_message_key` | 同 execution + 同 messageType 但**不同 payload** 连调两次 → 两次返回的 `Id` 相同;表内 1 行;读回的 `PayloadJson` 是**第一次**那份(钉死「先写者胜」) | 把 ensure-insert 换成裸 `Insertable` → 唯一索引抛异常(测试直接红);若同时误删唯一索引 → 变成 2 行,行数断言转红。两种退化都被覆盖。 |
| **T4** | `Duplicate_message_key_is_rejected_by_the_unique_index` | **绕过 store**,直接 `db.Insertable` 两行同 `MessageKey`(不同 `ExecutionId`)→ 第二次抛;随后断言表内该 key **只有 1 行** | 从 `[SugarIndex("uk_wf_outbox_message_key", ...)]` 拿掉 `IsUnique = true` → 不抛且 count == 2 → 转红。T3 单独抓不到这个变异(ensure 分支会提前返回),所以这条必须存在(先例:attempt #3)。 |
| **T5** | `Payload_body_survives_a_round_trip_intact` | payload 用一段 **含中文、约 32 KB** 的 JSON 文本;从 db 重新读回后与原串 `Assert.Equal`(**整串相等,不是长度相等**) | 主变异(四库皆红):store 里漏赋 `PayloadJson = payloadJson` → 读回 null → 转红。方言变异(mysql/postgres/sqlserver 腿红,**SQLite 腿看不见**,见 §6):把列从 `CodeFirst_BigString` 改成 `Length = 512` → 超长在 SqlServer/PG 直接抛、MySQL 静默截断 → 转红。第三种:改成裸 `ColumnDataType = "text"` → SqlServer 上中文变 `???` → 转红(仅 sqlserver 腿,归 nightly)。 |
| **T6** | `An_enqueue_inside_a_rolled_back_transaction_leaves_no_trace` | 在 `db.Ado.UseTranAsync` 里 `EnqueueAsync` 后主动抛 → `tran.IsSuccess == false`,且表内该 `ExecutionId` 下 **0 行** | 让 `EnqueueAsync` 内部自开事务(`UseTranAsync` 包住 insert)→ 内层提交后外层回滚不掉它 → 行残留 → 转红。**这是本轮对 §4.6「与 execution 结果同一短事务提交」唯一能拿到的直接证据。** |
| **T7** | `Two_executions_can_enqueue_the_same_message_type` | 两个不同 execution(各自 `UniqueKey()`)用**同一** `messageType` 入队 → 2 行,`MessageKey` 互不相同,各自 `ExecutionId` 正确 | 把 `MessageKey` 改成只用 `messageType`(丢掉 execution 段)→ 第二次命中 ensure 返回第一行、count == 1 → 转红。与 T2 是对偶的一对(先例:attempt #3/#4 那一对)。 |

合计 **7 条**,`313 → 320`。

#### 禁写清单(这些形式的空转测试一条都不许出现)

- 只断 `Assert.NotNull(row)` 或 `Assert.True(row.Id != 0)` 就收工 —— 任何返回一个新对象的实现都能过。
- **把刚写的对象和它自己比**:`Assert.Equal(row.MessageKey, row.MessageKey)`、或断言 `EnqueueAsync` 的返回值而**不从 db 重新读回**。凡是要证明「落库了什么」的断言,必须 `db.Queryable<WfOutbox>()` 重新读回后再比。
- **期望值由被测代码算出**:`Assert.Equal(WfOutboxStore.SomeCompose(...), row.MessageKey)` —— 同义反复,store 怎么错测试就怎么错。期望值必须在测试里手写字面拼接。
- 用 `Assert.True(Enum.IsDefined(row.Status))` 代替断言具体枚举值。
- 唯一索引测试只 `catch` 到异常就断言成功、**不断表内行数** —— 漏掉「索引没建成但插入因别的原因抛了」这一支(attempt #3 的先例明确要求两者都断)。
- payload 往返测试用 `"{}"`、`"ok"` 这类**又短又无中文**的串,却声称验证了正文存储。
- 为本轮零写入点的列(`LastError`/`CompletedAtUtc`/`Dispatching`/`Dispatched`/`Failed`)编造「手动 UPDATE 一下再读回」的测试 —— 那测的是 SqlSugar 不是本 Task,见 §6。
- 任何 `Assert.True(true)`、被注释掉的断言、只有 Arrange 没有 Assert 的 `[Fact]`。

---

### 5. 陷阱(按 exec 最可能踩的顺序)

1. **把 Task 1 的「非空列必须带 `DefaultValue`」套到建表上。** 本表是新建表,`CREATE TABLE` 路径根本不读 `DefaultValue`。**全表一列都不写**,并且必须把这条理由写进类注释 —— 不写,下一个机械审查者会把它当 P1 报上来(Task 3/4 都为此专门写了注释)。
2. **在 `EnqueueAsync` 方法体里读 `DateTime.UtcNow`。** `nowUtc` 必须是形参(测试要能固定时刻;本仓「时间由调用方传入」的既有姿势,见 `ClaimAsync`/`AppendAsync`)。
3. **`SetColumns` 里内联 `DateTime` 表达式。** 本轮 store 没有 UPDATE,但**测试**里若要造场景(改 `AvailableAtUtc`),必须先把时刻落到局部变量再用 —— zh-CN 下 SqlSugar 会把内联表达式格式化成含「下午」的字面量,直接炸 SQL。
4. **`PayloadJson` 写成裸 `ColumnDataType = "text"`。** 必须是 `StaticConfig.CodeFirst_BigString`(SqlServer → `nvarchar(max)`);裸 `"text"` 在 SqlServer 上非 Unicode,中文变 `???`(nightly #25,`SysNotice.cs:47` 有逐字警告)。
5. **给 `PayloadJson` 加 C# 侧截断。** 不许 —— 截断 JSON = 损坏消息(D3)。要截断的是 `LastError`,而它本轮没有写入点。
6. **测试脚手架里 execution 的 `ExecutionKey` 写死成常量。** 同一测试类的多个 `[Fact]` 共用一个库时会撞 `uk_wf_node_exec_key`,Task 3/4 已踩过,必须每次 `Guid.NewGuid().ToString("N")`。
7. **把 `AvailableAtUtc` 做成可空**(照抄 execution 的 `NextRetryAtUtc`)。它非空是 D1 的定案,可空会让未来的领取谓词掉进 SQL 三值逻辑(Task 3 为此写了 #12b)。
8. **顺手加 `Fence` 列 / `LeaseOwner` 列。** D1 明确拒绝;`AttemptCount` 就是 fence,这条要在实体注释里写死,否则下一个任务会去造重复机制。
9. **`MessageType` 做成枚举。** D2 明确拒绝(内核可替换性)。
10. **新建 `Outbox/` 目录 / 把实体放进 `TenonAdmin.Workflow.Entities` 命名空间。** 兄弟实体全是 `namespace TenonAdmin.Workflow;`(物理目录 `Entities/`,命名空间不带 `.Entities`);store 全在 `Engine/`。
11. **给 `EnqueueAsync` 写 try/catch 捕唯一冲突。** 本轮不做「认赢家」恢复(PG 需要 savepoint),原样抛 —— 与 `WfNodeExecutionStore.EnsureAsync`/`WfNodeExecutionAttemptStore.AppendAsync` 一致。
12. **手动给行赋 `Id`。** 雪花由审计 AOP 在 `Insertable` 时填。
13. **动 `WorkflowSetup.cs`。** 零改动;实体靠整程序集扫描进 CodeFirst,**本仓不存在实体类型列表**,别去找也别去建。

---

### 6. 射程限制(本 Task 诚实测不到的东西)

- **R1 领取 / 重投 / 退避 / 终态四条状态边零覆盖。** 本轮没有消费者(D4),`Pending → Dispatching → Dispatched|Failed` 与超时重领全部无实现、无测试。**不为它们硬凑测试**(手动 UPDATE 再读回测的是 SqlSugar,不是本 Task 的产出)。兜底:消费者任务 —— 届时必须把 D1 的状态图与「回写 CAS `AttemptCount`」一并落成测试。
- **R2 `LastError` / `CompletedAtUtc` / `AttemptCount > 0` 零写入点。** 只保证列建出来、可读回,与 Task 3 的 8 个「建表期预留」列同一模式。特别地,**`LastError` 的 C# 侧截断本轮无实现也无测试** —— 责任写在列注释里交给消费者任务;那一轮必须补一条「600 字错误文本 → 落库 512」的测试(形状照 attempt #5)。
- **R3 「与 execution 结果同一短事务提交」(§4.6)只证到一半。** 本轮能证的是 T6「回滚不留痕 + store 不自开事务」;真正的「attempt + 结果回写 + outbox 同一个事务一起提交」要等 **Task 6** 有那个回写短事务才可测。
- **R4 进程外消费方的去重零覆盖。** 本仓没有进程外消费者,能证的只有「`MessageKey` 构成稳定(T2/T7)且唯一(T4)」。真实的「消费方收到两次、靠 key 丢掉一次」要到有真实 adapter 的那一轮。
- **R5 长正文的方言行为在本机腿看不见。** SQLite 不强制列长度,所以 T5 的「`BigString` → `Length=512`」变异在本机与 CI 的 sqlite 腿**照绿**,只有 mysql/postgres 腿会红;「中文变 `???`」只有 sqlserver 腿会红。而 **`WfOutboxTests` 不在 sqlserver 腿 push/PR 的 `TEST_FILTER` 子集里**(该子集当前只含 `WfPersistenceContractTests` 一个 Wf 项),所以中文往返的证据落在 **nightly**。**不建议本 Task 去改 `TEST_FILTER`**(那会加长本已 40–60 分钟的 sqlserver 腿,且是 CI 策略变更不是本 Task 范围);正确去处是把「BigString 正文四库往返」并入 **Task 9 的四库契约套件**(`WfPersistenceContractTests` 已在子集内,加在那里零成本拿到 sqlserver 腿覆盖)。**挂账给 Task 9。**
- **R6 `MessageKey` 的天花板不可测。** 「同一 execution 发两条同类型消息」在 M3a-1 内不可达(一个 Webhook 节点一个地址),写不出能转红的测试;只作为注释里的已知边界与升级路径(D2)。
- **R7 并发入队零覆盖。** 本轮没有并发创建方(与 Task 3 `EnsureAsync` 同);「两个 worker 同时入队同一 key」的认赢家恢复(PG savepoint)归有并发创建方的那个任务。

---

### 7. 闸门

exec 完必须依次跑,两条都要绿:

```bash
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test  backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"
```

- 第 1 条:**0 错误**(警告数不得增加)。
- 第 2 条:过滤器写法**一个字都不许改**;基线 313 条全绿,本 Task 后应为 **320 条全绿**(+7,只增不减,一条不许删改既有断言)。

**前端闸门:不需要,明确判断如下。** 本 Task 只新增一张内核内部表、一个 `public static` store 与一个枚举;**零 controller、零 DTO、零路由、零 `TenonAdminOptions` 变更**,因此 `/openapi/v1.json` 无差异,`web/src/api/schema.d.ts` 与 `web-react/src/api/schema.d.ts` 均不会有输出变化,`scripts/check-contract-drift.mjs` 不会报漂移。不跑 `npm run build|lint|typecheck|gen:api`。

**额外自查(不是命令,是 exec 交付前必须目视确认的三条)**:
1. `git diff --stat` 恰好 **4 个文件**(3 新增 1 修改),`WfEnums.cs` 的 diff 是**纯追加**(`+` 行全部在文件末尾,零 `-` 行);
2. `WorkflowSetup.cs` **零改动**,`WorkflowReplaceabilityTests` 仍是 **10 条**;
3. 新增的 4 个文件里 **零** `TODO`/`test.skip`/`Assert.True(true)`/被注释掉的断言,`WfOutbox.cs` 里 **零** `DefaultValue`。

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

### Task 5 review(Round 19,Opus 自审 + 11 变异点)

> 方法:变异测试,每点走完「Edit → grep 确认落盘 → 跑全量 → 单文件 `git checkout --` 复原 → `git diff --stat` 空」。协调者在 Round 19 的三次唤醒中**分别观察到 `WfOutbox.cs`、`WfOutboxStore.cs` 处于变异态、最终工作树干净**,与报告自述吻合,变异确实发生过且已逐个复原。

**9 个 Plan 预期变异全部按预期转红**:M1 丢 type 段 → T2+T7 红;M2 丢 execution 段 → T7+T2 红;M3 `AvailableAtUtc` 加 1 分钟 → T1 红;M4 初始 `Status` 改 `Dispatching` → T1 红;M5 拿掉 `IsUnique` → **仅 T4 红(T3 确实抓不到,证实 Plan「T4 必须独立存在」不是冗余)**;M6 删 ensure 分支 → T3 红;M7 漏赋 `PayloadJson` → T5+T3 红;M9 store 自开 `UseTranAsync` → T6 红**且红在「表内残留 1 行」那条断言上而非靠异常侥幸**。另 M1b(自设:key 算对存对、只把 ensure 查询改按 `ExecutionId` 命中,精确复刻 D2「第二种消息被静默吞掉」)→ T2 的 (b) 腿独立转红,**D2 最重要那条论据有真守门**。

**M8 实测确认 R5 的射程判断为真**:`CodeFirst_BigString` 改成 `Length = 512` 后本机 **320/320 照绿**——SQLite 不强制列长度,32KB 正文塞进 512 列照样往返。子代理另查实 `.github/workflows/backend-ci.yml:147` 的 sqlserver push/PR `TEST_FILTER` 里 Wf 相关只有 `WfPersistenceContractTests`,`WfOutboxTests` 确实不在子集内;而该文件已有同型的 `Result_json_round_trips_chinese_and_long_payloads`(`WfOperationReceipt.ResultJson`)可抄 → **挂账 Task 9 的去处成立,未为它改 CI、未硬凑测试**。

**Plan §5 的 13 条陷阱一条未踩**(逐条 grep 有据);**§6 的 R1–R7 如实、无偷偷补上也无夸大**;实体/store/枚举的类注释把 Plan 要求的每条理由都写到位(尤其「`AttemptCount` 即 fence,别再造 `Fence` 列」与「不写 `DefaultValue` 的理由」),**不会导致下一轮去造重复机制**。7 条测试**逐条被证明有鉴别力,无一空转**,零写入点的列没有被编造假覆盖。

- [ ] **P1-1｜`NormalizeMessageType` 的三条校验(`Trim()`/拒空白/拒含 `':'`)零守门测试** —— 证据是 **M11**:把整个函数体退化成 `return messageType;`,grep 确认落盘后全量 **320/320 照绿**,**整段领域校验可以原地蒸发而无人发觉**。这是 Task 4 那类「断言缺失」的同款形状且更彻底(Task 4 至少还有别的测试路过那段代码,这里是零测试路过)。**不能豁免的三条理由**:①它是 **trust boundary**——D2 定案 `MessageType` 用 `string` 而非枚举**就是为了让消费者发自己的类型**,该形参将来由内核外代码提供;②它保护的是 `MessageKey` 的结构不变量(`':'` 是分隔符,放进含 `':'`/空白的类型就破坏 D2 论据 4「定宽 64 hex 前缀 + 无歧义分隔符」,而那是消费方去重的地基);③Plan §6 的 R1–R7 **没有把它列为射程限制**,即它不是「诚实测不到」而是**漏了**。**修法**:加 2 条测试(别为覆盖率凑 3 条)——`Message_type_containing_the_key_separator_is_rejected`(`Assert.ThrowsAsync<ArgumentException>` **并顺带断言表内该 `ExecutionId` 下 0 行**,证明抛在写库之前,同 T4「两者都断」的纪律);`Message_type_is_trimmed_before_it_joins_the_key`(传前后带空格的类型,**从库读回**后断言 `MessageKey` 无多余空格)。协调者独立核实:测试文件 `ThrowsAsync|ArgumentException` **0 命中**,属实。
- [ ] **P2-1｜`MessageKey` 从未被从库读回验证(Plan §4 禁写清单第 2 条的字面违反)** —— 这正是协调者在 Round 18 标记、要求 review「设计能区分『算对但落库错』的变异」来判定的疑点,**判定:是真缺口**。证据是 **M10**(专门设计):让 store 把 key **算对、返回对**,只把**插库那一行**带上 `MUTANT-` 前缀 → **T2/T7 全绿**,尽管库里每一行的 key 都是错的;只有 T3 红,且红的是 SQLite `UNIQUE constraint failed` 方言异常,**与「key 存错了」毫无关系**,排查要绕一大圈。逐列盘查后 **`MessageKey` 是全表唯一一个从未被读回验证过的列**,而它恰恰是本表主契约(消费方去重就靠它)。**不评 P1 的理由**:key 的**构成**已被 M1/M2/M1b 三个方向证明有守门,落库错这类退化虽逃过 key 断言但仍有一条测试会红,不是完全盲区。**修法**:T2/T7 在现有断言之后各加一次读回(按 `Id` 查回该行),期望值仍**测试里手写字面拼接、不许调 store**;**保留**现有对返回值的断言(它证明「返回给 Task 6 调用方的对象是对的」,是另一件有价值的事),只补落库那一半。协调者独立核实:`MessageKey` 的断言全在内存对象上(`:53` `first`、`:173-175` `rowA`/`rowB`),唯一碰该列的库查询是 `:111` 的 `CountAsync`(数行数,非值读回),属实。
- [ ] **P2-2｜对外契约常量 `MessageTypeNodeExecutionCompleted` 的字面值无快照钉死** —— 该常量是**发给进程外消费方的线上契约**(D2 明文),改它等于破坏所有消费者的路由与去重;但 T1 的断言两侧都是同一个常量(`:36`),把常量值改成任何别的串测试照绿。同型的对外契约 `ExecutionKey` 在本仓**是有**快照测试钉死的(`WfExecutionKeyTests`,语义契约写着「那条红了是撤回改动,不是改期望值」),这里缺同款保护。**修法**:一行把字面值写死的 `Assert.Equal` 加进 T1 或单列一条,注释写明「已发包的线上契约,红了是撤回改动」。**不建议**为此新建测试类。
- [ ] **P3-1(可选,且本轮明确不做)｜`LastError` 的 512 是裸字面量,无常量托底** —— attempt 的先例是列宽与 C# 侧截断共用 `WfNodeExecutionAttemptStore.SummaryMaxLength` 一个 token;`WfOutbox.LastError` 用裸 `Length = 512`,而注释要求「写入方必须 C# 侧截断到 512」。**本轮零写入点,现在加常量是为不存在的调用方服务(YAGNI),不改**;但**消费者任务落 `LastError` 写入点时必须同步补 `WfOutboxStore.LastErrorMaxLength` 并把列宽换成它**,否则 512 会在两处各写一遍。已挂账,见下方射程限制。
- [ ] **P3-2(可选,不做)｜`idx_wf_outbox_scan` 无测试** —— 纯性能索引,无可观测行为,写不出有鉴别力的测试(同 `idx_wf_node_exec_scan`,本仓无为扫描索引写测试的先例)。**如实记为射程限制,不硬凑。**

**Task 5 新增射程限制(并入台账,不许后续拿来当「测过了」)**:①`EnqueueAsync` 的参数守卫(`ArgumentNullException.ThrowIfNull` / `ThrowIfCancellationRequested`)零覆盖,与全仓姿势一致,**不补**——但**必须与 P1-1 区分**:那两条是框架级一行守卫,`NormalizeMessageType` 是本 Task 自己写的领域校验,不能一并豁免;②`idx_wf_outbox_scan` 无行为可观测;③`MessageKey` 落库值本轮无读回证据(P2-1 修完即消除);④R5 未变,`PayloadJson` 的 BigString 四库往返仍只有 sqlite 腿证据,**挂账 Task 9** 并入 `WfPersistenceContractTests`;⑤**挂账消费者任务**:落 `LastError` 写入点时补 `LastErrorMaxLength` 常量 + 一条「600 字错误文本 → 落库 512」的测试(形状照 attempt #5)。

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
