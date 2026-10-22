import type { RouteLocationRaw } from 'vue-router'
import type { useAuthStore } from '@/stores/auth'
import type { useUserStore } from '@/stores/user'
import { translateError } from '@/utils/error'

/** 将启动故障分成会话失效与可重试故障；只有前者回登录页。 */
export function resolveInitializationFailure(
  auth: ReturnType<typeof useAuthStore>,
  user: ReturnType<typeof useUserStore>,
  redirect: string,
  error: unknown,
): RouteLocationRaw {
  if (!user.isLoggedIn) return { path: '/login', replace: true }
  auth.initializationError = translateError(error)
  auth.initializationRedirect = redirect
  return { name: 'initialization-error', replace: true }
}
