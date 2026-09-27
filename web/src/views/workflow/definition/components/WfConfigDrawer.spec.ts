import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import { createWfFormField } from '@/workflow/formSchema'
import type { WfModel } from '@/workflow/schema'
import WfConfigDrawer from './WfConfigDrawer.vue'

vi.mock('@/components/AppIcon.vue', () => ({ default: { render: () => null } }))

let app: App<Element> | undefined

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

describe('WfConfigDrawer form permissions', () => {
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
    await nextTick()
    await nextTick()

    expect(updated?.root.next?.props?.allPassRatio).toBe(75)
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
    await nextTick()

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
    await nextTick()

    expect(updated?.root.next?.props?.formPerms).toEqual([{ field: 'legacy', access: 'hidden' }])
  })
})
