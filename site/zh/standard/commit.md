# 提交规范

::: tip 一句话版
代码、注释、文档统一中文；**Git commit 信息一律用英文**，遵循 conventional-commit 格式：`type(scope): subject`。
:::

## 为什么提交信息要用英文，代码注释却用中文

两者面向的读者不一样。代码和文档是给维护这个仓库的人看的，团队内部沟通用中文，效率更高也更准确。提交历史是另一回事，它属于仓库对外的一部分。TenonAdmin 是面向所有 .NET 使用者分发的 NuGet 包，`git log` 会被贡献者读到，也会被下游消费方读到。生成 release note、判定语义化版本这类自动化工具，同样要解析它。统一用英文，这条历史才能人人可读，也能被工具解析。

## 格式

```
type(scope): subject

[可选的正文,说明动机/影响,而不是复述 diff]
```

- `type`：固定词表，见下方。
- `scope`：可选，标注改动影响的范围（模块名、包名、目录名）。跨领域或没有明确单一范围的改动可以省略。
- `subject`：祈使句、小写开头、不加句号，一句话说清做了什么。

## type 取值

从仓库真实提交历史提炼，新增提交按语义就近选用，不要自造新词：

| type | 用于 |
|---|---|
| `feat` | 新功能 |
| `fix` | 修复缺陷 |
| `docs` | 仅文档改动（README、设计文档、注释翻译等） |
| `refactor` | 不改变外部行为的内部重构 |
| `test` | 新增或调整测试 |
| `build` | 构建产物、打包元数据相关（如模板包图标/README） |
| `ci` | CI/CD 流水线、发版流程 |
| `chore` | 维护性杂务：依赖升级、忽略规则、重新生成产物，不影响功能与外部行为 |

## 真实例子（取自本仓库历史）

```
fix(web,backend): gate every action button by button-level permission
fix(web): hide permission-gated buttons for users without access
feat: demo mode — block write operations for showcase deployments
feat(notice): targeted delivery + rich notification bell
feat(menu): app-scoped permission-route picker + menu UX cleanup
refactor(web): fold the repeated paged-list mapping into two helpers
test: backfill CRUD/endpoint coverage for untested service areas
build(templates): add package icon and readme to the template package
ci(release): fix OIDC username typo (hu531035580 -> hu53135580)
docs: sync READMEs to zh-CN baseline, log 0.1.0 features
```

要点：

- `scope` 可以是单个模块（`web`、`notice`、`menu`），也可以逗号并列多个（`web,backend`）。改动确实横跨两侧时就如实标注，不必强行归到一个。
- 没有明确单一范围、或改动本身就是全局性的（比如整仓文档翻译、新增一个横切能力），省略 `scope`，直接 `type: subject`。
- `subject` 允许用 `—`/`:`/括号做简短的补充说明，但整体仍是一句话，不要写成多句复合句。

## 正文（body）什么时候要写

一行 `subject` 说不清楚改动的**动机**或**影响面**时才加正文，例如：

- 修的是一个隐蔽的安全问题，需要说明触发条件和影响范围。
- 一次改动牵涉多处协同（前后端字段改名、配置迁移），需要给后来者一份「为什么这么改」的说明。

正文不是用来复述 diff 的，diff 本身就能看到改了什么代码。它补的是 diff 里看不出来的背景。日常的小修小补不需要正文，一行 `subject` 足够，比如样式调整、单个 bug 修复。

## 常见误区

::: warning 不要做的事
- 提交信息写中文（哪怕代码注释是中文，commit 也要英文）。
- `subject` 首字母大写或结尾加句号（遵循 conventional-commit 惯例，小写开头、无句号）。
- 把多个不相关的改动塞进一个 commit，再用一个笼统的 `type` 概括（比如同时改了 `feat` 的功能和 `fix` 的缺陷）。应按语义拆成多个提交。
- `type` 用仓库历史里没出现过的自造词（如 `update`、`change`）。语义已经被 `feat`/`fix`/`refactor` 等覆盖，新造词只会让历史不一致。
:::
