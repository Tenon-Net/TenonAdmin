import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { resolveInitializationFailure } from './initialization'
import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'

beforeEach(() => setActivePinia(createPinia()))

describe('resolveInitializationFailure', () => {
  it('有效会话遇到网络/5xx 初始化失败时保留会话并进入可重试页', () => {
    const auth = useAuthStore()
    const user = useUserStore()
    user.accessToken = 'still-valid'

    const route = resolveInitializationFailure(auth, user, '/system/user?page=2', new Error('network failed'))

    expect(route).toEqual({ name: 'initialization-error', replace: true })
    expect(user.accessToken).toBe('still-valid')
    expect(auth.initializationError).toBe('network failed')
    expect(auth.initializationRedirect).toBe('/system/user?page=2')
  })

  it('请求中间件已清掉失效会话时回登录页', () => {
    const auth = useAuthStore()
    const user = useUserStore()

    expect(resolveInitializationFailure(auth, user, '/system/user', new Error('401')))
      .toEqual({ path: '/login', replace: true })
    expect(auth.initializationError).toBe('')
  })
})
