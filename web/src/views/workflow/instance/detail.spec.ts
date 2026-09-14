import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'
import Detail from './detail.vue'

const { getInstance, getHistory, takeBack, runConfirm } = vi.hoisted(() => ({
  getInstance: vi.fn(),
  getHistory: vi.fn(),
  takeBack: vi.fn(),
  runConfirm: vi.fn(),
}))

vi.mock('naive-ui', async (importOriginal) => ({
  ...await importOriginal<typeof import('naive-ui')>(),
  useMessage: () => ({ error: vi.fn(), warning: vi.fn() }),
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, path: '/workflow/instance/1/detail' }),
  useRouter: () => ({ push: vi.fn() }),
}))
vi.mock('@/api/workflow', () => ({
  wfInstanceApi: { get: getInstance, history: getHistory },
  wfTaskApi: { takeBack },
}))
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ run: runConfirm }) }))
vi.mock('@/composables/useTabTitle', () => ({ useTabTitle: () => vi.fn() }))
vi.mock('@/stores/tabs', () => ({ useTabsStore: () => ({ removeTab: vi.fn() }) }))
vi.mock('@/stores/user', () => ({ useUserStore: () => ({ userInfo: { userId: 1 } }) }))
vi.mock('@/components/DetailPage/index.vue', () => ({
  default: defineComponent({
    setup: (_, { slots }) => () => h('div', [slots.actions?.(), slots.default?.()]),
  }),
}))
vi.mock('@/components/UserSelect/index.vue', () => ({ default: { render: () => null } }))
vi.mock('../definition/components/WfNodeTree.vue', () => ({ default: { render: () => null } }))
vi.mock('../components/WfFormMount.vue', () => ({
  default: defineComponent({
    props: {
      formSchema: Object,
      variablesJson: String,
      permissions: Array,
    },
    setup: (props) => () => {
      const schema = props.formSchema as { fields?: Array<{ key: string; label: string }> } | undefined
      const values = JSON.parse(props.variablesJson ?? '{}') as Record<string, unknown>
      const permissions = props.permissions as Array<{ field: string; access: string }> | undefined
      return h('div', schema?.fields
        ?.filter((field) => permissions?.find((permission) => permission.field === field.key)?.access !== 'hidden')
        .map((field) => `${field.label}${String(values[field.key] ?? '')}`))
    },
  }),
}))
let app: App<Element> | undefined

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

async function mountDetail(detail: Record<string, unknown>) {
  getInstance.mockResolvedValueOnce(detail)
  getHistory.mockResolvedValueOnce([])
  const host = document.createElement('div')
  document.body.append(host)
  app = createApp(Detail, { id: 1 })
  app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
  app.mount(host)
  await nextTick()
  await nextTick()
  return host
}

describe('workflow instance detail variables', () => {
  it('lets the built-in form exclusively display variables while preserving consumer summaries', async () => {
    const base = {
      id: 1,
      definitionId: 1,
      definitionName: '请假',
      status: 1,
      variablesJson: JSON.stringify({ public: '公开', secret: '隐藏内容' }),
      hisTasks: [],
    }

    let host = await mountDetail({
      ...base,
      model: {
        version: 1,
        formSchema: { version: 1, fields: [{ key: 'public', label: '公开字段', type: 'text' }] },
        root: { id: 'start', type: 'start', name: '开始', next: null },
      },
    })
    expect(host.textContent).toContain('公开字段')
    expect(host.textContent).not.toContain('隐藏内容')

    app?.unmount()
    app = undefined
    host.remove()
    host = await mountDetail({ ...base, formComponent: 'biz/leave/form' })
    expect(host.textContent).toContain('隐藏内容')
  })

  it('applies current approval permissions without a pending task', async () => {
    const host = await mountDetail({
      id: 1,
      definitionId: 1,
      definitionName: '请假',
      status: 1,
      variablesJson: JSON.stringify({ public: '公开内容', secret: 'secret' }),
      currentNodeIds: ['approval'],
      hisTasks: [],
      model: {
        version: 1,
        formSchema: {
          version: 1,
          fields: [
            { key: 'public', label: '公开字段', type: 'text' },
            { key: 'secret', label: '敏感字段', type: 'text' },
          ],
        },
        root: {
          id: 'approval',
          type: 'approval',
          name: '审批',
          props: { formPerms: [{ field: 'secret', access: 'hidden' }] },
          next: null,
        },
      },
    })

    expect(host.textContent).toContain('公开内容')
    expect(host.textContent).not.toContain('secret')
  })

  it('keeps hidden fields out of terminal replay', async () => {
    const host = await mountDetail({
      id: 1,
      definitionId: 1,
      definitionName: '请假',
      status: 2,
      variablesJson: JSON.stringify({ public: '公开内容', secret: 'secret' }),
      currentNodeIds: [],
      visitedNodeIds: ['approval'],
      hisTasks: [],
      model: {
        version: 1,
        formSchema: {
          version: 1,
          fields: [
            { key: 'public', label: '公开字段', type: 'text' },
            { key: 'secret', label: '敏感字段', type: 'text' },
          ],
        },
        root: {
          id: 'start',
          type: 'start',
          name: '开始',
          next: {
            id: 'approval',
            type: 'approval',
            name: '审批',
            props: { formPerms: [{ field: 'secret', access: 'hidden' }] },
            next: null,
          },
        },
      },
    })

    expect(host.textContent).toContain('公开内容')
    expect(host.textContent).not.toContain('secret')
  })

  it('applies the current user history node permissions without a pending task', async () => {
    const host = await mountDetail({
      id: 1,
      definitionId: 1,
      definitionName: '历史审批',
      status: 2,
      variablesJson: JSON.stringify({ public: '公开内容', secret: 'secret' }),
      currentNodeIds: [],
      visitedNodeIds: [],
      hisTasks: [{ userId: 1, nodeId: 'historical-approval', action: 1 }],
      model: {
        version: 1,
        formSchema: {
          version: 1,
          fields: [
            { key: 'public', label: '公开字段', type: 'text' },
            { key: 'secret', label: '敏感字段', type: 'text' },
          ],
        },
        root: {
          id: 'start',
          type: 'start',
          name: '开始',
          next: {
            id: 'historical-approval',
            type: 'approval',
            name: '历史审批',
            props: { formPerms: [{ field: 'secret', access: 'hidden' }] },
            next: null,
          },
        },
      },
    })

    expect(host.textContent).toContain('公开内容')
    expect(host.textContent).not.toContain('secret')
  })

  it('replays parallel arm state and ordered event identity', async () => {
    const host = await mountDetail({
      id: 1,
      definitionName: '并行审批',
      status: 1,
      currentNodeIds: ['approval-a'],
      currentTasks: [{ taskId: 201, tokenId: 11, nodeVisitId: 101, nodeId: 'approval-a', nodeName: '臂 A' }],
      parallelForks: [{
        forkId: 7,
        parentTokenId: 10,
        parentNodeVisitId: 100,
        nodeId: 'parallel',
        nodeName: '并行会签',
        parentTokenStatus: 4,
        pendingArmCount: 1,
        status: 1,
        arms: [
          {
            forkId: 7,
            armId: 'arm-a',
            parentTokenId: 10,
            childTokenId: 11,
            status: 1,
            currentNodeId: 'approval-a',
            currentNodeName: '臂 A',
            childTokenStatus: 1,
          },
          {
            forkId: 7,
            armId: 'arm-b',
            parentTokenId: 10,
            childTokenId: 12,
            status: 2,
            childTokenStatus: 2,
            reason: 'completed',
          },
        ],
      }],
      model: { version: 1, root: { id: 'start', type: 'start', name: '开始', next: null } },
      hisTasks: [],
    })

    expect(host.textContent).toContain('并行分支')
    expect(host.textContent).toContain('并行会签')
    expect(host.textContent).toContain('arm-a')
    expect(host.textContent).toContain('臂 A')
    expect(host.textContent).toContain('等待汇合')
    expect(host.textContent).toContain('Token #11')
  })

  it('sends take-back to a concrete current task instead of the legacy first-task field', async () => {
    runConfirm.mockImplementation(async (action: () => Promise<unknown>) => {
      await action()
      return true
    })
    takeBack.mockResolvedValueOnce({})
    const host = await mountDetail({
      id: 1,
      definitionName: '并行拿回',
      status: 1,
      currentTaskId: 999,
      currentTasks: [
        { taskId: 201, tokenId: 11, nodeId: 'approval-a', nodeName: '臂 A' },
        { taskId: 202, tokenId: 12, nodeId: 'approval-b', nodeName: '臂 B' },
      ],
      myTakeBackTaskId: 201,
      hisTasks: [{ id: 1, userId: 1, action: 1, nodeId: 'before', nodeName: '前置', createTime: '' }],
    })

    const button = Array.from(host.querySelectorAll('button'))
      .find((item) => item.textContent?.includes('拿回'))
    expect(button).toBeTruthy()
    button?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await nextTick()
    const confirm = Array.from(document.body.querySelectorAll('button'))
      .find((item) => item.textContent?.trim() === '确定')
    confirm?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await nextTick()
    await nextTick()

    expect(takeBack).toHaveBeenCalledWith(expect.objectContaining({ taskId: 201 }))
    expect(takeBack).not.toHaveBeenCalledWith(expect.objectContaining({ taskId: 999 }))
  })
})
