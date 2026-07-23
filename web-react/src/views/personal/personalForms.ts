// personal 中心纯逻辑(变异钉):头像首字母、手机/邮箱格式校验(选填)、资料表单载入/保存映射。
// 对齐 Vue 侧 profile.vue / password.vue 的同名逻辑。UI 接线在各 *.tsx。
import type { UserProfile } from '@/types/api'

/** 头像回退:姓名首字母大写;空名回退 ?。 */
export function initial(name: string): string {
  return name.trim().charAt(0).toUpperCase() || '?'
}

/** ISO 时间 → 'YYYY-MM-DD HH:mm:ss'(去 T 截到秒)。notice/sessions/bindings 共用。 */
export function fmtDateTime(s?: string | null): string {
  return (s ?? '').replace('T', ' ').slice(0, 19)
}

/** 手机号校验:选填,填了才验(大陆 11 位)。与管理端 user 页同规则。 */
export function isPhone(v: string): boolean {
  return !v || /^1[3-9]\d{9}$/.test(v)
}
/** 邮箱校验:选填,填了才验。 */
export function isEmail(v: string): boolean {
  return !v || /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(v)
}

export interface ProfileForm {
  account: string
  name: string
  nickname: string
  phone: string
  email: string
  gender: string | null
  avatar: string | null
  org: string
  position: string
}

/** UserProfile → 表单态。org/position 只读展示名(未分配/已删回落 —)。 */
export function profileToForm(p: UserProfile): ProfileForm {
  return {
    account: p.account,
    name: p.name,
    nickname: p.nickname ?? '',
    phone: p.phone ?? '',
    email: p.email ?? '',
    gender: p.gender ?? null,
    avatar: p.avatar ?? null,
    org: p.orgName ?? '—',
    position: p.positionName ?? '—',
  }
}

/** 可编辑字段子集(资料表单注册的 6 项;account/org/position 只读不入表单)。 */
export type ProfileEditable = Pick<ProfileForm, 'name' | 'nickname' | 'phone' | 'email' | 'gender' | 'avatar'>

/** 表单态 → updateProfile 入参(全量替换语义;选填空串归一 null)。 */
export function formToUpdate(f: ProfileEditable) {
  return {
    name: f.name,
    nickname: f.nickname || null,
    phone: f.phone || null,
    email: f.email || null,
    gender: f.gender,
    avatar: f.avatar,
  }
}
