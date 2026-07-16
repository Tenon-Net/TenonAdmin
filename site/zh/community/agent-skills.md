# Agent Skills 与 AI 辅助开发

如果你用 Claude Code(或其它 AI agent)参与 TenonAdmin 的开发,仓库里有两类约定,对应两种不同场景:

- **参与 TenonAdmin 本体开发**——docs/agents/ 下的一组文档,规定 agent 怎么读 issue、怎么打分诊标签、怎么读领域背景。
- **在 TenonAdmin 之上开发业务模块**——`skills/` 下的一组开发规范文档,教 agent 按项目既定模式建实体、建 CRUD、替换服务,不管你是内核维护者加系统模块,还是消费者在自己项目里二开。

两者都不是代码生成器,是「规则说明 + 参考模板」——agent 读完之后按你的需求生成符合规范的代码,而不是照抄一段样板。

## Issue / PRD:走 GitHub Issues

仓库的 issue 和 PRD 都是 GitHub issue,统一用 `gh` CLI 操作(约定详见 [`docs/agents/issue-tracker.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/agents/issue-tracker.md)):

```bash
gh issue create --title "..." --body "..."          # 建 issue,多行 body 用 heredoc
gh issue view <number> --comments                     # 读 issue(含评论)
gh issue comment <number> --body "..."                 # 评论
gh issue edit <number> --add-label "..."               # 打标签
gh issue close <number> --comment "..."                # 关闭
```

`gh` 在 clone 出来的仓库里跑会自动从 `git remote -v` 识别仓库,不用额外指定 `--repo`。

::: details PR 目前不当作请求入口
`issue-tracker.md` 里这条开关当前是「否」:外部 PR 不会走和 issue 一样的标签流程。如果哪天改成「是」,`gh pr` 系列命令(`gh pr view`、`gh pr diff`、`gh pr comment`、`gh pr edit --add-label`)才会启用,并且只挑 `authorAssociation` 为 `CONTRIBUTOR` / `FIRST_TIME_CONTRIBUTOR` / `NONE` 的外部 PR 参与分诊。
:::

## Triage 标签

Issue 分诊用五个规范化标签,标签串就是角色名本身(详见 [`docs/agents/triage-labels.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/agents/triage-labels.md)):

| 标签 | 含义 |
|---|---|
| `needs-triage` | 维护者还没评估过 |
| `needs-info` | 等报告人补充信息 |
| `ready-for-agent` | 需求已经描述清楚,可以丢给 AFK agent 直接做 |
| `ready-for-human` | 需要人工实现 |
| `wontfix` | 不会处理 |

想找能自动化跑掉的任务,挑 `ready-for-agent` 标签的 issue 最省心。

## 领域文档:CONTEXT.md + docs/adr

开始探索代码前,agent 应该先看(详见 [`docs/agents/domain.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/agents/domain.md)):

- 仓库根目录的 `CONTEXT.md`(或多上下文场景下的 `CONTEXT-MAP.md`,指向各上下文各自的 `CONTEXT.md`);
- `docs/adr/` 下和当前改动区域相关的 ADR。

::: tip 这两样目前还不存在,这是正常状态
TenonAdmin 现在还没有 `CONTEXT.md` 或 `docs/adr/`——按约定它们是「懒创建」的,只有当 `/domain-modeling` 之类的 skill 真的需要落地某个术语或某条决策时才会建。文件不存在不代表约定不存在,也不需要因此要求先补文档。
:::

如果你的产出里用到领域名词(issue 标题、重构提案、测试名),要和 `CONTEXT.md` 里的术语保持一致,不要在文档已经明确定义的地方随意换用近义词;如果你的产出和已有 ADR 冲突,要显式指出冲突,而不是悄悄用新方案覆盖旧决策。

## 业务开发 Skills(`skills/`)

这组文档面向「在 TenonAdmin 上面接着写业务」的场景——无论是内核维护者加系统模块,还是消费者在自己项目里二开,都按同一套模式来(详见 [`skills/README.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/skills/README.md)):

| Skill | 用途 | 适用场景 |
|---|---|---|
| `create-entity` | 创建 SqlSugar 实体类 | 新建表、新建实体 |
| `create-crud-backend` | 创建后端 CRUD 全套 | Models + Interface + Service + ErrorCode + DI + Controller |
| `create-crud-frontend` | 创建前端 CRUD 页面 | Types + API + Vue 页面(ProTable + FormContainer) |
| `replace-service` | 替换/扩展内置服务 | 定制登录流程、换密码哈希、覆写服务步骤 |
| `create-page-variant` | 非标准页面模板 | 树表、主从分栏、侧栏筛选 |

**Claude Code** 下这五个 skill 已经包装成 `.claude/skills/` 下的斜杠命令,直接输入 `/create-entity`、`/create-crud-backend`、`/create-crud-frontend`、`/replace-service`、`/create-page-variant` 即可;也支持自然语言自动触发,比如直接说「帮我创建一个产品实体」。其它 AI 工具没有斜杠命令机制,在对话里直接引用文件路径就行,比如「参考 skills/create-entity.md,帮我创建一个 BizProduct 实体」。

新增一个完整 CRUD 模块的标准顺序:

1. `/create-entity` —— 建实体
2. `/create-crud-backend` —— 建后端(含菜单种子数据)
3. `/create-crud-frontend` —— 建前端(含 i18n)

每个 skill 都会区分**系统模块**(内核维护者)和**业务模块**(消费者二开)两种模式,生成的代码位置和命名规则不一样,用之前先说清楚是哪种场景。

## 参考

- 根目录 [`CLAUDE.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/CLAUDE.md) 的「Agent skills」一节是这些约定的索引入口。
- 想了解怎么跑测试、怎么提 PR,见 [贡献指南](./contributing)。
