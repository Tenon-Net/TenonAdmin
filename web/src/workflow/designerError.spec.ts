import { describe, expect, it } from 'vitest'
import '@/locales'
import { ApiError } from '@/api'
import { translateError } from '@/utils/error'
import type { WfNode } from '@/workflow/schema'
import { withDesignerNodeName } from './designerError'

const root = {
  id: 'start',
  type: 'start',
  name: '发起',
  next: { id: 'apmv69l3x41', type: 'approval', name: '财务审批', next: null },
} as WfNode

describe('withDesignerNodeName', () => {
  it('发布错误用节点名称而不是内部 id', () => {
    const err = new ApiError(48002, 'error.code.48002', {
      reason: 'assigneeUsersRequired',
      nodeId: 'apmv69l3x41',
    }, 'error.code.48002')
    expect(translateError(withDesignerNodeName(err, root))).toBe('财务审批：指定成员不能为空')
  })
})
