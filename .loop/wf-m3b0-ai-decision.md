# M3b-0：shadow-only AI Decision v0

创建日期：2026-09-04

status: done
round: 25 / max: 40

## Goal

基于现有可靠自动节点执行内核，交付 shadow-only 的 AI Decision v0：Provider 生成结构化 proposal，服务端完成 schema/policy 校验，仅在当前 owner 的 fence/CAS 成功后由 tx2 原子写入审计并创建人工兜底任务。

## Done-condition

只有同时满足以下条件才可将 `status` 改为 `done`：

1. `## Tasks` 全部为 `[x]`，`## Blockers` 无未解决项。
2. AI Decision 已走现有 scheduler/worker/dispatcher，不存在第二套执行链。
3. V0 强制 shadow-only；AI 永不自动批准、拒绝或直接推进 task/token。
4. Provider 可替换，具备 Fake Provider 与 OpenAI-compatible HTTP Provider；测试不使用真实凭据。
5. proposal 必须经服务端 schema/policy；低置信度、高风险、格式错误、超时和 Provider 异常均可审计并转人工兜底。
6. handler 不直接写数据库、task/token 或自开事务；stale owner 不得新增 attempt、AI 审计、outbox 或人工待办。
7. `git diff --check`、Release build、默认 SQLite 全量测试、M3b 目标测试的 SQLite/MySQL/PostgreSQL/SQL Server 四库验证、contract drift 全部通过。
8. 如 API/schema 改动，Vue 与 React 两套独立 schema 已重新生成，且两端 typecheck/build 通过。
9. diff 中不存在 TODO、stub、`test.skip`、`.only` 或未实现分支。
10. 独立 reviewer/verifier 给出 PASS，最终命令与证据已记录在本台账。

## Protected baseline

首次创建时的分支状态：`dev...origin/dev`。

以下内容在本任务开始前已经存在，本任务不得回滚、覆盖、清理、stash 或据为己有：

- 已修改：`.gitignore`
- 已修改：`AGENTS.md`
- 已修改：`.loop/wf-m2c-handoff.md`
- 已修改：`.loop/wf-m3a1.md`
- 已修改：`docs/workflow/README.md`
- 已修改：`docs/workflow/elsa3-slickflow-ai-reference-2026-08-23.md`
- 已修改：`docs/workflow/openworkflow-reference-2026-08-23.md`
- 已修改：`docs/workflow/workflow-database-design-review-2026-08-24.md`
- 已修改：`docs/workflow/workflow-engine-research-2026-08-10.md`
- 已有未跟踪内容：`.codex/**`

其中 `docs/workflow/**` 的既有修改是 Task 8b/M2c/M3a-1 状态与历史快照修正。M3b 最终文档任务允许在相关文件继续追加或合并 M3b 内容，但必须先阅读当前 diff，保留上述既有修正，不得整文件覆盖。

每轮开始都必须执行 `git status --short --branch` 并与此基线比较。发现其他来源的新修改时，不得触碰；无法安全隔离则将 `status` 改为 `blocked-user` 并记录证据。

### Round 7 的既有 M3B0-06 改动接续授权（2026-09-07）

- 用户通过结构化确认选择“允许增量修复”，明确允许保留工作树已有 Task06 diff，继续增量补测试和修复 AI 节点发布校验。该授权解决 Round 7 的来源未确认阻塞，不代表本任务创作了此前未记账内容。
- 接续范围为 Task06 相关的 `Schema/WfNode.cs`、`Schema/WfSchemaEnums.cs`、`Services/WfDefinitionService.cs`、`Providers/AiDecisionNodeHandler.cs`，以及 `WfAiDecisionNodeDefinitionTests.cs`、`WfAiDecisionContractTests.cs`、`WfAiDecisionRegistrationTests.cs`、`WfFakeAiDecisionProviderTests.cs`；可为该修复新增聚焦回归测试。先读当前 diff，只做必要增量，不回滚或重写既有改动。
- 该授权不扩大原始 Protected baseline：`.gitignore`、`AGENTS.md`、`.codex/**`、旧 `.loop`、无关技能/指令整理仍不得触碰，`docs/workflow/**` 仍只能在 M3B0-16 增量合并。

## Fixed decisions

- 用户于 2026-09-04 决定当前不做 Task 8c，主线进入 M3b-0。
- Task 8c 明确不在范围内：不得实现 outbox consumer、transport、`Pending → Dispatching → Dispatched/Failed` 状态机、投递重试、死信或手工重放。
- M3b 可以继续复用 Task 8b 已有的 tx2 幂等写 `Pending` outbox 能力，但不得扩展为 Task 8c。
- V0 强制 shadow-only；即使 proposal 合法且低风险，也必须进入人工兜底。
- V0 不自动批准、不自动拒绝；模型不能独自决定放行。
- Provider 必须 interface-backed、可替换并通过 `TryAdd` 注册；长流程按现有风格拆成可覆写步骤。
- 提供确定性的 Fake Provider 和 OpenAI-compatible HTTP Provider；使用 `HttpClient`、Microsoft/System 组件，不引入模型 SDK或新的核心第三方依赖。
- 模型只生成结构化 proposal；schema、允许动作、置信度、风险和限额由服务端 policy 决定。
- handler 在工作流事务外调用 Provider，只返回类型化结果，不直接写状态或开启事务。
- 只有 fence/CAS 成功后的 tx2 能写 execution attempt、AI 审计、必要的 `Pending` outbox 和人工兜底任务。
- AI 审计保留每次 attempt，使用重试安全的唯一身份；优先 `(ExecutionId, AttemptNo)`，如现有约定存在更合适的等价方案，须在本台账记录代码证据与理由。
- 审计不得保存密钥、认证头、完整 prompt 中的未脱敏敏感信息或其他不必要 PII。
- M3b-0 不实现 Vue/React 设计器或独立 AI 审计管理页；API 变化仍须同步两套独立 schema。
- 不提交、不推送、不创建 PR 或 tag；不使用破坏性 git 命令。
- 仓库存在 `.codegraph/` 时，理解或定位代码必须先使用 CodeGraph。

## Tasks

- [x] M3B0-01 建立实现基线：读取权威 workflow 文档；用 CodeGraph 追踪 `EnterNodeOp`、scheduler、dispatcher、handler、command、tx2、人工任务和 outbox 调用链；把精确文件/符号、风险和最终实施决策写入本台账
- [x] M3B0-02 先补 Provider、proposal、policy 的失败契约测试，钉住 shadow-only、取消、超时、错误分类和“不得推进 task/token”
- [x] M3B0-02A 实现 M3B0-02 红测要求的最小纯契约纵切并恢复测试项目可编译：Provider interface/request/result、proposal DTO/parser、policy evaluator、typed outcome 与 shadow-only handler；不做 DI/Fake/OpenAI/node/tx2/persistence
- [x] M3B0-03 实现可替换的 AI Provider contract、options、类型化请求/结果和 `TryAdd` DI，并补 replaceability 测试
- [x] M3B0-04 实现确定性的 Fake Provider，并覆盖成功、格式错误、低置信度、高风险、超时和异常
- [x] M3B0-05 根据权威文档确认并记录 OpenAI-compatible v0 协议；用 `HttpClient` 实现 adapter，并用 fake HTTP handler 覆盖协议、取消、超时、非成功响应和脱敏
- [x] M3B0-06 增加 AiDecision 节点类型、最小 props 和发布时校验，并补定义发布与向后兼容测试
- [x] M3B0-06A M3B0-06 剩余修复：先补 AI 节点夹带 WebhookUrl/WebhookHeaders/Nobody 等非批准 props 的发布失败测试，再以 AI 专属校验拒绝不允许的已知字段；补旧 v1 草稿真实发布及 AI MaxAttempts 允许上下界/越界回归，保持 Webhook/旧模型兼容；聚焦测试与独立复审均通过后方可验收 M3B0-06（Round 7 用户已明确授权增量接续；Round 9 用保留的修复前实现完成缺陷检出回放并验证当前实现，不追认 Round 8 历史 TDD 顺序，见 Evidence）
- [x] M3B0-07 实现结构化 proposal parser/schema 与服务端 policy evaluator，强制所有结果转人工兜底并覆盖边界测试（Round 10 完成 parser/policy 子项；Round 11 完成 07A，独立 verifier 分别验收 07A 与本父项 PASS；不包含后续 tx2 第二层防线）
- [x] M3B0-07A 补齐已发布 AiInstructions/AiInputFields 的安全送模映射：以精确 Ordinal 顶层键白名单构造受大小/深度/数量限制、按服务端规则脱敏的不可变 typed request 与输入 hash；HTTP body 只使用受限指令和投影，不携带原始 VariablesJson；补缺失键、重复字段、非 object、嵌套/超限、敏感输入、取消及 fake HTTP body 回归，保持审计不留原始变量/完整 prompt，禁止提前接入 tx2 或新增执行链（Round 11 聚焦验证与独立复核 PASS，见 Evidence）
- [x] M3B0-08 新增 AI decision 审计实体、枚举、唯一键和索引；补四数据库持久化契约测试，并确保 SQL Server PR filter 覆盖新增测试（Round 12 实体/共享测试/CI/SQLite 子项 PASS；Round 13 安装 Docker 并完成 SQLite/MySQL/PostgreSQL/SQL Server 同一契约各 2/2，独立验收本项 PASS）
- [x] M3B0-09 先补 handler 结果到 tx2 的类型化 hand-off、原子性和 stale-fence 失败测试，禁止 handler 旁路写库（Round 14：tx2 shadow-only 归一化、typed hand-off 与真实 typed stale 零副作用测试通过，独立 verifier PASS）
- [x] M3B0-10 将 AiDecision 接入 `EnterNodeOp` 和现有 execution scheduler/dispatcher/handler 路径，不新建第二套 worker
- [x] M3B0-11 在 fence/CAS 成功的 tx2 中原子写 attempt、AI 审计、必要的 `Pending` outbox 和人工兜底任务；保证重试不覆盖历史且旧 owner 零副作用（Round 16 已完成并经独立 verifier PASS）
- [x] M3B0-12 增加 Fake Provider 的发布定义→启动实例→worker 执行→审计→人工待办端到端成功测试（Round 17：独立 verifier PASS）
- [x] M3B0-13 增加格式错误、低置信度、高风险、Provider 超时/异常、取消、重试、崩溃恢复、重复执行和 stale-fence 的负向矩阵测试（Round 18：真实 worker/tx2 负向矩阵，独立 verifier PASS）
- [x] M3B0-14 增加脱敏后的 AI 审计读取投影/API，复用实例参与者/监控权限边界，并补越权和敏感字段测试（Round 19）
- [x] M3B0-15 如 API/DTO 变化，启动 Development MinimalHost，重新生成 `web` 与 `web-react` schema，并运行两端 typecheck/build；如无变化，在证据区记录不需要生成的理由（Round 19）
- [x] M3B0-16 更新权威 workflow 文档和本台账，明确 M3b-0 已交付、shadow-only 边界、Task 8c 仍未实现及验证证据（Round 20）
- [x] M3B0-17 运行独立代码审查、安全审查和简化审查；修复并独立复核元数据链、`assigneeEmpty`、机构范围、proposal 脱敏、损坏审计 JSON、旧替换实现兼容性与真实 OpenAI→tx2 贯穿链路（Round 21）
- [x] M3B0-18 运行目标测试、Release build、默认 SQLite 全量测试，并检查 diff 中无 TODO/stub/`test.skip`/`.only`/未实现分支（Round 22）
- [x] M3B0-19 按 backend CI 配置运行 M3b 持久化与运行时目标测试的四数据库验证和 contract drift，记录完整命令与结果（Round 23）
- [x] M3B0-20 由独立 verifier 对需求、事务边界、fence、四库、权限、脱敏、非目标和全部证据作最终核验；PASS 后将 `status` 改为 `done`（Round 24）

## Evidence

### 已知前置证据

- Task 8b review-repair backend-ci：`33828658172`，SQLite/MySQL/PostgreSQL/SQL Server/template-smoke 成功。
- contract-drift：`33828658095`，成功。
- docker-smoke：`33828658106`，single/multi 成功。
- 当前文档已明确：Task 8b 只负责 tx2 幂等写 `Pending` outbox，Task 8c consumer/transport 不阻塞 M3b。

### M3B0-01 实现基线（Round 1；代码/文档研究已完成，聚焦测试环境阻塞）

#### 权威来源与冲突裁决

- 已按 [`docs/workflow/README.md`](../docs/workflow/README.md) 的 M3b 必读顺序阅读当前工作树中的 [`workflow-design-plan-2026-08-17.md`](../docs/workflow/workflow-design-plan-2026-08-17.md) §14.3/§15.4、[`elsa3-slickflow-ai-reference-2026-08-23.md`](../docs/workflow/elsa3-slickflow-ai-reference-2026-08-23.md) §4–§5，并补读 [`workflow-database-design-review-2026-08-24.md`](../docs/workflow/workflow-database-design-review-2026-08-24.md) §6–§7；代码、测试与本台账的用户定案优先于规划文档。
- 现有 README M3b 提示、设计规划 §14.3/里程碑表、数据库评审 §7 仍保留“低风险自动放行/只禁止自动拒绝”的旧规划文字；本台账 2026-09-04 的 Fixed decisions 更严格并覆盖它们：**M3b-0 全程 shadow-only，任何 proposal 都不得自动批准、拒绝或推进 task/token**。M3B0-16 必须把权威文档改成这一已交付边界，而不是把旧规划反向带回代码。
- Elsa 的可取边界是 proposal-only、provider 隔离与服务端治理；Slickflow 的可取形状是一等自动节点。明确不复制 Slickflow 的两套执行路径、静态 factory/memory、事务内远程调用或 README 中未落地的 confidence 路由。

#### 当前唯一生产调用链（CodeGraph + 当前源码）

1. **定义与入口**：`backend/src/TenonAdmin.Workflow/Schema/WfSchemaEnums.cs:17` 的 `WfNodeType` 是节点枚举；`backend/src/TenonAdmin.Workflow/Services/WfDefinitionService.cs:259` 的 `ValidateModelForPublish` 与 `:354` 的 `ValidateNode` 是发布白名单/props 校验入口。`backend/src/TenonAdmin.Workflow/Engine/Operations/EnterNodeOp.cs:23` 的 `ExecuteAsync` 先 `ClaimTokenAsync`，生成并写入 `NodeVisitId`，追加 `NodeEnter`；现有 Webhook 分支在 `:83` 的 `EnterWebhookAsync` 只构造 `WfNodeExecution(Pending)`，经 `backend/src/TenonAdmin.Workflow/Engine/WfExecutionKey.cs:38` 的稳定六维 hash 与 `backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionStore.cs:20` 的 `EnsureAsync` 幂等落库，绝不在工作流事务内外呼。
2. **现有 scheduler/worker**：`backend/src/TenonAdmin.Workflow/WorkflowSetup.cs:44` 的 `AddTenonAdminWorkflow` 在 `:82` 注册 scoped dispatcher，在 `:105-110` 注册既有 `IAdminJob` worker 与 seed；`backend/src/TenonAdmin.Workflow/Jobs/WfNodeExecutionJobSeed.cs:11` 固定创建 `wf-node-execution-scan`（5 秒、`SerialSkip`、`Ready`）。宿主链是 `backend/src/TenonAdmin.Services/ServicesSetup.cs:168-179` → `JobSchedulerService.TickAsync`/`DispatchDueJobsAsync`（`backend/src/TenonAdmin.Services/Jobs/JobSchedulerService.cs:85`/`:315`）→ `JobExecutor.TryFireAndTrack`/作用域解析（`backend/src/TenonAdmin.Services/Jobs/JobExecutor.cs:71`/`:185`）→ `DefaultJobHandlerResolver.ResolveAsync`（`backend/src/TenonAdmin.Services/Jobs/DefaultJobHandlerResolver.cs:13`）→ `WfNodeExecutionJob.ExecuteAsync`（`backend/src/TenonAdmin.Workflow/Jobs/WfNodeExecutionJob.cs:32`）。该 Job 在 `:80` 扫 `Pending`、到期 `RetryScheduled`、过期 `Running`，**不按 NodeType 分叉**，逐行调用同一 dispatcher。
3. **tx1 与事务外 handler**：`backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionDispatcher.cs:54` 的 `RunAsync` 用 `UseTranAsync` 包住 `WfNodeExecutionStore.ClaimAsync`（`backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionStore.cs:52`）；领取同时写 `Running`、lease owner/expiry、`Fence + 1`、`AttemptCount + 1`。事务提交后，dispatcher 在 `:155` 构造不含 SqlSugar session/entity 的 `WfNodeExecutionContext`，按 `IEnumerable<IWorkflowNodeHandler>` 首个相同 `NodeType` 解析 handler（`:144`），在 `:242` 调用。`OperationCanceledException` 原样传播；其他未分类异常当前被收敛成 `RetryableFailure/48032`。
4. **command 与 tx2**：dispatcher 在 `:122` 把领取时的 Fence、类型化 `WfNodeExecutionResult` 和起止时间装入 `backend/src/TenonAdmin.Workflow/Engine/WfCommands.cs:223` 的 `NodeExecutionCompletedCmd`，再调用 `IWorkflowEngine.ExecuteAsync`。`backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs:38` 为“一条命令一个事务”；`BeginNodeExecutionCompletedAsync`（`:1252`）先读 execution/instance/token/version/model，算 outcome，再把 `ClaimExecutionWritebackAsync`（`:1477`）作为 tx2 第一个写操作，CAS 条件严格为 `Id + Fence + Status == Running`。
5. **原子副作用顺序**：CAS 成功后才调用 `WfNodeExecutionAttemptStore.AppendAsync`（`backend/src/TenonAdmin.Workflow/Engine/WfNodeExecutionAttemptStore.cs:37`）；终态再由 `WfOutboxStore.EnqueueAsync`（`backend/src/TenonAdmin.Workflow/Engine/WfOutboxStore.cs:35`）按 `{ExecutionKey}:{MessageType}` 幂等写一行 `Pending`。随后同一事务中的 Agenda 对 `Succeeded` 计划 `TakeTransitionOp`，对 `ManualFallback` 计划 `backend/src/TenonAdmin.Workflow/Engine/Operations/WfManualFallbackOp.cs:24`；后者复用 `EnterNodeOp.CreateTaskAsync`（`backend/src/TenonAdmin.Workflow/Engine/Operations/EnterNodeOp.cs:318`）原子插入 `wf_task`、actors 与 `TaskCreated` 历史。任一后续写失败，整个 tx2（含 CAS/attempt/outbox）回滚。
6. **stale owner 零副作用**：租约过期后的新 owner 会再次领取并推高 Fence；旧 owner 的 tx2 因 `Fence` 不匹配失败。同一 owner 结果回放因状态已非 `Running` 失败。CAS 在 attempt/outbox/Agenda 之前，异常会回滚整笔事务，因此旧 owner 不会留下 attempt、outbox、任务或 token 推进。重提路径还在同一事务内取消旧 execution 并 `Fence + 1`，再做 token CAS；迟到结果同样被挡。
7. **人工兜底既有边界**：`WfManualFallbackOp` 只使用当前节点 `Props.Assignee`；未配置 provider 或动态解析为 0 人时不建任务、不自动放行，execution 保持 `ManualFallback`、token 原地。M3b 必须保留“绝不自动放行”，同时通过发布校验和审计把“兜底办理人缺失”变成显式可诊断配置问题。
8. **outbox 非目标**：当前 `WfOutboxStore` 只有入队；`WfNodeExecutionJob` 不读取 `wf_outbox`。现有生产 E2E 只断言 outbox 行存在，不证明消费。Task 8c 的领取、投递、重试、死信、重放与 transport 全部保持未实现。

#### M3b-0 最终实施决策

- **节点接入**：只在 `WfNodeType` 末尾追加 `AiDecision`（JSON `aiDecision`），旧值不重排；保持 schema v1 的加法兼容并补旧模型反序列化/发布测试。`EnterNodeOp` 新增 AI 分支，但应抽取一个受保护的自动 execution ensure 单步供 Webhook/AI 复用，保留现有 `EnterWebhookAsync` virtual seam；不增加第二个 Job、seed、dispatcher、状态机或 worker fleet。
- **最小节点 props**：新增内容只承载审查指令与允许送模的变量字段白名单；人工兜底继续复用现有 `Assignee`，执行预算复用 `MaxAttempts`。endpoint、API key、model、provider 选择、prompt/policy 版本和阈值属于服务端 Options/Adapter，不进入可发布流程 JSON；节点配置中不提供关闭 shadow 或自动批准/拒绝的开关。
- **Provider seam**：新增小型 `IAiDecisionProvider`，默认实现为 OpenAI-compatible HTTP Adapter，并提供可直接注入的确定性 Fake；全部 `TryAdd`，不引模型 SDK。Provider 只负责事务外请求/响应 envelope，不决定工作流动作；外部 cancellation 原样传播，adapter 自身 timeout 与非取消异常返回可分类结果。HTTP 正文、响应和错误摘要先限长，Authorization/API key 不进入日志、异常或审计。
- **schema/policy**：proposal 使用版本化严格 DTO（`schemaVersion/recommendation/confidence/reasonCodes/rationale/evidence/riskFlags`），以独立、拒绝未知/缺失/越界字段的 `System.Text.Json` 配置解析；长度、数组数、有限数值、枚举、证据 hash 等限制由服务端验证。policy 是确定性服务端分类器，只产生“shadow 候选资格/人工原因”等审计结论，不返回 `Approve/Reject/CompleteTask` 命令。模型自报 confidence 仅作记录；阈值缺省不代表放行。
- **硬性 shadow 防线**：内置 `AiDecisionNodeHandler` 无论 provider 成功、低置信度、高风险、格式错误、timeout 或异常，都返回带类型化 AI writeback 的 `ManualFallback`；只有外部 cancellation 不产结果。引擎 tx2 还要按 `execution.NodeType == AiDecision` 二次归一化为 `ManualFallback`，防止替换 handler 返回 `Succeeded` 绕过 shadow-only；AI attempt 的有效结果与 execution 状态不得出现“attempt 成功但流程被人工兜底”的矛盾口径。
- **类型化 hand-off**：proposal、provider envelope、schema/policy 判定和 fallback reason 通过专用类型附着到 `WfNodeExecutionResult`/`NodeExecutionCompletedCmd`，tx2 不从 `OutputJson` 或摘要反序列化业务决定。缺失/不合法的 AI payload 由引擎按 handler contract violation 安全归类并仍转人工，不能推进 token。
- **AI 审计**：新增 append-only `WfAiDecision`（表名按数据库评审为 `wf_ai_decision`），唯一身份固定为 `(ExecutionId, AttemptNo)`，另按实例/时间提供读取索引。每个已返回或 dispatcher 合成并进入 tx2 的 AI attempt 最多一行；顺序固定为 **fence CAS → generic attempt → AI audit → 终态 Pending outbox → Agenda 人工任务**，全部在现有 tx2。CAS 失败或任何后续失败整笔回滚；不得在 handler/adapter 旁路写表。
- **审计最小化/脱敏**：不保存 API key、Authorization、完整 prompt、原始变量 JSON、原始 provider response 或异常正文；模型自由文本、reason code、provider/model、prompt/policy 版本及 risk/evidence id/source 均只保存规范 `sha256:` 标识，另保存输入/content hash、schema/policy outcome、延迟/usage 和人工原因。审计带稳定 `NodeId`，不复制 evidence 正文。
- **人工任务**：合法发布的 AiDecision 节点必须配置已注册的 `Assignee.Provider`；正常解析到办理人时继续复用 `WfManualFallbackOp/CreateTaskAsync`，从而 AI 审计、outbox 与人工待办原子提交。动态解析为 0 人时必须审计 `assigneeEmpty` 并 fail closed（不推进 token、不虚构审批人）；不复用人工审批节点默认的 `nobody=autoPass`。
- **重试/恢复**：provider timeout/格式/policy/业务异常在 v0 直接审计并人工兜底，不由 AI 自己重试；进程在 handler 返回前取消/崩溃仍沿现有 lease 重新领取，可能再次调用 provider，故 `ExecutionKey` 必须作为请求幂等身份传给 adapter。外部 cancellation 维持现有语义：无 tx2、无 attempt/AI 审计，execution 保留 Running 直至租约恢复；测试不得伪称这类未完成调用已有审计。
- **权限/API**：后续审计读取只做脱敏投影，复用实例参与者/监控权限，不依赖后台 worker 缺失的 `IDataScopeContext`；M3b-0 不做独立审计管理页或设计器。现有 `Pending` execution-completed outbox 足够，不新增 AI 专属 transport，也不消费它。

#### 已识别的高风险点与验证锚点

- `WfManualFallbackOp` 的“0 人则静默停住”必须由发布校验、显式 audit reason 和负向测试覆盖；任何 fallback 到 `AutoPass` 都是阻断级缺陷。
- AI handler 只把 Provider 的非取消异常转换为安全人工结果；parser/policy/输入投影的未分类异常必须逸出到通用 dispatcher 统一记录，再由 tx2 的 AI 归一化防线收敛为人工结果，避免把内部契约错误误记成 Provider 故障。
- 原始模型响应是外部不可信大文本；必须先做字节上限，再解析严格 schema，错误日志只留分类/长度/hash，不留正文。
- `VariablesJson` 当前不受 schema 约束；只按节点明确白名单构造送模输入，并对禁止字段/值做脱敏，审计仅留 hash。
- tx2 中任何 AI 写入放在 fence CAS 之前都会让 stale owner 留痕；任何任务创建放到事务后都会打破“审计+待办原子性”。
- `WfNodeExecutionWorkerTests.cs:188-229` 才覆盖真实 seed→scheduler→resolver→worker seam；`WfNodeExecutionProductionE2ETests` 的 helper 直接取 `IAdminJob` 执行，绕过 scheduler。M3B0-12/13 应同时复用真实 scheduler seam 与端到端业务断言，不能把后者单独当作完整调度证据。
- 四库唯一键与列宽必须沿现有 `WfNodeExecutionAttempt`/`WfOutbox` 契约测试风格验证；SQL Server PR filter 需显式纳入新增持久化测试。

#### 本轮验证尝试

- 计划命令：`dotnet test backend/TenonAdmin.slnx -c Release --filter "FullyQualifiedName~WfNodeExecutionEntryTests|FullyQualifiedName~WfNodeExecutionDispatcherTests|FullyQualifiedName~WfNodeExecutionWorkerTests|FullyQualifiedName~WfNodeExecutionProductionE2ETests|FullyQualifiedName~WfNodeHandlerContractTests"`。
- 结果：未进入测试，shell 返回 exit 127：`/bin/bash: dotnet: command not found`。`rtk dotnet --info` 同样返回 exit 127；`command -v`、常见安装路径及本机限定路径搜索均未找到 `dotnet`。因此 M3B0-01 保持未勾选，待 SDK 可用后必须重跑上述聚焦测试，再决定是否完成该任务。
- 只读架构代理的父任务未能及时收敛，已停止；其已返回的 worker-chain 证据与本轮直接 CodeGraph 结果一致，不作为独立 reviewer/verifier PASS。

#### M3B0-01 环境修复与聚焦验证补证（2026-09-04，用户明确要求安装 .NET 10）

- 按 Microsoft 官方 `dotnet-install.sh` 方案安装到用户目录：`bash /tmp/dotnet-install.sh --channel 10.0 --quality GA --architecture x64 --install-dir /home/shiny/.dotnet`，成功安装 SDK `10.0.400`。
- 创建未占用的持久入口 `/home/shiny/.local/bin/dotnet -> /home/shiny/.dotnet/dotnet`；该目录已在当前及后续 shell 的 PATH 中，无需 sudo 或修改系统包。
- `dotnet --info`：SDK `10.0.400`、MSBuild `18.9.6`、Host/runtime `10.0.11`、RID `linux-x64`；`Microsoft.AspNetCore.App 10.0.11` 与 `Microsoft.NETCore.App 10.0.11` 已安装。
- 重跑原聚焦命令：`dotnet test backend/TenonAdmin.slnx -c Release --filter "FullyQualifiedName~WfNodeExecutionEntryTests|FullyQualifiedName~WfNodeExecutionDispatcherTests|FullyQualifiedName~WfNodeExecutionWorkerTests|FullyQualifiedName~WfNodeExecutionProductionE2ETests|FullyQualifiedName~WfNodeHandlerContractTests"`。
- 结果：exit 0，`Passed 56 / Failed 0 / Skipped 0 / Total 56`，耗时 56 秒；restore/build 成功。输出仅有仓库既有 C# nullable/XML-doc warnings，无测试失败。M3B0-01 据此完成并勾选，SDK blocker 已解除。

### M3B0-02 失败契约红测（Round 2）

- 新增且仅新增测试文件：`backend/tests/TenonAdmin.Tests/WfAiDecisionContractTests.cs`；未修改生产代码、权威文档或 protected baseline。
- Provider 边界被钉为单一、类型化、可取消的 `IAiDecisionProvider.ProposeAsync(AiDecisionProviderRequest, CancellationToken)`；handler 必须把 `ExecutionKey`、1 基 attempt 和绝对 deadline 原样传入 Provider。
- proposal wire contract 按 AI 基石 §4.2 固定为字符串 `schemaVersion: "1.0"`，严格拒绝数字版本、未知根/证据字段、任一必填根字段或证据字段缺失、命令式 recommendation；`approve|reject|manual` 均为合法 proposal 值。
- evidence `contentHash` 固定为 `sha256:<64 lowercase hex>`；裸 digest、错误算法、长度/字符错误和大写 hex 均拒绝。confidence 明确接受闭区间端点 0/1、拒绝越界，并用 `fr-FR` 用例锁住测试 JSON 的文化无关性。
- 服务端 policy 分类被钉为二维语义：保留模型 recommendation，同时独立给出 `ShadowCandidate`、`RejectRecommended`、`ManualRequested`、`LowConfidence`、`HighRisk`、`EvidenceInsufficient`、`DisallowedReason` 等确定性分类；同一 proposal 重复求值结果一致。
- 所有已完成路径最终均为 `WfNodeExecutionResultType.ManualFallback`：approve 用 `ShadowOnly`，reject 用 `RejectNotAllowed`，manual 用 `ManualRequested`；格式/命令式输出、低置信度、高风险、证据不足、reason 不允许、Provider timeout、typed failure 和抛出的非取消异常各保留不同 typed fallback reason。
- 取消契约：进门前已取消必须 0 次调用 Provider 并抛 `OperationCanceledException`；飞行中取消必须携带原 token 逸出，不转换成人工结果。Provider typed/thrown error 中注入的 `Authorization: Bearer tenon-test-ai-secret` 不得出现在 `Summary`、`OutputJson` 或序列化 typed hand-off。
- handler 结构守卫检查 public/non-public、instance/static 的构造参数/字段/属性，禁止 `IServiceProvider`、`IServiceScopeFactory`、`ISqlSugarClient`、`IWorkflowEngine`、`WfTask`、`WfToken`、`PrimaryId`/SqlSugar 类型，钉住 handler 无法直接推进 task/token 或旁路取得数据库 session。
- 主上下文独立验证命令使用 Python 包装 `dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --filter "FullyQualifiedName~WfAiDecisionContractTests" --no-restore`：包装器 exit 0，内部测试命令按预期 exit 1；共 20 条编译诊断且全部为 `CS0246` 缺少预定 AI contract，既有依赖项目均成功构建，无语法错误或既有测试失败。红测成立。
- 独立 `code-reviewer` 首轮发现 schemaVersion/hash/分类/敏感错误/文化/边界/取消等 9 项问题，复审发现严格 schema 与静态依赖守卫 2 项问题；全部修正后最终复审 `PASS`（0 finding）。
- 强类型红测会让整个 `TenonAdmin.Tests` 项目在 contract 尚未实现时无法编译，不能跨 M3B0-03～07 长期悬红。已立即插入 `M3B0-02A`：下一轮先实现不含 DI/Fake/HTTP/node/tx2/persistence 的最小纯契约纵切并恢复项目可编译，再继续原 M3B0-03。

### M3B0-02A 最小纯契约纵切（Round 3）

- 新增 `backend/src/TenonAdmin.Workflow/Abstractions/AiDecisionContracts.cs`：`IAiDecisionProvider`、封闭的 typed request/result、不可变 proposal/evidence、policy classification/evaluation、fallback reason 与 `AiDecisionOutcome`。Provider failure 工厂接收的原始诊断立即丢弃，不进入后续 hand-off。
- `AiDecisionProviderRequest` 只携带 execution/instance/token/node/definition identity、`OrgId`、starter、business key、1 基 attempt 与绝对 UTC deadline；本轮没有节点字段白名单，故**刻意不携带原始 `VariablesJson`**，避免在 Task 06 前形成未脱敏送模旁路。
- 新增 `backend/src/TenonAdmin.Workflow/Providers/AiDecisionProposalParser.cs`：严格接受 wire schema `"1.0"`，拒绝未知/缺失/重复根字段与 evidence 字段；recommendation 仅 `approve|reject|manual`，confidence 仅 `[0,1]`，content hash 仅 `sha256:<64 lowercase hex>`。同时实现总 JSON、rationale、数组数量及单项字符串长度/深度上限；reason code 固定大写规范形式（测试/默认 allowlist 使用 `POLICY_MATCH`）。
- 新增 `backend/src/TenonAdmin.Workflow/Providers/AiDecisionPolicyEvaluator.cs`：构造时验证并快照 options，固定优先级保留 `ManualRequested`/`RejectRecommended`，其余 approve proposal 依次检查 disallowed reason、evidence、high risk、confidence，最后才成为 `ShadowCandidate`；evaluator 不返回任何工作流 command。
- 新增 `backend/src/TenonAdmin.Workflow/Providers/AiDecisionNodeHandler.cs`：当前是尚未注册/尚未绑定 NodeType 的纯 handler 类；Provider → parser → policy 全链路所有已完成结果均返回 typed `ManualFallback`。进门前、飞行中以及 Provider 忽略 token 后迟到正常返回三种外部取消路径都重新检查并传播原 token；Provider 内部 OCE 才分类为 timeout。异常与 typed failure 只使用固定中文安全摘要。
- 修改 `backend/src/TenonAdmin.Workflow/Abstractions/IWorkflowNodeHandler.cs`：`WfNodeExecutionResult` 增加只读可空 `AiDecision` metadata，并增加 distinct internal `AiManualFallback` 工厂；原 public `ManualFallback(int? errorCode = null, string? summary = null)` 的 overload set 保持不变，`ManualFallback(null)` 的消费者源码兼容由测试锁定。
- 扩展 `backend/tests/TenonAdmin.Tests/WfAiDecisionContractTests.cs`：红测全部转绿，并补 duplicate 字段、解析限制边界、canonical reason code、Org/identity 传播、原始变量不外送、数组型 forbidden dependency、Provider 忽略取消以及 public factory source compatibility 回归。`WfNodeType.Webhook` 仍只是 Task 06 前满足 required context 的临时 fixture；M3B0-06 必须切为 `AiDecision` 并断言 handler 的真实 NodeType。
- 主上下文聚焦验证：`dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WfAiDecisionContractTests|FullyQualifiedName~WfNodeHandlerContractTests"` → `Passed 83 / Failed 0 / Skipped 0`。
- 主上下文 Release build：`dotnet build backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore` → 5 projects，0 errors，0 warnings。
- `git diff --check` 与 task 文件 placeholder/whitespace 扫描通过；无 TODO/HACK/`NotImplementedException`/skip/only。
- 独立 reviewer 首轮发现两项：Provider 忽略 cancellation 后迟到返回会被转成人工、public AI `ManualFallback` overload 会让消费者 `ManualFallback(null)` 产生 `CS0121`。均先补回归测试再修复；复审 `PASS`，无回归。
- 范围确认：未做 DI/config binding、生产 Fake、OpenAI/HTTP、`WfNodeType.AiDecision`、发布 props、tx2/实体/审计/outbox/API/E2E；这些继续由后续任务承担。全量 review 的其余 M3b findings 已映射：变量白名单与真实 NodeType 归 M3B0-06/07，权威 README shadow-only 冲突归 M3B0-16。

### M3B0-03 Provider options / TryAdd DI / replaceability（Round 4）

- 修改 `backend/src/TenonAdmin.Workflow/Abstractions/WorkflowOptions.cs`：新增 `WorkflowOptions.AiDecision` 与 policy-only `AiDecisionOptions`，绑定路径为 `TenonAdmin:Workflow:AiDecision:Policy`；V0 没有 vendor endpoint/key/model，也没有关闭 shadow-only 或自动放行的开关。启动校验拒绝 null policy、置信度/证据数量越界、空/重复/非规范 reason code 与非法 risk flags。
- 修改 `backend/src/TenonAdmin.Workflow/Abstractions/AiDecisionContracts.cs`：新增 `IAiDecisionProposalParser` 与 `IAiDecisionPolicyEvaluator`。为使外部消费者真正可实现这些 SPI，而不是只能转调内置实现，evidence/proposal/parse result/policy evaluation 提供受校验的 public factory；对象仍是不可变快照，并阻止 recommendation/classification 不相容组合。
- 修改 `backend/src/TenonAdmin.Workflow/Providers/AiDecisionProposalParser.cs` 与 `AiDecisionPolicyEvaluator.cs`：默认实现 interface-backed、方法可覆写，parser 通过同一 public validated factory 构造结果，policy evaluation 绑定实际 proposal。
- 新增 `backend/src/TenonAdmin.Workflow/Providers/FailClosedAiDecisionProvider.cs`：真实 Adapter 尚未配置时的完整安全默认实现；校验 request/cancellation，不保存状态、不外呼、不带诊断正文，只返回 typed `Failed`，不是测试 stub。
- 修改 `backend/src/TenonAdmin.Workflow/Providers/AiDecisionNodeHandler.cs`：依赖 parser/policy 接口；仍只是 concrete scoped service，未提前实现/注册 `IWorkflowNodeHandler`，避免在 `WfNodeType.AiDecision` 尚未交付时激活第二条或错误节点路径。
- 修改 `backend/src/TenonAdmin.Workflow/WorkflowSetup.cs`：全部使用 `TryAdd*`；parser/evaluator 为 singleton，provider/handler 为 scoped。嵌套数组配置显式绑定；默认 evaluator factory 对 **DI 最终解析出的** `WorkflowOptions` 再做完整校验，防止消费者前置无效 options 绕过启动校验后触发 NRE。重复调用 `AddTenonAdminWorkflow` 不产生重复 descriptor。
- 修改 `backend/tests/TenonAdmin.Tests/WorkflowReplaceabilityTests.cs`：可替换性由十件套扩为十三件套，前置 `IAiDecisionProvider`、parser、policy evaluator 均胜出。parser/policy fake 独立构造可区分结果，不再借用内置实现掩盖“接口可注册但无法实现”的假 seam。
- 新增 `backend/tests/TenonAdmin.Tests/WfAiDecisionRegistrationTests.cs`：覆盖 fail-closed 默认 Provider、取消、默认 handler graph、嵌套 policy/数组绑定、非法配置、有效/无效前置 options、生命周期、重复 Add、以及当前 `IWorkflowNodeHandler` 集合恰好只有 factory-backed `WebhookNodeHandler`。
- 主上下文验证：`dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WfAiDecisionRegistrationTests|FullyQualifiedName~WfAiDecisionContractTests|FullyQualifiedName~WorkflowReplaceabilityTests"` → `Passed 93 / Failed 0 / Skipped 0`。
- 主上下文 Release build：`dotnet build backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore` → 5 projects，0 errors，0 warnings；`git diff --check` 与 task 文件 placeholder/whitespace 扫描通过。
- 独立 reviewer 首轮确认 3 项有效问题：公开 parser/policy SPI 结果不可由外部构造、只校验被 TryAdd 丢弃的本地 options、no-premature-handler 测试漏掉 factory descriptor。均先补回归测试再修复；最终复审 `PASS`。
- 范围确认：未实现 Task 04 Fake、Task 05 OpenAI/HTTP、Task 06 AiDecision enum/props、任何 tx2/实体/审计/outbox/API/E2E；protected baseline 未触碰。

### M3B0-04 确定性 Fake Provider（Round 5）

- 新增 `backend/src/TenonAdmin.Workflow/Providers/FakeAiDecisionProvider.cs`：公开、非 sealed 的确定性测试 Adapter；`FakeAiDecisionScenario` 枚举只追加且无 0 值，覆盖 `Success/MalformedProposal/LowConfidence/HighRisk/TimedOut/Failed/Throws`。参数less 构造默认 Success，显式构造拒绝未定义枚举值。
- Fake proposal 使用 `System.Text.Json` 生成字节稳定的 canonical JSON：`schemaVersion="1.0"`、`approve`、`POLICY_MATCH`、固定 bounded rationale、单条 `sha256:` evidence；低置信度只改变 confidence，高风险只加入公开稳定的 `HighRiskFlag`。无随机数、时钟、sleep、网络、数据库或真实凭据。
- Fake 的 `ProposeAsync` 先检查 cancellation 与 request，再用 `Interlocked` 增加调用计数；预取消不计数、不产结果。timeout/failure 返回封闭 typed result，Throws 抛固定安全 `InvalidOperationException`，由现有 shadow handler 收敛为无正文的 `ProviderFailure`。
- 新增 `backend/src/TenonAdmin.Workflow/Providers/AiDecisionProviderRequestValidator.cs`：抽取内部共享 request 校验，Fake 与 `FailClosedAiDecisionProvider` 复用，避免两份 identity/deadline 验证漂移；不扩大 public surface。
- 新增 `backend/tests/TenonAdmin.Tests/WfFakeAiDecisionProviderTests.cs`：覆盖逐场景 direct result、成功 JSON 字节一致、parser/policy 的 `ShadowCandidate/LowConfidence/HighRisk`、所有场景最终 typed `ManualFallback`、Throws 安全摘要、预取消 0 调用、合法调用计数、非法枚举，以及结构上不保留 request/PII、无数据库/网络依赖。
- `backend/tests/TenonAdmin.Tests/WorkflowReplaceabilityTests.cs` 的 Provider 前置注册测试改用公开生产 Fake，证明消费者可直接 opt-in；`WorkflowSetup` 默认仍是 fail-closed Provider，Fake 未被默认注册。
- 主上下文验证：`dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WfFakeAiDecisionProviderTests|FullyQualifiedName~WfAiDecisionContractTests|FullyQualifiedName~WfAiDecisionRegistrationTests|FullyQualifiedName~WorkflowReplaceabilityTests"` → `Passed 119 / Failed 0 / Skipped 0`。
- 主上下文 Release build：`dotnet build backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore` → 5 projects，0 errors，0 warnings；`git diff --check` 与 task 文件 placeholder/whitespace 扫描通过。
- 独立 `code-reviewer` 复核 deterministic output、cancellation-before-count、线程安全计数、request 不留存、policy 对齐、默认 DI 与范围边界，结论 `PASS`，无 actionable finding。
- 范围确认：未修改默认 DI 选择，未实现 OpenAI/HTTP、AiDecision node、dispatcher、tx2/审计/persistence/outbox/API/E2E；protected baseline 未触碰。

### M3B0-05 OpenAI-compatible HTTP Adapter（Round 6）

#### 权威协议与兼容性裁决

- 选择 `POST /v1/chat/completions`，不是 Responses API：OpenAI 当前推荐新项目使用 Responses，但第三方“OpenAI-compatible”服务最广泛共享的是 Chat Completions 的 `model + messages + choices[0].message.content` 形状；v0 以可移植性优先。
- 官方依据：OpenAI OpenAPI [`openai-openapi`](https://github.com/openai/openai-openapi)；Chat Completions/Authorization 示例 [`latest-model`](https://developers.openai.com/api/docs/guides/latest-model)；Structured Outputs refusal [`structured-outputs#refusals-with-structured-outputs`](https://developers.openai.com/api/docs/guides/structured-outputs#refusals-with-structured-outputs)；错误语义 [`error-codes`](https://developers.openai.com/api/docs/guides/error-codes)；`response_format.json_schema` 类型 [`response_format_json_schema.py`](https://github.com/openai/openai-python/blob/main/src/openai/types/shared/response_format_json_schema.py)；request-id/timeout 参考 [`openai-python`](https://github.com/openai/openai-python#request-ids)。
- v0 request 固定为 `Content-Type: application/json`、可选 `Authorization: Bearer`、`model`、两条字符串 message（兼容面更广的 `system` + `user`），以及 `response_format={type:"json_schema",json_schema:{name:"tenon_ai_decision_proposal",strict:true,schema:{...}}}`；schema 的 required/additionalProperties/bounds 与服务端 parser 常量同源。
- success 只接受单次非流式响应中 `choices[0].finish_reason == "stop"`、无非空 `message.refusal` 且 `message.content` 为非空字符串；missing/null/length/content_filter 或 envelope 错误全部 fail closed。SDK 的默认 retry/10 分钟 timeout 不是 HTTP 合约，本 Adapter 明确 0 retry、单一总 timeout。

#### 实现与验证

- 修改 `backend/src/TenonAdmin.Workflow/Abstractions/WorkflowOptions.cs`：新增 `AiDecision.OpenAiCompatible` 配置。默认 `Enabled=false`、官方完整 endpoint、30s timeout、64KiB response cap；timeout `[1,120]`、cap `[1KiB,1MiB]`。启用时 model 必填；URI 必须绝对 HTTP/HTTPS、无 userinfo/fragment/control char。HTTPS 默认强制；即使显式允许 HTTP，也禁止携带非空 API key，杜绝明文 Bearer 泄漏。校验异常不回显 key 或 endpoint query。
- 新增 `backend/src/TenonAdmin.Workflow/Providers/OpenAiCompatibleAiDecisionProvider.cs`：公开、非 sealed、纯 `HttpClient` 实现，无模型 SDK。固定 proposal-only/shadow-only system 指令；user message 只含 execution/node/org/attempt 身份，不发送 `VariablesJson`、business key、starter id、API key 或后续节点 prompt。
- Adapter 使用 `ResponseHeadersRead` 和受限流式读取，在 JSON 解析前执行总字节 cap；非 2xx、refusal、非 stop finish、空 choices/content、malformed envelope、cap overflow、`HttpRequestException`/`IOException`/`JsonException` 全部返回无正文 typed `Failed`。外部取消原 token 传播；内部 timeout/deadline 返回 `TimedOut`；无自动 retry。
- 取消/timeout 采用显式 send-task ownership：当 `WaitAsync` 先退出但底层 handler 忽略 token 时，延迟保留 request 所有权、观察晚到任务并释放 late response/content，避免连接/响应泄漏。
- 修改 `backend/src/TenonAdmin.Workflow/WorkflowSetup.cs`：保留 consumer-first `TryAdd`。disabled 配置继续生成 fail-closed Provider；enabled 才使用已有 singleton `JobHttpClient.Client`、effective options 与 `TimeProvider`。构造前显式调用 `JobHttpFence.ValidateUrl` 以执行 `AllowedHosts`/CIDR policy，运行时 DNS callback 继续防 rebinding；围栏失败转为不含 URL/query/key 的统一安全异常。
- 新增 `backend/tests/TenonAdmin.Tests/WfOpenAiCompatibleAiDecisionProviderTests.cs`、`WfOpenAiCompatibleAiDecisionOptionsTests.cs` 与 review regression tests：fake `HttpMessageHandler` 覆盖 exact protocol/schema、可选 Bearer、敏感值不进 body/result、disabled 0 send、success、refusal/finish/envelope、400/401/429/500、网络/流异常、chunk/cap、0 retry、timeout、外部取消、late disposal、options binding/DI selection、consumer replacement、Jobs `AllowedHosts` 与 HTTP key 禁止。
- 主上下文验证：`dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WfOpenAiCompatibleAiDecisionProviderReviewRegressionTests|FullyQualifiedName~WfOpenAiCompatibleAiDecisionProviderTests|FullyQualifiedName~WfOpenAiCompatibleAiDecisionOptionsTests|FullyQualifiedName~WfAiDecisionContractTests|FullyQualifiedName~WfAiDecisionRegistrationTests|FullyQualifiedName~WfFakeAiDecisionProviderTests|FullyQualifiedName~WorkflowReplaceabilityTests"` → `Passed 161 / Failed 0 / Skipped 0`。
- 主上下文 Release build：`dotnet build backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore` → 5 projects，0 errors，0 warnings；`git diff --check` 与 task 文件 placeholder/whitespace 扫描通过。
- 独立 reviewer 首轮确认 4 项：遗漏 Jobs host allowlist、HTTP 可明文发送 Bearer、取消时晚到 response 泄漏、missing/null finish_reason 被接受。均先补 regression tests 后修复；复审 `PASS`（0 finding）。
- Semgrep Guardian 在本会话未认证，扫描 hook 不可用；没有宣称 Semgrep 结果。普通构建、测试、独立代码审查与手工边界验证均已完成，此项不构成本任务 blocker。
- 范围确认：未加入模型 SDK，未实现 AiDecision node/props、tx2/审计/persistence/outbox/API/E2E 或 Task 8c；protected baseline 未触碰。

### M3B0-06 既有未记账改动的只读验收（Round 7，2026-09-07）

- 本轮重读根指令（当前 `CLAUDE.md` 为 `@AGENTS.md` 导入）及完整台账，执行 `pwd; git status --short --branch`，确认工作目录与 `dev...origin/dev` 分支。Round 6 的 `NEXT` 与首个未完成项 M3B0-06 一致。
- 除 Round 1–6 已记账的 AI 文件外，工作树已经存在 Task06 的 enum/props/发布校验、handler NodeType 与 fixture 修改及 `WfAiDecisionNodeDefinitionTests.cs`，同时存在独立的技能/指令整理改动。本轮未假定其归属，只做只读核验；未修改这些实现、测试、技能、根指令、旧台账或 workflow 文档。
- 使用 Python 对 `git status --porcelain=v1 -z --untracked-files=all` 的 149 个路径记录状态与 SHA-256，快照为 `/tmp/tenon-m3b0-round7-baseline.json`；中断后重新执行 `pwd; git status --short --branch` 并比较，149 个路径均未改变。本轮仓库写入范围只有本台账。
- CodeGraph 先定位 `ValidateModelForPublish → ValidateChain → ValidateNode → ValidateAiDecisionNode`，并核对发布快照序列化、handler 声明与 DI 集合。独立 `m3b0-06-verifier` 只读审查结论为 **FAIL**；M3B0-06 保持未勾选，新增紧邻的剩余子任务 M3B0-06A。

#### 有效发现与运行证据

- **已确认的 Task06 阻断**：`backend/src/TenonAdmin.Workflow/Services/WfDefinitionService.cs:422-469` 的 AI 校验仅检查指令、输入字段、办理人，不拒绝 `WfNodeProps` 上已知但不属于 AI 的共享字段；`Schema/WfNode.cs:83-106` 的 `WebhookUrl`/`WebhookHeaders` 可以被映射并保留。`PublishAsync` 在 `Services/WfDefinitionService.cs:160-169` 校验后直接序列化模型，因此存在 endpoint/认证头进入 AI 发布快照的路径，违反本台账“最小节点 props、endpoint/key 只属于服务端配置”的边界。
- 独立代理在 `/tmp/tenon-m3b0-round7-verifier-probe.J0mpI4/Program.cs` 构造仅含虚拟数据的 v1 `start → aiDecision` 模型，执行真正的 `WfModelJson.Deserialize → ValidateModelForPublish → WfModelJson.Serialize`。主上下文已完整读取探针源码与 `/tmp/tenon-m3b0-round7-verifier-ai-props-probe.log`：输出 `validation=passed`，`mappedWebhookUrl=https://probe.invalid/endpoint`、`mappedAuthorization=Bearer probe-not-a-credential`、`mappedNobody=AutoPass`，序列化快照仍包含这三个字段；日志记录 exit 0。**这是发布校验/序列化运行探针，不是调用 `PublishAsync` 的数据库持久化测试，也不是已修复后的绿测。** 没有使用真实 endpoint、凭据或 Provider 外呼。
- `Nobody=autoPass` 的配置也能保留，但本轮不据此声称 AI 已发生运行时自动批准；Task10/11 的二次 shadow 防线与人工兜底仍未实现。修复需限制 AI 专属可发布 props，而不是改变 Webhook 或普通审批节点的合法配置。
- 审查中另提及未知 `endpoint/apiKey/model/shadowMode` 字段被忽略。已区分：这些未知字段会被反序列化丢弃，**没有证据证明它们进入发布快照**；不把这项等同于已知 Webhook 字段的持久化旁路，也不据此要求全局收紧旧模型的未知字段兼容行为。
- 已有新增旧 v1 测试只验证 codec 往返，尚缺旧草稿真实发布的兼容性证据；该回归纳入 M3B0-06A。枚举尾部追加、真实 `NodeType=AiDecision`、concrete handler 但未进入 dispatcher 集合、指令/字段/办理人/MaxAttempts 的已有值校验经独立核验符合 Task06 的其余边界。

#### 聚焦验证结果

- 独立核验测试日志 `/tmp/tenon-m3b0-round7-verifier-targeted-tests.log` 已由主上下文读取：`Passed 126 / Failed 0 / Skipped 0 / Total 126`，耗时 7 秒，`[verifier-exit] 0`。现有测试通过不覆盖上述有效发现，不能作为 Task06 PASS。
- Workflow Release build 日志 `/tmp/tenon-m3b0-round7-verifier-workflow-build.log` 已由主上下文读取：5 个项目构建成功、0 warnings、0 errors、2.69 秒，`[verifier-exit] 0`。代理已补回以下原始精确命令，与主上下文读取的日志一致；本轮命令记录缺口已补齐：

```bash
dotnet test /home/shiny/github/TenonAdmin/backend/TenonAdmin.slnx -c Release --no-restore --filter "FullyQualifiedName~WfAiDecisionNodeDefinitionTests|FullyQualifiedName~WfDefinition|FullyQualifiedName~WfAiDecisionContractTests|FullyQualifiedName~WfAiDecisionRegistrationTests|FullyQualifiedName~WfFakeAiDecisionProviderTests|FullyQualifiedName~WorkflowReplaceabilityTests"
dotnet build /home/shiny/github/TenonAdmin/backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore
dotnet run --project /tmp/tenon-m3b0-round7-verifier-probe.J0mpI4/AiPropsProbe.csproj -c Release
git diff --check -- backend/src/TenonAdmin.Workflow/Schema/WfSchemaEnums.cs backend/src/TenonAdmin.Workflow/Schema/WfNode.cs backend/src/TenonAdmin.Workflow/Services/WfDefinitionService.cs
```

- 上述四条命令均 exit 0。代理明确确认：没有新增失败契约红测，也没有执行数据库版 `PublishAsync` 探针；临时探针仅证明真实发布校验加序列化的已知字段旁路。测试必须先红后绿的修复步骤留给 M3B0-06A，不把本轮探针成功冒充修复成功。
- 主上下文执行 `git diff --check`：exit 0；对 Task06 相关的 8 个现有实现/测试文件扫描 TODO/FIXME/HACK/stub/NotImplementedException/test.skip/only/Skip/Assert.Skip：无命中。输出 `/tmp/tenon-m3b0-round7-static-check.log`：`Task06 files checked: 8`、`Placeholder scan: PASS`、`git diff --check exit: 0`。
- 最终台账/基线核验：`/tmp/tenon-m3b0-round7-final-integrity.log` 记录 `Ledger semantic checks: PASS`、`State: active, round 7/40`、首项 `M3B0-06`、下一子项 `M3B0-06A`；与开轮快照相比仅本台账发生变化，其他 148 个既有变更路径的状态/内容哈希均未改变，`git diff --check exit: 0`。
- 未运行完整 solution Release build、SQLite 全量、四库目标矩阵、contract drift 或双前端生成/验证；这些仍由 M3B0-15/18/19 负责。本轮发现的 schema/DTO 变化保持该后续验证要求，未手工编辑任何生成 schema。

### M3B0-06A 红测尝试受认证门禁阻断（Round 8，2026-09-07）

- 本轮重新读取根指令与完整台账，执行 `pwd; git status --short --branch`，确认 `/home/shiny/github/TenonAdmin`、`dev...origin/dev`、`round: 7 / max: 40`、首项 M3B0-06 及用户已授权接续的范围。开轮 149 个变更路径的状态/SHA-256 保存到 `/tmp/tenon-m3b0-round8-baseline.json`；已授权的 8 个实现/测试文件开轮原文保存到 `/tmp/tenon-m3b0-round8-before/`，供仅比较增量，不用于覆盖回滚。
- 委派作者代理 `m3b0-06a-executor` 先补真实发布红测，再最小修复 AI props 白名单。作者报告对 `backend/tests/TenonAdmin.Tests/WfAiDecisionNodeDefinitionTests.cs` 的 Edit 未保留，起初推测“并发覆盖”并拟重应用；主进程立即要求停止重应用及代码写入，仅保全证据。
- **已纠正归因**：作者随后确认 Edit 返回 `Not logged into Semgrep Guardian. Ask the guardian mcp to login.`，不是可采信的写入成功。主进程独立调用 `mcp__plugin_semgrep_guardian__whoami` 同样返回该原文并标为工具失败。因此当前可确认的阻塞是 Guardian 未认证/编辑门禁，不是已证实的并发写入；没有把来源不明的修改据为己有，也没有发起源文件回滚。
- 主进程在 `2026-09-07T03:30:49.778310+00:00` 只读比较开轮快照：149 个变更路径的内容和 git 状态均未改变。测试文件 SHA-256 仍为 `4e3b5b9cb5814be289f34c4cf7e1b705f56e8b6afcde32d8dd48c12784efd2ce`；状态快照 `/tmp/tenon-m3b0-round8-conflict-state.json`。mtime 变化不能证明有外部覆盖，不能据此反复重应用代码。
- 作者尝试运行的精确命令如下，日志 `/tmp/tenon-m3b0-round8-red.log` 已由主上下文读取：

```bash
dotnet test /home/shiny/github/TenonAdmin/backend/TenonAdmin.slnx -c Release --no-restore --filter "FullyQualifiedName~WfAiDecisionNodeDefinitionTests"
```

- 已读取的运行输出为 `Passed 6 / Failed 0 / Skipped 0 / Total 6`、7 秒，只能证明当次运行发现原有 6 个测试；日志未附退出码，不能凭推断补写。**该日志不能证明新增发布失败用例经过红测；尚无可核验的修复后绿测或独立复审 PASS。** 后续只读检查发现测试与生产服务均已出现增量，不能再声称“新增测试未落盘”或“源码保持开轮基线”；详情见下方收尾补证。M3B0-06/06A 均不勾选。
- Round 6 “Semgrep 未认证不构成 blocker”只描述当时扫描不可用的事实，不是跳过当前 Edit 门禁的授权。已明确禁止作者改用 Write/Bash/其他代理绕过被阻断的 Edit，禁止擅自禁用 hook、修改权限或登录外部服务。本轮停在认证恢复前，未推进 M3B0-07 或任何执行内核/审计/前端/文档任务。

#### Round 8 收尾补证：迟到差异保全（2026-09-07，不新增工作轮次）

- 首次收尾完整性检查 exit 1，断言实际差异为 `['.loop/wf-m3b0-ai-decision.md', 'backend/tests/TenonAdmin.Tests/WfAiDecisionNodeDefinitionTests.cs']`，因此“只有台账改变”的预期不成立；检查在写入 `/tmp/tenon-m3b0-round8-final-integrity.log` 之前失败，该路径不能作为 PASS 证据。
- 已通过 `TaskStop(task_id: "m3b0-06a-executor")` 停止本会话作者任务，工具返回 `Successfully stopped task: tl8o2k4db`。恢复上下文后只做只读差异核对及本台账纠错，没有重启作者、修改 C#、运行构建/测试、登录 Guardian 或改变权限/hook。`ListAgents` 仅列出空闲的本会话 verifier，没有列出该作者；不触碰其他会话。
- `2026-09-07T03:46:55.467859+00:00` 对测试文件先用 CodeGraph 定位，再用 Python `difflib.unified_diff` 与 `/tmp/tenon-m3b0-round8-before/` 原文比较：新增禁止已知 props 的 17 组输入、旧 v1 审批/Webhook 真实发布用例、MaxAttempts 的 null/上下界/越界用例，以及扩展模型构造和错误脱敏/无发布版本断言。当前 SHA-256 为 `48c3cfd8885e080c069f50bbf389ea09c03af5edbe493beecda211760e288e45`，开轮为 `4e3b5b9cb5814be289f34c4cf7e1b705f56e8b6afcde32d8dd48c12784efd2ce`。这里只确认代码存在，不确认可编译或测试结果。
- `2026-09-07T03:47:41.210429+00:00` 再次比较 `git status --porcelain=v1 -z --untracked-files=all` 与开轮 JSON：仍为 149 个路径，但实际差异扩为台账、测试文件和 `backend/src/TenonAdmin.Workflow/Services/WfDefinitionService.cs` 三处，另外 146 个路径的状态/内容 SHA-256 一致。以“仅前两处变化”为预期的包装检查 exit 1；其中独立执行的 `git diff --check` 为 exit 0。不得把格式检查成功等同于完整性或功能验证通过。
- `2026-09-07T03:48:04.507312+00:00` 对发布服务先用 CodeGraph，再与开轮副本比较：`ValidateAiDecisionNode` 增加 `aiPropsUnsupported` 检查，新增 virtual `HasUnsupportedAiDecisionProps`，用反射允许 `Assignee/MaxAttempts/AiInstructions/AiInputFields` 并拒绝其他非 null 已知属性。当前 SHA-256 为 `fa88ad84dcee684654e584d6451069806befb356605992d0267f892d02a1930c`，开轮为 `37b3d3425c7e675da35f3e85a967425fc05876fdc3bac74496ea37fad4f965ef`。这份实现增量未获本轮验收，不将源码存在当作已完成修复。
- 两个源文件的观察到的 mtime 分别为 `2026-09-07T03:37:11.226789+00:00`、`2026-09-07T03:38:46.752798+00:00`；mtime 与内容只能证明观察到差异，不能证明写入者、具体工具调用或是否属于在途操作。此前“并发覆盖”仍未证实，Guardian 的未认证返回也不足以断言所有 Edit 都未落盘。保留现状，不回滚、不反复重应用、不宣称来源或 TDD 顺序已核验。
- 当前继续保持 `status: blocked-user`、`round: 8 / max: 40`。本次只是纠正 Round 8 的证据，不新增 Round 9，不勾选任务；自动循环在本次收尾停止。正常授权编辑能力恢复且上述差异可安全接续后，仍从 M3B0-06 的 M3B0-06A 开始，先补齐可核验的红测证据，再完成绿测和独立复审。

#### Round 8 后恢复检查（2026-09-07，不新增工作轮次）

- 用户自行完成 Guardian 登录并重载插件，随后明确要求“继续”。本次原生 `mcp__plugin_semgrep_guardian__whoami` 再次成功返回 `auth_method: oauth`；未再次登录、读取凭据文件、修改权限/hook 或绕过工具边界。
- `2026-09-07T05:21:40.930285+00:00` 重读根指令、完整台账及本会话 checkpoint，执行 `pwd; git status --short --branch`，确认目录与 `dev...origin/dev`。当前仍为 149 个变更路径；相对 Round 8 开轮仅本台账、发布服务和测试文件不同，其余 146 个路径状态/哈希一致。两份源码哈希分别仍为 `fa88ad84dcee684654e584d6451069806befb356605992d0267f892d02a1930c` 与 `48c3cfd8885e080c069f50bbf389ea09c03af5edbe493beecda211760e288e45`，与 Round 8 收尾观察一致，没有新的待隔离源码变化。新基线：`/tmp/tenon-m3b0-round9-baseline.json`。
- CodeGraph 与开轮副本差异核对确认仍只有已记录的 AI 专属 props 校验与发布回归增量。既有作者已停止，本次不重启该作者、不回滚、不重复应用这些修改；沿 Round 7 明确授权的同一范围安全接续。
- 历史写入者与 Round 8 的先红后绿顺序仍不作追认。M3B0-06/06A 保持未勾选：接下来在独立临时副本用已保存的修复前发布服务运行当前回归，补充可核验的缺陷检出证据，再验证当前实现并独立复审。该回放不能改写成历史 TDD 证明。
- 用户侧恢复条件已满足，`status` 恢复 `active`，`round` 暂留 8；本次先执行一个 M3B0-06A 工作单元，收尾才记 Round 9，不重建此前已停止的定时循环。

### M3B0-06/06A 保留增量的缺陷检出回放与验收（Round 9，2026-09-07）

#### 验收范围与输入保全

- 本轮只完成 M3B0-06 的剩余子项 M3B0-06A；主工作树中的实现及测试没有再次修改，唯一仓库写入是本台账。执行代理 `m3b0-06a-recovery` 与独立核验代理 `m3b0-06-verifier` 均保持主工作树源码只读，没有重启此前停止的作者。
- 验收的已有实现：`Services/WfDefinitionService.cs:422-488` 在其他 AI 配置校验前拒绝非批准的已知 props；`WfNodeProps` 为 sealed，共 21 个 public declared 属性，仅允许 `Assignee/MaxAttempts/AiInstructions/AiInputFields`。其他 17 项只要映射为非 null 即拒绝，抛错只带 `reason/nodeId`；旧 v1 未知字段仍忽略并丢弃，不收紧所有旧模型。virtual seam 与 TryAdd 可替换性保持不变。
- 验收的已有回归：`WfAiDecisionNodeDefinitionTests.cs:32-69,382-407` 对 17 项共享 props 执行真实 authenticated add→publish，并断言 `48002/aiPropsUnsupported`、错误脱敏、`currentVersion=0` 与无已发布快照；`:72-98` 验证旧 v1 Approval/Webhook 真实发布及快照逐字一致；`:100-135` 验证 AI MaxAttempts null/1/100 成功、0/101 失败；`:293-330` 固定旧枚举值与新增 `AiDecision=6`。
- NodeType 与 AI fixtures 已一致切为 AiDecision，但 `WorkflowSetup.cs:72-128` 仍只注册 concrete AI handler，`IWorkflowNodeHandler` 集合只有 Webhook，`EnterNodeOp.cs:50-76` 尚无 AI 分支。未提前实现 Task10/11，未增加第二套 dispatcher/worker 或 Task 8c。
- 隔离回放目录：`/tmp/tenon-m3b0-round9-defect-replay-tj61iq6p/`。使用当前测试源（SHA-256 `48c3cfd8885e080c069f50bbf389ea09c03af5edbe493beecda211760e288e45`）与 `/tmp/tenon-m3b0-round8-before/` 保存的修复前服务（SHA-256 `37b3d3425c7e675da35f3e85a967425fc05876fdc3bac74496ea37fad4f965ef`）。输入清单 `source-input-sha256.txt` 的 SHA-256 为 `58397c7c554f13a2cb8219e4d041c30abe3cefa624d10fdd41d68c0c194a570c`；日志与 TRX 哈希另记于 `evidence-sha256.txt`。

#### 精确命令与结果

以下命令都来自日志中的 `[command]`，每份日志都有实际 `[exit]`。主进程已读取当前验证/证据检查日志，以及原始回放日志的命令、编译输出与断言失败/退出段；独立 verifier 另行核对运行证据。

```bash
dotnet restore /tmp/tenon-m3b0-round9-defect-replay-tj61iq6p/backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj --configfile /tmp/tenon-m3b0-round9-defect-replay-tj61iq6p/NuGet.config --ignore-failed-sources
dotnet test /tmp/tenon-m3b0-round9-defect-replay-tj61iq6p/backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WfAiDecisionNodeDefinitionTests.AiDecision_publish_rejects_non_approved_known_props_without_persisting_snapshot" --logger "console;verbosity=normal" --logger "trx;LogFileName=replay-known-props.trx" --results-directory /tmp/tenon-m3b0-round9-defect-replay-tj61iq6p/TestResults
python3 /tmp/tenon-m3b0-round9-defect-replay-tj61iq6p/collect-replay-evidence.py
dotnet test /home/shiny/github/TenonAdmin/backend/TenonAdmin.slnx -c Release --no-restore --filter "FullyQualifiedName~WfAiDecisionNodeDefinitionTests|FullyQualifiedName~WfDefinition|FullyQualifiedName~WfAiDecisionContractTests|FullyQualifiedName~WfAiDecisionRegistrationTests|FullyQualifiedName~WfFakeAiDecisionProviderTests|FullyQualifiedName~WorkflowReplaceabilityTests"
dotnet build /home/shiny/github/TenonAdmin/backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore
git -C /home/shiny/github/TenonAdmin diff --check
python3 /tmp/tenon-m3b0-round9-defect-replay-tj61iq6p/check-authorized-files.py
python3 /tmp/tenon-m3b0-round9-defect-replay-tj61iq6p/compare-current-integrity.py
```

- `replay-restore.log`：exit 0，临时目录的项目 restore 成功；未改变仓库依赖声明或配置。
- `replay-known-props.log` 与 `TestResults/replay-known-props.trx`：**预期 exit 1，Failed 17 / Passed 0 / Total 17**，24.5785 秒。17 个失败全部是旧实现真实发布返回 `0`、而测试期望 `48002` 的断言差异，不是编译/加载/环境错误；包括 Webhook URL、Authorization 请求头和 Nobody。隔离编译有既有 nullable/XML-doc warnings，不能把它们计作回归检出依据。
- `replay-evidence.log`：exit 0；17 次 publish 请求和 endpoint 执行、17 个失败用例、17 项 props 无遗漏；全部 content root、MVC manifest 与程序集在副本内，未引用主目录 host。三个副本位置的 Workflow DLL 哈希相同且均不含 `HasUnsupportedAiDecisionProps`，当前工作树 DLL 则含该方法、哈希不同，排除误用修复后 DLL 造成伪回放。
- `current-focused-tests.log`：**exit 0，Passed 149 / Failed 0 / Skipped 0 / Total 149**，38 秒。包含新增拒绝矩阵、旧 v1 发布、预算边界及原有 definition/AI contract/registration/Fake/replaceability 回归。
- `current-workflow-build.log`：**exit 0，Workflow Release build 成功，0 warnings、0 errors**，2.18 秒。这是 Workflow 项目构建，不冒充最终完整 solution Release build。
- `git-diff-check.log`、`authorized-placeholder-scan.log`、`current-integrity.log`：均 exit 0；8 个授权文件 placeholder 检查无命中；149 个脏路径相对本轮基线仅台账改变，其余 148 个路径的状态/内容不变。发布服务仍为 `fa88ad84dcee684654e584d6451069806befb356605992d0267f892d02a1930c`，定义测试仍为 `48c3cfd8885e080c069f50bbf389ea09c03af5edbe493beecda211760e288e45`。

#### 独立结论与证据限制

- 独立 `m3b0-06-verifier` 在只读代码复审后再次读取上述回放、绿测、build、placeholder 与 integrity 证据，最终结论为 **PASS，仅限 M3B0-06/06A，0 actionable finding**。其静态集合核对为 17 个不允许属性与 17 个 HTTP case 完全对应，missing/extra 均空；反射 allowlist 没有发现可执行绕过或需要额外复杂化的缺陷。
- Round 7 的缺陷探针、Round 9 的修复前真实发布回放、当前 149 个绿测及独立 PASS 共同支持验收保留的 Task06 修复。**本轮回放是新的缺陷检出证据，不证明 Round 8 的历史写入者或先红后绿顺序；旧 6 测试输出仍不作为新代码验收证据。** 本轮未新增生产改动，未回滚现有源码以制造红测。
- M3B0-06/06A 据此勾选；`status` 保持 `active`，`round` 更新为 9。最终完整 solution build、SQLite 全量、四数据库目标矩阵、contract drift、条件适用的双前端验证以及 M3b 总体独立核验仍未执行，由 M3B0-15/18/19/20 继续承担；不声称 M3b-0 已交付。
- 主进程收尾校验 `/tmp/tenon-m3b0-round9-final-integrity.log`：exit 0；台账状态、任务勾选、唯一 Round 9 与 NEXT=M3B0-07 一致，无未解决用户 blocker；证据清单的 12 个文件 SHA-256 全部匹配；149 个脏路径中仅本台账改变，另外 148 个路径完整保留；`git diff --check` 通过。
- 收尾后补充证据边界（不新增轮次，不重复测试）：基线比较覆盖 Git 可见的路径、状态与内容，不证明被忽略的 `bin/obj` 构建产物逐字节不变。测试工厂照常使用系统临时目录中的 SQLite 文件，ASP.NET 运行时使用既有 Data Protection 仓库；未宣称全部运行时文件都位于回放目录，也未检查密钥目录以证明绝对无运行时写入。源码、构建产物来源与 content root 的隔离结论不变。
- 当前绿测/build 按要求使用 `--no-restore`，覆盖的是已有 restore assets；只有隔离回放执行了独立 restore。修复方法存在性证据是程序集元数据名称的字节探针，结合源码/DLL 哈希、构建路径及 MVC manifest，不是完整 IL 反编译。旧实现的 17 个回放用例在错误码断言处失败，后续无版本断言在该回放中未执行；这些断言由当前实现的绿测覆盖。执行代理正式报告与独立 verifier 的原始日志核验一致，Task06/06A PASS 不变。

### M3B0-07 parser/policy 重复项与阈值边界子项（Round 10，2026-09-07）

#### 开轮与改动范围

- 用户明确要求“继续”后，重读根 `CLAUDE.md`、`AGENTS.md`、完整台账与当前恢复 checkpoint（`active_modes: {}`），执行 `pwd; git status --short --branch`：目录为 `/home/shiny/github/TenonAdmin`，分支为 `dev...origin/dev`，状态 `active`、9/40，首个未完成项与上一轮 NEXT 均为 M3B0-07。没有重启已停止的 `/loop`，没有创建 Team/Workflow。
- `2026-09-07T06:09:26.024516+00:00` 比较 Round 9 基线：149 个 Git 可见变更路径中只有台账的既有验收补注不同，其他 148 项状态/内容均相同。开轮快照 `/tmp/tenon-m3b0-round10-baseline.json`，SHA-256 `2442e45915268fa2e3c3f5862682a8fec3a8a6da5e527db22531441267302beb`。
- CodeGraph 先追踪当前 proposal parser、公开 validated factory、policy、handler 与相关测试；基于 Task02A/03 已有实现做窄增量，没有重写完整 AI 链。唯一源码作者为 `m3b0-07-executor`，独立核验为只读的 `m3b0-07-verifier`；主进程只写本台账及临时核验脚本。
- 修改 `backend/src/TenonAdmin.Workflow/Abstractions/AiDecisionContracts.cs` 的 `AiDecisionProposal.ValidateReasonCodes/ValidateRiskFlags/ValidateEvidence`：公开 factory 拒绝重复集合项，保持 immutable snapshot；字符串采用 Ordinal 精确身份，evidence 的唯一身份是精确 `(Id, Source, ContentHash)` 三元组。
- 修改 `backend/src/TenonAdmin.Workflow/Providers/AiDecisionProposalParser.cs` 的 `TryParseReasonCodes/TryParseRiskFlags/TryParseEvidence`：strict parser 与公开 factory 执行相同唯一性规则；未知/缺失/重复 JSON 属性、原有大小与格式边界继续保留。没有改变 `AiDecisionPolicyEvaluator`、`AiDecisionNodeHandler`、DI 或 HTTP adapter 的生产逻辑。
- 新增 `backend/tests/TenonAdmin.Tests/WfAiDecisionProposalPolicyBoundaryTests.cs`：12 条 case 覆盖重复 reason/risk/evidence、公开 factory、重复 evidence 不能虚增最低证据数、`MalformedProposal → ManualFallback`、confidence/evidence 的精确阈值和 policy 固定优先级；没有改动或放宽既有测试断言。

#### 缺陷、协议裁定与证据

- 修复前，相同 evidence 两份能够满足 `MinimumEvidenceCount=2`，合法 approve proposal 因而错误获得 `ShadowOnly` 分类；strict parser 与 public factory 也接受重复 reason/risk/evidence。**这是服务端分类与证据计数缺陷，不是 AI 已自动批准的证据**：原 handler 仍返回 `ManualFallback`。修复后，该输入作为无效 proposal 归为 `MalformedProposal` 并继续人工兜底。
- evidence 校验拒绝的是完全重复引用；不同 id/source/hash 的三元组仍是不同引用。不声称验证证据真实性、来源授权或按内容语义去重。
- 官方 [Structured Outputs guide](https://developers.openai.com/api/docs/guides/structured-outputs) 的 Supported properties 经 Context7 核对仅提供数组 `minItems/maxItems` 支持证据，没有取得 `uniqueItems` 支持证明。因此未向 strict HTTP schema 新增未经确认的关键字；不把“未确认支持”写成“已实测拒绝”。本轮服务端 parser/factory 的唯一性约束比 Provider 请求 schema 更严格，前者才是权威校验；无真实模型请求。
- 原始证据目录：`/tmp/tenon-m3b0-round10-07-evidence-896afa1378aa188a/`。主进程完整读取了下列六份日志并复算 `evidence-sha256.txt` 中全部哈希，6/6 匹配；manifest SHA-256 为 `d9a46dc625a320d0c2eb477b8e0db927f970e20bfe968ce04d91197d38a370c2`。各测试/build 日志包含原始 `[command]` 与实际 `[exit]`。

```bash
# red-boundary-tests.log 与 green-boundary-tests.log 使用同一命令，分别在修复前后执行。
dotnet test /home/shiny/github/TenonAdmin/backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WfAiDecisionProposalPolicyBoundaryTests"
# final-focused-tests.log
dotnet test /home/shiny/github/TenonAdmin/backend/TenonAdmin.slnx -c Release --no-restore --filter "FullyQualifiedName~WfAiDecision|FullyQualifiedName~WfFakeAiDecision|FullyQualifiedName~WfOpenAiCompatibleAiDecision|FullyQualifiedName~WorkflowReplaceabilityTests|FullyQualifiedName~WfNodeHandlerContractTests"
# workflow-release-build.log
dotnet build /home/shiny/github/TenonAdmin/backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore
# static-checks.log 另含三文件 whitespace/placeholder 检查。
git -C /home/shiny/github/TenonAdmin diff --check
```

- `red-boundary-tests.log`：**预期 exit 1，Failed 5 / Passed 7 / Skipped 0 / Total 12**，87ms；编译成功，三条 parser case 实际 `IsValid=true`、factory 首个重复断言未抛错、重复 evidence 的 fallback 实际 `ShadowOnly` 而期望 `MalformedProposal`，不是环境失败。factory 测试在首个断言失败，后续 factory 断言在本次红测中未执行；修复后绿色运行覆盖全部断言。
- `green-boundary-tests.log`：**exit 0，Passed 12 / Failed 0 / Skipped 0 / Total 12**，97ms。
- `final-focused-tests.log`：**exit 0，Passed 215 / Failed 0 / Skipped 0 / Total 215**，37s，覆盖所有现有 AI、HTTP adapter、replaceability 和 node handler contract 聚焦回归。
- `workflow-release-build.log`：**exit 0，Workflow 项目 Release build 成功，0 warnings / 0 errors**，2.69s；不是最终完整 solution build。
- `static-checks.log` 与 `baseline-integrity.log`：均 exit 0；三份改动文件无 whitespace/placeholder 命中。作者交付时 Git 可见变更路径 149→150，相对开轮恰好只有上述三个源码/测试路径不同，台账与 protected baseline 均未被作者修改。
- 交付源码 SHA-256 经主进程复算匹配：`AiDecisionContracts.cs=d54b6ac9ed3bab3d2ba578e5de9e92d7a03a4a99eeeeb11b08ba60a0a79fb43c`；`AiDecisionProposalParser.cs=ba04f305c65da0b9c6bb6c963eb1fdea58081d96343d2d884b57599a27d7bc4a`；`WfAiDecisionProposalPolicyBoundaryTests.cs=96bfc58a053768c5a3f9931cc77d5a75ebeeb062fe24066abb4caee35ddbdfa6`。主进程自己的 `/tmp/tenon-m3b0-round10-pre-review-integrity.log` 同样为三路径差异、范围外 0、placeholder 0、`git diff --check` exit 0。

#### 验收范围与剩余子项

- 独立 `m3b0-07-verifier` 已只读核对实现、原始红/绿/聚焦/build 日志及六份哈希，正式结论为 **PASS，仅限本轮重复集合缺陷与 parser/public factory/policy/内置 handler 边界子项，0 actionable 实现 finding**。完整 M3B0-07 因下述输入映射尚未闭合，独立结论为 **不得勾选**，不是整个 Task07/M3b 的 PASS；最终保留父项未勾选并明确 07A，不通过改变归属来制造完成。
- 当前 `WfNodeProps.AiInstructions/AiInputFields` 仅具备定义/发布校验；`AiDecisionProviderRequest` 尚不含受限业务输入投影，HTTP adapter 仅发送执行身份。当前是刻意保留的安全默认，**没有业务变量外送，但指令/白名单送模功能未实装**。本台账 Round 3 曾将该映射归于 Task06/07，不能为了勾选进度悄悄改写该范围。
- 因此本轮只完成 parser/policy 的有界子项，**M3B0-07 父项保持未勾选**；紧邻插入 M3B0-07A，明确发布指令、精确顶层白名单、受限/脱敏 immutable request、输入 hash 与 fake HTTP 负向验证。先保持不支持隐式 dot-path/递归全量送模；实现与独立复核留给下一工作单元。本轮不进入该子项。
- tx2 的第二层强制 shadow-only、审计/attempt/outbox/人工任务原子性仍属于 M3B0-09/10/11；未提前修改 dispatcher、worker、数据库、API、生成 schema 或前端，不涉及 Task 8c。无运行依赖或配置变更，无提交/推送/PR。
- 所有本轮 .NET 验证均使用 `--no-restore`，覆盖已有 restore assets；没有重复 Round 9 回放或运行完整 SQLite/四数据库/contract drift。Git 完整性仅覆盖 Git 可见路径/状态/内容，不声称 ignored `bin/obj` 或测试运行时临时文件逐字节未变。
- 主进程最终核验：`python3 /tmp/tenon-m3b0-round10-integrity.py --allowed backend/src/TenonAdmin.Workflow/Abstractions/AiDecisionContracts.cs backend/src/TenonAdmin.Workflow/Providers/AiDecisionProposalParser.cs backend/tests/TenonAdmin.Tests/WfAiDecisionProposalPolicyBoundaryTests.cs --final-ledger` → exit 0，日志 `/tmp/tenon-m3b0-round10-final-integrity.log`；150 个 Git 可见变更路径相对开轮仅上述三个路径及本台账不同，其余 146 个既有路径状态/内容保留，范围外 0、placeholder 0、`git diff --check` exit 0。另核对 Round 1–10 各唯一、07/07A 均未勾选、NEXT 仍执行 07 的 07A、结论仅为子项 PASS：均通过。无新增用户 blocker，本轮到此停止。

### M3B0-07A 安全输入投影与父项验收（Round 11，2026-09-07）

#### 本轮基线与精确改动

- 开始读取根 `AGENTS.md`、`CLAUDE.md`、完整本台账及后端 repository reference，执行 `pwd`、`git status --short --branch`：目录 `/home/shiny/github/TenonAdmin`，分支 `dev...origin/dev`，`active / 10/40`，NEXT 为 07 的 07A。未启动 `/loop`、Team、Workflow 或其他持久模式。
- 本轮独立证据目录：`/tmp/tenon-m3b0-round11-th_0t2fk/`（下文日志均相对此目录）。新建 `baseline.json` 覆盖 1,677 个 Git 可见路径的 SHA-256，`status.z` 保存逐文件状态，`before/` 保存相关源码/未跟踪实现/台账原文，`diff.patch` 保存开轮 diff；未借用 Round 10 快照作为本轮基线，也不把此前实现算作本轮创作。
- CodeGraph 优先查找节点 props、context、request、handler、validator、HTTP 与相关测试，并补查服务端脱敏。`redaction-codegraph.txt` 保存既有 `SensitiveDataMasker` 的名称关键词规则；该组件只按名字、不看值，不能直接覆盖安全送模要求。
- 生产增量共五个路径：`backend/src/TenonAdmin.Workflow/Abstractions/AiDecisionContracts.cs`；`backend/src/TenonAdmin.Workflow/Providers/AiDecisionNodeHandler.cs`；同目录 `AiDecisionProviderRequestValidator.cs`、`OpenAiCompatibleAiDecisionProvider.cs`，新增 `AiDecisionSafeInputProjector.cs`。
- 测试增量共五个路径：新增 `backend/tests/TenonAdmin.Tests/WfAiDecisionSafeInputTests.cs`；同目录 `WfAiDecisionContractTests.cs`、`WfAiDecisionProposalPolicyBoundaryTests.cs`、`WfAiDecisionRegistrationTests.cs`、`WfFakeAiDecisionProviderTests.cs` 仅补有效节点配置/变量 fixture，没有删测试或放宽原有断言。源码作者为 `m3b0_07a_author`，独立核验为 `m3b0_07a_verifier`；主进程负责基线、证据核验与本台账。

#### 输入安全决定

- 顺序为：校验节点配置 → 原始 JSON 限长/解析 → 检查整个变量树的根类型、每层重复属性与结构限额 → Ordinal 精确顶层白名单选择 → 对选中子树递归脱敏、对象键 Ordinal 排序、数组保序 → 生成安全 JSON → 对 `{instructions,inputs}` 的 UTF-8 JSON 计算 `sha256:` 小写 hash。缺失键省略；大小写不折叠；点号只是键字符，不解释路径；任一失败都不调用 Provider、不回退全量变量。
- 明确限额：指令非空白且最多 2,000 个 UTF-16 字符，只允许 CR/LF 控制字符；白名单 1–32 项，每项最多 64 字符、无首尾空白/控制字符且 Ordinal 唯一；原始 JSON 最多 65,536 字符，根必须 object；根深度为 0、最大深度 8，根及所有 value 总数最多 256；属性名最多 64 字符、字符串值最多 4,096 字符；最终安全投影最多 32,768 UTF-8 字节。边界和超一有测试，不依赖只有发布校验才能安全。
- 名称脱敏沿用既有服务端约定：`password,pwd,secret,token,credential,header,authorization,apikey,api_key,cookie`，OrdinalIgnoreCase 子串匹配，命中字段值替换为 `***`。未引入对 AspNetCore 脱敏类的反向引用或新依赖。
- 新增有限、确定性的字符串值规则：包含 Bearer/Basic 认证文本、PEM marker、三段 JWT 形状、简单邮箱、7–15 位纯电话号码（允许常见分隔符），或名称关键词后接 `=`/`%3D` 时整值打码。指令、白名单名或任意层 JSON 属性名本身命中这些敏感值形状时，整次投影 fail closed，避免把敏感正文当键外送。此决定不是通用 DLP，不声称识别任意自然语言或任意编码秘密。
- 输入由新集合、`JsonElement.Clone` 和 `ReadOnlyDictionary` 冻结，新增 request 属性只允许 `internal init`；外部旧 request 构造保留空安全投影兼容。原始变量不进入 request、不为 hash 留存；validator 校验规范 JSON 与 hash 一致性。hash 覆盖校验后的原样指令及脱敏后选中输入；未选中值或同样打码后的值变化不改变 hash，指令/可见安全值变化会改变 hash。数字用 `JsonElement.WriteTo` 保留 token 表示，不额外将 `1` 与 `1.0` 作语义折叠；对象顺序与空白则经序列化规范化。
- HTTP user body 仅新增 `instructions/inputs/inputHash`，保持原有允许身份与固定 system shadow-only 约束；没有原始 `VariablesJson`、starter/business key 或密钥。没有扩大 strict proposal schema、没有新增 `uniqueItems`，也没有宣称其被真实服务拒绝。
- 无效输入及 Provider 错误只生成固定摘要/类型化人工兜底；外部取消在预取消、飞行中、迟到正常返回、foreign-token OCE、迟到普通异常路径均优先抛出原 token。所有完成结果仍为 `ManualFallback`，不写数据库/工作流状态，不提前激活 dispatcher AI handler。

#### 红绿与实际验证

下列命令的原始输出及实际退出码均已保存；测试命令没有使用真实模型/凭据。

```bash
# 初始行为红、各次 review 红/绿及最终窄绿均使用此窄过滤器。
dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~WfAiDecisionSafeInputTests
# review2-final-focused-tests.log
dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter "FullyQualifiedName~WfAiDecision|FullyQualifiedName~WfFakeAiDecision|FullyQualifiedName~WfOpenAiCompatibleAiDecision|FullyQualifiedName~WorkflowReplaceabilityTests|FullyQualifiedName~WfNodeHandlerContractTests"
# review2-final-workflow-build.log
dotnet build backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore
git diff --check
```

- `red-safe-input-tests.log` 首次仅因 20 条缺新增属性的 CS1061 编译失败，exit 1，不算行为红。随后改为可编译行为断言，`red-safe-input-behavior-tests.log` **13 failed / 1 passed / 0 skipped，exit 1**；包含非法变量仍得到 `ManualRequested`、缺少 typed snapshot/hash 及 HTTP 字段等真实断言差异。红测源码副本为 `red-safe-input-tests.cs`；最终测试恢复强类型访问。
- 首次绿尝试 `green-safe-input-tests-attempt1.log` 缺 `JsonElement` using，exit 1，属于编译错误，已修复。中间 `final-focused-tests.log`、`final-focused-tests-verified.log` 只有 build 输出，没有 Test run/统计，虽 exit 0 **不作为覆盖证据**。中间窄绿 20/20 与实际矩阵 235/235 是有效历史步骤，最终以 review2 日志为准。
- 独立复核先发现 foreign OCE token 与嵌套敏感名称外送；`review-red-safe-input-tests.log` **7 failed / 20 passed，exit 1**，实际为 token 不匹配、Provider 非空、HTTP send=1；最小修复后 `review-green-safe-input-tests.log` **27/27，exit 0**。
- 复核又发现 Provider 忽略取消后迟到抛非 OCE 被吞为人工结果；`review2-red-safe-input-tests.log` **1 failed / 27 passed，exit 1**，实际未抛期望 OCE。各异常分支在归类前检查外部 token 后，`review2-green-safe-input-tests.log` **28/28，0 skipped，exit 0**。
- 最终 `review2-final-focused-tests.log` **243/243，0 failed / 0 skipped，38s，exit 0**，实际有 Test run；覆盖新增 28 条及原 AI/Fake/HTTP fence、zero retry、timeout、late disposal、replaceability、handler contract 回归。
- 最终 `review2-final-workflow-build.log` **Workflow 项目 Release build，0 warnings / 0 errors，exit 0**。`review2-final-integrity.log` 及主进程 `leader-final-source-integrity.log`：1,677→1,679 个 Git 可见路径，仅上述十个授权文件变化，范围外路径/状态变化 0，placeholder/whitespace 0，`git diff --check` exit 0；包含未跟踪文件。主进程复算 `review2-evidence-sha256.txt` 的源码与日志 **15/15 匹配**，结果 `leader-final-manifest-check.log`。

#### 独立验收与停止边界

- 独立 verifier 冻结后复审源码与证据，并独立窄测 **28/28，exit 0**，日志 `independent-safe-input-tests.log`。正式报告 `independent-review.md`（SHA-256 `30efd82e50dd2cc761fff284f6d9592f15098f277da57c960c3dec8b8474790c`）分别给出 **M3B0-07A PASS、M3B0-07 父项 PASS**；三项有效 finding 均已先红后绿，当前无剩余 07A/07 缺口。父项验收结合 Round 10 parser/policy 已有独立证据，不重做其实现或追认本轮创作。
- 据此只勾选 07A/07，`status` 保持 `active`，本工作单元仅在收尾增加一次 round 为 11/40。没有新增用户 Blocker，没有启动持久模式、提交、推送、PR、发布或真实模型调用。
- 此 PASS 不是 M3b 总体完成：未执行完整 solution Release build、SQLite 全量、四库、contract drift 或双前端；本轮无 API/OpenAPI/生成 schema 改动，不新增前端验证。后续 tx2 第二层 shadow-only、审计/attempt/outbox/人工任务原子性仍归 09/10/11；Task 8c 不在范围。基线完整性只覆盖 Git 可见路径/状态/内容，不声称 ignored bin/obj 或运行时临时文件不变。
- 收尾 `leader-final-integrity.log`：十个源码/测试路径及本台账外的 Git 可见内容/状态变化为 0，placeholder/whitespace 与 diff-check 通过；台账 `active/11/40`、07/07A 勾选、08 未勾选、唯一 Round 11、NEXT 及独立报告 hash 均通过。首次辅助检查误按 `[exit] 0` 匹配独立日志而失败，纠正为其真实 `COMMAND_EXIT_CODE="0"` 格式后通过；未重跑或改写独立测试结果。
- NEXT：M3B0-08；本轮到此停止，未开始该项。

### M3B0-08 审计实体与 SQLite 持久化子项（Round 12，2026-09-07）

#### 开轮与范围

- 用户在 Round 11 停止后要求继续，按台账 NEXT 只推进 M3B0-08。重读根指令、台账与后端/CI reference，应用 create-entity recipe，CodeGraph 优先查现有 execution attempt/outbox、审计实体、枚举、TestDb 与持久化契约。工作目录仍为 `/home/shiny/github/TenonAdmin`，分支 `dev...origin/dev`，开轮 `active/11/40`，无未隔离源码差异；未启动持久模式。
- 新基线目录 `/tmp/tenon-m3b0-round12-61nqbgl_/`：`baseline.json` 覆盖 1,679 个 Git 可见路径内容 hash，`status.z` 记录逐文件状态，`before/` 保存相关源码/测试/CI/台账，`diff.patch` 保存开轮 diff。与 Round 11 最终快照相比差异为 0，未复用上一轮开轮基线。下文独立日志相对此目录，作者日志位于子目录 `m3b0_08_author/`。
- 仅三处产品/测试配置路径变化：新增 `backend/src/TenonAdmin.Workflow/Entities/WfAiDecision.cs`；新增 `backend/tests/TenonAdmin.Tests/WfAiDecisionPersistenceContractTests.cs`；修改 `.github/workflows/backend-ci.yml`，仅在既有 SQL Server 非 schedule 过滤器中追加该测试类。原 filter 项、其余三库全量、SQL Server nightly、multi-replica 配置均保留。

#### 实体与测试决定

- `wf_ai_decision` 沿用现有工作流审计的 `BaseEntity` 约定，唯一键 `uk_wf_ai_decision_attempt(ExecutionId, AttemptNo)`，读取索引 `idx_wf_ai_decision_instance_time(InstanceId, CreateTime)`。每个 attempt 追加独立历史；本轮没有 store/写入调用，append-only 是既有代码约定，不声称数据库禁止任意 UPDATE。后续 tx2 写入仍须在 fence CAS 成功后验证只追加。
- 复用已有带稳定枚举值的 `AiDecisionProviderResultType`、`AiDecisionRecommendation`、`AiDecisionPolicyClassification`、`AiDecisionFallbackReason`，不新增同义枚举。
- 字段保存 execution/attempt/instance 身份、安全 input hash、持久化前规范化/脱敏的 proposal/risk/evidence 引用、provider/model、prompt/schema/policy 版本、schema/policy 结果、confidence、latency/token usage、人工兜底原因和 `ShadowMode`。尚未产生的模型元数据、hash、schema/policy/proposal/usage 使用 null，不伪造空 hash 或执行事实。没有原始变量、节点指令、完整 prompt、密钥、认证头、原始响应/异常正文、evidence content 列。
- 三个 JSON 列使用 `StaticConfig.CodeFirst_BigString`；confidence 使用可空 decimal，精度 18/6；`ShadowMode` 默认 true。没有增加后续人工覆盖更新列 `HumanOverrideOutcome`，避免以未来人工动作覆写 append-only 审计；本轮不实现人工事件。
- 两条最终契约测试走 `WorkflowAppFactory/TestDb` 真实 CodeFirst：重复 identity 拒绝且旧行不变，同 execution 新 attempt/新 execution 同 attempt 可插入；全列发现、Unicode 大文本、decimal/enum/usage/shadow 回读、失败记录 null、索引字段顺序/唯一属性、禁止原始/秘密字段。9,000 字中文仅是大文本列压力 fixture，不冒称可通过 proposal parser。共享 TestDb 支持四库路由，CI 新过滤器保证 SQL Server PR 会发现此类，但不能代替实际三库运行。

#### 原始命令、红绿及独立结论

作者 `commands.txt` 从本轮实际工具调用补录 cwd、环境和精确 shell 命令；各原始输出为同名 `.log`，退出码为 `.exit`，原始日志未重写。核心命令如下：

```bash
# red-tests、red-behavior-tests、green-sqlite-tests 使用此过滤器。
dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~WfAiDecisionPersistenceContractTests
# focused-sqlite-tests
dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter "FullyQualifiedName~WfAiDecision|FullyQualifiedName~WfFakeAiDecision|FullyQualifiedName~WfOpenAiCompatibleAiDecision|FullyQualifiedName~WfNodeExecutionContractTests|FullyQualifiedName~WorkflowReplaceabilityTests"
dotnet build backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore
git diff --check
```

- `red-tests.log`：exit 1，实体尚缺失的 CS0246 混有测试缺 `System.Reflection` using 的编译错误，只记编译红。随后 `red-behavior-tests.log` **1 failed / 0 passed，exit 1**：可编译的临时类型发现断言得到 null；此红测只证明实体缺失，**不证明唯一键约束曾行为红**。最终已恢复强类型测试、移除临时反射发现测试及 `#if ENTITY_EXISTS`，无残留跳过分支。
- `green-sqlite-tests.log` **2/2，exit 0**，但有 xUnit2031 analyzer warning；修复断言写法后才运行最终矩阵，不把这次中间结果冒充零警告。
- 最终 `focused-sqlite-tests.log` **246/246，0 failed / 0 skipped，38s，exit 0**，无 analyzer warning。实际包含 AI/Fake/HTTP、execution 持久化契约和 replaceability；不声称该 filter 覆盖 `WfNodeHandlerContractTests`。`workflow-build.log` **Workflow 项目 Release build，0 warnings / 0 errors，exit 0**，不是完整 solution build。
- 主进程 `leader-pre-review-integrity.log`：1,679→1,681 个 Git 可见路径，仅上述三处变化，范围外路径/状态为 0，placeholder/whitespace 为 0，`git diff --check` exit 0；包含未跟踪文件。`leader-manifest-check.log` 复算最初源码/日志/exit manifest **15/15 匹配**。补录说明的最终清单为 `m3b0_08_author/final-sha256.txt`，SHA-256 `5f13c722c9ecf398352a7d12bb24762a644262367f99eefe1a56498846cbb3a3`。
- 作者与 verifier 的连接曾中断；恢复相同任务继续补证/审查，未把中断视为权限拒绝或验收结果，未更换工具绕过门禁、未重建源码。作者冻结后，独立 verifier 复核当前三处增量与证据，独立执行同一持久化窄命令 **SQLite 2/2、exit 0**，日志 `independent-sqlite-tests.log`/`.exit`。
- 正式 `independent-review.md`（SHA-256 `ad474961c024189407faee9bb310c169bc1875a8d3e56648a10ff8ac8a1a4f9b`）结论：**本轮实体/共享契约/CI 接线/SQLite 子项 PASS，M3B0-08 整体 PARTIAL，不可勾选**；无当前可执行代码 finding。三库未验证和 append-only 写入待后续证明均明确列出。

#### 剩余与停止

- `database-environment.log`：docker 命令不存在（exit 127），docker/podman/mysql/psql/sqlcmd 均未在 PATH；`ss -ltn` 检查 3306/5432/1433 无监听，相关 `TENON_TEST_DBTYPE/MYSQL/SQLSERVER/POSTGRESQL` 均 unset（未读取真实凭据）。当前只有 SQLite 实测，未安装整套数据库或改变宿主配置、未调用真实模型。
- **M3B0-08 保持 `[ ]`**，剩余三库实际 CodeFirst、唯一键、Unicode、nullable 测试和独立证据复核留在本项，不转移到 Task19 来制造完成。status 保持 active，本工作单元仅收尾增加一次 round 为 12/40。没有新用户授权/源码冲突阻塞，但存在明确验证环境缺口，见 Blockers。
- 没有改 handler/provider/dispatcher/tx2，未提前注册 AI 执行链，未新增 API/OpenAPI/schema 或前端变化；完整 solution build、SQLite 全量、M3b 四库最终矩阵、contract drift、条件适用双前端仍是后续整体验收。Git 完整性不覆盖 ignored bin/obj 或运行时临时文件。
- 收尾 `leader-final-integrity.log`：相对本轮新基线只有三处授权路径及本台账改变，范围外内容/状态变化 0、placeholder/whitespace 0、diff-check exit 0；最终 manifest 17/17 hash 匹配，独立报告 hash、active/12/40、08/09 未勾选、唯一 Round 12 和 NEXT 均核对通过。
- NEXT：仍为 M3B0-08，补齐 MySQL/PostgreSQL/SQL Server 实际持久化验证与独立复核。本轮已停止，未开始 M3B0-09。

### M3B0-08 Docker 四库验证与验收（Round 13，2026-09-08）

- 用户提出在当前系统安装 Docker 测试三库，按此授权完成安装及 M3B0-08 剩余实测，未开始 09、未启动持久模式。当前 Ubuntu 26.04 x86-64、systemd 正常，账号有免密 sudo，资源满足数据库容器运行条件。开轮目录 `/home/shiny/github/TenonAdmin`、分支 `dev...origin/dev`、台账 `active/12/40`，NEXT 与首个未完成项均为 08。
- 新证据目录 `/tmp/tenon-m3b0-round13-0rf9blhp/`，下文文件均相对此目录。`baseline.json`/`status.z` 覆盖 1,681 个 Git 可见路径内容与状态，`ledger-before.md` 保存开轮台账。与 Round 12 最终快照相比无差异；CodeGraph 复查 TestDb 与持久化测试。**本轮产品源码、测试、CI 零修改**，不把前轮实现计入本轮创作；唯一仓库增量为本台账。
- 实际安装命令 `sudo -n apt-get install -y docker.io`，exit 0；使用 Ubuntu 仓库 `docker.io 29.1.3-0ubuntu4.1`，同时安装其 containerd/runc 等依赖。`docker-install-verification.log` 记录 dpkg 包版本、Docker client/server 29.1.3、systemd service active、overlayfs/cgroup v2。没有修改用户组、生产数据库配置或读取真实凭据。
- 通过官方镜像 `mysql:8.0`、`postgres:16`、`mcr.microsoft.com/mssql/server:2022-latest` 启动本轮隔离容器，三份 `*-pull.log`、`*-start.log` 有精确命令/退出码；镜像 digest、容器 ID 与绑定端口记录于 `database-runtime-evidence.log`、`container-ids.json`。端口仅为 `127.0.0.1:13306`、`:15432`、`:11433`，使用新测试凭据，无真实外部数据库。SQL Server 明确为 Developer Edition，容器内存上限 4 GiB、SQL 内存 2 GiB。
- MySQL 与 PostgreSQL 实际版本分别为 **8.0.46、16.15**，见 `mysql-postgres-versions.log`；SQL Server 登录执行 `SELECT @@VERSION` 成功，为 **2022 RTM-CU26 / 16.0.4265.3**，见 `sqlserver-readiness.log`。独立 verifier 另行直接只读查询三库版本及容器端口，不仅依赖作者报告。

#### 精确执行与结果

`run-persistence.py` 每次清除四个数据库选择/连接环境变量，再显式设置所选数据库类型及本机隔离端口连接串；测试仍由既有 TestDb 自动创建/清理临时库。使用参数数组启动命令、串行运行，不并发争用 bin/obj。执行次序为 MySql → PostgreSQL → Sqlite → SqlServer；下列脚本和每份日志均保留实际命令、环境、cwd、完整输出和退出码。

```bash
python3 /tmp/tenon-m3b0-round13-0rf9blhp/run-persistence.py MySql PostgreSQL
python3 /tmp/tenon-m3b0-round13-0rf9blhp/run-persistence.py Sqlite
python3 /tmp/tenon-m3b0-round13-0rf9blhp/run-persistence.py SqlServer
# 脚本为每个数据库执行以下测试，替换对应 TRX 文件名；连接串只来自该脚本的隔离测试环境。
dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~WfAiDecisionPersistenceContractTests --logger 'trx;LogFileName=SqlServer.trx' --results-directory /tmp/tenon-m3b0-round13-0rf9blhp/TestResults
```

- `MySql-persistence.log`、`PostgreSQL-persistence.log`、`SqlServer-persistence.log`、`Sqlite-persistence.log`：**每库 2 passed / 0 failed / 0 skipped，exit 0**。实际运行真实 CodeFirst、全列检查、复合唯一冲突且不覆盖旧行、相邻 attempt/execution 身份可插入、Unicode 大文本、失败行 nullable、enum/decimal/usage/shadow 往返与字段/索引形状。
- `TestResults/{MySql,PostgreSQL,SqlServer,Sqlite}.trx` 四份均有相同两条目标测试，**每份 total=2、executed=2、passed=2、failed=0、notExecuted=0**；不是过滤器零命中、只构建未测试或模拟方言。未重新编写实现或回放历史红测。
- `evidence-sha256.txt` 记录三份冻结源码/CI、四库日志/TRX、容器证据及执行脚本；独立 verifier 复算全部匹配。`pre-review-integrity.log`：开轮至审查前 1,681 个 Git 可见路径零内容/状态差异，`git diff --check` exit 0。Round 12 三份实现/CI hash 完全保持，因此其实体/字段/索引/CI 子项验收继续有效。
- 独立 `m3b0_08_verifier` 正式报告 `independent-review.md`（SHA-256 `41b2184d99d4448aee4175b707b70a4f6115ae337cdec02ab517000d098b56ec`）：**M3B0-08 PASS，可勾选，当前无缺口或有效 finding；整体 M3b 仍 PARTIAL**。本轮不声称完成 Task19、完整 solution Release build、SQLite 全量、四库业务/运行时全矩阵、contract drift 或前端验收；后续 append-only store/tx2/fence 仍待 09/10/11。
- 独立复核结束且无 dotnet 在途后，以 `container-ids.json` 中精确 ID 再校验 `tenon.test.round=13` label，逐个 `docker stop`、`docker rm -v`，三容器及其匿名测试卷均清理成功，命令/退出码见 `container-cleanup.log`。没有广泛清理其他容器/卷。Docker 服务及下载镜像保留，后续可复用；本轮标记的容器列表已为空。
- 据此勾选 08并解除 Round 12 三库验证缺口，status 保持 active，收尾仅增加一次 round 为 **13/40**。未提交、推送、创建 PR 或调用真实模型。NEXT：M3B0-09；本轮到此停止，未开始该项。

### M3B0-09 tx2 typed hand-off、shadow-only 与 stale fence（Round 14，2026-09-08）

- 本轮开轮基线为 `/tmp/tenon-m3b0-round14-6kzRir/baseline.json`：`pwd=/home/shiny/github/TenonAdmin`、分支 `dev...origin/dev`、HEAD `17b46bc`、Git 可见路径 155 个；完整保留 Round 13 及更早工作树修改。开轮先用 CodeGraph 复核 `WfNodeExecutionDispatcher`、`WorkflowEngine.BeginNodeExecutionCompletedAsync`、`WfNodeExecutionResult`、`AiDecisionOutcome` 和调用关系。
- 修改文件仅为 [`backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs`](../backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs) 与 [`backend/tests/TenonAdmin.Tests/WfNodeExecutionDispatcherTests.cs`](../backend/tests/TenonAdmin.Tests/WfNodeExecutionDispatcherTests.cs)。tx2 新增可覆写的 `NormalizeExecutionResult`：AI 节点只有带 `AiDecisionOutcome` 的 `ManualFallback` 原样通过；替换 handler 返回 `Succeeded`、重试、失败或普通回退时，统一丢弃 `OutputJson`/不可信摘要，生成固定 `ProviderFailure` typed fallback。归一化后的同一结果同时交给 `ResolveExecutionOutcome` 与 `WfNodeExecutionAttemptStore.AppendAsync`，避免 execution 状态与 attempt 类型分裂；CAS 仍是首个写操作，未新增 handler 旁路写库、dispatcher、worker、审计写入或第二执行链。
- 新增三条回归：替换 AI handler 返回含敏感正文的 `Succeeded` 时 tx2 必须人工兜底且 token/instance 不推进；真实 `AiDecisionNodeHandler + FakeAiDecisionProvider + parser + policy` 的 proposal/policy 结果必须以 `AiDecisionOutcome` 进入 `NodeExecutionCompletedCmd`；旧 fence 携带真实 typed proposal 时必须得到 48004，execution 仍属新 owner，attempt、`wf_ai_decision`、outbox、task、actor、history 均无新增。AI 测试模型使用合法 `user` assignee，stale 用例前后保存并比较 task/actor/history 计数。

#### 红/绿验证

```bash
dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WfNodeExecutionDispatcherTests.AiDecision' --logger 'console;verbosity=normal'
dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WfNodeExecutionDispatcherTests.Stale_ai_typed_result' --logger 'console;verbosity=minimal'
dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter 'FullyQualifiedName~WfNodeExecutionDispatcherTests' --logger 'console;verbosity=minimal'
dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter 'FullyQualifiedName~WfAiDecision|FullyQualifiedName~WfFakeAiDecision|FullyQualifiedName~WfOpenAiCompatibleAiDecision|FullyQualifiedName~WorkflowReplaceabilityTests|FullyQualifiedName~WfNodeHandlerContractTests' --logger 'console;verbosity=minimal'
dotnet build backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore
git diff --check
```

- 红测日志 `/tmp/tenon-m3b0-round14-ai-red.log`：实现 guard 前 **1 failed / 1 passed，exit 1**，失败为 `Expected ManualFallback / Actual Succeeded`，证明回归能检出缺口（SHA-256 `4899e2eda46ac34e96ecc7309d4f75728c80a9c65dcd5cfef4a0a739f05d8473`）。
- 绿测：`/tmp/tenon-m3b0-round14-ai-final2.log` **2/2，exit 0**（SHA-256 `01929af7995662697742356b9488f61c9ad9279c1aaf154820b8d2b762466812`）；`/tmp/tenon-m3b0-round14-stale-typed-final3.log` **1/1，exit 0**（SHA-256 `326f84ba1d1a75d4af3a35c4f060484fa8947cdc7b3a74c266680599d52a268a`）。
- 最终影响范围矩阵 `/tmp/tenon-m3b0-round14-dispatcher-final2.log` **30/30，0 failed / 0 skipped，exit 0**（SHA-256 `fb3653fbcc8947b03a7e03109a6087e1444eb8b1b72e263c982e7d2a36789965`）；规定聚焦矩阵 `/tmp/tenon-m3b0-round14-focused-final2.log` **245/245，0 failed / 0 skipped，exit 0**（SHA-256 `ba36c9301c79c1dd8aff0bb4865d5aec982caf3d74868958779ae75c47bbfdc2`）。
- `backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj` Release build `/tmp/tenon-m3b0-round14-workflow-build.log` **0 warnings / 0 errors，exit 0**（SHA-256 `59e239f2b9e54ae863d2235c1a22bddb5b69b82428a3969b1c4b4fca5d7534dd`）；`git diff --check` `/tmp/tenon-m3b0-round14-diff-check-final.log` **exit 0**（SHA-256 `1ca4a13146b2891c0e462123ac0cb848f92e6221f18b870a1b78c232d1956c35`）。
- `/tmp/tenon-m3b0-round14-integrity-final.log` 覆盖未跟踪文件：相对本轮新基线 157 个 Git 可见路径中只有上述两个授权源码/测试文件变化，意外变化 0，`integrity_pass=True`（SHA-256 `0aa2fc63436448844388a9f148c4bc9baa63d447f457a689e13fb02a4fbf2f24`）；授权文件 placeholder 扫描无命中。
- 独立 verifier 报告 `/tmp/tenon-m3b0-round14-independent-review.md`（SHA-256 `9881e0116da390dee951f2efa266befca734ebd26448c54dc5aec8019eedb205`）结论 **PASS**、无有效 finding：确认 typed hand-off、shadow-only 归一化、CAS 首写、真实 typed stale 下 attempt/AI audit/outbox/task/actor/history 零副作用和非 AI 兼容性均成立。报告同时注明 AI audit 成功写入属于 M3B0-11，本轮不提前接入。
- 本轮未修改 AI 审计写入、EnterNodeOp/scheduler 接线、API/OpenAPI、前端、四库验证或 Task 8c；完整 solution/SQLite 全量、四库业务运行时矩阵、contract drift、双前端与后续 M3B0-10/11 等仍按任务顺序执行。未提交、推送、创建 PR 或调用真实模型。

### M3B0-10 AiDecision 接入既有 execution/scheduler/dispatcher/handler 单链（Round 15，2026-09-08）

- 本轮开轮基线为 `/tmp/tenon-m3b0-round15-CEwRNq/baseline.json`：`pwd=/home/shiny/github/TenonAdmin`、分支 `dev...origin/dev`、HEAD `17b46bc04a8d21fa30abd08835fbef555373c3cd`、Git 可见路径 1,683 个；覆盖全部既有未跟踪文件。台账写入前源码/测试收尾完整性日志 `/tmp/tenon-m3b0-round15-integrity.log` 显示 6 个授权路径、`unexpected=[]`、`integrity_pass=True`（SHA-256 `b22a917deb9373c6ff341e2b9fefbff9bc689b669b0f101c3273dd6de5b6d827`）。
- 生产实现仅增量修改 [`EnterNodeOp.cs`](../backend/src/TenonAdmin.Workflow/Engine/Operations/EnterNodeOp.cs)、[`WorkflowSetup.cs`](../backend/src/TenonAdmin.Workflow/WorkflowSetup.cs) 与 [`AiDecisionNodeHandler.cs`](../backend/src/TenonAdmin.Workflow/Providers/AiDecisionNodeHandler.cs)：`EnterNodeOp` 为 `AiDecision` 增加独立 virtual 入口，并与保留的 `EnterWebhookAsync` 共用 `WfNodeExecutionStore.EnsureAsync` 的可靠占位逻辑；`WorkflowSetup` 用 `TryAddEnumerable` 将 `AiDecisionNodeHandler` 加入既有 `IWorkflowNodeHandler` 集合，未新增 worker、job、dispatcher 或状态机。现有 `WfNodeExecutionJob → WfNodeExecutionDispatcher → FirstOrDefault(NodeType)` 路径保持不变，消费者前置同类型 handler 仍优先。
- 测试增量修改 [`WfNodeExecutionEntryTests.cs`](../backend/tests/TenonAdmin.Tests/WfNodeExecutionEntryTests.cs)、[`WfNodeExecutionWorkerTests.cs`](../backend/tests/TenonAdmin.Tests/WfNodeExecutionWorkerTests.cs)、[`WfAiDecisionRegistrationTests.cs`](../backend/tests/TenonAdmin.Tests/WfAiDecisionRegistrationTests.cs)：覆盖 AI 进入流程只建 `Pending` execution 且入口不调 handler、worker/dispatcher 调用内置 AI handler、seeded scheduler 触发同一 worker、消费者同类型 handler 实际优先调用，以及重复注册不产生重复 AI descriptor。

#### 红/绿验证

- 红测命令、原始输出和退出码记录在 `/tmp/tenon-m3b0-round15-commands.txt`；实现前 **4/4 失败、exit 1**，分别检出 AI 节点入口不支持、AI handler 未注册及 DI descriptor/优先级缺口。原始输出 `/tmp/tenon-m3b0-round15-red.log`（SHA-256 `065e3625b00f48b420e0e4241e69bdf29527db22d80837222d47b83b40dc048d`）。
- AI 接线窄绿 **6/6、0 skipped、exit 0**：`/tmp/tenon-m3b0-round15-green-ai-chain.log`（SHA-256 `d78b297a6fd3759b8fa32c8d82aba9ae54f84d6cbd85403cd07697754dcf4245`）。
- 影响范围聚焦矩阵命令同 `/tmp/tenon-m3b0-round15-commands.txt`：`WfAiDecision|WfFakeAiDecision|WfOpenAiCompatibleAiDecision|WorkflowReplaceabilityTests|WfNodeHandlerContractTests|WfNodeExecutionEntryTests|WfNodeExecutionWorkerTests|WfNodeExecutionDispatcherTests`，**291/291、0 skipped、exit 0**；日志 `/tmp/tenon-m3b0-round15-focused.log`（SHA-256 `fe374416308b1a5aa6eb61d164157c4f8424f9845e67dccbb22779c0e916b21d`）。
- Workflow 项目 Release build **0 warnings / 0 errors、exit 0**：`/tmp/tenon-m3b0-round15-workflow-build.log`（SHA-256 `10c3a7cb16e62b9b5ad7971653026f02fb6c40f064bed222bcc199e9c0b9c485`）；台账写入后的最终 `git diff --check` **exit 0**：`/tmp/tenon-m3b0-round15-diff-check-final.log`（SHA-256 `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`）。最终完整性日志 `/tmp/tenon-m3b0-round15-integrity-final.log` 覆盖未跟踪文件，`changed_paths=7`（含本台账）、`unexpected=[]`、`integrity_pass=True`（SHA-256 `a053a5427e20b50e11e3a97783183533f1cb8ece99fed6e433c889646ed2077b`）。授权路径 placeholder/空实现扫描无命中。
- 独立 verifier `/root/m3b0_10_verifier` 在作者完成后重新核对源码、测试与证据，独立精确 6/6、`git diff --check` 均 exit 0，结论 **M3B0-10 PASS、0 个有效 finding，可勾选**；确认入口 Pending、事务外 handler、scheduler/worker/dispatcher 单链和消费者优先。其限制为完整 solution、SQLite 全量、四库、contract drift、前端仍属后续任务，本轮未声称通过。
- 本轮未修改 API/OpenAPI、前端、AI 审计/attempt/outbox/人工任务 tx2、Task 8c 或 `docs/workflow/**`；未提交、推送、创建 PR/tag，未调用真实模型。完成本工作单元后停止。

### M3B0-11 tx2 原子写入 AI 审计与人工兜底（Round 16，2026-09-08）

- 本轮开轮基线为 `/tmp/tenon-m3b0-round16-1788838720/baseline.json`：`pwd=/home/shiny/github/TenonAdmin`、分支 `dev...origin/dev`、HEAD `17b46bc04a8d21fa30abd08835fbef555373c3cd`；覆盖全部 Git 可见路径及既有未跟踪内容。实现前红测日志保存于 `/tmp/tenon-m3b0-round16-red2.log` 与 `/tmp/tenon-m3b0-round16-red3.log`，分别为 1/1 失败（审计集合为空）和 1/1 失败（第二次 claim 不可用），退出码均为 1；其余既有修改未回滚、stash、clean 或覆盖。
- 生产增量仅修改 [`AiDecisionContracts.cs`](../backend/src/TenonAdmin.Workflow/Abstractions/AiDecisionContracts.cs)、[`AiDecisionNodeHandler.cs`](../backend/src/TenonAdmin.Workflow/Providers/AiDecisionNodeHandler.cs) 与 [`WorkflowEngine.cs`](../backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs)：typed outcome 携带封闭 `ProviderResultType` 与安全 `InputHash`；handler 在事务外保留安全请求摘要并对 proposal/timeout/failure 分类；`BeginNodeExecutionCompletedAsync` 在同一 `UseTranAsync` 中按 CAS → generic attempt → AI audit → terminal `Pending` outbox → `WfManualFallbackOp/CreateTaskAsync` 顺序执行。审计仅由已校验 proposal 重建规范 JSON、policy/fallback、输入 hash、延迟与 shadow 标志，不保存变量、完整 prompt、原始 Provider response 或异常正文；审计写失败和后续任务失败都会回滚整笔 tx2。
- 测试增量仅修改 [`WfNodeExecutionDispatcherTests.cs`](../backend/tests/TenonAdmin.Tests/WfNodeExecutionDispatcherTests.cs)：覆盖成功审计字段与 attempt 对齐、规范 proposal/敏感值不泄漏、双 attempt append-only、审计步骤失败回滚 CAS/attempt/outbox/task、无办理人时 ManualFallback/token 原地且不建任务；原有 typed hand-off 断言同步改为要求审计行。没有新增执行链、dispatcher/worker、API/OpenAPI、前端或 Task 8c。

#### 红/绿验证

- 红测：`dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AiDecision_completion_writes_typed_audit_with_attempt_outbox_and_manual_task' --logger 'console;verbosity=minimal'` → **1 failed / 0 passed，exit 1**，原始日志 `/tmp/tenon-m3b0-round16-red2.log`（SHA-256 `59207f074e1a90cd93c44904469872d9640fd0e474633fa80ebc28cc6694f3d4`）；`dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AiDecision_audit_is_append_only_across_two_claimed_attempts' --logger 'console;verbosity=minimal'` → **1 failed / 0 passed，exit 1**，日志 `/tmp/tenon-m3b0-round16-red3.log`（SHA-256 `9313c6e7ababf14e5465fd2dc4e31028ef2d624a3881cebabdb209e3a090b9ca`）。第二条红测最初因测试 fixture 未把终态重新置为 Pending，随后修正测试 fixture 后再进入绿测，不改变生产缺口结论。
- AI 专项绿测：`dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WfNodeExecutionDispatcherTests.AiDecision' --logger 'console;verbosity=minimal'` → **6/6，0 skipped，exit 0**，日志 `/tmp/tenon-m3b0-round16-green-ai-audit4.log`（SHA-256 `ccc56c55be9c0f019f30568f1e7faa5437591a2a565834d8456c3f0b1d704ef9`）。
- 影响范围矩阵：`dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter 'FullyQualifiedName~WfAiDecision|FullyQualifiedName~WfFakeAiDecision|FullyQualifiedName~WfOpenAiCompatibleAiDecision|FullyQualifiedName~WorkflowReplaceabilityTests|FullyQualifiedName~WfNodeHandlerContractTests|FullyQualifiedName~WfNodeExecutionEntryTests|FullyQualifiedName~WfNodeExecutionWorkerTests|FullyQualifiedName~WfNodeExecutionDispatcherTests' --logger 'console;verbosity=minimal'` → **295/295，0 skipped，exit 0**，日志 `/tmp/tenon-m3b0-round16-focused.log`（SHA-256 `db0296cc16681fe080d821d94c619d001847ec7e567a5cc935d76c3882360ca7`）。
- Workflow Release build：`dotnet build backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore` → **0 warnings / 0 errors，exit 0**，日志 `/tmp/tenon-m3b0-round16-workflow-build.log`（SHA-256 `22879b27b19da05a3f8712cceb69d1fd4b2330cb1cf822302e9893f118e30f64`）；`git diff --check` → **exit 0**，日志 `/tmp/tenon-m3b0-round16-diff-check.log`（SHA-256 `49f6be09cd9bde2d8e27987df4cacf91eeb32fe633985e9ecd8092a91d784f24`）。
- 独立 verifier `/root/m3b0_11_verifier` 在作者完成后只读复核源码并独立运行 `WfNodeExecutionDispatcherTests` **34/34，exit 0**（含 7 个 AI 专项回归），独立 `git diff --check` **exit 0**；报告 `/tmp/tenon-m3b0-round16-independent-review.md`（SHA-256 `046f71ae93509b4516e99e1165363205f3146742642c8c19970444cd33ed8220`），测试日志 `/tmp/tenon-m3b0-round16-independent-dispatcher.log`（SHA-256 `f78f44de3edb132f38d28eedf80f688e4ad1130606aa352077b4b951515029ee`），diff 日志 `/tmp/tenon-m3b0-round16-independent-diff-check.log`（SHA-256 `b179726a7523f861c07d4446f269af7418aee845e35c90c82dbd56c45c8d0227`）。结论 **PASS、无阻断 finding**；独立验证仅覆盖默认 SQLite/dispatcher 34 项，四库和完整 solution/SQLite 全量留给 M3B0-19/20。
- 收尾完整性检查 `/tmp/tenon-m3b0-round16-integrity-final.log`（SHA-256 `0f46af18cf9f25988103dfb79e4901ca58621fbea77a08dc11f6c6a1165dce8d`）相对本轮基线确认只有授权的四个源码/测试路径改变（台账另计），范围外 0、placeholder/空实现 0、`git diff --check` 0；保留所有前序工作树修改。M3B0-11 已勾选，`status: active`、`round: 16/40`，NEXT：M3B0-12（定义→启动实例→worker→审计→人工待办端到端 Fake Provider 测试）。本轮未提交、推送、创建 PR/tag，未调用真实模型；到此停止。

### M3B0-12 端到端 Fake Provider 验收（Round 17，2026-09-08）

- 本轮开始读取根 `AGENTS.md`、`CLAUDE.md`、完整台账和后端 repository reference，执行 `pwd`、`git status --short --branch`；新建基线 `/tmp/tenon-m3b0-round17-Z9wboT/paths.sha256`，覆盖 1,683 个 Git 可见路径（含未跟踪文件；缺失的既有删除路径以 `MISSING` 记录）。保留所有前序修改，未回滚、清理、stash 或覆盖任何既有实现。
- 先用 CodeGraph 追踪定义发布、实例启动、`wf-node-execution-scan` 种子任务、scheduler/worker/dispatcher、Fake Provider、tx2 和 `WfManualFallbackOp` 调用链。基于 M3B0-10/11 已有实现，仅增量修改 [`WfNodeExecutionProductionE2ETests.cs`](../backend/tests/TenonAdmin.Tests/WfNodeExecutionProductionE2ETests.cs)，没有新增生产执行链、Provider、worker、dispatcher、数据库写入逻辑、API/OpenAPI、前端或 Task 8c。
- 新增测试真实调用 `IWfDefinitionService.AddAsync`/`PublishAsync` 和 `IWfInstanceService.StartAsync`，用前置注入的 `FakeAiDecisionProvider` 生成合法 proposal；确认入口先落 `Pending` AI execution，再将 seeded `wf-node-execution-scan` 设为到期并调用现有 `JobSchedulerService.TickAsync`。通过成功的 `SysJobLog` 与 `fake.CallCount == 1` 证明执行经过既有 scheduler→worker→dispatcher 单链。
- 测试验证 tx2 结果为 `ManualFallback`，`WfNodeExecutionAttempt` 与 `WfAiDecision` 一致，Provider result 为 proposal、schema 有效、policy 为 `ShadowCandidate`、fallback 为 `ShadowOnly`、`ShadowMode=true`；验证唯一 `Pending` outbox、同节点人工 `WfTask`、`Pending` actor、`TaskCreated` history，并验证 token 仍 `Active`、instance 仍 `Running`，固定全过程 shadow-only。
- 窄测命令 `dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WfNodeExecutionProductionE2ETests.A_published_ai_decision_runs_through_scheduler_worker_and_creates_shadow_audit_and_manual_todo' --logger 'console;verbosity=normal'`：**1/1 passed，0 failed，0 skipped，exit 0**。首次补测即在当前 M3B0-11 实现上通过，因此没有伪造回滚来制造红测，也没有需要新增生产修复的缺口。
- 影响范围矩阵命令为 `dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter 'FullyQualifiedName~WfNodeExecutionProductionE2ETests|FullyQualifiedName~WfAiDecision|FullyQualifiedName~WfFakeAiDecision|FullyQualifiedName~WfOpenAiCompatibleAiDecision|FullyQualifiedName~WorkflowReplaceabilityTests|FullyQualifiedName~WfNodeHandlerContractTests|FullyQualifiedName~WfNodeExecutionEntryTests|FullyQualifiedName~WfNodeExecutionWorkerTests|FullyQualifiedName~WfNodeExecutionDispatcherTests' --logger 'console;verbosity=minimal'`：**302/302 passed，0 failed，0 skipped，exit 0**。
- `dotnet build backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore`：**0 warnings / 0 errors，exit 0**；`git diff --check`：**exit 0**；新增测试文件 placeholder 扫描无 `TODO/FIXME/HACK/NotImplementedException/test.skip/.only/Skip` 命中。
- 独立 verifier 在作者完成后只读复核并运行窄测，首次提出“应固定成功 policy 分类”的低风险建议；作者补充 `ShadowCandidate` 与 `ShadowOnly` 断言后，verifier 再次复核并给出 **PASS、无 actionable finding**。最终完整性比较相对本轮新基线仅 `WfNodeExecutionProductionE2ETests.cs` 内容改变，最终 manifest SHA-256 为 `97e765e4796b5b150e80d647c49d4f1715b5b5beb3761f35d2de705aae3ba935`；前序路径状态/内容保持不变。
- 本轮未运行真实凭据或模型服务，未修改前端/schema，未实现 M3B0-13 负向矩阵或后续任务；未提交、推送、创建 PR/tag，未启动 `/loop`、Team 或其他持久模式。本轮到此停止。

### M3B0-13 负向矩阵验收（Round 18，2026-09-08）

- 本轮开轮基线 `/tmp/tenon-m3b0-round18-Nl0ksl/paths.sha256` 覆盖 1,683 个 Git 可见路径（含未跟踪文件，既有删除路径按 `MISSING` 记录）；保留全部前序修改。实现仅增量修改 [`WfNodeExecutionProductionE2ETests.cs`](../backend/tests/TenonAdmin.Tests/WfNodeExecutionProductionE2ETests.cs)，台账另计；未修改生产代码、API/OpenAPI、前端/schema 或执行链。
- 新增 6 个 Fake Provider Theory 情景覆盖 malformed proposal、low confidence、high risk、Provider timeout、typed failure 和 Provider exception；每个真实走定义发布→实例启动→worker→dispatcher→tx2，断言 `ManualFallback`、provider/schema/policy/fallback audit、`Pending` outbox、人工 task/actor/history，以及 token `Active`、instance `Running`。
- 新增 external cancellation、crash-before-tx2 reclaim、duplicate worker scan、stale-fence 四个真实负向用例。取消只保留 claim，attempt/AI audit/outbox/task/actor/history 均无新增；崩溃在 tx2 前不留副作用，lease 过期后新 owner 以 `AttemptNo=2` 原子写入一次；重复扫描不重复调用 Provider 或追加副作用；旧 fence 返回 48004 且新 owner/fence 与所有副作用保持不变。
- 窄测：`dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WfNodeExecutionProductionE2ETests.AiDecision_provider_negative_scenarios_are_audited_and_stay_manual|FullyQualifiedName~WfNodeExecutionProductionE2ETests.AiDecision_external_cancellation_leaves_only_the_claim_and_no_side_effects|FullyQualifiedName~WfNodeExecutionProductionE2ETests.AiDecision_crash_before_tx2_is_reclaimed_once_with_append_only_history|FullyQualifiedName~WfNodeExecutionProductionE2ETests.AiDecision_duplicate_worker_scan_does_not_append_any_side_effect|FullyQualifiedName~WfNodeExecutionProductionE2ETests.AiDecision_stale_owner_cannot_write_attempt_audit_outbox_or_task' --logger 'console;verbosity=minimal'` → **10/10 passed，0 failed，0 skipped，exit 0**；日志 `/tmp/tenon-m3b0-round18-narrow.log`（SHA-256 `3bcaad43a30f4ec025c9a305cb569be16892af27c0749513dc839a45afec7ced`）。
- 影响范围矩阵沿用 M3b AI/执行链目标过滤器 → **312/312 passed，0 failed，0 skipped，exit 0**；日志 `/tmp/tenon-m3b0-round18-focused.log`（SHA-256 `489ef48eed4a205e20ee7b89712eaae68b72421cefdc85b5807caf122efa84d6`）。Workflow Release build → **0 warnings / 0 errors，exit 0**；日志 `/tmp/tenon-m3b0-round18-workflow-build.log`（SHA-256 `44bf09dd4cf0aaac20461a8e396182748a0e2105869e4d7014a17218d239a2af`）。`git diff --check` → **exit 0**；日志 `/tmp/tenon-m3b0-round18-diff-check.log`（SHA-256 `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`）。新增测试文件精确 placeholder 扫描 → pass。
- 独立 verifier `01a07f8e-d372-7470-9343-6cbdfe686423` 在作者完成后只读复核并独立运行本轮测试 **10/10，exit 0**，独立 `git diff --check` **exit 0**；结论 **PASS、无 actionable finding**。复核确认默认 SQLite 范围，四数据库与全量 solution 仍留给后续统一验证。
- 最终基线对比仅发现本轮授权的测试文件和本台账内容变化；最终路径清单 `/tmp/tenon-m3b0-round18-Nl0ksl/paths-final.sha256`（SHA-256 `28e9ccbffbc476144901bce91e33645e307faf4573ef99e1997526736bb34f28`）。本轮未运行真实凭据或模型服务，未提交、推送、创建 PR/tag，到此停止。

## Blockers

- **已解除（Round 13，2026-09-08）：Round 12 的 M3B0-08 三库验证环境缺口。** 用户授权安装 Docker 后，本机隔离 MySQL/PostgreSQL/SQL Server 与 SQLite 的同一持久化契约均各 2/2，独立 verifier 验收 08 PASS。Docker 保留，本轮临时容器已精确清理；历史 Round 12 的环境不足与 PARTIAL 结论仍如实保留，不改写成当时已通过。

- **已解除（2026-09-07）：Round 8 的迟到源码差异与当前验证缺口。** 只读检查确认两份源码与已保存的收尾哈希完全一致，仍在用户已授权的 Task06 范围，全部增量保留；Round 9 已完成修复前缺陷检出回放、当前绿测和独立验收。历史归属及 Round 8 的 TDD 顺序不追认，不再把已闭合的当前验收证据或可由代理处理的技术工作作为用户阻塞。

- **已解除认证阻塞（2026-09-07）：Semgrep Guardian。** 用户自行完成正常 OAuth 登录并重载插件；原生 `whoami` 已成功返回 OAuth 身份与部署信息。此次台账 Edit 正常成功，但不将此等同于 C# 校验或功能测试通过；后续若再次发生工具拒绝，仍须停止该被拒绝操作，不得禁用 hook、修改权限或换工具/代理绕过。
  - Round 7 的 Task06 增量接续授权继续有效，范围不扩大。
  - “并发覆盖”是已撤回的初步推测，没有证据要求用户停止其他会话或归责其他写入者。

- **已解除（2026-09-07）：Round 7 的 M3B0-06 既有未记账改动接续权限。** 本轮首次 `git status --short --branch` 已出现 `Schema/WfNode.cs`、`Schema/WfSchemaEnums.cs`、`Services/WfDefinitionService.cs` 的节点接入 diff，以及未跟踪的 `WfAiDecisionNodeDefinitionTests.cs`；`AiDecisionNodeHandler` 和三份 AI fixture 也已带有 Task06 改动，但 Round 1–6 没有记录这部分工作。本轮不推断这些改动的作者或归属。独立核验发现 AI 发布校验允许 `WebhookUrl`/`WebhookHeaders`/`Nobody` 等共享字段通过，修复必须接续当前已修改的发布校验方法，无法只在隔离新文件中完成，故按 Protected baseline 规则停止，未修改实现或测试。
  - 用户处理结果：已通过结构化确认明确允许基于这些既有 M3B0-06 diff **增量补测试并修复**，保留全部原有修改；权限范围已记录于 Protected baseline。授权不涉及 `.gitignore`、`AGENTS.md`、`.codex/**`、旧 `.loop`、技能文件或 `docs/workflow/**`。
  - 恢复后先保持 M3B0-06 为第一个未完成项，处理其剩余子任务 M3B0-06A；完成聚焦测试与独立复审前不得勾选 M3B0-06。
- 旧 `.NET 10 SDK` blocker 已于 2026-09-04 解除；本轮构建与测试可运行，不需要真实模型密钥、外网 Provider 或新增权限配置。

## Round log

### Round 1 — M3B0-01 权威文档与现有 execution/scheduler/dispatcher/tx2/人工任务/outbox 调用链基线 → CodeGraph 与源码研究完成，精确锚点、风险、shadow-only 冲突裁决及实施决策已记录；首次聚焦测试因 `dotnet` 不存在（exit 127）未执行，任务当时保持未勾选并设为 blocked-user。NEXT: 用户恢复 .NET 10 SDK 后重跑 M3B0-01 聚焦测试；通过后勾选 M3B0-01，再进入 M3B0-02。

#### Round 1 后补证 — 用户要求安装 .NET 10 → SDK 10.0.400 已按官方脚本安装并通过 `dotnet --info`；原聚焦测试 56/56 通过，M3B0-01 已勾选、blocker 清除、status 恢复 active。NEXT: M3B0-02 先补 Provider、proposal、policy 的失败契约测试。

### Round 2 — M3B0-02 Provider/proposal/policy/shadow-only/取消/超时/错误分类红测 → 新增强类型纯契约测试，主上下文确认内部 `dotnet test` 仅以 20 条预期 `CS0246` 失败；两轮独立审查的 11 项有效发现均修正，最终 reviewer PASS。为避免红测跨多轮阻断整个测试项目，新增紧邻修复任务 M3B0-02A。NEXT: M3B0-02A 实现最小纯契约纵切并恢复测试项目可编译。

### Round 3 — M3B0-02A 最小纯契约纵切 → Provider/request/result、strict proposal parser、deterministic policy、typed outcome 与 standalone shadow-only handler 已实现；83 个聚焦测试及 Workflow Release build 通过，独立 reviewer 的 cancellation/source-compatibility 两项发现修复后复审 PASS。NEXT: M3B0-03 实现 options、TryAdd DI 与 replaceability 测试。

### Round 4 — M3B0-03 AI options、TryAdd DI 与 replaceability → policy-only 配置、fail-closed 默认 Provider、interface-backed parser/policy、scoped handler graph 与十三件套替换契约已完成；93 个聚焦测试和 Workflow Release build 通过，独立 reviewer 的 3 项 seam/validation/factory-test 发现修复后复审 PASS。NEXT: M3B0-04 实现确定性 Fake Provider 及结果矩阵。

### Round 5 — M3B0-04 确定性 Fake Provider → 七种稳定场景、共享 request validator、字节稳定 proposal、取消/计数/错误/handler 矩阵已完成；119 个聚焦测试与 Workflow Release build 通过，独立 reviewer PASS。NEXT: M3B0-05 确认 OpenAI-compatible v0 协议并实现 HTTP Adapter。

### Round 6 — M3B0-05 OpenAI-compatible 协议与 HTTP Adapter → 官方 Chat Completions + strict json_schema 合约、配置/DI、受限流式响应、取消/timeout、Jobs HTTP fence 与 0 retry 已完成；161 个聚焦测试和 Workflow Release build 通过，独立 reviewer 的 4 项安全/资源/协议发现修复后复审 PASS。NEXT: M3B0-06 增加 AiDecision 节点类型、最小 props 与发布校验。

### Round 7 — M3B0-06 既有未记账改动的只读验收 → 126 个现有聚焦测试与 Workflow Release build 通过，但独立 verifier FAIL；临时运行探针确认 AI 发布校验允许并序列化 Webhook endpoint/认证头及 Nobody 配置。未修改实现或测试，M3B0-06 保持未勾选，插入剩余修复项 M3B0-06A；因修复必须接续来源未确认的既有 diff，status 曾设为 blocked-user。NEXT: M3B0-06，先处理其 M3B0-06A 发布红测、AI 专属 props 修复与旧 v1 真实发布回归。

#### Round 7 后授权 — 用户选择“允许增量修复” → 已把 Task06 的具体接续范围及原基线仍受保护的约束写入 Protected baseline，接续权限 Blocker 解除，status 恢复 active；本轮不追加第二个代码工作单元。NEXT: M3B0-06（执行其 M3B0-06A 剩余修复并复审）。

#### Round 7 后补证 — 独立 verifier 已返回精确 test/build/探针/diff-check 命令，均与已有 exit 0 日志对应，已填入 Evidence；报告再次确认未运行实际数据库发布或红测。新增的 AI MaxAttempts 上下界测试缺口并入 M3B0-06A。本次仅补齐 Round 7 证据，不增加 round，不进入第二工作单元。NEXT: M3B0-06（先处理 M3B0-06A）。

### Round 8 — M3B0-06A 真实发布红测尝试 → Edit 报告 Guardian 未认证，主进程 whoami 复核同样失败；已读取的运行日志只有旧 6 测试，不能证明新增用例红测。收尾发现测试和发布服务两份迟到增量，已纠正“源码保持基线/没有实现增量”的旧结论并记录哈希、差异与失败检查；不推断写入来源，不回滚，不宣称修复通过。作者任务已停止，M3B0-06/06A 保持未勾选，status 为 blocked-user；本次补证不增加 round，停止自动循环。NEXT: 正常 Guardian 认证/授权编辑能力恢复并确认两份现有增量可安全接续后，继续首个未完成项 M3B0-06，先补齐 M3B0-06A 的红测证据、绿测及独立复审。

### Round 9 — M3B0-06A 保留增量的缺陷检出回放与验收 → Guardian OAuth 恢复、原差异稳定且在既有授权范围；主目录源码零改动。隔离修复前服务的真实发布矩阵 17/17 按预期以 48002/0 断言差异失败，当前聚焦测试 149/149 通过、Workflow Release build 通过、placeholder/diff/integrity 通过；独立 verifier 对 M3B0-06/06A 给出 PASS。只勾选这两个关联任务，不追认旧历史 TDD，不进入后续工作单元，status 保持 active、round 为 9/40。NEXT: M3B0-07 基于现有 proposal parser/schema 与服务端 policy evaluator 补齐剩余实现和边界覆盖，继续强制所有完成结果转人工兜底。

### Round 10 — M3B0-07 parser/policy 重复项与阈值边界子项 → 红测实际 5 失败/7 通过，最小修复 parser 与公开 factory 后窄测 12/12、聚焦矩阵 215/215、Workflow Release build 及 placeholder/diff/integrity 均通过；独立 verifier 对本轮 parser/policy/内置 handler 边界子项给出 PASS、0 actionable finding，但完整 Task07 因输入映射缺口尚不通过。只修改两个生产文件和新增一个测试文件，protected baseline 保留。安全指令/白名单送模尚未实现，M3B0-07 保持未勾选，新增剩余 M3B0-07A；status 为 active、round 为 10/40，本轮不进入剩余子项。NEXT: M3B0-07，先执行其 M3B0-07A 安全送模映射与负向测试，再独立验收父项。

### Round 11 — M3B0-07A 安全送模与父项验收 → 实现受限指令、Ordinal 顶层投影、有限脱敏、immutable request/hash 和受限 HTTP body；行为红 13失败/1通过，独立复核三项缺陷分别以7失败/20通过和1失败/27通过检出并修复。最终窄绿28/28、聚焦243/243、Workflow Release build 0 warnings/errors、基线/placeholder/diff/hash均通过；独立窄绿28/28，verifier分别给07A与07 PASS。只勾选这两个关联项，active、11/40，无新增用户Blocker；本轮已停止。NEXT: M3B0-08（尚未开始）。

### Round 12 — M3B0-08 审计实体与 SQLite 子项 → 新增 WfAiDecision 与四库共享契约测试，SQL Server PR filter 增量接线；类型发现红1失败，SQLite窄绿2/2、聚焦246/246、Workflow Release build 0 warnings/errors，独立SQLite2/2，基线/placeholder/diff通过。独立结论为本轮子项PASS、整体08 PARTIAL；因MySQL/PostgreSQL/SQLServer环境不可用，08保持未勾选，active、12/40。本轮已停止，未开始09。NEXT: M3B0-08，补齐三库实际持久化验证与独立复核。

### Round 13 — M3B0-08 Docker 四库验收 → 按用户授权安装 Docker 29.1.3，隔离运行 MySQL8.0.46、PostgreSQL16.15、SQLServer2022CU26；三库与SQLite目标持久化契约均各2/2，日志/TRX/hash/真实版本和端口经独立核验，08 PASS并勾选。源码/测试/CI零修改，三库环境缺口解除；临时容器/匿名卷已清理，Docker与镜像保留。active、13/40，本轮已停止。NEXT: M3B0-09（尚未开始）。

### Round 14 — M3B0-09 tx2 typed hand-off、shadow-only 与 stale fence → 红测检出替换 handler 成功绕过，加入最小 tx2 归一化；AI hand-off 2/2、typed stale 1/1、dispatcher 30/30、聚焦矩阵 245/245、Workflow Release build 0 warnings/errors、diff/integrity/placeholder 全部通过；独立 verifier PASS。勾选 09，active、14/40。本轮已停止。NEXT: M3B0-10（接入 EnterNodeOp 与现有 scheduler/dispatcher/handler 单一路径）。

### Round 15 — M3B0-10 AiDecision 接入既有 execution/scheduler/dispatcher/handler 单链 → `EnterNodeOp` 为 AI 节点创建 `Pending` execution，`WorkflowSetup` 以 `TryAddEnumerable` 注册内置 AI handler；入口、worker、seeded scheduler、dispatcher 与消费者优先回归通过。红测 4/4 预期失败，AI 链窄绿 6/6，影响范围聚焦矩阵 291/291，Workflow Release build 0 warnings/errors，`git diff --check` 与完整性检查通过；独立 verifier PASS，无有效 finding。勾选 10，status 保持 active、round 15/40。本轮已停止。NEXT: M3B0-11（仅在 fence/CAS 成功的 tx2 原子写 attempt、AI 审计、必要 Pending outbox 与人工兜底任务；尚未开始）。

### Round 16 — M3B0-11 tx2 原子写 attempt、AI 审计、Pending outbox 与人工兜底 → 红测 2 条真实检出缺口，AI 专项 6/6、影响范围 295/295、Workflow Release build 0 警告/0 错误、diff/integrity 通过；独立 verifier 对 dispatcher 34/34 与 diff-check PASS。勾选 11，status 保持 active、round 16/40。本轮已停止。NEXT: M3B0-12（Fake Provider 定义→实例→worker→审计→人工待办端到端测试）。

### Round 17 — M3B0-12 Fake Provider 定义→实例→scheduler/worker/dispatcher→审计→人工待办 → 新增真实 host-backed E2E：发布定义、启动实例、Pending execution、seeded scheduler、Fake proposal、tx2 attempt/AI audit/Pending outbox 与人工 task/actor/history 全链路通过；显式断言 `ShadowCandidate`、`ShadowOnly`、token Active、instance Running。窄测 1/1、影响范围 302/302、Workflow Release build 0 warnings/errors、`git diff --check` 与完整性检查通过；独立 verifier 复核 PASS、无 actionable finding。勾选 12，status 保持 active、round 17/40。本轮已停止。NEXT: M3B0-13（格式错误、低置信度、高风险、Provider 超时/异常、取消、重试、崩溃恢复、重复执行和 stale-fence 负向矩阵）。

### Round 18 — M3B0-13 Provider/worker/tx2 负向矩阵 → 6 个 Provider 情景、取消零副作用、崩溃后 lease/fence reclaim、重复扫描幂等和 stale owner 零副作用均通过；窄测 10/10、影响范围 312/312、Workflow Release build 0 warnings/errors、`git diff --check` 与完整性检查通过；独立 verifier PASS、无 actionable finding。勾选 13，status 保持 active、round 18/40。本轮已停止。NEXT: M3B0-14（脱敏后的 AI 审计读取投影/API、权限边界与越权/敏感字段测试）。

### M3B0-14/15 脱敏审计读取 API 与双前端 schema（Round 19，2026-09-08）

- 本轮开轮基线为 `/tmp/tenon-m3b0-round19-Pb22Yr/paths.sha256`，覆盖 1,683 个 Git 可见路径（含未跟踪文件）；保留全部前序修改。最终相对基线只改变 8 个授权代码/测试/schema 路径，台账另计：实例控制器、实例服务接口/实现、运行时投影模型、可替换性测试桩、AI 审计 API 测试，以及 Vue/React 生成 schema。
- 新增 `GET /api/v1/workflow/instance/ai-decisions/{id}`，复用 `WfInstanceService.RequireInstanceAsync` 与 `EnsureParticipantAsync`：发起人、当前办理人、历史办理人、抄送人和已有监控权限者沿既有边界可读，路人返回 `48015`。没有给新端点增加第二套权限判定或独立管理页。
- 新增 `WfAiDecisionAuditOutput` 脱敏投影；省略 `ProposalJson`、原始变量、执行内部 Id 和 Provider 异常正文，只映射受限元数据、hash、risk flags、evidence 引用、policy/fallback、token usage 与 shadow 标记。响应内植入的 proposal secret、`proposalJson` 和 `executionId` 均由 API 测试断言不得出现；无监控权限的当前办理人读取成功，非参与路人被拒。
- 先启动 Release `WorkflowTestHost` 的 Development 宿主生成 `/openapi/v1.json`，再运行 `npm run gen:api` 更新 `web/src/api/schema.d.ts` 与 `web-react/src/api/schema.d.ts`；两端 `npm run typecheck` 和 `npm run build` 均成功。前端构建仅报告既有 Rollup chunk/annotation 提示，无失败。
- 窄测：`dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WfAiDecisionAuditApiTests|FullyQualifiedName~WfReplayMonitorTests|FullyQualifiedName~WorkflowReplaceabilityTests' --logger 'console;verbosity=minimal'` → **18/18 passed，0 skipped，exit 0**；日志 `/tmp/tenon-m3b0-round19-narrow.log`，SHA-256 `84d8e88b309cc501c95e38a9755f4d3a6c2cba153f1d8b778c19edd1b7ceef95`。
- 影响范围矩阵：`dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter 'FullyQualifiedName~WfAiDecisionAuditApiTests|FullyQualifiedName~WfNodeExecutionProductionE2ETests|FullyQualifiedName~WfAiDecision|FullyQualifiedName~WfFakeAiDecision|FullyQualifiedName~WfOpenAiCompatibleAiDecision|FullyQualifiedName~WorkflowReplaceabilityTests|FullyQualifiedName~WfNodeHandlerContractTests|FullyQualifiedName~WfNodeExecutionEntryTests|FullyQualifiedName~WfNodeExecutionWorkerTests|FullyQualifiedName~WfNodeExecutionDispatcherTests' --logger 'console;verbosity=minimal'` → **313/313 passed，0 skipped，exit 0**；日志 `/tmp/tenon-m3b0-round19-focused.log`，SHA-256 `cfe0d65830e18681bdae49518bff9804efbb87134b90876b95695404a60276f3`。
- Workflow Release build：`dotnet build backend/src/TenonAdmin.Workflow/TenonAdmin.Workflow.csproj -c Release --no-restore` → **0 warnings / 0 errors，exit 0**；日志 `/tmp/tenon-m3b0-round19-workflow-build.log`，SHA-256 `5854d0f4b4d061bec34950f034858f0b4e1f9a7a19c5ab56071b50e309220d8c`。Vue/React typecheck 和 build 均 exit 0；`git diff --check` exit 0，日志 `/tmp/tenon-m3b0-round19-diff-check.log`，SHA-256 `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`。
- 独立 verifier `01a07faa-ad74-7850-868f-686efa650342` 在补齐监控权限非参与者回归后重新复核，结论 **PASS、无 actionable finding**；确认投影脱敏、参与者/监控边界和双 schema。范围限制为默认 SQLite/当前聚焦测试。本轮未运行真实模型或凭据、未实现 Task 8c、未提交/推送/创建 PR/tag。NEXT：M3B0-16（更新权威 workflow 文档与台账）。

### M3B0-16 权威 workflow 文档与台账收口（Round 20，2026-09-08）

- 先用 CodeGraph 定位并理解 workflow execution、AI 审计 API 与权限调用链；随后审阅当前 `docs/workflow/**` diff，保留 Round 46/Task 8b、M2c、M3a-1 的既有修订。未修改历史调研结论；在入口、设计规划、AI 实施基线和数据库评审中补充 M3b-0 当前状态。
- 权威文档现已明确：M3b-0 已交付；从 Provider 到 tx2/人工兜底全程 shadow-only，AI 不自动批准、拒绝、完成 task 或推进 token；`GET /api/v1/workflow/instance/ai-decisions/{id}` 只返回脱敏元数据、hash、风险/证据引用、策略/兜底、token usage 和 shadow 标记，并复用实例参与者/监控权限边界；proposal 原文、原始变量、执行内部标识和 Provider 异常正文不对外返回。
- 文档同步保留 Task 8c 的非目标边界：outbox consumer、transport、领取/投递/重试和状态回写仍未实现；M3b-0 只依赖 Task 8b 的 `Pending` 幂等入队。README、设计规划、AI 基线和数据库评审均把受控自动化、设计 UI 与独立审计管理页留到后续切片。
- 文档检查：四份权威文档关键词/一致性扫描通过；`git diff --check -- docs/workflow/...` 通过。Round 19 的既有验证证据保持不变：窄测 18/18、聚焦矩阵 313/313、Workflow Release build 0 warnings/0 errors、Vue/React typecheck/build、独立 verifier PASS。未运行真实模型或凭据，未实现 Task 8c，未提交、推送、创建 PR/tag。
- M3B0-16 已勾选；`status` 保持 `active`，`round` 更新为 `20 / 40`。NEXT：M3B0-17 独立代码审查、安全审查和简化审查。

### M3B0-17 独立审查、修复与复核（Round 21，2026-09-08）

- 独立 `code-reviewer` 发现五项有效问题：监控详情绕过机构数据范围、模型控制的 rationale/risk/evidence 字段未脱敏、空办理人没有审计事实、生产链未写 provider/model/version/usage、损坏审计 JSON 被静默当成空数组。独立 `architect` 另外确认元数据 typed handoff 与 `assigneeEmpty` 是阻断项；`code-simplifier` 确认 parser 重复校验和 handler 重复 fallback 可删除。
- 修复沿现有单链完成：`AiDecisionProviderResult`/`AiDecisionOutcome` 增加受限 provider/model/prompt/policy/usage 元数据；OpenAI-compatible response 解析 `usage.prompt_tokens`/`completion_tokens`/`total_tokens`，Fake/OpenAI/FailClosed 提供稳定标识；tx2 投影到 `WfAiDecision`。AI fallback 在 tx2 只解析一次办理人并把同一结果传给 `WfManualFallbackOp`；零人写入 `AssigneeEmpty`，仍不建任务、不自动放行。
- `EnsureParticipantAsync` 对 monitor permission 保留参与者例外，但要求实例在当前 `IOrgScoped` 范围内；proposal 的自由文本/引用在审计落库和 API 投影使用既有安全规则脱敏；损坏 risk/evidence JSON 返回受控 `OperationFailed/aiAuditCorrupt`。`IWfInstanceService.ListAiDecisionsAsync` 增加默认受控实现，旧消费者替换实现无需新增成员即可编译；parser 复用 typed factory 校验并保留 fail-closed。
- 新增/更新回归覆盖跨机构监控拒绝、损坏 JSON、模型字段脱敏、`AssigneeEmpty`、OpenAI usage metadata，以及真实 OpenAI-compatible Provider → handler → dispatcher → tx2 → `WfAiDecision` 贯穿链路；真实模型、网络凭据和 Task 8c 均未使用/实现。
- 验证：Workflow Release build **0 warnings / 0 errors**；审查修复聚焦矩阵 **317/317 passed，0 skipped**；本轮追加 dispatcher/replaceability/API **51/51 passed**，parser/provider **111/111 passed**，审计 API **3/3 passed**；`git diff --check` exit 0。独立 verifier `01a07ff0-0791-70e0-9048-7cdc5a1ad8a4` 在修复后结论 **PASS**，无 gap/risk。
- M3B0-17 已勾选；`status` 保持 `active`、`round` 为 `21 / 40`。NEXT：M3B0-18（目标测试、Release build、默认 SQLite 全量测试及 TODO/stub/skip/only/未实现分支检查）。

### M3B0-18 目标测试、Release build、SQLite 全量与静态收口（Round 22，2026-09-08）

- 开轮先执行 `git status --short --branch`：`dev...origin/dev`；既有工作树修改与未跟踪内容均保留，未提交、未推送、未创建 PR/tag。
- `dotnet build backend/TenonAdmin.slnx -c Release`：**Build succeeded，0 warnings / 0 errors，exit 0**。
- M3b 目标矩阵：
  `dotnet test backend/TenonAdmin.slnx -c Release --no-build --filter "FullyQualifiedName~WfAiDecisionAuditApiTests|FullyQualifiedName~WfNodeExecutionProductionE2ETests|FullyQualifiedName~WfAiDecision|FullyQualifiedName~WfFakeAiDecision|FullyQualifiedName~WfOpenAiCompatibleAiDecision|FullyQualifiedName~WorkflowReplaceabilityTests|FullyQualifiedName~WfNodeHandlerContractTests|FullyQualifiedName~WfNodeExecutionEntryTests|FullyQualifiedName~WfNodeExecutionWorkerTests|FullyQualifiedName~WfNodeExecutionDispatcherTests" --logger "console;verbosity=minimal"` → **317/317 passed，0 skipped，exit 0**。
- 默认 SQLite 全量：`dotnet test backend/TenonAdmin.slnx -c Release --no-build --logger "console;verbosity=minimal"` → **1400/1400 passed，0 skipped，exit 0**；未设置 `TENON_TEST_DBTYPE`，使用默认 SQLite。
- `git diff --check`：**exit 0**。对 M3b 相关生产/测试路径及新增 AI 文件扫描 TODO、stub、`test.skip`、`.only`、`NotImplementedException`、跳过断言和未实现标记均无命中；测试 HTTP/Stream helper 中为抽象成员所需的 `NotSupportedException` override 不属于生产未实现分支。
- M3B0-18 已勾选；`status` 保持 `active`、`round` 更新为 `22 / 40`。NEXT：M3B0-19（按 CI 配置执行 M3b 持久化/运行时目标测试的 SQLite、MySQL、PostgreSQL、SQL Server 四库验证及 contract drift）。

### M3B0-19 四数据库持久化/运行时矩阵与 contract drift（Round 23，2026-09-08）

- 按 `.github/workflows/backend-ci.yml` 启动临时 MySQL 8.0、PostgreSQL 16、SQL Server 2022 容器；测试完成后已删除三个 `tenon-m3b0-*` 容器，工作树未新增容器状态。
- 四库使用同一过滤器：`FullyQualifiedName~WfAiDecisionPersistenceContractTests|FullyQualifiedName~WfNodeExecutionContractTests|FullyQualifiedName~WfNodeExecutionEntryTests|FullyQualifiedName~WfNodeExecutionWorkerTests|FullyQualifiedName~WfNodeExecutionExceptionTests|FullyQualifiedName~WfNodeExecutionRecoveryTests|FullyQualifiedName~WfNodeExecutionProductionE2ETests|FullyQualifiedName~WfNodeExecutionRetryPolicyTests`，命令为 `dotnet test backend/TenonAdmin.slnx -c Release --no-build --filter '<filter>' --logger 'console;verbosity=minimal'`，数据库连接参数与 CI 一致：
  - SQLite（默认，未设置 `TENON_TEST_DBTYPE`）：**60/60 passed，0 skipped，exit 0**。
  - MySQL（`TENON_TEST_DBTYPE=MySql`）：**60/60 passed，0 skipped，exit 0**。
  - PostgreSQL（`TENON_TEST_DBTYPE=PostgreSQL`）：**60/60 passed，0 skipped，exit 0**。
  - SQL Server（`TENON_TEST_DBTYPE=SqlServer`）：**60/60 passed，0 skipped，exit 0**，耗时 32 分 54 秒。
- `node scripts/check-contract-drift.mjs` 在共享 dirty worktree 中完成 MinimalHost 启动和两套 schema 生成；其 `HEAD` stale guard 因本任务尚未提交的已授权 schema diff 返回 1。生成前后 `web/src/api/schema.d.ts` 与 `web-react/src/api/schema.d.ts` SHA-256 均为 `3df477787227ec39795b857283388702f25d0bb5067200de4e1dee82d2697dcf`，字节稳定。随后以当前完整 worktree 建立 `/tmp` 临时 Git 基线（未修改仓库、未提交产品分支）重新运行同一脚本，结果 **`contract in sync`，exit 0**。
- 收尾 `git status --short --branch` 仍为 `dev...origin/dev` 及原有修改集合；`git diff --check` exit 0。未提交、未推送、未创建 PR/tag。
- M3B0-19 已勾选；`status` 保持 `active`、`round` 为 `23 / 40`。NEXT：M3B0-20（独立 verifier 最终核验全部需求、事务/fence、权限/脱敏、四库、非目标与证据，PASS 后将 status 改为 done）。

### M3B0-20 独立最终核验（Round 24，2026-09-08）

- 独立 verifier `01a0802b-ffaf-7ac1-839c-ec8dfb8856d0` 只读核验当前源码、测试、台账和已记录命令，结论 **PASS，无验收阻断项**。确认现有 scheduler/worker/dispatcher 单链、shadow-only、CAS/fence 首写与 stale 零副作用、Provider/schema/policy、审计权限/脱敏、`AssigneeEmpty`、坏 JSON、replaceability、四库矩阵、schema contract 与 Task 8c 非目标均符合要求。
- verifier 交叉确认当前 Release 测试程序集晚于相关源码/测试文件，过滤器发现数为 317、1400、60；不重复运行长数据库测试。此前已记录的实际结果仍为目标 317/317、SQLite 全量 1400/1400、四库各 60/60，均 0 skipped。
- verifier 指出 `WfAiDecision.cs` 一句过时注释；已修正为“tx2 第二层强制规则将非兜底结果归一化为人工兜底”。修正后 `dotnet build backend/TenonAdmin.slnx -c Release --no-restore`：**0 warnings / 0 errors，exit 0**；`git diff --check`：**exit 0**；相关生产/测试路径禁止标记扫描无未实现生产分支。
- 最终状态：M3B0-01 至 M3B0-20 全部 `[x]`，Blockers 无未解决项；`status` 已改为 `done`、`round` 为 `24 / 40`。未提交、未推送、未创建 PR/tag；Task 8c 仍按固定非目标保持未实现。

### M3b-0 代码审查修复（Round 25，2026-09-09）

- 本节取代 Round 24 之后的旧审查结论，不改写历史证据。修复八项审查问题：Provider 异常捕获不再吞掉 parser/policy 未分类异常；所有模型可控审计文本（含 reason code、rationale、risk/evidence、Provider/Model/Prompt/Policy 版本）均哈希落库；补齐 engine/handler 与审计 reader 的受信任接管边界；审计 JSON 读取验证结构、数量、字段、重复项、hash、空白与序列化长度；列表按 `CreateTime, Id` 排序；`WfAiDecision` 改为 `AuditEntity`；审计读取拆为独立 `IWfAiDecisionAuditReader`；实体、tx2 与 API 补齐 `NodeId`。
- 为保持既有行为，安全输入投影的预期 `ArgumentException`/`JsonException` 仍在 Provider 调用前收敛为安全人工兜底；parser/policy 调用位于该捕获范围之外。第一次完整聚焦回归据此发现 17 条旧 fail-closed 用例失败，最小修复后安全输入及 parser/policy 专项 **28/28 passed**。
- 后续独立审查发现 reason code 明文、空白/超长审计 JSON、`WfInstanceService` 旧构造签名和审计 reader 接管说明四个缺口；均已修复并增加回归。相关窄测 **25/25 passed**，最终 M3b 聚焦矩阵 **326/326 passed，0 skipped**。
- 最终 `dotnet build backend/TenonAdmin.slnx -c Release --no-restore` 为 **0 warnings / 0 errors**；Vue 与 React 各自 `npm run typecheck`、`npm run build` 均通过；`git diff --check` exit 0。两份生成 schema SHA-256 均为 `a022e0d5957c8700f53f7ddfa65c499183d2f096c0896eec79c7b9f69ac375e4`，重复生成字节稳定；共享脏工作树的 HEAD guard 按预期报告未提交 schema，当前完整工作树的临时 Git 基线返回 **`contract in sync`，exit 0**。
- 独立 `code-reviewer` 复核结论 **APPROVE，0 findings**；独立 `architect` 复核结论 **CLEAR，无 blocker**。本轮未提交、未推送、未创建 PR/tag，未实现 Task 8c。
