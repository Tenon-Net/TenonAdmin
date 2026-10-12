import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { useAppStore } from '@/stores/app'
import { useStandardPointerMotion } from './useStandardPointerMotion'

afterEach(() => vi.restoreAllMocks())

describe('useStandardPointerMotion', () => {
  it('仅标准档和可见时绑定，恢复后重绑，卸载后清理', async () => {
    let hidden = false
    vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden)
    const listener = vi.fn()
    const pinia = createPinia()
    setActivePinia(pinia)
    const appStore = useAppStore()
    const app = createApp(defineComponent({
      setup() {
        useStandardPointerMotion(listener)
        return () => null
      },
    }))
    app.use(pinia)
    app.mount(document.createElement('div'))

    window.dispatchEvent(new MouseEvent('mousemove'))
    expect(listener).toHaveBeenCalledTimes(1)

    appStore.motionMode = 'light'
    await nextTick()
    window.dispatchEvent(new MouseEvent('mousemove'))
    expect(listener).toHaveBeenCalledTimes(1)

    appStore.motionMode = 'standard'
    await nextTick()
    window.dispatchEvent(new MouseEvent('mousemove'))
    expect(listener).toHaveBeenCalledTimes(2)

    hidden = true
    document.dispatchEvent(new Event('visibilitychange'))
    window.dispatchEvent(new MouseEvent('mousemove'))
    expect(listener).toHaveBeenCalledTimes(2)

    hidden = false
    document.dispatchEvent(new Event('visibilitychange'))
    window.dispatchEvent(new MouseEvent('mousemove'))
    expect(listener).toHaveBeenCalledTimes(3)

    app.unmount()
    window.dispatchEvent(new MouseEvent('mousemove'))
    expect(listener).toHaveBeenCalledTimes(3)
  })
})
