import { afterEach, describe, expect, it } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'
import { createWfFormField } from '@/workflow/formSchema'
import type { WfFormSchema } from '@/workflow/schema'
import WfFormMount from './WfFormMount.vue'

let app: App<Element> | undefined
afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

describe('WfFormMount', () => {
  it('keeps the consumer mount point authoritative if an invalid model has both forms', async () => {
    const schema: WfFormSchema = {
      version: 1,
      fields: [{ ...createWfFormField('text', 'subject'), label: '主题' }],
    }
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfFormMount, {
        formComponent: 'missing/consumer-form',
        formSchema: schema,
        mode: 'start',
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)
    await nextTick()

    expect(host.querySelector('.wf-form-missing')?.textContent).toContain('未找到业务表单组件')
    expect(host.querySelector('.wf-builtin-form')).toBeNull()
  })

  it('shows invalid variable data instead of rendering an empty builtin form', async () => {
    const schema: WfFormSchema = {
      version: 1,
      fields: [{ ...createWfFormField('text', 'subject'), label: '主题' }],
    }
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfFormMount, {
        formSchema: schema,
        mode: 'view',
        variablesJson: '{bad',
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)
    await nextTick()

    expect(host.querySelector('.wf-form-error')?.textContent).toContain('表单变量格式无效')
    expect(host.querySelector('.wf-builtin-form')).toBeNull()
  })
})
