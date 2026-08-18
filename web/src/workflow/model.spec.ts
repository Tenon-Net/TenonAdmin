import { reactive } from 'vue'
import { describe, expect, it } from 'vitest'
import {
  cloneModel,
  cloneNode,
  createApprovalNode,
  createCcNode,
  createDefaultModel,
  flattenChain,
  insertAfter,
  removeNode,
  validateM1Model,
} from './model'

describe('workflow/model M1 chain', () => {
  it('createDefaultModel is start-only', () => {
    const m = createDefaultModel()
    expect(m.version).toBe(1)
    expect(m.root.type).toBe('start')
    expect(m.root.next).toBeNull()
    expect(validateM1Model(m)).toEqual([])
  })

  it('insert approval then cc keeps serial order', () => {
    const m = createDefaultModel()
    const ap = createApprovalNode({ name: '部门审批' })
    const cc = createCcNode({ name: '抄送人事' })
    expect(insertAfter(m.root, m.root.id, ap)).toBe(true)
    expect(insertAfter(m.root, ap.id, cc)).toBe(true)
    expect(flattenChain(m.root).map((n) => n.type)).toEqual(['start', 'approval', 'cc'])
    expect(validateM1Model(m)).toEqual([])
  })

  it('removeNode cannot delete start', () => {
    const m = createDefaultModel()
    const ap = createApprovalNode()
    insertAfter(m.root, 'start', ap)
    expect(removeNode(m.root, 'start')).toBe(false)
    expect(removeNode(m.root, ap.id)).toBe(true)
    expect(m.root.next).toBeNull()
  })

  it('cloneModel deep-copies', () => {
    const m = createDefaultModel()
    insertAfter(m.root, 'start', createApprovalNode())
    const c = cloneModel(m)
    c.root.name = 'X'
    expect(m.root.name).toBe('')
  })

  it('cloneNode deep-copies through the chain', () => {
    const a = createApprovalNode({ name: 'A' })
    insertAfter(a, a.id, createCcNode({ name: 'B' }))
    const c = cloneNode(a)
    c.next!.name = 'Z'
    expect(a.next!.name).toBe('B')
  })

  it('cloneModel/cloneNode survive a reactive proxy (the bug this guards against)', () => {
    const m = reactive(createDefaultModel())
    expect(() => cloneModel(m)).not.toThrow()
    expect(() => cloneNode(reactive(createApprovalNode()))).not.toThrow()
  })
})
