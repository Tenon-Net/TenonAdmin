import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import { ApiError } from '@/api'
import zhCN from '@/locales/zh-CN'
import { createApprovalNode, createDefaultModel } from '@/workflow/model'
import Designer from './designer.vue'

const { warning, error, get, update, publish } = vi.hoisted(() => ({
  warning: vi.fn(),
  error: vi.fn(),
  get: vi.fn(),
  update: vi.fn().mockResolvedValue(true),
  publish: vi.fn(),
}))

vi.mock('naive-ui', async (importOriginal) => ({
  ...await importOriginal<typeof import('naive-ui')>(),
  useMessage: () => ({ error, warning, success: vi.fn(), info: vi.fn() }),
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ query: { id: '7' }, params: {}, path: '/workflow/definition/designer' }),
  useRouter: () => ({ replace: vi.fn(), back: vi.fn() }),
}))
vi.mock('@/api/workflow', () => ({
  wfDefinitionApi: { get, update, publish, add: vi.fn() },
}))
vi.mock('@/composables/useTabTitle', () => ({ useTabTitle: () => vi.fn() }))
vi.mock('@/components/AppIcon.vue', () => ({
  default: defineComponent({ name: 'AppIcon', setup: () => () => h('span') }),
}))
vi.mock('./components/WfNodeTree.vue', () => ({
  default: defineComponent({ name: 'WfNodeTree', setup: () => () => h('div') }),
}))
vi.mock('./components/WfConfigDrawer.vue', () => ({
  default: defineComponent({ name: 'WfConfigDrawer', setup: () => () => null }),
}))

function validModel() {
  const model = createDefaultModel()
  model.root.next = createApprovalNode({ id: 'ap1', name: '经理审批' })
  return model
}

let app: App<Element> | undefined
afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

function mount() {
  get.mockResolvedValue({
    id: 7,
    name: '请假',
    groupName: 'HR',
    icon: null,
    status: 0,
    model: validModel(),
  })
  const host = document.createElement('div')
  document.body.append(host)
  app = createApp(Designer)
  app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
  app.mount(host)
  return host
}

describe('workflow designer publish notice', () => {
  it('内容没有变化时用警告提示,其它发布失败仍是错误', async () => {
    warning.mockReset()
    error.mockReset()
    update.mockClear()
    publish.mockReset()
    publish
      .mockRejectedValueOnce(new ApiError(48052, 'error.code.48052'))
      .mockRejectedValueOnce(new ApiError(48001, 'error.code.48001'))
    const host = mount()
    await vi.waitFor(() => {
      const input = host.querySelector('input')
      expect(input?.value).toBe('请假')
    })

    const clickPublish = () => {
      const button = [...host.querySelectorAll('button')].find((item) => item.textContent?.includes('发布'))
      expect(button).toBeTruthy()
      button!.click()
    }
    clickPublish()
    await vi.waitFor(() => expect(warning).toHaveBeenCalledWith('流程内容没有变化，不会生成新版本'))
    expect(error).not.toHaveBeenCalled()

    clickPublish()
    await vi.waitFor(() => expect(error).toHaveBeenCalledWith('流程版本不存在或模型无效'))
  })
})
