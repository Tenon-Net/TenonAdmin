import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'
import StartPage from './index.vue'

const startable = vi.hoisted(() => vi.fn().mockResolvedValue([]))
const startableDetail = vi.hoisted(() => vi.fn())

vi.mock('@/api/workflow', () => ({
  wfInstanceApi: { startable, startableDetail, start: vi.fn() },
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ push: vi.fn() }),
}))
vi.mock('naive-ui', async (importOriginal) => ({
  ...await importOriginal<typeof import('naive-ui')>(),
  useMessage: () => ({ error: vi.fn(), success: vi.fn() }),
  NSelect: defineComponent({
    emits: ['update:value'],
    setup: (_, { emit }) => () => h('div', [
      h('button', { 'data-testid': 'definition-1', onClick: () => emit('update:value', 1) }, 'one'),
      h('button', { 'data-testid': 'definition-2', onClick: () => emit('update:value', 2) }, 'two'),
    ]),
  }),
}))
vi.mock('@/components/AppIcon.vue', () => ({ default: defineComponent({ render: () => h('span') }) }))
vi.mock('@/components/UserSelect/index.vue', () => ({ default: defineComponent({ render: () => h('span') }) }))
vi.mock('../components/WfFormMount.vue', () => ({
  default: defineComponent({
    props: { formComponent: String },
    setup: (props) => () => h('div', { 'data-testid': 'form-component' }, props.formComponent),
  }),
}))

let app: App<Element> | undefined
afterEach(() => {
  app?.unmount()
  app = undefined
  startableDetail.mockReset()
  document.body.replaceChildren()
})

describe('workflow start definition loading', () => {
  it('keeps the latest definition when an older request finishes last', async () => {
    let resolveFirst!: (value: object) => void
    let resolveSecond!: (value: object) => void
    startableDetail.mockImplementation((id: number) => new Promise((resolve) => {
      if (id === 1) resolveFirst = resolve
      else resolveSecond = resolve
    }))
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(StartPage)
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)

    host.querySelector<HTMLButtonElement>('[data-testid="definition-1"]')!.click()
    await nextTick()
    host.querySelector<HTMLButtonElement>('[data-testid="definition-2"]')!.click()
    await nextTick()
    resolveSecond({ formComponent: 'form-b', model: null })
    await nextTick()
    resolveFirst({ formComponent: 'form-a', model: null })
    await nextTick()

    expect(host.querySelector('[data-testid="form-component"]')?.textContent).toBe('form-b')
  })
})
