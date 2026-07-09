import { client } from './client'
import type { LoginOutput, ModuleInput, ModuleRow, MyModulesOutput, PagedList, UserItem, UserProfile } from '@/types/api'
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
  const env = (data ?? {}) as Envelope
  if (typeof env.code === 'number' && env.code !== 0) {
    throw new ApiError(env.code, env.msgKey, env.args, env.message)
  }
  return env.data as T
}

export const authApi = {
  login: (body: { account: string; password: string; captchaId?: string; captchaCode?: string }) =>
    client.POST('/api/v1/auth/login', { body }).then((r) => unwrap<LoginOutput>(r)),
  logout: () => client.POST('/api/v1/auth/logout', {}).then((r) => unwrap<boolean>(r)),
}

export const personalApi = {
  modules: () => client.GET('/api/v1/personal/modules', {}).then((r) => unwrap<MyModulesOutput>(r)),
  menu: (moduleId: number) =>
    client.GET('/api/v1/personal/menu', { params: { query: { moduleId } } }).then((r) => unwrap<MenuNode[]>(r)),
  setDefaultModule: (moduleId: number) =>
    client.PUT('/api/v1/personal/default-module', { body: { moduleId } }).then((r) => unwrap<boolean>(r)),
  profile: () => client.GET('/api/v1/personal/profile', {}).then((r) => unwrap<UserProfile>(r)),
  updateProfile: (body: { name: string }) =>
    client.PUT('/api/v1/personal/profile', { body }).then((r) => unwrap<boolean>(r)),
  updatePassword: (body: { oldPassword: string; newPassword: string }) =>
    client.PUT('/api/v1/personal/password', { body }).then((r) => unwrap<boolean>(r)),
}

export const userApi = {
  /** 归一后端 PagedList<UserItem>({current,size,total,items}) → useTable 期望的 {items,total}。 */
  page: (params: { page: number; pageSize: number; account?: string; name?: string }) =>
    client
      .GET('/api/v1/sys/user/page', {
        // 查询参数名沿用后端 record 属性(PascalCase);ASP.NET 绑定大小写不敏感,类型要求 PascalCase。
        params: { query: { Current: params.page, Size: params.pageSize, Account: params.account, Name: params.name } },
      })
      .then((r) => unwrap<PagedList<UserItem>>(r))
      .then((p) => ({ items: p.items, total: p.total })),
}

export const moduleApi = {
  list: () => client.GET('/api/v1/sys/module/list', {}).then((r) => unwrap<ModuleRow[]>(r)),
  add: (body: ModuleInput) => client.POST('/api/v1/sys/module/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: ModuleInput) =>
    client.PUT('/api/v1/sys/module/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/module/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}

export const menuApi = {
  tree: () => client.GET('/api/v1/sys/menu/tree', {}).then((r) => unwrap<MenuTreeNode[]>(r)),
  add: (body: MenuInput) => client.POST('/api/v1/sys/menu/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: MenuInput) =>
    client.PUT('/api/v1/sys/menu/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/menu/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}
