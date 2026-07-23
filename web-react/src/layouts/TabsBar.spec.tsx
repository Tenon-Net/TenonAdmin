import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import { MemoryRouter, useLocation } from 'react-router-dom'
import '@/locales' // t('tabs.close') = '关闭'
import { TabsBar } from './TabsBar'
import { useTabsStore, type TabItem } from '@/stores/tabs'
import { useAuthStore } from '@/stores/auth'

function LocProbe() {
  const { pathname } = useLocation()
  return <div data-testid="loc">{pathname}</div>
}

const T = (path: string, title: string, extra: Partial<TabItem> = {}): TabItem => ({ path, fullPath: path, title, ...extra })

beforeEach(() => {
  sessionStorage.clear()
  useAuthStore.setState({ modules: [], currentModuleId: null, menuTree: [] }) // homePath=/module → 无 affix
  useTabsStore.setState({ tabs: [T('/a', 'AA'), T('/b', 'BB'), T('/c', 'CC')], reloadKey: 0, excludeKey: '' })
})
afterEach(() => {
  cleanup()
  useTabsStore.setState({ tabs: [], reloadKey: 0, excludeKey: '' })
})

function mount(start = '/a') {
  render(
    <AntdApp>
      <MemoryRouter initialEntries={[start]}>
        <TabsBar />
        <LocProbe />
      </MemoryRouter>
    </AntdApp>,
  )
}

describe('TabsBar', () => {
  it('渲染所有标签 chip', () => {
    mount()
    expect(screen.getByText('AA')).toBeTruthy()
    expect(screen.getByText('BB')).toBeTruthy()
    expect(screen.getByText('CC')).toBeTruthy()
  })

  it('点标签 → 导航到它的 fullPath', () => {
    mount('/a')
    expect(screen.getByTestId('loc').textContent).toBe('/a')
    fireEvent.click(screen.getByText('BB'))
    expect(screen.getByTestId('loc').textContent).toBe('/b') // 导航生效
  })

  it('点关闭 X → 从 store 移除该标签', () => {
    mount('/a')
    const chipC = screen.getByText('CC').closest('.tab-chip') as HTMLElement
    fireEvent.click(within(chipC).getByLabelText('关闭'))
    expect(useTabsStore.getState().tabs.map((t) => t.path)).toEqual(['/a', '/b']) // /c 已移除
  })

  it('中键点标签 → 关闭', () => {
    mount('/a')
    const chipB = screen.getByText('BB').closest('.tab-chip') as HTMLElement
    // RTL 无 fireEvent.auxClick;直接派发 button=1 的 auxclick(React onAuxClick 监听它)。
    fireEvent(chipB, new MouseEvent('auxclick', { bubbles: true, button: 1 }))
    expect(useTabsStore.getState().tabs.some((t) => t.path === '/b')).toBe(false)
  })
})
