import { defineAsyncComponent, defineComponent, h, type AsyncComponentLoader } from 'vue'

/**
 * 包装懒加载页面,使「组件名 === 路由名」。
 * keep-alive 的 :include 按组件 name 匹配,而 <script setup> 的 __name 取自文件名
 * (众多 index.vue 会跨页冲突、且与路由名 menu-${id} 不符)。
 * inner 只建一次 → 身份稳定,keep-alive 缓存其子树;onActivated/onDeactivated 照常透传。
 */
export function namedPage(name: string, loader: AsyncComponentLoader) {
  const inner = defineAsyncComponent(loader)
  return defineComponent({ name, render: () => h(inner) })
}
