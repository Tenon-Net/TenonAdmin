import { describe, expect, it } from 'vitest'
import { toParticipantOption } from './options'

describe('toParticipantOption', () => {
  it('普通用户是姓名(账号)', () => {
    expect(toParticipantOption({ id: 8, name: '李四', account: 'lisi' })).toEqual({
      label: '李四(lisi)',
      value: 8,
    })
  })

  it('发起人禁用并附上原因,雪花 id 按字符串比较', () => {
    const option = toParticipantOption(
      { id: '858924034068480', name: '张申请', account: 'zhang-apply' },
      ['858924034068480'],
      '不能选择发起人，否则申请人可以审批自己的单据',
    )
    expect(option.disabled).toBe(true)
    expect(option.label).toBe('张申请(zhang-apply)（不能选择发起人，否则申请人可以审批自己的单据）')
  })
})
