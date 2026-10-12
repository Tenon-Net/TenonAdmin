import { onBeforeUnmount, onMounted, watch } from 'vue'
import { useAppStore } from '@/stores/app'

/** 仅标准档且页面可见时监听登录页指针动效；降级、隐藏与卸载都会实际解绑。 */
export function useStandardPointerMotion(listener: (event: MouseEvent) => void) {
  const app = useAppStore()
  let bound = false

  function bind() {
    if (bound) return
    window.addEventListener('mousemove', listener, { passive: true })
    bound = true
  }

  function unbind() {
    if (!bound) return
    window.removeEventListener('mousemove', listener)
    bound = false
  }

  function sync() {
    if (app.effectiveMotionMode === 'standard' && !document.hidden) bind()
    else unbind()
  }

  watch(() => app.effectiveMotionMode, sync)
  onMounted(() => {
    document.addEventListener('visibilitychange', sync)
    sync()
  })
  onBeforeUnmount(() => {
    document.removeEventListener('visibilitychange', sync)
    unbind()
  })
}
