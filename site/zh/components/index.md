# 组件生态

列表页和图标选择器已经从 TenonAdmin 前端拆成独立 npm 包。业务项目可以直接使用模板里接好的版本，也可以在任何 Vue 3 + Naive UI 项目中单独安装；两种用法运行的是同一套组件。

## 先按任务选择

| 包 | 定位 | 文档 |
|---|---|---|
| [`tenon-naive-pro-table`](/zh/components/pro-table) | 列驱动 Pro 表格：一个 `columns` 同时驱动搜索表单、字典渲染、列设置；一个 `fetcher` 适配任意后端 | [查看 →](/zh/components/pro-table) |
| [`tenon-naive-iconify-picker`](/zh/components/icon-picker) | 离线优先图标选择器，基于 Iconify：多图标库、注册过的图标集渲染不发请求、单字符串值 | [查看 →](/zh/components/icon-picker) |

需要搜索、分页、列设置或树表时，先看 ProTable；需要让菜单、按钮和本地 SVG 共享一个图标值时，先看 IconPicker。两个包都已发布到 npm。具体 prop 与默认值以各自仓库的 README 为准，站内文档集中说明它们在 TenonAdmin 模板中的接法和边界。

在本管理端模板里怎么接入、主题与图标怎么对齐，见 [主题与图标](/zh/frontend/appearance) 与上面两个包各自的文档页。
