# 多应用门户与路由守卫

一个用户可能同时被授权好几个应用（模块），登录之后到底该把他直接丢进某个应用，还是先弹个选择器让他自己挑?这一页讲清楚两件互相咬合的事：门户怎么做这个决定，以及守卫怎么在每次导航时把静态壳、动态路由和门户状态缝到一起。

## 登录之后进哪个应用：enterInitial

TenonAdmin 的外壳是个多应用门户：每个用户被授权若干个应用，右上角有个九宫格选择器随时切换。登录后或硬刷新后，决定"直接进某个应用"还是"弹选择器"的，是 `composables/useModule.ts` 里的 `enterInitial()`:

```ts
async function enterInitial(): Promise<EnterResult> {
  const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
    personalApi.modules(),
    personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] as string[] })),
    personalApi.profile().then((p) => ({ sadm: p.isSuperAdmin })).catch(() => ({ sadm: false })),
  ])
  auth.modules = modules
  auth.defaultModuleId = defaultModuleId ?? null
  auth.permissionCodes = perm.codes
  auth.permissionsLoaded = perm.ok
  auth.isSuperAdmin = profile.sadm
  if (modules.length === 0) return { chooser: true }
  const remembered = auth.currentModuleId
  if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
  if (modules.length === 1) return enter(modules[0]!.id)
  if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
  return { chooser: true }
}
```

模块列表、权限码、超管标记三者并行拉取。后两者都是失败即收紧：`personalApi.permissions()` 一旦失败，`permissionsLoaded` 就留在 `false`,`v-auth` 指令把它当成「藏起来」，而不是在拿不准的情况下放行;`profile` 拉不到就按普通用户处理，不会把谁误当成超管。这一步不阻断进门户——权限拿不到你照样能进，只是所有按钮先按"没权限"处理。

拉完这些数据，`enterInitial` 走一个"进哪个应用"的判定阶梯，自上而下，第一个命中的赢：

- **一个应用都没分配** → 弹选择器，选择页里显示一条"未分配应用"的空态提示。
- **有记住的应用，而且它还在你的应用列表里** → 直接进它。这个"记住的应用"是 `auth.currentModuleId`，也是 auth store 里唯一持久化的字段（`buildRoutesForModule` 每次进某个应用时把它写进去）。硬刷新或深链能落回上次那个应用，靠的就是这一条。
- **只有一个应用** → 直接进，没必要弹选择器。
- **配了默认应用**（`defaultModuleId`，可在选择页用 `setDefault` 设定）且它在列表里 → 直接进。
- **以上都不满足** → 弹选择器。

切换应用走的是另一条路。`switchModule(moduleId)` 重新 `enter()` 一次（重建那个应用的动态路由）、清空标签页 store（换了应用，标签栏理应从零开始）、再把当前路由替换成新应用自己的 `homePath`。`homePath` 是 auth store 的一个 getter：优先取模块自己的 `defaultRoute`，没有就退到菜单树的第一个叶子，再没有就兜底回 `/module`——一个菜单都没配的应用根本没有首页可言，把人送回选择器，好过让他撞上一个不属于本应用的路径吃 404。

## 守卫：每次导航都要过一遍 beforeEach

`router/index.ts` 的 `beforeEach` 就是把静态壳、动态路由和门户状态缝在一起的接缝：

```ts
router.beforeEach(async (to) => {
  const user = useUserStore()
  const auth = useAuthStore()

  if (to.name === 'login') return user.isLoggedIn ? { path: '/', replace: true } : true
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
      return to.fullPath
    } catch {
      user.clear()
      return { path: '/login', replace: true }
    }
  }

  if (to.path === '/') return { path: auth.homePath, replace: true }
  return true
})
```

它按顺序把四件事料理掉：

**登录跳转。** 已登录的人再访问 `/login` 会被弹回 `/`;未登录的人访问除 `/login` 外的任何地方，都会被送去 `/login`。`/login` 是唯一免认证页。

**强制改密。** `mustChangePassword` 一旦为真（管理员建号或重置密码后首登会带上它），除了 `/personal/password` 本身，任何导航都被拦下重定向到那里。这一判定刻意放在下面的动态路由重建**之前**：改密页是静态路由，不依赖菜单树就能渲染，先放行它，避免"重建 → 选应用 → 又被弹回改密页"这种绕圈。改密成功后现有流程会强制登出重登，标志由后端清零。

**刷新 / 深链的重建。** 动态路由只活在 router 的内存路由表里，不持久化。硬刷新或直接打开一条深链时，`auth.routesReady` 必然是 `false`，任何 `menu-{id}` 路由都还没注册。守卫检测到这一点，`enterInitial()` 就在这里被调进来——它既重建路由，又填好 `auth.modules`，门户的判定和守卫的重建其实共用同一次调用。拿到结果后守卫再决定去向：结果是选择器 → 去 `/module`（本来就去 `/module` 就直接放行，因为渲染选择页要的数据这时已经齐了，不能弹回 `/`，否则默认应用一旦设定就再没入口去改它）;目标是 `/` → 直接给出 `auth.homePath`;其余情况 → 重新返回 `to.fullPath`，让同一个 URL 在路由建好之后再解析一次。要是 `enterInitial()` 抛错，直接清空登录态送回 `/login`，不把用户晾在一个搭了一半的页面上。

这里有两个不返回 `to.fullPath` 的位置值得留意。目标是 `/` 时不能返回 `to.fullPath`——那等于重定向到自身，而 `/` 已经没有静态 `redirect` 了，Vue Router 会判成无限重定向。另一个坑更隐蔽：这段重建逻辑不能用 `to.meta.public` 提前短路，因为一条还没注册的动态路由会先命中 catch-all(404)，它带着 `public` 标记——真按 `public` 放行，用户看到的就是一个错误的 404，而不是重建后的正确页面。

**`/` 永远落到 `auth.homePath`。** 路由已经就绪的正常导航里，访问 `/` 同样交给守卫算首页。这条判断出于和上面一样的原因不能写成 `layout` 路由上的静态 `redirect`——`redirect` 在 resolve 阶段求值，早于这个守卫，那时菜单树（进而 `homePath`）还没准备好，算出来的落点必然是错的。

导航确认之后，`afterEach` 把访问过的页面记成标签，但跳过三类：标了 `meta.public` 的页面、`login`/`module`/`not-found` 这三个固定名字，以及没挂在 `layout` 下的路由（它们不属于任何应用的工作区，不该在标签栏留痕）:

```ts
router.afterEach((to) => {
  if (to.meta.public) return
  if (['login', 'module', 'not-found'].includes(to.name as string)) return
  if (!to.matched.some((r) => r.name === 'layout')) return
  useTabsStore().addTab(to)
})
```

动态路由本身是怎么从菜单树长出来的——`buildRoutesForModule` 如何把每个菜单节点的 `component` 字符串换成真实的懒加载组件、`namedPage` 又如何给它一个稳定身份好让 `keep-alive` 认得出——是[路由与动态菜单](/zh/frontend/routing)那一页的事;这一页只管门户"进哪个应用"的决策，和守卫在每次导航时怎么把它调进来。
