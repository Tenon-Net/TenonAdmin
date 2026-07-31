/**
 * 短时再认证(40024)闸门:client 中间件在 403 + ReauthRequired 时调用,
 * UI 宿主(ReauthModal)注册 handler 弹窗收 TOTP/密码后 resolve。
 * 并发 403 合流到同一次弹窗。
 */

export const REAUTH_REQUIRED_CODE = 40024
export const REAUTH_RETRY_HEADER = 'X-Tenon-Reauth-Retried'

type ReauthHandler = () => Promise<boolean>

let handler: ReauthHandler | null = null
let inflight: Promise<boolean> | null = null

/** 布局/App 挂载时注册;卸载时传 null。 */
export function registerReauthHandler(h: ReauthHandler | null): void {
  handler = h
}

/**
 * 请求一次再认证。无 handler 或用户取消 → false。
 * 已有进行中的弹窗则复用同一 Promise。
 */
export function requestReauth(): Promise<boolean> {
  if (inflight) return inflight
  inflight = (async () => {
    try {
      if (!handler) return false
      return await handler()
    } finally {
      inflight = null
    }
  })()
  return inflight
}
