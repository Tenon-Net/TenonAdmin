# 路由与动态菜单(概览)

前端的路由来自两个互不相干的源:构建期就定死的**静态壳**,和登录后按当前应用的菜单树在运行时重建出来的**动态路由**。本页依次讲清楚这两部分、多应用门户如何决定进哪个应用、把它们串起来的守卫,以及为什么每个页面都需要一个稳定身份,`keep-alive` 才认得出它。

## 总览

```text
staticRoutes(router/routes.ts)          buildRoutesForModule(useAuthMenu.ts)
  ├─ /login                                拉 personalApi.menu(moduleId)
  ├─ /module(应用选择器)                    拍平菜单树
  └─ /  → layout(default.vue)              对每个 Menu 类型节点:
        ├─ /personal/profile                 component 字符串 → /src/views/**/*.vue
        ├─ /personal/password                 router.addRoute('layout', { name: 'menu-{id}', ... })
        ├─ /personal/notice
        └─ /:pathMatch(.*)*(404)

构建期定死,部署间不变。                   登录 / 切应用 / 硬刷新时各重建一次。
```

静态那一侧在两次部署之间从不变化。动态那一侧完全由当前选中的应用返回的菜单树决定——不同用户、不同角色、不同应用,挂在 `layout` 下的那批路由都各不相同。

## 本节内容

- [静态路由](/zh/frontend/routing/static)
- [动态路由:菜单树→真实路由](/zh/frontend/routing/dynamic)
- [多应用门户](/zh/frontend/routing/portal)
- [路由守卫](/zh/frontend/routing/guards)
- [Keep-Alive 与具名组件](/zh/frontend/routing/keep-alive)

**下一节:** [静态路由](/zh/frontend/routing/static)
