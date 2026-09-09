# TenonAdmin Development Skills

本目录是 TenonAdmin 开发流程和示例的唯一维护位置。先核对当前源码与调用方，再选择任务需要的分支；示例不替代真实接口契约。

这不是代码生成器——每个 skill 是一份规则说明 + 参考模板，AI 助手读取后根据你的需求生成符合规范的代码。

## Skills 列表

| 文件 | 用途 | 适用场景 |
|---|---|---|
| [new-module.md](new-module.md) | **新增模块全流程编排** | 从零加一个完整模块时的入口,串起下面各专项 skill |
| [create-entity.md](create-entity.md) | 创建 SqlSugar 实体类 | 新建表、新建实体 |
| [create-crud-backend.md](create-crud-backend.md) | 创建后端 CRUD 全套 | Models + Interface + Service + ErrorCode + DI + Controller |
| [create-crud-frontend.md](create-crud-frontend.md) | 创建前端 CRUD 页面（Vue 版，`web/`） | Types + API + Vue 页面（ProTable + FormContainer） |
| [create-crud-frontend-react.md](create-crud-frontend-react.md) | 创建前端 CRUD 页面（React 版，`web-react/`） | Types + API + React 页面（DataTable + FormContainer + `<Can>`） |
| [replace-service.md](replace-service.md) | 替换/扩展内置服务 | 定制登录流程、换密码哈希、覆写服务步骤 |
| [wire-import-export.md](wire-import-export.md) | **给自己的实体接导入导出** | 装 `TenonAdmin.Excel`、`IImportProfile`/`IExportProfile`、六个端点、菜单取号、两坑 |
| [create-job.md](create-job.md) | **给自己的模块加定时任务** | 写 `IAdminJob`、注册一行、后台建任务或写种子;HTTP/SQL 任务与五个常见坑 |
| [create-page-variant.md](create-page-variant.md) | 非标准页面模板 | 树表、主从分栏、侧栏筛选 |
| [write-docs.md](write-docs.md) | **文档写作规范** | 写或改 `site/` 下任何一页；中英双语的口吻、标点、开头、破折号 + 闸门 |
| [tenon-release.md](tenon-release.md) | **TenonAdmin 发版**（`/tenon-release`） | 定版 → CHANGELOG → 双前端 + 文档站徽章 → 验绿 → 合 main → 打 tag → 盯 CI / 改 Release 说明 |

## 使用方式

### Claude Code / Grok

项目已配置 `.claude/skills/` 包装（薄壳，单一真源仍是本目录的 md），直接输入：

```
/new-module
/create-entity
/create-crud-backend
/create-crud-frontend
/create-crud-frontend-react
/replace-service
/wire-import-export
/create-job
/create-page-variant
/write-docs
/tenon-release
```

也支持自动触发——对 Claude 说"帮我加一个产品管理模块"即可匹配对应 skill。Grok 同样扫描 `.claude/skills/`。

### Codex

标准 Codex 从 `.agents/skills/` 发现本仓的 11 个开发技能，使用 `$new-module`、`$write-docs` 等调用，也可按描述自动选择。`.claude/skills/` 是 Claude/Grok 的兼容入口，两者均引用本目录的流程，不复制规则。

| 路径 | 作用 |
|------|------|
| `.agents/skills/<name>/SKILL.md` | 标准 Codex 项目级发现入口 |
| `~/.agents/skills/<name>/SKILL.md` | 标准 Codex 用户级入口 |
| `.codex/skills/<name>/SKILL.md` | 当前 OMX 安装提供的编排技能；是否加载取决于宿主配置 |

同一 Codex 可见范围内，每个名字保留一个入口，避免选择器出现重复项。Codex 会自动发现技能更新；当前会话未显示时再重启。发现规则见 [OpenAI 官方 skills 文档](https://developers.openai.com/codex/skills.md)。

### 维护 AI 工作流

根 [AGENTS.md](../AGENTS.md) 统一管理产品契约、执行、分工和权限边界；[CLAUDE.md](../CLAUDE.md) 仅导入该文件，供 Claude Code 加载。普通任务直接完成，按需读取技能；流程讨论不触发流程执行。OMX 运行时技能须检查实际宿主支持，配置和状态文件不是额外写入权限。

`omx setup` 可能刷新 `.codex/skills`、角色提示和根 managed block。更新后检查差异，保留本仓修正；不要把 `--force` 重装当作无副作用验证。审计依据与验证方法见 [2026-09-07 审计](../docs/agents/instruction-audit-2026-09-07.md)。

### 其他 AI 工具

在对话中引用对应文件：

> 参考 skills/create-entity.md，帮我创建一个 BizProduct 实体

### 全栈 CRUD 完整流程

新增一个完整模块，从 [new-module.md](new-module.md) 进入——它按顺序编排 实体 → 后端 → 测试 → `gen:api` → 前端 → i18n → 菜单/权限 → 验证，并列出步骤间最容易断的交接点（权限码四处一致、MsgKey 对 i18n 键、种子 Id 保留区间）。前端有两套官方模板（`web/` Vue 与 `web-react/` React，零共享、各自维护），前端那一步按消费者选的模板走对应 skill，不用两边都做。

建实体、建后端、建前端这三个 skill 连同 `new-module` 都会区分**系统模块**（内核维护者）和**业务模块**（消费者二开）两种模式；`replace-service` 只面向消费者，`create-page-variant` 只按页面形态分变体，都没有这条分叉。
