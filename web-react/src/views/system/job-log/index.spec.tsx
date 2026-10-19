import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App as AntdApp } from 'antd'
import { cleanup, render } from '@testing-library/react'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import type { SysJobLog } from '@/types/api'

const logPage = vi.hoisted(() => vi.fn())
const captured = vi.hoisted(() => ({
  query: 'jobId=9007199254740993',
  columns: [] as ProColumns<SysJobLog>[],
  fetcher: null as null | ((params: Record<string, unknown>) => Promise<unknown>),
}))

vi.mock('react-router-dom', () => ({
  useSearchParams: () => [new URLSearchParams(captured.query)],
}))
vi.mock('@/api', () => ({
  jobApi: {
    logPage,
    page: vi.fn(),
    killLog: vi.fn(),
    clearLogs: vi.fn(),
  },
}))
vi.mock('@/stores/auth', () => ({ useHasPerm: () => () => false }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: vi.fn() }) }))
vi.mock('@/components/Can', () => ({ Can: () => null }))
vi.mock('@/components/DataTable', () => ({
  DataTable: (props: {
    columns: ProColumns<SysJobLog>[]
    fetcher: (params: Record<string, unknown>) => Promise<unknown>
  }) => {
    captured.columns = props.columns
    captured.fetcher = props.fetcher
    return null
  },
}))

import JobLogPage from './index'

beforeEach(() => {
  captured.query = 'jobId=9007199254740993'
  captured.columns = []
  captured.fetcher = null
  logPage.mockResolvedValue({ items: [], total: 0 })
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe('JobLogPage large ids', () => {
  it.each([
    ['jobId=7', 7],
    ['jobId=abc', undefined],
    ['jobId=0', undefined],
    ['', undefined],
  ])('任务查询串 %s 保持安全编号回显与非法值过滤', async (query, expected) => {
    captured.query = query
    render(<AntdApp><JobLogPage /></AntdApp>)
    const initialJobId = captured.columns.find((column) => column.dataIndex === 'jobId')?.initialValue
    await captured.fetcher?.({ page: 1, pageSize: 20, jobId: initialJobId })
    expect(logPage).toHaveBeenCalledWith(expect.objectContaining({ jobId: expected }))
  })

  it('从查询串筛选任务时不把十进制字符串 Id 转成 number', async () => {
    render(<AntdApp><JobLogPage /></AntdApp>)

    const initialJobId = captured.columns.find((column) => column.dataIndex === 'jobId')?.initialValue
    await captured.fetcher?.({ page: 1, pageSize: 20, jobId: initialJobId })

    expect(logPage).toHaveBeenCalledWith(expect.objectContaining({ jobId: '9007199254740993' }))
  })
})
