import { defineAsyncComponent, defineComponent, h, type AsyncComponentLoader } from 'vue'

// 加载占位:必须是真实元素——defineAsyncComponent 默认 pending 态渲染 null(注释节点),
// out-in Transition 遇到会判成"非元素根节点",连续快切页面时可能卡死整条过渡序列、之后每页永久空白。
// delay:0 让占位立即生效,不留 pending-无占位的窗口期。
const LOADING = defineComponent({ render: () => h('div') })

/**
 * 包装懒加载页面,使「组件名 === 路由名」。
 * keep-alive 的 :include 按组件 name 匹配,而 <script setup> 的 __name 取自文件名
 * (众多 index.vue 会跨页冲突、且与路由名 menu-${id} 不符)。
 * inner 只建一次 → 身份稳定,keep-alive 缓存其子树;onActivated/onDeactivated 照常透传。
 */
export function namedPage(name: string, loader: AsyncComponentLoader) {
  const inner = defineAsyncComponent({ loader, loadingComponent: LOADING, delay: 0 })
  return defineComponent({ name, render: () => h(inner) })
}
