import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import '@/locales' // t() 要真文案
import { DetailPage } from './DetailPage'

afterEach(cleanup)

describe('DetailPage', () => {
  it('渲染标题 / actions / children', () => {
    render(
      <DetailPage title="用户详情" actions={<span>导出</span>}>
        <div>正文内容</div>
      </DetailPage>,
    )
    expect(screen.getByText('用户详情')).toBeTruthy()
    expect(screen.getByText('导出')).toBeTruthy()
    expect(screen.getByText('正文内容')).toBeTruthy()
  })

  it('默认显示返回按钮,点击触发 onBack', () => {
    const onBack = vi.fn()
    render(<DetailPage title="x" onBack={onBack} />)
    fireEvent.click(screen.getByRole('button', { name: '返回' }))
    expect(onBack).toHaveBeenCalledOnce()
  })

  it('showBack=false 不渲染返回按钮', () => {
    render(<DetailPage title="x" showBack={false} onBack={vi.fn()} />)
    expect(screen.queryByRole('button', { name: '返回' })).toBeNull()
  })
})
