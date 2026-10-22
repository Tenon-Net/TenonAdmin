import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import { ApiError } from '@/api'
import zhCN from '@/locales/zh-CN'
import { createApprovalNode, createDefaultModel } from '@/workflow/model'
import Designer from './designer.vue'

const { warning, error, get, update, publish, add, replace, setTabTitle } = vi.hoisted(() => ({
  warning: vi.fn(),
  error: vi.fn(),
  get: vi.fn(),
  update: vi.fn().mockResolvedValue(true),
  publish: vi.fn(),
  add: vi.fn(),
  replace: vi.fn(),
  setTabTitle: vi.fn(),
}))
const routeState = vi.hoisted(() => ({
  query: { id: '7' } as { id?: string },
  setId: undefined as undefined | ((id: string | undefined) => void),
}))

vi.mock('naive-ui', async (importOriginal) => ({
  ...await importOriginal<typeof import('naive-ui')>(),
  useMessage: () => ({ error, warning, success: vi.fn(), info: vi.fn() }),
}))
vi.mock('vue-router', async () => {
  const { reactive } = await import('vue')
  const route = reactive({ query: routeState.query, params: {}, path: '/workflow/definition/designer' })
  routeState.setId = (id) => { route.query.id = id }
  replace.mockImplementation(async (location: { query: { id?: string } }) => {
    route.query.id = location.query.id
  })
  return {
    useRoute: () => route,
    useRouter: () => ({ replace, back: vi.fn() }),
  }
})
vi.mock('@/api/workflow', () => ({
  wfDefinitionApi: { get, update, publish, add },
}))
vi.mock('@/composables/useTabTitle', () => ({ useTabTitle: () => setTabTitle }))
vi.mock('@/components/AppIcon.vue', () => ({
  default: defineComponent({ name: 'AppIcon', setup: () => () => h('span') }),
}))
vi.mock('./components/WfNodeTree.vue', () => ({
  default: defineComponent({
    name: 'WfNodeTree',
    props: { errorMessages: { type: Object, required: true } },
    setup: (props) => () => h('div', { 'data-testid': 'node-errors' }, JSON.stringify(props.errorMessages)),
  }),
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
beforeEach(() => {
  vi.clearAllMocks()
})

afterEach(() => {
  app?.unmount()
  app = undefined
  routeState.setId?.('7')
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

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function definition(id: number, name: string) {
  return {
    id,
    name,
    groupName: 'HR',
    icon: null,
    status: 0,
    model: validModel(),
  }
}

describe('workflow designer publish notice', () => {
  it('按原始十进制字符串 Id 加载大 Id 流程定义', async () => {
    const id = '9007199254740993'
    routeState.setId?.(id)
    mount()

    await vi.waitFor(() => expect(get).toHaveBeenCalledWith(id))
  })

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

  it('路由快速切换时忽略旧定义的迟到响应', async () => {
    const first = deferred<ReturnType<typeof definition>>()
    const second = deferred<ReturnType<typeof definition>>()
    get.mockReset()
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)
    const host = mount()
    routeState.setId?.('8')
    await nextTick()
    expect(get).toHaveBeenNthCalledWith(2, '8')

    second.resolve(definition(8, '采购'))
    await vi.waitFor(() => expect(host.querySelector('input')?.value).toBe('采购'))
    first.resolve(definition(7, '请假'))
    await nextTick()

    expect(host.querySelector('input')?.value).toBe('采购')
  })

  it('新建后由路由 watcher 只加载一次定义', async () => {
    routeState.setId?.(undefined)
    add.mockResolvedValue('9')
    get.mockReset().mockResolvedValue(definition(9, '新流程'))
    const host = mount()
    const input = host.querySelector<HTMLInputElement>('input')
    expect(input).toBeTruthy()
    input!.value = '新流程'
    input!.dispatchEvent(new Event('input'))
    await nextTick()
    const button = [...host.querySelectorAll('button')].find((item) => item.textContent?.includes('新建'))
    expect(button).toBeTruthy()
    button!.click()

    await vi.waitFor(() => expect(get).toHaveBeenCalledWith('9'))
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('保存旧定义完成后不再改写新路由的标签标题', async () => {
    const pendingUpdate = deferred<unknown>()
    update.mockReturnValueOnce(pendingUpdate.promise)
    const host = mount()
    await vi.waitFor(() => expect(host.querySelector('input')?.value).toBe('请假'))
    const saveButton = [...host.querySelectorAll('button')].find((item) => item.textContent?.includes('保存'))
    expect(saveButton).toBeTruthy()
    saveButton!.click()
    await vi.waitFor(() => expect(update).toHaveBeenCalledOnce())

    get.mockResolvedValueOnce(definition(8, '采购'))
    routeState.setId?.('8')
    await vi.waitFor(() => expect(host.querySelector('input')?.value).toBe('采购'))
    setTabTitle.mockClear()
    pendingUpdate.resolve(true)
    await pendingUpdate.promise
    await nextTick()

    expect(setTabTitle).not.toHaveBeenCalled()
  })

  it('发布旧定义失败后不把节点错误写入新路由', async () => {
    const pendingPublish = deferred<unknown>()
    publish.mockReturnValueOnce(pendingPublish.promise)
    const host = mount()
    await vi.waitFor(() => expect(host.querySelector('input')?.value).toBe('请假'))
    const publishButton = [...host.querySelectorAll('button')].find((item) => item.textContent?.includes('发布'))
    expect(publishButton).toBeTruthy()
    publishButton!.click()
    await vi.waitFor(() => expect(publish).toHaveBeenCalledOnce())

    get.mockResolvedValueOnce(definition(8, '采购'))
    routeState.setId?.('8')
    await vi.waitFor(() => expect(host.querySelector('input')?.value).toBe('采购'))
    pendingPublish.reject(new ApiError(48001, 'error.code.48001', { nodeId: 'ap1' }))
    await expect(pendingPublish.promise).rejects.toBeInstanceOf(ApiError)
    await nextTick()

    expect(host.querySelector('[data-testid="node-errors"]')?.textContent).toBe('{}')
  })
})
