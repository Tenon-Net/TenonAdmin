# 选择前端模板

后端只有一个，前端给你两套：`web/` 是 Vue 3 加 Naive UI，`web-react/` 是 React 19 加 Ant Design。两套连的是同一份 OpenAPI 契约，登录、菜单、权限、增删改查逐个功能对齐。挑哪一套，只看你团队顺手哪个栈。

## 一样的后端，两种前端

两套模板都从后端的 `/openapi/v1.json` 生成类型化客户端，跑的是同一套 `gen:api`，连错误码、数据权限、动态菜单都是同一份。看得见的区别只落在组件库上，一套 Naive UI，一套 Ant Design。dev 端口还特意错开，`5173` 和 `5174` 能同时起，并排比着看。

| | Vue 模板 | React 模板 |
|---|---|---|
| 目录 | `web/` | `web-react/` |
| 技术栈 | Vue 3 + Naive UI | React 19 + Ant Design |
| 状态管理 | Pinia | Zustand |
| 表格封装 | ProTable | DataTable |
| dev 端口 | `5173` | `5174` |

## 怎么选

先看团队。天天写 Vue 的选 `web/`，习惯 React 的选 `web-react/`，这一条就够定大多数人的去向。功能上两套是对齐的，不存在「选了这个就少个能力」的取舍，别在功能清单上纠结。

没有明显偏好，就用 Vue 那套。它是随内核发布的默认模板，`dev.bat` 和快速开始里先拉起的都是它，遇到问题时能搭把手的人也更多。

两套模板各自自包含，互不引用，你只会带走选中的那一个，另一套不会跟着来。这是有意为之的产品决策：一套模板就是一个完整的起点，拉下来能独立跑、独立改、独立发。代价维护者自己扛：同一句文案、同一个设计令牌，两边各维护一遍。但那是仓库这一侧的事，落到你手里的，永远是一套干净、完整的模板。

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

拉下来这一份就完全归你，改成什么样都行。代价是没有升级通道，上游修了 bug 得自己读 diff 手动搬。想持续吃上游修复，别走快照，走[同步 Fork 与上游](/zh/guide/sync-fork)，那套接缝就是为把合并冲突压到近乎为零而做的。
