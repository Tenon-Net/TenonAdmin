# 项目结构与启动

第一次进入 `web-react/`，先把模板跑起来，再顺着入口文件定位目录。这样看到 `api/`、`router/`、`stores/` 时，每个名字都能对应到浏览器里已经发生的动作。

两套模板怎么取舍、各自装了什么见[前端模板对比](/zh/guide/frontend-templates)；写页面的具体约定散在[请求流程](/zh/frontend-react/request)、[权限](/zh/frontend-react/permission)、[国际化](/zh/frontend-react/i18n)几页，这里只讲地基。

> 前置：还没有接通过页面时，先完成 [React 前端入门](/zh/frontend-react/getting-started)。

## 先运行，再定位入口

从仓库根目录启动 React 模板：

```bash
npm --prefix web-react run dev
```

打开终端打印的地址并登录。浏览器里的侧栏来自 `layouts/`，当前页面来自 `views/`，网络请求经过 `api/`，刷新后恢复登录与菜单则依赖 `stores/` 和 `router/`。

[![React 界面与源码目录的对应关系](/screenshots/react-admin.png)](/screenshots/react-admin.png)

接着打开 `web-react/src/main.tsx`，找到 token CSS、store、图标与 Markdown 初始化，再看最后的 React root。这里的顺序会影响运行结果：`@/styles/tokens.css` 若排到 `App` 后面，主题桥读取 CSS 变量时会得到空值，antd 随后静默退回默认色。

## 目录结构

以下路径均相对于 `web-react/src/`。

| 目录 | 职责 |
|---|---|
| `api/` | `client.ts`（类型化客户端及认证、刷新、再认证中间件）+ `index.ts`（按域分组的接口调用）+ 生成的 `schema.d.ts` |
| `assets/` | 静态资源：`svg/`（本地 SVG）与 `icons.generated.json`（`gen:icons` 构建期生成的离线图标子集，非手写） |
| `components/` | 可复用组件（DataTable、FormContainer、Can、Dict* 系列等，详见 `web-react/COMPONENTS.md`） |
| `composables/` | 不依赖组件生命周期的模块级逻辑：门户进入与切换、SignalR 长连接及未读消息发布订阅 |
| `hooks/` | 需要组件上下文的 React hook：`useConfirm`（二次确认 + 结果 toast，内部用 antd `App.useApp`）、`useBatchDelete`（勾选态 + 批量删除） |
| `layouts/` | 布局壳：顶栏、侧栏、标签页、设置抽屉、菜单搜索、通知铃铛 |
| `lib/` | 一次性初始化与运行时基座：`icons`（离线图标注册）、`markdown`（md-editor-rt 的 XSS 过滤接线）、`echarts`（按需注册图种） |
| `locales/` | i18n 资源与 i18next 实例；业务文案放入 `ext/`，可减少同步上游时修改同一文件的冲突 |
| `router/` | `buildRoutes`（菜单树 → `RouteObject`）、`menuRoutes`（菜单路由决策）、`detailRoutes`（约定式详情路由）、`Protected`（守卫组件）、`MissingRoute` |
| `stores/` | Zustand 状态（`app`、`auth`、`user`、`dict`、`site`、`tabs`），各 store 按生命周期选择持久化字段 |
| `styles/` | `tokens.css`（设计令牌）、`chrome.css`（布局壳裸 CSS + reset）、`code.css`（代码块高亮配色） |
| `theme/` | antd 主题桥：`antd-theme`（构建 `ThemeConfig`）、`useAntdTheme`（落 `data-*` 并重建主题的 hook）、`accents`/`mix`（强调色候选与混色）、`useDocumentGrayscale`（哀悼灰阶，独立 CSS filter） |
| `types/` | 手写领域类型（`menu.ts`、`api.ts`），UI 层消费它而不直接用 openapi 生成的 verbose 类型 |
| `utils/` | 工具函数（`error.ts`、`chunkUpload.ts`、`tree.ts`、`ua.ts`、`url.ts`） |
| `views/` | 页面，按模块组织 |

`src/` 之上还有两个文件：入口 `main.tsx` 和根组件 `App.tsx`。装配拆成两半：`main.tsx` 管全局单例与副作用，`App.tsx` 管 Provider 树与路由。

Vue 版有个 `router/index.ts`，用命令式的 `addRoute` 把动态路由一条条挂上去。React 这边没有对应的文件：路由由 `buildRoutes(menuTree)` 算出来，交给 `useRoutes` 渲染。`menuTree` 一变，路由自动跟着重新计算，不用手动挂载。细节见[路由](/zh/frontend-react/routing)。

## 启动流程

`main.tsx` 只做全局初始化，然后把 `<App />` 挂上去：

```tsx
import '@/styles/tokens.css'   // 必须在 import App 之前:主题桥要 getComputedStyle 读回这些变量
import '@/styles/chrome.css'
import '@/styles/code.css'
import '@/locales'             // 副作用:建 i18next 实例并接上 store 订阅
import { setupIcons } from '@/lib/icons'
import { setupMarkdown } from '@/lib/markdown'
import App from './App'

setupIcons()      // 注册 4 套离线图标集(非阻塞,集合就绪后菜单/AppIcon 自动重渲染)
setupMarkdown()   // 首次渲染前挂上 md-editor-rt 的 XSS 过滤插件

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
```

三份样式表写在最前面，因为 ES module 是按书写顺序求值的。`tokens.css` 要是排到 `import App` 后面，`App` 那一整条依赖会先被求值。这些模块求值时如果调了 `getComputedStyle`，读到的就是空值。空值喂给 antd 的 `ConfigProvider`，颜色就悄悄掉回默认色。这和 Vue 版 `app.use(pinia)` 必须排在 `app.use(router)` 前面，是同一类顺序问题。

`App.tsx` 补上 `main.tsx` 没做的部分：Provider 树、antd 上下文、路由。

```tsx
export default function App() {
  // 分字段订阅,不整体订阅:无关字段变动不该重建整棵 ConfigProvider
  const dark = useAppStore(isDark)
  const accent = useAppStore((s) => s.accent)
  const density = useAppStore((s) => s.density)
  const locale = useAppStore((s) => s.locale)

  const themeConfig = useAntdTheme({ dark, accent, density })
  useDocumentGrayscale() // 灰阶是 <html> 上的一条 CSS filter,不进 antd 主题依赖

  useEffect(() => { /* loadSite() 拉站点品牌,有值就设 document.title */ }, [])

  return (
    <ConfigProvider theme={themeConfig} locale={locale === 'en-US' ? antdEnUS : antdZhCN}>
      <AntdApp>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/oauth/callback" element={<CallbackPage />} />
            <Route path="/*" element={<Protected />} />
          </Routes>
        </BrowserRouter>
      </AntdApp>
    </ConfigProvider>
  )
}
```

- **`ConfigProvider`** 是主题桥。`theme` 来自 `useAntdTheme()`。`locale` 传的是 antd 自带的一套文案（`antd/locale` 里的 `zh_CN`/`en_US`），跟应用自己的 i18n 是两套系统，得一起切换。不切会出现「界面是中文，表格却显示 No data」这种错位。
- **`AntdApp`**（antd 的 `App` 组件）提供 `message`/`modal`/`notification` 这几个上下文实例。`useConfirm` 一类 hook 要靠它才能用，所以它得套在 `ConfigProvider` 里面。
- **路由**只有两条静态项：`/login`、`/oauth/callback`。其余全部交给 `/*` 下的 `Protected` 处理。登录守卫、强制改密、F5 深链重建、布局壳，还有从菜单派生出来的动态路由，都在 `Protected` 里面，细节见[路由](/zh/frontend-react/routing)和[门户与守卫](/zh/frontend-react/portal-guards)两页。
- `useEffect` 里调一次 `loadSite()`，拉匿名站点信息。拿到 `title` 就写进 `document.title`，对应 Vue 侧 `App.vue` 的 `onMounted`。

## Dev 代理

`vite.config.ts` 做了代理，让浏览器只跟 `:5174` 通信：

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5174,
  strictPort: true,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
    '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR Hub,ws 反代 WebSocket 升级
  },
},
```

React 使用 5174，Vue 使用 5175，两个模板可以同时运行。`strictPort: true` 是刻意加的：默认情况下，5174 被占后 Vite 会静默挪到下一个空闲端口。浏览器书签或脚本若仍指向 5174，就会连到占用该端口的进程。直接拒绝启动，端口冲突才不会被掩盖。

后端 dev 端口默认 5100。要指向别的后端实例，启动 Vite 前设 `TENON_API_TARGET` 即可。后端 CORS 默认 deny-all，本地能同源访问全靠这层代理。`/hub` 那条比 Vue 版多出来，反代 SignalR 的 WebSocket，实时通知走它。

`vite.config.ts` 还在构建期从 `package.json` 的 `version` 字段 `define` 出 `__APP_VERSION__`，展示在登录页页脚。这个值打包即固化，不走后端配置。`resolve.alias` 只有一条 `@` → `./src`：本模板自包含，不引 `web/` 也不引任何共享层，所以不需要给别的路径指路。

## 常用脚本

以下命令在 `web-react/` 目录下执行：

| 脚本 | 命令 |
|---|---|
| `npm run dev` | `vite`：dev server，`:5174`（`predev` 先跑 `gen:icons`） |
| `npm run build` | `tsc --noEmit && vite build`（`prebuild` 先跑 `gen:icons`） |
| `npm run preview` | `vite preview` |
| `npm run test` | `vitest run` |
| `npm run test:watch` | `vitest` |
| `npm run test:e2e` | `playwright test` |
| `npm run lint` | `oxlint` |
| `npm run lint:fix` | `oxlint --fix` |
| `npm run typecheck` | `tsc --noEmit` |
| `npm run gen:api` | `openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts` |
| `npm run gen:icons` | `node scripts/generate-icon-subset.mjs` |

类型检查用 `tsc --noEmit`，不是 Vue 版的 `vue-tsc`。

::: tip gen:icons 是自动跑的
`gen:icons` 扫 `src/**` 里出现过的图标名，连同 `scripts/icon-manifest.json` 里的种子，生成 `assets/icons.generated.json` 这份离线图标子集。`predev`/`prebuild` 已经把它挂在 `dev` 和 `build` 前面，正常开发不用手动跑。
:::

::: warning gen:api 需要后端正在运行
`gen:api` 要从一个真实运行中的后端拉 `/openapi/v1.json`，所以先启动后端（`dotnet run --project backend/samples/MinimalHost`，或直接跑 `dev.bat`）。生成的 `src/api/schema.d.ts` 禁止手改，下次跑 `gen:api` 就会被覆盖。
:::

仓库根目录还有两个批处理脚本，一次性管理整套服务：

| 脚本 | 作用 |
|---|---|
| `dev.bat` | 开三个窗口：后端（`:5100`）、`web`（Vue，`:5175`）、`web-react`（`:5174`），各自 `npm install && npm run dev` |
| `stop.bat` | 当前脚本处理 `5100`、`5173`、`5174`；若 Vue 的 `:5175` 仍在运行，请在启动它的终端结束进程 |

## 第一次运行的完成标准

浏览器应能打开 `http://localhost:5174`，登录后进入一个菜单页面；网络请求从 `:5174/api/...` 发出，实时通知通过 `:5174/hub` 建立；切换语言、主题或密度后刷新，选择仍应保留。开发控制台不应出现 StrictMode 引起的重复初始化错误。做到这些，入口装配、代理和 Zustand 持久化才算一起工作。

这套结构跑通之后，往下一页是[路由](/zh/frontend-react/routing)：后端菜单树怎么派生成路由表。再往后是[请求流程](/zh/frontend-react/request)，一次接口请求怎么走过类型化客户端。
