# 项目结构与启动

`app.use(pinia)` 一旦排到 `app.use(router)` 后面，路由守卫就读不到 store。`web/` 这一侧的规矩多半藏在这种顺序里，不在额外的约定层里。想知道什么在哪、什么时候跑，翻文件就能得到答案。

设计上为什么这么取舍（数据权限、可替换性），见[核心概念](/zh/guide/concepts)。写页面时的具体约定（API 调用、权限、状态、i18n）在[前端规范](/zh/standard/frontend)。

## 目录结构

以下路径均相对于 `web/src/`。

| 目录 | 职责 |
|---|---|
| `api/` | `client.ts`（类型化的 `openapi-fetch` 封装）+ `index.ts`（按域分组的接口调用）+ 生成的 `schema.d.ts` |
| `assets/` | 静态资源（SVG 等） |
| `components/` | 可复用组件（ProTable、FormContainer、Dict* 系列等，详见 `web/COMPONENTS.md`） |
| `composables/` | 与 UI 库无关的 `use*` 逻辑 |
| `directives/` | 自定义指令：`auth.ts` 定义 `v-auth` |
| `layouts/` | 布局壳：顶栏、侧栏、标签页、设置抽屉 |
| `lib/` | 小型初始化工具：`icons.ts` 导出 `setupIcons()` |
| `locales/` | i18n 资源与 `i18n` 实例（`index.ts`） |
| `router/` | 静态路由（`routes.ts`）+ 动态路由注入（`index.ts`） |
| `stores/` | Pinia 状态（`app`、`user`、`tabs` 等） |
| `styles/` | 设计令牌（`tokens.css`）与全局样式（`index.css`） |
| `theme/` | Naive UI 主题覆写（`naive-theme.ts`、`accents.ts`、`mix.ts`） |
| `types/` | 手写类型（`menu.ts`、`api.ts`） |
| `utils/` | 工具函数（`error.ts`、`chunkUpload.ts`、`tree.ts`、`ua.ts`） |
| `views/` | 页面，按模块组织 |

`src/` 之上还有两个文件：入口 `main.ts` 和根组件 `App.vue`。

## 启动流程

`main.ts` 按以下顺序把应用装配起来：

```ts
const pinia = createPinia()
pinia.use(piniaPluginPersistedstate)

const app = createApp(App)
app.use(pinia) // 必须在 router 之前:守卫用到 store
app.use(router)
app.use(i18n)
app.directive('auth', vAuth)

app.provide(PRO_TABLE_DEFAULTS, createProTableDefaults({ labels: computed(...) }))
setupIcons()
app.mount('#app')
```

1. **Pinia**（装了 `pinia-plugin-persistedstate`），必须注册在 router 之前，因为路由守卫要读 store 状态。
2. 然后是 **router**、**i18n**。
3. 全局注册 **`v-auth` 指令**（`directives/auth.ts`），它按权限码控制元素显隐。
4. **ProTable 默认配置**：给 `PRO_TABLE_DEFAULTS` 提供一份 `computed` 的 labels（搜索/重置/刷新/密度/列设置等），内部读 `i18n.global.t`。因为是 `computed` 且订阅了当前语言，切换语言时所有表格的文案会立即更新。各页面于是不用手动传 `:labels`。
5. **`setupIcons()`** 注册离线图标集与本地 SVG，并预热 `ph` 图标集。这一步是非阻塞的：注册完成后 `<Icon>` 从本地数据渲染，不会命中外部 CDN。
6. **挂载**到 `#app`。

`main.ts` 顶部还引入了 `styles/tokens.css` 和 `styles/index.css` 两份样式表，它们在上述流程执行之前就已加载。

`App.vue` 是挂载目标，补上了 `main.ts` 没做的部分：

- 用 `n-config-provider` 包裹全部内容。`:theme` 和 `:theme-overrides` 来自 `useTheme()` 组合式函数。`:locale` 和 `:date-locale` 按 app store 的 locale 算出来，取的是 naive-ui 的 `zhCN`/`enUS` 与 `dateZhCN`/`dateEnUS`。
- 内部嵌套 `n-message-provider` > `n-dialog-provider` > `router-view`。
- `onMounted` 时调用 `loadSite()` 拉一次站点品牌信息，这份信息是匿名的、全站共用的。`site.title` 有值就设 `document.title = site.title`。`loadSite()` 来自 `useSite()`。
- 监听 app store 的 `locale`，把 `i18n.global.locale.value` 同步过去（`immediate: true`），这样应用任何地方切换语言都会立即反映到译文上。

## Dev 代理

`vite.config.ts` 做了代理，让浏览器只跟 `:5173` 通信：

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5173,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
},
```

后端 dev 环境默认端口是 5100。要让 dev server 指向另一个后端实例，启动 Vite 前设置 `TENON_API_TARGET` 即可。后端 CORS 默认 deny-all。本地开发能同源访问，全靠这层代理。

`vite.config.ts` 还会在构建期从 `package.json` 的 `version` 字段 `define` 出 `__APP_VERSION__`，展示在登录页页脚。这个值在打包时就固化了，不走后端配置。

::: tip 兄弟包本地联调
如果你在同时开发 `tenon-naive-iconify-picker` 或 `tenon-naive-pro-table`，在 `npm run dev` 前设置 `NIP_LOCAL=1` 或 `NPT_LOCAL=1` 会把这些包别名指向兄弟仓库的源码，支持 HMR。除非你在直接改这两个包，否则用不上。
:::

## 常用脚本

以下命令在 `web/` 目录下执行：

| 脚本 | 命令 |
|---|---|
| `npm run dev` | `vite`：dev server,`:5173` |
| `npm run build` | `vue-tsc --noEmit && vite build` |
| `npm run preview` | `vite preview` |
| `npm run lint` | `oxlint` |
| `npm run lint:fix` | `oxlint --fix` |
| `npm run typecheck` | `vue-tsc --noEmit` |
| `npm run gen:api` | `openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts` |

::: warning gen:api 需要后端正在运行
`gen:api` 要从一个真实运行中的后端拉 `/openapi/v1.json`，所以要先启动后端（`dotnet run --project backend/samples/MinimalHost`，或直接跑 `dev.bat`）。生成的 `src/api/schema.d.ts` 禁止手改，因为下次跑 `gen:api` 就会被覆盖。
:::

仓库根目录还有两个批处理脚本，一次性管理整套服务：

| 脚本 | 作用 |
|---|---|
| `dev.bat` | 开两个窗口：后端（`dotnet run --project samples/MinimalHost`，`:5100`）和前端（`npm install && npm run dev`，`:5173`） |
| `stop.bat` | 结束占用 `5100`、`5173` 端口的进程 |

这套结构跑通之后，往下一页是[路由](/zh/frontend/routing)：后端菜单树怎么拼成路由表。再往后是[请求流程](/zh/frontend/request)，一次接口请求怎么走过类型化客户端。
