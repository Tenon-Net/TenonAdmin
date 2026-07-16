# Keep-Alive 与具名组件

`layouts/default.vue` 用这样的写法缓存页面:

```vue
<keep-alive :include="tabs.cachedNames" :exclude="tabs.excludeName">
  <component :is="Component" v-if="rvShow" :key="activeKey" />
</keep-alive>
```

`keep-alive` 的 `:include` 是按渲染出来的组件的 **`name`** 来匹配的。对于 `<script setup>` 单文件组件,Vue 会从文件名推断这个 `name`——而 `src/views/**` 下几十个同名的 `index.vue`,推断出来的名字互相冲突,也对不上路由自己的名字(`menu-{id}`)。`router/namedPage.ts` 就是补这个洞的:

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

不管静态还是动态路由,每个页面组件都经过 `namedPage` 包一层,给它一个显式的 `name`,恰好等于路由名——这样 `:include="tabs.cachedNames"`(其实是一串 `TabItem.name`,也就是路由名)才真能匹配上它。这层包装按 `name` 缓存在一个 `Map` 里,只有底层的 **loader 引用**变了才会重建:`import.meta.glob` 给每个文件返回的是同一个稳定函数,所以改一个不相关的菜单、触发一次完整的 `buildRoutesForModule` 重建,那些 `component` 路径没变的路由照样复用同一个组件对象——`keep-alive` 的缓存条目原封不动,不会被逼着重新挂载。它还把懒加载组件包进单独一层 `<div class="page-view">` 根节点,因为 `default.vue` 的 `<transition mode="out-in">` 要求子节点是单一元素根,而不少页面模板本身是「主体 + 若干并排弹窗」的多根结构。

`stores/tabs.ts` 在这基础上补了一道保险:它的 `cachedNames` getter 会把标签列表过滤到 `router.hasRoute(n)` 为真的那些,这样在菜单重建之后、某个旧标签对应的路由还没被重新注册的那一小段窗口期里,不会让 `keep-alive` 去匹配一个还不存在的名字。`refreshTab(name)` 通过设置 `excludeName` 并递增 `reloadKey` 强制来一次真实的重挂载(绕开缓存),`default.vue` 监听 `reloadKey` 短暂地 `v-if` 卸载再恢复路由出口来实现这一点。

::: tip 这两样东西不在这里
路由链路里没有进度条(不用 NProgress 或类似的库)。文档标题也不是守卫设置的——它只在 `App.vue` 挂载时设一次,标题变化时由站点配置页再设一次,不会随每次导航联动。
:::

**上一节:** [路由守卫](/zh/frontend/routing/guards)

## 接下来看什么

- [前端目录结构](/zh/frontend/structure)——views、components、stores 是怎么组织的。
- [权限指令(`v-auth`)](/zh/frontend/permission)——按权限码控制按钮和元素的显隐。
- [加一个前端页面](/zh/tutorial/frontend-page)——从零到有,完整走一遍加一个菜单驱动页面的过程。
