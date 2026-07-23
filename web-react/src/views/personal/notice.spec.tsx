import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import { noticeApi } from '@/api'
import { NoticeType, type NoticeMineItem } from '@/types/api'

// <DataTable> 整体 mock(pro-components vitest 解析墙,同 system/notice 范式):捕 columns,直接调操作列 render 拿「查看」钮。
let captured: { columns?: ProColumns<NoticeMineItem>[] } = {}
vi.mock('@/components/DataTable', () => ({
  DataTable: (props: { columns?: ProColumns<NoticeMineItem>[] }) => {
    captured = props
    return null
  },
}))
// MarkdownView 拉重依赖;本测不看正文
vi.mock('@/components/MarkdownView', () => ({ MarkdownView: () => null }))

import NoticePage from './notice'

const row = (isRead: boolean, id: number): NoticeMineItem => ({
  id, title: isRead ? '已读' : '未读', content: 'x', type: NoticeType.Notice, publishTime: '2026-07-21T08:00:00', isRead,
})

// 渲染操作列的「查看」钮并点击(触发 NoticePage 里真正的 openView 闭包)
function clickView(r: NoticeMineItem) {
  const op = captured.columns!.find((c) => c.key === 'op')!
  const node = (op.render as (dom: unknown, e: NoticeMineItem) => React.ReactNode)(null, r)
  render(<AntdApp>{node}</AntdApp>)
  fireEvent.click(screen.getByRole('button', { name: /查\s*看/ }))
}

beforeEach(() => {
  captured = {}
  vi.spyOn(noticeApi, 'markRead').mockResolvedValue(true)
})
afterEach(() => { cleanup(); vi.restoreAllMocks() })

describe('NoticePage openView 读态守卫', () => {
  it('查看未读 → markRead(id) 被调(顺手标记已读)', async () => {
    render(<AntdApp><NoticePage /></AntdApp>)
    clickView(row(false, 7))
    await waitFor(() => expect(noticeApi.markRead).toHaveBeenCalledWith(7))
  })

  it('查看已读 → 不调 markRead(守卫早返)', async () => {
    render(<AntdApp><NoticePage /></AntdApp>)
    clickView(row(true, 8))
    await Promise.resolve() // 放行一拍微任务,证明确实没排队
    expect(noticeApi.markRead).not.toHaveBeenCalled()
  })
})
