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

**上一节:** [多应用门户](/zh/frontend/routing/portal)
**下一节:** [Keep-Alive 与具名组件](/zh/frontend/routing/keep-alive)
