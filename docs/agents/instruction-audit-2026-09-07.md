# Agent 指令与工作流审计（2026-09-07）

## 范围与改动计划

目标是降低 GPT-6 Astra 的无关上下文和冲突指令，保持 TenonAdmin 产品约束与 OMX 权限边界。审计根 AGENTS.md/CLAUDE.md、模板入口、项目 skills、OMX skills/prompts/agents 和仓库 Codex 配置；workflow 指 AI 协作流程，不修改业务工作流引擎或发布 CI。

修改前已保存工作区原始文件到 `/tmp/tenon-agent-audit.TSJynN`；该快照包含本次开始前尚未提交的 OMX 安装内容，不能用 HEAD 代替它进行比较。

1. 合并根指令中的重复自治、分工、验证规则；保留运行时标记、状态所有权、取消边界和产品契约。架构及 CI 细节通过明确条件指针读取。
2. 根据官方支持的配置将主模型改为 `gpt-6-astra`，保留原有 `medium` 推理强度和专用子代理模型；不把所有任务强制升级到最高推理强度。
3. 修复技能发现、源文档链接和废弃工作流路由；保留项目模板示例和运行时安全协议，避免重写已安装运行时。
4. 修改前记录入口、标记及技能元数据基线；修改后验证本地链接、YAML/TOML、模型设置、运行时边界和独立场景推演。纯指令修改不运行不相关的后端/前端全套测试。

计划已由独立研究代理复核。具体保留项为 CodeGraph、OMX:AGENTS、OMX:GUIDANCE、OMX:MODELS、OMX:RUNTIME、OMX:TEAM:WORKER 标记，以及根 `Durable Runtime Invariants` 的完整正文。模型验证同时检查解析后的配置值和宿主可用模型信息。

## 官方依据

- [Using GPT-6 Astra](https://developers.openai.com/api/docs/guides/latest-model/gpt-6-astra.md)：明确建议审计 skills/AGENTS.md；校准自主完成、用户指令优先、并行委派及验证力度。迁移时除 `none`/`minimal` 外保留当前推理强度。
- [AGENTS.md](https://developers.openai.com/codex/guides/agents-md.md)：根到工作目录加载，默认总上限 32 KiB；简洁规则与按范围披露。
- [Build skills](https://developers.openai.com/codex/skills.md)：名称/描述先加载，正文按需加载；`.agents/skills` 是标准仓库发现路径，同名技能不会合并。
- [Subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents.md)：按职责拆分独立工作，避免并行写入冲突，主线程负责集成和最终交付。
- [Codex configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference.md)：模型与推理配置支持范围；实际可用性和优先级仍由当前宿主决定。

验证证明指令结构与约束的一致性；文本长度变化不等于模型质量或延迟提升，后者需要固定任务集的实际对比评测。

## 审计结论与修复

| 优先级 | 证据与问题 | 已做调整 |
| --- | --- | --- |
| 高 | 根 AGENTS 多处重复自治、工作流链、分工、验证，并固定列出旧模型表 | 合并成执行、路由、委派、验证与运行时边界；配置文件保持模型唯一来源 |
| 高 | `plan` 引用不存在的 `ask_codex`，skip planning 自动进入持久模式 | 普通工作直接执行；使用实际可用的问答工具；去掉 80%/90% 文案门槛 |
| 高 | `visual-ralph` 依赖退役 `$ralph`，每次都要求参考审批并把主观分数当硬门槛 | 保留兼容名，改为编辑/截图/比较；复用既有授权，尊重用户给定阈值 |
| 高 | `best-practice-research` 即使作为已授权实现的子步骤也要求用户再选流程 | 研究本身保持只读，完成后把证据交回原任务继续执行 |
| 高 | `doctor` 将标准 `.agents/skills` 当旧目录，示例广泛递归删除及覆盖全局 AGENTS | 仅处理精确、已确认归属的目标；保留用户技能和既有授权边界 |
| 高 | daisyUI 入口声称任意 HTML/JSX 都必须采用它，与两套模板选型冲突 | 仅匹配 daisyUI 明确请求或已采用的界面；不自动引入依赖 |
| 中 | 多个 OMX 技能把本不存在的 `templates/AGENTS.md` 当根规范 | 链接到本仓根 `../../../AGENTS.md` |
| 中 | Deep Interview 与 Ralplan 重复交接说明并推荐退役入口 | 合并交接表；问题 payload、研究 intake、stride schema 按条件加载；保留评分、阶段顺序、权限和恢复边界 |
| 中 | 超长仓库材料会强制用户自行概括，与代理应查明仓库事实冲突 | 代理分段读取并记录摘要；仅缺失关键源/决策时提问；摘要前不评分或交接 |
| 中 | 本仓只有发版技能被标准 Codex 发现，且又在 `.codex` 重复；Claude write-docs 路径/元数据无效 | `.agents` 提供 11 个薄入口，删除重复 Codex 发版入口，修正 Claude write-docs 的 SKILL.md 结构 |
| 中 | 项目示例存在陈旧事实与权限漏项 | 修复长文本 Unicode 类型、Vue 行按钮/状态权限、编码编辑说明、默认操作日志、任务 DI 发现、消费者验证路径、动态种子 ID 上限和错误国际化 |
| 中 | 流程固定跑重复全套验证，文档规范带旧计数/病历 | 按影响面验证；清理历史配额，保留实际 prose 检查及中英约束 |

审计前 inventory：25 个 OMX 技能、1 个标准 Codex 入口、16 个 Claude SKILL.md、12 份项目流程/索引、18 份原生角色配置、32 份角色 prompt。审计后为 24 / 11 / 17 / 12 / 18 / 32。角色 prompt 与 TOML 的数量不同不等于死配置：prompt 是独立执行面，未因没有同名 TOML 而删除。HUD/cancel 对旧模式的识别仍用于恢复，未误删为废弃入口。

## 上下文体积

以下比较本次开始前的工作区快照，而非 HEAD；未把用户原有 OMX 安装算成本次新增。

| 入口 | 修改前字节 | 修改后字节 |
| --- | ---: | ---: |
| AGENTS.md | 25,333 | 10,119 |
| CLAUDE.md | 18,007 | 4,076 |
| 合计 | 43,340 | 14,195 |

两份根指令合计减少约 **67%**。架构、环境和 CI 的必要细节保存在按需读取的 `docs/agents/repository-reference.md`。Deep Interview 的 payload、Autoresearch intake 和 execution-contract 也保留在自己的 references 中；这部分是延迟加载，不是删除功能。

## 验证记录

- `node scripts/check-agent-guidance.mjs`：52 个技能入口、142 个 Markdown 文件的入口字段、Codex 重名、正文相对链接及根标记检查通过。
- 使用已安装 YAML/TOML 解析器：52 份技能 YAML、19 份 TOML 解析通过；配置除主模型外与原始快照一致，18 份角色 TOML 逐字未变。
- 技能创建器的 portable validator：46 个入口通过；6 个 OMX 入口保留原有 `argument-hint`、`role`、`scope` 或 `triggers` 扩展，超出该校验器白名单。它们的 YAML 有效，但未宣称通过 portable validator，也未为消除提示而改写 hook 可能使用的字段。
- 独立静态场景推演覆盖：跳过规划直接修复、已指定截图、超长访谈材料、Ralplan 权限预检失败、研究后继续已授权实现、混合用户/OMX 技能目录、文档拼写修改。超长材料的摘要与轮次歧义已据反馈修正。
- 根 Durable Runtime Invariants 正文与原始快照逐字一致，SHA-256 为 `870ba8eadee490e89f744f5dcbb7bc68349da4791e2bdf1d77dddfd9456448cc`。
- `node --check scripts/check-agent-guidance.mjs`、`node site/scripts/lint-prose.mjs --selftest`、`git diff --check` 通过。

## 生效与局限

当前宿主列出了 `gpt-6-astra`；本机 CLI 为 0.153.4，本地没有 model cache。仓库 `.codex/config.toml` 已设为 `gpt-6-astra` / `medium`，但该文件被现有 gitignore 忽略，属于本机设置；18 份专用子代理配置保留原有模型与推理分工。启动参数、账户或宿主选择仍可覆盖默认值；未额外发起计费模型调用验证。

没有启动 OMX Team/Autopilot/Ralplan，也没有运行发布、发送通知或取消其他会话。当前为 outside-tmux App，静态场景推演不等于运行时端到端测试。前端未安装 node_modules，Markdown 内 Vue/React 示例未进行编译；权限示例已与现有 position 页面逐符号对照。未改业务实现，因此未跑数据库矩阵。

本次只删除 `.codex/skills/tenon-release/SKILL.md` 的重复入口，并将 `.claude/skills/write-docs.md` 移到有效技能目录；流程真源仍保留，原入口可从本次临时快照或 Git 恢复。其余技能主要为原地修正。临时快照不进入 Git，也不保证跨机器可用。

`omx setup` 仍可能重建 managed block、技能和角色提示。升级后应审查差异并重跑结构检查；本次没有修改安装器、hook trust 或权限实现来阻止生成。新会话重新加载根指导；技能更新通常自动发现，列表仍旧时重启会话。
