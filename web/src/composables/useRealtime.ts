import { useRouter } from 'vue-router'
import { useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useEventBus } from '@vueuse/core'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useUserStore } from '@/stores/user'

/**
 * 未读通知刷新总线:useRealtime 收到后端 `notice-changed` 推送后 emit,NoticeBell 订阅后立即重拉未读角标。
 * 解耦长连接与铃铛组件(二者互不 import 对方)。
 */
export const noticeBus = useEventBus<void>('notice-changed')

// 进程内单例连接:鉴权外壳(default.vue)挂载一次即建一条;重复 start 幂等(已连不再建)。
let connection: HubConnection | null = null
// 主动登出/改密重登:后端 Logout 也走 RevokeAsync → 推 force-logout;本页应静默,勿弹「您已被强制下线」。
// 其它同会话标签页仍会收到推送并提示(它们没调 beginVoluntaryLogout)。下次 start 清零。
let voluntaryLogout = false

async function stopConnection(): Promise<void> {
  const c = connection
  connection = null
  if (c) {
    try {
      await c.stop()
    } catch {
      /* 停止失败无害 */
    }
  }
}

/**
 * 主动登出前调用:标记自愿退出 + 先断 SignalR。
 * <p>后端会话吊销会推 `force-logout`(与管理员强退共用通道);本页断连后收不到,即便迟到推送也不弹强制下线文案。
 * 其它标签页仍可被踢并提示。</p>
 */
export async function beginVoluntaryLogout(): Promise<void> {
  voluntaryLogout = true
  await stopConnection()
}

/**
 * 实时通知客户端(SignalR)。仅在鉴权外壳挂载时 start、登出/卸载时 stop。
 * <p>后端 `TenonAdmin:Realtime:Enabled` 关闭时 Hub 不存在 → 初次连接失败,**静默退回** NoticeBell 的 30s 轮询兜底
 * 与「下次请求 401」惰性登出(纯增强,无回归)。令牌走 query `access_token`(浏览器 WebSocket 带不了 Authorization 头)。</p>
 * message/router/i18n 在 setup 期(default.vue)绑定,推送到达时复用,故务必在 setup 中调用本 composable。
 */
export function useRealtime() {
  const router = useRouter()
  const message = useMessage()
  const { t } = useI18n()

  function start() {
    if (connection || !useUserStore().accessToken) return
    voluntaryLogout = false
    const baseUrl = import.meta.env.VITE_API_BASE ?? ''
    const conn = new HubConnectionBuilder()
      .withUrl(`${baseUrl}/hub/realtime`, { accessTokenFactory: () => useUserStore().accessToken })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    // 强制下线:清会话 + 授权态 + 提示 + 跳登录(与 api/client.ts 刷新失败路径同款收尾)
    conn.on('force-logout', () => {
      const silent = voluntaryLogout
      voluntaryLogout = false
      void stopConnection()
      useUserStore().clear()
      void import('@/stores/auth').then(({ useAuthStore }) => useAuthStore().reset())
      void import('@/router').then(({ resetRouter }) => resetRouter())
      // 主动登出也会触发后端 force-logout 推送:只静默清会话,不谎报「被强制下线」。
      if (!silent) message.warning(t('realtime.forcedLogout'))
      if (router.currentRoute.value.path !== '/login') router.replace('/login')
    })
    // 公告变更:通知 NoticeBell 立即重拉未读(替代最长 30s 轮询延迟)
    conn.on('notice-changed', () => noticeBus.emit())

    connection = conn
    conn.start().catch(() => {
      // 初次连接失败(后端未开启实时 → Hub 404):静默退回轮询兜底,不重试刷屏。
      // withAutomaticReconnect 只在「连过又断」后重连,不重试初次 start,故这里不会造成对已关实时的后端反复叩门。
      if (connection === conn) connection = null
    })
  }

  return { start, stop: stopConnection }
}
