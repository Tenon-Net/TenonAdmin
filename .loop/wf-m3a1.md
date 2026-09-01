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

- 轮次: 4
- max: 70
- 当前任务: Task 1 **已收口**(NodeVisitId 贯穿 + wf_history 补字段)
- 当前阶段: 修 Findings 完成 → **已勾选**
- 上一轮: Round 4 — Task 1 **修 Findings**(executor/sonnet)。commit `98c2837`,3 文件 +17/-9,**产品逻辑零改动**(只改注释与测试断言),精确对口两条 P2 无外溢。协调者**独立复核**:①自己另做一次变异(把 `NextAsync` 的 `+ 1` 改成 `+ 3`,与子代理用的 `+ 2` 不同以免抄结论),`git diff --stat` 确认落盘,`--filter "~WfHistoryIdentityTests"` → **失败 3 / 通过 5**,其中 `Sequence_starts_at_one_strictly_increases_and_never_repeats` 在 :49 转红——**新断言确实钉住了间隙**;②`git checkout` 单文件复原,`git diff` 空;③重跑两条闸门:build **0 错误**,test **279/279 通过、失败 0**。0×P1、0×未修 P2、闸门已跑 → **勾选 Task 1**。
- 下一步: Round 5 — Task 2 **plan**(`IWorkflowNodeHandler` SPI + `WfNodeExecutionContext`/`WfNodeExecutionResult` 类型 + `FakeNodeHandler` 参考实现)。协调者派 `Agent(model="opus"`,不传 `subagent_type`),prompt 塞入 Task 2 目标、`## 语义契约` 全文、设计文档章节(`elsa3-slickflow-ai-reference-2026-08-23.md` §4.5/§4.7 的 handler 硬约束、`workflow-database-design-review-2026-08-24.md` §4.5)、以及**语义契约里待 Task 2 定案的那一项**(`TerminalFailure` 与 `Cancelled` 是否合并)。明确:**本 Task 不接入引擎,纯类型/接口定义**,不写 dispatcher、不建任何表。

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
| Handler 结果枚举 | `Succeeded` / `RetryableFailure` / `ManualFallback` / `TerminalFailure`(或 `Cancelled`,待 Task 2 plan 定案是否合并)——handler **不得**直接推进 token、写任务状态、自开数据库事务(AI 基石 §4.5/§4.7 硬约束,即使本阶段不做 AI,也要把 Seam 立对,否则 M3b 接入时要返工) |
| execution 状态机 | `Pending → Running → Succeeded`;失败可进 `RetryScheduled → Running`;`ManualFallback`;`Cancelled`/`Failed`(AI 基石 §4.6,Task 3 plan 落地成具体枚举值) |
| 事务边界 | 短事务领取(CAS lease/fence)→ 事务外调用 handler → 短事务落 attempt/result/token 推进/outbox,**不得**让远程调用发生在数据库事务内(AI 基石 §4.6 步骤 1–5,验收线 §4.8 明文要求) |
| lease/fence | 待 Task 3 plan 定案具体字段与领取 SQL 形状(参照 M2c `WfOperationReceipt`/`WfInstance.Version` 的 CAS 先例,不是发明新范式) |
| attempt 记录 | append-only,重试**不覆盖**旧 attempt(AI 基石 §4.5) |
| outbox | 结果提交后可靠触发通知/外部副作用;短事务与 execution 结果同提交(AI 基石 §4.6) |
| Webhook 超时/重试分类 | 待 Task 8 plan 定案(哪些 HTTP 状态码/异常归 `RetryableFailure`,哪些归 `TerminalFailure`,是否有的场景该转 `ManualFallback`) |
| 与人工任务的关系 | `ManualFallback` 时如何创建人工 `wf_task`——待 Task 6(dispatcher)plan 定案,复用 `EnterNodeOp.CreateTaskAsync` 还是新路径 |
| 范围外 | 不建 AI Decision(`wf_ai_decision`、provider adapter、policy、shadow mode——留给 M3b);不建并行网关(`ParentTokenId`/`ForkId`/join 表);不新增审批动词;不 port React 工作流页;不抽 web/web-react 共享层 |

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段的 Agent 重写,协调者转写进本节)

### Task 1 决策点定案

1. **`NodeVisitId` 类型与生成方式** — `long?`（可空雪花），五张表一律 `[SugarColumn(IsNullable = true)]`，不给 `DefaultValue`。生成点在 `EnterNodeOp.cs:37-42`，紧跟 `ClaimTokenAsync` 之后、与 `NodeId` **同一条 UPDATE** 落库：
   ```csharp
   await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);   // 既有第 37 行
   ctx.Token.NodeVisitId = ctx.IdGenerator.NextId();                      // 新增
   ctx.Token.NodeId = Node.Id;
   await ctx.Db.Updateable(ctx.Token)
       .UpdateColumns(t => new { t.NodeId, t.NodeVisitId, t.UpdateTime, t.UpdateUserId })
       .ExecuteCommandAsync();
   ```
   `EnterNodeOp.ExecuteAsync` 是 token 进节点的唯一入口，「每次进新节点生成一次」自动成立；「停留期间不变」也自动成立——停留期间的写路径（`CompleteTaskOp.cs:110` 未满票分支的 `ClaimTokenAsync`、转办、催办）不经过本 Op，只推 `Version`。发号器用内核既有 `IIdGenerator`（`TenonAdmin.Core/Ids/IIdGenerator.cs:12`，`SqlSugarSetup.cs:54` `TryAddSingleton`），不新造发号机制。
   下游取值：`WfTask`/`WfCc` 在 `EnterNodeOp.cs:258` / `:144` 从 `ctx.Token.NodeVisitId` 拷；`WfHisTask` 三个插入点（`CompleteTaskOp.cs:69`、`ReturnTaskOp.cs:70`、`ReassignTaskOpBase.cs:122`）从 **`Task.NodeVisitId`** 拷（读任务行才准确表达「这件待办是哪一次访问建的」）；`WfHistory` 在 `AppendHistoryAsync` 里从 `ctx.Token.NodeVisitId` 拷，零调用点改动。
   向后兼容：全部可空，旧行读 `null`（评审 §九 #5）。老 token 下次进节点自然补上；永远停在某节点的老 token 保持 `null`——这是语义不是缺陷。**不写回填 HostedService**（与 `WfCompletedTimeBackfill` 不同：完结时间能从事件推出来，节点访问身份推不出来）。

2. **`wf_history.Sequence` 并发写入** — 定案：**`wf_instance` 新增计数列 `HistorySeq`（`int`，NOT NULL，`DefaultValue="0"`），用「原子相对递增 + 读回」分配，逐行分配、无间隙、无重试循环**。不用 `MAX(Sequence)+1`（必撞号），不用读-then-CAS（MySQL RR 下事务内 CAS 失败重读仍是旧快照 → 活锁）。
   ```csharp
   // Engine/WfHistorySequence.cs（新文件）
   internal static async Task<int> NextAsync(ISqlSugarClient db, long instanceId)
   {
       await db.Updateable<WfInstance>()
           .SetColumns(i => new WfInstance { HistorySeq = i.HistorySeq + 1 })  // SET HistorySeq = HistorySeq + 1
           .Where(i => i.Id == instanceId)
           .ExecuteCommandAsync();
       return await db.Queryable<WfInstance>()
           .ClearFilter<IOrgScoped>()
           .Where(i => i.Id == instanceId)
           .Select(i => i.HistorySeq)
           .FirstAsync();
   }
   ```
   四库成立论证：`SET col = col + 1` 四库通用（仓内先例 `Services/Jobs/JobExecutor.cs:334`），四库都在该 UPDATE 上取行排他锁持有到提交；MySQL RR 下 UPDATE 走 current read，读回 SELECT 读到本事务自己的写。不用任何方言特有语法（不用 PG `RETURNING`、不用 SqlServer `SET @v = col = col+1`）。
   **必须在事务内**才成立。`AppendHistoryAsync` 天然在引擎「一条 Cmd 一个事务」里；绕开 ctx 的 4 个路径今天是裸调用，统一走同文件的 `WfHistorySequence.WriteSystemRowAsync(db, row, ct)`（自带只包「分配序号 + 插一行」的短事务）。
   重复/跳号：**不允许重复**（原子递增保证）；**允许跳号**（事务回滚会连递增一起回滚，所以实际不跳；真正断裂只有「升级前旧行全为 0、新行从 1 起」这一次）。

3. **`ActorType`/`ActorUserId` 口径** — 新增枚举 `WfHistoryActorType`（放 `Entities/WfEnums.cs`，**不复用**已被 `wf_task_actor` 占用的 `WfActorType`）：
   ```csharp
   public enum WfHistoryActorType { Unknown = 0, Human = 1, System = 2, Timeout = 3, Worker = 4, Ai = 5 }
   ```
   `Unknown = 0` 是给升级后旧行的；`Worker`/`Ai` 是评审 §4.6 点名的值，现零写入点先占位（§九 #6：只追加、不重排）。**驳回 `Reminder`**：催办是真实用户点的按钮，「催办」由 `EventType = TaskUrged` 表达，actor 维度回答「谁」不是「干了什么」。
   `AppendHistoryAsync` **签名不改**（20 个调用点零改动，照抄 M2c Task 6）。改为在 `WfExecutionContext` 上加三个 `required init` 属性，与 `WfExecutionContext.cs:42` 的 `RequestId` 同型：
   ```csharp
   public required WfHistoryActorType ActorType { get; init; }
   public required long? ActorUserId { get; init; }
   public required IIdGenerator IdGenerator { get; init; }
   ```
   八个 `BeginXxxAsync` 填法：`Start` → `Human`/`cmd.StarterUserId`；`Complete`/`Transfer`/`Delegate`/`Return` → `Human`/`cmd.UserId`；`Cancel`/`Resubmit` → `Human`/`cmd.CallerUserId`；`Timeout`（`BeginTimeoutAsync`，`TimeoutFireCmd` 无用户身份）→ `Timeout`/`null`。
   绕开 ctx 的 4 处：`WfTimeoutJob.cs:240`(retire)、`:280`(failed)、`:362`(remind) 一律 `Timeout`/`null`；`WfTaskService.cs:226`(催办) 填 `Human`/`callerUserId`。

4. **`PayloadVersion`** — `int`，NOT NULL，实体初始化器 `= 1`、列上 `DefaultValue="1"`。语义：读取方按 `EventType + PayloadVersion` 解释 `PayloadJson`。**没人显式写**——C# 默认值覆盖新行，`DefaultValue` 覆盖旧行。只有某个 `EventType` 的 payload 形状变了，才在那一个写入点显式抬到 2。Task 1 不动任何值。

5. **`wf_history.TokenId`** — `long?`，`AppendHistoryAsync` 从 `ctx.Token.Id` 取；4 个绕开路径从 `task.TokenId` 取（`WfTask.TokenId` 非空 long，`WfTask.cs:23`）。**不存在写不出的情况**：token 行从不物理删（只翻 `Status`）；`InstanceStarted` 那行也有值（token 在 `WorkflowEngine.cs:288` 插入、Id 已生成，ctx 在 `:296` 才构造，`:316` 才写历史）。保持可空只为旧行与将来真正的实例级事件（与 `WfHisTask.TokenId` 既有可空口径一致）。

6. **建表/迁移** — 全靠 CodeFirst（`DatabaseInitializer` 的 `InitTables` 补列），**不写迁移脚本、不写回填 HostedService**。两类列两条路：
   - **可空列**（`NodeVisitId` ×5、`WfHistory.TokenId`、`ActorUserId`）→ 单步 `ADD COLUMN ... NULL`，四库直接接受。这是 `WfInstance.CompletedTime` / `WfHistory.RequestId` 走过的路。
   - **非空带默认列**（`WfHistory.Sequence`/`ActorType`/`PayloadVersion`、`WfInstance.HistorySeq`）→ SqlSugar `DbMaintenanceProvider.AddColumn` 三步：临时翻可空 → `ADD COLUMN` 可空 → `Updateable.AS(table).Where("<col> is null")` 回填 → `UpdateColumn` 改 NOT NULL。这是 `WfInstance.Version` / `WfToken.Version` 走过的路。
   方言坑在第二类：**PG 与 SqlServer 的 `ADD COLUMN ... NOT NULL`（无 DEFAULT）在有行的表上直接被拒**（MySQL 才隐式补 0）——这四列**必须**写 `DefaultValue`，漏写就是 PG/MSSQL 升级现场炸、而空库 CI 全绿看不见。SQLite 例外：`SqliteCodeFirstEnableDefaultValue` 未开启，DDL 不出现 DEFAULT，但回填 UPDATE 照跑。

### 改动清单

产品代码（`backend/src/TenonAdmin.Workflow/`）：

| 文件 | 改什么 |
|---|---|
| `Entities/WfEnums.cs` | 新增 `WfHistoryActorType` 枚举（6 值，含 `Unknown = 0`） |
| `Entities/WfToken.cs` | 新增 `long? NodeVisitId`（主注释：每次进节点生成、停留期间不变、与 `Version` 职责不混用） |
| `Entities/WfTask.cs` | 新增 `long? NodeVisitId` |
| `Entities/WfHisTask.cs` | 新增 `long? NodeVisitId` |
| `Entities/WfCc.cs` | 新增 `long? NodeVisitId`（注明：**不改** `(InstanceId, NodeId)` 去重键） |
| `Entities/WfHistory.cs` | 新增 6 列：`long? TokenId`、`long? NodeVisitId`、`int Sequence`(`DefaultValue="0"`)、`WfHistoryActorType ActorType`(`DefaultValue="0"`)、`long? ActorUserId`、`int PayloadVersion = 1`(`DefaultValue="1"`) |
| `Entities/WfInstance.cs` | 新增 `int HistorySeq`(`DefaultValue="0"`)，历史序号分配计数器 |
| `Engine/WfHistorySequence.cs` | **新文件** `internal static`：`NextAsync(db, instanceId)`；`WriteSystemRowAsync(db, row, ct)`（给 4 个绕开 ctx 的路径，自带短事务） |
| `Engine/WfExecutionContext.cs` | 加 3 个 `required init` 属性；`AppendHistoryAsync`(:192-209) 在同一初始化器补 `TokenId`/`NodeVisitId`/`ActorType`/`ActorUserId` + `Sequence = await WfHistorySequence.NextAsync(...)` |
| `Engine/WorkflowEngine.cs` | 主构造函数追加 `IIdGenerator idGenerator`（第 4 次刻意的源码级破坏性变更，按类 `<remarks>` 既有格式补一句）；8 个 `BeginXxxAsync` 构造 ctx 时各补 3 个属性 |
| `Engine/Operations/EnterNodeOp.cs` | :39 前生成 `NodeVisitId`，:41 `UpdateColumns` 加该列；:258 建 `WfTask`、:144 建 `WfCc` 时拷 |
| `Engine/Operations/CompleteTaskOp.cs` | :69 `WfHisTask` 初始化器加 `NodeVisitId = Task.NodeVisitId` |
| `Engine/Operations/ReturnTaskOp.cs` | :70 同上 |
| `Engine/Operations/ReassignTaskOpBase.cs` | :122 同上 |
| `Jobs/WfTimeoutJob.cs` | :240 / :280 / :362 三处 `db.Insertable(new WfHistory{...})` → 走 `WfHistorySequence.WriteSystemRowAsync`，行上补 `TokenId = task.TokenId`、`NodeVisitId = task.NodeVisitId`、`ActorType = Timeout`、`ActorUserId = null` |
| `Services/WfTaskService.cs` | :226 同上，`ActorType = Human`、`ActorUserId = callerUserId` |

测试（`backend/tests/TenonAdmin.Tests/`）：

| 文件 | 改什么 |
|---|---|
| `WorkflowMultiLeaderSnapshotTests.cs` | :657 `WorkflowEngineProbe` 的 `base(...)` 补第 10 个 `null!`（**已知会红，必须一并改**） |
| `WfNodeVisitIdTests.cs` | **新文件**（7 条） |
| `WfHistoryIdentityTests.cs` | **新文件**（8 条） |

**不改**：任何 DTO（`WfHistoryItemOutput` 在 `Services/WfInstanceService.cs:358`）、任何 Controller、`WorkflowSetup.cs`、`web/`、`web-react/`、`site/`。本 Task **零 OpenAPI 变更**。

### 实现步骤

1. **枚举先行**：`WfEnums.cs` 末尾加 `WfHistoryActorType`，注释写明「只追加、不重排（评审 §九 #6）」及为什么不复用 `WfActorType`。
2. **五张表的 `NodeVisitId`**：各加 `[SugarColumn(IsNullable = true, ColumnDescription = "节点访问 Id")]`。主注释写在 `WfToken`，其余四处 `<see cref="WfToken.NodeVisitId"/>` 引过去。
3. **`WfHistory` 六列 + `WfInstance.HistorySeq`**：`Sequence`/`ActorType`/`PayloadVersion`/`HistorySeq` **必须**带 `DefaultValue`；注释指回 `WfInstance.Version` 那段三步升级序列，并写明「本轮刻意不建 `UNIQUE(InstanceId, Sequence)`」及理由。
4. **新建 `Engine/WfHistorySequence.cs`**：
   ```csharp
   internal static class WfHistorySequence
   {
       public static async Task<int> NextAsync(ISqlSugarClient db, long instanceId) { /* 见决策点 2 */ }

       /// 绕开 WfExecutionContext 的系统写入（超时 ×3、催办 ×1）专用：短事务包住「分配序号 + 插一行」。
       /// 事务不是装饰——两条裸自动提交语句之间，并发的另一次分配会让读回值撞号。
       public static async Task WriteSystemRowAsync(ISqlSugarClient db, WfHistory row, CancellationToken ct)
       {
           var tran = await db.Ado.UseTranAsync(async () =>
           {
               row.Sequence = await NextAsync(db, row.InstanceId);
               await db.Insertable(row).ExecuteCommandAsync();
           });
           if (!tran.IsSuccess) throw tran.ErrorException!;
       }
   }
   ```
5. **`WfExecutionContext`**：加三个 `required init` 属性（注释照抄 `RequestId` 那段「为什么是 required」的论证形状）；`AppendHistoryAsync` 的行初始化器补 `TokenId = Token.Id`、`NodeVisitId = Token.NodeVisitId`、`ActorType = ActorType`、`ActorUserId = ActorUserId`、`Sequence = await WfHistorySequence.NextAsync(Db, Instance.Id)`。（`PayloadVersion` 由实体初始化器给 1，这里不写。）
6. **`WorkflowEngine`**：主构造函数追加 `IIdGenerator idGenerator`；八个 `BeginXxxAsync` 的 ctx 初始化器各补 `IdGenerator`/`ActorType`/`ActorUserId`。编译器会逐个点名漏掉的——这正是 `required` 的用途。
7. **`EnterNodeOp`**：三处（token UPDATE、建 task、建 cc）按决策点 1 改。cc 去重查询（`:138-141`）**原样不动**。
8. **三个 `WfHisTask` 插入点**：各加 `NodeVisitId = Task.NodeVisitId`。
9. **4 个绕开路径**：`WfTimeoutJob` 三处 + `WfTaskService.UrgeAsync` 一处，改成先构造 `WfHistory`（补 4 个新字段）再 `await WfHistorySequence.WriteSystemRowAsync(...)`。`WfTaskService` 用 `histories.Db` 拿客户端。`WfTimeoutJob.HandleFailureAsync` 里那段按 `TimeoutFired` 行数近似失败次数的查询（`:288-294`）不受影响，别顺手改。
10. **修 `WorkflowMultiLeaderSnapshotTests.cs:657`** 的构造参数个数。
11. 写两个新测试文件 → 跑闸门。

### 测试清单

**新增 `WfNodeVisitIdTests.cs`（7 条）**

1. 进入节点后 `wf_token.NodeVisitId` 非空。
2. 同一次访问建的 `wf_task` / `wf_history`（`NodeEnter`+`TaskCreated`）/ `wf_cc` 三张表的值与 token **相等**。
3. **再次进入同一节点产生不同的值**（用 `onReject=toNode` 拒绝路由或 Return+Resubmit 构造），且第一次访问留下的旧行仍保持旧值。← 头等钉子。
4. **停留期间不变**：会签模式下第一票同意（走 `CompleteTaskOp` 未满票分支）后，token 的 `NodeVisitId` 与 `Version` 一个变一个不变。← 钉「与 Version 职责不混用」。
5. `wf_his_task` 携带它关闭的那件待办的访问 Id（同意 / 转办 / 退回三条路各断言一次，合并为一条测试）。
6. 抄送节点重走（重提）后 `wf_cc` **不新增行**（去重键未变），已有行的访问 Id 保持首次值。
7. `InstanceStarted` 那行 `NodeVisitId` 为 `null`（写在 `EnterNodeOp` 之前），`TokenId` 非空。

**新增 `WfHistoryIdentityTests.cs`（8 条）**

8. 一个实例的 `Sequence` 从 1 起、严格递增、无重复。
9. 一次命令写的 N 条历史占 N 个**连续**号（钉「逐行分配、无间隙」；任何「块预留」实现会红）。
10. 两个实例的序号互相独立（各自从 1 起）。
11. 超时 Job 写的行也有序号，且接在引擎写的行后面（钉绕开路径没漏分配器）。
12. 催办行：`ActorType == Human` 且 `ActorUserId == 催办人`。
13. 超时行（Job 三处 + 引擎 `BeginTimeoutAsync` 那条命令写的全部行）：`ActorType == Timeout` 且 `ActorUserId == null`。
14. 用户命令写的每一行：`ActorType == Human` 且 `ActorUserId ==` 该次动作的用户（发起→发起人、同意→审批人、撤销→发起人），含 `InstanceStarted`。
15. 所有行 `PayloadVersion == 1`，`TokenId` 非空且等于当时的活跃 token。

断言一律**直接查库**（照 `WfHistoryRequestIdTests.cs` 先例），本轮不把新列透出到 DTO。

**预期条数**：264 → **279**（+15），只增不减。

**明确不做**：`Sequence` 的真实并发竞态测试（同 M2c Task 5/8 射程说明——单进程 SQLite 构造不出可靠交错，构造出来也证不了 MySQL RR 语义）。分配器正确性论证写在代码注释里，射程限制在 review 阶段如实写进 Findings。

### 陷阱

- **`WorkflowMultiLeaderSnapshotTests.cs:657` 必红**——`WorkflowEngineProbe` 硬编码 9 个构造参数。唯一一处已知会红的现有测试，exec 必须一并改。
- **PG / SqlServer 加非空列**：`Sequence`/`ActorType`/`PayloadVersion`/`HistorySeq` 漏写 `DefaultValue` → 空库 CI 全绿，存量库升级现场被拒。本机 SQLite 腿**永远看不见**这个错误。
- **SqlServer 唯一索引把多个 NULL 视为相等**——本方案靠「`Sequence` 非空 + 本轮不建唯一索引」双重绕开。将来补 `UNIQUE(InstanceId, Sequence)` 必须先分实例回填旧行（全为 0），否则建索引当场失败。
- **序号分配器必须在事务里**。谁把那 4 处写成 `NextAsync` + `Insertable` 两条裸语句，就引入只在并发下现形的撞号。
- **锁顺序窗口变宽**：`AppendHistoryAsync` 现在会在每条命令早期就 UPDATE `wf_instance` 一行（行排他锁持到提交）。`CompleteTaskOp` 先锁 wf_task 再锁 wf_instance，`CancelInstanceOp` 反序——这个 AB/BA **今天已存在**（同意路径末尾也 `ClaimInstanceAsync`），本改动只让 wf_instance 的锁取得更早。不是新增死锁类别，但 CI 若出偶发死锁，第一嫌疑人在这里。
- **`SetColumns` 不触发审计 AOP**（`WfExecutionContext.cs:90-97` 已论证）——`HistorySeq` 递增**不会**改 `wf_instance.UpdateTime/UpdateUserId`。现有「审计字段不可变」断言（commit `a90a0ce`）不会红。别为了「顺手更新一下」改成整对象 `Updateable`。
- **读回必须 `ClearFilter<IOrgScoped>()`**：`WfInstance` 是 `DataEntity`，全局数据范围过滤器作用于 `Queryable`（超时 Job/催办既有代码到处写 `ClearFilter` 正是因此）。漏了它，后台路径读回 `HistorySeq` 拿 0 行 → `FirstAsync` 返回 default → 序号永远是 0。
- **SQLite 并发写是 `SQLITE_BUSY` 而非排队**——序号分配让每条命令多一次 wf_instance 写，本机 SQLite 腿上并发用例抖动概率略升。
- **`web`/`web-react` 不受影响**：本 Task 不碰任何 DTO/Controller，`schema.d.ts` 不漂。exec 阶段若发现自己在改 `WfHistoryItemOutput`，说明越界了（那归 Task 10）。

### 闸门

```
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test  backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"
```
期望 **279/279 绿**（基线 264 + 15）。**不需要前端闸门**：本 Task 零 API 面变更。

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
