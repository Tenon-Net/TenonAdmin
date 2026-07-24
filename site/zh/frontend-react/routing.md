# 路由与动态菜单

web-react 的路由表是一个从菜单树派生出来的普通数组，交给 `useRoutes` 渲染。菜单树一变，React 重渲染、重新匹配路由，用不着命令式 `addRoute`，也没有 Vue 版重解析当前 URL 那一步。路由有两个源：构建期写死的静态壳，和登录后按菜单树派生的动态路由。

门户凭什么决定进哪个应用、守卫怎么把两套路由缝起来，都归[多应用门户与守卫](/zh/frontend-react/portal-guards)，这页不碰。

```text
静态路由(App.tsx / Protected.tsx)          动态路由(buildRoutes ← menuTree)
  ├─ /login  /oauth/callback  公开           useRoutes([ ...buildRoutes(menuTree) ])
  └─ /*  → <Protected>                        对每个 Menu 节点:
        ├─ /module  选择器,壳外                 component 字符串 → /src/views/**/*.tsx
        └─ <LayoutShell>                          menuToRouteDescriptors  决策哪些节点
              ├─ ...buildRoutes(menuTree)          buildRoutes             落成 RouteObject
              ├─ /personal/*  5 个静态页
              ├─ /  → <Navigate to={home}>
              └─ *  → <NotFoundPage>

构建期写死,部署间不变。                       menuTree 一变(登录/切应用/F5)即重新派生。
```

「派生」这个词是关键。Vue 那边动态路由靠 `router.addRoute` 一条条命令式挂上去，重建后当前 URL 还得手动重解析。React 这边路由是 `menuTree` 的纯函数结果，`useRoutes(routes)` 随 `menuTree` 变化自动重新匹配，不存在「路由挂上了、当前 URL 却没重新匹配」的空窗。

## 静态路由

静态路由分两层。最外层在 `App.tsx`：`/login` 和 `/oauth/callback` 是公开页，其余全部 `/*` 交给 `<Protected>`。受保护区里再排一层：

```tsx
useRoutes([
  // 选择器全屏,不进布局壳
  { path: '/module', element: <ModuleChooser /> },
  {
    element: <LayoutShell />,
    children: [
      ...buildRoutes(menuTree),            // 菜单派生的动态路由
      { path: '/personal/profile',  element: <ProfilePage /> },
      { path: '/personal/password', element: <PasswordPage /> },
      { path: '/personal/notice',   element: <NoticePage /> },
      { path: '/personal/sessions', element: <SessionsPage /> },
      { path: '/personal/bindings', element: <BindingsPage /> },
      { path: '/', element: <Navigate to={home} replace /> },   // home 随 menuTree 派生
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
```

几处取舍是刻意的：

- **`/` 不写死跳转目标。** 落点是 `<Navigate to={home}>`，`home` 由 `homePath(authStore)` 经 `useMemo` 依赖 `menuTree` 算出来。菜单树没就绪时 `home` 回落 `/module`，就绪后自动指向当前应用首页。写死一个静态 `redirect` 会在菜单树还没建好时就算错落点。
- **404 挂在壳内。** `*` 是 `<LayoutShell>` 的子路由，打错一个 URL 时侧边栏、标签栏、退出按钮照样在，不会把人甩到一个光秃秃的页面外面去。
- **`/module` 排在动态 `*` 前面能赢，靠的不是数组顺序。** react-router 按路由特异性排名裁决，静态段 `/module` 天然比通配 `*` 特异，挪到数组末位仍然赢（实测过）。写在最前只是给人读的。
- **personal 五页是静态路由，不是菜单项。** 它们在后端都走 `[ActiveSession]`，任何登录用户都能读，不需要具体权限码。做成菜单反而得先种子它、再逐个角色授权，纯属多余功课。入口在顶栏用户下拉和通知铃铛，不在侧边栏菜单里。

## 动态路由：菜单树 → 真实路由

`<LayoutShell>` 下除了那五个静态个人页和 404 兜底，其余全部来自 `buildRoutes(menuTree)`。这个函数把菜单树拍平，对每个 `type` 为 `MenuType.Menu` 的节点算出一条路由。目录 `Catalog` 只组织层级、不落地成页，按钮 `Button` 不是路由，两者都跳过。约定很直接：菜单的 `component` 字段就是相对 `src/views` 的文件路径去掉 `.tsx` 后缀。比如 `system/user/index` 对应 `/src/views/system/user/index.tsx`。

### 决策与落地分成两层

`buildRoutes` 内部拆成两块，各管一件事：

```ts
// menuRoutes.ts —— 决策:哪些节点建路由、建成什么(view / iframe / missing)
menuToRouteDescriptors(tree, hasView): RouteDescriptor[]

// buildRoutes.tsx —— 落地:描述符 → react-router 的 RouteObject
//   view    → React.lazy + Suspense 包的页面
//   iframe  → 通用 IframeView
//   missing → 可诊断的 MissingRoute
```

分开是为了让决策能脱离 react-router 单测。`menuToRouteDescriptors` 是动态路由里唯一有真实分支的一块，它不直接摸 `import.meta.glob` 和 `console`：判断组件在不在的 `hasView`、组件缺失时告警的 `warn` 都从参数注入。这样「缺组件要不要留一条路由、要不要告警」这条分支才断言得了。

### 组件路径就是文件路径，下拉不靠手敲

能落地成页面的组件由 `import.meta.glob('/src/views/**/*.tsx')` 收集成一张映射表。`component` 字段拿去比对这张表，命中就取它的懒加载 loader。管理员配菜单时不用手敲这个路径：`buildRoutes.tsx` 导出 `viewComponentPaths`，把 glob 表里每个合法键反推成同样格式的 `component` 字符串，喂给菜单管理表单的「组件路径」下拉。下拉从 glob 反推，天然不会和真实文件漂移。

### 组件缺失：留一条可诊断的路由

`component` 在 glob 表里找不到时，这里和 Vue 的处理不同。Vue 直接把这条菜单项丢掉、不注册路由，点进去是 404，管理员看不出错在哪。React 这边照样建一条路由，只是渲染成 `MissingRoute`：页面上一行 `role="alert"` 的告警，写明缺的是哪个组件。两边都会打一条 `console.warn`。留一条看得见的诊断，比静默消失一个菜单项好排查。配菜单时用上面那个下拉，从一开始就敲不错。

### glob 排除表：静态页别混进动态 import

那张 glob 有一串排除项，不是随手加的：

```ts
import.meta.glob([
  '/src/views/**/*.tsx',
  '!/src/views/**/*.spec.tsx',
  '!/src/views/login/**',    '!/src/views/module/**',
  '!/src/views/oauth/**',    '!/src/views/error/**',
  '!/src/views/embed/**',    '!/src/views/personal/**',
  '!/src/views/_placeholder/**',
])
```

被排掉的这几类都在别处被**静态** import：登录页、应用选择页、OAuth 回调、404、个人五页都是静态路由直接 import，iframe 视图由 `buildRoutes` 静态 import，`_placeholder` 是内部占位。留在 glob 里会有两个后果：同一个文件既被静态又被动态 import，Vite 没法 code-split（build 告警就是这么来的）；管理员还会在「组件路径」下拉里选到一个坏页（iframe 没有 src、占位页是空壳）。加静态路由页时要顺手把它排进来，否则这个冲突会复发。

## 外链与内嵌页：没有新的菜单类型

`MenuType` 只有 `Catalog`/`Menu`/`Button` 三种。外链和 iframe 内嵌都没有新增枚举值，而是复用既有的 `Path`/`Component` 两个字段，靠 `isHttpUrl()`（判断字符串是不是 `http(s)://` 开头）区分意图。

| 想要的效果 | 怎么配 | 运行时怎么处理 |
| --- | --- | --- |
| 外链菜单 | `Path` 填完整 URL，`Component` 留空 | `buildRoutes` 跳过，不建路由；菜单里照常显示，点击由布局侧 `window.open` |
| 内嵌 iframe 菜单 | `Path` 填内部路径，`Component` 填完整 URL | 建一条通用 `IframeView` 路由，`Component` 里的 URL 当 `src` 传进去 |

外链这一条有个不对称：`buildRoutes` 把外链跳过不建路由，`menuItems` 却照常把它放进菜单、`key` 就是那个 URL。理由是外链要在菜单里看得见、点得动，但它不该占一条内部路由。点击时靠 `isHttpUrl(key)` 认出来，`window.open` 新标签打开。

iframe 这边 React 省了 Vue 的一处小心思。Vue 版把 URL 存进 `route.meta.iframeSrc`，还得在 `setup` 里只取一次快照，防止 `keep-alive` 缓存复用时 `src` 被响应式重算成空。React 这边 `src` 是建路由时就钉死在 `<IframeView src={...} />` 上的 prop，缓存命中时元素原样复用、prop 不会变，没有那个响应式重算的口子要堵。

## 页面缓存：手写的 keep-alive

React 没有 Vue `<keep-alive>` 的对等物，`KeepAliveOutlet` 是手搓的一套。它拿一个 `Map<path, 元素>` 把「该缓存」的已开标签常驻挂载，非活动页用 `display:none` 藏起来、不卸载。切走再切回，组件树还在，状态、滚动位置、没提交的表单都保留。`noCache` 的页（详情等瞬时页）不进这个 Map，走 live 渲染：离开即卸载，复访重挂、重新拉数据。标签被关掉时，它的缓存条目按 `aliveKeys` 逐出。

和 Vue 最大的差别在「按什么匹配缓存」。Vue 的 `<keep-alive :include>` 按组件的 `name` 匹配，而 `src/views/**` 下几十个 `index.vue` 推断出的 `name` 会撞车、也对不上路由名，Vue 只好用 `namedPage` 给每个页面补一个等于路由名的显式 `name`。React 这边缓存直接按路由 `path` 存进 `Map`，`path` 本来就唯一，那套 `namedPage` 的具名机制整个用不上。

刷新一个标签（`refreshTab`）要强制重挂，光删缓存不够。store 置一个 `excludeKey` 并递增 `reloadKey`，`KeepAliveOutlet` 监听到就给这个 path 的 div `key` 换一个版本号，再删它的缓存条目。`key` 一变 React 才会卸载重挂；只删 Map、`key` 不变的话，React 会复用同一个 fiber，状态清不掉。

页面切换的入场动画只挂在当前可见的那页上，用 CSS `animation` 不用 `transition`。div 从 `display:none` 变回 `block` 时 `animation` 会重跑，`transition` 跨 `display` 切换不触发，所以切走切回也有入场效果，且全程不 remount、不动缓存。

## 没有约定式详情路由

Vue 版有一条约定：`views/**/detail.vue` 会自动生成 `/<模块>/:id/detail` 路由。**web-react 这一版没有对应机制。** 路由目录里没有 `registerDetailRoutes` 之类的扫描，详情不靠约定路由，而是在列表页里就地用弹层或抽屉展开（`system/user` 编辑用 Modal，`system/log` 用 Drawer）。真需要一条带参数的独立详情页时，按普通页面显式配置，别指望约定帮你生成。

::: tip 这两样东西不在这里
路由链路里没有进度条（不用 NProgress 或类似的库）。`document.title` 也不由守卫按导航设，只在 `App` 启动拉站点配置时设一次，站点标题改了再由系统配置页设一次，不随每次导航联动。
:::

想从零把一条链路走通，先看[目录与装配](/zh/frontend-react/structure)；两个模板的整体对照在[前端模板](/zh/guide/frontend-templates)。
