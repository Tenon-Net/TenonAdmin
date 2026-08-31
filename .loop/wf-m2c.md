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

- 轮次: 6
- max: 45
- 当前任务: 2(**已勾选**)→ 下一任务 3
- 当前阶段: review 完成 + 修 P2 + 勾选(0×P1 / 2×P2 均已修)
- 上一轮: Round 6 — Task 2 review(**自审**,已声明)。四处变异各转红后复原(先 grep 验证文件真改了)。发现并当场修掉 2 条 P2:①`CommitAsync` 静默更新 0 行会留下「有回执但结果为空」的自相矛盾态 → 改为 `affected != 1` 即抛并回滚,补测试;②`FindAsync` 签名 `Task<WfOperationReceipt>` 却返回 null,而它是 `protected virtual` 公开重写点,趁未发包改成可空。另顺手修 P3:`catch (Exception)` 不再吞取消。闸门 **210/210 绿**。**Task 2 已打勾。**
- 下一步: Round 7 — **Task 3 plan only**(`WfInstance.CompletedTime`)。plan 前必读:①`Engine/Operations/TakeTransitionOp.cs` 与 `CompleteTaskOp.cs` 的终态分支(实例进 Approved/Rejected/Cancelled/Terminated 的**全部**落点,别漏 `CancelInstanceOp`);②`WfInstance.Version` 的 `DefaultValue` 长注释(新增列在四库的 ADD COLUMN 行为,尤其 SQLite 例外);③数据库评审 §4.2 与 §九 #4(旧行可从 `InstanceCompleted` 事件回填,测一条即可)。**Task 3 不得夹带 receipt 逻辑**(`## Findings` 硬约束 #5)。本轮只 plan,不写产品代码。

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
| 催办 | **默认不进 receipt**(可重复催办);翻转须改本表并补测试 |
| 通知失败 | 不得拖垮审批事务;但必须**结构化日志**(至少 `ILogger`)+ 可计数指标钩子;禁止继续纯静默 |
| `CompletedTime` | 实例进入终态时写入;旧数据可从 `InstanceCompleted` 事件回填,无法确定保持空 |
| `RequestId` 事件 | `wf_history` 增可空 `RequestId`;新数据写入,旧行 nullable |
| 范围外 | 不建 outbox、不建 execution/attempt、不加 Webhook、不 port React 工作流页、不新增 Backend Interface 面 |

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段重写)

> **Task 2 — Receipt 服务 + 事务内挂钩**(Round 4 写于 2026-08-31)。已读:`## Findings` 跨任务待办、`WorkflowSetup.cs` 全文、`Abstractions/IWorkflowEngine.cs`、`WorkflowEngine.ExecuteAsync`、`WfInstanceService`/`WfTaskService` 的 7 处命令构造点、`WorkflowReplaceabilityTests`(现「九件套」)、`WorkflowAppFactory`、`WfVersionCasTests` 的射程声明写法。
> **Task 1 的 plan 已完成使命,记录留在 `## Findings` 与 `## Log`。**

### 边界(先划清,免得和 Task 5 打架)

- **Task 2 = 回执的持久化 SPI 本身**:查已有 / 占位 / 回填 / 回滚语义 / `TryAdd` 可替换面 / 直接对服务的测试。
- **Task 5 = 把它挂到 `ExecuteAsync`** 并让 8 个写命令携带 `RequestKey`。**本轮不碰 `ExecuteAsync`、不碰命令 DTO、不碰服务层签名。**
- 只有 `StartInstanceCmd` 带机构(`StarterOrgId`),其余命令都没有 —— `ScopeKey` 怎么贯穿是 **Task 4/5** 的事,Task 2 只保证「给什么都能正确归一化并算出同一 identity」。

### 决策点(exec 不得二次发挥)

| # | 决策 | 理由 |
|---|---|---|
| E1 | 接口 `IWfOperationReceiptService`(`Abstractions/`),两个方法:`TryBeginAsync(WfOperationIdentity, ct)` → **`WfOperationReceipt?`**(`null` = 新占位成功、继续执行;非 `null` = 已有回执、调用方短路返回它);`CommitAsync(WfOperationIdentity, int resultCode, string? resultJson, ct)` → 回填占位行 | 一个返回值表达两种结局,不套 wrapper 类。名字沿用台账 Task 2 的 `TryBeginAsync`/`CommitAsync` |
| E2 | **占位在前(reserve-first)**,不是「执行成功后再插回执」 | 事后插入挡不住并发双提交:两边都已推进状态,第二个只能拿到 `TaskConflict` —— 那正是 M2c 要消灭的现象。占位行走唯一索引,第二个请求会阻塞到第一个提交/回滚,拿到确定结果 |
| E3 | **归一化只有一份**:把 `ScopeKey`/`RequestKey` 的 trim + 哨兵逻辑从 `WfIdentityHash.Compute` 里提成两个 `public static`(`NormalizeScopeKey` / `NormalizeRequestKey`),`Compute` 与新的 `WfOperationIdentity.Create` 都调它 | 消化 `## Findings` 的 **P3 → Task 2**:落库值必须与算 hash 的值同源。两处各写一遍 trim/哨兵 = 分叉风险,正是评审 §五最担心的。**这是对已勾选 Task 1 文件的唯一允许改动**,且 Task 1 的快照测试就是它的防回归证据(转红 = 改坏了不可逆契约) |
| E4 | 新值对象 `WfOperationIdentity`(`Engine/`),`Create(...)` 静态工厂:归一化六维 + 当场算出 `IdentityHash`,只读属性 | 服务与将来的引擎钩子都拿同一个对象,不再各自传 6 个散参、也不可能出现「入库值 ≠ 算 hash 的值」 |
| E5 | 实现类 `Services/WfOperationReceiptService.cs`,注入 `IRepository<WfOperationReceipt>`,读写都走它的 `.Db` | 同一个 `SqlSugarScope` 单例 → 自动落在调用方的 `UseTranAsync` 事务里(Task 5 挂钩后即与领域状态同事务)。`IRepository<>` 可用正是 D1 选 `BaseEntity` 的红利 |
| E6 | 唯一键冲突**不解析数据库错误码**:`TryBeginAsync` = SELECT → 未命中则 INSERT → **INSERT 抛异常时再 SELECT 一次**,查到就当命中返回,查不到才原样抛 | 四库错误码/异常类型各不相同,解析它是方言陷阱;二次 SELECT 是 provider-neutral 的等价判据 |
| E7 | **业务失败不落回执**:`ResultCode` 只写 `0`(成功)。业务抛错 → 整事务回滚 → 占位行一并消失 → 重试可再试 | 与 `## 语义契约`「receipt 事务边界」一致。把失败也幂等化会让「改完权限再重试」永远拿到旧的失败,不是本里程碑要的语义。`ResultCode` 字段保留给将来 |
| E8 | DI:`services.TryAddScoped<IWfOperationReceiptService, WfOperationReceiptService>();` 挂在 `WorkflowSetup` 的引擎注册附近;`WorkflowReplaceabilityTests` 从**九件套变十件套**(类注释同步改) | 内置服务一律 `TryAdd`(内核第一原则);九件套是契约测试,新增一面必须同时补钉子和文档 |

### 改动清单(exec 只允许碰这 7 个文件)

1. `backend/src/TenonAdmin.Workflow/Abstractions/IWfOperationReceiptService.cs` — 新增 SPI
2. `backend/src/TenonAdmin.Workflow/Engine/WfOperationIdentity.cs` — 新增值对象 + `Create`
3. `backend/src/TenonAdmin.Workflow/Services/WfOperationReceiptService.cs` — 新增实现
4. `backend/src/TenonAdmin.Workflow/Engine/WfIdentityHash.cs` — **仅** E3 的提取重构(不改算法)
5. `backend/src/TenonAdmin.Workflow/WorkflowSetup.cs` — `TryAddScoped` 一行
6. `backend/tests/TenonAdmin.Tests/WorkflowReplaceabilityTests.cs` — 补第十面 + 类注释九→十
7. `backend/tests/TenonAdmin.Tests/WfOperationReceiptTests.cs` — 新增服务级测试

### 步骤

1. E3 提取归一化 → 立刻跑 `~WfIdentityHashTests` 确认 **11/11 仍绿**(快照不动 = 重构无副作用) → 2. `WfOperationIdentity` → 3. 接口 → 4. 实现 → 5. DI + 第十面 → 6. `WfOperationReceiptTests` → 7. `dotnet build -c Release` → 8. 指定过滤器闸门(当前 201,本 Task 后应 ≈ 210)。

### 测试清单(`WfOperationReceiptTests`,7 条 + 可替换性 1 面)

1. `TryBeginAsync` 首次返回 `null`,且库里落了一行占位(按 hash 查得到)
2. 同 identity 第二次 `TryBeginAsync` 返回**已有回执**,`ResultJson` 与第一次 `CommitAsync` 写入的一致
3. `CommitAsync` 只回填不新增:行数仍为 1,`ResultCode == 0`
4. **回滚不残留**:在 `UseTranAsync` 里 `TryBeginAsync` 后抛异常 → 事务失败 → 按 hash 查为空(这条是 E2/E7 的核心钉子)
5. 归一化同源:`scopeKey = null` 与 `= "-"` 命中同一行;入库 `ScopeKey` 恰为哨兵、`RequestKey` 为 trim 后的值
6. 不串味:仅 `RequestKey` 不同 / 仅 `CommandType` 不同(Approve vs Reject)→ 两行互不命中
7. 同源钉子:`WfOperationIdentity.Create(x).IdentityHash == WfIdentityHash.Compute(x)`(逐参数对照)
8. `WorkflowReplaceabilityTests`:前置注册 `FakeReceiptService` 胜出内置(把 `TryAdd` 退化成 `Add` 即红)

### 陷阱

- **别解析数据库错误码**判唯一冲突(E6);别在 `catch` 里吞掉非唯一冲突的异常。
- **别把业务失败写进回执**(E7);别在 `TryBeginAsync` 里自己开事务——它必须跑在调用方的事务里,自己开会把占位行提前提交、回滚不掉。
- 改 `WfIdentityHash` **只允许**提取归一化方法这一种重构:签名、顺序、分隔符、算法、输出格式一个字都不能动;`Snapshot_of_a_known_tuple_is_frozen` 转红就是改坏了。
- `IRepository<WfOperationReceipt>.DeleteAsync` 是**软删**,回执永不删,别用它清理测试数据。
- 可替换性测试的类注释写着「九件套」,加了第十面必须同步改,否则文档与钉子对不上。
- 本轮**不碰** `ExecuteAsync` / 命令 DTO / 服务层签名 / 前端(Task 4/5)。
- 测试用 `WorkflowAppFactory` 起真实 Host 取 `IWfOperationReceiptService` 与 `ISqlSugarClient`;**射程要写清**:并发交错构造不出来,第 4 条钉的是「同事务回滚」而非真并发(照 `WfVersionCasTests` 的射程声明写法),别把它吹成并发证明。
- 不提交 `TestResults/`。

### 给后续 Task 的锚点(本轮只记录,不实施)

- Task 5 挂钩落点:`ExecuteAsync` 的 `UseTranAsync` 内、`command switch` **之前**做 `TryBeginAsync`,`ctx.ToResult()` **之后**做 `CommitAsync`;`TimeoutFireCmd` 显式跳过(D4)。
- Task 4 的 P2 仍在:`RequestId` 长度必须 ≤64,与 `RequestKey` 列宽对齐。

<!-- TASK1-PLAN-ANCHOR -->

## Tasks

> 任务顺序 = 依赖顺序。编号稳定;`## Log` 引用任务号。

- [x] **1. Operation receipt 实体 + `IdentityHash`**:新增 `wf_operation_receipt`(`WfOperationReceipt`)、`IdentityHashBuilder`(或同级静态类)、唯一索引 on `IdentityHash`、**无 HTTP** 的快照/归一化单元测试(已知输入 → 已知 hash,四库同一算法)。`CommandType`/`TargetType` 枚举或常量表在实现任务定稿。依据:数据库评审 §五。
- [x] **2. Receipt 服务 + 引擎事务内挂钩**: `IWfOperationReceiptService`(或引擎内 `virtual` 步骤,须 `TryAdd`) — `TryBeginAsync`(查已有 / 占位)与 `CommitAsync`(同事务写 `ResultJson`);与 `WorkflowEngine.BeginXxxAsync` 事务边界对齐。失败路径:业务抛错 → receipt 随事务回滚。`WorkflowReplaceabilityTests` 补一面。
- [ ] **3. `WfInstance.CompletedTime`**:实体列 + 终态写入落点(`TakeTransitionOp`/`CompleteTaskOp` 终止分支等);CodeFirst 可空或带默认值;旧行回填策略按评审 §十(可从 `InstanceCompleted` 事件回填,测一条即可)。**不改** receipt 行为。
- [ ] **4. 写命令 DTO + Controller 收 `RequestId`**: `Start/Approve/Reject/Transfer/Delegate/Return/Cancel/Resubmit` 输入 DTO 增 `RequestId`(或 `IdempotencyKey`,plan 阶段二选一对外名、另一名作别名/映射);Controller 透传。OpenAPI 变更 → 留给 Task 10 `gen:api`。**不含 Urge**(默认)。
- [ ] **5. 引擎写路径接 receipt**:上述 8 个 `BeginXxxAsync` 入口在事务开头解析 identity → 命中则直接返回缓存 `WfEngineResult` → 否则执行现有 Op 链 → 成功则落 receipt。覆盖「串行双提交」「并发双提交仅一次推进」「业务失败无 receipt」「终态重试返回首次结果」的集成测试(单库,≥6 条,附变异点)。
- [ ] **6. `wf_history.RequestId`**:列 + `AppendHistoryAsync` 写路径传入;与 receipt 的 `RequestKey` 同源。测试:重复请求不重复追加**可观测**历史(或命中 receipt 根本不进引擎 — plan 阶段二选一并写进契约)。
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

### 跨任务待办(不阻塞 Task 1,后续任务必须消化)

- **P2 → Task 4**:`RequestKey` / `ScopeKey` 列宽都是 **64**,而 `WfIdentityHash.Compute` 对长度不设限。写命令 DTO 必须把 `RequestId` 卡在 **≤64**(配一条超长即拒的测试):否则 MySQL 非严格模式静默截断诊断列(identity 由完整值算出,不受影响,但排查时看到的是截断值),严格方言下直接插入报错。
- **P3 → Task 2**:落库的 `ScopeKey`/`RequestKey` 必须写**归一化后**的值(哨兵 + `Trim()`,复用 `WfIdentityHash.ScopeSentinel`),不能一边存原值一边用归一化值算 hash,否则诊断列与 identity 对不上。
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
