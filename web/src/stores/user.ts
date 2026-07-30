import { defineStore } from 'pinia'
import type { LoginOutput } from '@/types/api'

interface UserInfo {
  userId: number
  account: string
  name: string
  /** 头像 ViewUrl;登录出参不含,由 useModule.enterInitial 经 /personal/profile 回填,profile 页保存时同步。 */
  avatar?: string | null
  /** 是否需强制改密(管理员建号/重置后首登为 true);路由守卫据此强制跳改密页。 */
  mustChangePassword: boolean
}

/**
 * 判定是否为 Level3 Cookie 会话:
 * - sessionMode === 'cookie' / csrfRequired === true(后端 AuthCookieService 显式信号)
 * - 或 body 下发了 access 但 refresh 为空(兼容未 regen OpenAPI 的过渡期)
 */
export function isCookieSession(data: Pick<LoginOutput, 'sessionMode' | 'csrfRequired' | 'accessToken' | 'refreshToken'>): boolean {
  if (data.sessionMode === 'cookie' || data.csrfRequired === true) return true
  if (data.sessionMode === 'body' || data.csrfRequired === false) return false
  return !!data.accessToken && !data.refreshToken
}

/**
 * 令牌 + 用户信息。
 * - 非 Level3:持久化 access/refresh 到 localStorage(与历史行为完全一致)。
 * - Level3 Cookie 会话:access 仅内存;refresh 在 HttpOnly Cookie;storage 只记 cookieSession 标记,
 *   绝不以 localStorage/sessionStorage 存放 access/refresh。
 */
export const useUserStore = defineStore('user', {
  state: () => ({
    accessToken: '',
    refreshToken: '',
    /**
     * Level3 Cookie 会话标记(可持久化,不含任何令牌)。
     * F5 后 access 已丢,靠此标记 + HttpOnly refresh Cookie 触发静默刷新。
     */
    cookieSession: false,
    userInfo: null as UserInfo | null,
  }),
  getters: {
    isLoggedIn: (s): boolean => !!s.accessToken,
  },
  actions: {
    /** 用登录/刷新出参落地会话。 */
    setSession(data: LoginOutput) {
      const cookie = isCookieSession(data)
      this.cookieSession = cookie
      this.accessToken = data.accessToken
      // Cookie 模式 body 的 refresh 为空串,不进内存也不进 storage
      this.refreshToken = cookie ? '' : (data.refreshToken ?? '')
      this.userInfo = {
        userId: Number(data.userId),
        account: data.account,
        name: data.name,
        mustChangePassword: data.mustChangePassword ?? false,
      }
    },
    clear() {
      this.accessToken = ''
      this.refreshToken = ''
      this.cookieSession = false
      this.userInfo = null
    },
  },
  persist: {
    // 序列化时按会话模式分流:Level3 只落 cookieSession,令牌字段强制空串。
    serializer: {
      serialize(state) {
        if (state.cookieSession) {
          return JSON.stringify({
            accessToken: '',
            refreshToken: '',
            cookieSession: true,
            userInfo: null,
          })
        }
        return JSON.stringify({
          accessToken: state.accessToken ?? '',
          refreshToken: state.refreshToken ?? '',
          cookieSession: false,
          userInfo: state.userInfo ?? null,
        })
      },
      deserialize(raw) {
        return JSON.parse(raw) as {
          accessToken: string
          refreshToken: string
          cookieSession: boolean
          userInfo: UserInfo | null
        }
      },
    },
    afterHydrate: ({ store }) => {
      // 防御:Level3 会话若 storage 残留旧版令牌,一律抹掉
      if (store.cookieSession) {
        store.accessToken = ''
        store.refreshToken = ''
      }
    },
  },
})
