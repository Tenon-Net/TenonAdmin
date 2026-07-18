# 路由与动态菜单

你在菜单管理里加一条菜单、填好组件路径、保存，它就成了一个能点进去的页面——这中间没有一张你手写的路由表。前端的路由来自两个互不相干的源：构建期就定死的**静态壳**，和登录后按当前应用的菜单树在运行时重建出来的**动态路由**。

门户怎么决定进哪个应用、守卫怎么把这两侧缝起来，这页不讲，那是[多应用门户与守卫](/zh/frontend/portal-guards)的事。

```text
staticRoutes(router/routes.ts)          buildRoutesForModule(useAuthMenu.ts)
  ├─ /login                                拉 personalApi.menu(moduleId)
  ├─ /module(应用选择器)                    拍平菜单树
  └─ /  → layout(default.vue)              对每个 Menu 类型节点:
        ├─ /personal/profile                 component 字符串 → /src/views/**/*.vue
        ├─ /personal/password                 router.addRoute('layout', { name: 'menu-{id}', ... })
        ├─ /personal/notice
        ├─ /personal/sessions
        └─ /:pathMatch(.*)*(404)

构建期定死,部署间不变。                   登录 / 切应用 / 硬刷新时各重建一次。
```

静态那一侧在两次部署之间从不变化。动态那一侧完全由当前选中的应用返回的菜单树决定。所以不同用户、不同角色、不同应用，挂在 `layout` 下的那批路由都各不相同。

## 静态路由

`router/routes.ts` 只定义一棵顶层树：

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
      { path: '/personal/sessions', name: 'personal-sessions', component: namedPage('personal-sessions', () => import('@/views/personal/sessions.vue')) },
      { path: '/:pathMatch(.*)*', name: 'not-found', component: namedPage('not-found', () => import('@/views/error/404.vue')), meta: { public: true } },
    ],
  },
]
```

这里几处取舍都是刻意的：

- **`/` 不设静态 `redirect`。** `redirect` 在路由 resolve 阶段就求值，早于全局守卫。那时菜单树很可能还没建好，算出来的落点必然是错的。`/` 真正落到哪由 `router.beforeEach` 里的守卫决定（见[多应用门户与守卫](/zh/frontend/portal-guards)）。
- **404 挂在壳内，而非顶层。** 打错一个 URL 时侧边栏、标签栏、退出按钮照样在，不会把人甩到一个光秃秃的页面外面去。
- **`/personal/notice` 和 `/personal/sessions` 是静态路由，不是菜单项。** 两者在后端都走 `[ActiveSession]`（任何登录用户都能读，不需要具体权限码）。做成菜单反而意味着要播种它、再给每个角色都授权一遍，纯属多余功课。入口分别在顶栏通知铃铛的「查看全部」链接和顶栏用户下拉里。

## 动态路由：菜单树→真实路由

`layout` 下除了那四个静态个人页和 404 兜底，其余全部来自 `useAuthMenu.ts` 的 `buildRoutesForModule`：

```ts
const views = import.meta.glob('/src/views/**/*.vue') as Record<string, () => Promise<Component>>

export async function buildRoutesForModule(moduleId: number): Promise<void> {
  const auth = useAuthStore()
  const tree = await personalApi.menu(moduleId)
  auth.menuTree = tree
  auth.currentModuleId = moduleId

  resetRouter()
  for (const node of flatten(tree)) {
    if (node.type !== MenuType.Menu || !node.component || !node.path) continue
    const key = `/src/views/${node.component.replace(/^\/+/, '')}.vue`
    const loader = views[key]
    if (!loader) {
      console.warn('[menu] 缺少视图组件:', node.component, '→', key)
      continue
    }
    const name = `menu-${node.id}`
    if (router.hasRoute(name)) router.removeRoute(name)
    router.addRoute('layout', {
      path: node.path.startsWith('/') ? node.path : `/${node.path}`,
      name,
      component: namedPage(name, loader),
      meta: { title: node.title, icon: node.icon, keepAlive: true },
    })
    registerDynamic(name)
  }
  auth.routesReady = true
}
```

整条链路是：拉当前应用的菜单树（`personalApi.menu(moduleId)`）、拍平成一维数组，对每个 `type` 为 `MenuType.Menu` 的节点（目录 `Catalog` 没有页面、按钮 `Button` 不是路由，两者都跳过）把它的 `component` 字符串拿去比对 `import.meta.glob('/src/views/**/*.vue')` 生成的映射表。约定很直接：菜单的 `component` 字段就是相对 `src/views` 的文件路径，去掉 `.vue` 后缀。例如 `system/user/index` 对应 `/src/views/system/user/index.vue`。

**组件缺失不会有任何显眼的报错，只会悄悄把这条菜单项从路由表里丢掉。** 如果 `node.component` 在 glob 表里找不到对应的键，`buildRoutesForModule` 只打一条 `console.warn` 然后跳过这个节点，不注册路由。菜单链接哪怕还渲染得出来，点了也是 404 或者压根不显示，普通用户完全看不出哪里出了问题。为了不让菜单管理员踩这个坑，`useAuthMenu.ts` 同时导出了 `viewComponentPaths`：它把 glob 表里每个合法键都换算成同样格式的 `component` 字符串，喂给菜单管理表单的「组件路径」字段做下拉选择，而不是让人手敲。

每条注册出来的路由都带 `name: 'menu-{id}'` 和 `meta.keepAlive: true`，通过 `router.addRoute('layout', ...)` 挂在 `layout` 下。每个用这种方式加进去的路由名都会经 `registerDynamic(name)` 登记，好在登出或切应用时精确地把它们移除（见 `router/index.ts` 里的 `resetRouter`）。

## 页面缓存与具名组件

`layouts/default.vue` 用这样的写法缓存页面：

```vue
<keep-alive :include="tabs.cachedNames" :exclude="tabs.excludeName">
  <component :is="Component" v-if="rvShow" :key="activeKey" />
</keep-alive>
```

`keep-alive` 的 `:include` 是按渲染出来的组件的 **`name`** 来匹配的。对于 `<script setup>` 单文件组件，Vue 会从文件名推断这个 `name`。而 `src/views/**` 下几十个同名的 `index.vue`，推断出来的名字互相冲突，也对不上路由自己的名字（`menu-{id}`）。`router/namedPage.ts` 就是补这个洞的：

```ts
export function namedPage(name: string, loader: AsyncComponentLoader) {
  const hit = cache.get(name)
  if (hit?.loader === loader) return hit.comp

  const inner = defineAsyncComponent({ loader, loadingComponent: LOADING, delay: 0 })
  const comp = defineComponent({ name, render: () => h('div', { class: 'page-view' }, h(inner)) })
  cache.set(name, { loader, comp })
  return comp
}
```

不管静态还是动态路由，每个页面组件都经过 `namedPage` 包一层，给它一个显式的 `name`，恰好等于路由名。这样 `:include="tabs.cachedNames"`（其实是一串 `TabItem.name`，也就是路由名）才真能匹配上它。这层包装按 `name` 缓存在一个 `Map` 里，只有底层的 **loader 引用**变了才会重建：`import.meta.glob` 给每个文件返回的是同一个稳定函数，所以改一个不相关的菜单、触发一次完整的 `buildRoutesForModule` 重建，那些 `component` 路径没变的路由照样复用同一个组件对象。也就是说，`keep-alive` 的缓存条目原封不动，不会被逼着重新挂载。它还把懒加载组件包进单独一层 `<div class="page-view">` 根节点，因为 `default.vue` 的 `<transition mode="out-in">` 要求子节点是单一元素根，而不少页面模板本身是「主体 + 若干并排弹窗」的多根结构。

`stores/tabs.ts` 在这基础上补了一道保险：它的 `cachedNames` getter 会把标签列表过滤到 `router.hasRoute(n)` 为真的那些，这样在菜单重建之后、某个旧标签对应的路由还没被重新注册的那一小段窗口期里，不会让 `keep-alive` 去匹配一个还不存在的名字。`refreshTab(name)` 通过设置 `excludeName` 并递增 `reloadKey` 强制来一次真实的重挂载（绕开缓存），`default.vue` 监听 `reloadKey` 短暂地 `v-if` 卸载再恢复路由出口来实现这一点。

::: tip 这两样东西不在这里
路由链路里没有进度条（不用 NProgress 或类似的库）。文档标题也不是守卫设置的。它只在 `App.vue` 挂载时设一次，标题变化时由站点配置页再设一次，不会随每次导航联动。
:::

要从零把这套链路走一遍：建视图组件、种一条菜单、把组件路径填对，看[加一个前端页面](/zh/guide/frontend-page)。
