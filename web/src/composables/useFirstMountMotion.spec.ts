import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { useAppStore } from '@/stores/app'
import { cssTimeMs, useFirstMountMotion } from './useFirstMountMotion'

const animate = vi.fn()

beforeEach(() => {
  setActivePinia(createPinia())
  document.documentElement.style.setProperty('--motion-duration-panel-enter', '260ms')
  document.documentElement.style.setProperty('--motion-stagger-step', '30ms')
  document.documentElement.style.setProperty('--motion-stagger-cap', '90ms')
  document.documentElement.style.setProperty('--motion-distance', '8px')
  document.documentElement.style.setProperty('--motion-ease-standard', 'ease-out')
  Object.defineProperty(HTMLElement.prototype, 'animate', { configurable: true, value: animate })
})

afterEach(() => {
  animate.mockReset()
  delete (HTMLElement.prototype as Partial<HTMLElement>).animate
  document.documentElement.removeAttribute('style')
})

describe('cssTimeMs', () => {
  it('解析 motion.css 支持的毫秒和秒值', () => {
    expect(cssTimeMs('260ms')).toBe(260)
    expect(cssTimeMs('0.12s')).toBe(120)
    expect(cssTimeMs('0ms')).toBe(0)
  })
})

describe('useFirstMountMotion', () => {
  function mountTarget() {
    const cancel = vi.fn()
    animate.mockReturnValue({ cancel, onfinish: null, oncancel: null })
    const host = document.createElement('div')
    const component = defineComponent({
      setup() {
        const target = ref<HTMLElement>()
        useFirstMountMotion([target])
        return () => h('div', { ref: target })
      },
    })
    const app = createApp(component)
    app.use(createPinia())
    app.mount(host)
    return { app, cancel }
  }

  it('首次挂载播放，并在卸载时取消自己持有的动画', async () => {
    const { app, cancel } = mountTarget()
    await nextTick()
    await nextTick()
    expect(animate).toHaveBeenCalledOnce()
    expect(animate.mock.calls[0]?.[1]).toMatchObject({ fill: 'backwards' })

    app.unmount()
    expect(cancel).toHaveBeenCalledOnce()
  })

  it('off 模式直接呈现终态', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAppStore().motionMode = 'off'
    const target = ref<HTMLElement>()
    const host = document.createElement('div')
    const app = createApp(defineComponent({
      setup() {
        useFirstMountMotion([target])
        return () => h('div', { ref: target })
      },
    }))
    app.use(pinia)
    app.mount(host)
    await nextTick()
    await nextTick()

    expect(animate).not.toHaveBeenCalled()
    app.unmount()
  })

  it('nextTick 前卸载不会让过期任务重新启动动画', async () => {
    const { app } = mountTarget()
    app.unmount()
    await nextTick()
    await nextTick()
    expect(animate).not.toHaveBeenCalled()
  })
})
