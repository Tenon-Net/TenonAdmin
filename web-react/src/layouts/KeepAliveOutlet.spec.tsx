import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { useState } from 'react'
import { render, screen, cleanup, fireEvent, act } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useNavigate } from 'react-router-dom'
import { KeepAliveOutlet } from './KeepAliveOutlet'
import { useTabsStore, type TabItem } from '@/stores/tabs'
import { useAuthStore } from '@/stores/auth'

// 有状态页:输入框留存与否用来判定是否真被缓存(挂载常驻)。
function StatefulPage({ label }: { label: string }) {
  const [v, setV] = useState('')
  return (
    <div>
      <span>{label} PAGE</span>
      <input aria-label={`${label}-input`} value={v} onChange={(e) => setV(e.target.value)} />
    </div>
  )
}
function Nav() {
  const navigate = useNavigate()
  return (
    <div>
      <button onClick={() => navigate('/a')}>go A</button>
      <button onClick={() => navigate('/b')}>go B</button>
      <button onClick={() => navigate('/c')}>go C</button>
    </div>
  )
}

const T = (path: string, extra: Partial<TabItem> = {}): TabItem => ({ path, fullPath: path, title: path.toUpperCase(), ...extra })

beforeEach(() => {
  sessionStorage.clear()
  useAuthStore.setState({ modules: [], currentModuleId: null, menuTree: [] })
  // /a /b 应缓存;/c 是 noCache
  useTabsStore.setState({ tabs: [T('/a'), T('/b'), T('/c', { noCache: true })], reloadKey: 0, excludeKey: '' })
})
afterEach(() => {
  cleanup()
  useTabsStore.setState({ tabs: [], reloadKey: 0, excludeKey: '' })
})

function mount(start = '/a') {
  render(
    <MemoryRouter initialEntries={[start]}>
      <Nav />
      <Routes>
        <Route element={<KeepAliveOutlet />}>
          <Route path="/a" element={<StatefulPage label="A" />} />
          <Route path="/b" element={<StatefulPage label="B" />} />
          <Route path="/c" element={<StatefulPage label="C" />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('KeepAliveOutlet', () => {
  it('缓存页:切走再切回,组件状态留存(A 的输入不丢)', () => {
    mount('/a')
    fireEvent.change(screen.getByLabelText('A-input'), { target: { value: '张三' } })
    fireEvent.click(screen.getByText('go B')) // 切到 B(A 转 display:none,仍挂载)
    expect(screen.getByText('B PAGE')).toBeTruthy()
    fireEvent.click(screen.getByText('go A')) // 切回 A
    expect(screen.getByDisplayValue('张三')).toBeTruthy() // 状态留存 = 真被缓存
  })

  it('noCache 页:切走即卸载,复访重挂,状态不留存(C 的输入清空)', () => {
    mount('/c')
    fireEvent.change(screen.getByLabelText('C-input'), { target: { value: '临时' } })
    fireEvent.click(screen.getByText('go A')) // 离开 C → 卸载
    fireEvent.click(screen.getByText('go C')) // 复访 C → 重挂
    expect(screen.getByText('C PAGE')).toBeTruthy()
    expect(screen.queryByDisplayValue('临时')).toBeNull() // 未留存 = 未缓存
  })

  it('refreshTab:noCache 当前页也重挂,状态清空(live 块带版本键)', () => {
    mount('/c') // /c 是 noCache,走 live 渲染分支(不入缓存)
    fireEvent.change(screen.getByLabelText('C-input'), { target: { value: '待刷新' } })
    // 若 live 块无版本键,refreshTab 对 noCache 当前页会是静默 no-op(状态残留)。
    act(() => useTabsStore.getState().refreshTab('/c'))
    expect(screen.queryByDisplayValue('待刷新')).toBeNull() // 重挂 → 状态清空
  })

  it('refreshTab:递增 reloadKey 令当前页重挂,状态清空', () => {
    mount('/a')
    fireEvent.change(screen.getByLabelText('A-input'), { target: { value: '要被刷掉' } })
    // 直接调 store 在 React 批处理外 —— 包 act 才会 flush 重渲染 + effect(bump→删缓存→换 key 重挂)。
    act(() => useTabsStore.getState().refreshTab('/a'))
    expect(screen.queryByDisplayValue('要被刷掉')).toBeNull() // 重挂 → 状态清空
    expect(useTabsStore.getState().excludeKey).toBe('') // 刷新后已复位
  })
})
