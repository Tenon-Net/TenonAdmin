import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import { createWfFormField } from '@/workflow/formSchema'
import type { WfModel } from '@/workflow/schema'
import WfConfigDrawer from './WfConfigDrawer.vue'

vi.mock('@/components/AppIcon.vue', () => ({ default: { render: () => null } }))

let app: App<Element> | undefined

async function flushPromises() {
  await new Promise((resolve) => setTimeout(resolve, 0))
  await nextTick()
}

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

describe('WfConfigDrawer form permissions', () => {
  it('编辑审批人时保留超过安全整数范围的字符串 Id', async () => {
    const userId = '9007199254740993'
    const model: WfModel = {
      version: 1,
      root: {
        id: 'start', type: 'start', name: 'Start',
        next: {
          id: 'approval', type: 'approval', name: 'Approval',
          props: { assignee: { provider: 'user', params: { userIds: [userId] } } },
          next: null,
        },
      },
    }
    let updated: WfModel | undefined
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfConfigDrawer, {
        show: true,
        model,
        nodeId: 'approval',
        'onUpdate:model': (value: WfModel) => { updated = value },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'en-US', missingWarn: false, fallbackWarn: false }))
    app.mount(host)
    await nextTick()
    await nextTick()

    Array.from(document.body.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('common.save'))
      ?.click()
    await flushPromises()

    expect(updated?.root.next?.props?.assignee?.params?.userIds).toEqual([userId])
  })

  it('shows and saves the persisted all-sign ratio', async () => {
    const model: WfModel = {
      version: 1,
      root: {
        id: 'start', type: 'start', name: 'Start',
        next: {
          id: 'approval', type: 'approval', name: 'Approval',
          props: { assignee: { provider: 'user', params: { userIds: [1, 2] } }, mode: 'all', allPassRatio: 75 },
          next: null,
        },
      },
    }
    let updated: WfModel | undefined
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfConfigDrawer, {
        show: true,
        model,
        nodeId: 'approval',
        'onUpdate:model': (value: WfModel) => { updated = value },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'en-US', missingWarn: false, fallbackWarn: false }))
    app.mount(host)
    await nextTick()
    await nextTick()

    const ratioItem = Array.from(document.body.querySelectorAll<HTMLElement>('.n-form-item'))
      .find((item) => item.textContent?.includes('workflow.designer.allPassRatio'))
    const input = ratioItem?.querySelector<HTMLInputElement>('input')
    expect(input?.value).toBe('75')
    Array.from(document.body.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('common.save'))
      ?.click()
    await flushPromises()

    expect(updated?.root.next?.props?.allPassRatio).toBe(75)
  })

  it('shows validation feedback and does not save when the all-sign ratio is cleared', async () => {
    const model: WfModel = {
      version: 1,
      root: {
        id: 'approval', type: 'approval', name: 'Approval',
        props: { assignee: { provider: 'user', params: { userIds: [1, 2] } }, mode: 'all', allPassRatio: 75 },
        next: null,
      },
    }
    let updated: WfModel | undefined
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfConfigDrawer, {
        show: true,
        model,
        nodeId: 'approval',
        'onUpdate:model': (value: WfModel) => { updated = value },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'en-US', missingWarn: false, fallbackWarn: false }))
    app.mount(host)
    await nextTick()
    await nextTick()

    const ratioItem = Array.from(document.body.querySelectorAll<HTMLElement>('.n-form-item'))
      .find((item) => item.textContent?.includes('workflow.designer.allPassRatio'))
    const input = ratioItem?.querySelector<HTMLInputElement>('input')
    expect(input).toBeTruthy()
    input!.value = ''
    input!.dispatchEvent(new Event('input', { bubbles: true }))
    input!.dispatchEvent(new Event('blur', { bubbles: true }))
    await nextTick()
    Array.from(document.body.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('common.save'))
      ?.click()
    await flushPromises()

    expect(updated).toBeUndefined()
    expect(ratioItem?.textContent).toContain('workflow.designer.allPassRatioRange')
  })

  it.each([
    ['workflow.webhook.timeoutSeconds', 'workflow.webhook.timeoutRange', false],
    ['workflow.webhook.maxAttempts', 'workflow.webhook.maxAttemptsRange', true],
  ] as const)('does not save a webhook when %s is cleared', async (label, message, expandAdvanced) => {
    const model: WfModel = {
      version: 1,
      root: {
        id: 'webhook', type: 'webhook', name: 'Webhook',
        props: {
          webhookUrl: 'https://example.test/hook',
          webhookMethod: 'POST',
          webhookTimeoutSeconds: 30,
          webhookOnFailure: 'fail',
          maxAttempts: 3,
        },
        next: null,
      },
    }
    let updated: WfModel | undefined
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfConfigDrawer, {
        show: true,
        model,
        nodeId: 'webhook',
        'onUpdate:model': (value: WfModel) => { updated = value },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'en-US', missingWarn: false, fallbackWarn: false }))
    app.mount(host)
    await nextTick()
    await nextTick()

    if (expandAdvanced) {
      document.body.querySelector<HTMLElement>('.wf-advanced .n-collapse-item__header-main')?.click()
      await nextTick()
      await nextTick()
    }
    const fieldItem = Array.from(document.body.querySelectorAll<HTMLElement>('.n-form-item'))
      .find((item) => item.textContent?.includes(label))
    const input = fieldItem?.querySelector<HTMLInputElement>('input')
    expect(input).toBeTruthy()
    input!.value = ''
    input!.dispatchEvent(new Event('input', { bubbles: true }))
    input!.dispatchEvent(new Event('blur', { bubbles: true }))
    await nextTick()
    Array.from(document.body.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('common.save'))
      ?.click()
    await flushPromises()

    expect(updated).toBeUndefined()
    expect(fieldItem?.textContent).toContain(message)
  })

  it('renders schema fields and serializes only known non-default permissions', async () => {
    const model: WfModel = {
      version: 1,
      formSchema: {
        version: 1,
        fields: [
          { ...createWfFormField('money', 'amount'), label: 'Amount' },
          { ...createWfFormField('text', 'note'), label: 'Note' },
        ],
      },
      root: {
        id: 'start',
        type: 'start',
        name: 'Start',
        next: {
          id: 'approval',
          type: 'approval',
          name: 'Approval',
          props: {
            assignee: { provider: 'leader', params: { level: 1 } },
            formPerms: [
              { field: 'amount', access: 'readonly' },
              { field: 'note', access: 'editable' },
              { field: 'removed', access: 'hidden' },
            ],
          },
          next: null,
        },
      },
    }
    let updated: WfModel | undefined
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfConfigDrawer, {
        show: true,
        model,
        nodeId: 'approval',
        'onUpdate:model': (value: WfModel) => { updated = value },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'en-US', missingWarn: false, fallbackWarn: false }))
    app.mount(host)
    await nextTick()
    await nextTick()

    document.body.querySelector<HTMLElement>('.wf-advanced .n-collapse-item__header-main')?.click()
    await nextTick()
    await nextTick()

    const permissions = document.body.querySelector('.wf-form-permissions')
    expect(permissions?.querySelectorAll('.n-form-item')).toHaveLength(2)
    expect(permissions?.textContent).toContain('Amount')
    expect(permissions?.textContent).toContain('Note')
    expect(permissions?.textContent).not.toContain('removed')

    Array.from(document.body.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('common.save'))
      ?.click()
    await flushPromises()

    expect(updated?.root.next?.props?.formPerms).toEqual([{ field: 'amount', access: 'readonly' }])
  })

  it('keeps legacy permissions untouched for a consumer form mount', async () => {
    const model: WfModel = {
      version: 1,
      formComponent: 'biz/request/form',
      root: {
        id: 'start',
        type: 'start',
        name: 'Start',
        next: {
          id: 'approval',
          type: 'approval',
          name: 'Approval',
          props: {
            assignee: { provider: 'leader', params: { level: 1 } },
            formPerms: [{ field: 'legacy', access: 'hidden' }],
          },
          next: null,
        },
      },
    }
    let updated: WfModel | undefined
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfConfigDrawer, {
        show: true,
        model,
        nodeId: 'approval',
        'onUpdate:model': (value: WfModel) => { updated = value },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'en-US', missingWarn: false, fallbackWarn: false }))
    app.mount(host)
    await nextTick()
    await nextTick()

    expect(document.body.querySelector('.wf-form-permissions')).toBeNull()
    Array.from(document.body.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('common.save'))
      ?.click()
    await flushPromises()

    expect(updated?.root.next?.props?.formPerms).toEqual([{ field: 'legacy', access: 'hidden' }])
  })
})
