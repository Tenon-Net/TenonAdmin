import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import type { WfWebhookFieldsState } from './drawerForm'
import WfWebhookFields from './WfWebhookFields.vue'

vi.mock('@/components/AppIcon.vue', () => ({ default: { render: () => null } }))

let app: App<Element> | undefined

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

function mountFields(model: WfWebhookFieldsState, onUpdate: (value: WfWebhookFieldsState) => void) {
  const host = document.createElement('div')
  document.body.append(host)
  app = createApp(defineComponent({
    setup: () => () => h(WfWebhookFields, {
      modelValue: model,
      'onUpdate:modelValue': onUpdate,
    }),
  }))
  app.use(createI18n({ legacy: false, locale: 'en-US', missingWarn: false, fallbackWarn: false }))
  app.mount(host)
}

function frozenModel(): WfWebhookFieldsState {
  return Object.freeze({
    url: 'https://example.test/old',
    method: 'POST',
    headers: Object.freeze([{ key: 'X-Old', value: 'old' }]) as unknown as WfWebhookFieldsState['headers'],
    timeoutSeconds: 30,
    onFailure: 'fail',
    maxAttempts: 3,
  })
}

describe('WfWebhookFields model updates', () => {
  it('emits a new object for a scalar field without mutating the input model', async () => {
    const source = frozenModel()
    let updated: WfWebhookFieldsState | undefined
    mountFields(source, (value) => { updated = value })
    await nextTick()

    const urlItem = Array.from(document.body.querySelectorAll<HTMLElement>('.n-form-item'))
      .find((item) => item.textContent?.includes('workflow.webhook.url'))
    const input = urlItem?.querySelector<HTMLInputElement>('input')
    input!.value = 'https://example.test/new'
    input!.dispatchEvent(new Event('input', { bubbles: true }))
    await nextTick()

    expect(updated).toEqual({ ...source, url: 'https://example.test/new' })
    expect(updated).not.toBe(source)
    expect(source.url).toBe('https://example.test/old')
  })

  it('emits a new headers array when adding a row without mutating the input array', async () => {
    const source = frozenModel()
    let updated: WfWebhookFieldsState | undefined
    mountFields(source, (value) => { updated = value })
    await nextTick()

    document.body.querySelector<HTMLElement>('.wf-advanced .n-collapse-item__header-main')?.click()
    await nextTick()
    await nextTick()
    Array.from(document.body.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('workflow.webhook.addHeader'))
      ?.click()
    await nextTick()

    expect(updated?.headers).toEqual([{ key: 'X-Old', value: 'old' }, { key: '', value: '' }])
    expect(updated?.headers).not.toBe(source.headers)
    expect(source.headers).toEqual([{ key: 'X-Old', value: 'old' }])
  })
})
