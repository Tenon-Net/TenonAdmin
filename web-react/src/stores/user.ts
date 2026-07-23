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
}

export interface UserState {
  accessToken: string
  refreshToken: string
  userInfo: UserInfo | null
  /** 用登录/刷新出参落地会话。 */
  setSession: (data: LoginOutput) => void
  clear: () => void
}

/** 登录态判定。写成独立选择器而非 store 里的字段,免得多一份要同步的派生状态。 */
export const isLoggedIn = (s: UserState): boolean => !!s.accessToken

/** 令牌 + 用户信息。持久化(localStorage)——刷新后仍登录。对应 Vue 侧 `stores/user.ts`。 */
export const useUserStore = create<UserState>()(
  persist(
    (set) => ({
      accessToken: '',
      refreshToken: '',
      userInfo: null,
      setSession: (data) =>
        set({
          accessToken: data.accessToken,
          refreshToken: data.refreshToken,
          userInfo: {
            userId: data.userId,
            account: data.account,
            name: data.name,
            mustChangePassword: data.mustChangePassword ?? false,
          },
        }),
      clear: () => set({ accessToken: '', refreshToken: '', userInfo: null }),
    }),
    {
      name: 'user',
      // 白名单写死而不是靠「JSON 丢掉函数」兜底:靠兜底的话,以后往 state 里加一个不该落盘的字段
      // (在途 Promise、解密后的明文)会**默默**被持久化,而这里是显式的一行。与 Vue 侧 `persist: true` 等价
      //(Vue 那边 state 里只有这三项)。
      partialize: (s) => ({ accessToken: s.accessToken, refreshToken: s.refreshToken, userInfo: s.userInfo }),
    },
  ),
)
