# 组件生态

几个和业务无关的通用组件已从 TenonAdmin 前端**拆成独立 npm 包**:不依赖 TenonAdmin,任何 Vue 3 + Naive UI 项目都能单装,也是本管理端在用的同一套。

| 包 | 定位 | 文档 |
|---|---|---|
| [`tenon-naive-pro-table`](/zh/components/pro-table) | 列驱动 Pro 表格:一个 `columns` 同时驱动搜索表单、字典渲染、列设置;一个 `fetcher` 适配任意后端 | [查看 →](/zh/components/pro-table) |
| [`tenon-naive-iconify-picker`](/zh/components/icon-picker) | 离线优先图标选择器,基于 Iconify:多图标库、零网络请求、单字符串值 | [查看 →](/zh/components/icon-picker) |

> 两个包均已发布到 npm,遵循各自仓库的 README 为权威文档;本站页面是精选速览。

在本管理端模板里怎么接入、主题与图标怎么对齐,见 [主题与图标](/zh/frontend/appearance) 与上面两个包各自的文档页。
