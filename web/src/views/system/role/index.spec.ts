import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, ref, type App, type VNodeChild } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'
import RolePage from './index.vue'

const api = vi.hoisted(() => ({
  getMenus: vi.fn(),
  setMenus: vi.fn().mockResolvedValue(true),
  getUsers: vi.fn(),
  setUsers: vi.fn().mockResolvedValue(true),
  getDataScope: vi.fn(),
  setDataScope: vi.fn().mockResolvedValue(true),
}))

vi.mock('@/api', () => ({
  roleApi: {
    page: vi.fn(),
    remove: vi.fn(),
    batchRemove: vi.fn(),
    add: vi.fn(),
    update: vi.fn(),
    ...api,
  },
  menuApi: { tree: vi.fn().mockResolvedValue([]) },
  moduleApi: { list: vi.fn().mockResolvedValue([]) },
  userApi: { page: vi.fn().mockResolvedValue({ items: [], total: 0 }) },
}))
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ isSuperAdmin: true, currentModuleId: null, hasPerm: () => true }),
}))
vi.mock('@/composables/useConfirm', () => ({
  useConfirm: () => ({ confirm: vi.fn() }),
}))
vi.mock('@/composables/useBatchDelete', () => ({
  useBatchDelete: () => ({ checkedKeys: ref([]), hasSelection: ref(false), run: vi.fn() }),
}))
vi.mock('tenon-naive-pro-table', () => ({
  ProTable: defineComponent({
    props: { columns: { type: Array, required: true } },
    setup(props) {
      const rows = [
        { id: 1, name: '角色 A', code: 'a', sort: 1, enabled: true },
        { id: 2, name: '角色 B', code: 'b', sort: 2, enabled: true },
      ]
      return () => h('div', rows.map((row) => {
        const columns = props.columns as Array<{ key?: string; render?: (value: typeof row) => unknown }>
        const operation = columns.find((column) => column.key === 'op')?.render?.(row) as VNodeChild
        return h('div', { 'data-role': row.id }, [operation])
      }))
    },
  }),
}))
vi.mock('naive-ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('naive-ui')>()
  return {
    ...actual,
    useMessage: () => ({ error: vi.fn(), success: vi.fn(), destroyAll: vi.fn() }),
    NDropdown: defineComponent({
      props: { options: { type: Array, default: () => [] } },
      emits: ['select'],
      setup(props, { emit, slots }) {
        return () => h('div', [
          slots.default?.(),
          ...(props.options as Array<{ key: string; label: string }>).map((option) =>
            h('button', { 'data-action': option.key, onClick: () => emit('select', option.key) }, option.label),
          ),
        ])
      },
    }),
    NSelect: defineComponent({
      props: { value: Number },
      setup(props) {
        return () => h('span', { 'data-testid': 'scope-value' }, String(props.value))
      },
    }),
  }
})
vi.mock('@/components/AppIcon.vue', () => ({ default: defineComponent(() => () => null) }))
vi.mock('@/components/StatusSwitch/index.vue', () => ({ default: defineComponent(() => () => null) }))
vi.mock('@/components/OrgTreeSelect/index.vue', () => ({ default: defineComponent(() => () => null) }))
vi.mock('@/components/FormContainer/index.vue', () => ({
  default: defineComponent({
    props: { show: Boolean, title: String, onConfirm: Function },
    emits: ['update:show'],
    setup(props, { slots }) {
      return () => props.show
        ? h('section', { 'data-container': props.title }, [
            slots.default?.(),
            h('button', { 'data-confirm': props.title, onClick: () => props.onConfirm?.() }, 'confirm'),
          ])
        : null
    },
  }),
}))
vi.mock('./components/GrantMenuTable.vue', () => ({
  default: defineComponent({
    props: { granted: { type: Array, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': 'menu-granted' }, JSON.stringify(props.granted))
    },
  }),
}))
vi.mock('@/components/UserPicker/index.vue', () => ({
  default: defineComponent({
    emits: ['confirm'],
    setup(_, { emit, expose }) {
      const ids = ref<Array<string | number>>([])
      expose({ open: (value: Array<string | number>) => { ids.value = value } })
      return () => h('div', [
        h('span', { 'data-testid': 'user-ids' }, JSON.stringify(ids.value)),
        h('button', { 'data-testid': 'user-confirm', onClick: () => emit('confirm', [...ids.value]) }, 'confirm'),
      ])
    },
  }),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

let app: App<Element> | undefined
let host: HTMLDivElement | undefined

beforeEach(() => {
  vi.clearAllMocks()
})

afterEach(() => {
  app?.unmount()
  app = undefined
  host?.remove()
  host = undefined
})

function mount() {
  host = document.createElement('div')
  document.body.append(host)
  app = createApp(RolePage)
  app.directive('auth', () => undefined)
  app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
  app.mount(host)
  return host
}

function clickAction(roleId: number, action: 'menus' | 'users' | 'scope') {
  const button = host?.querySelector<HTMLButtonElement>(`[data-role="${roleId}"] [data-action="${action}"]`)
  expect(button).toBeTruthy()
  button!.click()
}

describe('角色授权目标竞态', () => {
  it('菜单授权只显示最后选择的角色，并按打开时目标保存', async () => {
    const first = deferred<Array<number>>()
    const second = deferred<Array<number>>()
    api.getMenus.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    mount()

    clickAction(1, 'menus')
    clickAction(2, 'menus')
    second.resolve([22])
    await vi.waitFor(() => expect(host?.querySelector('[data-testid="menu-granted"]')?.textContent).toBe('[22]'))
    first.resolve([11])
    await first.promise
    expect(host?.querySelector('[data-testid="menu-granted"]')?.textContent).toBe('[22]')

    host?.querySelector<HTMLButtonElement>('[data-confirm="授权菜单"]')?.click()
    expect(api.setMenus).toHaveBeenCalledWith(2, [22])
  })

  it('用户授权只打开最后选择的用户集，并按对应角色保存', async () => {
    const first = deferred<Array<number>>()
    const second = deferred<Array<number>>()
    api.getUsers.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    mount()

    clickAction(1, 'users')
    clickAction(2, 'users')
    second.resolve([202])
    await vi.waitFor(() => expect(host?.querySelector('[data-testid="user-ids"]')?.textContent).toBe('[202]'))
    first.resolve([101])
    await first.promise
    expect(host?.querySelector('[data-testid="user-ids"]')?.textContent).toBe('[202]')

    host?.querySelector<HTMLButtonElement>('[data-testid="user-confirm"]')?.click()
    expect(api.setUsers).toHaveBeenCalledWith(2, [202])
  })

  it('数据范围只显示最后选择的角色，并保存该角色的表单快照', async () => {
    const first = deferred<{ scopeType: number; customOrgIds: string }>()
    const second = deferred<{ scopeType: number; customOrgIds: string }>()
    api.getDataScope.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    mount()

    clickAction(1, 'scope')
    clickAction(2, 'scope')
    second.resolve({ scopeType: 5, customOrgIds: '20,21' })
    await vi.waitFor(() => expect(host?.querySelector('[data-testid="scope-value"]')?.textContent).toBe('5'))
    first.resolve({ scopeType: 2, customOrgIds: '' })
    await first.promise
    expect(host?.querySelector('[data-testid="scope-value"]')?.textContent).toBe('5')

    host?.querySelector<HTMLButtonElement>('[data-confirm="数据范围"]')?.click()
    expect(api.setDataScope).toHaveBeenCalledWith(2, 5, [20, 21])
  })
})
