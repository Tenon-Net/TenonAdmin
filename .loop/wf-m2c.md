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

- 轮次: 2
- max: 45
- 当前任务: 1(Operation receipt 实体 + `IdentityHash`)
- 当前阶段: exec(已完成,**未勾选**)
- 上一轮: Round 2 — 按 Plan 落地 4 个文件:`Entities/WfEnums.cs`(+`WfCommandType` 8 值 / `WfTargetType` 3 值)、`Entities/WfOperationReceipt.cs`(`BaseEntity` + `uk_wf_receipt_identity` 唯一索引)、`Engine/WfIdentityHash.cs`(静态 `Compute`,SHA-256 小写 hex)、`tests/WfIdentityHashTests.cs`(11 例)。`dotnet build -c Release` **0 错 0 警**;指定过滤器 **201/201 绿**(基线 190 + 11)。**计划外补了一条 `Enum.IsDefined` 守卫**(未定义枚举值会让 `ToString()` 退化成数字、把数值混进本该是枚举名的契约)——已配套测试,**留待 review 裁定是否保留**。
- 下一步: Round 3 — **Task 1 review**(独立复核,自审须在 `## Findings` 声明)。必做:①亲手跑 `dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"`;②**变异点**至少三处转红验证——改分隔符 `'\n'`→`'|'`、把拼接顺序里 `TargetId` 与 `ActorUserId` 对调、把 `ScopeSentinel` 从 `"-"` 改成 `""`,每处确认快照用例转红后**复原**;③核对 D1–D8 是否被 exec 二次发挥(尤其 `Enum.IsDefined` 这条计划外守卫);④确认没碰 `ExecuteAsync`/DTO/Controller/前端。**仍不勾选**,除非 0×P1 / 0×未修 P2。

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

> **Task 1 — Operation receipt 实体 + `IdentityHash`**(Round 1 写于 2026-08-31;读了数据库评审 §五/§八/§九、设计规划 §14.2/§15.1、`WfCc`/`WfHistory`/`WfInstance` 实体、`BaseEntity`/`DataEntity`/`IOrgScoped` 定义、`WorkflowEngine.ExecuteAsync`、`WfCommands.cs`、`WorkflowSetup.cs`)。

### 决策点(exec 不得二次发挥)

| # | 决策 | 依据 / 理由 |
|---|---|---|
| D1 | 基类用 **`BaseEntity`**,**不用 `DataEntity`** | `DataEntity` 带 `IOrgScoped` → 吃全局数据范围过滤器(只作用于 SELECT)。窄范围用户重试时**可能查不到自己刚写的 receipt** → 幂等静默失效、重复推进。机构维度改由显式**非空** `ScopeKey` 列承载,正是评审 §五「不要直接依赖包含 nullable `CreateOrgId` 的组合唯一索引」。`BaseEntity` 与同为 append-only 的 `WfHistory` 先例一致,且 `IRepository<>`/种子仍可用(`AuditEntity` 用不了仓储)。 |
| D2 | 表 `wf_operation_receipt`;唯一索引 `uk_wf_receipt_identity` on `IdentityHash`(`IsUnique = true`);辅助 `idx_wf_receipt_target` on `(TargetType, TargetId)` 仅排查用 | 评审 §八表格;唯一索引写法照 `SysJob` 的 `uk_sys_job_code` 先例 |
| D3 | 字段(评审 §五清单,顺序照抄):`ScopeKey`(Length 64,非空)、`CommandType`、`TargetType`、`TargetId`、`ActorUserId`、`RequestKey`(Length 64,非空)、`IdentityHash`(Length 64,非空)、`ResultCode`(int,0=成功)、`ResultJson`(BigString,可空);`Id`/`CreateTime`/审计由 `BaseEntity` 提供 | 评审 §五。`ResultJson` 存序列化后的 `WfEngineResult`,Task 2 才写入 |
| D4 | 新枚举 `WfCommandType`:`Start=1, Approve=2, Reject=3, Transfer=4, Delegate=5, Return=6, Cancel=7, Resubmit=8`;**不含 Urge / Timeout** | §14.2 列举的 8 个写命令。`Approve`/`Reject` 虽同属 `CompleteTaskCmd`,但**必须是两个 identity**(同人同任务同 key 先拒后批不能命中同一 receipt)。枚举只追加不重排(评审 §九 #6);`Urge` 见 `## 语义契约` 默认不进 receipt |
| D5 | 新枚举 `WfTargetType`:`Instance=1, Task=2, DefinitionVersion=3`。**`Start` 命令 `TargetType=DefinitionVersion`、`TargetId=DefinitionVersionId`** | 发起时实例尚不存在,没有 InstanceId 可锚;`(defVerId, actor, requestKey)` 足以定死一次发起 |
| D6 | 哨兵:`ScopeKey` 为 null / 空白 → 固定 `"-"`;**`RequestKey` 为 null/空白直接 `ArgumentException`**,不归一化 | 评审 §五「可空维度归一化为固定哨兵」。RequestKey 反向处理:「没传 key」和「传了空 key」若共享 identity,会让所有未传 key 的请求互相命中,比不幂等更危险。上层校验在 Task 4 |
| D7 | 拼接:六段按 `ScopeKey → CommandType → TargetType → TargetId → ActorUserId → RequestKey` 顺序,分隔符 `'\n'`;`long` 用 `InvariantCulture` 十进制;字符串 `Trim()` 后**保留大小写**;枚举用**枚举名字符串**(`nameof` 语义,非数值);UTF-8 → SHA-256 → **小写 hex**(`Convert.ToHexStringLower`) | 评审 §五全部六条细则。用枚举名而非数值:将来枚举值若因合并追加而变动,名字仍稳 |
| D8 | 落点:实体 `Entities/WfOperationReceipt.cs`;两个枚举追加进 `Entities/WfEnums.cs`;算法 `Engine/WfIdentityHash.cs` 的 **静态类**(`WfIdentityHash.Compute(...)`) | 不建接口——单实现的 seam 是 Task 2 的 `IWfOperationReceiptService`,hash 本身是纯函数,做成接口只会让「可替换」把不可逆契约变成可替换契约,与 §15.1 冲突 |

### 改动清单(exec 只允许碰这 4 个文件)

1. `backend/src/TenonAdmin.Workflow/Entities/WfOperationReceipt.cs` — 新增实体(SugarTable + 2 个 SugarIndex)
2. `backend/src/TenonAdmin.Workflow/Entities/WfEnums.cs` — 追加 `WfCommandType`、`WfTargetType`
3. `backend/src/TenonAdmin.Workflow/Engine/WfIdentityHash.cs` — 新增静态算法类 + XML doc 明写「发包后不可逆」
4. `backend/tests/TenonAdmin.Tests/WfIdentityHashTests.cs` — 新增纯单元测试(**无 HTTP、无 AppFactory**)

**无需**改 `WorkflowSetup.cs`:实体走 `ApplicationAssemblies` 程序集扫描(`WorkflowSetup.cs:28-30`),新表 CodeFirst 自动建。

### 步骤

1. 追加两个枚举 → 2. 写实体 → 3. 写 `WfIdentityHash.Compute` → 4. 写测试(先手算/先跑一次拿到 hex,再**写死成字面量常量**) → 5. `dotnet build -c Release` → 6. 跑指定过滤器闸门(基线 190,本 Task 后应为 190+8 左右)。

### 测试清单(`WfIdentityHashTests`,≥8 条)

1. **快照**:固定六元组 → 写死的 64 位 hex 常量(任何算法改动都转红 = §15.1 的不可逆锁)
2. `ScopeKey = null` 与 `ScopeKey = "-"` **同 hash**(哨兵归一化)
3. `ScopeKey = ""` / `"   "` 与 null **同 hash**
4. `RequestKey` 为 null / `""` / `"  "` → `ArgumentException`
5. **换位不撞车**:`Approve` vs `Reject`、`Instance` vs `Task`、`TargetId`↔`ActorUserId` 互换 → 三对 hash 各不相同
6. 值前后空白 `Trim` 后同 hash;大小写不同 → 不同 hash
7. 输出格式:`^[0-9a-f]{64}$`
8. 长度自洽:`Compute(...).Length == 64`(与实体 `Length = 64` 对齐,写成断言而非注释)

### 陷阱

- **别用 `DataEntity`**(D1);别给 receipt 加软删业务语义——`IsDelete` 来自 `BaseEntity`,永不置真。
- 分隔符必须是**参与值里不可能出现**的字符;`RequestKey` 由客户端给,若将来允许含 `\n` 需在 builder 里显式拒绝——本轮 builder 对含 `'\n'` 的输入**抛异常**,别让它悄悄产生歧义 hash。
- `Convert.ToHexStringLower` 是 .NET 9+ API,本仓 `net10.0` 可用;不要退回 `BitConverter.ToString().Replace("-","")`。
- 不要用 `string.GetHashCode()`/`HashCode.Combine`(进程内随机化,跨库不稳)。
- 本 Task **不碰** `WorkflowEngine.ExecuteAsync`、命令 DTO、Controller、前端——那是 Task 2/4/5。
- 不提交 `backend/tests/TenonAdmin.Tests/TestResults/`(已在工作区未跟踪,别 `git add -A`)。

### 给后续 Task 的锚点(本轮只记录,不实施)

- **事务唯一入口是 `WorkflowEngine.ExecuteAsync`**(`Engine/WorkflowEngine.cs:36` 的 `db.Ado.UseTranAsync`),8 个 `BeginXxxAsync` 都在这一个事务里。**Task 2/5 的 receipt 钩子挂 `ExecuteAsync` 一处即可覆盖全部 8 个写命令**,不必改 8 个方法——但 `TimeoutFireCmd` 也走这里,须按 D4 显式排除(超时不是客户端重试)。
- 通知静默吞异常在 `Engine/WorkflowEngine.cs:83` 与 `:94` 两处 `catch (Exception)` —— Task 7 的落点。

<!-- TASK1-PLAN-ANCHOR -->

## Tasks

> 任务顺序 = 依赖顺序。编号稳定;`## Log` 引用任务号。

- [ ] **1. Operation receipt 实体 + `IdentityHash`**:新增 `wf_operation_receipt`(`WfOperationReceipt`)、`IdentityHashBuilder`(或同级静态类)、唯一索引 on `IdentityHash`、**无 HTTP** 的快照/归一化单元测试(已知输入 → 已知 hash,四库同一算法)。`CommandType`/`TargetType` 枚举或常量表在实现任务定稿。依据:数据库评审 §五。
- [ ] **2. Receipt 服务 + 引擎事务内挂钩**: `IWfOperationReceiptService`(或引擎内 `virtual` 步骤,须 `TryAdd`) — `TryBeginAsync`(查已有 / 占位)与 `CommitAsync`(同事务写 `ResultJson`);与 `WorkflowEngine.BeginXxxAsync` 事务边界对齐。失败路径:业务抛错 → receipt 随事务回滚。`WorkflowReplaceabilityTests` 补一面。
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

### 待 plan/exec 填充

- (空)

## Log

| 轮次 | 阶段 | 摘要 |
|---|---|---|
| 0 | draft | 起草台账。M2b 收口 commit `bffec77`;基线 190/190。下一步 Round 1 Task 1 plan。 |
| 0b | handoff | 补 `## Loop 纪律` + `wf-m2c-handoff.md`;用户要求「严格按 loop」接续。 |
| 1 | plan | Task 1 plan 定稿:receipt 用 `BaseEntity`+显式 `ScopeKey`(避开数据范围过滤器)、唯一索引 on `IdentityHash`、`WfCommandType`(8 值,排除 Urge/Timeout)/`WfTargetType`(Start 锚 DefinitionVersion)、换行符分隔 + SHA-256 小写 hex、RequestKey 空值抛异常。锚点:`ExecuteAsync` 是唯一事务入口,Task 2/5 挂一处即可。未写产品代码。 |
| 2 | exec | Task 1 落地 4 文件:`WfCommandType`/`WfTargetType` 枚举、`WfOperationReceipt`(`BaseEntity`,唯一索引 on `IdentityHash`)、静态 `WfIdentityHash.Compute`、`WfIdentityHashTests` 11 例(含两条冻结快照常量)。build 0 错 0 警;过滤器 **201/201**(190+11)。计划外补 `Enum.IsDefined` 守卫,交 review 裁定。未勾选。 |

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
