import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'

import WfAddNode from './WfAddNode.vue'

vi.mock('@/components/AppIcon.vue', () => ({ default: { render: () => null } }))

/** 展开 popover 默认插槽,避免依赖 Naive 浮层挂载。 */
vi.mock('naive-ui', async () => {
  const actual = await vi.importActual<typeof import('naive-ui')>('naive-ui')
  return {
    ...actual,
    NPopover: defineComponent({
      name: 'NPopoverStub',
      setup(_, { slots }) {
        return () => h('div', { class: 'n-popover-stub' }, [
          slots.trigger?.(),
          slots.default?.(),
        ])
      },
    }),
  }
})

let app: App<Element> | undefined

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

function mountAddNode(allowParallel?: boolean): HTMLElement {
  const host = document.createElement('div')
  document.body.append(host)
  app = createApp(WfAddNode, allowParallel === undefined ? {} : { allowParallel })
  app.use(createI18n({
    legacy: false,
    locale: 'zh-CN',
    messages: {
      'zh-CN': {
        workflow: {
          designer: { addNode: '添加节点' },
          node: {
            approval: '审批',
            cc: '抄送',
            branch: '条件分支',
            parallel: '并行',
            webhook: 'Webhook',
          },
        },
      },
    },
  }))
  app.mount(host)
  return host
}

describe('WfAddNode', () => {
  it('defaults to showing the parallel insert option', async () => {
    const host = mountAddNode()
    await nextTick()
    const labels = [...host.querySelectorAll('.wf-add-label')].map((el) => el.textContent)
    expect(labels).toContain('并行')
  })

  it('hides parallel when allowParallel is false', async () => {
    const host = mountAddNode(false)
    await nextTick()
    const labels = [...host.querySelectorAll('.wf-add-label')].map((el) => el.textContent)
    expect(labels).not.toContain('并行')
    expect(labels).toContain('Webhook')
  })
})
