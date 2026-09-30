# 选择前端模板

Vue 和 React 两套模板连接同一个 TenonAdmin 后端。选择团队熟悉的一套即可：`web/` 使用 Vue 3 和 Naive UI，`web-react/` 使用 React 19 和 Ant Design。每套都可以独立安装、开发和部署。

## 一样的后端，两种前端

两套模板各自运行 `npm run gen:api`，从后端的 `/openapi/v1.json` 生成 API 类型。它们使用相同的后端权限与错误契约，但组件、状态管理和页面实现各自维护。开发端口分别为 `5175` 和 `5174`，需要比较时可以同时运行。

| | Vue 模板 | React 模板 |
|---|---|---|
| 目录 | `web/` | `web-react/` |
| 技术栈 | Vue 3 + Naive UI | React 19 + Ant Design |
| 状态管理 | Pinia | Zustand |
| 表格封装 | ProTable | DataTable |
| dev 端口 | `5175` | `5174` |

选好模板后，跟着 [Vue 分步教程](/zh/frontend/getting-started)或 [React 分步教程](/zh/frontend-react/getting-started)，先创建静态页面，再接菜单、接口和权限。完成练习后，再按需要查阅路由、请求层、国际化和主题等参考章节。

登录后的用户管理页面如下。左侧选择功能，中间筛选和查看记录，表格上方新增、导入或导出。两套模板的布局和组件风格不同，业务权限来自同一个后端。点击图片可放大。

[![Vue · Naive UI](/screenshots/vue-admin.png)](/screenshots/vue-admin.png)

[![React · Ant Design](/screenshots/react-admin.png)](/screenshots/react-admin.png)

## 怎么选

选择时主要看团队经验和现有组件。已有 Vue 项目可沿用 Vue 模板；使用 React 和 Ant Design 的团队可选择 React 模板。接入前，检查所需页面和组件在选定版本中的实现。

没有明显偏好时，可以先运行 Vue 示例，再通过在线演示或本地 React 模板比较组件用法。选择前端不会改变后端的权限模型。

两套模板互不引用。只需要安装所选目录的依赖，也只构建和部署该模板；无需维护另一套前端。开发页面时，应使用对应模板的组件和约定。

## degit 一份，归你自己

想在仓库里直接跑一遍看看，去[快速开始](/zh/guide/getting-started)，那里两套的启动命令都在。想把某一套当成自己项目的起点，用 degit 拉一份不带 `.git` 历史的快照，选哪套拉哪套：

::: code-group

```bash [Vue (web/)]
npx degit Tenon-Net/TenonAdmin/web my-web
```

```bash [React (web-react/)]
npx degit Tenon-Net/TenonAdmin/web-react my-web
```

:::

快照适合独立维护前端的项目，但不保留上游 Git 历史，后续修复需要手动迁移。如果希望通过 Git 合并更新，选择[同步 Fork 与上游](/zh/guide/sync-fork)的方式。无论采用哪种方式，都要检查前端与后端版本是否配套。
