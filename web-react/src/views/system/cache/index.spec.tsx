import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { cacheApi } from '@/api'
import { useAuthStore } from '@/stores/auth'

// mock useConfirm.ask:受控放行/拦截。
const { askMock } = vi.hoisted(() => ({ askMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ ask: askMock }) }))

import CachePage from './index'

function mount() {
  render(
    <AntdApp>
      <CachePage />
    </AntdApp>,
  )
}
// 4 张卡的「执行」钮按 CACHE_ACTIONS 顺序:auth=0, dict=1, config=2, portal=3。
// 名用正则:antd 对两个汉字的按钮会在中间插空格('执行'→'执 行')。
const execButtons = () => screen.getAllByRole('button', { name: /执\s*行/ })

beforeEach(() => {
  vi.spyOn(cacheApi, 'flushAuth').mockResolvedValue(3)
  vi.spyOn(cacheApi, 'rebuildPortal').mockResolvedValue(42)
  askMock.mockReset()
  askMock.mockResolvedValue(true)
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
})

describe('CachePage', () => {
  it('未确认(ask=false)→ 不执行动作', async () => {
    askMock.mockResolvedValue(false)
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    fireEvent.click(execButtons()[0])
    await Promise.resolve()
    expect(cacheApi.flushAuth).not.toHaveBeenCalled()
  })

  it('确认后清权限缓存 → 显被清条数文案', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    fireEvent.click(execButtons()[0])
    // 先等异步链走完(ask 是微任务),再断言:message 出现 ⇒ flushAuth 必已被调
    expect(await screen.findByText('已清除 3 项缓存')).toBeTruthy()
    expect(cacheApi.flushAuth).toHaveBeenCalled()
  })

  it('门户走「重建代际」分支 → portalDone 文案(非条数)', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    fireEvent.click(execButtons()[3])
    expect(await screen.findByText('已重建门户菜单缓存')).toBeTruthy()
    expect(cacheApi.rebuildPortal).toHaveBeenCalled()
  })

  it('权限门:仅有权限码的卡片渲染', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['POST:/api/v1/sys/cache/flush-auth'] })
    mount()
    expect(execButtons()).toHaveLength(1) // 仅 auth 卡
    expect(screen.getByText('权限缓存')).toBeTruthy()
  })
})
