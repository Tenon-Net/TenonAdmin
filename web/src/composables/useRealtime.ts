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
    const baseUrl = import.meta.env.VITE_API_BASE ?? ''
    const conn = new HubConnectionBuilder()
      .withUrl(`${baseUrl}/hub/realtime`, { accessTokenFactory: () => useUserStore().accessToken })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    // 强制下线:清会话 + 提示 + 跳登录(与 api/client.ts 刷新失败路径同款收尾)
    conn.on('force-logout', () => {
      void stop()
      useUserStore().clear()
      message.warning(t('realtime.forcedLogout'))
      if (router.currentRoute.value.path !== '/login') router.replace('/login')
    })
    // 公告变更:通知 NoticeBell 立即重拉未读(替代最长 30s 轮询延迟)
    conn.on('notice-changed', () => noticeBus.emit())

    connection = conn
    conn.start().catch(() => {
      // 初次连接失败(后端未开启实时 → Hub 404):静默退回轮询兜底,不重试刷屏。
      // withAutomaticReconnect 只在「连过又断」后重连,不重试初次 start,故这里不会造成对已关实时的后端反复叩门。
      connection = null
    })
  }

  async function stop() {
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

  return { start, stop }
}
