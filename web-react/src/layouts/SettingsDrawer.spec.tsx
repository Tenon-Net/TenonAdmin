import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within, act } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { SettingsDrawer } from './SettingsDrawer'
import { effectiveMotionMode, useAppStore } from '@/stores/app'

beforeEach(() => {
  localStorage.clear()
  useAppStore.setState({ systemReducedMotion: false })
  useAppStore.getState().resetSettings()
})
afterEach(cleanup)

function mount() {
  render(
    <AntdApp>
      <SettingsDrawer open onClose={() => {}} />
    </AntdApp>,
  )
}

describe('SettingsDrawer', () => {
  it('渲染三个 tab', () => {
    mount()
    expect(screen.getByText('外观')).toBeTruthy()
    expect(screen.getByText('布局')).toBeTruthy()
    expect(screen.getByText('通用')).toBeTruthy()
  })

  it('切到布局 tab,拨"显示多标签"开关 → store showTabs 翻转', () => {
    mount()
    expect(useAppStore.getState().showTabs).toBe(true) // 默认开
    fireEvent.click(screen.getByText('布局')) // antd Tabs 惰性渲染非活动 pane,先切过去
    const row = screen.getByRole('group', { name: '显示多标签' })
    fireEvent.click(within(row).getByRole('switch'))
    expect(useAppStore.getState().showTabs).toBe(false)
  })

  it('点布局卡片 → store layoutMode 变', () => {
    mount()
    expect(useAppStore.getState().layoutMode).toBe('vertical') // 默认
    fireEvent.click(screen.getByText('布局'))
    fireEvent.click(screen.getByRole('button', { name: '顶部菜单模式' })) // horizontal 卡片
    expect(useAppStore.getState().layoutMode).toBe('horizontal')
  })

  it('外观 tab 切换动效模式 → store motionMode 变', () => {
    mount()
    expect(useAppStore.getState().motionMode).toBe('standard')
    fireEvent.click(screen.getByText('轻量'))
    expect(useAppStore.getState().motionMode).toBe('light')
  })

  it('只在浏览器覆盖所选档位时显示提示，解除系统偏好后恢复所选模式', () => {
    mount()
    const hint = /浏览器报告减少动态效果偏好/
    expect(screen.queryByText(hint)).toBeNull()
    act(() => useAppStore.setState({ systemReducedMotion: true }))
    expect(screen.getByText(hint).textContent).toContain('标准')
    fireEvent.click(screen.getByText('轻量', { exact: true }))
    expect(screen.getByText(hint).textContent).toContain('轻量')
    expect(effectiveMotionMode(useAppStore.getState())).toBe('off')
    fireEvent.click(screen.getByText('关闭', { exact: true }))
    expect(screen.queryByText(hint)).toBeNull()
    fireEvent.click(screen.getByText('标准', { exact: true }))
    expect(screen.getByText(hint).textContent).toContain('标准')
    act(() => useAppStore.setState({ systemReducedMotion: false }))
    expect(screen.queryByText(hint)).toBeNull()
    expect(effectiveMotionMode(useAppStore.getState())).toBe('standard')
  })

  // 页脚重置入口渲染即可:确认→resetSettings 的实际重置逻辑由 app.spec 的 resetSettings 用例充分覆盖;
  // Popconfirm 弹层是 antd 门户/rc-trigger 行为,在 happy-dom 下不稳定打开,不在此硬碰(测框架不测自己)。
  it('页脚渲染"重置"入口', () => {
    mount()
    expect(screen.getByText('重置')).toBeTruthy()
  })
})
