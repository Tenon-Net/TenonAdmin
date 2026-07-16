# 静态路由

`router/routes.ts` 只定义一棵顶层树:

```ts
export const staticRoutes: RouteRecordRaw[] = [
  { path: '/login', name: 'login', component: () => import('@/views/login/index.vue'), meta: { public: true } },
  { path: '/module', name: 'module', component: () => import('@/views/module/index.vue'), meta: { title: '选择应用' } },
  {
    path: '/',
    name: 'layout',
    component: () => import('@/layouts/default.vue'),
    children: [
      { path: '/personal/profile', name: 'personal-profile', component: namedPage('personal-profile', () => import('@/views/personal/profile.vue')) },
      { path: '/personal/password', name: 'personal-password', component: namedPage('personal-password', () => import('@/views/personal/password.vue')) },
      { path: '/personal/notice', name: 'personal-notice', component: namedPage('personal-notice', () => import('@/views/personal/notice.vue')) },
      { path: '/:pathMatch(.*)*', name: 'not-found', component: namedPage('not-found', () => import('@/views/error/404.vue')), meta: { public: true } },
    ],
  },
]
```

这里几处取舍都是刻意的:

- **`/` 不设静态 `redirect`。** `redirect` 在路由 resolve 阶段就求值,早于全局守卫——那时菜单树很可能还没建好,算出来的落点必然是错的。`/` 真正落到哪由 `router.beforeEach` 里的守卫决定(见下文[守卫](/zh/frontend/routing/guards)一节)。
- **404 挂在壳内,而非顶层。** 打错一个 URL 时侧边栏、标签栏、退出按钮照样在,不会把人甩到一个光秃秃的页面外面去。
- **`/personal/notice` 是静态路由,不是菜单项。** 它在后端走 `[ActiveSession]`(任何登录用户都能读,不需要具体权限码)——做成菜单意味着要播种它、再给每个角色都授权一遍,纯属多余功课。它的入口在顶栏通知铃铛的「查看全部」链接上。

**上一节:** [路由与动态菜单(概览)](/zh/frontend/routing/)
**下一节:** [动态路由:菜单树→真实路由](/zh/frontend/routing/dynamic)
