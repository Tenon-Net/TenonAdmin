import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { personalApi } from '@/api'
import { useUserStore } from '@/stores/user'
import type { UserProfile } from '@/types/api'

// gender/avatar 控件依赖字典/上传,stub 掉;值由 setFieldsValue 灌进表单 store,validateFields 照样带上。
vi.mock('@/components/DictSelect', () => ({ DictSelect: () => <div data-testid="dict" /> }))
vi.mock('@/components/FileUpload', () => ({ FileUpload: (p: { children?: unknown }) => <div data-testid="upload">{p.children as never}</div> }))

import ProfilePage from './profile'

const PROFILE: UserProfile = {
  id: 1, account: 'acc', name: '张三', isSuperAdmin: false,
  nickname: '昵', phone: '', email: 'a@b.com', gender: '1', avatar: 'u', orgName: '研发', positionName: '工程师',
}
const mount = () => render(<AntdApp><ProfilePage /></AntdApp>)

beforeEach(() => {
  vi.spyOn(personalApi, 'profile').mockResolvedValue(PROFILE)
  vi.spyOn(personalApi, 'updateProfile').mockResolvedValue(true)
  useUserStore.setState({ accessToken: 't', refreshToken: 'r', userInfo: { userId: 1, account: 'acc', name: '旧名', mustChangePassword: false } })
})
afterEach(() => { cleanup(); vi.restoreAllMocks() })

describe('ProfilePage', () => {
  it('载入回填 + 保存全字段映射(空手机→null)+ 同步顶栏 userInfo', async () => {
    mount()
    await waitFor(() => expect(screen.getByDisplayValue('张三')).toBeTruthy()) // 载入完成
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() =>
      expect(personalApi.updateProfile).toHaveBeenCalledWith({
        name: '张三', nickname: '昵', phone: null, email: 'a@b.com', gender: '1', avatar: 'u',
      }),
    )
    expect(useUserStore.getState().userInfo?.name).toBe('张三') // 顶栏名即时同步
    expect(useUserStore.getState().userInfo?.avatar).toBe('u')
  })
})
