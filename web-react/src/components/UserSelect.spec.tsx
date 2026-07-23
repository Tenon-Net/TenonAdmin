import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, cleanup } from '@testing-library/react'
import type { UserItem } from '@/types/api'

// mock ApiSelect,捕获 UserSelect 传下去的 fetch / reloadOn,单独验它的接线(不趟真 antd Select)。
let captured: { fetch?: (k: string) => Promise<unknown>; reloadOn?: unknown } = {}
vi.mock('./ApiSelect', () => ({
  ApiSelect: (p: { fetch?: (k: string) => Promise<unknown>; reloadOn?: unknown }) => {
    captured = p
    return null
  },
}))
vi.mock('@/api', () => ({ userApi: { page: vi.fn() } }))

import { userApi } from '@/api'
import { UserSelect, toUserOption } from './UserSelect'

const u = (id: number, name: string, account: string) => ({ id, name, account }) as UserItem

afterEach(() => {
  cleanup()
  captured = {}
  vi.mocked(userApi.page).mockReset()
})

describe('toUserOption(纯逻辑)', () => {
  it('label「姓名(账号)」,value=id', () => {
    expect(toUserOption(u(5, '张三', 'zhangsan'))).toEqual({ label: '张三(zhangsan)', value: 5 })
  })
})

describe('UserSelect 接线', () => {
  it('fetch:关键字→name、orgId 透传、pageSize 默认 50、映射 name(账号)', async () => {
    vi.mocked(userApi.page).mockResolvedValue({ items: [u(5, '张三', 'zhangsan')], total: 1 })
    render(<UserSelect orgId={7} />)
    const opts = await captured.fetch!('张')
    expect(userApi.page).toHaveBeenCalledWith({ page: 1, pageSize: 50, name: '张', orgId: 7 })
    expect(opts).toEqual([{ label: '张三(zhangsan)', value: 5 }])
  })

  it('空关键字→name undefined;orgId=null→orgId undefined;pageSize 可覆盖', async () => {
    vi.mocked(userApi.page).mockResolvedValue({ items: [], total: 0 })
    render(<UserSelect orgId={null} pageSize={20} />)
    await captured.fetch!('')
    expect(userApi.page).toHaveBeenCalledWith({ page: 1, pageSize: 20, name: undefined, orgId: undefined })
  })

  it('reloadOn 绑 orgId(部门变化即重拉)', () => {
    render(<UserSelect orgId={9} />)
    expect(captured.reloadOn).toBe(9)
  })
})
