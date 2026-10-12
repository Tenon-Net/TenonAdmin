import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, h, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'
import WfNodeCard from './WfNodeCard.vue'

vi.mock('@/components/AppIcon.vue', () => ({ default: { render: () => null } }))

let app: App<Element> | undefined

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

function mountCard(props: Record<string, unknown>, onInspect = vi.fn(), onSelect = vi.fn()) {
  const host = document.createElement('div')
  document.body.append(host)
  app = createApp({
    setup: () => () => h(WfNodeCard, {
      node: { id: 'approval', type: 'approval', name: '主管审批', next: null },
      ...props,
      onInspect,
      onSelect,
    }),
  })
  app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
  app.mount(host)
  return { host, onInspect, onSelect }
}

describe('workflow node runtime card', () => {
  it('shows a conservative not-started state instead of the designer placeholder', () => {
    const { host } = mountCard({ readonly: true })
    expect(host.textContent).toContain('未开始')
    expect(host.textContent).not.toContain('请选择审批人')
  })

  it('opens read-only personnel details from an inspectable runtime card', () => {
    const { host, onInspect } = mountCard({
      readonly: true,
      current: true,
      runtimeLines: ['张三 同意', '待审批：李四', '等待轮到：王五', '无需处理：赵六'],
      runtimeInspectable: true,
    })
    const card = host.querySelector('[data-node-id="approval"][data-runtime-inspectable="true"]')
    expect(card?.textContent).toContain('待审批：李四')
    expect(card?.querySelectorAll('[data-testid="wf-node-runtime-line"]')).toHaveLength(4)
    expect(card?.querySelector('.wf-card-text.is-runtime')).toBeTruthy()
    card?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    expect(onInspect).toHaveBeenCalledOnce()
  })

  it('keeps the designer card selectable and its configuration placeholder visible', () => {
    const { host, onSelect } = mountCard({ readonly: false })
    expect(host.textContent).toContain('请选择审批人')
    host.querySelector('[data-node-id="approval"]')?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    expect(onSelect).toHaveBeenCalledOnce()
  })

  it('keeps a downstream node not started after return while retaining access to old details', () => {
    const { host, onInspect } = mountCard({
      readonly: true,
      visited: false,
      current: false,
      runtimeLines: ['张三 同意'],
      runtimeInspectable: true,
    })
    const card = host.querySelector('[data-node-id="approval"][data-runtime-inspectable="true"]')
    expect(card?.textContent).toContain('未开始')
    expect(card?.textContent).not.toContain('张三 同意')
    card?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    expect(onInspect).toHaveBeenCalledOnce()
  })
})
