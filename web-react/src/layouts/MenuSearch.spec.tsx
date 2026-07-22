import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import { MemoryRouter } from 'react-router-dom'
import '@/locales'
import { MenuSearch } from './MenuSearch'
import type { MenuItem } from './menuItems'

const navigate = vi.fn()
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => navigate,
}))

const ITEMS: MenuItem[] = [
  { key: '/dashboard', label: '工作台' },
  {
    key: 'cat-2',
    label: '系统',
    children: [
      { key: '/system/user', label: '用户' },
      { key: '/system/role', label: '角色' },
    ],
  },
  { key: 'https://ex.com/docs', label: '文档' },
]

const onClose = vi.fn()

function mount() {
  render(
    <AntdApp>
      <MemoryRouter>
        <MenuSearch open onClose={onClose} items={ITEMS} />
      </MemoryRouter>
    </AntdApp>,
  )
}

const input = () => screen.getByPlaceholderText('搜索菜单…')
const list = () => document.querySelector('.palette-list') as HTMLElement

beforeEach(() => vi.clearAllMocks())
afterEach(cleanup)

describe('MenuSearch 命令面板', () => {
  it('空查询:渲染全部叶子(目录不入)', () => {
    mount()
    // 顶层叶子 title 与 path(面包屑含自身)同文本,故按 .palette-title 精确取,别用 getByText(会命中 path)。
    const titles = [...list().querySelectorAll('.palette-title')].map((t) => t.textContent)
    expect(titles).toEqual(['工作台', '用户', '角色', '文档']) // cat-2 目录不出,4 个叶子按深度优先序
  })

  it('按 label 过滤', () => {
    mount()
    fireEvent.change(input(), { target: { value: '用户' } })
    const items = list().querySelectorAll('.palette-item')
    expect(items).toHaveLength(1)
    expect(items[0]!.textContent).toContain('用户')
  })

  it('按 key(路由/URL 片段)过滤', () => {
    mount()
    fireEvent.change(input(), { target: { value: 'role' } })
    const items = list().querySelectorAll('.palette-item')
    expect(items).toHaveLength(1)
    expect(items[0]!.textContent).toContain('角色') // key '/system/role' 命中,虽 label '角色' 不含 'role'
  })

  it('无匹配 → 空态', () => {
    mount()
    fireEvent.change(input(), { target: { value: 'zzz不存在' } })
    expect(document.querySelector('.palette-list')).toBeNull()
    expect(screen.getByText('无匹配菜单')).toBeTruthy()
  })

  it('点内部叶子 → navigate(path) + onClose', () => {
    mount()
    fireEvent.click(within(list()).getByText('用户'))
    expect(navigate).toHaveBeenCalledWith('/system/user')
    expect(onClose).toHaveBeenCalled()
  })

  it('点外链叶子 → window.open,不 navigate', () => {
    const open = vi.fn()
    vi.stubGlobal('open', open)
    mount()
    // '文档' 是顶层外链叶子,title 与 path 同文本 → 按 .palette-title 精确取再点(点击冒泡到 .palette-item 的 onClick)。
    const doc = [...list().querySelectorAll('.palette-title')].find((t) => t.textContent === '文档')!
    fireEvent.click(doc)
    expect(open).toHaveBeenCalledWith('https://ex.com/docs', '_blank', 'noopener,noreferrer')
    expect(navigate).not.toHaveBeenCalled()
    expect(onClose).toHaveBeenCalled()
    vi.unstubAllGlobals()
  })

  it('键盘:↓ 移动高亮 + ↵ 进入当前项', () => {
    mount()
    fireEvent.change(input(), { target: { value: 'system' } }) // 命中 /system/user、/system/role(按 key)
    fireEvent.keyDown(input(), { key: 'ArrowDown' }) // 光标 0→1(角色)
    fireEvent.keyDown(input(), { key: 'Enter' })
    expect(navigate).toHaveBeenCalledWith('/system/role')
  })

  it('键盘:↓ 到末项后再按不越界(上钳位)', () => {
    mount()
    fireEvent.change(input(), { target: { value: 'system' } }) // 2 项:/system/user(0)、/system/role(1)
    fireEvent.keyDown(input(), { key: 'ArrowDown' }) // 0→1(末项)
    fireEvent.keyDown(input(), { key: 'ArrowDown' }) // 越界一次:Math.min 钳回 1
    fireEvent.keyDown(input(), { key: 'Enter' })
    expect(navigate).toHaveBeenCalledWith('/system/role') // 仍是末项;去掉 Math.min 则 cursor=2 → go(undefined) 不 navigate
  })

  it('键盘:↑ 到首项后再按不越界(下钳位)', () => {
    mount()
    fireEvent.change(input(), { target: { value: 'system' } }) // 2 项,光标起于首项 0
    fireEvent.keyDown(input(), { key: 'ArrowUp' }) // 越界一次:Math.max 钳回 0
    fireEvent.keyDown(input(), { key: 'Enter' })
    expect(navigate).toHaveBeenCalledWith('/system/user') // 仍是首项;去掉 Math.max 则 cursor=-1 → go(undefined) 不 navigate
  })

  it('关闭后重开是干净态:清空关键词 + 光标复位', () => {
    const shell = (o: boolean) => (
      <AntdApp>
        <MemoryRouter>
          <MenuSearch open={o} onClose={onClose} items={ITEMS} />
        </MemoryRouter>
      </AntdApp>
    )
    const { rerender } = render(shell(true))
    fireEvent.change(input(), { target: { value: '用户' } })
    expect((input() as HTMLInputElement).value).toBe('用户')
    rerender(shell(false)) // 关
    rerender(shell(true)) // 再开:依赖 open 的 effect 清空 q/cursor(不走 Modal 过渡回调)
    expect((input() as HTMLInputElement).value).toBe('') // 残留旧词则此断言红(网住"打开不清空"的改动)
  })

  it('命中片段用 <mark> 高亮(不走 v-html,天然转义)', () => {
    mount()
    fireEvent.change(input(), { target: { value: '工作' } })
    const mark = list().querySelector('mark')
    expect(mark?.textContent).toBe('工作')
  })
})
