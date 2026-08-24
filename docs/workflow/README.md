# TenonAdmin.Workflow 文档入口

这里是 TenonAdmin 工作流领域的唯一文档入口。产品决策、开发计划、参考项目研究和 AI 工作流基石统一放在本目录；后续阅读先从本页开始，不再到 `docs/review/` 或外部参考仓中搜索。

## 当前定位

TenonAdmin.Workflow 以 **AI 原生审批**为产品方向：M1–M2 建立可信人工审批链，M3a 建立可靠机器节点执行 Module，M3b 交付 AI Decision v0，让机器处理低风险、人处理异常。它仍是可替换的审批卫星包，不扩张成通用自动化编排平台。

共享领域术语仍保留在仓根 [`CONTEXT.md`](../../CONTEXT.md) 的“工作流”一节；它不是工作流专项文档，不从全仓领域词汇表中拆出。本目录负责完整设计和研究，`CONTEXT.md` 只保留跨任务必须统一的简短语义。

## 必读顺序

1. [`workflow-design-plan-2026-08-17.md`](./workflow-design-plan-2026-08-17.md) — 当前产品决策、Schema、数据模型、运行时语义、M1–M3+ 里程碑。**继续开发时先读这份。**
2. [`workflow-database-design-review-2026-08-24.md`](./workflow-database-design-review-2026-08-24.md) — 当前 9 表兼容性评审：保留项、实例/Token CAS、NodeVisitId、办理人历史，以及 M2c/M3a/M3b 的目标表与迁移顺序。**修改工作流字段或开发 M2c/M3a 时必读。**
3. [`elsa3-slickflow-ai-reference-2026-08-23.md`](./elsa3-slickflow-ai-reference-2026-08-23.md) — AI 工作流实施基线。§3 固化 Slickflow 调用链，§4 固化 Tenon 的 Module、Interface、execution/attempt/AI decision/outbox、安全不变量与验收线，§5 固化阶段安排。**开发 M3a/M3b 时必读。**
4. [`openworkflow-reference-2026-08-23.md`](./openworkflow-reference-2026-08-23.md) — 可靠执行参考：幂等、持久化唤醒、lease/fence、attempt、重试与崩溃恢复。**开发 M2c/M3a 时按需读。**
5. [`workflow-engine-research-2026-08-10.md`](./workflow-engine-research-2026-08-10.md) — 完整选型与参考项目调研，保留“为什么这样设计”的证据。日常实现不必通读。

## 按任务读取

| 任务 | 先读 | 再读 |
| --- | --- | --- |
| 继续 M2a/M2b | 设计规划 §13 | 总调研中对应产品参考 |
| 开发 M2c 幂等与四库契约 | 数据库评审 §四、§五、§九、§十 | 设计规划 §14.2、OpenWorkflow 报告 §4–§6 |
| 开发 M3a 自动节点执行 | 数据库评审 §四、§六、§八–§十 | AI 基石 §4.4–§4.8、OpenWorkflow 的 execution/lease/retry 部分 |
| 开发 M3b AI Decision | AI 基石 §4–§5 | 设计规划 §14.3 |
| 开发 RAG/Agent/设计 Copilot | AI 基石 §2–§4 | 固定参考提交有变化时才增量复核源码 |
| 修改 `wf_*` 字段、索引或迁移 | 数据库评审全文 | 当前实体、四库契约测试和设计规划 |
| 查选型缘由或许可证 | 总调研 | 对应专项报告 |

## 继续开发提示词

下面的提示词可以直接交给后续 AI。优先使用“继续当前阶段”；只有已经明确要进入某个里程碑时，才使用对应的专项提示词。提示词要求先核对代码和测试，文档只负责约束方向，不能把规划中的能力误判为已经实现。

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

以当前代码和测试为事实源，设计并实现最小闭环：IWorkflowNodeHandler 扩展点、持久化 WfNodeExecution/Attempt、稳定 execution key、短事务 claim、lease + fencing token、可分类重试、outbox 唤醒、超时与崩溃恢复。先用 Fake Handler 和 Webhook Handler 验证执行框架，不在本阶段加入模型厂商耦合或让外部调用持有数据库事务。

测试必须证明：重复投递不重复产生业务副作用、过期 worker 不能覆盖新结果、进程在关键边界崩溃后可恢复、重试次数与最终状态可审计、人工任务原有语义不回归。同步数据库迁移和四库兼容性；契约变化时重新生成双前端 schema。把最终状态机和不变量回写到工作流文档，报告验证结果和 M3b 可复用的接口；不要自行提交或推送。
```

### M3b：AI Decision v0

```text
继续开发 TenonAdmin.Workflow 的 M3b“AI Decision v0”。

先阅读 AGENTS.md、CLAUDE.md、docs/workflow/README.md、elsa3-slickflow-ai-reference-2026-08-23.md §4–§5，以及 workflow-design-plan-2026-08-17.md §14.3。存在 .codegraph 时先用 CodeGraph；固定参考 commit 未变化时不要重新调研参考仓。

在 M3a 可靠执行层之上实现最小 AI 决策闭环：模型适配接口、Fake Provider 和一个 OpenAI-compatible Provider、结构化 proposal schema、服务端 policy 校验、shadow mode、置信度与风险阈值、人工兜底和完整审计。模型输出只能是 proposal，不能直接修改 task/token；v0 只允许低风险场景自动批准，不允许自动拒绝。所有越权、解析失败、超时、低置信度、策略不匹配和敏感场景都必须确定性转人工。

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

参考项目源码位于 `C:\HuHuHu\参考项目\工作流\`，不进入本仓。专项报告已经保存固定 commit、源码锚点和转化后的 Tenon 设计；固定 commit 不变时，不重新通读参考仓。上游升级时只做差异核对，并把新结论回写到对应专项报告和本入口。

## 维护规则

- 新的工作流设计、调研和专项报告统一放在 `docs/workflow/`，并在本页登记。
- 需求实现中的最终语义同步回设计规划；不要只写在临时任务记录或聊天中。
- 外部文档只能证明产品方向；已交付能力以固定源码和可获取包为准。
- AI 模型只生成 proposal，服务端 schema/policy 决定路由；模型不得直接修改任务或 token 状态。
- 移动或重命名本目录文件时，全仓更新引用，并重新生成受 XML 注释影响的双前端 OpenAPI schema。
