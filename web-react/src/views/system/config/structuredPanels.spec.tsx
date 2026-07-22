import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { configApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { ALL_BOOL_KEYS, NUM_FIELDS } from './configForm'

const { loadSiteMock } = vi.hoisted(() => ({ loadSiteMock: vi.fn() }))
// 仅 SysBaseConfig 消费 site store(load(true) 即时生效);对 Upload/Security 无害。
vi.mock('@/stores/site', () => ({ useSiteStore: (sel: (s: { load: typeof loadSiteMock }) => unknown) => sel({ load: loadSiteMock }) }))

import SysBaseConfig from './SysBaseConfig'
import UploadConfig from './UploadConfig'
import SecurityConfig from './SecurityConfig'

const wrap = (node: React.ReactNode) => render(<AntdApp>{node}</AntdApp>)
const saveBtn = () => screen.getByRole('button', { name: /保\s*存/ })

beforeEach(() => {
  loadSiteMock.mockReset()
  vi.spyOn(configApi, 'saveBatch').mockResolvedValue(true)
  useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] }) // 令 <Can> 渲染保存钮
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
  vi.restoreAllMocks()
})

describe('SysBaseConfig', () => {
  it('载入 sys 分组 → 回填;保存序列化全字段 + loadSite(true) + 同步标题', async () => {
    vi.spyOn(configApi, 'listByGroup').mockResolvedValue([
      { id: 1, configKey: 'sys.site.title', configValue: 'UniqueTitle', name: '', sort: 0 },
    ])
    wrap(<SysBaseConfig />)
    expect(configApi.listByGroup).toHaveBeenCalledWith('sys')
    await screen.findByDisplayValue('UniqueTitle') // 载入完成
    fireEvent.click(saveBtn())
    await waitFor(() => expect(configApi.saveBatch).toHaveBeenCalled())
    const items = (configApi.saveBatch as unknown as { mock: { calls: any[][] } }).mock.calls[0][0]
    expect(items.find((i: any) => i.configKey === 'sys.site.title')?.configValue).toBe('UniqueTitle')
    expect(items).toHaveLength(5) // 全 5 个 SYS_FIELDS
    await waitFor(() => expect(loadSiteMock).toHaveBeenCalledWith(true))
    expect(document.title).toBe('UniqueTitle')
  })
})

describe('UploadConfig', () => {
  it('载入 upload 分组 → 回填;保存序列化大小 + 后缀', async () => {
    vi.spyOn(configApi, 'listByGroup').mockResolvedValue([
      { id: 1, configKey: 'sys.upload.maxSizeMb', configValue: '42', name: '', sort: 0 },
      { id: 2, configKey: 'sys.upload.allowedExtensions', configValue: '.jpg,.png', name: '', sort: 0 },
    ])
    wrap(<UploadConfig />)
    expect(configApi.listByGroup).toHaveBeenCalledWith('upload')
    await screen.findByDisplayValue('42')
    fireEvent.click(saveBtn())
    await waitFor(() => expect(configApi.saveBatch).toHaveBeenCalled())
    const items = (configApi.saveBatch as unknown as { mock: { calls: any[][] } }).mock.calls[0][0]
    expect(items.find((i: any) => i.configKey === 'sys.upload.maxSizeMb')?.configValue).toBe('42')
    expect(items.find((i: any) => i.configKey === 'sys.upload.allowedExtensions')?.configValue).toBe('.jpg,.png')
  })
})

describe('SecurityConfig', () => {
  it('载入 security 分组 → 回填;保存序列化全部数值+布尔+验证码类型键', async () => {
    vi.spyOn(configApi, 'listByGroup').mockResolvedValue([
      { id: 1, configKey: 'sys.security.loginLock.maxFailCount', configValue: '7', name: '', sort: 0 },
      { id: 2, configKey: 'sys.security.captcha.type', configValue: 'math', name: '', sort: 0 },
    ])
    wrap(<SecurityConfig />)
    expect(configApi.listByGroup).toHaveBeenCalledWith('security')
    await screen.findByDisplayValue('7')
    fireEvent.click(saveBtn())
    await waitFor(() => expect(configApi.saveBatch).toHaveBeenCalled())
    const items = (configApi.saveBatch as unknown as { mock: { calls: any[][] } }).mock.calls[0][0]
    const map = new Map(items.map((i: any) => [i.configKey, i.configValue]))
    expect(map.get('sys.security.loginLock.maxFailCount')).toBe('7')
    expect(map.get('sys.security.captcha.type')).toBe('math')
    expect(items).toHaveLength(NUM_FIELDS.length + ALL_BOOL_KEYS.length + 1) // 9 + 8 + 1
  })
})
