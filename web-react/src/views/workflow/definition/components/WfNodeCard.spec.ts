import { describe, expect, it } from 'vitest'
import type { TFunction } from 'i18next'
import { nodeCardBody } from './WfNodeCard'

const t = ((key: string, options?: { defaultValue?: string }) => {
  const text: Record<string, string> = {
    'workflow.provider.user': '指定成员',
    'workflow.node.placeholder.cc': '请选择抄送人',
  }
  return text[key] ?? options?.defaultValue ?? key
}) as TFunction

describe('workflow node card body', () => {
  it('shows cc recipient names from the designer directory and from runtime display names', () => {
    const designer = nodeCardBody({
      id: 'cc1',
      type: 'cc',
      name: '抄送',
      props: { assignee: { provider: 'user', params: { userIds: ['858'] } } },
      next: null,
    }, t, { '858': '赵抄送' })
    expect(designer).toEqual({ text: '赵抄送', empty: false })

    const unresolved = nodeCardBody({
      id: 'cc1',
      type: 'cc',
      name: '抄送',
      props: { assignee: { provider: 'user', params: { userIds: ['858'] } } },
      next: null,
    }, t)
    expect(unresolved.text).toBe('指定成员 · 1')

    const runtime = nodeCardBody({
      id: 'cc1',
      type: 'cc',
      name: '抄送',
      props: { assignee: { provider: 'user', params: { userNames: ['赵抄送', '李四'] } } },
      next: null,
    }, t)
    expect(runtime).toEqual({ text: '赵抄送、李四', empty: false })
  })
})
