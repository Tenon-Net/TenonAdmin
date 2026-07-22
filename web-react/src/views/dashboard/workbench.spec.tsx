import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor } from '@testing-library/react'
import '@/locales'
import { dashboardApi } from '@/api'
import type { DashboardSummary } from '@/types/api'

// Chart 是 canvas(echarts),happy-dom 单测不了 → mock 掉并捕获传入的 option。
const { chartOptions } = vi.hoisted(() => ({ chartOptions: [] as any[] }))
vi.mock('@/components/Chart', () => ({ Chart: (p: { option: unknown }) => { chartOptions.push(p.option); return <div data-testid="chart" /> } }))

import Workbench from './workbench'

const SUMMARY: DashboardSummary = {
  roles: 3, users: 12, perms: 40, onlineSessions: 5,
  trendDays: ['d1', 'd2'], trendLogins: [1, 2], trendActiveUsers: [3, 4],
}

beforeEach(() => {
  chartOptions.length = 0
  vi.spyOn(dashboardApi, 'summary').mockResolvedValue(SUMMARY)
})
afterEach(() => { cleanup(); vi.restoreAllMocks() })

describe('Workbench', () => {
  it('4 统计卡按 summary 字段映射(不串字段)', async () => {
    render(<Workbench />)
    await waitFor(() => expect(screen.getByText('3')).toBeTruthy()) // roles
    expect(screen.getByText('12')).toBeTruthy() // users
    expect(screen.getByText('40')).toBeTruthy() // perms
    expect(screen.getByText('5')).toBeTruthy() // onlineSessions
    expect(screen.getByText('角色总数')).toBeTruthy()
    expect(screen.getByText('在线会话')).toBeTruthy()
  })
  it('趋势图取 trendDays、分布图取 roles/users/perms', async () => {
    // 图在 summary 载入前后各渲染一次 → 取最新一次(reverse+find),避开首渲染的空 summary。
    render(<Workbench />)
    const latestLine = () => [...chartOptions].reverse().find((o) => o.xAxis)
    await waitFor(() => expect(latestLine()?.xAxis?.data).toEqual(['d1', 'd2']))
    const pie = [...chartOptions].reverse().find((o) => o.series?.[0]?.type === 'pie')
    expect(pie.series[0].data.map((d: { value: number }) => d.value)).toEqual([3, 12, 40])
  })
})
