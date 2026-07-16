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

/** 令牌 + 用户信息。持久化(localStorage)——刷新后仍登录。 */
export const useUserStore = defineStore('user', {
  state: () => ({
    accessToken: '',
    refreshToken: '',
    userInfo: null as UserInfo | null,
  }),
  getters: {
    isLoggedIn: (s): boolean => !!s.accessToken,
  },
  actions: {
    /** 用登录/刷新出参落地会话。 */
    setSession(data: LoginOutput) {
      this.accessToken = data.accessToken
      this.refreshToken = data.refreshToken
      this.userInfo = { userId: data.userId, account: data.account, name: data.name, mustChangePassword: data.mustChangePassword ?? false }
    },
    clear() {
      this.accessToken = ''
      this.refreshToken = ''
      this.userInfo = null
    },
  },
  persist: true,
})
