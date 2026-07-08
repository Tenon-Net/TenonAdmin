import { defineStore } from 'pinia'
import type { LoginOutput } from '@/types/api'

interface UserInfo {
  userId: number
  account: string
  name: string
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
      this.userInfo = { userId: data.userId, account: data.account, name: data.name }
    },
    clear() {
      this.accessToken = ''
      this.refreshToken = ''
      this.userInfo = null
    },
  },
  persist: true,
})
