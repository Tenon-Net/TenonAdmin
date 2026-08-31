# Loop: TenonAdmin.Workflow M2c 可靠性收口

## GOAL

在 M2b(已收口,commit `bffec77`,指定过滤器 **190/190**,Tasks 1–14 全勾)基础上做 **M2c**:写操作幂等回执(operation receipt)、`RequestId`/`IdempotencyKey` 贯穿命令与事件、通知失败可观测、超时/CAS/事务回滚/回执唯一性的四库契约测试。范围与定案见 `docs/workflow/workflow-design-plan-2026-08-17.md` **§十四 14.2**、**§十五 15.1**(`IdentityHash` 不可逆契约)、`workflow-database-design-review-2026-08-24.md` **§五 / §九 / §十(M2c)**、`openworkflow-reference-2026-08-23.md` 幂等与 receipt 章节。

**禁止做 M3a/M3b**(execution/attempt/outbox/Webhook/AI Decision/动态表单/并行/React 工作流页 port)。**不改 `web-react/`**,除了最后一个任务的 `gen:api` 刷 `schema.d.ts`。不抽 `web/` 与 `web-react/` 共享层。不新增审批动词、监控页、设计器能力。不照搬 OpenWorkflow 二十多个 Backend Interface——幂等回执是现实 Seam,不是 hypothetical Seam。

## Loop 纪律(硬约束,协调者与执行者共用)

每个 **Task** 必须走完 **plan → exec → review → (修 Findings) → 勾选**,**禁止跳过 review、禁止 plan+exec 同一轮勾选、禁止未跑闸门就勾选**。

| 阶段 | 做什么 | 禁止 |
|---|---|---|
| **plan** | 读码 + 写 `## Plan`(决策点/改动清单/步骤/测试清单/陷阱);更新 Status(`当前阶段=plan`)。**不写产品代码**(除非用户显式要求 plan+exec 合并)。 | 跳过读设计文档 §五;猜 `IdentityHash` 规则 |
| **exec** | 按 Plan 实现;跑本 Task 相关测试;更新 Status(`当前阶段=exec`);**不勾选 Task**。 | 顺带做下一 Task;改 `EnterCcAsync`/M3 范围;留 `MUTATION`/`REVIEW-PROBE` |
| **review** | **独立复核**(换人/换 agent/自审须声明):亲手跑指定过滤器 + 本 Task 变异点;记 P1/P2 到 `## Findings`;**仍不勾选**(有 P1/P2 未修)。 | 只读 diff 不跑测试;重复评已闭合 Task |
| **修 Findings** | 只修 review 列出的 P1/P2;变异转红后复原;再跑 review 同款闸门。 | 扩大范围「顺手」重构 |
| **勾选** | review 0×P1 / 0×未修 P2 后打勾;Status 写「下一步=下一 Task plan」。 | 用全绿套件掩盖本 Task 未测路径 |

**轮次记账**:每轮结束更新 `## Status` + `## Log` 一行。`max: 45` 是熔断线,不是建议跳过 review 的理由。

**Git**:commit message 英文 conventional commits;**默认不 push**,用户明确要求才 push。不提交 `TestResults/`。

**接续入口**:新 agent 先读 [wf-m2c-handoff.md](./wf-m2c-handoff.md),再读本台账 `## Status` 与当前 `## Plan`。

## DONE-CONDITION

- 本账本 `## Tasks` 全部打勾
- `dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` 绿(**基线 190**,M2c 只增不减)
- **四库契约套件**在 CI 矩阵四腿各绿(与 `TestDb` 方言绑定;同一套用例跑 SQLite/MySQL/PostgreSQL/SqlServer,见 Task 8)
- `cd web && npm run typecheck && npm run lint` 绿;发起/详情写操作在一次用户动作生命周期内复用同一 request key(见 Task 9)
- 双模板 `gen:api` 后 `web` 与 `web-react` 的 `schema.d.ts` SHA256 一致
- 重复提交同一 `RequestId` 返回第一次 `WfEngineResult`(HTTP 层可观测),不再只报 `TaskConflict`/`InstanceStatusConflict` 当「丢响应重试」的唯一出口

> ⚠️ 过滤器沿用 M2b 修正写法:`FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow`。不要回退成 `~Workflow` 或 `~Wf|~Workflow`。

## Status

- 轮次: 19
- max: 45
- 当前任务: 7(通知失败可观测)
- 当前阶段: plan(已定稿,**未写产品代码**)
- 上一轮: Round 19 — Task 7 plan 定稿(K1–K8)。读码推翻了台账原文的前提:静默吞异常**不是 2 处而是 7 层** —— `WfDefaultNotifier` 内部 3 个方法各吞一次,**外加全部 4 个调用点**各包一层(引擎 ×2、催办 ×1、超时提醒 ×1)。**双层网正是病根**:默认实现的失败被自己的内层 catch 吃掉,永远到不了外层;而只在 `WfDefaultNotifier` 里加日志又覆盖不到被消费者替换掉的实现。故方案定为 **K1 删掉内层 3 个 catch(删代码)+ K2 在外层 4 处记结构化 Warning**,一步到位覆盖两种情形。另一条读码事实:`TaskUrgedAsync` 有 **2 个调用点根本不经引擎**(催办、超时提醒),所以「在 `DispatchPendingNotificationsAsync` 一处解决」是覆盖不全的。`ILogger` 已可用(`WfCompletedTimeBackfill` 先例),不需新 NuGet。改动面 **5 + 1 预期计划外**(`WorkflowEngineProbe` 又要补 `null!`,Round 14 同款,已先声明)。5 条用例(第 5 条「正常时不记警告」不可省)+ 5 个变异点已列。
- 下一步: Round 20 — **Task 7 exec**(按 K1–K8 实现,**不勾选**)。顺序:删 `WfDefaultNotifier` 的 3 个 try/catch 并改类注释 → 三个类构造各加 `ILogger<T>`(引擎 `<remarks>` 与 M2c 的 `receipts` 并列记一笔)→ 四处 catch 记 `LogWarning(ex, ...)`(**异常走 exception 形参,不拼 `ex.Message`**;级别一律 Warning)→ 自制最小 `ILoggerProvider` + `WfNotifyLoggingTests` 五条 → 全量 `--no-incremental` Release 构建判警告 → 过滤器闸门(240 → 约 245)。

## 已知起点(2026-08-27,M2b 收口后)

- **M2b 已提前落地的 M2c 前置项(§十五 15.1,勿重做)**:
  - `WfInstance.Version` / `WfToken.Version` 已就位,实例/Token 级 CAS 已在 `CancelInstanceOp`/`EnterNodeOp`/`ReturnTaskOp`/`BeginResubmitAsync`/`CompleteTaskOp`/`TakeTransitionOp` 收口;`WfVersionCasTests` 钉机制。
  - `WfTimeoutJob` 用 `taskId + Version + DueTime` 条件领取,CAS 失败 = 人工已胜出;提醒路径零 CAS(§14.1)。
  - 竞争语义测试已在 M2b 建立;M2c **不重写** CAS,只在其上叠 receipt 与四库契约。
- **今天零存在的 M2c 核心件(别去找)**:
  - `wf_operation_receipt` 表 / `WfOperationReceipt` 实体 — **零文件**
  - `IdentityHash` 构造器 / 快照测试 — **零文件**
  - 命令 DTO 上的 `RequestId`/`IdempotencyKey` — **零字段**
  - `wf_history.RequestId` 列 — **未加**
  - `WfInstance.CompletedTime` — **未加**(数据库评审 §十 M2c #1)
  - provider-neutral 四库工作流持久化契约套件 — **零文件**(现有 `WfListContractTests` 是列表契约,不是幂等/CAS 四库套件)
- **写命令端点现状**(M2c 要贯穿 receipt 的 HTTP 面):
  - `POST instance/start` / `cancel` / `resubmit`
  - `POST task/approve` / `reject` / `transfer` / `delegate` / `return` / `urge`
  - **不在 M2c 范围**:定义 CRUD/发布(`WfDefinitionController`)、抄送 `POST cc/read`(已有行级幂等语义,可挂 P3 决定是否统一 receipt)
- **催办(Urge)幂等裁定(预置,plan 可翻转须写进 `## 语义契约`)**:
  - 设计规划 §14.2 枚举的写命令**未列 Urge**;M2b 语义契约定案「可重复催办,不做频率限制」。
  - **倾向**:Urge **不进** operation receipt(每次点击都是新提醒事件);若 plan 要纳入,必须新增「同 key 返回同一历史事件 Id」的显式语义,与 YAGNI 催办定案冲突。**默认 Task 4 不包含 Urge**。
- **通知现状(`WfDefaultNotifier`)**:
  - 事务提交后调用;失败 `catch` 静默吞掉,**无** `ILogger`/指标(§14.1 要求 M2c 补结构化日志)。
  - `IWorkflowNotifier` 已 `TryAddScoped`;消费者可覆写 `WfDefaultNotifier`。
- **前端现状**:
  - 详情/发起页按钮有 UI 防连点,**无** request key 生成与复用;丢响应后重试走全新 HTTP,靠 CAS 撞墙。
  - `web-react/` 无工作流页;本轮只刷 `schema.d.ts`。
- **测试基线**:指定过滤器 **190/190**;`web` typecheck/lint 绿;`src/workflow/` vitest **29/29**。

## 语义契约(跨任务长期有效;`## Plan` 被重写也不得丢)

| 场景 | 定案(源:§十四 / 数据库评审 §五 / M2b 语义,本轮未翻转) |
|---|---|
| 幂等 identity | `ScopeKey + CommandType + TargetType + TargetId + ActorUserId + RequestKey` → 规范化后 `IdentityHash`;**对 `IdentityHash` 建唯一索引**,不直接依赖 nullable `CreateOrgId` 组合唯一 |
| `IdentityHash` 构造 | **发包后不可逆**:参与字段顺序固定、`ScopeKey` 等可空维度用哨兵归一化、分隔符固定、SHA-256 小写 hex、四库+运行时快照用例锁定(细则见数据库评审 §五) |
| receipt 事务边界 | receipt 与领域状态**同一事务**提交;业务回滚 → receipt 不残留;重复 identity 串行/并发只推进一次 |
| 重试语义 | 相同 identity 的第二次请求返回**第一次**成功的 `WfEngineResult`(信封 `data` 与第一次一致),不是新的冲突码当终态 |
| receipt vs CAS | receipt 解决「HTTP 重试/双击」;`Version` CAS 解决「并发两个不同请求」;互补,不互相替代 |
| 终态保护 | 对已终态实例/任务的写命令:receipt 仍记录(或命中已有 receipt),**不得**再次推进状态(与 CAS/状态机一致) |
| 对外字段名 | 定为 **`requestId`**(Round 10),**不设别名**、不做 `IdempotencyKey` 映射;命令层归一化:`null`/纯空白 → `null`(=本次不做幂等),否则 `Trim()`;>64 或含换行 → `RequestIdInvalid`(48028) |
| 并发败者 | 唯一冲突后若查不到赢家(赢家尚未提交)→ **该请求失败**,但**绝不推进第二次**;客户端再重试一次才拿到首次结果。不为此跨事务等待赢家提交(Round 13 H8) |
| 回执结果 JSON | 用 `WfModelJson.Options` 序列化 `WfEngineResult`;`ResultCode` 恒 `0`(业务失败随事务回滚,压根不落回执)。**`WfEngineResult` 今后只增可选字段** —— 新增 `required` 成员会让旧回执反序列化整条抛异常 |
| 重放与历史 | 台账 Task 6 的二选一定为 **「命中回执根本不进引擎」**(Round 16 J7):短路发生在 `switch` 之前,`AppendHistoryAsync` 一次都不会跑,所以重放天然不追加历史。**不另建去重机制** |
| `wf_history.RequestId` | 与 `wf_operation_receipt.RequestKey` **同源不同名**(两张表的既有命名,不统一)。无请求身份的写入(超时 ×3、催办 ×1,都绕开 ctx)一律 `null`,**不是空串** |
| 催办 | **默认不进 receipt**(可重复催办);翻转须改本表并补测试 |
| 通知失败 | 不得拖垮审批事务(4 个调用点的 `catch (Exception)` 保持不变);但**内置实现不再自己吞**(Round 19 K1),失败一律浮到调用点的网里记一条**结构化 Warning**(异常走 `exception` 形参)。级别用 Warning 不用 Error:事务已提交、业务已成功,丢的只是一次推送 |
| `CompletedTime` | 实例进入终态时写入;旧数据可从 `InstanceCompleted` 事件回填,无法确定保持空 |
| `RequestId` 事件 | `wf_history` 增可空 `RequestId`;新数据写入,旧行 nullable |
| 范围外 | 不建 outbox、不建 execution/attempt、不加 Webhook、不 port React 工作流页、不新增 Backend Interface 面 |

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段重写)

> **Task 7 — 通知失败可观测**(Round 19 写于 2026-08-31)。已读:`Abstractions/IWorkflowNotifier.cs` 三个方法、`Engine/WfDefaultNotifier.cs` 全文、`WorkflowEngine.DispatchPendingNotificationsAsync` 两处 catch、`WfTaskService.UrgeAsync` 的 catch、`WfTimeoutJob` 提醒路径的 catch、两个类的构造函数、`WfCompletedTimeBackfill`(仓内 `ILogger` 注入先例)。
> **Task 6 的 plan 已完成使命,记录留在 `## Findings` 与 `## Log`。**

### 读码所得(决策的事实底座,exec 不必重查)

- **静默吞异常共有 7 层,不是台账写的 2 处**:`WfDefaultNotifier` 内部 **3 个方法各吞一次**,加上**全部 4 个调用点**各自又包一层 try/catch(`WorkflowEngine` ×2、`WfTaskService.UrgeAsync` ×1、`WfTimeoutJob` 提醒 ×1)。**是双层网**。
- **这层双层网正是问题所在**:默认实现的失败被它自己的内层 catch 吃掉,**永远到不了**外层那 4 个 catch。所以「只在引擎里加日志」修不好默认路径;而「只在 `WfDefaultNotifier` 里加日志」又修不好**被消费者替换掉**的通知实现(那时内层不存在,失败落进外层 4 个 catch,依旧无声)。
- **`TaskUrgedAsync` 有 2 个调用点根本不经过引擎**:`WfTaskService.UrgeAsync`(用户催办)与 `WfTimeoutJob`(超时提醒,`fromUserId = null`)。所以「在 `DispatchPendingNotificationsAsync` 一处解决」覆盖不到催办与提醒。
- **`ILogger` 已经可用,不需要新 NuGet**:`WfCompletedTimeBackfill` 已经注入 `ILogger<>`(经 Hosting/Core 传递引入),台账 Task 7「不引入新 NuGet」自动满足。
- `WorkflowOptions` 里**没有**任何通知开关位。

### 决策点(exec 不得二次发挥)

| # | 决策 | 理由 |
|---|---|---|
| K1 | **删掉 `WfDefaultNotifier` 内部那 3 个 try/catch**,让它老实地抛 | 这是本 Task 的**关键动作**,也是唯一能同时修好两种情形的动作:默认实现的失败终于能到达外层网被记录;而外层 4 个 catch 保证行为不变(仍然绝不拖垮事务/不炸 HTTP)。**删代码,不是加代码** —— 双层网里删掉内层那层,可观测性就通了 |
| K2 | 结构化日志加在**外层那 4 个 catch** 里,每处一条 | 那是唯一的通用网:任何实现(内置的、消费者替换的)抛出都会落进来。4 处**不是重复**——四种通知的上下文字段本就不同(待办到达 / 实例完结 / 催办 / 超时提醒) |
| K3 | 级别一律 **`LogWarning`**,不用 `LogError` | 事务已提交、业务已成功,丢的只是一次推送 —— 对系统健康而言这是"降级"不是"故障"。用 Error 会让本就该忽略的噪声去污染告警 |
| K4 | 字段:`InstanceId` 必带;其余按现场带 `UserId`/`UserCount`/`TaskId`/`NodeId`;异常对象走 `ILogger` 的 `exception` 形参(**不要** `ex.Message` 拼进消息串) | 拼字符串会丢掉堆栈与内层异常,而这正是排障要看的东西 |
| K5 | **不加** `IOptions` 静默开关 | YAGNI。测试要断言日志,用一个自制的 `ILoggerProvider` 就够(K7),不需要产品代码为测试让路 |
| K6 | `WorkflowEngine` 加 `ILogger<WorkflowEngine>`、`WfTaskService` 加 `ILogger<WfTaskService>`、`WfTimeoutJob` 加 `ILogger<WfTimeoutJob>`;引擎的类 `<remarks>` 里把这次追加与 M2c 那次 `receipts` **并列记一笔** | 与 M2a/M2b/M2c 既有的「有意的源码级破坏性变更」同型;`ILogger<T>` 在任何 Host 里都已注册,DI 不需要额外配置 |
| K7 | 测试用**自制最小 `ILoggerProvider`**(捕获 level + 消息 + 异常 + 状态字段),经 `WorkflowAppFactory.Overrides` 注册;不引第三方断言库 | 仓内没有现成的日志假实现先例;自制约 30 行,比引包便宜 |
| K8 | **不碰**通知内容/时机/`IRealtimePublisher`、不动四个 catch 的**捕获范围**(仍是 `catch (Exception)`) | Task 边界。catch 宽是既有定案(通知绝不能炸事务),本轮只让它**出声** |

### 改动清单(exec 只允许碰这 6 个文件)

1. `backend/src/TenonAdmin.Workflow/Engine/WfDefaultNotifier.cs` — **删** 3 个 try/catch + 改类注释(K1)
2. `backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs` — 构造加 logger + 2 处 catch 记日志 + `<remarks>` 补一句(K2/K6)
3. `backend/src/TenonAdmin.Workflow/Services/WfTaskService.cs` — 构造加 logger + 催办 catch 记日志
4. `backend/src/TenonAdmin.Workflow/Jobs/WfTimeoutJob.cs` — 构造加 logger + 提醒 catch 记日志
5. `backend/tests/TenonAdmin.Tests/WfNotifyLoggingTests.cs` — 新增
6. `backend/tests/TenonAdmin.Tests/WorkflowMultiLeaderSnapshotTests.cs` — **预期计划外**:`WorkflowEngineProbe` 直接 `new WorkflowEngine(...)`,加参数必然要补一个 `null!`(Round 14 同款)。**先声明,不算溢出**

### 步骤

1. K1 删 3 个内层 catch → 2. K6 三个构造加 logger → 3. K2/K3/K4 四处 catch 记日志 → 4. `dotnet build` 过 → 5. K7 假 `ILoggerProvider` + `WfNotifyLoggingTests` 五条 → 6. 全量 `--no-incremental` Release 构建判警告(**只信全量;上一轮自引入 3 条 `xUnit2031` 就是这么抓到的**)→ 7. 指定过滤器闸门(当前 **240**,本 Task 后应 ≈ 245)。

### 测试清单(`WfNotifyLoggingTests`,5 条)

前置:注册一个**抛异常的** `IRealtimePublisher`(让内置通知真的失败),外加捕获日志的 `ILoggerProvider`。

1. **待办到达失败 → 审批仍成功 + 有一条 Warning**:approve 返回 `code = 0`,且日志里有一条含 `InstanceId` 的警告,`Exception` 非空。**这条是台账 Task 7 点名要的那条**。
2. **实例完结失败 → 同上**(走 `InstanceCompletedAsync` 那处 catch)。
3. **催办失败 → `urge` 仍返回成功 + 有警告**(证明覆盖到了**不经引擎**的那条路)。
4. **超时提醒失败 → 扫描不中断 + 有警告**(第二条不经引擎的路)。
5. **通知正常时不产生警告**:同样的流程但 publisher 不抛 → 零条本类警告。**没有这条,前四条无法排除「无论如何都记一条」的实现**。

### 变异点(留给 Round 21 的 review,exec 阶段不跑)

| 变异 | 应红 |
|---|---|
| 把 `WfDefaultNotifier` 的 3 个 try/catch **加回去** | 1、2、3、4 全红 —— 这正是「双层网让默认路径无声」的证明 |
| 某一处 catch 的 `LogWarning` 删掉 | 对应那一条 |
| `LogWarning(ex, ...)` 改成 `LogWarning(...)` 丢掉异常形参 | 断言 `Exception` 非空的那几条 |
| 无条件在成功路径也记一条警告 | 用例 5 |
| 引擎的 catch 改成 `catch (Exception) { throw; }` | 用例 1/2 会从"成功+警告"变成 5xx —— 反向确认「绝不拖垮」仍成立 |

### 陷阱

- **别只在一层加日志**。只加内层 → 替换实现的失败仍无声;只加外层 → 默认实现的失败被内层吃掉。K1 删内层是让「只加外层」成立的前提,两步是一件事,不能只做一半。
- **别用 `ex.Message` 拼串**,异常要走 `ILogger` 的 `exception` 形参,否则堆栈与 inner exception 全丢。
- **别收窄 `catch (Exception)`** —— 通知绝不能炸事务是既有定案,本轮只让它出声。
- **用例 5(正常时不记警告)不能省**:没有它,一个「无脑记一条」的实现也能让前四条全绿。
- `WfTimeoutJob` 有 `JobExecutionContext.Log`,那是**面向作业执行记录**的文本口,**不能替代**结构化 `ILogger`;两者都写会重复,本轮只写 `ILogger`。
- 不提交 `TestResults/`。

### 给后续 Task 的锚点(本轮只记录,不实施)

- Task 8 的四库套件与本 Task 无耦合(日志不落库)。
- P2→Task 8(PG 唯一冲突后整事务 aborted)仍在,是 Task 8 的头等事项。
- 若将来要做通知重试/outbox,那是 M3,本轮的日志正是那件事的入口证据。

## Tasks

> 任务顺序 = 依赖顺序。编号稳定;`## Log` 引用任务号。

- [x] **1. Operation receipt 实体 + `IdentityHash`**:新增 `wf_operation_receipt`(`WfOperationReceipt`)、`IdentityHashBuilder`(或同级静态类)、唯一索引 on `IdentityHash`、**无 HTTP** 的快照/归一化单元测试(已知输入 → 已知 hash,四库同一算法)。`CommandType`/`TargetType` 枚举或常量表在实现任务定稿。依据:数据库评审 §五。
- [x] **2. Receipt 服务 + 引擎事务内挂钩**: `IWfOperationReceiptService`(或引擎内 `virtual` 步骤,须 `TryAdd`) — `TryBeginAsync`(查已有 / 占位)与 `CommitAsync`(同事务写 `ResultJson`);与 `WorkflowEngine.BeginXxxAsync` 事务边界对齐。失败路径:业务抛错 → receipt 随事务回滚。`WorkflowReplaceabilityTests` 补一面。
- [x] **3. `WfInstance.CompletedTime`**:实体列 + 终态写入落点(`TakeTransitionOp`/`CompleteTaskOp` 终止分支等);CodeFirst 可空或带默认值;旧行回填策略按评审 §十(可从 `InstanceCompleted` 事件回填,测一条即可)。**不改** receipt 行为。
- [x] **4. 写命令 DTO + Controller 收 `RequestId`**: `Start/Approve/Reject/Transfer/Delegate/Return/Cancel/Resubmit` 输入 DTO 增 `RequestId`(或 `IdempotencyKey`,plan 阶段二选一对外名、另一名作别名/映射);Controller 透传。OpenAPI 变更 → 留给 Task 10 `gen:api`。**不含 Urge**(默认)。
- [x] **5. 引擎写路径接 receipt**:上述 8 个 `BeginXxxAsync` 入口在事务开头解析 identity → 命中则直接返回缓存 `WfEngineResult` → 否则执行现有 Op 链 → 成功则落 receipt。覆盖「串行双提交」「并发双提交仅一次推进」「业务失败无 receipt」「终态重试返回首次结果」的集成测试(单库,≥6 条,附变异点)。
- [x] **6. `wf_history.RequestId`**:列 + `AppendHistoryAsync` 写路径传入;与 receipt 的 `RequestKey` 同源。测试:重复请求不重复追加**可观测**历史(或命中 receipt 根本不进引擎 — plan 阶段二选一并写进契约)。
- [ ] **7. 通知失败可观测**: `WfDefaultNotifier` 注入 `ILogger<WfDefaultNotifier>`(或内核既有日志抽象),`catch` 改 `LogWarning`/`LogError` 结构化字段(`InstanceId`,`Event`,`UserId`,异常);可选 `IOptions` 开关保留静默模式给测试。补一条「publisher 抛错 → 审批仍成功 + 日志有条目」测试。不引入新 NuGet。
- [ ] **8. 四库持久化契约套件**:新建 `WfPersistenceContractTests`(或同级),**同一套用例**经 `TestDb.DbType` 在四库 CI 腿各跑:①`IdentityHash` 快照;②receipt 唯一性;③并发 CAS(实例/Token/任务至少各一条);④事务回滚 receipt 不残留;⑤超时领取 vs 人工 `Approve` 仅一方胜出;⑥终态保护。不复制 190 条全集,只钉持久化契约(目标 **12–20** 条,plan 阶段列清单)。SqlServer PR 腿若已有 `TEST_FILTER`,评估是否纳入子集或 nightly — plan 阶段读 `.github/workflows/backend-ci.yml` 后定。
- [ ] **9. Vue request key 生命周期**: `web/` 发起页 + 实例详情写操作:一次用户动作(打开弹窗/点一次按钮)生成 UUID,该动作重试(含 axios 重试若存在)复用;成功或明确失败后丢弃;新动作新 key。按钮防连点保留。`src/workflow/` 或 composable 单点实现,避免每页复制。typecheck/lint/vitest 绿。
- [ ] **10. `gen:api` + 契约漂移 + 验收**:双模板 `gen:api`;SHA256 一致;去掉因新字段产生的 `@ts-expect-error`(若有)。可选:Playwright 或 API 级「双 POST 同 key → 同一 instanceId/同一结果」轻量验收(不强制浏览器截图,除非协调者要求)。**勾选本 Task 前**跑齐 DONE-CONDITION 全闸门。

## Findings

> P1/P2 与跨任务约束。exec 修完打勾;P3 可挂账。

### 来自设计文档的硬约束(非 Findings,但 exec 不得违反)

1. **`IdentityHash` 首个实现即终局** — 字段顺序/分隔符/哨兵/算法/输出格式写进快照测试;后续里程碑只增字段、不重排(§十五 15.1 #2)。
2. **receipt 与状态同事务** — 禁止「先 commit 状态再异步写 receipt」。
3. **Urge 默认不进 receipt** — 翻转须同步改 `## 语义契约` 与 Task 4/5 范围。
4. **M2b CAS 不重写** — M2c 测试建立在现有 `Claim*Async` 之上;四库套件验证方言差异,不替换单库 190 条回归。
5. **`CompletedTime` 与 receipt 独立** — Task 3 不得夹带 receipt 逻辑,避免 review 范围膨胀。

### Task 1 review(Round 3,2026-08-31)

> **⚠ 自审声明**:本次 review 与 exec 由**同一 context** 完成(会话规则禁止未经用户要求就派子 agent),不满足「换人复核」。已用**变异测试**替代第二双眼睛:三处变异各自转红后复原,见下表。Task 2 起若条件允许,应换 agent 做 review。

**跑过的闸门**:`dotnet build -c Release` 0 错 0 警;`dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` → **201/201 绿**;变异后已 `git checkout` 复原,`git status` 干净。

**变异点验证**(每处只改 `Engine/WfIdentityHash.cs` 一行,跑 `~WfIdentityHashTests` 后复原):

| 变异 | 结果 | 说明 |
|---|---|---|
| 分隔符换行符 → 竖线 | **红 2/11** | `Snapshot_of_a_known_tuple_is_frozen` + `Values_containing_the_separator_are_rejected` |
| 拼接顺序 `TargetId` ↔ `ActorUserId` 对调 | **红 1/11** | 只有快照抓得到——`Different_dimensions_do_not_collide` 抓不到(两侧同步换位)。**顺序契约只由快照守**,这条测试不可删 |
| `ScopeSentinel` 从 `-` 改成空串 | **红 1/11** | 快照的无机构用例转红;哨兵归一化用例因两侧同变而仍绿,同上 |

**首次尝试的教训**:第一次改分隔符用 `sed` 转义写错,文件根本没变却报「测试全绿」——**变异测试必须先 `grep` 确认文件真的改了**,否则那个「绿」是假的。已重做并确认转红。

**核对 D1–D8**:D1(`BaseEntity` 非 `DataEntity`)✅ / D2(表名 + 唯一索引 + 辅助索引)✅ / D3(9 字段与评审 §五一致)✅ / D4(8 值,无 Urge/Timeout)✅ / D5(`Start` 锚 `DefinitionVersion`)✅ / D6(哨兵 + RequestKey 抛异常)✅ / D7(顺序/分隔符/InvariantCulture/枚举名/SHA-256 小写 hex)✅ / D8(静态类,不建接口)✅。`git diff --name-only HEAD~1` 只含计划内 4 文件 + 台账,**未碰** `ExecuteAsync`/DTO/Controller/前端 ✅。

**计划外守卫裁定**:`Enum.IsDefined` 两条 —— **保留**。它不是新决策,而是 D7「枚举用枚举名」的执行保障(未定义值会让 `ToString()` 退化成 `"99"`,把数值悄悄写进不可逆契约);4 行 + 1 条测试,无扩面。记为**已声明的计划外增量**,不计 P1/P2。

**P1**:0 条。**P2(阻塞 Task 1)**:0 条。→ 满足勾选条件。

### Task 2 review(Round 6,2026-08-31)

> **⚠ 自审声明**:与 exec 同一 context(会话规则禁止未经用户要求派子 agent)。仍用**变异测试**代替第二双眼睛,四处变异**每处先 `grep` 确认文件真改了**再跑(Round 3 的教训)。

**变异点验证**(改 → 跑 `~WfOperationReceiptTests|~WorkflowReplaceabilityTests` → 复原):

| 变异 | 结果 | 说明 |
|---|---|---|
| `TryBeginAsync` 里去掉占位 INSERT | **红 4/17** | `First_try_begin_reserves...` / `Second_try_begin...` / `Scope_and_request_key...` / `Commit_updates_in_place...` |
| `CommitAsync` 改成 `Insertable` 新增行 | **红 1/17** | `Second_try_begin_returns_the_first_result` |
| `TryAddScoped` 退化成 `AddScoped` | **红 1/17** | 第十面 `PreRegisteredOperationReceiptService_ShouldWinOverBuiltIn` |
| `WfOperationIdentity.Create` 不归一化 `ScopeKey` | **红 2/17** | `Scope_and_request_key_are_stored_normalized` / `Identity_hash_matches_the_raw_algorithm` |

**一处要说清的覆盖真相**:去掉占位 INSERT 后,`Rollback_leaves_no_receipt_behind`(第 4 条)**仍然绿** —— 它只能证明「回滚后没有残留」,证明不了「本来就写进去过」。两条合起来才完整:`First_try_begin...` 证明占位真的发生,`Rollback...` 证明它随事务消失。**第 4 条单独看是弱钉子**,后续任务别只留它。

**修掉的 P2**(review 发现,本轮已修 + 补测试 + 变异验证):

1. **P2 已修 — `CommitAsync` 静默更新 0 行**:占位行不存在时原实现只是「更新了 0 行」然后当成功返回。后果不是报错而是**留下一条 `ResultJson` 为空的回执**,下一次重试命中它 → 拿到「有回执但结果为空」的自相矛盾状态,幂等在最不该出错的地方悄悄坏掉。现改为 `affected != 1` 即抛 `AdminException`(`OperationFailed` + `reason=receiptPlaceholderMissing`),整事务回滚。补测试 `Commit_without_a_placeholder_throws_instead_of_updating_nothing`;把判断改成 `if (false)` 该测试转红(已验证)。
2. **P2 已修 — `FindAsync` 返回类型撒谎**:签名是 `Task<WfOperationReceipt>` 却会返回 `null`。它是 `protected virtual`,属于**发包后的公开重写点**,签名后改就是破坏性变更,故趁没人依赖时改成 `Task<WfOperationReceipt?>`。
3. **P3 已顺手修 — `catch (Exception)` 会吞掉取消**:改成 `when (ex is not OperationCanceledException)`,取消不再被误判成唯一键冲突。

**核对 E1–E8**:E1(两方法 + `WfOperationReceipt?` 返回)✅ / E2(占位在前)✅ / E3(归一化单一来源,快照 11/11 仍绿)✅ / E4(值对象 `Create`)✅ / E5(走 `IRepository.Db`,不自开事务)✅ / E6(SELECT→INSERT→再 SELECT,零方言错误码)✅ / E7(失败不落回执)✅ / E8(`TryAdd` + 十件套 + 类注释)✅。改动面只含计划内 7 文件,**未碰** `ExecuteAsync` / 命令 DTO / 服务层签名 / 前端 ✅。

**闸门**:`dotnet build -c Release` 0 错、工作流包 0 警;`dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` → **210/210 绿**(209 + 新增守卫测试)。

**本轮操作教训**:变异复原用了 `git checkout <整个 src 目录>`,把**同轮未提交的 P2 修复一起冲掉**,导致一次「210 里红 1」的假警报。变异复原只该 checkout **被变异的那一个文件**,或先把修复提交掉再做变异。

**P1**:0 条。**P2**:2 条,**均已修并验证**。→ 满足勾选条件。

### Round 8 修正:Round 6 「工作流包 0 警」的说法不准

`WfOperationReceiptService.FindAsync` 在 Round 6 改成 `Task<WfOperationReceipt?>` 后,SqlSugar 的 `FirstAsync` 标注是 `Task<T>` 非空 → 留下一条 **CS8619**(`bin` 已缓存时不会重现,故 Round 6 的「0 警」是增量构建的假象)。Round 8 顺手修掉:在 `FirstAsync(...)` 后加 `!` 并注明原因。不是新缺陷,是上一轮闸门读数的更正 —— **判「0 警」要看全量构建输出,别信增量**。

### Task 3 review(Round 9,2026-08-31)

> **⚠ 自审声明**:与 exec 同一 context(会话规则禁止未经用户要求派子 agent)。仍以**变异测试**代替第二双眼睛,四处变异**每处先 `grep` 确认文件真改了**再跑,复原**只 checkout 被变异的那一个文件**。

**变异点验证**(改 → 跑 `~WfCompletedTime` → 复原):

| 变异 | 结果 | 说明 |
|---|---|---|
| `WriteInstanceTerminalStatusAsync` 删掉 `CompletedTime` 赋值 | **红 3/5** | 同意 / 拒绝终止 / 撤销三条 |
| `UpdateColumns` 去掉 `i.CompletedTime`(内存写了但不落库) | **红 3/5** | 同上 —— 两处变异共同证明「写了且落库了」 |
| 回填去掉 `h.EventType == InstanceCompleted` | **首轮仍绿 → P2** | 补钉子后 **红 1/5** |
| 回填改成整对象 `Updateable`(触发审计 AOP) | **首轮仍绿 → P2** | 补钉子后 **红 1/5** |

**修掉的 P2**(review 发现,本轮已补测试 + 变异验证):

1. **P2 已修 — 回填的事件类型过滤没有钉子**:原用例给旧行只造了一条 `InstanceCompleted` 历史,于是把事件类型过滤删掉后 `MIN(CreateTime)` 仍落在同一行 → 绿。而真实实例的事件流里 `InstanceCompleted` **从来不是第一条**,过滤一丢,回填出来的就是**发起时刻**冒充完结时刻 —— 错数据,还查不出来。现在旧行额外带一条更早的 `NodeLeave` 事件,过滤一删立刻红。
2. **P2 已修 — 「回填不动审计字段」没有钉子**:把 `SetColumns` 条件更新换成整对象 `Updateable` 会触发只认 `UpdateByObject` 的审计 AOP,把 `UpdateTime`/`UpdateUserId` 刷成升级那一刻 = 把一次机械回填伪造成人为修改。现在断言回填后两列仍为 `null`。

**一处要说清的覆盖真相**:「连跑两次结果不变」这条**是弱钉子** —— 回填写入是确定性的(同一 `MIN(CreateTime)`),去掉候选查询或更新语句里任一个 `CompletedTime == null` 守卫,第二遍写进去的还是同一个值,断言照样绿。那两个守卫真正省下的是**无谓的 UPDATE**,不是正确性。本条钉住的实际是「有事件→按事件时间补齐」「无事件→保持空」。

**核对 F1–F6**:F1(可空、无 `DefaultValue`、无索引)✅ / F2(唯一落点 + 三处改调用,`ClaimInstanceAsync` 一个字没动)✅ / F3(`ctx.TimeProvider`)✅ / F4(同一条 UPDATE,`=` 非 `??=`)✅ / F5(存在性守卫沿用 `DatabaseInitializer` 的 `IsAnyTable`+`GetColumnInfosByTableName`,回避没验证过的 `IsAnyColumn`;两步 provider-neutral;`SetColumns`)✅ / F6(未透出 DTO)✅。`CompleteTaskOp` 的 **ToNode 分支未被误改**(专门一条用例钉住它仍为空)✅。改动面只含计划内 7 文件,**未碰** `ExecuteAsync` / 命令 DTO / 前端 ✅。

**闸门**:`dotnet build -c Release` 0 错、工作流包 0 警;`dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` → **215/215 绿**。

**P1**:0 条。**P2**:2 条,**均已修并验证**。→ 满足勾选条件。

### Task 4 review(Round 12,2026-08-31)

> **⚠ 自审声明**:与 exec 同一 context(会话规则禁止未经用户要求派子 agent)。仍以**变异测试**代替第二双眼睛,每处**先 `grep` 确认文件真改了**再跑,复原**只 checkout 被变异的那一个文件**。

**变异点验证**(改 → 跑 `~WfRequestIdTests` → 复原):

| 变异 | 结果 | 说明 |
|---|---|---|
| `Normalize` 去掉 `trimmed.Length > RequestIdMaxLength` | **红 1/9** | 65 字符用例 |
| `Normalize` 去掉 `trimmed.Any(char.IsControl)` | **红 1/9** | 换行用例 |
| `string.IsNullOrWhiteSpace(value)` → `value is null` | **红 2/9** | 纯空白与空串两条(`Actual: ""`,正是「空白流成空串」的形状) |
| `WfTaskController` 的 approve 透传 `input.RequestId` → `null` | **红 1/9** | approve 贯穿用例 |
| **计划外第五处**:`WfInstanceController` 的 cancel 透传 → `null` | **仍绿 9/9 → P2** | 补钉子后 red |

**修掉的 P2**(review 发现,本轮已修 + 变异验证):

1. **P2 已修 — 7 处透传只有 2 处有钉子**:原用例只钉了 approve(服务签名路径)与 start(DTO 直传路径),其余 **6 个动词**(reject/transfer/delegate/return/cancel/resubmit)的透传是**各自独立的手工活**,断掉任意一处套件全绿。后果不是崩,而是那个动词**永远不做幂等**,并且要等到 Task 5 上线后线上重放才暴露。补 `Every_remaining_write_verb_carries_its_own_request_id`:一条流水线(transfer a→b、delegate b→c、return by c、resubmit by starter、reject 新待办)加另起一个实例做 cancel,每步紧跟 `probe.Last` 断言。把 `return`+`cancel` 两处透传同时换 `null` 后转红,已验证。**造用例时踩到的真事实**:委托不能弹回给链上已持有过的人(`DelegateTargetInvalid` 48026),故委托目标必须是第三人。
2. **P2 已修 — Round 11 的「0 警」是增量构建假象(Round 8 同一个坑,第二次踩)**:全量 `--no-incremental` Release 构建里工作流包有 **20+ 条 CS1573**。根因不是漏写文档,而是这 7 个方法**原本一个 `<param>` 都没有**,只给 `requestId` 加一个就触发了「有些参数有标记、有些没有」。修法取最省的一档:把说明从 `<param>` 挪进 `<remarks>`(并在注释里写明为什么),而不是给 7 个方法补 30 个重复参数名的样板标记。修后全量构建工作流包 **0 警**;仓内**既有** 13 条警告全在 `TenonAdmin.Core`/`TenonAdmin.Services`(CS8602/CS1574/CS1573),不在 M2c 范围,本轮不动。

**两处要说清的覆盖真相**:

- `Urge_accepts_a_request_id_but_never_reaches_the_engine` **是弱钉子** —— 它断的是「催办后 `probe.Last` 仍是发起命令」。给 urge 加上透传,催办依然不进引擎,这条**照样绿**。它记录的是事实(共用 DTO 不等于共用语义),但**守不住** G7 那条决策。真正守住 G7 的只有 `WfTaskController.Urge` 里那句注释和 `UrgeAsync` 不收该参数的签名。
- `ProbingEngine` 用 `ActivatorUtilities.CreateInstance<WorkflowEngine>(sp)` 直接构造内置引擎,**绕过了 `TryAdd` 的可替换性语义** —— 若消费者替换了 `IWorkflowEngine`,这个探针装饰的仍是内置实现。测试内自用可接受(它要的就是内置引擎的行为),但**不可作为消费者示范**。记 **P3**,见下。

**核对 G1–G8**:G1(对外名 `requestId` 无别名,已写进 `## 语义契约`)✅ / G2(4 个 DTO 加字段)✅ / G3(`WfWriteCmd` 基类;**7** 个命令继承 —— 计划写 8 是把同意/拒绝当成两条命令,实际共用 `CompleteTaskCmd`;`TimeoutFireCmd` 未继承)✅ / G4(空白→`null`、`Trim`、≤64、拒控制字符,唯一一份在 `init`)✅ / G5(`RequestIdInvalid = 48028`,48022 空号未填,未借 `ModelFieldTooLong`)✅ / G6(7 个方法加可选参数在 `CancellationToken` 之前)✅ / G7(透传 7 处,urge 不传)✅ / G8(未碰 `ExecuteAsync`/receipt/`wf_history`/前端/`gen:api`)✅。改动面 = 计划内 9 文件 + 已声明的 `WorkflowReplaceabilityTests`(G6 承认的实现者破坏)✅。

**闸门**:全量 `--no-incremental` Release 构建 0 错、工作流包 0 警;`dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` → **225/225 绿**。

**P1**:0 条。**P2**:2 条,**均已修并验证**。→ 满足勾选条件。

### Task 5 review(Round 15,2026-08-31)

> **⚠ 自审声明**:与 exec 同一 context(会话规则禁止未经用户要求派子 agent)。仍以**变异测试**代替第二双眼睛,每处**先 `grep` 确认文件真改了**再跑,复原**只 checkout 被变异的那一个文件**。

**变异点验证**(改 `WorkflowEngine.cs` → 跑 `~WfReceiptEngineTests` → 复原):

| 变异 | 结果 | 说明 |
|---|---|---|
| 去掉命中后的短路 `return` | **红 2/8** | 串行重放 + 终态重试 |
| 短路时返回 `new WfEngineResult()` 而非反序列化 | **红 2/8** | 同上 —— 两处变异合起来证明「短路了」且「回的是首次快照」 |
| 资格判断 `{ RequestId: not null }` 放宽成 `is WfWriteCmd` | **红 8/8** | 空 key 进 `WfIdentityHash.NormalizeRequestKey` 直接抛 `ArgumentException` → 每条写命令 500。这正是 G4「空白必须在命令层变成 `null`」要防的形状,意外地也证明了那条决策是承重的 |
| `CompleteTaskCmd` 的 `CommandType` 写死 `Approve` | **红 1/8** | 「同 key 不同动作」——不拆 `Action` 的话,用户点拒绝会收到「同意成功」 |
| **`CommitAsync` 挪到 `UseTranAsync` 之外** | **首轮全绿 → P2** | 补钉子后 **红 1/9** |

**修掉的 P2**(review 发现,本轮已补测试 + 变异验证):

1. **P2 已修 — 「回执与领域状态同事务提交」没有钉子**:把 `CommitAsync` 移出事务,八条用例**全绿**。原因是占位行也在事务里,业务失败时一起回滚,所以「业务失败不残留」那条看不出区别。但真正坏掉的是**崩溃窗口**:状态已提交、回执还没回填时进程挂掉,库里就留下一条**已提交**且 `ResultJson` 为空的回执 —— 此后每次重试都命中它并抛 `receiptResultMissing`,一个其实已经成功的操作永远重试不回来。这恰好是设计文档硬约束 #2(禁止「先 commit 状态再异步写 receipt」)的违反形态。补 `The_receipt_is_committed_inside_the_domain_transaction`:测试替身包住内置回执服务,在 `CommitAsync` 里用 `db.Ado.IsAnyTran()` 记录调用时是否仍在事务中,再原样委托。**同时断言 `CommitCalled`**,否则「没被调用」也会让 `IsAnyTran` 断言空转。

**两处要说清的覆盖真相**:

- 「串行重放」那条里的 `wf_his_task` 计数断言,在变异①下**根本够不着** —— 短路一去掉,第二次审批就撞上已关闭的待办,`Assert.Equal(0, second.code)` 先失败,后面的计数与 `createdTaskId` 比对都没执行。它不是坏断言(留着能挡住「短路后又跑了一遍 Op 链」这类变异),但**它不是让这条用例转红的那个断言**,别把它当成「只推进一次」的证据来源。
- 超时那条依赖一个本仓语义:`hours = 0` 是**不设到期**(`dueTime` 落 null),不是「立刻到期」。用例改用 `hours = 1` 再手动把 `DueTime` 推到过去(与 `WfTimeoutTests` 同一姿势)。若后续有人把它改回 `hours = 0`,用例会因为超时压根没触发而**假绿**(回执表当然是 0 行)——所以那条里专门先断言实例已被自动通过。

**核对 H1–H10**:H1(挂钩在 `UseTranAsync` 开头、`switch` 之前)✅ / H2(`command is WfWriteCmd { RequestId: not null }`,`TimeoutFireCmd` 零特例)✅ / H3(六维映射表逐行落地,`Action` 拆码)✅ / H4(仅 `Start` 取 `StarterOrgId`,其余哨兵)✅ / H5(`WfModelJson.Options`;`ResultCode` 恒 0)✅ / H6(命中不进 `switch`、不派通知;`ResultJson` 空则抛)✅ / H7(构造参数 + `<remarks>` 记第三次源码级破坏性变更)✅ / H8(并发败者语义未实现额外等待逻辑)✅ / H9(未碰 Op 链/CAS/`wf_history`/通知/前端)✅ / H10(三个新步骤全 `protected virtual`,零新增 DI 注册)✅。改动面 = 计划内 2 文件 + 已声明的 `WorkflowMultiLeaderSnapshotTests`(H7 承认的直接构造者破坏)✅。

**闸门**:全量 `--no-incremental` Release 构建 0 错、工作流包 0 警;`dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` → **234/234 绿**。

**P1**:0 条。**P2**:1 条,**已修并验证**。→ 满足勾选条件。

### Task 6 review(Round 18,2026-08-31)

> **⚠ 自审声明**:与 exec 同一 context(会话规则禁止未经用户要求派子 agent)。仍以**变异测试**代替第二双眼睛,每处**先 `grep` 确认文件真改了**再跑,复原**只 checkout 被变异的那一个文件**。

**变异点验证**(跑 `~WfHistoryRequestIdTests` → 复原):

| 变异 | 结果 | 说明 |
|---|---|---|
| `AppendHistoryAsync` 的 `new WfHistory` 里删掉 `RequestId` 赋值 | **红 3/6** | 用例 1、2 **加上超时那条**——它的对照断言跟着红 |
| 改成 `RequestId = RequestId ?? ""` | **红 2/6** | 用例 3(null≠空串)+ 超时那条的对照断言 |
| `BeginStartAsync` 的构造传 `null` | **红 2/6** | **用例 1 如期红**——「构造 ctx 时就带上」这条钉子成立 |
| `BeginCompleteAsync` 的构造传 `null` | **红 1/6** | 精准命中用例 2,不误伤其他 |
| 去掉 `ExecuteAsync` 的短路(回滚 Task 5 行为) | **红 1/6** | 用例 6 —— 从历史侧再证一次「命中回执根本不进引擎」 |

**上一轮标记要复核的两条,结论都是钉子有效**:

1. **用例 3「全为 null」单看确实弱**(整列压根没建出来时它照样绿),但它**不是孤证**:用例 1、2 是它的对照组,而变异①(删赋值)让 1、2 同时红。三条合起来才完整——「写得进去」由 1、2 证,「该为空时为空」由 3 证。
2. **超时那条的内置对照断言是真起作用的**:它在变异①③里都跟着转红,说明「同实例的发起行**有**值」这半句确实挡住了「整列没写进去 → 超时行当然为空」的假绿。写这条对照时是预防性的,现在有证据了。

**核对 J1–J8**:J1(列名 `RequestId`,与回执表的 `RequestKey` 同源不同名,已记语义契约)✅ / J2(可空 64、无默认值、无索引)✅ / J3(`required` —— 编译器精确炸出 8 处,未靠肉眼数)✅ / J4(7 处 `cmd.RequestId` + 超时处 `null` 并附注释)✅ / J5(取 `cmd.RequestId`,未重新归一化)✅ / J6(只在 `AppendHistoryAsync` 赋一行,20 个调用点零改动)✅ / J7(二选一定为「命中回执不进引擎」,只补钉子未建新机制)✅ / J8(未透出 DTO、未动读路径投影、未碰 `gen:api`)✅。绕开 ctx 的 4 处直插(超时 ×3 + 催办 ×1)**一个字没动**,`null` 由构造得来 ✅。改动面 = 计划内 4 文件,**零溢出**——本 M2c 第一次 ✅。

**闸门**:全量 `--no-incremental` Release 构建 0 错、13 警(全为 `Core`/`Services` 既有基线,工作流包与测试工程 0 警);`dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` → **240/240 绿**。

**P1**:0 条。**P2**:0 条。→ 满足勾选条件。**这是 M2c 至今第一个 review 阶段没揪出缺陷的 Task**;可归因于 plan 阶段已把「switch 之后赋值会漏第一行」这个真陷阱提前识别并写成了用例 1。

### 跨任务待办(不阻塞 Task 1,后续任务必须消化)

- **P2 → Task 4**:`RequestKey` / `ScopeKey` 列宽都是 **64**,而 `WfIdentityHash.Compute` 对长度不设限。写命令 DTO 必须把 `RequestId` 卡在 **≤64**(配一条超长即拒的测试):否则 MySQL 非严格模式静默截断诊断列(identity 由完整值算出,不受影响,但排查时看到的是截断值),严格方言下直接插入报错。
- **P3 → Task 2**:落库的 `ScopeKey`/`RequestKey` 必须写**归一化后**的值(哨兵 + `Trim()`,复用 `WfIdentityHash.ScopeSentinel`),不能一边存原值一边用归一化值算 hash,否则诊断列与 identity 对不上。
- **P2 → Task 8**:`WfOperationReceiptService.TryBeginAsync` 靠「唯一索引冲突 → 二次 SELECT」认赢家,这在 **PostgreSQL** 上有方言陷阱 —— PG 一旦语句报错就把整个事务置为 aborted,紧接着的 SELECT 会直接报 `current transaction is aborted, commands ignored until end of transaction block`,于是「查到赢家」这条路在 PG 上走不通。SQLite/MySQL/SqlServer 不这样。**单库套件永远看不见这条**,四库套件必须专门钉;修法(savepoint / `ON CONFLICT DO NOTHING` / 先查后插的窗口容忍)留给 Task 8 的 plan 定。
- **P3 → Task 5/8**:测试里用 `ActivatorUtilities.CreateInstance<WorkflowEngine>` 构造内置引擎来做装饰器探针,绕过了 `TryAdd` 的可替换性语义(消费者替换 `IWorkflowEngine` 时探针装的仍是内置实现)。Task 5/8 若还要装饰引擎,先想清楚是要「内置引擎的行为」还是「当前注册的实现」;两者不同,别把这个写法当消费者示范。
- **P3 → Task 2/5**:`ResultCode` 是 `int`,`TenonAdmin.Core.ErrorCode` 也是 int 枚举;映射时 `0` 恒表示成功,别让 `ErrorCode` 的某个具体值落到 `0`。

## Log

| 轮次 | 阶段 | 摘要 |
|---|---|---|
| 0 | draft | 起草台账。M2b 收口 commit `bffec77`;基线 190/190。下一步 Round 1 Task 1 plan。 |
| 0b | handoff | 补 `## Loop 纪律` + `wf-m2c-handoff.md`;用户要求「严格按 loop」接续。 |
| 1 | plan | Task 1 plan 定稿:receipt 用 `BaseEntity`+显式 `ScopeKey`(避开数据范围过滤器)、唯一索引 on `IdentityHash`、`WfCommandType`(8 值,排除 Urge/Timeout)/`WfTargetType`(Start 锚 DefinitionVersion)、换行符分隔 + SHA-256 小写 hex、RequestKey 空值抛异常。锚点:`ExecuteAsync` 是唯一事务入口,Task 2/5 挂一处即可。未写产品代码。 |
| 2 | exec | Task 1 落地 4 文件:`WfCommandType`/`WfTargetType` 枚举、`WfOperationReceipt`(`BaseEntity`,唯一索引 on `IdentityHash`)、静态 `WfIdentityHash.Compute`、`WfIdentityHashTests` 11 例(含两条冻结快照常量)。build 0 错 0 警;过滤器 **201/201**(190+11)。计划外补 `Enum.IsDefined` 守卫,交 review 裁定。未勾选。 |
| 3 | review+勾选 | Task 1 自审(已声明):三处变异(分隔符 / `TargetId`↔`ActorUserId` / 哨兵)各转红后复原,D1–D8 全对,改动面无溢出,全过滤器 201/201。`Enum.IsDefined` 计划外守卫裁定保留。**Task 1 打勾**。新增跨任务待办:P2→Task 4(`RequestId` 长度 ≤64)、P3→Task 2(落库存归一化值)、P3→Task 2/5(`ResultCode` 0=成功)。教训:变异测试要先 grep 确认文件真改了,否则「绿」是假的。 |
| 4 | plan | Task 2 plan 定稿(E1–E8):`IWfOperationReceiptService` 两方法(`TryBeginAsync` 返回 `WfOperationReceipt?`/`CommitAsync` 回填);**占位在前**;唯一冲突走 SELECT→INSERT→SELECT 不碰方言错误码;新值对象 `WfOperationIdentity` + 归一化提取到 `WfIdentityHash`(入库值与 hash 同源,消化 P3);业务失败不落回执;可替换性九件套→十件套。边界:不碰 `ExecuteAsync`/DTO/服务签名(那是 Task 5)。未写产品代码。 |
| 5 | exec | Task 2 落地 7 文件:归一化提取(快照 11/11 仍绿)、`WfOperationIdentity`、`IWfOperationReceiptService` + 实现(占位在前;唯一冲突走二次 SELECT,不碰方言错误码)、`TryAddScoped` 一行、可替换性**十件套**、`WfOperationReceiptTests` 7 例(含「回滚不残留」核心钉子 + 射程声明)。build 0 错、工作流包 0 警;过滤器 **209/209**(201+8)。未勾选。 |
| 6 | review+修+勾选 | Task 2 自审:四处变异(去占位 / Commit 改新增 / TryAdd→Add / 不归一化)各转红后复原。修 2×P2——`CommitAsync` 0 行不再静默(改抛 + 补测试 + 变异验证)、`FindAsync` 可空签名趁未发包改正;顺带 P3 不再吞 `OperationCanceledException`。闸门 **210/210**。**Task 2 打勾**。教训:变异复原只 checkout 被变异的那一个文件,别 checkout 整个 src 目录(会冲掉同轮未提交的修复,制造假红)。 |
| 7 | plan | Task 3 plan 定稿(F1–F6):`CompletedTime` 为 nullable 无默认值列(nullable ADD COLUMN 四库均接受,不触发 `Version` 注释里的三步路);终态写入收成 `WfExecutionContext.WriteInstanceTerminalStatusAsync` 一处、三个落点改调用,**不动 M2b 的 `ClaimInstanceAsync`**;时间源 `ctx.TimeProvider`;回填走带 `IsAnyTable`/`IsAnyColumn` 守卫的一次性 HostedService + 两步 provider-neutral SQL(`SetColumns` 条件更新,不污染审计字段),无事件的旧行保持空;**不透出 DTO**(OpenAPI 面留给 Task 10 的 `RequestId`)。读码新事实:`Terminated` 全仓无写入点、实例终态不可逆、`ISeedData` 只插不改。未写产品代码。 |
| 8 | exec | Task 3 落地 7 文件:`CompletedTime` 可空列、终态写入收成 `WfExecutionContext.WriteInstanceTerminalStatusAsync` 一处(三个分支改调用,`ClaimInstanceAsync` 未动)、`WfCompletedTimeBackfill` 一次性 HostedService(守卫沿用 `DatabaseInitializer` 的 `IsAnyTable` + `GetColumnInfosByTableName` 写法,回避没验证过的 `IsAnyColumn`;回填 `InnerJoin`+`GroupBy MIN(CreateTime)` → 逐条 `SetColumns` 条件更新)、`WfCompletedTimeTests` 5 例(含 ToNode 分支保持空、回填幂等)。顺带修 Round 6 遗留的 `CS8619`。build 0 错 0 警;过滤器 **215/215**。未勾选。 |
| 9 | review+修+勾选 | Task 3 自审:四处变异,前两处(删赋值 / `UpdateColumns` 去列)各红 3/5;后两处(删事件类型过滤 / 回填改整对象更新)**仍绿** → 2×P2,当场补钉子后各转红 1/5。另记下「幂等断言是弱钉子」的覆盖真相(回填写入是确定性的,去掉任一 `CompletedTime == null` 守卫也写同样的值)。闸门 **215/215**、工作流包 0 警。**Task 3 打勾**。 |
| 10 | plan | Task 4 plan 定稿(G1–G8):对外名定 `requestId` 无别名;4 个入参 DTO 加字段(`WfTaskActionInput` 一个覆盖 6 个动词);抽 `WfWriteCmd` 基类把归一化(空白→`null`、`Trim`、≤64、禁换行)写成**唯一一份**,`TimeoutFireCmd` 不继承 → Task 5 的排除条件变成类型判断;新码 `RequestIdInvalid = 48028`(不填 48022 空号、不借 `ModelFieldTooLong`);7 个服务方法加可选参数(`StartAsync` 收 DTO 无需改),Controller 透传 7 处、**urge 不传**(它压根不进引擎)。测试靠引擎装饰器探针,6 条。未写产品代码。 |
| 11 | exec | Task 4 落地 10 文件:`RequestIdInvalid = 48028`;`WfWriteCmd` 基类(归一化 + ≤64 + 拒控制字符,**唯一一份**在 `init` 里),**7** 个命令类改继承(同意/拒绝共用 `CompleteTaskCmd`,故不是 8 个),`TimeoutFireCmd` 不继承;4 个 DTO 加 `RequestId`;7 个服务方法加可选参数;Controller 透传 7 处、urge 不传;`WfRequestIdTests` 9 例(含 `Theory` 的归一化 3 例与长度边界 2 例),靠包住内置引擎的装饰器探针断言真实调用链。计划外必改 `WorkflowReplaceabilityTests` 的两个 Fake(签名跟随)。build 0 错 0 警;过滤器 **224/224**。未勾选。 |
| 12 | review+修+勾选 | Task 4 自审:四处计划内变异各转红(去长度判断 / 去控制字符判断 / `IsNullOrWhiteSpace`→`is null` / approve 控制器透传换 `null`)。**计划外第五处变异揭出真缺口**:断掉 cancel 透传套件全绿 → P2「7 处透传只有 approve+start 两处有钉子」,补一条流水线用例覆盖余下 6 个动词并变异验证(顺带钉住:委托不能弹回给链上持有过的人,48026)。第二个 P2:Round 11 的「0 警」又是增量假象,全量构建里工作流包 20+ 条 CS1573 —— 根因是只给 `requestId` 加 `<param>` 而同方法其余参数都没标记,把说明挪进 `<remarks>` 修掉。另记两条覆盖真相(urge 那条是弱钉子、`ProbingEngine` 绕过 `TryAdd` → P3)。闸门:全量 Release 工作流包 0 警;过滤器 **225/225**。**Task 4 打勾**。 |
| 13 | plan | Task 5 plan 定稿(H1–H10):挂钩收敛到 `ExecuteAsync` **一处**(`UseTranAsync` 已把 8 个 `BeginXxxAsync` + `RunAgendaAsync` 全包住);资格判断 `command is WfWriteCmd { RequestId: not null }` 零特例;8 条命令 → 六维映射表(`CompleteTaskCmd` 按 `Action` 拆 Approve/Reject,否则「同 key 先同意后拒绝」会被误判成重试);`ScopeKey` 只 `Start` 取机构(其余 `TargetId` 是雪花 Id,机构已隐含);结果 JSON 复用 `WfModelJson.Options`,`ResultCode` 恒 0(消化 P3→Task 2/5);命中不派通知靠现有 `ctx is null` 守卫免费拿到;并发败者「不推进第二次但也不跨事务等赢家」(H8)。改动面 **2 文件**,8 条用例 + 5 个变异点已列。新记 P2→Task 8:PG 唯一冲突会中止整个事务,`TryBeginAsync` 的二次 SELECT 在 PG 上会炸,单库看不见。未写产品代码。 |
| 14 | exec | Task 5 落地 3 文件(计划 2 + 计划外 1):`ExecuteAsync` 一处短路 + 同事务 `CommitAsync`;三个 `protected virtual` 小步(`TryCreateIdentity` 六维映射、`SerializeResult`/`DeserializeResult` 复用 `WfModelJson.Options`),`CompleteTaskCmd` 按 `Action` 拆码;引擎构造函数追加 `IWfOperationReceiptService`(第三次同型破坏性变更,已记 `<remarks>`)。计划外必改 `WorkflowMultiLeaderSnapshotTests` 的 `WorkflowEngineProbe`(直接 `new WorkflowEngine`,补 `null!`)—— 按 Plan 自检回头质疑过,属 H7 承认的代价。`WfReceiptEngineTests` 8 例,其中**并发那条按射程学说换成「同 key 不同 actor/target 不串」**并写明射程。踩到的真事实:`hours = 0` 在本仓是「不设到期」而非「立刻到期」,超时用例要 `hours = 1` + 手动推 `DueTime`。build 0 错、工作流包 0 警(全量);过滤器 **233/233**。未勾选。 |
| 15 | review+修+勾选 | Task 5 自审:五处变异,前四处各转红(去短路 2/8、返回空结果 2/8、资格判断放宽 **8/8**、`CommandType` 写死 1/8)。**第五处「`CommitAsync` 挪出事务」八条全绿 → P2** —— 占位行也在事务里,业务失败一起回滚,所以「无残留」那条看不出差别;真正坏的是崩溃窗口会留下一条已提交却 `ResultJson` 为空的回执,让成功的操作永远重试不回来。补 `The_receipt_is_committed_inside_the_domain_transaction`(替身在 `CommitAsync` 里用 `db.Ado.IsAnyTran()` 记录,并同时断言 `CommitCalled` 防空转),变异⑤转红 1/9。另记两条覆盖真相:「串行重放」的计数断言在变异下**够不着**(前面的 `code == 0` 先失败),不是它让用例红;超时那条必须 `hours = 1` + 手动推 `DueTime`,`hours = 0` 在本仓是「不设到期」会造成假绿。闸门:工作流包 0 警(全量);过滤器 **234/234**。**Task 5 打勾**。 |
| 16 | plan | Task 6 plan 定稿(J1–J8):20 个 `AppendHistoryAsync` 调用全收敛到 ctx 里一条 `Insertable`,列值只赋一行;绕开 ctx 的 4 处直插(超时 ×3 + 催办 ×1)不设属性即 `null`,零改动且正是语义;`BeginStartAsync` 构造后立刻写 `InstanceStarted`,故排除「switch 后赋值」,改 8 处构造各带一行并把属性声明为 **`required`**(编译器兜底,加第 9 个 Begin 忘带 = 编译错误);台账二选一由 Task 5 短路免费解决,只补钉子。显式复核 Task 5 锚点:取 `cmd.RequestId` 不违反「禁止再取第二遍」——那禁的是重新归一化出第二条路径(J5)。改动面 4 文件,6 条用例(第 1 条专钉「构造时就带上」)+ 5 个变异点。未写产品代码。 |
| 17 | exec | Task 6 落地 **4 文件、零溢出**:`WfHistory` 可空 64 列;ctx 的 `required string? RequestId` —— `required` 一加编译器精确炸出 8 处构造(与 plan 一致,未靠肉眼数),7 处 `cmd.RequestId` + 超时处 `null`;`AppendHistoryAsync` 赋一行,20 个调用点零改动;绕开 ctx 的 4 处直插(超时 ×3 + 催办 ×1)一个字没动,`null` 是语义。plan 预警的第 5 文件风险未兑现。`WfHistoryRequestIdTests` 6 例一次全绿(用例 1 专钉「构造时就带上」,用例 4 内置对照断言防「整列没写」的假绿)。踩到 3 条自引入的 `xUnit2031`,全量警告 13→16,改用 `Assert.Single` 谓词重载后回到 13 —— 再次印证只信全量构建。过滤器 **240/240**。未勾选。 |
| 18 | review+勾选 | Task 6 自审:五处变异**全部按预期转红**(删赋值 3/6、`?? ""` 2/6、`BeginStartAsync` 传 null 2/6、`BeginCompleteAsync` 传 null 1/6、去掉 Task 5 短路 1/6),**0×P1 / 0×P2 —— M2c 第一次 review 无新增缺陷**。上一轮标记的两条疑虑均有结论:用例 3 单看弱但与用例 1/2 构成对照组;超时那条的内置对照断言在两处变异里跟着红,证明它真挡得住「整列没写进去」的假绿。J1–J8 全对,改动面零溢出。闸门:全量 Release 0 错、工作流包与测试工程 0 警;过滤器 **240/240**。**Task 6 打勾**。 |
| 19 | plan | Task 7 plan 定稿(K1–K8)。**读码推翻台账原文的前提**:静默吞异常不是 2 处而是 **7 层**(`WfDefaultNotifier` 内 3 + 4 个调用点各 1),双层网正是病根 —— 默认实现的失败被内层吃掉、到不了外层,而只在 Notifier 里加日志又覆盖不到消费者替换的实现。方案定为 **删内层 3 个 catch + 外层 4 处记结构化 Warning**,一个动作同时修好两种情形,且是**删代码**。另一条事实:`TaskUrgedAsync` 有 2 个调用点不经引擎(催办、超时提醒),「引擎一处解决」覆盖不全。`ILogger` 已可用,不需新 NuGet。改动面 5 + 1 预期计划外(`WorkflowEngineProbe` 补 `null!`,已先声明)。5 条用例(第 5 条「正常时不记警告」不可省)+ 5 个变异点。未写产品代码。 |

## 参考读码清单(Round 1 plan 前)

| 主题 | 路径 |
|---|---|
| M2c 定案 | `docs/workflow/workflow-design-plan-2026-08-17.md` §14.1–14.2、§15.1 |
| receipt 字段与 hash | `docs/workflow/workflow-database-design-review-2026-08-24.md` §五、§九、§十 M2c |
| OpenWorkflow 对照 | `docs/workflow/openworkflow-reference-2026-08-23.md` §六、M2c 小节 |
| 引擎事务入口 | `Engine/WorkflowEngine.cs` 全部 `BeginXxxAsync` |
| 命令 DTO | `Services/WfRuntimeModels.cs`、`Engine/WfCommands.cs` |
| Controller | `Controllers/WfInstanceController.cs`、`WfTaskController.cs` |
| 历史写入 | `Engine/WfExecutionContext.cs` `AppendHistoryAsync` |
| 终态写入 | `Engine/Operations/TakeTransitionOp.cs`、`CompleteTaskOp.cs` |
| 通知 | `Engine/WfDefaultNotifier.cs`、`Abstractions/IWorkflowNotifier.cs` |
| 四库测试先例 | `backend/tests/TenonAdmin.Tests/TestDb.cs`、`WorkflowAppFactory.cs` |
| CI 矩阵 | `.github/workflows/backend-ci.yml` `TEST_FILTER` / SqlServer 子集 |
| 前端写操作 | `web/src/views/workflow/instance/detail.vue`、发起相关页 |
| M2b 禁区对照 | `.loop/wf-m2b.md` GOAL / DONE-CONDITION |
