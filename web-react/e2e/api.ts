import type { APIRequestContext } from '@playwright/test'
import { ADMIN_ACCOUNT, ADMIN_PASSWORD } from './helpers'

/** Set by playwright.config.ts to the unique host URL for this run. */
const apiBase = () => process.env.TENON_E2E_API_BASE ?? 'http://127.0.0.1:5101'

type Envelope<T> = { code: number; msg?: string; data?: T; args?: Record<string, unknown> }

async function readEnvelope<T>(res: { ok: () => boolean; status: () => number; json: () => Promise<unknown> }): Promise<Envelope<T>> {
  const body = (await res.json()) as Envelope<T>
  if (!res.ok() && body?.code === undefined) {
    throw new Error(`HTTP ${res.status()} without envelope`)
  }
  return body
}

/** Password login as super admin; returns bearer token. */
export async function apiAdminToken(request: APIRequestContext): Promise<string> {
  const res = await request.post(`${apiBase()}/api/v1/auth/login`, {
    data: { account: ADMIN_ACCOUNT, password: ADMIN_PASSWORD },
  })
  const env = await readEnvelope<{ accessToken: string }>(res)
  if (env.code !== 0 || !env.data?.accessToken) {
    throw new Error(`admin login failed: code=${env.code} msg=${env.msg}`)
  }
  return env.data.accessToken
}

export async function apiCreateUser(
  request: APIRequestContext,
  token: string,
  input: { account: string; name: string; password: string; forceTotp?: boolean },
): Promise<number> {
  const res = await request.post(`${apiBase()}/api/v1/sys/user`, {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      account: input.account,
      name: input.name,
      password: input.password,
      enabled: true,
      forceTotp: input.forceTotp ?? true,
      roleIds: [],
    },
  })
  const env = await readEnvelope<{ id: number }>(res)
  if (env.code !== 0 || env.data?.id == null) {
    throw new Error(`create user failed: code=${env.code} msg=${env.msg}`)
  }
  return env.data.id
}

/** 建 ForceTotp 用户供自助绑定 e2e(ADR 0006:无邀请)。宿主须启用 Totp:Enabled。 */
export async function seedForceTotpUser(request: APIRequestContext): Promise<{
  account: string
  password: string
  userId: number
}> {
  const account = `e2e_mfa_${Date.now().toString(36)}`
  const password = 'TestPass123!'
  const admin = await apiAdminToken(request)
  const userId = await apiCreateUser(request, admin, {
    account,
    name: 'E2E MFA User',
    password,
    forceTotp: true,
  })
  return { account, password, userId }
}

const TOTP_FEATURE_KEY = 'sys.security.totp.enabled'

/** 运行时打开/关闭 TOTP 总闸。须在 RequireReauth 尚未生效时打开;关闭前先密码再认证。 */
export async function setTotpFeatureEnabled(
  request: APIRequestContext,
  token: string,
  enabled: boolean,
): Promise<void> {
  const res = await request.put(`${apiBase()}/api/v1/sys/config/batch`, {
    headers: { Authorization: `Bearer ${token}` },
    data: [{ configKey: TOTP_FEATURE_KEY, configValue: enabled ? 'true' : 'false' }],
  })
  const env = await readEnvelope<boolean>(res)
  if (env.code !== 0) {
    throw new Error(`set totp feature failed: code=${env.code} msg=${env.msg}`)
  }
}

/** 高危写 40024 后用当前管理员密码完成短时再认证。 */
export async function apiReauthWithPassword(
  request: APIRequestContext,
  token: string,
  password = ADMIN_PASSWORD,
): Promise<void> {
  const res = await request.post(`${apiBase()}/api/v1/auth/reauth`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { method: 'password', password },
  })
  const env = await readEnvelope<boolean>(res)
  if (env.code !== 0) {
    throw new Error(`reauth failed: code=${env.code} msg=${env.msg}`)
  }
}
