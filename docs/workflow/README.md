# TenonAdmin.Workflow 文档入口

这里是 TenonAdmin 工作流领域的唯一文档入口。产品决策、开发计划、参考项目研究和 AI 工作流基石统一放在本目录；后续阅读先从本页开始，不再到 `docs/review/` 或外部参考仓中搜索。

## 当前定位

TenonAdmin.Workflow 以 **AI 原生审批**为产品方向：M1–M2 建立可信人工审批链，M3a 建立可靠机器节点执行 Module，M3b-0 交付 shadow-only 的 AI Decision v0 基础闭环。它仍是可替换的审批卫星包，不扩张成通用自动化编排平台。

共享领域术语仍保留在仓根 [`CONTEXT.md`](../../CONTEXT.md) 的“工作流”一节；它不是工作流专项文档，不从全仓领域词汇表中拆出。本目录负责完整设计和研究，`CONTEXT.md` 只保留跨任务必须统一的简短语义。

## 当前交付状态（2026-09-14，M3a-2 Vue 与 Task 8c 已完成）

M3b-0 已交付：AI Decision 已接入既有 execution、scheduler、worker、dispatcher 和 tx2 链路，Provider/proposal/policy、人工兜底和 append-only AI 审计均沿同一执行路径落库。内置执行链全程 **shadow-only**；AI 永不自动批准、拒绝、完成 task 或推进 token，低风险自动放行仍未开放。消费者整体替换 `IWorkflowEngine` 时属于受信任的完全接管，须自行维持事务、fence、审计与 shadow-only 不变量；自定义 handler 同样不得旁路写工作流状态。

审计读取 API `GET /api/v1/workflow/instance/ai-decisions/{id}` 只返回受限元数据、输入 hash、模型文本/证据引用 hash、节点标识、策略/兜底结果、token usage 和 shadow 标记，不返回 proposal 原文、原始变量、执行内部标识或 Provider 异常正文。权限复用实例参与者与监控边界：发起人、办理人、抄送人和持有 `GET:/api/v1/workflow/instance/monitor` 权限的非参与者可读，路人拒绝。

M3b-0 当时只依赖 Task 8b 的 `Pending` 幂等入队；Task 8c 已于 2026-09-14 补齐 `Pending → Dispatching → Dispatched/Failed` 的领取、可见性超时重领、`IWfOutboxTransport`、AttemptCount fence CAS、退避、死信和人工重放。默认 transport 是明确失败的 `NoOpWfOutboxTransport`，生产消费者必须在 `AddTenonAdminWorkflow()` 前替换为 HTTP/MQ transport。`WfOutboxStore` 仍只暴露 `EnqueueAsync`。后台扫描走固定 `wf-outbox-scan` 任务，不扫描 `wf_node_execution`。死信监控 API 为 `GET /api/v1/workflow/outbox/page`（缺省 `Failed`）与 `POST /api/v1/workflow/outbox/{id}/replay`；本轮不开发前端 outbox 页面。M3b-0 最终聚焦矩阵 326/326 通过，独立代码与架构复核无 blocker，详细证据见 [M3b-0 台账](../../.loop/wf-m3b0-ai-decision.md)。M3a-2 Vue 的 T01–T25A 已完成：Webhook 设计器、简易动态表单与字段权限、高级审批动词、并行分支、多 Token 回放和运行时安全投影已接入现有 Workflow 链路；四数据库代表流程各 `29/29` 通过，最新 Vue 单元测试 `183/183`，独立 `code-reviewer` 返回 `APPROVE`、`architect` 返回 `CLEAR`。本轮复审补齐表单变量 ID 的 number/string 兼容、非法变量阻止提交、重提表单回写、加减签与撤销 CAS 竞态以及动态字典 OpenAPI 契约。2026-09-14 功能测试收口已消除此前完整 Vue Playwright 的 MFA/RBAC 失败，以及历史投影丢掉 `DuplicateApproverSkipped.userIds` 的后端回归。完整 M3a-2 仍等待后续 React port；本轮未进入 React 工作流页面、AI 自动放行或 M3+。Vue 阶段证据见 [`m3a2-vue-task-plan-2026-09.md`](./m3a2-vue-task-plan-2026-09.md)。Task 8c 最终语义见设计规划 §15.8。

## 接下来开发计划（2026-09-15）

发布前约定：`NoOpWfOutboxTransport` 仅用于未接入外部通道的开发环境，运行时会明确写入 `Failed`；生产消费者必须在 `AddTenonAdminWorkflow()` 前注册真实 HTTP/MQ transport。

### 预览版发布门禁（2026-09-15 本地验收）

本轮在当前工作区候选改动上执行（含 `TakeBackTaskOp` 任务级 CAS 提前与 PostgreSQL/MySQL 模板库模式），**未**改版本号、**未** commit/push/打 tag，**未**开发 React。

| 门禁 | 结果 |
| --- | --- |
| Release build | `dotnet build backend/TenonAdmin.slnx -c Release` → 0 warning / 0 error |
| SQLite 全量 | `1540 passed / 0 failed / 0 skipped`，约 3 m 36 s（墙钟 ~220 s；含新增 HTTP transport 4 测） |
| MySQL 全量 | `TENON_TEST_MYSQL_TEMPLATE=1` → `1540 / 0 / 0`，约 5 m 29 s（墙钟 ~332 s） |
| PostgreSQL 全量 | `TENON_TEST_POSTGRESQL_TEMPLATE=1` → `1540 / 0 / 0`，约 6 m 7 s（墙钟 ~371 s） |
| SQL Server 全量 | 已恢复容器并闭合。`docker start tenon-t23-sqlserver` 后 `sa` 可登录；清掉崩溃残留 `tenon_*` 库；`TENON_TEST_SQLSERVER_TEMPLATE=1` + 本机四片并行（先把 `fs.inotify.max_user_instances` 提到 1024，否则四进程会撞 128 上限）。四片合计 **1540** 用例：片 1 `407/0/0`（~39.7 m）、片 2 初跑 `340/2/0`、片 3 `428/0/0`（~42.2 m）、片 4 初跑 `362/1/0`；墙钟约 42 m。3 个失败均为环境竞态（`HttpListener` 端口重试复用已 disposed 实例 ×2、`CodeFirstNullableUpgrade` 并行压力 ×1）；已修 listener（每次尝试新建 + 按 PID 错开端口），串行复跑 `WfOutboxHttpTransportTests` + 该 CodeFirst 用例 → **5/5** 通过（~3.3 m）。容器复跑后仍 `running`。**有效 1540/1540**。 |
| Redis | `docker exec tenon-t23-redis redis-cli ping` → `PONG`；契约测试后容器仍 `running`/`OOMKilled=false` |
| 真实 HTTP transport | 测试专用 `WfOutboxHttpTransportTests`（本地 `HttpListener` endpoint + `IWfOutboxTransport`，不改 `NoOp` 默认失败语义、不向核心包加依赖）：`Pending→Dispatching→Dispatched`；失败入 `Failed` 后 replay 再投递成功；同 `MessageKey` 重复投递业务计数仍为 1；未注册时内置 NoOp 仍明确 `Failed`。`4/4` 通过。 |
| Vue | `lint` / `typecheck` / `build` 通过；单元 `34 files / 183 passed`；工作流 Playwright `e2e/workflow-{layout,m2a,m2b,m3a2}.spec.ts` → `5 passed` |
| Contract drift | 本轮门禁未改 Controllers/DTO/OpenAPI 面；`schema.d.ts` 相对 HEAD 无变更，未重跑 `check-contract-drift.mjs` |

浏览器 E2E 仍主要覆盖登录相关既有用例、分支、基础动词和 Webhook 发布；动态表单权限、并行多 Token、加减签/拿回/长期委托仍以后端回归为主。Vue 预览版发布条件：**本地四库 + Redis + HTTP transport + Vue 检查已绿**（SQL Server 以模板库四片 + 失败用例串行复跑闭合）。

Vue/后端功能测试收口与 Task 8c 已完成。React 工作流 port 可以启动，但**本轮未实现**；AI 自动放行和 M3+ 仍未开始。

1. **React 工作流页面 port**：以已稳定的 Vue API、schema 和交互语义为输入，在 `web-react/` 独立实现定义设计器、表单运行时、实例详情和高级审批操作；不建立跨模板共享层，不手工编辑生成的 schema。
2. **M3b 受控自动化**：先用 shadow 评测集确定场景级阈值、人工推翻率和失败转人工规则，再设计默认关闭的受控放行；模型仍只能产生 proposal，不能直接推进 task/token。
3. **M3+ 能力**：在上述基线稳定后，分别评估带来源版本的 RAG、只读受控 Agent tools 和只能生成草案的设计 Copilot；每个能力先完成权限、预算、提示注入防护、幂等副作用和离线评测。

## 必读顺序

1. [`workflow-design-plan-2026-08-17.md`](./workflow-design-plan-2026-08-17.md) — 当前产品决策、Schema、数据模型、运行时语义、M1–M3+ 里程碑。**继续开发时先读这份。**
2. [`workflow-database-design-review-2026-08-24.md`](./workflow-database-design-review-2026-08-24.md) — 当前 9 表兼容性评审：保留项、实例/Token CAS、NodeVisitId、办理人历史，以及 M2c/M3a/M3b 的目标表与迁移顺序。**修改工作流字段或开发 M2c/M3a 时必读。**
3. [`elsa3-slickflow-ai-reference-2026-08-23.md`](./elsa3-slickflow-ai-reference-2026-08-23.md) — AI 工作流实施基线。§3 固化 Slickflow 调用链，§4 固化 Tenon 的 Module、Interface、execution/attempt/AI decision/outbox、安全不变量与验收线，§5 固化阶段安排。**开发 M3a/M3b 时必读。**
4. [`openworkflow-reference-2026-08-23.md`](./openworkflow-reference-2026-08-23.md) — 可靠执行参考：幂等、持久化唤醒、lease/fence、attempt、重试与崩溃恢复。**开发 M2c/M3a 时按需读。**
5. [`workflow-engine-research-2026-08-10.md`](./workflow-engine-research-2026-08-10.md) — 完整选型与参考项目调研，保留“为什么这样设计”的证据。日常实现不必通读。

## 按任务读取

| 任务 | 先读 | 再读 |
| --- | --- | --- |
| 继续 M2a/M2b | 设计规划 §13、§15.1（Version 字段提前项） | 总调研中对应产品参考 |
| 开发 M2c 幂等与四库契约 | 数据库评审 §四、§五、§九、§十 | 设计规划 §14.2、§15.1、OpenWorkflow 报告 §4–§6 |
| 开发 M3a-1 自动节点执行 | 设计规划 §15.2–§15.3、数据库评审 §四、§六、§八–§十 | AI 基石 §4.4–§4.8、OpenWorkflow 的 execution/lease/retry 部分 |
| 开发 Task 8c outbox consumer | 设计规划 §15.8、数据库评审 outbox 状态机 | AI 基石 §4.6、现有 `WfOutbox*` 测试 |
| 开发 M3a-2 Vue 主线（Webhook 设计器、表单/动词/并行） | M3a-2 Vue 任务计划、设计规划 §八、§15.2–§15.3 | 本地 goal 提示词；React port 后置 |
| 开发 M3b AI Decision | AI 基石 §4–§5 | 设计规划 §14.3、§15.4 |
| 开发 RAG/Agent/设计 Copilot | AI 基石 §2–§4 | 固定参考提交有变化时才增量复核源码 |
| 修改 `wf_*` 字段、索引或迁移 | 数据库评审全文 | 当前实体、四库契约测试和设计规划 |
| 查选型缘由或许可证 | 总调研 | 对应专项报告 |

## 继续开发提示词

下面的提示词可以直接交给后续 AI。优先使用“继续当前阶段”；只有已经明确要进入某个里程碑时，才使用对应的专项提示词。提示词要求先核对代码和测试，文档只负责约束方向，不能把规划中的能力误判为已经实现。

M3a-2 Vue 与 Task 8c 均已收口。下一产品切片是 React 工作流页面 port；AI 自动放行和 M3+ 仍后置。Vue 阶段记录仍见 [`m3a2-vue-task-plan-2026-09.md`](./m3a2-vue-task-plan-2026-09.md)。

### 继续当前阶段（推荐）

```text
继续开发 TenonAdmin.Workflow。

先完整阅读仓库根目录 AGENTS.md、CLAUDE.md 和 docs/workflow/README.md，再按入口中的“按任务读取”只读取当前任务需要的专项章节；固定参考 commit 没有变化时，不要重新通读外部参考仓。如果仓库存在 .codegraph，理解和定位代码时先使用 CodeGraph。

先检查 git status、当前代码、测试和最近提交，以代码与测试为事实源，确认 workflow-design-plan-2026-08-17.md 中当前已完成和下一个未完成的里程碑。不要覆盖用户已有改动，不要假设文档中的规划已经实现。

从下一个可独立验收的小步继续实现：先补能证明行为的测试，再完成代码和必要文档。保持 TenonAdmin.Workflow 为可替换的审批卫星包，不扩张成通用自动化编排平台；AI 只能生成 proposal，最终路由必须由服务端 schema/policy 决定。

完成前运行与改动风险相匹配的后端、前端和契约测试。若后端 API、DTO 或 XML 注释影响 OpenAPI，启动真实后端并重新生成 web 与 web-react 的 schema.d.ts，确认两份结果一致。把形成的最终语义回写到 docs/workflow/ 下的权威文档。

最后报告：本次完成的里程碑、关键设计决定、修改文件、验证结果、尚未完成项和建议的下一步。除非我明确要求，否则不要提交或推送。
```

### M2c：幂等与四数据库契约

```text
继续开发 TenonAdmin.Workflow 的 M2c“写操作幂等与四数据库契约”。

先阅读 AGENTS.md、CLAUDE.md、docs/workflow/README.md、workflow-database-design-review-2026-08-24.md 的 M2c 相关章节、workflow-design-plan-2026-08-17.md §14.2，以及 openworkflow-reference-2026-08-23.md 中幂等、receipt、事务边界和崩溃恢复相关章节。固定参考 commit 未变化时不要重新调研参考仓；存在 .codegraph 时先用 CodeGraph 定位调用链。

先依据 git status、当前代码和测试确认已有实现，再补齐所有关键写命令的 RequestId/IdempotencyKey、同事务 receipt、唯一性约束和首次结果重放语义。覆盖至少“重复请求不重复推进”“并发同键只有一次生效”“业务事务回滚时 receipt 不残留”“重试返回首次成功结果”。不要覆盖用户已有改动。

在 SQLite、MySQL、PostgreSQL、SQL Server 上补齐或运行等价契约测试，数据库差异必须封装在基础设施层。API 契约变化时通过真实后端重新生成两套 schema.d.ts 并校验一致。完成后更新工作流设计文档中的最终语义，报告测试矩阵、剩余风险和下一步；不要自行提交或推送。
```

### M3a：可靠自动节点执行

```text
继续开发 TenonAdmin.Workflow 的 M3a“可靠自动节点执行层”。

先阅读 AGENTS.md、CLAUDE.md、docs/workflow/README.md、workflow-database-design-review-2026-08-24.md 的 M3a 相关章节、elsa3-slickflow-ai-reference-2026-08-23.md §4.4–§4.8，以及 openworkflow-reference-2026-08-23.md 中 execution、attempt、lease/fence、retry、outbox 和恢复相关章节。存在 .codegraph 时先用 CodeGraph；固定参考 commit 未变化时不要重读外部项目。

以当前代码和测试为事实源，设计并实现最小闭环：IWorkflowNodeHandler 扩展点、持久化 WfNodeExecution/Attempt、稳定 execution key、短事务 claim、lease + fencing token、可分类重试、tx2 幂等写入 `Pending` outbox、超时与崩溃恢复。Task 8b 只负责在 tx2 中幂等写入 `Pending` outbox，不领取或实际投递 outbox；`Pending → Dispatching → Dispatched/Failed` 的领取、投递、重试、CAS 回写与 transport 已由 Task 8c 交付。先用 Fake Handler 和 Webhook Handler 验证执行框架，不在本阶段加入模型厂商耦合或让外部调用持有数据库事务。

测试必须证明：同一 execution 的重复执行不重复推进业务状态，外部投递携带稳定幂等键；过期 worker 不能覆盖新结果，进程在关键边界崩溃后可恢复，重试次数与最终状态可审计，人工任务原有语义不回归。Task 8c 已单独验收 outbox 的领取、投递、重试、CAS 回写与 transport。同步数据库迁移和四库兼容性；契约变化时重新生成双前端 schema。把最终状态机和不变量回写到工作流文档，报告验证结果和 M3b 可复用的接口；不要自行提交或推送。
```

### M3b：AI Decision v0

```text
继续开发 TenonAdmin.Workflow 的 M3b“AI Decision v0”。

先阅读 AGENTS.md、CLAUDE.md、docs/workflow/README.md、elsa3-slickflow-ai-reference-2026-08-23.md §4–§5，以及 workflow-design-plan-2026-08-17.md §14.3。存在 .codegraph 时先用 CodeGraph；固定参考 commit 未变化时不要重新调研参考仓。

M3b-0 已在 M3a 可靠执行层之上交付最小 AI 决策闭环：模型适配接口、Fake Provider 和一个 OpenAI-compatible Provider、结构化 proposal schema、服务端 policy 校验、shadow-only、人工兜底和完整审计。模型输出只能是 proposal，不能直接修改 task/token；当前不自动批准或拒绝。后续受控自动化默认关闭，阈值来自消费者部署在自己数据上的 shadow 评测，模型自报 confidence 不得单独作为放行条件。所有越权、解析失败、超时、低置信度、策略不匹配和敏感场景都必须确定性转人工。

测试覆盖结构化输出校验、策略路由、重复执行幂等、provider 故障、PII/secret 处理、tenant 隔离、审计重放，以及“模型不可直接推进流程”的安全不变量。先用 shadow 数据证明效果，再开放受控自动化。契约变化时重新生成双前端 schema，并把最终接口、阈值来源、审计字段和上线门槛回写到工作流文档；不要自行提交或推送。
```

### M3+：RAG、Agent 或设计 Copilot

```text
继续开发 TenonAdmin.Workflow 的 M3+ 能力，目标是：<在这里写 RAG、受控 Agent 或流程设计 Copilot 的具体目标>。

先阅读 AGENTS.md、CLAUDE.md、docs/workflow/README.md，以及入口中为“RAG/Agent/设计 Copilot”指定的章节。先核对当前 M3a/M3b 是否已经满足可靠执行、proposal/policy 分离、人工兜底、审计、幂等和租户隔离；任一基线未完成时，先补基线，不要直接堆叠自治能力。存在 .codegraph 时先用 CodeGraph，固定参考 commit 未变化时只做增量核对。

把目标拆成一个可独立验收的最小纵切：明确输入、结构化输出、可调用工具白名单、权限边界、预算/超时、证据引用、失败转人工和评估指标。RAG 必须保留来源与版本；Agent 的每个副作用必须经过服务端授权、幂等执行和审计；Copilot 只能生成可审查草案，不能绕过发布与校验流程。

先建立离线评测和失败样本，再实现功能；测试安全不变量、租户隔离、提示注入防护、工具越权、重复副作用和降级路径。完成后把稳定接口、评测基线和上线条件回写到 docs/workflow/，报告已验证能力和仍需人工控制的边界；不要自行提交或推送。
```

## 文档权威级别

发生冲突时按以下顺序处理：

1. 当前代码、测试和已合入 ADR；
2. `workflow-design-plan-2026-08-17.md` 的明确决议与里程碑；
3. Elsa/Slickflow 与 OpenWorkflow 专项报告中的固定源码事实；
4. `workflow-engine-research-2026-08-10.md` 的历史调研结论；
5. 外部项目 README、Wiki 和宣传页面。

参考项目源码位于本仓上级目录 `../参考项目/工作流/`（当前绝对路径为 `/home/shiny/github/参考项目/工作流/`），不进入本仓。专项报告已经保存固定 commit、源码锚点和转化后的 Tenon 设计；固定 commit 不变时，不重新通读参考仓。上游升级时只做差异核对，并把新结论回写到对应专项报告和本入口。

## 维护规则

- 新的工作流设计、调研和专项报告统一放在 `docs/workflow/`，并在本页登记。
- 需求实现中的最终语义同步回设计规划；不要只写在临时任务记录或聊天中。
- 契约性决定（`IdentityHash` 构造规则、状态机、fence 语义、放行阈值来源等）在任务收尾报告中列明回写到了哪份文档；只存在于聊天或 `.loop` 台账中的语义视为未定稿。
- 外部文档只能证明产品方向；已交付能力以固定源码和可获取包为准。
- AI 模型只生成 proposal，服务端 schema/policy 决定路由；模型不得直接修改任务或 token 状态。
- 移动或重命名本目录文件时，全仓更新引用，并重新生成受 XML 注释影响的双前端 OpenAPI schema。
M3a-2 Vue 的 T01–T25A 已完成：Webhook 设计器、简易动态表单与字段权限、高级审批动词、并行分支设计与多 Token 回放均已接入现有 Workflow 链路；四数据库代表流程验证共 `29/29` 通过，最终独立 `code-reviewer` 为 `APPROVE`、`architect` 为 `CLEAR`。2026-09-14 功能测试收口后 Vue Playwright `16/16`、后端 `1536/1536`。Task 8c outbox consumer/transport 已于同日补齐；完整 M3a-2 仍等待后续 React port，本轮未进入 React 工作流页面、AI 自动放行和 M3+。Vue 阶段证据见 [M3a-2 Vue 任务计划](./m3a2-vue-task-plan-2026-09.md)，outbox 最终语义见设计规划 §15.8。
