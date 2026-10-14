import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'

const push = vi.hoisted(() => vi.fn())
const warning = vi.hoisted(() => vi.fn())
const get = vi.hoisted(() => vi.fn())
const forceTransfer = vi.hoisted(() => vi.fn())
const terminate = vi.hoisted(() => vi.fn())
const skip = vi.hoisted(() => vi.fn())
const fixture = vi.hoisted(() => ({
  instanceId: '9007199254740993' as string | number,
  userId: '9007199254740999' as string | number,
}))

vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => ({ hasPerm: () => true }) }))
vi.mock('@/api/workflow', () => ({
  wfInstanceApi: { monitor: vi.fn(), get, forceTransfer, terminate, skip },
}))
vi.mock('naive-ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('naive-ui')>()
  return {
    ...actual,
    useMessage: () => ({ error: vi.fn(), success: vi.fn(), warning }),
    NInput: defineComponent({
      props: { value: String },
      emits: ['update:value'],
      setup(props, { emit }) {
        return () => h('textarea', {
          'data-testid': 'reason',
          value: props.value ?? '',
          onInput: (event: Event) => emit('update:value', (event.target as HTMLTextAreaElement).value),
        })
      },
    }),
  }
})
vi.mock('tenon-naive-pro-table', () => ({
  ProTable: defineComponent({
    name: 'ProTable',
    props: { columns: { type: Array, required: true } },
    emits: ['row-click'],
    setup(props, { emit }) {
      return () => {
        const columns = props.columns as Array<{ key?: string; render?: (row: { id: string | number; status: number }) => unknown }>
        const row = { id: fixture.instanceId, status: 1 }
        const rendered = columns.find((column) => column.key === 'op')?.render?.(row)
        const buttons = Array.isArray(rendered) ? rendered : rendered == null ? [] : [rendered]
        return h('div', [
          h('button', { onClick: () => emit('row-click', row) }, 'row-click'),
          ...buttons,
        ])
      }
    },
  }),
}))
vi.mock('@/components/UserSelect/index.vue', () => ({
  default: defineComponent({
    name: 'UserSelect',
    emits: ['update:value'],
    setup(_, { emit }) {
      return () => h('button', { type: 'button', onClick: () => emit('update:value', fixture.userId) }, 'pick-user')
    },
  }),
}))

import MonitorPage from './index.vue'

const snowflake = '9007199254740993'
const targetUser = '9007199254740999'

let app: App<Element> | undefined
let host: HTMLDivElement | undefined

beforeEach(() => {
  vi.clearAllMocks()
  fixture.instanceId = snowflake
  fixture.userId = targetUser
  get.mockResolvedValue({ currentTasks: [{ nodeId: 'approval', nodeName: '审批' }] })
  forceTransfer.mockResolvedValue({})
  terminate.mockResolvedValue({})
  skip.mockResolvedValue({})
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
  app = createApp(MonitorPage)
  app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
  app.mount(host)
}

function clickButton(label: string) {
  const button = [...document.querySelectorAll('button')].find((item) => item.textContent?.includes(label))
  if (!button) throw new Error(`missing button ${label}`)
  button.click()
}

async function fillReason(text: string) {
  await nextTick()
  const input = document.querySelector<HTMLTextAreaElement>('[data-testid="reason"]')
  if (!input) throw new Error('missing reason')
  input.value = text
  input.dispatchEvent(new Event('input'))
  await nextTick()
}

describe('workflow monitor large ids', () => {
  it('keeps the instance id in the detail url', async () => {
    mount()
    clickButton('查看')
    expect(push).toHaveBeenCalledWith(`/workflow/instance/${snowflake}/detail`)
    clickButton('row-click')
    expect(push).toHaveBeenLastCalledWith(`/workflow/instance/${snowflake}/detail`)
  })

  it('sends the original instance id and target user id on force transfer', async () => {
    mount()
    clickButton('强制转办')
    await nextTick()
    await Promise.resolve()
    expect(get).toHaveBeenCalledWith(snowflake)
    clickButton('pick-user')
    await fillReason('转给财务')
    clickButton('确定')
    await nextTick()
    await Promise.resolve()
    expect(forceTransfer).toHaveBeenCalledWith({
      instanceId: snowflake,
      toUserId: targetUser,
      reason: '转给财务',
      nodeId: 'approval',
    })
  })

  it('sends the original instance id on terminate and skip', async () => {
    mount()
    clickButton('终止')
    await fillReason('结束')
    clickButton('确定')
    await nextTick()
    await Promise.resolve()
    expect(terminate).toHaveBeenCalledWith({ instanceId: snowflake, reason: '结束' })
    expect(get).not.toHaveBeenCalled()

    terminate.mockClear()
    clickButton('跳过节点')
    await nextTick()
    await Promise.resolve()
    expect(get).toHaveBeenCalledWith(snowflake)
    await fillReason('跳过')
    clickButton('确定')
    await nextTick()
    await Promise.resolve()
    expect(skip).toHaveBeenCalledWith({ instanceId: snowflake, reason: '跳过', nodeId: 'approval' })
  })

  it('keeps safe integer ids as numbers', async () => {
    fixture.instanceId = 42
    fixture.userId = 7
    mount()
    clickButton('强制转办')
    await nextTick()
    await Promise.resolve()
    expect(get).toHaveBeenCalledWith(42)
    clickButton('pick-user')
    await fillReason('转办')
    clickButton('确定')
    await nextTick()
    await Promise.resolve()
    expect(forceTransfer).toHaveBeenCalledWith({
      instanceId: 42,
      toUserId: 7,
      reason: '转办',
      nodeId: 'approval',
    })
  })

  it('does not submit an unsafe number id', async () => {
    fixture.instanceId = Number.MAX_SAFE_INTEGER + 2
    mount()
    clickButton('终止')
    await fillReason('不能交')
    clickButton('确定')
    await nextTick()
    expect(terminate).not.toHaveBeenCalled()
    expect(warning).toHaveBeenCalled()
  })
})
