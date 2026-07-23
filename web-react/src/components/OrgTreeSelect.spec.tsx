import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor } from '@testing-library/react'
import type { SysOrg } from '@/types/api'

vi.mock('@/api', () => ({ orgApi: { list: vi.fn() } }))
import { orgApi } from '@/api'
import { OrgTreeSelect, orgTreeData } from './OrgTreeSelect'

const org = (id: number, parentId: number, name: string): SysOrg =>
  ({ id, parentId, name, code: String(id), sort: 0, enabled: true }) as SysOrg

// 1 → 2 → 3(总部→研发→前端组),4 独立(分公司)
const flat: SysOrg[] = [org(1, 0, '总部'), org(2, 1, '研发'), org(3, 2, '前端组'), org(4, 0, '分公司')]
const ids = (nodes: { id: number; children?: unknown[] }[]): number[] =>
  nodes.flatMap((n) => [n.id, ...(n.children ? ids(n.children as typeof nodes) : [])])

afterEach(() => {
  cleanup()
  vi.mocked(orgApi.list).mockReset()
})

describe('orgTreeData(纯逻辑)', () => {
  it('无 exclude → 全量建树(4 个节点都在)', () => {
    expect(ids(orgTreeData(flat)).sort()).toEqual([1, 2, 3, 4])
  })
  it('exclude 中间节点的子树 → 剪掉它及后代', () => {
    // 剪 2 的子树 = {2,3};剩 1(因子被剪而无 children)、4
    expect(ids(orgTreeData(flat, 2)).sort()).toEqual([1, 4])
  })
  it('exclude 根 → 整棵消失,只剩兄弟', () => {
    expect(ids(orgTreeData(flat, 1)).sort()).toEqual([4])
  })
})

describe('OrgTreeSelect 渲染', () => {
  it('挂载拉 orgApi.list,渲染 TreeSelect + rest 透传', async () => {
    vi.mocked(orgApi.list).mockResolvedValue(flat)
    render(<OrgTreeSelect placeholder="选机构" />)
    expect(screen.getByText('选机构')).toBeTruthy() // rest 透传
    await waitFor(() => expect(orgApi.list).toHaveBeenCalled())
  })
})
