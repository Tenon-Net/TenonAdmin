# Agent Skills 与 AI 辅助开发

AI 助手只有读到仓库契约、领域词汇和对应开发流程，产出的模块才会和现有代码接得上。开始前先判断任务属于内核维护还是消费方业务开发，再把对应入口交给助手：

- **参与 TenonAdmin 本体开发**：docs/agents/ 下的一组文档。
- **在 TenonAdmin 之上开发业务模块**：`skills/` 下的一组开发规范文档，教 agent 按项目既定模式建实体、建 CRUD、替换服务。

## Issue / PRD：走 GitHub Issues

仓库的 issue 和 PRD 都是 GitHub issue，统一用 `gh` CLI 操作，约定详见 [`docs/agents/issue-tracker.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/agents/issue-tracker.md)：

```bash
gh issue create --title "..." --body "..."          # 建 issue,多行 body 用 heredoc
gh issue view <number> --comments                     # 读 issue(含评论)
gh issue comment <number> --body "..."                 # 评论
gh issue edit <number> --add-label "..."               # 打标签
gh issue close <number> --comment "..."                # 关闭
```

`gh` 在 clone 出来的仓库里跑会自动从 `git remote -v` 识别仓库，不用额外指定 `--repo`。

::: details PR 目前不当作请求入口
`issue-tracker.md` 里这条开关当前是「否」：外部 PR 不会走和 issue 一样的标签流程。如果哪天改成「是」，`gh pr` 系列命令才会启用，包括 `gh pr view`、`gh pr diff`、`gh pr comment`、`gh pr edit --add-label`。启用后也只挑外部 PR 参与分诊，就是 `authorAssociation` 为 `CONTRIBUTOR` / `FIRST_TIME_CONTRIBUTOR` / `NONE` 的那些。
:::

## Triage 标签

Issue 分诊用五个规范化标签，标签串就是角色名本身，取值和用法在 [`docs/agents/triage-labels.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/agents/triage-labels.md) 定死：

| 标签 | 含义 |
|---|---|
| `needs-triage` | 维护者还没评估过 |
| `needs-info` | 等报告人补充信息 |
| `ready-for-agent` | 需求、边界和验收标准清楚，可交给自动化代理执行 |
| `ready-for-human` | 需要人工实现 |
| `wontfix` | 不会处理 |

自动化代理应优先选择 `ready-for-agent`，其余标签仍需要维护者判断或报告人补充信息。

## 领域文档：CONTEXT.md + docs/adr

[`docs/agents/domain.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/agents/domain.md) 要求 agent 动代码之前先看两样：

- 仓库根目录的 `CONTEXT.md`。多上下文场景下换成 `CONTEXT-MAP.md`，它指向各上下文各自的 `CONTEXT.md`。
- `docs/adr/` 下和当前改动区域相关的 ADR。

::: tip 按改动范围读取，不必全量加载
仓库根目录已经有 `CONTEXT.md`，`docs/adr/` 也记录了实时通知、外部登录、定时任务和可选安全等决策。先读 `CONTEXT.md`，再只选与当前改动直接相关的 ADR；没有关联 ADR 时继续工作，不需要为凑流程新建一份。
:::

如果你的产出里用到领域名词，比如 issue 标题、重构提案、测试名，就要和 `CONTEXT.md` 里的术语保持一致，别在文档已经明确定义的地方随意换用近义词。这些内容和已有 ADR 冲突时，要显式指出来，不能悄悄用新方案覆盖旧决策。

## 业务开发 Skills(`skills/`)

这组文档面向「在 TenonAdmin 上面接着写业务」的场景。内核维护者加系统模块，消费方在自己项目里二开，走的是同一套模式。索引在 [`skills/README.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/skills/README.md)：

| Skill | 用途 | 适用场景 |
|---|---|---|
| `new-module` | 新增完整业务模块的全流程编排 | 从零做一个模块（实体 → 后端 → 前端 → 菜单/权限） |
| `create-entity` | 创建 SqlSugar 实体类 | 新建表、新建实体 |
| `create-crud-backend` | 创建后端 CRUD 全套 | Models + Interface + Service + ErrorCode + DI + Controller |
| `create-crud-frontend` | 创建前端 CRUD 页面 | Types + API + Vue 页面（ProTable + FormContainer） |
| `create-crud-frontend-react` | 创建 React CRUD 页面 | Types + API + React 页面（DataTable + FormContainer + `<Can>`） |
| `replace-service` | 替换/扩展内置服务 | 定制登录流程、换密码哈希、覆写服务步骤 |
| `wire-import-export` | 给自己的实体接导入导出 | 装 `TenonAdmin.Excel`、档案、六个端点、菜单取号 |
| `create-job` | 给业务模块加定时任务 | `IAdminJob`、HTTP/SQL 任务、后台配置与验证 |
| `create-page-variant` | 非标准页面模板 | 树表、主从分栏、侧栏筛选 |

Claude Code 从 `.claude/skills/` 发现斜杠命令；Codex 从 `.agents/skills/` 发现 `$new-module`、`$create-entity` 等技能。两套入口都只引用 `skills/` 下的同一份流程。其它 AI 工具没有技能发现机制时，直接在对话中给出文件路径，例如「参考 `skills/create-entity.md`，帮我创建一个 BizProduct 实体」。完整命令清单以 [`skills/README.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/skills/README.md) 为准。

新增一个完整 CRUD 模块，标准顺序是下面三步。`new-module` 会把它们串起来一次跑完，想分步来就单独调用：

1. `/create-entity`：建实体
2. `/create-crud-backend`：建后端（含菜单种子数据）
3. 按模板选 `create-crud-frontend` 或 `create-crud-frontend-react`：建前端（含 i18n）

上面这三步连同 `new-module`，都会区分两种模式：**系统模块**是内核维护者用的，**业务模块**是消费方二开用的。两种模式生成的代码位置和命名规则不一样，用之前先说清楚是哪种场景。`replace-service` 只面向消费方，`create-page-variant` 只按页面形态分变体，都没有这条分叉。

## 参考

- 根目录 [`AGENTS.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/AGENTS.md) 是仓库指令的唯一来源；`CLAUDE.md` 只负责导入它。
- 想自己手动走一遍替换/扩展内置服务的流程（而不是让 agent 按 `replace-service` skill 生成），见 [替换内置服务](/zh/guide/replace-service)。
- 想了解怎么跑测试、怎么提 PR，见 [贡献指南](./contributing)。
