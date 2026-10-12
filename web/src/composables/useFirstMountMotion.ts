import { nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, watch, type Ref } from 'vue'
import { useAppStore } from '@/stores/app'

/** 将 motion.css 的时间变量转为 WAAPI 毫秒值，JS 不维护第二份时长常量。 */
export function cssTimeMs(value: string): number {
  const normalized = value.trim()
  if (normalized.endsWith('ms')) return Number.parseFloat(normalized) || 0
  if (normalized.endsWith('s')) return (Number.parseFloat(normalized) || 0) * 1000
  return 0
}

/**
 * 少量主区块仅首次 mount 入场。KeepAlive 恢复不会再次调用 onMounted；停用、隐藏、
 * 设置切换与卸载都会 cancel 到 CSS 最终态，不依赖动画事件推进业务状态。
 */
export function useFirstMountMotion(targets: Ref<HTMLElement | undefined>[]) {
  const app = useAppStore()
  const owned = new Set<Animation>()
  let active = true
  let generation = 0

  function cancel() {
    generation++
    for (const animation of owned) animation.cancel()
    owned.clear()
  }

  function canPlay() {
    return app.pageTransition !== 'none' && app.effectiveMotionMode !== 'off' && !document.hidden
  }

  async function play() {
    if (!canPlay()) return
    const currentGeneration = generation
    await nextTick()
    // nextTick 期间可能已停用、卸载、隐藏或切换设置；过期任务不得重新启动动画。
    if (
      !active
      || currentGeneration !== generation
      || !canPlay()
    ) return
    const style = getComputedStyle(document.documentElement)
    const duration = cssTimeMs(style.getPropertyValue('--motion-duration-panel-enter'))
    const step = cssTimeMs(style.getPropertyValue('--motion-stagger-step'))
    const cap = cssTimeMs(style.getPropertyValue('--motion-stagger-cap'))
    const distance = style.getPropertyValue('--motion-distance').trim() || '0px'

    targets.forEach((target, index) => {
      if (!target.value?.animate || duration === 0) return
      const animation = target.value.animate(
        [
          { opacity: 0, transform: `translateY(${distance})` },
          { opacity: 1, transform: 'translateY(0)' },
        ],
        {
          duration,
          delay: Math.min(index * step, cap),
          easing: style.getPropertyValue('--motion-ease-standard').trim(),
          // 错峰等待期间保持首帧，避免元素先可见、delay 到点后再跳回 opacity:0。
          fill: 'backwards',
        },
      )
      owned.add(animation)
      animation.onfinish = () => owned.delete(animation)
      animation.oncancel = () => owned.delete(animation)
    })
  }

  function onVisibilityChange() {
    if (document.hidden) cancel()
  }

  onMounted(() => {
    document.addEventListener('visibilitychange', onVisibilityChange)
    void play()
  })
  watch([() => app.effectiveMotionMode, () => app.pageTransition], cancel)
  onActivated(() => {
    active = true
  })
  onDeactivated(() => {
    active = false
    cancel()
  })
  onBeforeUnmount(() => {
    active = false
    cancel()
    document.removeEventListener('visibilitychange', onVisibilityChange)
  })

  return { cancel }
}
