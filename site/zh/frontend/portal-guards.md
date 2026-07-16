# 多应用门户

TenonAdmin 的外壳是个多应用门户——一个用户可能属于好几个应用(模块),从一个选择器里切来切去。`useModule().enterInitial()`(定义在 `useModule.ts`)就是登录后或硬刷新时,决定直接把用户丢进某个应用还是弹出选择器的那个函数:

```ts
async function enterInitial(): Promise<EnterResult> {
  const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
    personalApi.modules(),
    personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] })),
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

模块列表、权限码、profile 三者并行拉取。权限码和超管标记都是失败即收紧:`personalApi.permissions()` 一旦失败,`permissionsLoaded` 就留在 `false`,`v-auth` 指令把它当成「藏起来」而不是在拿不准的情况下放行。

进门户的判定顺序:

1. **没分配任何应用** → 弹选择器(带一个空态提示)。
2. **有记住的应用**(`auth.currentModuleId`,auth store 里唯一持久化的字段)且它还在用户的应用列表里 → 直接进它。硬刷新或深链能落回原来那个应用,靠的就是这一条。
3. **只有一个应用** → 直接进,不用弹选择器。
4. **配置了默认应用**(`defaultModuleId`,可通过 `setDefault` 设置)→ 直接进。
5. **以上都不满足** → 弹选择器。

`switchModule(moduleId)` 会重新走一遍 `enter()`、清空标签页 store(换了应用,标签栏理应从零开始)、再把当前路由替换成新应用自己的 `homePath`。



---

<!-- TODO(rewrite): merged from guards.md -->

# 路由守卫

`router/index.ts` 的 `beforeEach` 把静态和动态两侧缝在一起:

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

- **登录跳转** —— 已登录的人再访问 `/login` 会被弹回 `/`;未登录的人访问除 `/login` 外的任何地方都会被送去 `/login`。
- **强制改密** —— `mustChangePassword` 一旦为真,除了 `/personal/password` 本身,任何导航都会被拦下重定向到那里。这一判定放在下面的动态路由重建**之前**,因为改密页是静态路由,不依赖菜单树就能渲染。
- **刷新 / 深链的重建逻辑。** 动态路由只活在 router 的内存路由表里,不会被持久化。硬刷新或直接打开一条深链,`auth.routesReady` 必然是 `false`,任何 `menu-{id}` 路由都还没注册。守卫检测到这一点,调用 `enterInitial()` 重建,再重新判定去向:选择器结果 → `/module`;本来就是去 `/module` → 放行;目标是 `/` → 直接给出 `auth.homePath`(这里不能返回 `to.fullPath`,因为那等于重定向到自身——`/` 已经没有静态 `redirect` 了,Vue Router 会判成无限重定向);其余情况 → 重新返回 `to.fullPath`,让同一个 URL 在路由已经建好之后再解析一次。如果 `enterInitial()` 抛错,直接清空登录态送回 `/login`,不会把用户晾在一个搭了一半的页面上。
- **`/` 永远落到 `auth.homePath`。** 这条判断出于同样的原因不能写成 `layout` 路由上的静态 `redirect`——`redirect` 求值早于这个守卫,那时菜单树(进而 `homePath`)还没准备好。

`afterEach` 把访问过的页面记成标签,跳过标了 `meta.public` 的页面、`login`/`module`/`not-found` 这三个固定名字,以及没挂在 `layout` 下的路由:

```ts
router.afterEach((to) => {
  if (to.meta.public) return
  if (['login', 'module', 'not-found'].includes(to.name as string)) return
  if (!to.matched.some((r) => r.name === 'layout')) return
  useTabsStore().addTab(to)
})
```

