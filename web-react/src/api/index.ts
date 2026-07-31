import { client } from './client'
import type { components } from './schema'
import type { AddUserInput, AddUserOutput, ChunkInitOutput, ConfigInput, CronPreviewOutput, DashboardSummary, DataScopeType, DictItem, DictItemInput, DictTypeInput, DuplicateStrategy, FileUploadOutput, ImportCommitResult, ImportPreview, ImportRow, JobDashboard, JobHandlersOutput, JobInput, LoginOutput, ModuleInput, ModuleRow, MyModulesOutput, MySessionItem, NoticeMineItem, NoticePublishInput, OnlineSessionItem, OrgInput, PagedList, PermissionRouteItem, PositionInput, RoleInput, ServerInfoOutput, SysConfig, SysDictItem, SysDictType, SysExceptionLog, SysFile, SysJob, SysJobLog, SysLoginLog, SysNotice, SysOpLog, SysOrg, SysPosition, SysRole, SysRoleDataScope, UpdateUserInput, UserDetail, UserItem, UserProfile } from '@/types/api'
import type { MenuInput, MenuNode, MenuTreeNode } from '@/types/menu'

/** 业务错误(含后端 code / msgKey);视图 catch 后经 translateError 展示。 */
export class ApiError extends Error {
  code: number
  msgKey?: string
  args?: Record<string, unknown>
  constructor(code: number, msgKey?: string, args?: Record<string, unknown>, message?: string) {
    super(message ?? msgKey ?? `Error ${code}`)
    this.name = 'ApiError'
    this.code = code
    this.msgKey = msgKey
    this.args = args
  }
}

interface Envelope {
  code?: number
  msgKey?: string
  args?: Record<string, unknown>
  message?: string
  data?: unknown
}

function malformedResponse(response: Response): ApiError {
  return new ApiError(response.status, undefined, undefined, 'Malformed API response')
}

/**
 * 解包 openapi-fetch 结果,同时容忍两种形状:
 *   - 2xx:body 是 Result<T> 信封;code!==0 抛 ApiError,否则返回 data.data。
 *   - 非 2xx:enveloped(401/403/429)有 code → 抛带 code 的 ApiError;
 *            ProblemDetails(400 校验 / 500)无 code → 抛 status + title/detail。
 */
export function unwrap<T>(res: { data?: unknown; error?: unknown; response: Response }): T {
  const { data, error, response } = res
  if (error !== undefined && error !== null) {
    const env = error as Envelope
    if (typeof env.code === 'number') {
      throw new ApiError(env.code, env.msgKey, env.args, env.message)
    }
    const pd = error as { title?: string; detail?: string }
    throw new ApiError(response.status, undefined, undefined, pd.title ?? pd.detail ?? response.statusText)
  }
  if (!data || typeof data !== 'object') throw malformedResponse(response)
  const env = data as Envelope
  if (typeof env.code !== 'number') throw malformedResponse(response)
  if (env.code !== 0) {
    throw new ApiError(env.code, env.msgKey, env.args, env.message)
  }
  return env.data as T
}

/**
 * 分页三件套复用(每个列表端点都重复):
 *   - 前端 {page,pageSize} → 后端 record 属性 {Current,Size}(PascalCase;ASP.NET 绑定大小写不敏感但类型要 Pascal)。
 *     `...pageParams(p)` 展进各端点自己的 PascalCase 过滤条件,查询对象仍逐端点强类型校验(不走 any)。
 *   - 后端 PagedList<T>({current,size,total,items}) → ProTable fetcher 契约的 {items,total}。
 *
 * 二者连同 unwrap/ApiError 一起导出,是<b>消费者写自己 API 模块的接缝</b>:消费者新建 `api/<域>.ts`
 * 从这里 import,就不必把自己的端点塞进本文件。本文件是上游自留地(改动频繁),消费者一旦编辑它,
 * 每次 merge upstream 都冲突 —— 所以这两个 export 没有站内调用方也必须留着,别当未使用导出清掉。
 */
export const pageParams = (p: { page: number; pageSize: number }) => ({ Current: p.page, Size: p.pageSize })

export function toPage<T>(res: Parameters<typeof unwrap>[0]): { items: T[]; total: number } {
  const p = unwrap<PagedList<T>>(res)
  if (!Array.isArray(p.items) || typeof p.total !== 'number') {
    throw new ApiError(res.response.status, undefined, undefined, 'Malformed paged API response')
  }
  return { items: p.items, total: p.total }
}

/**
 * 下载 xlsx 等二进制:parseAs blob + 手查 ok(excel-ledger 坑 2:unwrap 假定 JSON 信封)。
 * AdminExceptionFilter 对业务失败仍回 HTTP 200 + JSON 信封,故还要辨 content-type /
 * 是否以 `{` 开头,避免把错误信封当 xlsx 触发下载。
 */
async function unwrapDownload(res: { data?: unknown; error?: unknown; response: Response }): Promise<Blob> {
  const { data, error, response } = res
  if (!response.ok) {
    const env = (error ?? {}) as Envelope
    if (typeof env.code === 'number') throw new ApiError(env.code, env.msgKey, env.args, env.message)
    throw new ApiError(response.status, undefined, undefined, response.statusText)
  }
  const blob = data as Blob
  const ct = response.headers.get('content-type') ?? ''
  if (ct.includes('spreadsheet') || ct.includes('octet-stream')) return blob
  // 200 + application/json = 业务失败信封(导出行数超限等)
  if (ct.includes('json') || ct.includes('text/plain')) {
    const env = JSON.parse(await blob.text()) as Envelope
    if (typeof env.code === 'number' && env.code !== 0) {
      throw new ApiError(env.code, env.msgKey, env.args, env.message)
    }
  } else {
    // content-type 缺失时嗅探:xlsx 是 zip(`PK`),信封以 `{` 开头
    const head = new Uint8Array(await blob.slice(0, 1).arrayBuffer())
    if (head[0] === 0x7b /* { */) {
      const env = JSON.parse(await blob.text()) as Envelope
      if (typeof env.code === 'number' && env.code !== 0) {
        throw new ApiError(env.code, env.msgKey, env.args, env.message)
      }
    }
  }
  return blob
}

/** 归一 ImportPreview:int64/int 序列化噪音 → number,缺省数组补齐。 */
function normalizePreview(raw: ImportPreview): ImportPreview {
  return {
    headers: raw.headers ?? [],
    mapping: raw.mapping ?? {},
    columns: (raw.columns ?? []).map((c) => ({
      key: c.key,
      title: c.title,
      required: c.required,
      dictTypeCode: c.dictTypeCode,
      hint: c.hint,
      width: c.width != null ? Number(c.width) : undefined,
    })),
    rows: (raw.rows ?? []).map(normalizeRow),
    total: Number(raw.total ?? 0),
    errorRows: Number(raw.errorRows ?? 0),
    columnErrors: (raw.columnErrors ?? []).map((e) => ({
      columnKey: e.columnKey,
      code: Number(e.code),
      args: e.args,
    })),
  }
}

function normalizeRow(r: ImportRow): ImportRow {
  return {
    index: Number(r.index ?? 0),
    cells: { ...r.cells },
    errors: (r.errors ?? []).map((e) => ({
      columnKey: e.columnKey,
      code: Number(e.code),
      args: e.args,
    })),
  }
}

function normalizeCommit(raw: ImportCommitResult): ImportCommitResult {
  return {
    total: Number(raw.total ?? 0),
    inserted: Number(raw.inserted ?? 0),
    updated: Number(raw.updated ?? 0),
    skipped: Number(raw.skipped ?? 0),
    failed: Number(raw.failed ?? 0),
    failures: (raw.failures ?? []).map(normalizeRow),
  }
}

/**
 * 回传行:schema 把 cells 标成 `Record<string, string>`(无 null),而运行时/编辑态允许空。
 * 发送前把 null/undefined 收成 `''`,避免 openapi-fetch 类型卡死。
 */
function toWireRows(rows: ImportRow[]) {
  return rows.map((r) => {
    const cells: Record<string, string> = {}
    for (const [k, v] of Object.entries(r.cells ?? {})) cells[k] = v ?? ''
    return {
      index: r.index,
      cells,
      errors: r.errors.map((e) => ({
        columnKey: e.columnKey,
        code: e.code,
        args: e.args as Record<string, never> | null | undefined,
      })),
    }
  })
}

export const authApi = {
  login: (body: { account: string; password: string; captchaId?: string; captchaCode?: string }) =>
    client.POST('/api/v1/auth/login', { body }).then((r) => unwrap<LoginOutput>(r)),
  logout: () => client.POST('/api/v1/auth/logout', {}).then((r) => unwrap<boolean>(r)),
  captcha: () => client.GET('/api/v1/auth/captcha', {}).then((r) => unwrap<{ captchaId: string; svg: string; type?: string }>(r)),
  /** 短信二次验证:凭密码登录 40009 信令下发的挑战 Id + 短信码完成登录。 */
  smsChallengeLogin: (body: { challengeId: string; code: string }) =>
    client.POST('/api/v1/auth/login/sms', { body }).then((r) => unwrap<LoginOutput>(r)),
  /** 短信二次验证:重发验证码(服务端冷却/日上限,过频抛 40008)。 */
  smsChallengeResend: (body: { challengeId: string }) =>
    client.POST('/api/v1/auth/login/sms/resend', { body }).then((r) => unwrap<{ expiresSeconds: number; resendSeconds: number }>(r)),
  /** TOTP 二次验证完成登录(40018 / SSO totpChallenge);走 openapi-fetch → CSRF/credentials 中间件生效。 */
  totpChallengeLogin: (body: { challengeId: string; code: string }) =>
    client.POST('/api/v1/auth/login/totp', { body }).then((r) => unwrap<LoginOutput>(r)),
  /** 短时再认证(Level3 高危写操作 40024 后由中间件唤起;窗口约 5 分钟)。 */
  reauth: (body: { method?: string; totpCode?: string; password?: string }) =>
    client.POST('/api/v1/auth/reauth', { body }).then((r) => unwrap<void>(r)),
  /** 免密登录发码(图形验证码启用时须携带;响应不区分手机号是否存在,防枚举)。 */
  smsLoginSend: (body: { phone: string; captchaId?: string; captchaCode?: string }) =>
    client.POST('/api/v1/auth/sms/send', { body }).then((r) => unwrap<{ expiresSeconds: number; resendSeconds: number }>(r)),
  /** 免密登录:手机号 + 短信码换令牌。 */
  smsLogin: (body: { phone: string; code: string }) =>
    client.POST('/api/v1/auth/sms/login', { body }).then((r) => unwrap<LoginOutput>(r)),
}

/** 高敏权限自定义追加(内核默认只读;写操作须 reauth)。 */
export const highSensApi = {
  list: () =>
    client.GET('/api/v1/sys/mfa/high-sensitivity', {}).then((r) => unwrap<components['schemas']['HighSensitivityPermissionList']>(r)),
  add: (body: components['schemas']['HighSensitivityPermissionInput']) =>
    client.POST('/api/v1/sys/mfa/high-sensitivity', { body }).then((r) => unwrap<components['schemas']['HighSensitivityPermissionItem']>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/mfa/high-sensitivity/{id}', { params: { path: { id } } }).then((r) => unwrap<void>(r)),
}

/** TOTP 自助绑定 / 恢复 / 管理员清除(ADR 0006;无邀请路径)。 */
export const mfaApi = {
  bindStart: (body: components['schemas']['TotpBindStartInput']) =>
    client.POST('/api/v1/auth/mfa/bind/start', { body }).then((r) => unwrap<components['schemas']['TotpBindStartOutput']>(r)),
  bindComplete: (body: components['schemas']['TotpBindCompleteInput']) =>
    client.POST('/api/v1/auth/mfa/bind/complete', { body }).then((r) => unwrap<components['schemas']['TotpBindCompleteOutput']>(r)),
  recovery: (body: components['schemas']['TotpRecoveryInput']) =>
    client.POST('/api/v1/auth/mfa/recovery', { body }).then((r) => unwrap<void>(r)),
  clear: (body: components['schemas']['TotpClearMfaInput']) =>
    client.POST('/api/v1/sys/mfa/clear', { body }).then((r) => unwrap<void>(r)),
}

export interface ExternalProvider {
  code: string
  displayName: string
  icon?: string | null
}

export interface ExternalBinding {
  provider: string
  displayName?: string | null
  boundAt: string
}

// 外部登录 / SSO(批次 D)。登录:providers 点亮按钮 → 顶层导航到 authorizeUrl(后端 302 跳 IdP)→ 回调页 exchange 换令牌。
// 绑定(个人中心):bindStart 拿授权 URL 跳转 → 同一回调把外部身份绑到当前用户;bindings 查、unbind 解绑。
export const externalAuthApi = {
  /** 登录页可用的外部登录方式(启用的);空数组 = 不显 SSO 区。 */
  providers: () => client.GET('/api/v1/auth/external/providers', {}).then((r) => unwrap<ExternalProvider[]>(r)),
  /** 发起某 provider 登录的 URL(顶层浏览器导航,不走 fetch —— 后端 302 跳 IdP)。 */
  authorizeUrl: (code: string) =>
    `${import.meta.env.VITE_API_BASE ?? ''}/api/v1/auth/external/${encodeURIComponent(code)}/authorize`,
  /** 一次性票据换令牌(登录回调后);票据无效/过期/已用抛 40014。 */
  exchange: (ticket: string) =>
    client.POST('/api/v1/auth/external/exchange', { body: { ticket } }).then((r) => unwrap<LoginOutput>(r)),
  /** 我的外部账号绑定列表(个人中心)。 */
  bindings: () => client.GET('/api/v1/auth/external/bindings', {}).then((r) => unwrap<ExternalBinding[]>(r)),
  /** 发起绑定:返回授权 URL,前端跳转开始 OAuth 往返(回调把身份绑到当前用户)。 */
  bindStart: (code: string) =>
    client
      .POST('/api/v1/auth/external/{provider}/bind', { params: { path: { provider: code } } })
      .then((r) => unwrap<{ authorizeUrl: string }>(r)),
  /** 解绑某 provider 的外部账号。 */
  unbind: (code: string) =>
    client
      .DELETE('/api/v1/auth/external/{provider}/binding', { params: { path: { provider: code } } })
      .then((r) => unwrap<boolean>(r)),
}

export const dashboardApi = {
  /** 工作台首页统计([ActiveSession]:任何登录用户可取,无需权限码)。 */
  summary: () => client.GET('/api/v1/dashboard/summary', {}).then((r) => unwrap<DashboardSummary>(r)),
}

export const personalApi = {
  modules: () => client.GET('/api/v1/personal/modules', {}).then((r) => unwrap<MyModulesOutput>(r)),
  menu: (moduleId: number) =>
    client.GET('/api/v1/personal/menu', { params: { query: { moduleId } } }).then((r) => unwrap<MenuNode[]>(r)),
  setDefaultModule: (moduleId: number) =>
    client.PUT('/api/v1/personal/default-module', { body: { moduleId } }).then((r) => unwrap<boolean>(r)),
  profile: () => client.GET('/api/v1/personal/profile', {}).then((r) => unwrap<UserProfile>(r)),
  /** 改自己的资料(全量替换语义:缺字段即置空,前端整表单提交)。 */
  updateProfile: (body: { name: string; nickname?: string | null; phone?: string | null; email?: string | null; gender?: string | null; avatar?: string | null }) =>
    client.PUT('/api/v1/personal/profile', { body }).then((r) => unwrap<boolean>(r)),
  /** 我的活跃会话列表(个人视角;isCurrent = 本次请求所用会话)。 */
  sessions: () => client.GET('/api/v1/personal/sessions', {}).then((r) => unwrap<MySessionItem[]>(r)),
  /** 下线自己的某个会话;他人的/不存在的一律 42024(不区分,防探测)。 */
  kickSession: (sessionId: string) =>
    client.DELETE('/api/v1/personal/sessions/{sessionId}', { params: { path: { sessionId } } }).then((r) => unwrap<boolean>(r)),
  updatePassword: (body: { oldPassword: string; newPassword: string }) =>
    client.PUT('/api/v1/personal/password', { body }).then((r) => unwrap<boolean>(r)),
  /** 当前用户权限码集合(= 规范化路由);喂给 authStore.permissionCodes 驱动 v-auth。超管返回空集。 */
  permissions: () => client.GET('/api/v1/personal/permissions', {}).then((r) => unwrap<string[]>(r)),
}

export const userApi = {
  /** 归一后端 PagedList<UserItem>({current,size,total,items}) → ProTable fetcher 契约的 {items,total}。 */
  page: (params: { page: number; pageSize: number; account?: string; name?: string; orgId?: number; roleId?: number; sortField?: string; sortOrder?: string }) =>
    client
      .GET('/api/v1/sys/user/page', {
        // 查询参数名沿用后端 record 属性(PascalCase);ASP.NET 绑定大小写不敏感,类型要求 PascalCase。
        // roleId:角色反查(「这个角色有哪些人」/ 删角色前报人数)复用本端点,不另开接口。
        // sortField/sortOrder:ProTable 排序;后端按实体列白名单校验(非法忽略回退默认),见 PagedListExtensions.OrderBySafe。
        params: {
          query: {
            ...pageParams(params),
            Account: params.account,
            Name: params.name,
            OrgId: params.orgId,
            RoleId: params.roleId,
            SortField: params.sortField,
            SortOrder: params.sortOrder,
          },
        },
      })
      .then((r) => toPage<UserItem>(r)),
  /** 用户详情(含 roleIds,编辑回显 + 提交时原样带回避免清空角色)。 */
  detail: (id: number) => client.GET('/api/v1/sys/user/{id}', { params: { path: { id } } }).then((r) => unwrap<UserDetail>(r)),
  /** 新增用户;返回新 Id + 实际生效的初始口令(留空 password 时后端随机生成,不接住就没人知道这个号的密码)。 */
  add: (body: AddUserInput) => client.POST('/api/v1/sys/user', { body }).then((r) => unwrap<AddUserOutput>(r)),
  update: (id: number, body: UpdateUserInput) =>
    client.PUT('/api/v1/sys/user/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) => client.DELETE('/api/v1/sys/user/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** 批量软删除;集合含超管后端整体拒绝(SuperAdminProtected)。 */
  batchRemove: (ids: number[]) => client.POST('/api/v1/sys/user/batch-delete', { body: { ids } }).then((r) => unwrap<boolean>(r)),
  /** 重置密码;返回实际生效的初始密码(newPassword 留空 = 后端默认初始密码)。 */
  resetPassword: (id: number, newPassword?: string | null) =>
    client.PUT('/api/v1/sys/user/{id}/password', { params: { path: { id } }, body: { newPassword: newPassword || null } }).then((r) => unwrap<string>(r)),
  /** 专用启停端点(非全量 update)。 */
  setEnabled: (id: number, enabled: boolean) =>
    client.PUT('/api/v1/sys/user/{id}/enabled', { params: { path: { id } }, body: { enabled } }).then((r) => unwrap<boolean>(r)),

  // ── 导入 / 导出(excel-ledger §5.1;下载走坑 2,上传走坑 3) ──

  /** 下载用户导入模板(xlsx)。[ActiveSession],无需独立权限码。 */
  importTemplate: () =>
    client.GET('/api/v1/sys/user/import/template', { parseAs: 'blob' }).then((r) => unwrapDownload(r)),

  /**
   * 上传 xlsx 预览校验。multipart:`file` + 可选 `mapping`(JSON 字符串:表头→列 Key)。
   * bodySerializer 建 FormData(坑 3:与 fileApi.upload 同型)。
   */
  importPreview: (file: File, mapping?: Record<string, string>) =>
    client
      .POST('/api/v1/sys/user/import/preview', {
        body: {
          file: file as unknown as string,
          mapping: mapping ? JSON.stringify(mapping) : undefined,
        },
        bodySerializer: (body) => {
          const b = body as unknown as { file: File; mapping?: string }
          const fd = new FormData()
          fd.append('file', b.file)
          if (b.mapping) fd.append('mapping', b.mapping)
          return fd
        },
      })
      .then((r) => normalizePreview(unwrap<ImportPreview>(r))),

  /** 对前端改过的行重新校验(不碰文件)。 */
  importValidate: (rows: ImportRow[]) =>
    client
      .POST('/api/v1/sys/user/import/validate', { body: { rows: toWireRows(rows) } })
      .then((r) => normalizePreview(unwrap<ImportPreview>(r))),

  /** 下载错误报告 xlsx(原列 +「错误原因」列)。 */
  importErrorReport: (rows: ImportRow[]) =>
    client
      .POST('/api/v1/sys/user/import/error-report', {
        body: { rows: toWireRows(rows) },
        parseAs: 'blob',
      })
      .then((r) => unwrapDownload(r)),

  /** 按策略提交导入(部分提交:有错行跳过;服务端重校验,不信任前端 Errors)。 */
  importCommit: (rows: ImportRow[], strategy: DuplicateStrategy = 0) =>
    client
      .POST('/api/v1/sys/user/import/commit', {
        body: { rows: toWireRows(rows), strategy },
      })
      .then((r) => normalizeCommit(unwrap<ImportCommitResult>(r))),

  /**
   * 导出用户 xlsx。筛选条件与列表同源(Account/Name/OrgId/RoleId…)+ `columns` 逗号分隔列 Key。
   * 返回 blob,不进信封(§5.2)。
   */
  export: (params: {
    account?: string
    name?: string
    orgId?: number
    roleId?: number
    enabled?: boolean
    sortField?: string
    sortOrder?: string
    /** 逗号分隔列 Key;缺省 = 档案 DefaultSelected */
    columns?: string
  }) =>
    client
      .GET('/api/v1/sys/user/export', {
        params: {
          query: {
            Account: params.account,
            Name: params.name,
            OrgId: params.orgId,
            RoleId: params.roleId,
            Enabled: params.enabled,
            SortField: params.sortField,
            SortOrder: params.sortOrder,
            columns: params.columns,
          },
        },
        parseAs: 'blob',
      })
      .then((r) => unwrapDownload(r)),
}

export const orgApi = {
  /** 全部机构(平铺,按 Sort、Id 排序)。前端 buildTree 拼树。R4 下拉 / R9 树表复用。 */
  list: () => client.GET('/api/v1/sys/org/list', {}).then((r) => unwrap<SysOrg[]>(r)),
  add: (body: OrgInput) => client.POST('/api/v1/sys/org/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: OrgInput) =>
    client.PUT('/api/v1/sys/org/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/org/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** 复制机构子树(整支克隆挂到源节点同级),返回新根 Id。 */
  copy: (id: number, body?: { name?: string }) =>
    client.POST('/api/v1/sys/org/{id}/copy', { params: { path: { id } }, body }).then((r) => unwrap<number>(r)),
}

export const positionApi = {
  /** 职位分页;搜索键 name → PascalCase Name;sortField/sortOrder → 后端安全排序。R4 下拉(拉大页)/ R6 ProTable 复用。 */
  page: (params: { page: number; pageSize: number; name?: string; sortField?: string; sortOrder?: string }) =>
    client
      .GET('/api/v1/sys/position/page', {
        params: {
          query: {
            ...pageParams(params),
            Name: params.name,
            SortField: params.sortField,
            SortOrder: params.sortOrder,
          },
        },
      })
      .then((r) => toPage<SysPosition>(r)),
  add: (body: PositionInput) => client.POST('/api/v1/sys/position/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: PositionInput) =>
    client.PUT('/api/v1/sys/position/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/position/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}

export const roleApi = {
  /** 角色分页;搜索键 name → PascalCase Name。列表用 + 用户/授权页拉一大页当下拉源。 */
  page: (params: { page: number; pageSize: number; name?: string }) =>
    client
      .GET('/api/v1/sys/role/page', {
        params: { query: { ...pageParams(params), Name: params.name } },
      })
      .then((r) => toPage<SysRole>(r)),
  add: (body: RoleInput) => client.POST('/api/v1/sys/role/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: RoleInput) =>
    client.PUT('/api/v1/sys/role/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/role/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** 批量删除角色(各自级联清关联)。 */
  batchRemove: (ids: number[]) => client.POST('/api/v1/sys/role/batch-delete', { body: { ids } }).then((r) => unwrap<boolean>(r)),
  /** 某角色当前授予的菜单 Id 集合(授权抽屉回显)。 */
  getMenus: (id: number) => client.GET('/api/v1/sys/role/{id}/menus', { params: { path: { id } } }).then((r) => unwrap<number[]>(r)),
  /** 全量设置角色授予的菜单(空数组 = 收回全部)。 */
  setMenus: (roleId: number, menuIds: number[]) =>
    client.PUT('/api/v1/sys/role/menu', { body: { roleId, menuIds } }).then((r) => unwrap<boolean>(r)),
  /** 某角色的数据范围配置(未配置返回 null)。 */
  getDataScope: (id: number) =>
    client.GET('/api/v1/sys/role/{id}/datascope', { params: { path: { id } } }).then((r) => unwrap<SysRoleDataScope | null>(r)),
  /** 设置角色数据范围;customOrgIds 仅 scopeType=Custom(5)时有意义。 */
  setDataScope: (roleId: number, scopeType: DataScopeType, customOrgIds?: number[]) =>
    client.PUT('/api/v1/sys/role/datascope', { body: { roleId, scopeType, customOrgIds } }).then((r) => unwrap<boolean>(r)),
  /** 某角色当前关联的用户 Id 集合(授权用户抽屉回显)。 */
  getUsers: (id: number) =>
    client.GET('/api/v1/sys/role/{id}/users', { params: { path: { id } } }).then((r) => unwrap<number[]>(r)),
  /** 全量设置角色关联的用户(空数组 = 收回全部)。 */
  setUsers: (roleId: number, userIds: number[]) =>
    client.PUT('/api/v1/sys/role/users', { body: { roleId, userIds } }).then((r) => unwrap<boolean>(r)),
}

export const moduleApi = {
  list: () => client.GET('/api/v1/sys/module/list', {}).then((r) => unwrap<ModuleRow[]>(r)),
  add: (body: ModuleInput) => client.POST('/api/v1/sys/module/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: ModuleInput) =>
    client.PUT('/api/v1/sys/module/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/module/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}

export const configApi = {
  /** 归一后端 PagedList<SysConfig> → ProTable {items,total};搜索键 configKey/name/groupCode → PascalCase 查询参。 */
  page: (params: { page: number; pageSize: number; configKey?: string; name?: string; groupCode?: string; excludedGroupCodes?: string[] }) =>
    client
      .GET('/api/v1/sys/config/page', {
        params: {
          query: {
            ...pageParams(params), ConfigKey: params.configKey, Name: params.name,
            GroupCode: params.groupCode, ExcludedGroupCodes: params.excludedGroupCodes,
          },
        },
      })
      .then((r) => toPage<SysConfig>(r)),
  add: (body: ConfigInput) => client.POST('/api/v1/sys/config', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: ConfigInput) =>
    client.PUT('/api/v1/sys/config/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/config/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** 取某分组下全部配置(完整行,含 id/key/value),供分类配置中心结构化表单回填。 */
  listByGroup: (group: string) =>
    client
      .GET('/api/v1/sys/config/page', { params: { query: { Current: 1, Size: 200, GroupCode: group } } })
      .then((r) => unwrap<PagedList<SysConfig>>(r))
      .then((p) => p.items),
  /** 批量按键回写配置值(结构化表单保存);仅更新已存在键,未知键后端忽略。 */
  saveBatch: (items: { configKey: string; configValue?: string | null }[]) =>
    client.PUT('/api/v1/sys/config/batch', { body: items }).then((r) => unwrap<boolean>(r)),
  /** 站点信息(匿名可读:站点标题/副标题/版权等品牌展示白名单)。 */
  siteInfo: () =>
    client
      .GET('/api/v1/sys/config/site', {})
      .then((r) =>
        unwrap<{
          title?: string | null
          subtitle?: string | null
          copyright?: string | null
          copyrightUrl?: string | null
          logo?: string | null
          captchaEnabled?: boolean
          smsLoginEnabled?: boolean
        }>(r),
      ),
  /** 当前生效密码策略(任何登录用户可读);改密页据此展示真实规则清单,免配置读权限。 */
  passwordPolicy: () =>
    client
      .GET('/api/v1/sys/config/password-policy', {})
      .then((r) =>
        unwrap<{ minLength: number; requireUpper: boolean; requireLower: boolean; requireDigit: boolean; requireSpecial: boolean }>(r),
      ),
}

/**
 * ProTable 的 daterange 搜索控件回传 `[开始, 结束]` 两个日期串(YYYY-MM-DD),后端要的是 StartTime/EndTime 两个参数。
 * 结束日拼上 23:59:59,否则"筛到今天"会把今天已经发生的操作全部漏掉(日期串等价于当天 00:00:00)。
 */
function splitRange(range?: [string, string] | null) {
  if (!range || range.length !== 2) return { StartTime: undefined, EndTime: undefined }
  return { StartTime: range[0] || undefined, EndTime: range[1] ? `${range[1]} 23:59:59` : undefined }
}

export const logApi = {
  /** 登录日志分页;账号模糊 + 成败 + 时间范围。 */
  loginPage: (params: { page: number; pageSize: number; account?: string; success?: boolean; createTime?: [string, string] | null }) =>
    client
      .GET('/api/v1/sys/log/login/page', {
        params: {
          query: {
            ...pageParams(params),
            Account: params.account, Success: params.success,
            ...splitRange(params.createTime),
          },
        },
      })
      .then((r) => toPage<SysLoginLog>(r)),
  /** 清空登录日志(硬删,不可恢复)。 */
  loginClear: () => client.DELETE('/api/v1/sys/log/login', {}).then((r) => unwrap<boolean>(r)),
  /** 操作日志分页;操作名模糊 + 成败 + 操作人(精确)+ 接口路径(模糊)+ 时间范围——审计要能答"谁在什么时候干了什么"。 */
  opPage: (params: {
    page: number; pageSize: number
    title?: string; success?: boolean; operatorId?: number; path?: string
    createTime?: [string, string] | null
  }) =>
    client
      .GET('/api/v1/sys/log/op/page', {
        params: {
          query: {
            ...pageParams(params),
            Title: params.title, Success: params.success,
            OperatorId: params.operatorId, Path: params.path,
            ...splitRange(params.createTime),
          },
        },
      })
      .then((r) => toPage<SysOpLog>(r)),
  /** 清空操作日志(硬删,不可恢复)。 */
  opClear: () => client.DELETE('/api/v1/sys/log/op', {}).then((r) => unwrap<boolean>(r)),
  /**
   * 导出操作日志 xlsx。筛选与 opPage 同源 + `columns` 逗号分隔列 Key。
   * 返回 blob(坑 2);不进信封(§5.2)。
   */
  opExport: (params: {
    title?: string
    success?: boolean
    operatorId?: number
    path?: string
    createTime?: [string, string] | null
    columns?: string
  }) =>
    client
      .GET('/api/v1/sys/log/op/export', {
        params: {
          query: {
            Title: params.title,
            Success: params.success,
            OperatorId: params.operatorId,
            Path: params.path,
            ...splitRange(params.createTime),
            columns: params.columns,
          },
        },
        parseAs: 'blob',
      })
      .then((r) => unwrapDownload(r)),
  /** 异常日志分页;接口路径模糊 + 异常类型模糊 + 时间范围。 */
  exceptionPage: (params: { page: number; pageSize: number; path?: string; exceptionType?: string; createTime?: [string, string] | null }) =>
    client
      .GET('/api/v1/sys/log/exception/page', {
        params: {
          query: {
            ...pageParams(params),
            Path: params.path, ExceptionType: params.exceptionType,
            ...splitRange(params.createTime),
          },
        },
      })
      .then((r) => toPage<SysExceptionLog>(r)),
  /** 清空异常日志(硬删,不可恢复)。 */
  exceptionClear: () => client.DELETE('/api/v1/sys/log/exception', {}).then((r) => unwrap<boolean>(r)),
}

export const monitorApi = {
  /** 服务器运行快照(CPU/内存/磁盘/运行时;后端含一次约 500ms 的 CPU 采样)。 */
  server: () => client.GET('/api/v1/sys/monitor/server', {}).then((r) => unwrap<ServerInfoOutput>(r)),
}

// 缓存管理:定向失效动作(只清不看——缓存含明文 OTP、键含 PII,故后端无键浏览/取值端点)。flush* 返回被清条数。
export const cacheApi = {
  /** 清全部用户的权限/数据范围缓存(返回被清用户数)。 */
  flushAuth: () => client.POST('/api/v1/sys/cache/flush-auth', {}).then((r) => unwrap<number>(r)),
  /** 清全部字典项缓存(返回被清字典类型数)。 */
  flushDict: () => client.POST('/api/v1/sys/cache/flush-dict', {}).then((r) => unwrap<number>(r)),
  /** 清全部系统配置缓存(返回被清配置项数)。 */
  flushConfig: () => client.POST('/api/v1/sys/cache/flush-config', {}).then((r) => unwrap<number>(r)),
  /** 递增门户代际,令所有用户的门户菜单/模块缓存整体失效(返回新代际值)。 */
  rebuildPortal: () => client.POST('/api/v1/sys/cache/rebuild-portal', {}).then((r) => unwrap<number>(r)),
}

export const dictApi = {
  /** 按类型编码取字典项(服务端已按 sort 排序)。归一 int64 序列化噪音 → DictItem 投影,供 stores/dict 缓存消费。 */
  items: (typeCode: string) =>
    client
      .GET('/api/v1/sys/dict/items/{typeCode}', { params: { path: { typeCode } } })
      .then((r) => unwrap<{ label?: string; value?: string; sort?: number | string; enabled?: boolean }[]>(r))
      .then((list) =>
        list.map(
          (i): DictItem => ({
            label: i.label ?? '',
            value: i.value ?? '',
            sort: Number(i.sort ?? 0),
            enabled: i.enabled ?? true,
          }),
        ),
      ),
}

export const dictAdminApi = {
  /** 字典类型分页;搜索键 code/name → PascalCase Code/Name 查询参。 */
  typePage: (params: { page: number; pageSize: number; code?: string; name?: string }) =>
    client
      .GET('/api/v1/sys/dict/type/page', {
        params: { query: { ...pageParams(params), Code: params.code, Name: params.name } },
      })
      .then((r) => toPage<SysDictType>(r)),
  typeAdd: (body: DictTypeInput) => client.POST('/api/v1/sys/dict/type', { body }).then((r) => unwrap<number>(r)),
  typeUpdate: (id: number, body: DictTypeInput) =>
    client.PUT('/api/v1/sys/dict/type/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  typeRemove: (id: number) =>
    client.DELETE('/api/v1/sys/dict/type/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** 批量删除字典类型(各自级联删项)。 */
  typeBatchRemove: (ids: number[]) => client.POST('/api/v1/sys/dict/type/batch-delete', { body: { ids } }).then((r) => unwrap<boolean>(r)),
  /** 某类型下的字典项(管理端:含停用、带 id;非下拉缓存源)。ponytail: 拉一页 500,字典项数量小,真超再分页。 */
  items: (typeCode: string) =>
    client
      .GET('/api/v1/sys/dict/item/page', { params: { query: { TypeCode: typeCode, Current: 1, Size: 500 } } })
      .then((r) => unwrap<PagedList<SysDictItem>>(r))
      .then((p) => p.items),
  itemAdd: (body: DictItemInput) => client.POST('/api/v1/sys/dict/item', { body }).then((r) => unwrap<number>(r)),
  itemUpdate: (id: number, body: DictItemInput) =>
    client.PUT('/api/v1/sys/dict/item/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  itemRemove: (id: number) =>
    client.DELETE('/api/v1/sys/dict/item/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** 批量删除字典项。 */
  itemBatchRemove: (ids: number[]) => client.POST('/api/v1/sys/dict/item/batch-delete', { body: { ids } }).then((r) => unwrap<boolean>(r)),
}

export const fileApi = {
  /** 文件分页;搜索键 originalName → 后端 FileName 模糊过滤。 */
  page: (params: { page: number; pageSize: number; originalName?: string }) =>
    client
      .GET('/api/v1/sys/file/page', {
        params: { query: { ...pageParams(params), FileName: params.originalName } },
      })
      .then((r) => toPage<SysFile>(r)),
  /** 上传单个文件:bodySerializer 建 FormData(字段名 file),openapi-fetch 对 FormData 不注入 json header,浏览器自动补 boundary(client.ts 无需改)。 */
  upload: (file: File) =>
    client
      .POST('/api/v1/sys/file/upload', {
        body: { file: file as unknown as string },
        bodySerializer: (body) => {
          const fd = new FormData()
          fd.append('file', (body as unknown as { file: File }).file)
          return fd
        },
      })
      .then((r) => unwrap<FileUploadOutput>(r)),
  /** 下载:parseAs blob 取原始字节(非信封,不套 unwrap);Bearer 由 client 拦截器自动带。 */
  download: (id: number) =>
    client.GET('/api/v1/sys/file/{id}/download', { params: { path: { id } }, parseAs: 'blob' }).then((r) => {
      if (!r.response.ok) throw new ApiError(r.response.status, undefined, undefined, r.response.statusText)
      return r.data as Blob
    }),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/file/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** 批量软删除文件记录(物理文件保留)。 */
  batchRemove: (ids: number[]) => client.POST('/api/v1/sys/file/batch-delete', { body: { ids } }).then((r) => unwrap<boolean>(r)),
  // ── 分片 / 断点续传上传 ──
  /** 分片初始化:秒传探测 + 已收分片(断点续传)。 */
  chunkInit: (body: { fileHash: string; fileName: string; totalSize: number; chunkCount: number }) =>
    client.POST('/api/v1/sys/file/chunk/init', { body }).then((r) => unwrap<ChunkInitOutput>(r)),
  /** 上传单个分片(multipart:uploadId + index + chunk),同 upload 用 bodySerializer 建 FormData。 */
  chunkUpload: (uploadId: string, index: number, chunk: Blob) =>
    client
      .POST('/api/v1/sys/file/chunk', {
        body: { uploadId, index, chunk: chunk as unknown as string },
        bodySerializer: (body) => {
          const b = body as unknown as { uploadId: string; index: number; chunk: Blob }
          const fd = new FormData()
          fd.append('uploadId', b.uploadId)
          fd.append('index', String(b.index))
          fd.append('chunk', b.chunk)
          return fd
        },
      })
      .then((r) => unwrap<boolean>(r)),
  /** 完成:触发服务端合并 + 哈希校验 + 三道关 + 落库。 */
  chunkComplete: (body: { uploadId: string; fileHash: string; fileName: string; chunkCount: number }) =>
    client.POST('/api/v1/sys/file/chunk/complete', { body }).then((r) => unwrap<FileUploadOutput>(r)),
}

export const sessionApi = {
  /** 在线会话分页(只读)。后端仅支持按 UserId 过滤,这里不带业务搜索。 */
  online: (params: { page: number; pageSize: number }) =>
    client
      .GET('/api/v1/sys/session/online', { params: { query: pageParams(params) } })
      .then((r) => toPage<OnlineSessionItem>(r)),
  /** 强制下线:按 sessionId 踢会话。 */
  kick: (sessionId: string) =>
    client.DELETE('/api/v1/sys/session/{sessionId}', { params: { path: { sessionId } } }).then((r) => unwrap<boolean>(r)),
}

export const noticeApi = {
  // ── 管理端 ──
  /** 管理端分页(全量);搜索键 title/type → PascalCase 查询参。 */
  page: (params: { page: number; pageSize: number; title?: string; type?: number }) =>
    client
      .GET('/api/v1/sys/notice/page', {
        params: { query: { ...pageParams(params), Title: params.title, Type: params.type } },
      })
      .then((r) => toPage<SysNotice>(r)),
  publish: (body: NoticePublishInput) => client.POST('/api/v1/sys/notice', { body }).then((r) => unwrap<number>(r)),
  remove: (id: number) => client.DELETE('/api/v1/sys/notice/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  // ── 用户端(任何登录用户) ──
  /** 我的通知分页(含已读标记);onlyUnread 供"未读"页签用。 */
  mine: (params: { page: number; pageSize: number; onlyUnread?: boolean }) =>
    client
      .GET('/api/v1/sys/notice/mine', { params: { query: { ...pageParams(params), OnlyUnread: params.onlyUnread } } })
      .then((r) => toPage<NoticeMineItem>(r)),
  /** 当前用户未读通知数(顶栏角标轮询)。 */
  unreadCount: () => client.GET('/api/v1/sys/notice/unread-count', {}).then((r) => unwrap<number>(r)),
  markRead: (id: number) => client.PUT('/api/v1/sys/notice/{id}/read', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  markAllRead: () => client.PUT('/api/v1/sys/notice/read-all', {}).then((r) => unwrap<boolean>(r)),
}

export const menuApi = {
  tree: () => client.GET('/api/v1/sys/menu/tree', {}).then((r) => unwrap<MenuTreeNode[]>(r)),
  /** 可授权路由清单(后端 EndpointDataSource 实时算出,含消费方自建控制器);喂菜单表单「权限码」下拉,免手敲。 */
  routes: () => client.GET('/api/v1/sys/menu/routes', {}).then((r) => unwrap<PermissionRouteItem[]>(r)),
  add: (body: MenuInput) => client.POST('/api/v1/sys/menu/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: MenuInput) =>
    client.PUT('/api/v1/sys/menu/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/menu/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}

// ── 定时任务(G8;路由前缀 /api/v1/sys/job,13 端点)──────────────

export const jobApi = {
  /** 任务分页;行含全列(编辑表单直接用行数据)。搜索键 name/status/handlerKind → PascalCase 查询参。 */
  page: (params: { page: number; pageSize: number; name?: string; status?: number; handlerKind?: number; sortField?: string; sortOrder?: string }) =>
    client
      .GET('/api/v1/sys/job/page', {
        params: {
          query: {
            ...pageParams(params),
            Name: params.name,
            Status: params.status,
            HandlerKind: params.handlerKind,
            SortField: params.sortField,
            SortOrder: params.sortOrder,
          },
        },
      })
      .then((r) => toPage<SysJob>(r)),
  add: (body: JobInput) => client.POST('/api/v1/sys/job', { body }).then((r) => unwrap<number>(r)),
  /** 更新任务(code 创建后不可变,服务层忽略;触发配置变更即重算下次执行时刻)。 */
  update: (id: number, body: JobInput) =>
    client.PUT('/api/v1/sys/job/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  /** 删除任务(软删,可回收站恢复;内置任务 47014 拒删)。 */
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/job/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** 批量删除(命中内置任务则整批拒绝)。 */
  batchRemove: (ids: number[]) => client.POST('/api/v1/sys/job/batch-delete', { body: { ids } }).then((r) => unwrap<boolean>(r)),
  /** 启停(true=恢复调度并重算下次执行时刻,false=暂停);集群下最长 30 秒后生效。 */
  setEnabled: (id: number, enabled: boolean) =>
    client.PUT('/api/v1/sys/job/{id}/enabled', { params: { path: { id }, query: { enabled } } }).then((r) => unwrap<boolean>(r)),
  /** 执行一次(在收到请求的副本本机执行,不经选主、不影响调度节奏)。 */
  runOnce: (id: number) => client.POST('/api/v1/sys/job/{id}/run', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** cron 预览(归一化 + 未来若干次);任何登录用户可用。POST 而非 GET:cron 含 `? #`,走 query 有转义坑。 */
  previewCron: (body: { cron: string; count?: number; from?: string | null }) =>
    client.POST('/api/v1/sys/job/preview-cron', { body }).then((r) => unwrap<CronPreviewOutput>(r)),
  /** 已注册的编译处理器全名清单(表单下拉数据源,免手打 handlerName)。 */
  handlers: () => client.GET('/api/v1/sys/job/handlers', {}).then((r) => unwrap<JobHandlersOutput>(r)),
  /**
   * 执行记录分页。startTime 是 dateRange 列回传的 [开始, 结束] 日期串 → 后端 StartFrom/StartTo;
   * 结束日拼 23:59:59(与 splitRange 同理,免"筛到今天"漏掉今天)。
   */
  logPage: (params: { page: number; pageSize: number; jobId?: number; runStatus?: number; startTime?: [string, string] | null; sortField?: string; sortOrder?: string; fireInstanceId?: number }) =>
    client
      .GET('/api/v1/sys/job/log/page', {
        params: {
          query: {
            ...pageParams(params),
            JobId: params.jobId,
            RunStatus: params.runStatus,
            FireInstanceId: params.fireInstanceId,
            StartFrom: params.startTime?.[0] || undefined,
            StartTo: params.startTime?.[1] ? `${params.startTime[1]} 23:59:59` : undefined,
            SortField: params.sortField,
            SortOrder: params.sortOrder,
          },
        },
      })
      .then((r) => toPage<SysJobLog>(r)),
  /** 终止一次执行(跨节点:写终止旗标,目标节点最迟 KillPollSeconds 后停)。 */
  killLog: (id: number) =>
    client.POST('/api/v1/sys/job/log/{id}/kill', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** 清空执行记录(硬删;运行中的记录一律保留)。返回删除行数。 */
  clearLogs: (body: { beforeDays?: number | null; jobId?: number | null }) =>
    client.POST('/api/v1/sys/job/log/clear', { body }).then((r) => unwrap<number>(r)),
  /** 监控仪表盘(今日成败/在飞/状态分布/近 14 日趋势/即将执行/集群节点)。 */
  dashboard: () => client.GET('/api/v1/sys/job/dashboard', {}).then((r) => unwrap<JobDashboard>(r)),
}

// ── 回收站 ────────────────────────────────────────────────────────

export type RecycleBinItem = Omit<components['schemas']['RecycleBinItem'], 'id' | 'deletedBy'> & {
  id: number
  deletedBy: number | null
}

export const recycleApi = {
  page: (type: string) => (params: { page: number; pageSize: number }) =>
    client.GET('/api/v1/sys/recycle/{type}/page', { params: { path: { type }, query: pageParams(params) } })
      .then((r) => toPage<RecycleBinItem>(r)),
  restore: (type: string, id: number) =>
    client.POST('/api/v1/sys/recycle/{type}/{id}/restore', { params: { path: { type, id } } }).then((r) => unwrap<boolean>(r)),
  purge: (type: string, id: number) =>
    client.DELETE('/api/v1/sys/recycle/{type}/{id}', { params: { path: { type, id } } }).then((r) => unwrap<boolean>(r)),
}
