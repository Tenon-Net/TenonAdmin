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

- 轮次: 36
- max: 45
- 当前任务: 10(gen:api + 契约漂移 + 验收,本台账最后一个 Task)
- 当前阶段: **勾选完成**
- 上一轮: Round 36 — Task 10 **review+勾选**(0×P1/0×P2,合并同一轮,沿用 Round 3/6/9/12/15/18/21 的先例)。`gh run view 33454582264` 最终确认:`backend-ci` 整体 `conclusion=success`(`template-smoke`/`build-test (sqlite)`/`build-test (mysql)`/`build-test (postgres)`/`build-test (sqlserver)` 全部 success,`nightly-alert` 按设计 skipped)。至此五个工作流对 commit `4679467` **全绿**:web-ci ✅/web-react-ci ✅/contract-drift ✅/docker-smoke ✅/backend-ci ✅。逐条核对 Task 10 验收清单(`## Plan` 步骤 6):①`## Tasks` 1–9 巡查仍为 `[x]` ✅;②`dotnet test` 指定过滤器基线 259(Round 32 本地实测 259/259,`backend-ci` 的 sqlite 腿双重验证)✅;③四库矩阵 CI 四腿逐条 success ✅(本轮新增证据);④`web` typecheck/lint 绿 + request key 复用(Round 32 本地 35/35,Task 9 交付机制未被打破)✅;⑤双模板 `schema.d.ts` SHA256 逐字相同(Round 32 独立核验 `36dbccb7...fa8335`)✅;⑥重复提交同 `RequestId` 返回首次 `WfEngineResult`(Task 5 `WfReceiptEngineTests.Same_request_id_replays_the_first_result_without_advancing_twice` 既有 HTTP 层证据,未新增测试)✅。**0×P1、0×P2** —— `## Tasks` 第 10 项打勾。**M2c 十项 Task 全部勾选**。
- 下一步: Round 37 — **GUARD 检查**。核对 `## Tasks` 十项皆 `[x]` 且 `## DONE-CONDITION` 六条闸门证据齐全(本轮已逐条列出对应证据),若确认无误,输出「✅ DONE — M2c 收口完成」并停止循环,不再排 `ScheduleWakeup`。

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

> **Task 10 — `gen:api` + 契约漂移 + 验收**(Round 31 写于 2026-09-01)。本台账最后一个 Task。已读:
> `.github/workflows/backend-ci.yml`(四库矩阵 `db: [sqlite, mysql, sqlserver, postgres]`,push/PR 触发,
> paths 含 `backend/**`;SqlServer 在 push/PR 只跑 `TEST_FILTER` 子集,nightly 才跑全量)、
> `.github/workflows/contract-drift.yml`(push/PR 触发,paths 含 `backend/**` 与两个 `schema.d.ts`,
> 起 MinimalHost → 两模板 `gen:api` → `git diff --exit-code` 判漂移)、`.github/workflows/web-ci.yml`/
> `web-react-ci.yml`/`docker-smoke.yml`(各自 paths 触发条件)、`scripts/gen-api.mjs`(共享生成器,读
> `TENON_API_TARGET` 默认 `http://localhost:5100`,写 `src/api/schema.d.ts`)、`scripts/check-contract-drift.mjs`
> (根 `CLAUDE.md` 文档化的本地等价闸门:起 MinimalHost 于 5101 → 两模板 `gen:api` → `git diff --quiet HEAD`
> 判漂移,**不做 web/web-react 互相比对**,只判「本地生成物是否已提交」)、`docs/react-template-ledger.md` E5
> 条目(2026-07-22,`7d82447`:契约漂移闸门的历史与本地等价验证配方 —— 已实测两模板 `gen:api` 后 IN SYNC)、
> 两模板 `package.json` 的 `gen:api` 脚本(逐字相同,均指向根 `scripts/gen-api.mjs`)、`backend/tests/.../
> WfReceiptEngineTests.cs`(确认 `Same_request_id_replays_the_first_result_without_advancing_twice`/
> `Retry_after_the_instance_finished_returns_the_first_result_not_a_conflict` 已是**真实 HTTP 层**
> `WorkflowAppFactory` 集成测试,DONE-CONDITION 最后一条已经被 Task 5 满足,不需要新测试)、`## Findings` 里
> Round 26 记的 **P2 → Task 10** 锚点(四库 CI 本机取不到证)。**Task 9 的 plan 已完成使命,记录留在
> `## Findings` 与 `## Log`。**
>
> **用户决定(Round 31,AskUserQuestion,已获授权)**:DONE-CONDITION「四库契约套件在 CI 矩阵四腿各绿」这条
> 本机无 Docker/无本地 PostgreSQL 取不到证据,用户在本轮明确选择**「push dev 拿 CI 信号」**(而非授权连局域网
> VM 的真实 PG/MySQL,也非跳过实证)。**这是本台账「默认不 push」纪律在本 Task 的唯一例外,范围仅限
> `git push origin dev`(不建 PR、不 push 到 main、不 force push)**,专为触发 `backend-ci.yml` 的四库矩阵 +
> `contract-drift.yml`/`docker-smoke.yml`/`web-ci.yml`/`web-react-ci.yml` 拿真实 CI 信号,不是本任务之后的
> 常态授权——后续任何 Task(理论上已无后续)仍不得不问自推。

### 读码所得(决策的事实底座,exec 不必重查)

- **契约漂移闸门已经是现成基础设施,不是本 Task 要新建的东西**:`contract-drift.yml`(`7d82447`,E5)+
  `scripts/check-contract-drift.mjs` 早就在 CI 里跑,根 `CLAUDE.md` 也文档化了本地等价用法
  (`git config core.hooksPath .githooks` 激活的 pre-push 钩子)。Task 10 的活是**实际跑一次**(本地
  `check-contract-drift.mjs` + 之后 push 拿 CI 信号),不是发明新脚本。
- **`check-contract-drift.mjs` 的判据是「本地生成物 vs HEAD」,不是「web vs web-react 互相比对」**:脚本
  跑完两模板 `gen:api` 后只对每个文件各自跑 `git diff --quiet HEAD`,不会直接断言两个 `schema.d.ts` 内容
  相等。DONE-CONDITION 明文要求「SHA256 一致」,这是**独立于漂移闸门的另一条断言**,exec 必须自己算
  两个文件的 hash 比对,不能拿漂移闸门的绿代替。
- **两模板 `schema.d.ts` 理论上必然逐字相同**:同一个 `scripts/gen-api.mjs`、同一个 `openapi-typescript`
  版本(两模板 `package.json` 锁定版本一致,`npm ci` 保证)、同一个后端 `/openapi/v1.json` 输出 ——
  SHA256 相等是这条流水线的**必然结果**,不是需要额外代码保证的东西;E5 历史记录(`react-template-ledger.md`
  行 261)已经实测过一次「两个 schema 均 IN SYNC」。exec 只是重新验证这个不变量在 M2c 改动后仍成立。
- **Task 4 起两侧 `schema.d.ts` 都没重生成过**(`grep requestId web/src/api/schema.d.ts` 零命中,Task 9
  读码已确认),所以本 Task 跑 `gen:api` **预期会产生真实 diff**:`WfStartInput`/`WfTaskActionInput`/
  `WfInstanceCancelInput`/`WfInstanceResubmitInput` 的 `requestId`(Task 4)、`WfInstance` 相关响应类型的
  `completedTime`(Task 3)、以及 `WfEngineResult`/回执相关如果有新暴露字段(Task 1–2、5)。**这些才是
  「计划内」的 diff 内容**——exec 必须读一遍实际 diff,确认改动都落在这几类字段上,没有跟 M2c 无关的
  意外漂移(意外漂移意味着后端在别的分支/提交里悄悄改了契约,超出本 Task 该处理的范围)。
- **`web-react/` 没有任何工作流代码**(`find web-react/src -iname "*workflow*" -o -iname "*wf*"` 零命中,
  Round 31 读码已验证)——它这次唯一要做的是**吃下 schema 变化后仍能 typecheck/lint/build**,不需要
  也不应该新增任何工作流页面或测试(禁区)。
- **仓内没有 `@ts-expect-error`**(`grep -rln "@ts-expect-error" web/src web-react/src` 零命中,Round 31
  读码已验证)——DONE-CONDITION 提到的「去掉因新字段产生的 `@ts-expect-error`」在本仓不适用,不必找。
- **DONE-CONDITION 最后一条(重复提交同 `RequestId` 返回首次 `WfEngineResult`,HTTP 层可观测)已经被
  Task 5 的 `WfReceiptEngineTests` 满足**,且是真实 `WorkflowAppFactory` HTTP 集成测试而非纯引擎层单测
  (`Same_request_id_replays_the_first_result_without_advancing_twice` 用 `PostEnvelope` 走两次
  `/api/v1/workflow/task/approve`,断言两次响应的 `instanceId`/`createdTaskId` 逐字相同、`wf_his_task`
  只有一行)。Task 10 **不需要为这条另写测试**,验收时直接引用这条已有证据。
- **`web/` 与 `web-react/` 的验证四件套已是既定纪律**(`react-template-ledger.md` 首行):
  `lint`/`test`/`tsc --noEmit`/`build`,且**本机内存紧张,一次只跑一个重进程,不与 `dotnet test` 并发**——
  exec 的步骤顺序必须串行,不能图快并发起多个 `dotnet`/`npm`/`node` 重进程。
- **推送后的 CI 面**:`push` 到 `dev` 会同时触发 `backend-ci`(四库矩阵 + SqlServer PR 子集)、
  `contract-drift`(应绿,因为本 Task 会先在本地把两个 `schema.d.ts` 更新到位再提交)、`docker-smoke`
  (`single`+`multi`)、`web-ci`、`web-react-ci`——五个工作流。`backend-release.yml` 只在 `v*` tag 触发,
  本次 push 不会跑,不必等它。review 阶段要逐个工作流确认结论(不能只看第一个绿就收工)。

### 决策点(exec 不得二次发挥)

| # | 决策 | 理由 |
|---|---|---|
| E1 | exec 用 `node scripts/check-contract-drift.mjs` 完成两模板 `gen:api` 的重生成(它会自动起停 MinimalHost、串行跑两次生成),**不手写另一套「手动起 dotnet run + curl」流程**。脚本大概率以非零退出(检测到漂移)结束,这是**预期结果**,不是失败——它已经把两个文件写到位,只是「跟 HEAD 有 diff」这句话本身就是本 Task 要证实的事。 | 复用现成、已验证过的工具(E5 历史记录),避免重新发明一套本机等价流程;脚本内部已处理 Windows `taskkill` 清进程等细节,自己写容易漏 |
| E2 | exec 额外单独算一次 `sha256` 比较 `web/src/api/schema.d.ts` 与 `web-react/src/api/schema.d.ts`,作为独立于 `check-contract-drift.mjs` 的第二条证据,写进 `## Log`/`## Findings`。 | DONE-CONDITION 字面要求「SHA256 一致」,不能只靠漂移脚本的「各自不漂移」推断「两者相等」——两条证据分开记录更扎实 |
| E3 | 生成后**先读 diff 内容**(`git diff -- web/src/api/schema.d.ts`),确认改动只落在 `requestId`/`completedTime`/回执相关字段上,**没有**意外的、与 M2c 无关的契约变化;若有意外字段,记 P1/P2,不得直接无视提交。 | 契约漂移闸门只能告诉「是否漂移」,告诉不了「漂移得对不对」——这一步是人工核对,避免把无关的、可能有问题的后端契约变化悄悄夹带进本次收口提交 |
| E4 | 本地闸门顺序:①`dotnet build -c Release` + `dotnet test` 指定过滤器(确认 259 基线不降,backend 本身这轮没改代码,预期依旧 259);②`cd web && npm run typecheck && npm run lint && npx vitest run src/workflow/`(Task 9 的 35 条不受影响);③`cd web-react && npm run typecheck && npm run lint && npm run build`(无工作流测试可跑,以 typecheck/lint/build 三件套代替「验」,不新增测试——四件套里 `test` 对 web-react 而言等于跑全量 97 文件会 OOM,按 `react-template-ledger.md` 的既有教训跳过全量 `npm test`,只用 build 兜底「至少能编译」)。**一次只跑一个重进程**,不并发。 | 严格按既定验证纪律执行,不因为是收尾轮就抄近道;web-react 沿用台账里已经吃过教训的「分片/避免全量 OOM」经验,不重蹈 |
| E5 | 本地闸门全绿后,**一次性提交**(`schema.d.ts` × 2 + 台账),再 `git push origin dev`(用户 Round 31 已授权,仅此一次、仅此分支、非 force)。**不开 PR**(dev 是长期分支,不是 feature 分支流程)。 | 避免「推了但本地闸门没跑全」的半成品状态占用 CI 资源;PR 流程不是本仓 dev 分支的既定工作方式(`git log` 显示直接提交 `dev`) |
| E6 | push 后进入 review:轮询 `gh run list --branch dev --limit 10` 确认五个工作流(`backend-ci`/`contract-drift`/`docker-smoke`/`web-ci`/`web-react-ci`)都已触发且最终 `conclusion=success`;`backend-ci` 要展开看 `db` 矩阵四条腿逐条确认(不能只看整体 job 汇总),尤其 postgres/mysql/sqlserver 三条不能被漏看成「跳过」。**若 CI 仍在跑,记录当前状态,下一轮继续轮询**,不得瞎猜「应该会过」就直接勾选。 | DONE-CONDITION 明文要求「四腿各绿」是**逐腿**的断言,不是「整个 workflow 绿」的汇总断言(某一腿 fail-fast=false 下可能单独失败而 workflow 汇总仍显示其他腿的绿);CI 有真实延迟,不能靠猜测代替观测 |
| E7 | Task 10 **不修复**已知的 P3(`ActivatorUtilities.CreateInstance<WorkflowEngine>` 绕过 `TryAdd`,Round 8 起挂账)——这是既有测试代码风格问题,不在「gen:api + 契约漂移 + 验收」范围内,继续挂账,写进最终收尾说明避免被误读成「本该收口却漏了」。 | 严格限定 Task 10 范围,避免收尾轮「顺手」扩面违反 Loop 纪律 |

### 改动清单(exec 只允许碰这些文件 + 两条 CI 只读操作)

1. `web/src/api/schema.d.ts` —— 重生成(`check-contract-drift.mjs` 自动写入)
2. `web-react/src/api/schema.d.ts` —— 重生成(同上)
3. `.loop/wf-m2c.md` —— 台账
4. **只读**:`git push origin dev`(不建分支、不开 PR、不改任何 workflow yaml)
5. **只读**:`gh run list`/`gh run view` 轮询(review 阶段)

**预期计划外:0**。若 E3 核对 diff 发现字段超出 M2c 范围(比如 CC/定义相关端点也变了形状),按「记 P1,不擅自处理」——那属于本 Task 发现的、但修复动作超出「gen:api + 验收」职责的问题,交给 Findings 挂账,不在本 Task 里顺手改后端契约。

### 步骤

1. `node scripts/check-contract-drift.mjs`(根目录跑,期望非零退出 = 检测到漂移,两个 `schema.d.ts` 已被就地重写)。
2. `git diff -- web/src/api/schema.d.ts web-react/src/api/schema.d.ts` 读全部 diff,核对改动范围(D3/E3)。
3. 算 SHA256:两个文件逐字节比较(`certutil -hashfile` 或 Node `crypto` 均可),记录两串 hash 到 `## Log`。
4. 依次跑(不并发):`dotnet build -c Release` → `dotnet test` 指定过滤器 → `cd web && typecheck/lint/vitest run src/workflow/` → `cd web-react && typecheck/lint/build`。
5. 全绿后一次性 `git add` 两个 `schema.d.ts` + 台账,提交(英文 conventional commit),`git push origin dev`。
6. review 阶段(可能跨轮):`gh run list --branch dev --limit 10` 找到本次 push 触发的五个工作流运行,逐个 `gh run view <id>` 确认结论;`backend-ci` 展开 `db` 矩阵四条腿。全绿前不得勾选。

### 验收清单(对应 `## DONE-CONDITION` 逐条,不是新测试,是证据清单)

1. `## Tasks` 十项全勾 —— Task 10 本身勾选时自然满足,勾选前逐一巡查 1–9 仍是 `[x]`(防止之前某轮误操作漏勾/多勾)。
2. `dotnet test` 指定过滤器绿(基线 259,只增不减)—— 本地步骤 4 + CI `backend-ci` 的 sqlite 腿双重验证。
3. 四库矩阵 CI 四腿绿 —— push 后 `gh run view` 逐腿核对(E6)。
4. `web` typecheck/lint 绿 + request key 复用 —— 本地步骤 4(Task 9 已交付复用机制,本轮只确认 schema 变化没打破它)。
5. 双模板 `schema.d.ts` SHA256 一致 —— 步骤 3 的独立比对(E2)。
6. 重复提交同 `RequestId` 返回首次结果 —— 引用 Task 5 `WfReceiptEngineTests` 既有证据,不新增测试。

### 陷阱

- **`check-contract-drift.mjs` 非零退出是预期行为,不是本 Task 的失败**——它检测到「HEAD 上的 schema 陈旧」就会 `fail()` 退出 1,但此时文件已经写好在工作区,`git status` 能看到改动;不要把这个退出码误判成阻塞。
- **不要用「两个漂移检查各自绿」代替「SHA256 一致」**——两者断言的对象不同(各自 vs HEAD / 两者互相),E2 的独立哈希比对不可省。
- **CI 轮询别用短轮次硬等**——SqlServer 即使是 PR 子集也可能要几分钟,`docker-smoke` 的 `multi` job 双副本编排也要时间;review 轮次如果 CI 还没跑完,如实记录「仍在跑」,下一轮继续查,不得因为等不及就假设通过。
- **`gh run list` 默认按时间倒序,要认准 SHA**——本次 push 的 commit SHA 记入 `## Log`,轮询时用 `--commit <sha>` 或核对 `headSha` 字段,避免看错成别的分支/别人推的旧跑。
- **push 权限只限 `dev`**——不得顺手 `push --force`、不得推 `main`、不得开 PR(用户只授权了「push dev 拿 CI 信号」这一件事)。
- **不提交 `TestResults/`**。

### 给后续维护者的收尾说明(勾选轮写,先记在这)

- Task 10 完成后,`start/index.vue`/`detail.vue` 里手写的 `requestId` 字段理论上可以被新生成的 schema 类型覆盖替代(纯类型层收尾,Task 9 已预告),但**不是**本 Task 强制项,留给后续按需处理。
- P3(`ActivatorUtilities` 绕过 `TryAdd` 的测试写法)与 P2 里已如实披露的 SQLite 射程局限(`WfPersistenceContractTests` 类注释)均**继续挂账**,不因为 M2c 收口而被误判为"已解决"。

## Tasks

> 任务顺序 = 依赖顺序。编号稳定;`## Log` 引用任务号。

- [x] **1. Operation receipt 实体 + `IdentityHash`**:新增 `wf_operation_receipt`(`WfOperationReceipt`)、`IdentityHashBuilder`(或同级静态类)、唯一索引 on `IdentityHash`、**无 HTTP** 的快照/归一化单元测试(已知输入 → 已知 hash,四库同一算法)。`CommandType`/`TargetType` 枚举或常量表在实现任务定稿。依据:数据库评审 §五。
- [x] **2. Receipt 服务 + 引擎事务内挂钩**: `IWfOperationReceiptService`(或引擎内 `virtual` 步骤,须 `TryAdd`) — `TryBeginAsync`(查已有 / 占位)与 `CommitAsync`(同事务写 `ResultJson`);与 `WorkflowEngine.BeginXxxAsync` 事务边界对齐。失败路径:业务抛错 → receipt 随事务回滚。`WorkflowReplaceabilityTests` 补一面。
- [x] **3. `WfInstance.CompletedTime`**:实体列 + 终态写入落点(`TakeTransitionOp`/`CompleteTaskOp` 终止分支等);CodeFirst 可空或带默认值;旧行回填策略按评审 §十(可从 `InstanceCompleted` 事件回填,测一条即可)。**不改** receipt 行为。
- [x] **4. 写命令 DTO + Controller 收 `RequestId`**: `Start/Approve/Reject/Transfer/Delegate/Return/Cancel/Resubmit` 输入 DTO 增 `RequestId`(或 `IdempotencyKey`,plan 阶段二选一对外名、另一名作别名/映射);Controller 透传。OpenAPI 变更 → 留给 Task 10 `gen:api`。**不含 Urge**(默认)。
- [x] **5. 引擎写路径接 receipt**:上述 8 个 `BeginXxxAsync` 入口在事务开头解析 identity → 命中则直接返回缓存 `WfEngineResult` → 否则执行现有 Op 链 → 成功则落 receipt。覆盖「串行双提交」「并发双提交仅一次推进」「业务失败无 receipt」「终态重试返回首次结果」的集成测试(单库,≥6 条,附变异点)。
- [x] **6. `wf_history.RequestId`**:列 + `AppendHistoryAsync` 写路径传入;与 receipt 的 `RequestKey` 同源。测试:重复请求不重复追加**可观测**历史(或命中 receipt 根本不进引擎 — plan 阶段二选一并写进契约)。
- [x] **7. 通知失败可观测**: `WfDefaultNotifier` 注入 `ILogger<WfDefaultNotifier>`(或内核既有日志抽象),`catch` 改 `LogWarning`/`LogError` 结构化字段(`InstanceId`,`Event`,`UserId`,异常);可选 `IOptions` 开关保留静默模式给测试。补一条「publisher 抛错 → 审批仍成功 + 日志有条目」测试。不引入新 NuGet。
- [x] **8. 四库持久化契约套件**:新建 `WfPersistenceContractTests`(或同级),**同一套用例**经 `TestDb.DbType` 在四库 CI 腿各跑:①`IdentityHash` 快照;②receipt 唯一性;③并发 CAS(实例/Token/任务至少各一条);④事务回滚 receipt 不残留;⑤超时领取 vs 人工 `Approve` 仅一方胜出;⑥终态保护。不复制 190 条全集,只钉持久化契约(目标 **12–20** 条,plan 阶段列清单)。SqlServer PR 腿若已有 `TEST_FILTER`,评估是否纳入子集或 nightly — plan 阶段读 `.github/workflows/backend-ci.yml` 后定。
- [x] **9. Vue request key 生命周期**: `web/` 发起页 + 实例详情写操作:一次用户动作(打开弹窗/点一次按钮)生成 UUID,该动作重试(含 axios 重试若存在)复用;成功或明确失败后丢弃;新动作新 key。按钮防连点保留。`src/workflow/` 或 composable 单点实现,避免每页复制。typecheck/lint/vitest 绿。
- [x] **10. `gen:api` + 契约漂移 + 验收**:双模板 `gen:api`;SHA256 一致;去掉因新字段产生的 `@ts-expect-error`(若有)。可选:Playwright 或 API 级「双 POST 同 key → 同一 instanceId/同一结果」轻量验收(不强制浏览器截图,除非协调者要求)。**勾选本 Task 前**跑齐 DONE-CONDITION 全闸门。

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

### Task 7 review(Round 21,2026-08-31)

> **⚠ 自审声明**:与 exec 同一 context(会话规则禁止未经用户要求派子 agent)。仍以**变异测试**代替第二双眼睛,每处**先 `grep` 确认文件真改了**再跑,复原**只 checkout 被变异的那一个文件**。

**变异点验证**(跑 `~WfNotifyLoggingTests` → 复原):

| 变异 | 结果 | 说明 |
|---|---|---|
| **把 `WfDefaultNotifier` 的 3 个 try/catch 加回去** | **红 4/5** | 四条失败路径**同时**变回无声。这是本 Task 核心论点的实证:双层网存在时,内置实现的失败到不了外层,加多少日志都白搭 |
| 删掉催办那处 `LogWarning` | **红 1/5** | 精准命中催办那条,不误伤其余三条 —— 四处日志确实各管各的路 |
| `LogWarning(ex, ...)` 丢掉异常形参 | **红 1/5** | 用例 1 的 `Assert.All(..., NotNull(Exception))` 接住 |
| 成功路径也无条件记一条 Warning | **红 1/5** | 用例 5 —— 「不能省」这句话有证据了 |
| 引擎的 catch 里加 `throw;` | **红 4/5** | 连**发起**请求本身都挂(发起就会触发待办到达通知)。反向确认「通知绝不拖垮业务」仍然成立 |

**上一轮标记要复核的两条,结论都是钉子有效**:

1. **用例 1 改成「至少一条」并没有弱化**。当初担心「某处 catch 漏记而另一处记了,它仍绿」——变异②证明漏记催办会被催办那条抓到,变异③证明丢异常会被 `Assert.All` 抓到。数量本就不该硬钉:两条待办到达通知都失败是**正确行为**(发起给 a 建待办、同意后给 b 建待办),硬钉 1 条反而是错的断言。
2. **`LogSink` 对所有类别返回同一个 logger,没有污染用例 5**。变异④能让用例 5 转红,说明它对「多出来的 Warning」是敏感的;而未变异时它绿,说明内核其他组件并没有产出含「通知失败」的 Warning。敏感 + 干净,两头都验到了。

**核对 K1–K8**:K1(内层 3 个 catch 已删,类注释写明为什么)✅ / K2(外层 4 处各记一条,不是重复而是四种上下文)✅ / K3(级别一律 Warning)✅ / K4(异常走 `exception` 形参,未拼 `ex.Message`;字段含 `InstanceId` 等)✅ / K5(未加 `IOptions` 开关)✅ / K6(三个类各注入 `ILogger<T>`,引擎 `<remarks>` 已并列记一笔)✅ / K7(自制最小 `ILoggerProvider`,未引第三方)✅ / K8(未碰通知内容/时机/`IRealtimePublisher`,catch 仍是 `catch (Exception)`)✅。改动面 = 计划内 5 文件 + **plan 已预先声明**的 `WorkflowMultiLeaderSnapshotTests`(直接 `new WorkflowEngine`,补 `null!`)✅。

**闸门**:全量 `--no-incremental` Release 构建 0 错、13 警(全为 `Core`/`Services` 既有基线,工作流包 0 警);`dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` → **245/245 绿**。

**P1**:0 条。**P2**:0 条。→ 满足勾选条件。

### 跨任务待办(不阻塞 Task 1,后续任务必须消化)

- **P2 → Task 4**:`RequestKey` / `ScopeKey` 列宽都是 **64**,而 `WfIdentityHash.Compute` 对长度不设限。写命令 DTO 必须把 `RequestId` 卡在 **≤64**(配一条超长即拒的测试):否则 MySQL 非严格模式静默截断诊断列(identity 由完整值算出,不受影响,但排查时看到的是截断值),严格方言下直接插入报错。
- **P3 → Task 2**:落库的 `ScopeKey`/`RequestKey` 必须写**归一化后**的值(哨兵 + `Trim()`,复用 `WfIdentityHash.ScopeSentinel`),不能一边存原值一边用归一化值算 hash,否则诊断列与 identity 对不上。
- **P2 → Task 8**:`WfOperationReceiptService.TryBeginAsync` 靠「唯一索引冲突 → 二次 SELECT」认赢家,这在 **PostgreSQL** 上有方言陷阱 —— PG 一旦语句报错就把整个事务置为 aborted,紧接着的 SELECT 会直接报 `current transaction is aborted, commands ignored until end of transaction block`,于是「查到赢家」这条路在 PG 上走不通。SQLite/MySQL/SqlServer 不这样。**单库套件永远看不见这条**,四库套件必须专门钉;修法(savepoint / `ON CONFLICT DO NOTHING` / 先查后插的窗口容忍)留给 Task 8 的 plan 定。
- **P3 → Task 5/8**:测试里用 `ActivatorUtilities.CreateInstance<WorkflowEngine>` 构造内置引擎来做装饰器探针,绕过了 `TryAdd` 的可替换性语义(消费者替换 `IWorkflowEngine` 时探针装的仍是内置实现)。Task 5/8 若还要装饰引擎,先想清楚是要「内置引擎的行为」还是「当前注册的实现」;两者不同,别把这个写法当消费者示范。
- **P2 → Task 10**:DONE-CONDITION 写着「四库契约套件在 CI 矩阵四腿各绿」,但**本机取不到这个证据** —— 开发机无 Docker,局域网 VM 上的 PG/MySQL 是用户自己的服务(端口已占、凭据未知,而 `TestDb` 会在上面建/删 `tenon_it_*` 库,不在本轮授权内)。于是 Task 8 的 PG 用例只能在 SQLite 上验「不倒」,验不了「修前红」。Task 10 收口前必须就取证方式向用户要一个决定:**push 一次拿 CI 四腿信号**,或**授权连一个真实 PG**。**不许拿 SQLite 的绿当四腿的绿**(Round 22 定)。
- **P3 → Task 2/5**:`ResultCode` 是 `int`,`TenonAdmin.Core.ErrorCode` 也是 int 枚举;映射时 `0` 恒表示成功,别让 `ErrorCode` 的某个具体值落到 `0`。

### Task 8 review(Round 24,2026-08-31)

> **⚠ 自审声明**:与 exec 同一 context(会话规则禁止未经用户要求派子 agent)。仍以**变异测试**代替第二双眼睛;每处变异**先 `grep` 确认文件真改了**,复原**只 `checkout` 被变异的那一个文件**。

**闸门**:`dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` → **259/259 绿**(1m34s)。变异全部复原后 `git status` 只剩未跟踪的 `TestResults/`。

**变异点验证**(改 → 跑 `~WfPersistenceContractTests`(14 条) → 复原):

| # | 变异 | 结果 | 判读 |
|---|---|---|---|
| M1 | `UseNestedSavepoint` 恒 `false`(等于去掉 savepoint 两条语句) | **绿 14/14** | **未取证**,不是钉子失效。SQLite 的语句错误不中止事务,这条只能在 postgres 腿观察。**严禁把这个绿读成 PG 的证据** |
| M2 | `WfOperationReceipt` 的 `IsUnique = true` → `false` | **红 4/14** | A1/A2/A3/A4 全红 —— 唯一索引真被 CodeFirst 建出来,且 A 段四条都真的依赖它 |
| M3 | `ResultJson` 的 `CodeFirst_BigString` → `Length = 200` | **绿 14/14** | **本轮唯一真发现**,见下面的 P2 |
| M4 | `AppendHistoryAsync` 的 `RequestId` → `RequestId ?? ""` | **红 1/14** | C2 `Legacy_history_rows_read_null_for_request_id_not_empty_string` |
| M5 | `ScanDueTasksAsync` 删掉 `(t.DueTime == cursor && t.Id > afterTaskId)` 半边 | **红 1/14** | E1 `Tasks_sharing_one_due_time_are_all_scanned_across_pages` |
| M6 | `ClaimInstanceAsync` 删掉 `i.Version == current` | **绿 14/14** | 本套件**本来就不该**红(L4:不复制 M2b);追加取证见下 |

**M6 的追加取证(避免把「不该覆盖」误记成「没覆盖」)**:同一处变异改跑**全过滤器** → **红 1/259**,红的是 `WfVersionCasTests.Instance_losing_cas_returns_48004_and_rolls_back_whole_transaction`。产品 CAS 条件由 M2b 的套件钉住,Task 8 的 D 段钉的是**方言层的 affected-rows 口径**(自己发裸 UPDATE,不经产品代码)。所以这不是覆盖洞,是 `## Plan` 变异表里「应红:用例 9、10」那一格**预期写错了** —— 已在此更正,后续 review 不必再追。

**P1**:0 条。

**P2(阻塞 Task 8)**:1 条,**待 Round 25 修**。

1. **B 段两条在 SQLite 腿上是恒真断言,而类注释的射程声明只提了 PG、漏了列宽。** M3 把 `ResultJson` 从 `CodeFirst_BigString` 改成 `Length = 200` 之后,写入 > 8000 字符的中文载荷**照样通过**——SQLite 的类型亲和性根本不执行列宽,`varchar(200)` 收下任意长度;追加取证:同一变异跑全过滤器 259/259 仍绿(1m17s)。同理 B2 的「64 列真能装 64」在 SQLite 上也判别不了。这两条在 mysql / postgres / sqlserver 三腿上都是真钉子(CHANGELOG #26 那类事故正出在 SqlServer),**测试本身没写错**;错在**本地一句「B 段绿了」会被下一个人读成「列宽已验证」**,而这正是本轮 M1 已经防住、B 段却漏防的同一种假象。**修法(不改产品代码、不加 `SkippableFact`)**:B1/B2 各补一句射程注释,并把类注释的射程声明从「PG 相关断言」扩写成「PG 事务语义 + 列宽/列类型两类断言在 SQLite 腿都不具判别力」。

**P3(挂账)**:

1. **`RollbackNestedAsync` 失败会重新引入它自己要修的那个「真因被顶替」**:catch 里若 `ROLLBACK TO SAVEPOINT` 本身抛错,新异常会盖掉原始的插入异常。实际触发面很窄——语句级错误(含唯一冲突)之后回滚到点在 PG 上恰恰是能成功的,要它失败基本得是连接已断,那时一切都已经坏了。若将来要收紧,写法是「回滚不成就立刻 `throw;` 原始异常,别再去做那次 SELECT」。**本轮不动**(review 阶段只修 P1/P2)。
2. **`ReleaseNestedAsync` 抛错会掉进 catch,继而对一个已释放的点发 `ROLLBACK TO SAVEPOINT`**。同上,窄且不致命,记录以免将来当新缺陷重查。

### Task 8 修 Findings(Round 25,2026-08-31)

只修 Round 24 记的那 1 条 P2,未扩面:

1. **类注释射程声明扩写**:原文只提「PG 相关断言在 SQLite 腿加不加 savepoint 都是绿的」,现拆成①②两类,②补上「B 段两条列宽/列类型断言在 SQLite 腿是恒真断言,Round 24 mutation 已实测:把 `ResultJson` 换成 `Length = 200` 依旧全绿」,并点名两条测试方法。
2. **B1/B2 方法级文档各补一段射程说明**,分别指向「本条在 SQLite 腿不具判别力,真正的钉子在 mysql/postgres/sqlserver 三腿」。

**未改动**:产品代码(0 文件)、测试断言逻辑(0 处)、`SkippableFact` 或任何方言跳过写法(遵守 L6)。`git diff --stat` 只有 `WfPersistenceContractTests.cs` 一个文件,+14/-3,全部是文档注释。

**闸门**:`dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` → 259/259 绿(1m12s)。

**P1**:0(沿用 Round 24 结论)。**P2**:1 条,**本轮已修**。→ 待 Round 26 核对后勾选。

### Task 9 review(Round 29,2026-09-01)

> **⚠ 自审声明**:与 exec(Round 28)同一 context(会话规则禁止未经用户要求派子 agent),不满足「换人复核」。用**变异测试**替代第二双眼睛,三处变异**每处先 grep 确认文件真的改了**再跑,复原只 `git checkout` 被变异的单个文件。

**闸门**(变异前基线):`npx vitest run src/workflow/` → 35/35 绿。

**变异点验证**(改 `useRequestKey.ts` → 跑 `useRequestKey.spec.ts` → grep 复原 → 复跑确认绿):

| 变异 | 结果 | 说明 |
|---|---|---|
| `settle()` 对 `'network'` 也清空 key | **红 1/6** | 用例 2,与 Round 27 变异点表一致 |
| `value()` 去掉惰性判断(每次强制重新生成) | **红 2/6** | 用例 1 **和** 2 —— Round 27 的表只列了用例 1;去记忆化天然也破坏"网络重试复用同一个 key"(用例 2 的前提是"多次 `value()` 拿到同一个值"),覆盖面比预期更强,**不是缺口**,是预期记录的一处更正 |
| `classifyOutcome` 恒返回 `'error'` | **红 1/6** | 用例 6,与 Round 27 变异点表一致 |

**第四项(人工核对,无既有 spec 覆盖 urge 分支)**:读 `detail.vue` 现状 ——`submitAction()` 里 `requestId = kind === 'urge' ? undefined : requestKey.value()`;`dispatch()` 的 `urge` 分支调 `wfTaskApi.urge({ taskId })`,字面量里根本不含 `requestId`/`body`;`openAction()` 只对 `kind !== 'urge'` 调 `requestKey.reset()`。urge 既不生成也不透传 `requestId`,与语义契约「催办默认不进 receipt」及 Round 27 决策点 D4 一致。

**逐条核对改动清单/决策点**:`git diff HEAD~1 HEAD` 精确等于计划内 6 个源码文件 + 台账;`start/index.vue` 的 `submit()` 已改成 `const body`(D5),`settle('success')` 落在 `wfInstanceApi.start(body)` 成功之后、`router.push` 之前;`api/workflow.ts` 的 `cancel`/`resubmit` 手写类型各加 `requestId?: string | null`(D6);`COMPONENTS.md` 新增一节格式对齐 `useConfirm`/`useTabTitle`;**未碰** `web-react/`、未跑 `gen:api`、未改 `useConfirm.ts`/`api/client.ts` 中间件(D7)。

**收尾闸门**(变异全部复原后重跑):`npm run typecheck` 0 错;`npm run lint`(oxlint)0 错 0 警;`npx vitest run src/workflow/` → 35/35 绿。`git status --short` 干净(仅常驻的 `TestResults/`)。

**P1**:0 条。**P2**:0 条。→ 满足勾选条件,留给 Round 30 按「一轮一阶段」纪律打勾。
### Task 10 review+勾选(Round 36,2026-09-01)

> **⚠ 自审声明**:与 exec(Round 32)同一 context(会话规则禁止未经用户要求派子 agent)。本 Task 未写产品代码(只重生成生成物),**review 的证据来源是 Round 32 的本地闸门记录 + Round 33–36 跨四轮的真实 CI 观测**,不是代码变异——这是 Task 10 的性质决定的(gen:api + 验收,没有可变异的业务逻辑),前九个 Task 的「先 grep 确认真改、变异转红后单文件复原」纪律在本 Task 不适用,改用「逐条核对 DONE-CONDITION 证据来源」代替。

**CI 轮询全过程**(Round 33→36,commit `4679467`):

| 工作流 | Round 33 | Round 34 | Round 35 | Round 36(本轮) |
|---|---|---|---|---|
| web-ci | success | — | — | — |
| web-react-ci | success | — | — | — |
| contract-drift | success | — | — | — |
| docker-smoke | in_progress | **success** | — | — |
| backend-ci | in_progress(4 腿 in_progress) | in_progress(sqlite/mysql/postgres 转绿,sqlserver 仍跑) | 仅剩 sqlserver 未收敛 | **success**(sqlserver 收尾,四腿 + template-smoke 全 success,`nightly-alert` 按设计 skipped) |

**逐条核对验收清单(`## Plan` 步骤 6,对应 `## DONE-CONDITION` 六条)**:

1. `## Tasks` 十项全勾 —— 打勾前巡查 1–9 仍为 `[x]`,无误勾/漏勾 ✅
2. `dotnet test` 指定过滤器基线 259,只增不减 —— Round 32 本地实测 **259/259**;`backend-ci` 的 `build-test (sqlite)` 腿独立复验 ✅
3. 四库契约套件 CI 矩阵四腿各绿 —— 本轮 `gh run view 33454582264` 最终确认 `build-test (sqlite/mysql/postgres/sqlserver)` **全部 conclusion=success** ✅(这是本 Task 存在的核心理由:本机无 Docker/无本地 PG 取不到的证据,由用户授权 push 换来)
4. `web` typecheck/lint 绿 + request key 复用 —— Round 32 本地 `typecheck`/`lint`/`vitest run src/workflow/` **35/35**,Task 9 交付的 `useRequestKey` 未被 schema 重生成打破 ✅
5. 双模板 `schema.d.ts` SHA256 一致 —— Round 32 独立算过 `sha256sum`,两文件哈希 `36dbccb759527b199774b2c1bdfd0d1749ced9d17e0924dfefa311201fa83356` **逐字相同** ✅
6. 重复提交同 `RequestId` 返回首次 `WfEngineResult`(HTTP 层可观测)—— 引用 Task 5 `WfReceiptEngineTests.Same_request_id_replays_the_first_result_without_advancing_twice`(真实 `WorkflowAppFactory` 集成测试,两次 `POST /api/v1/workflow/task/approve` 断言 `instanceId`/`createdTaskId` 逐字相同、`wf_his_task` 仅一行)既有证据,未新增测试(Round 31 plan 已定,验收时直接引用)✅

**核对改动清单/决策点**:E1(用 `check-contract-drift.mjs` 而非重造工具,非零退出为预期)✅ / E2(独立 SHA256 已在 Round 32 执行)✅ / E3(diff 范围人工核对,精确落在 4 处 `requestId`,无意外漂移)✅ / E4(本地闸门顺序 backend→web→web-react,不并发重进程)✅ / E5(一次性提交 + `git push origin dev`,不建 PR、不 push main、非 force,严格在用户 Round 31 授权范围内)✅ / E6(五个工作流逐个确认,`backend-ci` 展开四条 `db` 矩阵腿逐条核对,未只看整体汇总;经 Round 33–36 四轮如实记录「仍在跑」直至真正收敛,未提前假设通过)✅ / E7(未修 P3 `ActivatorUtilities` 反模式,继续挂账,不擅自扩面)✅。

**改动面复核**:本轮(Round 33–36 四轮)只改了 `.loop/wf-m2c.md` 一个文件(每轮一次只读 `gh run view` + 台账更新),未再碰任何产品代码或生成物,与 Round 32 exec 的改动清单(2 个 `schema.d.ts` + 台账 + 只读 push/gh 操作)合计仍在计划范围内,**预期计划外 0** 兑现 ✅。

**P1**:0 条。**P2**:0 条。→ 满足勾选条件。**`## Tasks` 第 10 项(本台账最后一项)打勾。**

**给后续维护者的收尾说明**(按 `## Plan` 预留位补齐):`start/index.vue`/`detail.vue` 里手写的 `requestId` 字段理论上可被新生成的 schema 类型覆盖替代(纯类型层收尾),**不是**强制项,留给后续按需处理。**P3(`ActivatorUtilities` 绕过 `TryAdd` 的测试写法,Round 8 起挂账)与 SQLite 射程局限披露(`WfPersistenceContractTests` 类注释,Round 25)均继续挂账**,不因 M2c 收口而被误判为「已解决」——这两条本就不在任何 Task 的范围内,是长期已知、已披露的既有状态。

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
| 20 | exec | Task 7 落地 **5 + 1 预期计划外**:K1 **删掉** `WfDefaultNotifier` 内部 3 个 try/catch(净删代码,类注释写明内层为何必须消失);`WorkflowEngine`/`WfTaskService`/`WfTimeoutJob` 三个构造各加 `ILogger<T>`;四处 catch 记 `LogWarning(ex, ...)`(异常走 exception 形参、一律 Warning)。`WfNotifyLoggingTests` 5 例,用抛异常的 `IRealtimePublisher` 让内置 Notifier 原样跑到失败,自制最小 `ILoggerProvider` 捕获日志。踩到两条**测试自身**的错:待办到达实际有 2 条警告(`Assert.Single` 用错,改「至少一条且每条带异常」)、超时提醒写的是 `TimeoutFired` 而非 `TaskUrged`(催办与提醒共用通知方法但事件类型不同)。build 0 错、工作流包 0 警(全量);过滤器 **245/245**。未勾选。 |
| 21 | review+勾选 | Task 7 自审:五处变异全按预期转红。**最有说服力的一处**:把内层 3 个 catch 加回去 → **red 4/5**,四条失败路径同时变回无声,直接实证了「双层网让默认路径不可观测」这个本 Task 的核心论点;`throw;` 那处 red 4/5 则反向确认「通知绝不拖垮业务」仍成立。上一轮两条疑虑均证伪:用例 1 改「至少一条」没弱化(变异②③各自接住),`LogSink` 跨类别捕获没污染用例 5(变异④能让它红 = 敏感,未变异时绿 = 干净)。K1–K8 全对,改动面 5 + 1 已预先声明。**0×P1 / 0×P2**。闸门:全量 Release 工作流包 0 警;过滤器 **245/245**。**Task 7 打勾**。 |
| 22 | plan | Task 8 plan 定稿(L1–L8)。**读码推翻了台账对本 Task 的原始设想**:`WorkflowAppFactory` 只喂 `TestDb.DbType`,所以现有 **245 条本来就在 sqlite/mysql/postgres 三腿全跑** —— 把 CAS/回滚/重放再写一遍纯属复制。新套件的正当性收敛成三条:①PG 唯一冲突后整事务 aborted(只有新用例能钉);②现有用例**一条都没碰**的数据库层方言真相(唯一索引是否真被建出、`CodeFirst_BigString` 中文与长文本、64 满宽、可空 `ADD COLUMN` 旧行、affected-rows、`DueTime` 相等游标);③**今天 SqlServer PR 腿零条工作流测试**,`TEST_FILTER` 的 13 个类里没有任何 `Wf*`。另两条读码事实:SqlSugar 5.1.4.198 **没有 savepoint API**(扫过 dll 字符串表),内核 `src/` **今天零个方言分支**。PG 修法定 **PG-only SAVEPOINT**,三个替代方案(无差别 savepoint / `ON CONFLICT DO NOTHING` / 接受败者恒失败)各自被否的理由已写进 L1。复现手法定 **致盲首次 `FindAsync` + 事务外预提交赢家行**,把构造不出的并发换成一个诚实的单点伪造。14 条用例 + 6 个变异点 + 9 条陷阱已列;改动面 4 文件、预期计划外 **0**。新记 **P2→Task 10**:四腿绿本机取不到证,需用户 push 或授权。未写产品代码。 |
| 23 | exec | Task 8 落地 **3 文件 + 台账,零溢出**。`WfOperationReceiptService` 加 PG-only SAVEPOINT(三个 `protected virtual` 小步;守卫 `DbType == PostgreSQL && Ado.IsAnyTran()` —— 自动提交模式下 PG 拒收 `SAVEPOINT`,而那时也根本不需要它),注释写明为什么这是内核第一个方言分支、以及「不解析错误码」的旧决定为何仍然成立。新增 `WfPersistenceContractTests` **14 条**(A 回执唯一性与 PG 事务中止 4 / B 列类型与列宽 2 / C 可空升级列 2 / D CAS 与 affected-rows 2 / E `DateTime` 相等游标 1 / F 为 SqlServer PR 腿而设的端到端幂等冒烟 3);致盲替身只蒙前 N 次 `FindAsync`,赢家行事务外预提交,冲突与恢复都是真的。`TEST_FILTER` 追加本类 + 一段「为什么唯独这一个 `Wf*` 进 PR 腿」的注释,YAML 已解析验证、过滤器单行 14 项完好。**两条测试自身的错**:D2 让人工先赢时活动 `wf_task` 当场归档进 `wf_his_task`(库里没待办可推到期,证明不了仲裁)→ 改成超时先赢、人工后到收业务错误;`DateTime.Now - TimeSpan` 写进 `SetColumns` 表达式会被当 SQL 翻译,落库值读回绑不上 `DateTime` → 提成局部变量。**L7 探测:本机无 PG**,PG 的「修前红」取不到证,射程声明写进类注释。闸门:全量 `--no-incremental` Release 0 错、13 警(既有基线);过滤器 **259/259**。未勾选。 |
| 24 | review | Task 8 **自审 review**。指定过滤器 **259/259 绿**;6 处变异逐一跑(先 `grep` 验改、复原只 checkout 单文件):**红 3 处**(`IsUnique` → 4/14、`RequestId ?? ""` → 1/14、删 `DueTime == cursor` 半边 → 1/14),**绿 3 处**且性质不同:M1(去 savepoint)方言使然、如实记「未取证」;M6(删 `Version ==`)本套件按 L4 就不该覆盖,追加取证改跑全过滤器 **红 1/259**(`WfVersionCasTests.Instance_losing_cas_...`)—— 不是覆盖洞,是 plan 变异表那一格预期写错,已更正;M3(`ResultJson` 换 `Length = 200`)**仍绿**,这是本轮唯一真发现 —— **SQLite 不执行列宽**,B1/B2 在 SQLite 腿是恒真断言,而类注释的射程声明只写了 PG、漏了列宽(追加取证:全过滤器 259/259 仍绿(1m17s))。记 **1 条 P2**(补射程注释,不改产品代码、不加 `SkippableFact`)+ 2 条 P3(`RollbackNested`/`ReleaseNested` 抛错会顶替真因,窄,挂账)。**未勾选**。 |
| 25 | 修 Findings | Task 8 修掉 Round 24 的唯一 P2:`WfPersistenceContractTests` 的类注释射程声明补上「列宽/列类型断言在 SQLite 腿恒真」,B1/B2 方法文档各加一段射程说明,指向 mysql/postgres/sqlserver 才是真钉子。只改这一个文件、只加文档注释,不碰产品代码、不加 `SkippableFact`。闸门:259/259 绿(1m12s)。未勾选(留给 Round 26)。 |
| 26 | 勾选 | Task 8 **勾选**。核对 0×P1、1×P2 已修(Round 25)、闸门已绿(259/259),`## Tasks` 第 8 项打勾。四库持久化契约套件收尾:内核首个方言分支(PG SAVEPOINT)+ 14 条新测试 + `TEST_FILTER` 纳入 SqlServer PR 腿,基线 245→**259**。给后续任务的两条锚点(P2→Task10 四腿本机取不到证 / P3→Task5、8 的 `ActivatorUtilities` 写法)原样保留待用。 |
| 27 | plan | Task 9 **plan**。重写 `## Plan`:新建 `web/src/workflow/useRequestKey.ts` composable(惰性生成 + `settle` 三态 + `reset`)+ `classifyOutcome` 纯函数(复用 `err instanceof ApiError` 既有判据);`detail.vue`/`start/index.vue` 接入,Urge 不生成/不传 key;`wfInstanceApi.cancel`/`resubmit` 手写类型各加 `requestId?`。**关键发现**:schema.d.ts 从 Task 4 起未重生成,但因 `detail.vue` 已是「先 `const body`」写法,TS 结构类型允许多余字段,本 Task 不需要提前 `gen:api`;`start/index.vue` 需先改成同款写法(字面量直传会触发超额属性检查)。7 文件改动清单,预期计划外 0。**未写产品代码。** |
| 28 | exec | Task 9 **exec**。新建 `useRequestKey.ts`(惰性生成 + `settle` 三态 + `reset`)+ `classifyOutcome` + 6 条单测;`detail.vue`/`start/index.vue` 接入(urge 不生成/不传 key,`start` 页改成 `const body` 写法对齐 `detail.vue`);`api/workflow.ts` 的 `cancel`/`resubmit` 手写类型各加 `requestId?`;`COMPONENTS.md` 补索引节。闸门:typecheck 0 错、lint 0 错 0 警、`vitest run src/workflow/` **35/35**。改动面精确等于计划内 6 文件,未碰 web-react/backend。**未勾选**,留 Round 29 review。 |
| 29 | review | Task 9 **review**(自审)。变异 `useRequestKey.ts` 三处:`settle` 对 network 也清空(红用例2)/`value()` 去惰性(红用例1+2,比预期表更强)/`classifyOutcome` 恒 error(红用例6),均先 grep 确认真改、单文件复原。人工核对 `detail.vue` urge 分支不生成/不透传 `requestId`,`start/index.vue`/`api/workflow.ts`/`COMPONENTS.md` 逐条对照 D5–D7 无计划外改动。闸门重跑:typecheck/lint/vitest 全绿(35/35)。**0×P1、0×P2**,留 Round 30 勾选。 |
| 30 | 勾选 | Task 9 **勾选**。核对 0×P1、0×P2(Round 29 review)、收尾闸门重跑绿(typecheck/lint/vitest 35/35),`## Tasks` 第 9 项打勾。Vue request key 生命周期收尾:`useRequestKey` composable + `classifyOutcome`,`detail.vue`/`start/index.vue` 接入,`api/workflow.ts` 手写类型加字段,`COMPONENTS.md` 索引;全程未碰 web-react/未跑 gen:api,留给 Task 10。M2c 仅剩 **Task 10** 未完成。 |
| 31 | plan | Task 10 **plan**(本台账最后一个 Task)。读 CI 工作流五个文件 + gen:api/契约漂移两个脚本 + react-template-ledger E5 历史 + Task 5 的 HTTP 层幂等回放测试(确认 DONE-CONDITION 最后一条已满足,无需新测试)。**AskUserQuestion 问出 P2→Task10 锚点的决定:用户选「push dev 拿 CI 信号」**,写入 Plan 顶部作为「默认不 push」纪律的唯一限定例外。重写 `## Plan`:E1–E7 决策点、2+1 文件改动清单(两 schema.d.ts + 台账)、6 项 DONE-CONDITION 对应验收清单。未写产品代码、未跑 gen:api、未 push。 |
| 32 | exec | Task 10 **exec**。`check-contract-drift.mjs` 重生成两模板 schema(非零退出=预期),diff 精确落在 4 处 `requestId`(Cancel/Resubmit/Start/TaskAction 四个 Input),无意外漂移;`sha256sum` 独立核验两文件逐字相同。本地闸门串行全绿:backend build 0/0、test 259/259;web typecheck/lint/vitest 35/35;web-react typecheck/lint/build 全绿。提交 + **已 `git push origin dev`**(用户 Round 31 授权)。未勾选,CI 四库矩阵结果留 Round 33+ review 轮询。 |
| 33 | review | Task 10 review 轮询 #1。`gh run list`/`gh run view` 核对 commit `4679467` 的五个工作流:web-ci/web-react-ci/contract-drift 已 success;backend-ci(四条 db 矩阵腿全 in_progress)与 docker-smoke(multi 腿 in_progress,single 已 success)仍在跑。如实记录未完成,不勾选,留待后续轮次继续轮询。只读操作,无代码改动。 |
| 34 | review | Task 10 review 轮询 #2。docker-smoke 转 success(single+multi 均绿)。backend-ci 仍 in_progress:template-smoke/sqlite 已绿,mysql/postgres/sqlserver 三条腿未收敛。五个工作流现状 4 绿 1 未定,如实记录未完成,不勾选,留待后续轮次继续轮询 backend-ci。只读操作,无代码改动。 |
| 35 | review | Task 10 review 轮询 #3。backend-ci 的 mysql/postgres 腿转 success,只剩 sqlserver 一条腿仍 in_progress(其余四项 job 与其余四个工作流均已确认绿)。如实记录未完成,不勾选,留待后续轮次继续轮询 sqlserver 腿。只读操作,无代码改动。 |
| 36 | review+勾选 | Task 10 **review+勾选**(自审,0×P1/0×P2)。`gh run view` 最终确认 backend-ci 整体 conclusion=success(四条 db 矩阵腿 + template-smoke 全绿,nightly-alert 按设计 skipped),五个工作流对 commit `4679467` **全部绿**。逐条核对 DONE-CONDITION 六条证据(Tasks 全勾/259 基线/四库矩阵/web 三件套+request key/双模板 SHA256 一致/Task 5 既有幂等回放证据)全部满足。`## Tasks` 第 10 项(最后一项)打勾。**M2c 十项 Task 全部完成**,下一轮 GUARD 核对闸门后应输出 DONE。 |

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
