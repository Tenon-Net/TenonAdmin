---
name: new-module
description: 新增一个完整业务模块的全流程编排(实体 → 后端 CRUD → 测试 → gen:api → 前端页面 → i18n → 菜单/权限 → 验证)。当用户要"加一个 XX 管理模块/功能"、从零做一个新模块时使用;单做其中一步用对应的专项 skill。
---

读仓库根目录的 `skills/new-module.md` 并严格按它执行。它是单一真源,本文件只是入口包装;各步骤细节在它引用的专项 skill(`skills/create-entity.md`、`skills/create-crud-backend.md`、`skills/create-crud-frontend.md` 等)里。
