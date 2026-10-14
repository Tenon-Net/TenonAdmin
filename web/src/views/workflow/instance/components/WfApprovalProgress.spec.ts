import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'
import type { ApprovalRound } from '@/workflow/approvalProgress'
import WfApprovalProgress from './WfApprovalProgress.vue'

let app: App<Element> | undefined

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

function mountProgress(rounds: ApprovalRound[]) {
  const host = document.createElement('div')
  document.body.append(host)
  app = createApp({ setup: () => () => h(WfApprovalProgress, { rounds }) })
  app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
  app.mount(host)
  return host
}

describe('workflow approval progress details', () => {
  it('hides an unknown historical mode while preserving participants and actions', () => {
    const host = mountProgress([{
      key: 'task:40',
      current: false,
      taskId: 40,
      approvalMode: null,
      participants: [{ actorId: 1, userId: 1, displayName: '张三', status: 2, sort: 1 }],
      history: [{
        id: 1, taskId: 40, nodeId: 'approval', userId: 1, operatorName: '张三',
        action: 1, comment: '兜底办理完成',
      }],
    }])

    expect(host.querySelector('.wf-progress-person')?.textContent).toContain('张三')
    expect(host.querySelector('.wf-progress-person')?.textContent).toContain('同意')
    expect(host.querySelector('.wf-progress-action')?.textContent).toContain('兜底办理完成')
    expect(host.textContent).not.toContain('或签')
    expect(host.textContent).not.toContain('会签')
  })

  it('shows proven historical actions before the closed actor status', () => {
    const host = mountProgress([{
      key: 'task:10',
      current: false,
      taskId: 10,
      participants: [
        { actorId: 1, userId: 1, displayName: '张三', status: 3, sort: 1 },
        { actorId: 2, userId: 2, displayName: '李四', status: 3, sort: 2 },
        { actorId: 3, userId: 3, displayName: '王五', status: 3, sort: 3 },
      ],
      history: [
        { id: 1, taskId: 10, nodeId: 'approval', userId: 1, operatorName: '张三', action: 1 },
        { id: 2, taskId: 10, nodeId: 'approval', userId: 2, operatorName: '李四', action: 4 },
      ],
    }])

    const rows = [...host.querySelectorAll('.wf-progress-person')]
    expect(rows.find((row) => row.textContent?.includes('张三'))?.textContent).toContain('同意')
    expect(rows.find((row) => row.textContent?.includes('李四'))?.textContent).toContain('退回')
    expect(rows.find((row) => row.textContent?.includes('王五'))?.textContent).toContain('无需处理')
  })

  it('does not let an earlier action hide a current pending assignment for the same user', () => {
    const host = mountProgress([{
      key: 'task:20',
      current: true,
      taskId: 20,
      participants: [{ actorId: 2, userId: 1, displayName: '张三', status: 1, sort: 2 }],
      history: [{ id: 1, taskId: 20, nodeId: 'approval', userId: 1, operatorName: '张三', action: 3 }],
    }])

    expect(host.querySelector('.wf-progress-person')?.textContent).toContain('待审批')
  })

  it('shows how many countersign approvers have agreed', () => {
    const host = mountProgress([{
      key: 'task:50',
      current: true,
      taskId: 50,
      approvalMode: 'all',
      participants: [
        { actorId: 1, userId: 1, displayName: '张三', status: 2, sort: 1 },
        { actorId: 2, userId: 2, displayName: '李四', status: 1, sort: 2 },
        { actorId: 3, userId: 3, displayName: '王五', status: 0, sort: 3 },
      ],
      history: [],
    }])

    expect(host.textContent).toContain('已同意 1/3')
    expect(host.textContent).toContain('待审批')
    expect(host.textContent).toContain('等待轮到')
  })

  it('labels automatic history as system handling instead of a manual user action', () => {
    const host = mountProgress([{
      key: 'task:30',
      current: false,
      taskId: 30,
      participants: [{ actorId: 1, userId: 1, displayName: '张三', status: 3, sort: 1 }],
      history: [{
        id: 1,
        taskId: 30,
        nodeId: 'approval',
        userId: 1,
        operatorName: '张三',
        action: 1,
        isAutomatic: true,
      }],
    }])

    const row = host.querySelector('.wf-progress-person')
    expect(row?.textContent).toContain('系统自动处理（代张三）')
    expect(row?.textContent).toContain('同意')
  })
})
