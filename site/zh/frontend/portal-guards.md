# 多应用门户与路由守卫

登录后进哪个应用，由一道阶梯逐级判定：记住的、唯一的、默认的，一个都不成立，才弹选择器让用户自己挑。一个用户可能同时被授权好几个应用（模块），进哪个不能写死，得按当前用户现算。

> 前置：先完成[入门教程](/zh/frontend/getting-started)，并保留「岗位预览」菜单。

## 先从三个入口进入同一页面

依次做三次：从侧栏点击「岗位预览」、复制 URL 后刷新、退出登录后把该 URL 直接贴回地址栏。前两次应回到同一页面；第三次先去登录，登录完成后再由当前应用的菜单恢复目标。若账号有多个应用，再从九宫格切换一次，确认不含该菜单的应用不会保留旧页面标签。

这三个入口分别触发普通导航、硬刷新重建和未登录拦截。下面的 `enterInitial` 与 `beforeEach` 就是在保证它们最后落到同一套授权后的菜单状态。

## 登录之后进哪个应用：enterInitial

TenonAdmin 的外壳是个多应用门户：每个用户被授权若干个应用，右上角有个九宫格选择器随时切换。登录后或硬刷新后，决定「直接进某个应用」还是「弹选择器」的，是 `composables/useModule.ts` 里的 `enterInitial()`：

```ts
async function enterInitial(): Promise<EnterResult> {
  const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
    personalApi.modules(),
    personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] as string[] })),
    personalApi.profile()
      .then((p) => ({ sadm: p.isSuperAdmin, avatar: p.avatar ?? null }))
      .catch(() => ({ sadm: useUserStore().userInfo?.isSuperAdmin ?? false, avatar: null })),
  ])
  auth.modules = modules
  auth.defaultModuleId = defaultModuleId ?? null
  auth.permissionCodes = perm.codes
  auth.permissionsLoaded = perm.ok
  auth.isSuperAdmin = profile.sadm
  const user = useUserStore()
  if (user.userInfo) user.userInfo.avatar = profile.avatar
  if (modules.length === 0) return { chooser: true }
  const remembered = auth.currentModuleId
  if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
  if (modules.length === 1) return enter(modules[0]!.id)
  if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
  return { chooser: true }
}
```

模块列表、权限码和个人资料并行拉取。`personalApi.permissions()` 失败时，`permissionsLoaded` 保持 `false`，`v-auth` 会隐藏普通权限按钮。`profile` 失败时沿用登录响应里的超管快照；没有快照才按普通用户处理。两种辅助请求失败都不会阻断门户，只会让前端使用已有的可信状态或更保守的默认值。服务端的 `sadm` claim 与权限过滤器仍然负责最终授权。

拉完这些数据，`enterInitial` 走一个「进哪个应用」的判定阶梯，自上而下，第一个命中的赢：

- **一个应用都没分配** → 弹选择器，选择页里显示一条「未分配应用」的空态提示。
- **有记住的应用，而且它还在你的应用列表里** → 直接进它。这个「记住的应用」是 `auth.currentModuleId`，也是 auth store 里唯一持久化的字段（`buildRoutesForModule` 每次进某个应用时把它写进去）。硬刷新或深链能落回上次那个应用，靠的就是这一条。
- **只有一个应用** → 直接进，没必要弹选择器。
- **配了默认应用**（`defaultModuleId`，可在选择页用 `setDefault` 设定）且它在列表里 → 直接进。
- **以上都不满足** → 弹选择器。

切换应用走另一条路径。`switchModule(moduleId)` 先重新调用 `enter()`，通过[路由与动态菜单](/zh/frontend/routing)中的 `buildRoutesForModule(moduleId)` 重建目标应用的动态路由；随后清空标签页 store，并把当前路由替换成目标应用的 `homePath`。`homePath` 优先取模块的 `defaultRoute`，没有则取菜单树的第一个叶子，再没有就回到 `/module`。没有菜单的应用不存在可进入的首页，返回选择器能避免跳到不属于该应用的路径。

## 守卫：每次导航都要过一遍 beforeEach

`router/index.ts` 的 `beforeEach` 就是把静态壳、动态路由和门户状态缝在一起的接缝：

```ts
router.beforeEach(async (to) => {
  const user = useUserStore()
  const auth = useAuthStore()

  if (!user.accessToken && (user.cookieSession || user.refreshToken)) {
    const ok = await ensureAccessToken()
    if (!ok) user.clear()
  }

  if (to.name === 'login') return user.isLoggedIn ? { path: '/', replace: true } : true
  if (to.meta.public && to.name !== 'not-found') return true
  if (!user.isLoggedIn) return { path: '/login', replace: true }

  if (user.userInfo?.mustChangePassword) {
    return to.path === '/personal/password' ? true : { path: '/personal/password', replace: true }
  }

  if (!auth.routesReady) {
    try {
      const { useModule } = await import('@/composables/useModule')
      const res = await useModule().enterInitial()
      if (res.chooser) return to.name === 'module' ? true : { path: '/module', replace: true }
      if (to.name === 'module') return true
      if (to.path === '/') return { path: auth.homePath, replace: true }
      const resolved = router.resolve(to.fullPath)
      if (resolved.name && resolved.name !== 'not-found') {
        return { name: resolved.name, params: resolved.params, query: to.query, hash: to.hash, replace: true }
      }
      return { path: to.path, query: to.query, hash: to.hash, replace: true }
    } catch {
      user.clear()
      return { path: '/login', replace: true }
    }
  }

  if (to.path === '/') return { path: auth.homePath, replace: true }
  return true
})
```

它按顺序处理五种状态：

**恢复 Cookie 会话。** Level 3 Cookie 会话的 access token 只在内存中，F5 后会消失。守卫先根据持久化的 `cookieSession` 标记，通过 HttpOnly refresh Cookie 静默换取新的 access，再判断是否登录。body 会话的令牌已经从本地存储水合，这一步会立即通过。

**公开页与登录跳转。** `/login`、OAuth 回调和 MFA 绑定/恢复页可以在动态路由未就绪时访问。已登录的人访问普通登录页会回到 `/`；带 `pendingLink` 或 `totpChallenge` 的登录流程会清理残留会话并留在登录页。catch-all 404 虽标记为 public，却不能提前放行，否则动态深链在重建前会被误判成 404。

**强制改密。** `mustChangePassword` 一旦为真，除了 `/personal/password` 本身，任何导航都被拦下、重定向到那里。这个标志是管理员建号或重置密码后首登带上的。这一判定刻意放在下面的动态路由重建**之前**。为什么？改密页是静态路由，不依赖菜单树就能渲染，先放行它，能避免「重建 → 选应用 → 又被弹回改密页」这种绕圈。改密成功后现有流程会强制登出重登，标志由后端清零。

**刷新 / 深链的重建。** 动态路由只存在于 router 内存。硬刷新或直接打开深链时，`routesReady` 为 `false`，守卫调用 `enterInitial()` 重建路由并填充模块数据。选择器结果落到 `/module`，根路径落到 `homePath`。普通深链则用更新后的 matcher 执行 `router.resolve(to.fullPath)`；若匹配到真实动态路由，就按路由名重新进入，否则按原路径重试。这样不会沿用本次导航开始时解析出的 catch-all 记录。重建失败会清空登录态并返回登录页。

根路径不能按自身重试，否则 Vue Router 会判成无限重定向。公开页判断也不能无条件放行 `not-found`，因为它可能只是动态路由恢复前临时命中的 catch-all。

**`/` 永远落到 `auth.homePath`。** 路由已经就绪的正常导航里，访问 `/` 同样交给守卫算首页。这条判断不能写成 `layout` 路由上的静态 `redirect`，原因和上面一样。`redirect` 在 resolve 阶段求值，早于这个守卫。那时候菜单树还没准备好，`homePath` 自然也没有，算出来的落点必然是错的。

导航确认之后，`afterEach` 把访问过的页面记成标签，但有三类要跳过：标了 `meta.public` 的页面、`login`/`module`/`not-found` 这三个固定名字、还有没挂在 `layout` 下的路由。最后这一类不属于任何应用的工作区，不该在标签栏留痕：

```ts
router.afterEach((to) => {
  if (to.meta.public) return
  if (['login', 'module', 'not-found'].includes(to.name as string)) return
  if (!to.matched.some((r) => r.name === 'layout')) return
  useTabsStore().addTab(to)
})
```

## 用四种入口验收守卫

守卫改动后至少走四条路径：未登录时直达业务深链应回到登录页；被要求改密的账号只能进入密码页；只有一个应用的账号登录后应直接进入该应用；有多个应用且没有可用默认项时应停在选择器。最后在一个动态页面上刷新浏览器，页面应在路由重建后回到原地址，而不是先落到 404。这样能同时覆盖登录态、强制改密、应用选择和深链恢复。

要是你找的是动态路由本身怎么从菜单树长出来的，去[路由与动态菜单](/zh/frontend/routing)。`buildRoutesForModule` 怎么把每个菜单节点的 `component` 字符串换成真实的懒加载组件，`namedPage` 又怎么给它一个稳定身份、好让 `keep-alive` 认得出，都在那页。门户「进哪个应用」的决策，还有守卫每次导航时怎么把它调进来，到这里就讲完了。
