import { describe, it, expect, vi } from 'vitest'
import { toProTable } from './toProTable'
import type { PageQuery } from './toProTable'

/** 捕获 fetcher 收到的入参,返回固定 {items,total}。 */
function capturing(items: unknown[] = [{ id: 1 }], total = 42) {
  const seen: (PageQuery & Record<string, unknown>)[] = []
  const fetcher = vi.fn(async (q: PageQuery & Record<string, unknown>) => {
    seen.push(q)
    return { items, total }
  })
  return { fetcher, seen, run: toProTable(fetcher) }
}

describe('toProTable —— ProTable request 契约', () => {
  it('items→data、total 透传、success=true', async () => {
    const rows = [{ id: 1 }, { id: 2 }]
    const { run } = capturing(rows, 57)
    const res = await run({ current: 1, pageSize: 10 }, {})
    expect(res).toEqual({ data: rows, total: 57, success: true })
  })

  it('current→page、pageSize 透传', async () => {
    const { seen, run } = capturing()
    await run({ current: 3, pageSize: 20 }, {})
    expect(seen[0]).toMatchObject({ page: 3, pageSize: 20 })
  })

  it('current/pageSize 缺省 → page=1, pageSize=10', async () => {
    const { seen, run } = capturing()
    await run({}, {})
    expect(seen[0]).toMatchObject({ page: 1, pageSize: 10 })
  })

  it('降序:{f:descend} → sortField=f, sortOrder=desc(后端只认 desc)', async () => {
    const { seen, run } = capturing()
    await run({ current: 1, pageSize: 10 }, { createTime: 'descend' })
    expect(seen[0]).toMatchObject({ sortField: 'createTime', sortOrder: 'desc' })
  })

  it('升序:{f:ascend} → sortField=f, sortOrder=asc', async () => {
    const { seen, run } = capturing()
    await run({ current: 1, pageSize: 10 }, { name: 'ascend' })
    expect(seen[0]).toMatchObject({ sortField: 'name', sortOrder: 'asc' })
  })

  it('无排序(空 sort)→ sortField/sortOrder 都为 undefined', async () => {
    const { seen, run } = capturing()
    await run({ current: 1, pageSize: 10 }, {})
    expect(seen[0]!.sortField).toBeUndefined()
    expect(seen[0]!.sortOrder).toBeUndefined()
  })

  it('排序被清空 {字段: null} → sortField 也不传(不能只看字段名)', async () => {
    // antd 清掉某列排序时传的是 `{createTime: null}`,字段键还在但 order 为 null。
    // 若只按字段名取 sortField(不看 order),会把一个"没方向"的排序传给后端 —— 空 sort 那条测不出这个,
    // 因为空 sort 时字段名本身也是 undefined,`order ? field` 那道守卫被短路了。
    const { seen, run } = capturing()
    await run({ current: 1, pageSize: 10 }, { createTime: null })
    expect(seen[0]!.sortField).toBeUndefined()
    expect(seen[0]!.sortOrder).toBeUndefined()
  })

  it('搜索表单字段(current/pageSize 之外)原样透传给 fetcher', async () => {
    const { seen, run } = capturing()
    await run({ current: 1, pageSize: 10, account: 'admin', orgId: 5 }, {})
    expect(seen[0]).toMatchObject({ account: 'admin', orgId: 5 })
    // `current` 已映射成 `page`,不再以 current 名混进搜索条件(避免各页 api 收到既有 page 又有 current)。
    expect(seen[0]).not.toHaveProperty('current')
    expect(seen[0]).toHaveProperty('page') // 它变成了 page
  })
})
