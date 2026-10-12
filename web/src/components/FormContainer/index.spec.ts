import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App as VueApp } from 'vue'

const holder = vi.hoisted(() => ({
  app: undefined as unknown as { formStyle: 'modal' | 'drawer'; effectiveMotionMode: 'standard' | 'light' | 'off' },
}))

vi.mock('@/stores/app', async () => {
  const { reactive } = await import('vue')
  holder.app = reactive({ formStyle: 'modal', effectiveMotionMode: 'standard' })
  return { useAppStore: () => holder.app }
})

vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@vueuse/core', async (importOriginal) => {
  const { ref } = await import('vue')
  const actual = await importOriginal<typeof import('@vueuse/core')>()
  return { ...actual, useWindowSize: () => ({ width: ref(1280), height: ref(720) }) }
})
vi.mock('naive-ui', async () => {
  const { defineComponent, h } = await import('vue')
  const Root = defineComponent({
    inheritAttrs: false,
    setup(_, { attrs, slots }) {
      return () => h('div', attrs, slots.default?.())
    },
  })
  return { NModal: Root, NDrawer: Root, NDrawerContent: Root, NButton: Root, NSpace: Root }
})

import FormContainer from './index.vue'

describe('FormContainer motion policy changes', () => {
  let app: VueApp<Element> | undefined
  let host: HTMLDivElement
  const finish = vi.fn()
  const getAnimations = vi.fn(() => [
    { playState: 'running', finish },
    { playState: 'finished', finish },
  ] as unknown as Animation[])

  beforeEach(async () => {
    holder.app.formStyle = 'modal'
    holder.app.effectiveMotionMode = 'standard'
    finish.mockReset()
    getAnimations.mockClear()
    Object.defineProperty(HTMLElement.prototype, 'getAnimations', { configurable: true, value: getAnimations })
    host = document.createElement('div')
    document.body.append(host)
    app = createApp(FormContainer, { title: '测试弹层', show: true })
    app.mount(host)
    await nextTick()
  })

  afterEach(() => {
    app?.unmount()
    host.remove()
    delete (HTMLElement.prototype as Partial<HTMLElement>).getAnimations
  })

  it('档位或系统偏好改变有效模式时立即完成自身仍在运行的过渡', async () => {
    const unrelated = document.createElement('div')
    const unrelatedAnimations = vi.fn(() => [{ playState: 'running', finish }] as unknown as Animation[])
    unrelated.className = 'motion-form-panel'
    unrelated.dataset.formMotionId = 'other-instance'
    Object.defineProperty(unrelated, 'getAnimations', { configurable: true, value: unrelatedAnimations })
    document.body.prepend(unrelated)

    holder.app.effectiveMotionMode = 'light'
    await nextTick()
    expect(getAnimations).toHaveBeenCalledOnce()
    expect(finish).toHaveBeenCalledOnce()
    expect(unrelatedAnimations).not.toHaveBeenCalled()

    holder.app.effectiveMotionMode = 'off'
    await nextTick()
    expect(getAnimations).toHaveBeenCalledTimes(2)
    expect(finish).toHaveBeenCalledTimes(2)

    unrelated.remove()
  })
})
