// 用户新增/编辑表单的**纯逻辑**:表单模型、空值语义映射、超管/权限判据。
// 抽出来是为了能被 userForm.spec 变异钉死 —— 页面 index.tsx 只做接线(弹窗/表格),
// 真正会静默出错的两处(空值语义、超管自锁保护)在这里,单测覆盖。
import type { AddUserInput, UpdateUserInput, UserDetail } from '@/types/api'

/**
 * 编辑弹窗的字段模型。账号/姓名/昵称/手机/邮箱/性别/机构/职位/主管/角色/状态都有编辑控件;
 * `avatar` 无 Form 控件(受控 state 挂上传组件),save 时并回。模型必须涵盖**全部可写字段**:
 * `UpdateUserInput` 是全量替换语义(缺字段即置空),编辑时少带一个就会把用户对应字段**清掉**。
 */
export interface UserForm {
  account: string
  password: string
  name: string
  nickname: string
  phone: string
  email: string
  gender: string | null
  enabled: boolean
  forceTotp: boolean
  /** 只读回显;不写入 Update 入参 */
  totpEnabled: boolean
  roleIds: number[]
  orgId: number | null
  positionId: number | null
  directorId: number | null
  // 无 Form 控件(受控 state 挂上传组件),save 时并回;仍进模型防全量替换清空
  avatar: string | null
}

export const blankForm = (): UserForm => ({
  account: '', password: '', name: '', nickname: '', phone: '', email: '',
  gender: null, enabled: true, forceTotp: false, totpEnabled: false, roleIds: [],
  orgId: null, positionId: null, directorId: null, avatar: null,
})

/** detail → 编辑回显。password 永远空(编辑不改密,改密走专用重置端点);可空文本 null → '' 便于绑 input。 */
export function detailToForm(d: UserDetail): UserForm {
  return {
    account: d.account,
    password: '',
    name: d.name,
    nickname: d.nickname ?? '',
    phone: d.phone ?? '',
    email: d.email ?? '',
    gender: d.gender ?? null,
    enabled: d.enabled,
    forceTotp: !!d.forceTotp,
    totpEnabled: !!d.totpEnabled,
    roleIds: d.roleIds ?? [],
    orgId: d.orgId ?? null,
    positionId: d.positionId ?? null,
    directorId: d.directorId ?? null,
    avatar: d.avatar ?? null,
  }
}

/**
 * 新增入参。**两种空值语义不同,别统一**:
 *   - password 留空 → `undefined`(**省略字段**)→ 后端生成随机强口令(经出参回传,当场展示一次)。
 *     若写成 `|| null` 会把 null 当"显式设空口令"送过去,建出无法登录的号。
 *   - 其余可空文本留空 → `null`(**显式置空**),与后端"缺字段即置空"一致。
 */
export function toAddInput(f: UserForm): AddUserInput {
  return {
    account: f.account,
    password: f.password || undefined,
    name: f.name,
    nickname: f.nickname || null,
    phone: f.phone || null,
    email: f.email || null,
    gender: f.gender,
    avatar: f.avatar,
    orgId: f.orgId,
    positionId: f.positionId,
    directorId: f.directorId,
    enabled: f.enabled,
    forceTotp: f.forceTotp,
    roleIds: f.roleIds,
  }
}

/** 更新入参(全量替换):所有可写字段一并回送,缺一个就被后端置空。 */
export function toUpdateInput(f: UserForm): UpdateUserInput {
  return {
    name: f.name,
    nickname: f.nickname || null,
    phone: f.phone || null,
    email: f.email || null,
    gender: f.gender,
    avatar: f.avatar,
    orgId: f.orgId,
    positionId: f.positionId,
    directorId: f.directorId,
    enabled: f.enabled,
    forceTotp: f.forceTotp,
    roleIds: f.roleIds,
  }
}

// ── 行内动作判据 ──────────────────────────────────────────────
// 超管行禁删/禁停用是**自锁保护**(停了就没法从 UI 恢复,后端也会拒),不是样式;和权限码判据合成一个谓词,
// 这样列渲染只问"能不能",判据本身在这里被单测钉住。has = authStore.hasPerm。

// 编辑/重置不受自锁保护(超管也能改自己),签名仍收行参保持四个判据统一调用形态;行未用故 _r。
export const canEdit = (_r: { isSuperAdmin: boolean }, has: (c: string) => boolean) =>
  has('PUT:/api/v1/sys/user/{id}')

export const canReset = (_r: { isSuperAdmin: boolean }, has: (c: string) => boolean) =>
  has('PUT:/api/v1/sys/user/{id}/password')

/** 删除:超管行一律不可(自锁保护),叠加删除权限码。 */
export const canDelete = (r: { isSuperAdmin: boolean }, has: (c: string) => boolean) =>
  !r.isSuperAdmin && has('DELETE:/api/v1/sys/user/{id}')

/** 启停:超管行一律不可(自锁保护),叠加启停权限码。 */
export const canToggleEnabled = (r: { isSuperAdmin: boolean }, has: (c: string) => boolean) =>
  !r.isSuperAdmin && has('PUT:/api/v1/sys/user/{id}/enabled')
