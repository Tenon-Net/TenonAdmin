# Agent 指令增量审计（2026-10-04）

本次在 [9 月 7 日审计](instruction-audit-2026-09-07.md) 的基础上处理触发冲突、重复澄清和无关材料加载，不修改业务实现。依据为 [OpenAI GPT-6 指南](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-6-astra#gpt-6-astra-testing-and-verification) 与 [Rethinking skills and prompts for GPT-6 Astra](https://x.com/pvncher/status/2095991462416490862)。

## 仓库内变更

| 范围 | 调整 |
| --- | --- |
| `AGENTS.md` | 任务参考按所需契约读取并复用有效上下文；单个缺失信息用普通澄清，系统梳理需求才进入访谈 |
| `deep-interview` / `autopilot` | Quick / Standard 按实质缺口提问，可核对完整规格后零新增问答交接；Deep 保留 pressure pass；阶段、真实产物、完成理由和权限边界保留 |
| `code-review` | 普通审查直接报告问题；明确合并就绪请求或活动流程门禁才加载 `references/merge-ready.md` 中的双代理协议 |
| 审查与规划结束条件 | 未提交审查覆盖 staged、unstaged 和新增文件；代码审查结论与实际可合并状态分开；Ralplan 达到迭代上限时向父流程报告失败，保留产物 |
| `analyze` / `ai-slop-cleaner` | 缩短分析触发描述，报告按内容组织；清理按行为变化和风险验证，不按固定 pass 重复检查 |
| Vue / React CRUD recipes | 保留契约、产出规则与交互验收，代码示例移到各自的 `skills/references/` 分支，继续独立维护 |
| `write-docs` | 共同契约与分支路由留在入口；README、站点正文细节分别按需读取 |
| 指令检查脚本 | 增加可重复的 `--skill-root`，检查选定外部技能目录的元数据与重名，支持真实路径去重和符号链接 |

`Product contracts` 与 `Durable Runtime Invariants` 正文与修改前逐字一致。模型设置、hook trust、状态所有权、取消边界和业务工作区原有修改未变。

## 本机用户级变更

以下内容不在本仓 Git diff 中，迁移到其他机器时需要单独配置：

- `~/.agents/skills/code-review/` 改名为 `spec-review/`，同步 `name`、界面名称和 `implement`、`implement-spec`、`tdd`、`ask-matt` 的调用引用。
- `~/.codex/config.toml` 将 `plugins."ponytail@ponytail".enabled` 设为 `false`，保留安装内容、marketplace 和 hook trust。关闭原插件是因为它的会话与子代理 hooks 会自动注入持久规则，仅修改描述不能解决冲突。
- 将六个 Ponytail 技能保留为 `~/.agents/skills/ponytail*` 的本地版本，不再由插件缓存维护。主技能按当前简化任务使用，保留明确需求、扩展接口、DI 替换点和项目测试工具；review / audit 同样尊重这些契约。
- 更新本地 help 的按需调用方式，并注明 gain 的上游历史基准不代表本地修改版或当前模型的收益。保留上游 MIT 许可。

本机原始文件备份位于 `~/.local/state/agent-guidance-backups/20261004-202124/`，目录仅当前用户可访问。恢复时只还原所需文件并核对之后的新改动；不要覆盖整个技能目录或配置。

普通技能使用 `$ponytail`、`$ponytail-review` 等名称。新会话重新加载插件开关与技能目录；本轮已经注入的旧技能目录不会因磁盘修改而从上下文消失。上游插件升级不会更新这些本地技能副本，后续按需比较更新；不要重新启用自动激活来更新技能。

## 验证与适用边界

- 指令结构检查覆盖仓库以及显式选择的用户级技能根；默认运行仍不依赖用户主目录。
- 新检查脚本的行为测试覆盖真实路径去重、符号链接、跨根重名、缺少元数据和错误参数。
- 修改过的技能 frontmatter 使用 YAML 解析，配置使用 TOML 解析。Portable skill validator 的 `argument-hint` 与 `disable-model-invocation` 白名单限制单独记录：保留既有宿主扩展，不把它们误报为 YAML 无效，也不宣称全部通过 portable 校验。
- 使用安装的 OMX `validateAutopilotCompletionTransition` 在临时目录验证：已有规范产物与完成理由可在零新增问答时进入 Ralplan；缺少理由产生缺口报告，越界产物被拒绝。仅调用纯验证函数，不写真实工作流状态，不将产物视为权限证明。
- 独立场景推演覆盖局部文案、普通审查、合并就绪审查、完整 PRD 复用、Deep 访谈和消费者替换接口。静态推演与单函数探针不等于完整 OMX 运行时端到端测试。

常用命令：

```bash
node scripts/check-agent-guidance.mjs
node scripts/check-agent-guidance.mjs --skill-root "$HOME/.agents/skills"
node --test scripts/check-agent-guidance.test.mjs
git diff --check
```

`--skill-root` 校验传入的候选目录，不自动解析宿主启用配置或插件命名空间。不要把已禁用插件、历史缓存版本或宿主未使用的系统技能副本一并当作当前启用目录；重复副本会被如实报告为重名。

本次未启动 Team / Autopilot / Ralplan，未跑无关产品测试，也未测量模型延迟或任务成本。`omx setup` 仍可能刷新仓库的 managed block 和 OMX skills，升级后应检查本次修正并重跑结构与路由验证。
