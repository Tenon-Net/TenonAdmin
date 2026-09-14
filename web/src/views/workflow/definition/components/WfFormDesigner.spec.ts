import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'
import { createWfFormField } from '@/workflow/formSchema'
import type { WfFormSchema } from '@/workflow/schema'
import WfFormDesigner from './WfFormDesigner.vue'

vi.mock('@/components/AppIcon.vue', () => ({ default: { render: () => null } }))

let app: App<Element> | undefined
afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

describe('WfFormDesigner', () => {
  it('emits null when the user deletes the final field', async () => {
    const model = ref<WfFormSchema | null>({
      version: 1,
      fields: [{ ...createWfFormField('text', 'reason'), label: '原因' }],
    })
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfFormDesigner, {
        modelValue: model.value,
        'onUpdate:modelValue': (value: WfFormSchema | null) => { model.value = value },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)

    ;(host.querySelector('[aria-label="删除字段"]') as HTMLButtonElement).click()
    await nextTick()

    expect(model.value).toBeNull()
  })
})
