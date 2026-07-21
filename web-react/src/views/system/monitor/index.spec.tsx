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
    // 关键:antd Button 在 loading 态会吞掉 onClick(handleClick 里 innerLoading 直接 return)。
    // 满载 + GC 停顿下,首拉的 CPU 卡可能先于 setLoading(false) 的渲染出现,此刻点刷新会被吞、第二次永不发生
    // → 故点击前先等按钮退出 loading。这才是 flake 根因(非单纯超时),消除后再断言精确调 2 次。
    const btnName = { name: /刷\s*新/ } // antd 两汉字按钮插空格:'刷新'→'刷 新'
    await waitFor(() => expect(screen.getByRole('button', btnName).className).not.toContain('loading'))
    fireEvent.click(screen.getByRole('button', btnName))
    // waitFor 上限 5s(命中即返回);per-test 超时抬到 15s(第三个入参)给渲染滞后留头寸。断言不变。
    await waitFor(() => expect(monitorApi.server).toHaveBeenCalledTimes(2), { timeout: 5000 })
  }, 15000)

  it('容量为 0 的盘不渲染卡片', async () => {
    mount()
    await screen.findByText('CPU 占用')
    expect(screen.getByText('磁盘 C:')).toBeTruthy()
    expect(screen.queryByText('磁盘 Z:')).toBeNull()
  })
})
