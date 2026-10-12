import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { jobApi } from '@/api'
import type { JobDashboard } from '@/types/api'
import { CachedPageActiveProvider } from '@/layouts/CachedPageActive'
import JobMonitorPage from './index'

vi.mock('@/components/Chart', () => ({ Chart: () => <div data-testid="chart" /> }))

const DASHBOARD: JobDashboard = {
  todaySuccess: 7,
  todayFailed: 1,
  running: 2,
  totalJobs: 10,
  statusCounts: { Ready: 7, Paused: 1, Completed: 1, Panic: 1 },
  trend: [],
  upcoming: [],
  nodes: [],
}

const view = (active: boolean) => (
  <AntdApp>
    <CachedPageActiveProvider active={active}>
      <JobMonitorPage />
    </CachedPageActiveProvider>
  </AntdApp>
)

beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.useRealTimers()
})

describe('JobMonitorPage 缓存生命周期', () => {
  it('仅活跃时立即刷新并每 15 秒轮询；失活停表，恢复后重新开始', async () => {
    const request = vi.spyOn(jobApi, 'dashboard').mockResolvedValue(DASHBOARD)
    const mounted = render(view(true))
    await act(async () => {})
    expect(request).toHaveBeenCalledTimes(1)

    await act(() => vi.advanceTimersByTimeAsync(15_000))
    expect(request).toHaveBeenCalledTimes(2)

    mounted.rerender(view(false))
    await act(() => vi.advanceTimersByTimeAsync(30_000))
    expect(request).toHaveBeenCalledTimes(2)

    mounted.rerender(view(true))
    await act(async () => {})
    expect(request).toHaveBeenCalledTimes(3)
    await act(() => vi.advanceTimersByTimeAsync(15_000))
    expect(request).toHaveBeenCalledTimes(4)
  })

  it('失活后忽略已经在途的迟到响应', async () => {
    let resolve!: (value: JobDashboard) => void
    vi.spyOn(jobApi, 'dashboard').mockReturnValue(new Promise((done) => { resolve = done }))
    const mounted = render(view(true))
    mounted.rerender(view(false))
    await act(async () => resolve({ ...DASHBOARD, todaySuccess: 99 }))
    expect(screen.queryByText('99')).toBeNull()
  })
})
