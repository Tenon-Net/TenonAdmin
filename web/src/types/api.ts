// 接口出参领域类型(与后端 DTO 对齐)。API 层用它标注 unwrap<T> 的返回,视图直接消费。
import type { AppModule } from './menu'

/** 登录/刷新出参(后端 LoginOutput)。 */
export interface LoginOutput {
  accessToken: string
  expiresAt: string
  refreshToken: string
  refreshExpiresAt: string
  userId: number
  account: string
  name: string
}

/** 我的应用列表(后端 MyModulesOutput)。 */
export interface MyModulesOutput {
  modules: AppModule[]
  defaultModuleId?: number | null
}

/** 个人资料(后端 UserProfile)。 */
export interface UserProfile {
  id: number
  account: string
  name: string
  orgId?: number | null
  positionId?: number | null
  isSuperAdmin: boolean
}

/** 后端统一分页结果 PagedList<T>。 */
export interface PagedList<T> {
  current: number
  size: number
  total: number
  pages: number
  items: T[]
}

/** 用户列表项(后端 UserItem;刻意不含密码)。 */
export interface UserItem {
  id: number
  account: string
  name: string
  orgId?: number | null
  positionId?: number | null
  enabled: boolean
  isSuperAdmin: boolean
  createTime: string
}

/** 模块/应用管理行(后端 SysModule;列表全字段)。 */
export interface ModuleRow {
  id: number
  code: string
  title: string
  icon?: string | null
  defaultRoute?: string | null
  sort: number
  enabled: boolean
  remark?: string | null
  createTime?: string
}

/** 字典项消费投影(后端 SysDictItem;sort 归一为 number,甩掉 int64 序列化的 number|string 噪音)。 */
export interface DictItem {
  label: string
  value: string
  sort: number
  enabled: boolean
}

/** 模块新增/编辑入参(后端 ModuleInput)。 */
export interface ModuleInput {
  code: string
  title: string
  icon?: string | null
  defaultRoute?: string | null
  sort: number
  enabled: boolean
  remark?: string | null
}

/** 系统配置行(后端 SysConfig;列表返回全字段,int64 收敛为 number)。 */
export interface SysConfig {
  id: number
  configKey: string
  configValue?: string | null
  name: string
  groupCode?: string | null
  sort: number
  remark?: string | null
  createTime?: string
}

/** 配置新增/编辑入参(后端 ConfigInput;configKey 创建后不可改)。 */
export interface ConfigInput {
  configKey: string
  configValue?: string | null
  name: string
  groupCode?: string | null
  sort: number
  remark?: string | null
}

/** 登录日志行(后端 SysLoginLog;只读,int64 收敛为 number)。 */
export interface SysLoginLog {
  id: number
  account: string
  success: boolean
  resultCode: number
  userId?: number | null
  ip?: string | null
  userAgent?: string | null
  createTime: string
}

/** 操作日志行(后端 SysOpLog;只读,分页项已含全字段,详情抽屉直接用行数据)。 */
export interface SysOpLog {
  id: number
  title: string
  httpMethod: string
  path: string
  paramJson?: string | null
  resultCode: number
  success: boolean
  elapsedMs: number
  operatorId?: number | null
  ip?: string | null
  userAgent?: string | null
  createTime: string
}

/** 机构行(后端 SysOrg;平铺,前端 buildTree)。int64 收敛为 number。 */
export interface SysOrg {
  id: number
  parentId: number
  name: string
  code: string
  sort: number
  enabled: boolean
  createTime?: string
}

/** 职位行(后端 SysPosition)。int64 收敛为 number。 */
export interface SysPosition {
  id: number
  name: string
  code: string
  sort: number
  enabled: boolean
  createTime?: string
}

/** 新增用户入参(后端 AddUserInput;account 建后不可改,password 留空=后端默认初始密码)。 */
export interface AddUserInput {
  account: string
  password?: string | null
  name: string
  orgId?: number | null
  positionId?: number | null
  enabled: boolean
  roleIds: number[]
}

/** 更新用户入参(后端 UpdateUserInput;无 account/password。roleIds 由 detail 原样带回避免清空)。 */
export interface UpdateUserInput {
  name: string
  orgId?: number | null
  positionId?: number | null
  enabled: boolean
  roleIds: number[]
}

/** 用户详情(后端 UserDetail;列表字段 + roleIds,编辑回显用)。 */
export interface UserDetail {
  id: number
  account: string
  name: string
  orgId?: number | null
  positionId?: number | null
  enabled: boolean
  isSuperAdmin: boolean
  roleIds: number[]
  createTime?: string
}
