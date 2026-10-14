import { describe, expect, it } from 'vitest'
import { defaultMonitorNodeId, monitorNodeOptions } from './nodes'

const model = {
  version: 1,
  root: {
    id: 'start', type: 'start', name: '发起',
    next: {
      id: 'parallel', type: 'parallel', name: '并行',
      parallelArms: [
        { id: 'fin', name: '财务', next: { id: 'fin-approve', type: 'approval', name: '财务审批', next: null } },
        { id: 'hr', name: '人事', next: { id: 'hr-approve', type: 'approval', name: '人事审批', next: null } },
      ],
      next: null,
    },
  },
}

describe('monitorNodeOptions', () => {
  it('单个待办节点可预选', () => {
    const options = monitorNodeOptions({
      currentTasks: [{ nodeId: 'fin-approve', nodeName: '财务审批' }],
    })
    expect(options).toEqual([{ id: 'fin-approve', name: '财务审批' }])
    expect(defaultMonitorNodeId(options)).toBe('fin-approve')
  })

  it('并行待办不预选,阻断节点补进选项并用模型名称', () => {
    const options = monitorNodeOptions({
      currentTasks: [{ nodeId: 'fin-approve', nodeName: '财务审批' }],
      blockedNodeIds: ['hr-approve', 'fin-approve'],
      model,
    })
    expect(options).toEqual([
      { id: 'fin-approve', name: '财务审批' },
      { id: 'hr-approve', name: '人事审批' },
    ])
    expect(defaultMonitorNodeId(options)).toBeNull()
  })

  it('只有阻断节点时预选该节点', () => {
    const options = monitorNodeOptions({
      currentTasks: [],
      blockedNodeIds: ['hr-approve'],
      model,
    })
    expect(defaultMonitorNodeId(options)).toBe('hr-approve')
    expect(options[0]?.name).toBe('人事审批')
  })
})
