import { describe, it, expect } from 'vitest'
import { blankForm, rowToInput } from './orgForm'
import type { SysOrg } from '@/types/api'

describe('orgForm', () => {
  describe('blankForm', () => {
    it('默认:parentId 0=根、启用、sort 0、name/code 空、category null', () => {
      expect(blankForm()).toEqual({ parentId: 0, name: '', code: '', category: null, sort: 0, enabled: true })
    })
    it('传 parentId → 用作上级(新增下级)', () => {
      expect(blankForm(7).parentId).toBe(7)
    })
  })

  describe('rowToInput', () => {
    const row: SysOrg = { id: 5, parentId: 2, name: '研发部', code: 'RD', category: 'dept', sort: 3, enabled: false, createTime: '2026-07-01' }
    it('逐字段映射全量入参(全量 update,漏字段会抹空该行)', () => {
      expect(rowToInput(row)).toEqual({ parentId: 2, name: '研发部', code: 'RD', category: 'dept', sort: 3, enabled: false })
    })
    it('category 缺失(null/undefined)归一 null', () => {
      expect(rowToInput({ ...row, category: null }).category).toBeNull()
      expect(rowToInput({ ...row, category: undefined }).category).toBeNull()
    })
    it('不带 id/createTime(id 走 URL path、createTime 是审计字段)', () => {
      const out = rowToInput(row)
      expect('id' in out).toBe(false)
      expect('createTime' in out).toBe(false)
    })
  })
})
