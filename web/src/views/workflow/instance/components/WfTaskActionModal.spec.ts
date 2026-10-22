import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'
import WfTaskActionModal from './WfTaskActionModal.vue'

const { approve, runConfirm } = vi.hoisted(() => ({
  approve: vi.fn(),
  runConfirm: vi.fn(),
}))

vi.mock('naive-ui', async (importOriginal) => ({
  ...await importOriginal<typeof import('naive-ui')>(),
  useMessage: () => ({ error: vi.fn(), warning: vi.fn() }),
}))
vi.mock('@/api/workflow', () => ({
  wfInstanceApi: { cancel: vi.fn(), resubmit: vi.fn() },
  wfTaskApi: {
    approve,
    reject: vi.fn(),
    transfer: vi.fn(),
    return: vi.fn(),
    delegate: vi.fn(),
    addSign: vi.fn(),
    removeSign: vi.fn(),
    takeBack: vi.fn(),
    urge: vi.fn(),
  },
}))
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ run: runConfirm }) }))
vi.mock('@/components/UserSelect/index.vue', () => ({ default: { render: () => null } }))
vi.mock('../../components/WfFormMount.vue', () => ({
  default: defineComponent({ expose: ['validate'], setup: (_, { expose }) => {
    expose({ validate: () => true })
    return () => null
  } }),
}))

let app: App<Element> | undefined

beforeEach(() => {
  vi.clearAllMocks()
})

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

async function mountModal(onCompleted: () => void, instanceId = ref(1)) {
  const modal = ref<InstanceType<typeof WfTaskActionModal> | null>(null)
  const host = document.createElement('div')
  document.body.append(host)
  const Root = defineComponent({
    setup: () => () => h('div', [
      h('button', { onClick: () => modal.value?.open('approve') }, 'open'),
      h(WfTaskActionModal, {
        ref: modal,
        instanceId: instanceId.value,
        detail: { id: 1, definitionName: '请假', status: 1, starterUserId: 2, hisTasks: [] },
        pendingTasks: [{ taskId: 5, instanceId: 1, nodeId: 'approval', nodeName: '主管' }],
        currentTasks: [],
        variablesJson: '{"days":2}',
        validateCurrentForm: () => true,
        onCompleted,
      }),
    ]),
  })
  app = createApp(Root)
  app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
  app.mount(host)
  await nextTick()
  host.querySelector('button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  await nextTick()
}

describe('WfTaskActionModal', () => {
  it('实例切换后旧提交不能触发新实例的完成事件', async () => {
    let resolve!: () => void
    approve.mockImplementationOnce(() => new Promise<void>((done) => { resolve = done }))
    runConfirm.mockImplementation(async (action: () => Promise<unknown>) => { await action(); return true })
    const completed = vi.fn()
    const instanceId = ref(1)
    await mountModal(completed, instanceId)
    clickConfirm()
    await expect.poll(() => approve.mock.calls.length).toBe(1)
    instanceId.value = 2
    await nextTick()
    resolve()
    await Promise.resolve()
    await nextTick()
    await Promise.resolve()
    expect(completed).not.toHaveBeenCalled()
  })

  it('网络结果不确定时保留草稿和 requestId，成功后只发出 completed', async () => {
    approve.mockRejectedValueOnce(new Error('network')).mockResolvedValueOnce({})
    runConfirm.mockImplementation(async (action: () => Promise<unknown>) => {
      try {
        await action()
        return true
      } catch {
        return false
      }
    })
    const completed = vi.fn()
    await mountModal(completed)

    const textarea = document.body.querySelector('textarea')
    expect(textarea).toBeTruthy()
    setNativeValue(textarea!, '同意，请归档')
    await nextTick()

    clickConfirm()
    await expect.poll(() => approve.mock.calls.length).toBe(1)
    expect(document.body.querySelector('textarea')?.value).toBe('同意，请归档')

    clickConfirm()
    await expect.poll(() => approve.mock.calls.length).toBe(2)
    await expect.poll(() => completed.mock.calls.length).toBe(1)

    const first = approve.mock.calls[0]?.[0]
    const second = approve.mock.calls[1]?.[0]
    expect(first).toMatchObject({ taskId: 5, comment: '同意，请归档', variablesJson: '{"days":2}' })
    expect(first.requestId).toBeTruthy()
    expect(second.requestId).toBe(first.requestId)
    expect(document.body.querySelector('textarea')?.value).toBe('')
  })
})

function clickConfirm() {
  const button = Array.from(document.body.querySelectorAll('button'))
    .find((item) => item.textContent?.trim() === '确定')
  expect(button).toBeTruthy()
  button?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
}

function setNativeValue(element: HTMLTextAreaElement, value: string) {
  const descriptor = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')
  descriptor?.set?.call(element, value)
  element.dispatchEvent(new Event('input', { bubbles: true }))
}
