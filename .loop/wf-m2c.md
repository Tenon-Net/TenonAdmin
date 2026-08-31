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

- 轮次: 11
- max: 45
- 当前任务: 4(写命令 DTO + Controller 收 `RequestId`)
- 当前阶段: exec(已完成,**未勾选**)
- 上一轮: Round 11 — Task 4 exec 落地。按 G1–G8:新码 `RequestIdInvalid = 48028`;`WfWriteCmd` 基类持 `RequestId`,归一化/校验**全仓唯一一份**写在 `init` 里;**7 个**命令类改继承(不是 8 个 —— 同意/拒绝共用 `CompleteTaskCmd`),`TimeoutFireCmd` 保持裸 `IWfCommand`;4 个入参 DTO 加字段;7 个服务方法加可选参数并传进命令(`StartAsync` 从 `input.RequestId` 取);Controller 透传 7 处,**urge 刻意不传**(附注释说明)。校验用 `trimmed.Any(char.IsControl)` 而非只挡 `
`/`` —— 更难绕过且不必在源码里写转义。计划外必改 1 文件:`WorkflowReplaceabilityTests` 的两个 Fake 服务(签名跟随,正是 G6 承认的「实现者会破」的代价)。build 0 错 0 警;过滤器 **224/224**(215+9)。
- 下一步: Round 12 — **Task 4 review**(自审须声明)。变异点至少四处,每处**先 grep 确认文件真改了**、复原**只 checkout 被变异的那一个文件**:①`Normalize` 里去掉长度判断 → 65 字符用例应红;②去掉 `Any(char.IsControl)` → 换行用例应红;③`IsNullOrWhiteSpace` 改成 `is null` → 纯空白用例应红(命令里会变成空串);④任一 Controller 的 `input.RequestId` 换成 `null` → 对应贯穿用例应红。另需复核:`urge` 那条用例是**弱钉子**(给 urge 加透传它也不会红),要在 Findings 里写清;以及 `ProbingEngine` 用 `ActivatorUtilities.CreateInstance<WorkflowEngine>` 是否绕过了 `TryAdd` 的可替换性语义(记 P3)。

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
| 催办 | **默认不进 receipt**(可重复催办);翻转须改本表并补测试 |
| 通知失败 | 不得拖垮审批事务;但必须**结构化日志**(至少 `ILogger`)+ 可计数指标钩子;禁止继续纯静默 |
| `CompletedTime` | 实例进入终态时写入;旧数据可从 `InstanceCompleted` 事件回填,无法确定保持空 |
| `RequestId` 事件 | `wf_history` 增可空 `RequestId`;新数据写入,旧行 nullable |
| 范围外 | 不建 outbox、不建 execution/attempt、不加 Webhook、不 port React 工作流页、不新增 Backend Interface 面 |

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段重写)

> **Task 4 — 写命令 DTO + Controller 收 `RequestId`**(Round 10 写于 2026-08-31)。已读:`Engine/WfCommands.cs` 全部 9 条命令、`Services/WfRuntimeModels.cs` 的入参 DTO、`IWfTaskService`/`IWfInstanceService` 全部签名、两个 Controller 的写端点、`Abstractions/WorkflowErrorCode.cs` 全码表、`## Findings` 的 P2→Task 4 与 P3→Task 2/5。
> **Task 3 的 plan 已完成使命,记录留在 `## Findings` 与 `## Log`。**

### 读码所得(决策的事实底座,exec 不必重查)

- **入参 DTO 只有 4 个,却覆盖 8 个写命令**:`WfStartInput`(start)、`WfTaskActionInput`(approve/reject/transfer/delegate/return **以及 urge**)、`WfInstanceCancelInput`、`WfInstanceResubmitInput`。加字段只动 4 处,不是 8 处。
- **催办天然不进引擎**:`WfTaskService` 里只有 4 处 `engine.ExecuteAsync`,`UrgeAsync` 不在其中(它只追加事件 + 推通知,返回 `Task` 而非 `WfEngineResult`)。所以「urge 不做幂等」不需要任何开关 —— **不给它透传即可**。
- **服务方法收的是散参不是 DTO**(`ApproveAsync(taskId, userId, comment, ct)`),唯一例外是 `StartAsync(WfStartInput input, ...)` —— 它**不需要改签名**,`input.RequestId` 直接可用。要加参数的是另外 **7 个**。
- **控制器一律位置传参**(`..., input.Comment, cancellationToken)`),插参数必须同步改调用点。**测试不直接调这些服务**(全走 HTTP),所以改签名不会波及现有用例。
- **仓内 DTO 零 `DataAnnotations`,`TenonAdmin.AspNetCore` 也没有任何 `ModelState` 处理** —— 校验一律在代码里抛数字 `ErrorCode`(§13.2)。`[MaxLength]` 在本仓是死代码,不能用。
- **错误码表连续到 48027**(`CcNotFound`),**48022 是历史空号**。

### 决策点(exec 不得二次发挥)

| # | 决策 | 理由 |
|---|---|---|
| G1 | 对外名定 **`requestId`**,**不设别名**、不做 `IdempotencyKey` 映射 | 台账 `## Tasks` 与 `## DONE-CONDITION` 全文用的就是 `RequestId`;两个名字指同一件事正是三点钟要解码的那类东西。写进 `## 语义契约` |
| G2 | 4 个入参 DTO 各加 `string? RequestId { get; init; }` | 见上:4 个 DTO 覆盖 8 命令。`WfTaskActionInput` 被 urge 共用是**可接受的**,因为 urge 侧不透传(G7) |
| G3 | 新增 `abstract class WfWriteCmd : IWfCommand`,持 `RequestId`;**8 个写命令改继承它**,`TimeoutFireCmd` **不继承**(仍是裸 `IWfCommand`) | 归一化/校验只写**一份**(在 `init` 访问器里),8 个命令零复制;而「超时没有请求身份」直接由类型表达 —— Task 5 挂钩时 `is WfWriteCmd` 就是天然的排除条件,不必再写 `TimeoutFireCmd` 的特例分支 |
| G4 | 归一化规则(与 receipt 同源):`null` 或纯空白 → **`null`**(= 本次不做幂等);否则 `Trim()`;`Trim()` 后 **长度 > 64** 或**含换行符** → 抛新码 | ①列宽 `RequestKey(64)`,MySQL 非严格模式会静默截断诊断列(消化 `## Findings` 的 P2→Task 4);②`WfIdentityHash.NormalizeRequestKey` 明确拒换行符,DTO 层不拦,Task 5 就会拿一个**能进 DTO 却必然抛 `ArgumentException`**(→ 500)的值;③**空白必须在 DTO 层就变成 `null`**,否则 Task 5 把空白喂给 `NormalizeRequestKey` 同样是 500 —— `null` 才是「不做幂等」的合法表达 |
| G5 | 新错误码 **`RequestIdInvalid = 48028`**;**不填 48022 空号**、不复用 `ModelFieldTooLong` | 48017 的语义写死在「流程**模型**字段」,借它会让排障读到错误的方向;空号是历史,填回去可能与旧数据/旧文档撞车。48028 是表尾顺延 |
| G6 | 7 个服务方法加 `string? requestId = null`,**位置在 `CancellationToken` 之前**;`StartAsync` 不动签名 | 带默认值 → 消费者现有**调用**源码兼容;实现者(覆写 `IWfTaskService` 的消费者)会破,但工作流包尚未发包,现在改是最便宜的时刻 |
| G7 | Controller 透传 **7 处**(approve/reject/transfer/delegate/return + cancel/resubmit);**urge 不传** | 催办不进引擎,传了没人读,反而暗示它有幂等语义(与 `## 语义契约`「催办默认不进 receipt」冲突) |
| G8 | 本轮**只让值流到命令对象为止**:不碰 `ExecuteAsync`、不建 identity、不落 receipt(那是 Task 5);OpenAPI 变更留给 Task 10 的 `gen:api` | Task 边界,越界即本轮作废 |

### 改动清单(exec 只允许碰这 9 个文件)

1. `backend/src/TenonAdmin.Workflow/Abstractions/WorkflowErrorCode.cs` — 加 `RequestIdInvalid = 48028`
2. `backend/src/TenonAdmin.Workflow/Engine/WfCommands.cs` — 加 `WfWriteCmd` 基类(G3/G4 的唯一一份校验)+ 8 个写命令改继承
3. `backend/src/TenonAdmin.Workflow/Services/WfRuntimeModels.cs` — 4 个入参 DTO 加 `RequestId`
4. `backend/src/TenonAdmin.Workflow/Services/IWfTaskService.cs` — 5 个方法加参数(**不含 `UrgeAsync`**)
5. `backend/src/TenonAdmin.Workflow/Services/WfTaskService.cs` — 传进命令
6. `backend/src/TenonAdmin.Workflow/Services/IWfInstanceService.cs` — `CancelAsync`/`ResubmitAsync` 加参数
7. `backend/src/TenonAdmin.Workflow/Services/WfInstanceService.cs` — 传进命令(`StartAsync` 从 `input.RequestId` 取)
8. `backend/src/TenonAdmin.Workflow/Controllers/WfTaskController.cs` + `WfInstanceController.cs` — 透传 7 处
9. `backend/tests/TenonAdmin.Tests/WfRequestIdTests.cs` — 新增

### 步骤

1. G5 错误码 → 2. G3/G4 基类 + 8 命令改继承 → 3. G2 四个 DTO → 4. 服务接口 + 实现(7 处签名) → 5. Controller 透传 7 处 → 6. `dotnet build` 过 → 7. `WfRequestIdTests` → 8. `dotnet build -c Release` → 9. 指定过滤器闸门(当前 **215**,本 Task 后应 ≈ 221)。

### 测试清单(`WfRequestIdTests`,6 条)

命令对象是引擎的入参、本轮又不碰引擎,所以断言要靠**探针**:前置注册一个包住内置 `IWorkflowEngine` 的装饰器捕获 `IWfCommand`(照 `WfVersionCasTests` 的 `Overrides` + 事务内 SPI 注入写法)。

1. `approve` 带合法 `requestId` → 引擎收到的命令 `RequestId` 与请求**逐字一致**
2. `start` 带 `requestId`(DTO 直传路径,不经新增参数)→ 同上
3. 首尾带空格 → 命令里是 `Trim()` 后的值;**纯空白 → `null`**(不报错、不做幂等)
4. 65 字符 → 拒绝,信封 `code == 48028`;64 字符**通过**(边界两侧各一)
5. 含换行符 → 拒绝,`code == 48028`
6. `urge` 带 `requestId` → **正常成功**(不报错),且这条只是记录事实:催办不进引擎,该字段无人读

### 陷阱

- **`WfTaskActionInput` 被 urge 共用** —— 别顺手给 urge 也透传(G7)。
- 服务签名把 `requestId` 插在 `CancellationToken` 前,**控制器的位置实参必须同步改**;编译器会报,但别用命名实参糊过去掩盖漏改。
- **空白 → `null` 必须在 DTO/命令层完成**,否则 Task 5 的 `NormalizeRequestKey` 会抛 `ArgumentException`(500 而不是业务码)。
- 校验只能在 `WfWriteCmd` 的 `init` 里写**一份**;别在 7 个服务方法里各抄一遍(那正是 Task 3 收成一处要避免的形状)。
- 不填 48022 空号;不复用 `ModelFieldTooLong`。
- **不碰** `ExecuteAsync` / receipt / `wf_history` / 前端 / `gen:api`(Task 5/6/9/10)。
- 不提交 `TestResults/`。

### 给后续 Task 的锚点(本轮只记录,不实施)

- Task 5 的排除条件现成:`command is WfWriteCmd { RequestId: not null }` 才建 identity —— `TimeoutFireCmd` 与「没传 key 的请求」自然落在外面。
- Task 5 拼 identity 时 `RequestKey` 已被 Task 4 归一化过一遍;`WfIdentityHash.NormalizeRequestKey` 仍会再归一一次(幂等),**不要**因此把 DTO 层的校验删掉 —— 那层拦的是 500 与静默截断。
- Task 6 的 `wf_history.RequestId` 与本字段同源,直接取命令上的值。
- P3→Task 2/5 仍在:`ResultCode` 的 `0` 恒表示成功。

<!-- TASK1-PLAN-ANCHOR -->

## Tasks

> 任务顺序 = 依赖顺序。编号稳定;`## Log` 引用任务号。

- [x] **1. Operation receipt 实体 + `IdentityHash`**:新增 `wf_operation_receipt`(`WfOperationReceipt`)、`IdentityHashBuilder`(或同级静态类)、唯一索引 on `IdentityHash`、**无 HTTP** 的快照/归一化单元测试(已知输入 → 已知 hash,四库同一算法)。`CommandType`/`TargetType` 枚举或常量表在实现任务定稿。依据:数据库评审 §五。
- [x] **2. Receipt 服务 + 引擎事务内挂钩**: `IWfOperationReceiptService`(或引擎内 `virtual` 步骤,须 `TryAdd`) — `TryBeginAsync`(查已有 / 占位)与 `CommitAsync`(同事务写 `ResultJson`);与 `WorkflowEngine.BeginXxxAsync` 事务边界对齐。失败路径:业务抛错 → receipt 随事务回滚。`WorkflowReplaceabilityTests` 补一面。
- [x] **3. `WfInstance.CompletedTime`**:实体列 + 终态写入落点(`TakeTransitionOp`/`CompleteTaskOp` 终止分支等);CodeFirst 可空或带默认值;旧行回填策略按评审 §十(可从 `InstanceCompleted` 事件回填,测一条即可)。**不改** receipt 行为。
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
| 7 | plan | Task 3 plan 定稿(F1–F6):`CompletedTime` 为 nullable 无默认值列(nullable ADD COLUMN 四库均接受,不触发 `Version` 注释里的三步路);终态写入收成 `WfExecutionContext.WriteInstanceTerminalStatusAsync` 一处、三个落点改调用,**不动 M2b 的 `ClaimInstanceAsync`**;时间源 `ctx.TimeProvider`;回填走带 `IsAnyTable`/`IsAnyColumn` 守卫的一次性 HostedService + 两步 provider-neutral SQL(`SetColumns` 条件更新,不污染审计字段),无事件的旧行保持空;**不透出 DTO**(OpenAPI 面留给 Task 10 的 `RequestId`)。读码新事实:`Terminated` 全仓无写入点、实例终态不可逆、`ISeedData` 只插不改。未写产品代码。 |
| 8 | exec | Task 3 落地 7 文件:`CompletedTime` 可空列、终态写入收成 `WfExecutionContext.WriteInstanceTerminalStatusAsync` 一处(三个分支改调用,`ClaimInstanceAsync` 未动)、`WfCompletedTimeBackfill` 一次性 HostedService(守卫沿用 `DatabaseInitializer` 的 `IsAnyTable` + `GetColumnInfosByTableName` 写法,回避没验证过的 `IsAnyColumn`;回填 `InnerJoin`+`GroupBy MIN(CreateTime)` → 逐条 `SetColumns` 条件更新)、`WfCompletedTimeTests` 5 例(含 ToNode 分支保持空、回填幂等)。顺带修 Round 6 遗留的 `CS8619`。build 0 错 0 警;过滤器 **215/215**。未勾选。 |
| 9 | review+修+勾选 | Task 3 自审:四处变异,前两处(删赋值 / `UpdateColumns` 去列)各红 3/5;后两处(删事件类型过滤 / 回填改整对象更新)**仍绿** → 2×P2,当场补钉子后各转红 1/5。另记下「幂等断言是弱钉子」的覆盖真相(回填写入是确定性的,去掉任一 `CompletedTime == null` 守卫也写同样的值)。闸门 **215/215**、工作流包 0 警。**Task 3 打勾**。 |
| 10 | plan | Task 4 plan 定稿(G1–G8):对外名定 `requestId` 无别名;4 个入参 DTO 加字段(`WfTaskActionInput` 一个覆盖 6 个动词);抽 `WfWriteCmd` 基类把归一化(空白→`null`、`Trim`、≤64、禁换行)写成**唯一一份**,`TimeoutFireCmd` 不继承 → Task 5 的排除条件变成类型判断;新码 `RequestIdInvalid = 48028`(不填 48022 空号、不借 `ModelFieldTooLong`);7 个服务方法加可选参数(`StartAsync` 收 DTO 无需改),Controller 透传 7 处、**urge 不传**(它压根不进引擎)。测试靠引擎装饰器探针,6 条。未写产品代码。 |
| 11 | exec | Task 4 落地 10 文件:`RequestIdInvalid = 48028`;`WfWriteCmd` 基类(归一化 + ≤64 + 拒控制字符,**唯一一份**在 `init` 里),**7** 个命令类改继承(同意/拒绝共用 `CompleteTaskCmd`,故不是 8 个),`TimeoutFireCmd` 不继承;4 个 DTO 加 `RequestId`;7 个服务方法加可选参数;Controller 透传 7 处、urge 不传;`WfRequestIdTests` 9 例(含 `Theory` 的归一化 3 例与长度边界 2 例),靠包住内置引擎的装饰器探针断言真实调用链。计划外必改 `WorkflowReplaceabilityTests` 的两个 Fake(签名跟随)。build 0 错 0 警;过滤器 **224/224**。未勾选。 |

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
