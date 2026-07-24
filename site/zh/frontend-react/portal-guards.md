# 多应用门户与路由守卫

整个受保护区归一个常驻组件 `Protected` 看管。它不像 Vue 那样每次导航拦一道 `beforeEach`，而是在渲染时按登录态、改密态、路由就绪态逐级短路，动态路由则随 `menuTree` 反应式重匹配。登录后进哪个应用，走一道 `enterInitial` 的判定阶梯：记住的、唯一的、默认的，都不成立才弹选择器。

## 登录后进哪个应用：enterInitial 的阶梯

`composables/useModule.ts` 里的 `enterInitial()` 决定登录或硬刷新后是「直接进某个应用」还是「弹选择器」。它写成模块级的 async 函数，不是 hook：守卫和选择页都要调它，不该绑在某个组件的生命周期上。

进门户先并行拉模块列表、权限码和个人资料。

```ts
const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
  personalApi.modules(),
  personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] as string[] })),
  personalApi.profile().then((p) => ({ sadm: p.isSuperAdmin, avatar: p.avatar ?? null })).catch(() => ({ sadm: false, avatar: null })),
])
```

后两样都是失败即收紧。权限码一旦拉失败，`permissionsLoaded` 就留在 `false`，`hasPerm` 于是把每个按钮当「没权限」藏起来，而不是拿不准时放行。资料拉不到就按普通用户处理，不会把谁误当超管。这一步不阻断进门户。权限拿不到你照样进得去，只是除超管外的按钮先都按「没权限」处理。超管例外，它走 `hasPerm` 里的 fail-open 分支，最后有服务端的 `sadm` 兜底。

数据到手，`enterInitial` 走一道判定阶梯，自上而下第一个命中的赢：

```ts
if (modules.length === 0) return { chooser: true }
const remembered = useAuthStore.getState().currentModuleId
if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
if (modules.length === 1) return enter(modules[0]!.id)
if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
return { chooser: true }
```

- **一个应用都没分配** → 弹选择器，选择页显示一条「未分配应用」的空态。
- **有记住的应用，而且它还在列表里** → 直接进。`remembered` 是 `auth.currentModuleId`，auth store 里唯一持久化的字段（`enter` 每次进应用时写它）。硬刷新或深链能落回上次那个应用，靠的就是这一条。但它必须校验仍在列表里：应用被收回权限后，记住的 id 会指向一个不存在的应用。
- **只有一个应用** → 直接进，没必要弹。
- **配了默认应用**（`defaultModuleId`，在选择页用 `setDefault` 设定）且它在列表里 → 直接进。
- **以上都不满足** → 弹选择器。

`enter(moduleId)` 干的事只有一件：拉那个应用的菜单树，写进 auth store。

```ts
export async function enter(moduleId: number): Promise<EnterResult> {
  const tree = await personalApi.menu(moduleId)
  useAuthStore.setState({ menuTree: tree, currentModuleId: moduleId, routesReady: true })
  return { chooser: false, moduleId }
}
```

它不碰路由表。路由由 `buildRoutes(menuTree)` 经 `useRoutes` 反应式派生，`menuTree` 一变，当前 URL 自然重新匹配。菜单树怎么长成路由，见[路由与动态菜单](/zh/frontend-react/routing)。

`enterInitial` 还包了一层在途去重。React StrictMode 下守卫的 effect 会挂载两次（挂载、卸载、再重挂），加上多个组件可能同时触发，`enterInitial` 会被并发调用好几次。缓存在途的 Promise 让这些调用合流到同一次门户拉取，settle 后清空，下次硬刷新照常重拉。

```ts
let inflight: Promise<EnterResult> | null = null
export function enterInitial(): Promise<EnterResult> {
  inflight ??= doEnterInitial().finally(() => { inflight = null })
  return inflight
}
```

## 守卫是一个常驻组件，不是导航钩子

Vue 那边守卫是 `router.beforeEach`，每次导航跑一遍、返回一个重定向目标。React 这边是 `Protected`：整个受保护区一个组件，常驻挂载，不拦导航。它在渲染时按顺序处理三道短路，第一个命中的直接决定渲染什么。

```tsx
if (!loggedIn) return <Navigate to="/login" replace />
if (mustChange) {
  return location.pathname === '/personal/password' ? lazyEl(PasswordPage) : <Navigate to="/personal/password" replace />
}
if (!routesReady && !booted) return <Spin />   // enterInitial 在途,转圈
return <DynamicRoutes />
```

**未登录** → 去 `/login`。`/login` 和 OAuth 回调是 `App` 顶层的公开静态路由，压根不进 `Protected`。

**强制改密** → 锁死改密页，其它一切导航都弹回 `/personal/password`。这个判定刻意排在路由重建之前。改密页是静态路由，不靠菜单树就能渲染；先放行它，免得陷进「重建后又被弹回改密页」的循环。

**路由未就绪** → 转圈，等 `enterInitial`。动态路由只活在内存里，不持久化。硬刷新或直开深链时，`routesReady` 必然是 `false`，任何 `menu-{id}` 路由都还没派生出来。重建由一个 effect 触发，不是导航钩子：`Protected` 一挂载，发现 `!routesReady && !booted`，就调一次 `enterInitial()` 把菜单树填回 store。

这里的 `booted` 是个只跑一次的本地标志，不能拿 `routesReady` 顶替。`routesReady` 只在 `enter()` 成功时转真，而选择器态它永远是 `false`。真拿 `routesReady` 当「要不要重跑 `enterInitial`」的判据，选择器态就会无限重跑，打成打点风暴。所以另立 `booted`：不管结果是进应用还是弹选择器，引导只跑这一次。`enterInitial` 抛错就清掉会话，下一帧回落 `/login`，不把人晾在半搭的页面上。

引导过后渲染 `DynamicRoutes`。它读 `menuTree` 和 `homePath`，用 `useRoutes` 把路由铺开：

```tsx
const routes = useMemo(() => [
  { path: '/module', element: <ModuleChooser /> },   // 全屏,不进布局壳
  {
    element: <LayoutShell />,
    children: [
      ...buildRoutes(menuTree),           // 菜单派生的动态路由
      // …个人中心五页(静态)…
      { path: '/', element: <Navigate to={home} replace /> },
      { path: '*', element: lazyEl(NotFoundPage) },
    ],
  },
], [menuTree, home])
return useRoutes(routes)
```

这套反应式派生正是 React 侧不需要 Vue 那个 `return to.fullPath` 重解析 trick 的原因。Vue 的动态路由是命令式 `addRoute` 挂上去的，挂完当前 URL 还停在旧的匹配结果上，得手动重解析一次。React 这边路由是从 `menuTree` 算出来的普通数组，`menuTree` 一变 React 就重渲染、`useRoutes` 重新匹配，没有「挂了但没匹配」的空窗。

选择器态也因此不需要单开一条分支。`enter` 没被调，`menuTree` 就一直是空，`buildRoutes` 派生不出任何动态路由，`homePath` 于是一路回落到 `/module`，由 `/module` 渲染 `ModuleChooser`。空的 `menuTree` 加 `homePath` 的兜底，天然就表达了「弹选择器」。`homePath` 的兜底顺序是：先取模块自己的 `defaultRoute`，没有就退到菜单树第一个叶子，再没有就回 `/module`。一个菜单都没配的应用没有首页可言，把人送回选择器，好过让他撞上一个不属于本应用的路径吃 404。

`/module` 是静态段，壳内还有个 catch-all `*`。两者谁赢由 react-router 按路由特异性裁决，跟数组顺序无关，静态段 `/module` 稳赢，写在最前只是为了好读。

## 切换应用与标签清空

应用内右上角的九宫格随时切换应用，走的是 `switchModule`，跟登录进门户是两条路。

```ts
export async function switchModule(moduleId: number): Promise<string> {
  await enter(moduleId)
  useTabsStore.getState().clearTabs()
  return homePath(useAuthStore.getState())
}
```

`switchModule` 做三件事。`enter()` 重建目标应用的动态路由，`clearTabs()` 清空标签（旧应用的标签在新应用里都是死链），最后返回新应用的 `homePath`。导航这一步交给调用方：选择页组件手里才有 router 上下文，`useModule` 保持 router-free 才好单测。落地后，新应用的首页会被标签同步自然补成第一个标签。

标签清空只发生在切应用（`switchModule`）和登出、换号（auth store 的 `reset`）时，别处都不清。标签的写入不在守卫里，而在 `layouts/useTabSync.ts`：它盯着当前 pathname，变一次就查菜单或个人页的元数据补一个标签。`/module` 在壳外、没有标题来源，404 也一样，都不建标签。这也是 Vue `router.afterEach(addTab)` 在 React 侧的替身：Vue 用导航钩子记标签，React 用一个订阅 location 的 effect。

按钮级权限怎么用 `hasPerm` 和 `<Can>` 门控，见[权限与按钮门控](/zh/frontend-react/permission)。
