# 动态路由:菜单树→真实路由

`layout` 下除了那三个静态个人页和 404 兜底,其余全部来自 `useAuthMenu.ts` 的 `buildRoutesForModule`:

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

整条链路是:拉当前应用的菜单树(`personalApi.menu(moduleId)`)、拍平成一维数组,对每个 `type` 为 `MenuType.Menu` 的节点(目录 `Catalog` 没有页面、按钮 `Button` 不是路由,两者都跳过)把它的 `component` 字符串拿去比对 `import.meta.glob('/src/views/**/*.vue')` 生成的映射表。约定很直接:菜单的 `component` 字段就是相对 `src/views` 的文件路径,去掉 `.vue` 后缀——比如 `system/user/index` 对应 `/src/views/system/user/index.vue`。

**组件缺失不会有任何显眼的报错,只会悄悄把这条菜单项从路由表里丢掉。** 如果 `node.component` 在 glob 表里找不到对应的键,`buildRoutesForModule` 只打一条 `console.warn` 然后跳过这个节点——不注册路由,菜单链接(哪怕它还渲染出来)点了就是 404 或者压根不显示,普通用户完全看不出哪里出了问题。为了不让菜单管理员踩这个坑,`useAuthMenu.ts` 同时导出了 `viewComponentPaths`——把 glob 表里每个合法键都换算成同样格式的 `component` 字符串——喂给菜单管理表单的「组件路径」字段做下拉选择,而不是让人手敲。

每条注册出来的路由都带 `name: 'menu-{id}'` 和 `meta.keepAlive: true`,通过 `router.addRoute('layout', ...)` 挂在 `layout` 下。每个用这种方式加进去的路由名都会经 `registerDynamic(name)` 登记,好在登出或切应用时精确地把它们移除(见 `router/index.ts` 里的 `resetRouter`)。

**上一节:** [静态路由](/zh/frontend/routing/static)
**下一节:** [多应用门户](/zh/frontend/routing/portal)
