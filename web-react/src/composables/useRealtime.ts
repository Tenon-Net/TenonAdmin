import { useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { App } from 'antd'
import { useTranslation } from 'react-i18next'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'

/**
 * 未读通知刷新总线:useRealtime 收到后端 `notice-changed` 推送后 emit,顶栏未读角标订阅后立即重拉。
 * 解耦长连接与角标(二者互不 import 对方)。模块级 pub/sub 替 Vue 侧的 VueUse `useEventBus`;
 * 与 web 一样内联于本文件(唯一消费者是 LayoutShell 的未读轮询,不单开文件)。
 */
type Listener = () => void
const listeners = new Set<Listener>()
export const noticeBus = {
  /** 订阅未读变更,返回退订函数。 */
  on(fn: Listener): () => void {
    listeners.add(fn)
    return () => listeners.delete(fn)
  },
  emit(): void {
    listeners.forEach((fn) => fn())
  },
}

// 进程内单例连接:鉴权外壳挂载一次即建一条;重复 start 幂等(已连不再建)。
let connection: HubConnection | null = null
// 主动登出/改密重登:后端 Logout 也走 RevokeAsync → 推 force-logout;本页应静默,勿弹「您已被强制下线」。
// 其它同会话标签页仍会收到推送并提示。下次 start 清零。
let voluntaryLogout = false

function start(onForceLogout: Listener, onNoticeChanged: Listener): void {
  if (connection || !useUserStore.getState().accessToken) return
  voluntaryLogout = false
  try {
    const baseUrl = import.meta.env.VITE_API_BASE ?? ''
    const conn = new HubConnectionBuilder()
      // 令牌走 query `access_token`(浏览器 WebSocket 带不了 Authorization 头);getState 每次取最新令牌。
      .withUrl(`${baseUrl}/hub/realtime`, { accessTokenFactory: () => useUserStore.getState().accessToken })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    conn.on('force-logout', onForceLogout)
    conn.on('notice-changed', onNoticeChanged)

    connection = conn
    conn.start().catch(() => {
      // 初次连接失败(后端未开启实时 → Hub 404):静默退回未读轮询兜底 + 下次请求 401 惰性登出,不重试刷屏。
      // withAutomaticReconnect 只在「连过又断」后重连,不重试初次 start,故不会对已关实时的后端反复叩门。
      // 归属判断:仅当单例仍指向本连接才置空 —— StrictMode 双挂载下,本连接被 stop 中止后的 reject 不能抹掉后一次挂载已建的活连接。
      if (connection === conn) connection = null
    })
  } catch {
    // build()/withUrl() 可同步抛(URL 无法解析等):实时是纯增强,静默退回轮询兜底,绝不拖垮鉴权外壳。
    connection = null
  }
}

async function stop(): Promise<void> {
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
  await stop()
}

/**
 * 实时通知客户端(SignalR)。在鉴权外壳(LayoutShell)挂载时 start、卸载时 stop —— 整个会话只挂一次。
 * 后端 `TenonAdmin:Realtime:Enabled` 关闭时 Hub 不存在 → 初次连接失败,**静默退回**未读轮询兜底与惰性 401 登出(纯增强,无回归)。
 */
export function useRealtime(): void {
  const navigate = useNavigate()
  const { message } = App.useApp()
  const { t } = useTranslation()

  useEffect(() => {
    start(
      () => {
        // 强制下线:停连 + 清会话 + 提示 + 跳登录(与本地登出同款收尾,少一次 authApi.logout —— 服务端已强制)。
        const silent = voluntaryLogout
        voluntaryLogout = false
        void stop()
        useAuthStore.getState().reset()
        useUserStore.getState().clear()
        // 主动登出也会触发后端 force-logout 推送:只静默清会话,不谎报「被强制下线」。
        if (!silent) message.warning(t('realtime.forcedLogout'))
        if (window.location.pathname !== '/login') navigate('/login', { replace: true })
      },
      () => noticeBus.emit(),
    )
    return () => void stop()
    // 一次性捕获挂载时的 navigate/message/t:鉴权外壳整个会话只挂一次,与 Vue setup 同款(切换语言后强制下线走旧语言文案,可接受)。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
}
