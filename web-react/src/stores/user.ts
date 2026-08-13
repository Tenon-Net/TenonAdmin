import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { LoginOutput } from '@/types/api'

export interface UserInfo {
  userId: number
  account: string
  name: string
  /** 头像 ViewUrl;登录出参不含,由 useModule.enterInitial 经 /personal/profile 回填,profile 页保存时同步。 */
  avatar?: string | null
  /** 是否需强制改密(管理员建号/重置后首登为 true);路由守卫据此强制跳改密页。 */
  mustChangePassword: boolean
  /** 登录时快照的超管标记;profile 故障时 hasPerm 用此值 fail-open。 */
  isSuperAdmin?: boolean
}

/** 浏览器会话交付模式:与后端 LoginOutput.sessionMode 对齐。 */
export type SessionMode = 'body' | 'cookie'

export interface UserState {
  accessToken: string
  refreshToken: string
  userInfo: UserInfo | null
  /**
   * 会话模式。
   * - `body`(默认/非 Level3):access+refresh 可持久化到 localStorage
   * - `cookie`(Level3):access 仅内存;refresh 走 HttpOnly Cookie,不得落盘
   * - null:尚未建立会话(或旧持久化数据未声明模式)
   */
  sessionMode: SessionMode | null
  /** Level3 双提交 CSRF 是否启用(登录/刷新出参驱动)。 */
  csrfRequired: boolean
  /** 用登录/刷新出参落地会话。 */
  setSession: (data: LoginOutput) => void
  clear: () => void
}

/** 登录态判定。写成独立选择器而非 store 里的字段,免得多一份要同步的派生状态。 */
export const isLoggedIn = (s: UserState): boolean => !!s.accessToken

/** Level3 cookie 会话:access 不落盘,仅内存;F5 靠 cookie 静默刷新恢复。 */
export const isCookieSession = (s: Pick<UserState, 'sessionMode' | 'csrfRequired'>): boolean =>
  s.sessionMode === 'cookie' || s.csrfRequired === true

function resolveSessionMode(data: LoginOutput): SessionMode {
  if (data.sessionMode === 'cookie' || data.csrfRequired === true) return 'cookie'
  // 后端 cookie 模式会清空 body refresh;兼容未声明 sessionMode 的契约
  if (data.sessionMode === 'body') return 'body'
  if (!data.refreshToken && data.accessToken) return 'cookie'
  return 'body'
}

/** 令牌 + 用户信息。非 Level3 持久化(localStorage);Level3 仅持久化会话模式/用户信息,令牌永不落盘。对应 Vue 侧 `stores/user.ts`。 */
export const useUserStore = create<UserState>()(
  persist(
    (set) => ({
      accessToken: '',
      refreshToken: '',
      userInfo: null,
      sessionMode: null,
      csrfRequired: false,
      setSession: (data) => {
        const mode = resolveSessionMode(data)
        const cookie = mode === 'cookie'
        set({
          accessToken: data.accessToken,
          // Level3:refresh 只在 HttpOnly Cookie,body 为空串;本地也不保存
          refreshToken: cookie ? '' : data.refreshToken,
          sessionMode: mode,
          csrfRequired: cookie || !!data.csrfRequired,
          userInfo: {
            userId: data.userId,
            account: data.account,
            name: data.name,
            mustChangePassword: data.mustChangePassword ?? false,
            isSuperAdmin: data.isSuperAdmin ?? false,
          },
        })
      },
      clear: () =>
        set({
          accessToken: '',
          refreshToken: '',
          userInfo: null,
          sessionMode: null,
          csrfRequired: false,
        }),
    }),
    {
      name: 'user',
      // 白名单写死而不是靠「JSON 丢掉函数」兜底:靠兜底的话,以后往 state 里加一个不该落盘的字段
      // (在途 Promise、解密后的明文)会**默默**被持久化,而这里是显式的一行。
      // Level3 cookie 模式:严禁 accessToken/refreshToken 进 localStorage。
      partialize: (s) => {
        if (isCookieSession(s)) {
          return {
            sessionMode: 'cookie' as const,
            csrfRequired: true,
            userInfo: s.userInfo,
            // 显式空串覆盖旧版本可能残留的令牌键,防止 rehydrate 合并回旧 at/rt
            accessToken: '',
            refreshToken: '',
          }
        }
        return {
          accessToken: s.accessToken,
          refreshToken: s.refreshToken,
          userInfo: s.userInfo,
          sessionMode: (s.sessionMode ?? 'body') as SessionMode,
          csrfRequired: false,
        }
      },
      // 旧持久化无 sessionMode:有令牌 → body;仅有空壳 → null
      merge: (persisted, current) => {
        const p = (persisted ?? {}) as Partial<UserState>
        const mode =
          p.sessionMode === 'cookie' || p.csrfRequired
            ? ('cookie' as const)
            : p.sessionMode === 'body'
              ? ('body' as const)
              : p.accessToken || p.refreshToken
                ? ('body' as const)
                : (p.sessionMode ?? null)
        const cookie = mode === 'cookie'
        return {
          ...current,
          ...p,
          sessionMode: mode,
          csrfRequired: cookie || !!p.csrfRequired,
          // cookie 模式强制内存令牌为空(即使旧 localStorage 脏数据)
          accessToken: cookie ? '' : (p.accessToken ?? current.accessToken),
          refreshToken: cookie ? '' : (p.refreshToken ?? current.refreshToken),
        }
      },
    },
  ),
)
