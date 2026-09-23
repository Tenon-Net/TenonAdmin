import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'
import UserPicker from './index.vue'

const user = {
  id: 7,
  account: 'alice',
  name: 'Alice',
  enabled: true,
  isSuperAdmin: false,
  createTime: '2026-01-01T00:00:00Z',
}

vi.mock('@/api', () => ({
  orgApi: { list: vi.fn().mockResolvedValue([]) },
  userApi: { page: vi.fn() },
}))
vi.mock('naive-ui', async (importOriginal) => ({
  ...await importOriginal<typeof import('naive-ui')>(),
  useMessage: () => ({ error: vi.fn() }),
}))
vi.mock('@/components/FormContainer/index.vue', () => ({
  default: defineComponent({
    setup: (_, { slots }) => () => h('div', slots.default?.()),
  }),
}))
vi.mock('@/components/AppIcon.vue', () => ({
  default: defineComponent({ render: () => h('span') }),
}))
vi.mock('tenon-naive-pro-table', () => ({
  ProTable: defineComponent({
    emits: ['update:checkedRowKeys'],
    setup: (_, { emit, expose }) => {
      const rows = ref([user])
      expose({ rows, refresh: vi.fn() })
      return () => h('button', {
        'data-testid': 'check-user',
        onClick: () => emit('update:checkedRowKeys', [user.id]),
      }, 'check')
    },
  }),
}))

let app: App<Element> | undefined
afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

describe('UserPicker', () => {
  it('adds checked users from the ProTable public rows', async () => {
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(UserPicker)
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)

    host.querySelector<HTMLButtonElement>('[data-testid="check-user"]')!.click()
    await nextTick()
    const add = Array.from(host.querySelectorAll('button')).find((button) => button.textContent?.includes('添加选中'))!
    add.click()
    await nextTick()

    expect(host.textContent).toContain('Alice(alice)')
  })
})
