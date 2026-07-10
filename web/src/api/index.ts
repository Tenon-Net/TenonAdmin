import { client } from './client'
import type { AddUserInput, ConfigInput, DictItem, DictItemInput, DictTypeInput, FileUploadOutput, LoginOutput, ModuleInput, ModuleRow, MyModulesOutput, OnlineSessionItem, PagedList, PositionInput, SysConfig, SysDictItem, SysDictType, SysFile, SysLoginLog, SysOpLog, SysOrg, SysPosition, UpdateUserInput, UserDetail, UserItem, UserProfile } from '@/types/api'
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
  /** 归一后端 PagedList<UserItem>({current,size,total,items}) → ProTable fetcher 契约的 {items,total}。 */
  page: (params: { page: number; pageSize: number; account?: string; name?: string }) =>
    client
      .GET('/api/v1/sys/user/page', {
        // 查询参数名沿用后端 record 属性(PascalCase);ASP.NET 绑定大小写不敏感,类型要求 PascalCase。
        params: { query: { Current: params.page, Size: params.pageSize, Account: params.account, Name: params.name } },
      })
      .then((r) => unwrap<PagedList<UserItem>>(r))
      .then((p) => ({ items: p.items, total: p.total })),
  /** 用户详情(含 roleIds,编辑回显 + 提交时原样带回避免清空角色)。 */
  detail: (id: number) => client.GET('/api/v1/sys/user/{id}', { params: { path: { id } } }).then((r) => unwrap<UserDetail>(r)),
  add: (body: AddUserInput) => client.POST('/api/v1/sys/user', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: UpdateUserInput) =>
    client.PUT('/api/v1/sys/user/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) => client.DELETE('/api/v1/sys/user/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
  /** 重置密码;返回实际生效的初始密码(newPassword 留空 = 后端默认初始密码)。 */
  resetPassword: (id: number, newPassword?: string | null) =>
    client.PUT('/api/v1/sys/user/{id}/password', { params: { path: { id } }, body: { newPassword: newPassword || null } }).then((r) => unwrap<string>(r)),
  /** 专用启停端点(非全量 update)。 */
  setEnabled: (id: number, enabled: boolean) =>
    client.PUT('/api/v1/sys/user/{id}/enabled', { params: { path: { id } }, body: { enabled } }).then((r) => unwrap<boolean>(r)),
}

export const orgApi = {
  /** 全部机构(平铺,按 Sort、Id 排序)。前端 buildTree 拼树。R4 下拉 / R9 树表复用。 */
  list: () => client.GET('/api/v1/sys/org/list', {}).then((r) => unwrap<SysOrg[]>(r)),
}

export const positionApi = {
  /** 职位分页;搜索键 name → PascalCase Name。R4 下拉(拉大页)/ R6 ProTable 复用。 */
  page: (params: { page: number; pageSize: number; name?: string }) =>
    client
      .GET('/api/v1/sys/position/page', {
        params: { query: { Current: params.page, Size: params.pageSize, Name: params.name } },
      })
      .then((r) => unwrap<PagedList<SysPosition>>(r))
      .then((p) => ({ items: p.items, total: p.total })),
  add: (body: PositionInput) => client.POST('/api/v1/sys/position/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: PositionInput) =>
    client.PUT('/api/v1/sys/position/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/position/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
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
  page: (params: { page: number; pageSize: number; configKey?: string; name?: string; groupCode?: string }) =>
    client
      .GET('/api/v1/sys/config/page', {
        params: { query: { Current: params.page, Size: params.pageSize, ConfigKey: params.configKey, Name: params.name, GroupCode: params.groupCode } },
      })
      .then((r) => unwrap<PagedList<SysConfig>>(r))
      .then((p) => ({ items: p.items, total: p.total })),
  add: (body: ConfigInput) => client.POST('/api/v1/sys/config', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: ConfigInput) =>
    client.PUT('/api/v1/sys/config/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/config/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}

export const logApi = {
  /** 登录日志分页;搜索键 account/success → PascalCase Account/Success 查询参。 */
  loginPage: (params: { page: number; pageSize: number; account?: string; success?: boolean }) =>
    client
      .GET('/api/v1/sys/log/login/page', {
        params: { query: { Current: params.page, Size: params.pageSize, Account: params.account, Success: params.success } },
      })
      .then((r) => unwrap<PagedList<SysLoginLog>>(r))
      .then((p) => ({ items: p.items, total: p.total })),
  /** 清空登录日志(硬删,不可恢复)。 */
  loginClear: () => client.DELETE('/api/v1/sys/log/login', {}).then((r) => unwrap<boolean>(r)),
  /** 操作日志分页;搜索键 title/success → PascalCase Title/Success 查询参。 */
  opPage: (params: { page: number; pageSize: number; title?: string; success?: boolean }) =>
    client
      .GET('/api/v1/sys/log/op/page', {
        params: { query: { Current: params.page, Size: params.pageSize, Title: params.title, Success: params.success } },
      })
      .then((r) => unwrap<PagedList<SysOpLog>>(r))
      .then((p) => ({ items: p.items, total: p.total })),
  /** 清空操作日志(硬删,不可恢复)。 */
  opClear: () => client.DELETE('/api/v1/sys/log/op', {}).then((r) => unwrap<boolean>(r)),
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
        params: { query: { Current: params.page, Size: params.pageSize, Code: params.code, Name: params.name } },
      })
      .then((r) => unwrap<PagedList<SysDictType>>(r))
      .then((p) => ({ items: p.items, total: p.total })),
  typeAdd: (body: DictTypeInput) => client.POST('/api/v1/sys/dict/type', { body }).then((r) => unwrap<number>(r)),
  typeUpdate: (id: number, body: DictTypeInput) =>
    client.PUT('/api/v1/sys/dict/type/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  typeRemove: (id: number) =>
    client.DELETE('/api/v1/sys/dict/type/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
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
}

export const fileApi = {
  /** 文件分页;搜索键 originalName → 后端 FileName 模糊过滤。 */
  page: (params: { page: number; pageSize: number; originalName?: string }) =>
    client
      .GET('/api/v1/sys/file/page', {
        params: { query: { Current: params.page, Size: params.pageSize, FileName: params.originalName } },
      })
      .then((r) => unwrap<PagedList<SysFile>>(r))
      .then((p) => ({ items: p.items, total: p.total })),
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
}

export const sessionApi = {
  /** 在线会话分页(只读)。后端仅支持按 UserId 过滤,这里不带业务搜索。 */
  online: (params: { page: number; pageSize: number }) =>
    client
      .GET('/api/v1/sys/session/online', { params: { query: { Current: params.page, Size: params.pageSize } } })
      .then((r) => unwrap<PagedList<OnlineSessionItem>>(r))
      .then((p) => ({ items: p.items, total: p.total })),
  /** 强制下线:按 sessionId 踢会话。 */
  kick: (sessionId: string) =>
    client.DELETE('/api/v1/sys/session/{sessionId}', { params: { path: { sessionId } } }).then((r) => unwrap<boolean>(r)),
}

export const menuApi = {
  tree: () => client.GET('/api/v1/sys/menu/tree', {}).then((r) => unwrap<MenuTreeNode[]>(r)),
  add: (body: MenuInput) => client.POST('/api/v1/sys/menu/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: MenuInput) =>
    client.PUT('/api/v1/sys/menu/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/menu/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}
