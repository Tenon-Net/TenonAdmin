import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { monitorApi } from '@/api'
import type { ServerInfoOutput } from '@/types/api'

import MonitorPage from './index'

const fixture = (): ServerInfoOutput => ({
  machineName: 'HOST', osDescription: 'Windows', frameworkDescription: '.NET 10', processArchitecture: 'X64',
  processorCount: 8, processUptimeSeconds: 3661, processCpuPercent: 12.3, processWorkingSetBytes: 1024 * 1024 * 200,
  gcHeapBytes: 1024 * 1024 * 50, totalAvailableMemoryBytes: 1024 * 1024 * 1024, threadCount: 30,
  disks: [
    { name: 'C:', totalBytes: 1024 * 1024 * 1024 * 100, freeBytes: 1024 * 1024 * 1024 * 40 },
    { name: 'Z:', totalBytes: 0, freeBytes: 0 }, // 未就绪/映射盘:应被过滤
  ],
})

function mount() {
  render(
    <AntdApp>
      <MonitorPage />
    </AntdApp>,
  )
}

beforeEach(() => vi.spyOn(monitorApi, 'server').mockResolvedValue(fixture()))
afterEach(cleanup)

describe('MonitorPage', () => {
  it('挂载即拉一次快照并渲染 CPU 卡', async () => {
    mount()
    expect(await screen.findByText('CPU 占用')).toBeTruthy()
    expect(monitorApi.server).toHaveBeenCalledTimes(1)
  })

  it('手动刷新再拉一次(不轮询)', async () => {
    mount()
    await screen.findByText('CPU 占用')
    fireEvent.click(screen.getByRole('button', { name: /刷\s*新/ })) // antd 两汉字按钮插空格:'刷新'→'刷 新'
    // timeout 放宽到 5s:默认 1000ms 在全量重载 + 本机内存紧张(GC 停顿)下会偶发超时;
    // 断言不变(仍是精确调 2 次),只增时序容差,治全量 gate 的间歇性 flake。
    await waitFor(() => expect(monitorApi.server).toHaveBeenCalledTimes(2), { timeout: 5000 })
  })

  it('容量为 0 的盘不渲染卡片', async () => {
    mount()
    await screen.findByText('CPU 占用')
    expect(screen.getByText('磁盘 C:')).toBeTruthy()
    expect(screen.queryByText('磁盘 Z:')).toBeNull()
  })
})
