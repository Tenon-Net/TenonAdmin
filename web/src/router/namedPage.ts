import { defineAsyncComponent, defineComponent, h, type Component, type AsyncComponentLoader } from 'vue'

// 加载占位:必须是真实元素——defineAsyncComponent 默认 pending 态渲染 null(注释节点),
// out-in Transition 遇到会判成"非元素根节点",连续快切页面时可能卡死整条过渡序列、之后每页永久空白。
// delay:0 让占位立即生效,不留 pending-无占位的窗口期。
const LOADING = defineComponent({ render: () => h('div') })

// 按路由名记忆化:改一次菜单就会重建全部动态路由(见 useAuthMenu.buildRoutesForModule),
// 若每次都新建组件对象,已开标签页的路由记录会换上新身份 → keep-alive 缓存全部 miss、切回时重挂、状态清空。
// 失效条件用 loader 引用:import.meta.glob 的 loader 每个文件一个稳定函数,
// 组件路径没改 → 同一引用 → 复用(缓存保住);管理员改了组件路径 → 引用变 → 重建(正确失效)。
const cache = new Map<string, { loader: AsyncComponentLoader; comp: Component }>()

/**
 * 包装懒加载页面,使「组件名 === 路由名」。
 * keep-alive 的 :include 按组件 name 匹配,而 <script setup> 的 __name 取自文件名
 * (众多 index.vue 会跨页冲突、且与路由名 menu-${id} 不符)。
 * inner 只建一次 → 身份稳定,keep-alive 缓存其子树;onActivated/onDeactivated 照常透传。
 *
 * 外面再套一层原生 <div.page-view>:default.vue 的 <transition mode="out-in"> 硬性要求子节点是
 * 单个根元素,而不少页面模板是「主体 + 并排若干弹窗组件」的多根 Fragment(关着时渲染成注释节点)。
 * out-in 遇到非元素/多根会走进"non-element root node"分支瞬时卡死。统一套一层 div,交给 transition
 * 的永远是单元素根,任何页面(单根/多根/异步加载中)都能瞬时过渡,不再需要卡死自愈的看门狗。
 */
export function namedPage(name: string, loader: AsyncComponentLoader) {
  const hit = cache.get(name)
  if (hit?.loader === loader) return hit.comp

  const inner = defineAsyncComponent({ loader, loadingComponent: LOADING, delay: 0 })
  const comp = defineComponent({ name, render: () => h('div', { class: 'page-view' }, h(inner)) })
  cache.set(name, { loader, comp })
  return comp
}
