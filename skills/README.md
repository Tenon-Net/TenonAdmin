# TenonAdmin Development Skills

本目录包含 TenonAdmin 的开发规范文档（skills），帮助开发者（或 AI 助手）按照项目既定模式快速搭建模块，无需手动翻源码对着抄。

这不是代码生成器——每个 skill 是一份规则说明 + 参考模板，AI 助手读取后根据你的需求生成符合规范的代码。

## Skills 列表

| 文件 | 用途 | 适用场景 |
|---|---|---|
| [new-module.md](new-module.md) | **新增模块全流程编排** | 从零加一个完整模块时的入口,串起下面各专项 skill |
| [create-entity.md](create-entity.md) | 创建 SqlSugar 实体类 | 新建表、新建实体 |
| [create-crud-backend.md](create-crud-backend.md) | 创建后端 CRUD 全套 | Models + Interface + Service + ErrorCode + DI + Controller |
| [create-crud-frontend.md](create-crud-frontend.md) | 创建前端 CRUD 页面 | Types + API + Vue 页面（ProTable + FormContainer） |
| [replace-service.md](replace-service.md) | 替换/扩展内置服务 | 定制登录流程、换密码哈希、覆写服务步骤 |
| [create-page-variant.md](create-page-variant.md) | 非标准页面模板 | 树表、主从分栏、侧栏筛选 |
| [write-docs.md](write-docs.md) | **文档写作规范** | 写或改 `site/` 下任何一页；中英双语的口吻、标点、开头、破折号 + 闸门 |

## 使用方式

### Claude Code

项目已配置 `.claude/skills/` 包装（薄壳，单一真源仍是本目录的 md），直接输入：

```
/new-module
/create-entity
/create-crud-backend
/create-crud-frontend
/replace-service
/create-page-variant
/write-docs
```

也支持自动触发——对 Claude 说"帮我加一个产品管理模块"即可匹配对应 skill。

### 其他 AI 工具

在对话中引用对应文件：

> 参考 skills/create-entity.md，帮我创建一个 BizProduct 实体

### 全栈 CRUD 完整流程

新增一个完整模块，从 [new-module.md](new-module.md) 进入——它按顺序编排 实体 → 后端 → 测试 → `gen:api` → 前端 → i18n → 菜单/权限 → 验证，并列出步骤间最容易断的交接点（权限码四处一致、MsgKey 对 i18n 键、种子 Id 保留区间）。

建实体、建后端、建前端这三个 skill 连同 `new-module` 都会区分**系统模块**（内核维护者）和**业务模块**（消费者二开）两种模式；`replace-service` 只面向消费者，`create-page-variant` 只按页面形态分变体，都没有这条分叉。
