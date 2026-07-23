import { describe, it, expect, afterEach, beforeEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'

// mock lib/icons:控制集合/图标名,避免在测试里加载真实的大 icons.json。
vi.mock('@/lib/icons', () => ({
  COLLECTIONS: [{ prefix: 'ph', name: 'Phosphor' }, { prefix: 'lucide', name: 'Lucide' }],
  LOCAL_PREFIX: 'local',
  loadIconNames: vi.fn(),
  ensureIconLoaded: vi.fn(),
  getLocalIconNames: vi.fn(() => ['star']),
}))
import { loadIconNames } from '@/lib/icons'
import { IconPicker, filterNames } from './IconPicker'

afterEach(cleanup)

function mount(props: { value?: string; onChange: (v: string) => void }) {
  const { container } = render(
    <AntdApp>
      <IconPicker value={props.value ?? ''} onChange={props.onChange} />
    </AntdApp>,
  )
  return container
}
const openModal = (container: HTMLElement) => fireEvent.click(container.querySelector('.icon-picker-trigger')!)

// ── 纯逻辑 ──
describe('filterNames', () => {
  it('空/纯空白 keyword 返回原列表(同引用)', () => {
    const list = ['apple', 'banana']
    expect(filterNames(list, '  ')).toBe(list)
  })
  it('子串大小写不敏感过滤', () => {
    expect(filterNames(['Apple', 'banana', 'grape'], 'AP')).toEqual(['Apple', 'grape']) // ap 命中 Apple / grApe
  })
})

// ── 组件接线 ──
describe('IconPicker', () => {
  beforeEach(() => {
    vi.mocked(loadIconNames).mockResolvedValue(['apple', 'banana', 'cherry'])
  })

  it('点触发器开弹窗 → 加载图标名成网格', async () => {
    const c = mount({ onChange: vi.fn() })
    openModal(c)
    expect(screen.getByPlaceholderText('搜索图标名…')).toBeTruthy() // 弹窗开(搜索框可见)
    await waitFor(() => expect(screen.getByText('banana')).toBeTruthy())
  })

  it('点图标 → onChange 收到 prefix:name 并关窗', async () => {
    const onChange = vi.fn()
    const c = mount({ onChange })
    openModal(c)
    await waitFor(() => expect(screen.getByText('apple')).toBeTruthy())
    fireEvent.click(screen.getByText('apple'))
    // 断回调契约(pick 也 setOpen(false),但 happy-dom 无 CSS 过渡、antd 关闭动画 transitionend
    // 永不触发、弹层内容不卸载 → 不断 DOM 关窗,与 C1 FormContainer 同款,关窗留 B12 实点)。
    expect(onChange).toHaveBeenCalledWith('ph:apple')
  })

  it('搜索过滤网格', async () => {
    const c = mount({ onChange: vi.fn() })
    openModal(c)
    await waitFor(() => expect(screen.getByText('banana')).toBeTruthy())
    fireEvent.change(screen.getByPlaceholderText('搜索图标名…'), { target: { value: 'ban' } })
    await waitFor(() => expect(screen.queryByText('apple')).toBeNull())
    expect(screen.getByText('banana')).toBeTruthy()
  })

  it('切到本地 tab → 取本地 svg,点选回 local:name', async () => {
    const onChange = vi.fn()
    const c = mount({ onChange })
    openModal(c)
    fireEvent.click(screen.getByText('本地 SVG'))
    await waitFor(() => expect(screen.getByText('star')).toBeTruthy())
    fireEvent.click(screen.getByText('star'))
    expect(onChange).toHaveBeenCalledWith('local:star')
  })

  it('超出 CAP(300)→ 只渲 300 + more 提示计数', async () => {
    vi.mocked(loadIconNames).mockResolvedValue(Array.from({ length: 350 }, (_, i) => `ic${i}`))
    const c = mount({ onChange: vi.fn() })
    openModal(c)
    await waitFor(() => expect(screen.getByText('ic0')).toBeTruthy())
    expect(document.querySelectorAll('.icon-picker-cell').length).toBe(300)
    expect(screen.getByText(/还有 50 个/)).toBeTruthy()
  })

  it('已选值 → 触发器显值 + 清空按钮回空串', () => {
    const onChange = vi.fn()
    mount({ value: 'ph:folder', onChange })
    expect(screen.getByText('ph:folder')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '清空' }))
    expect(onChange).toHaveBeenCalledWith('')
  })
})
