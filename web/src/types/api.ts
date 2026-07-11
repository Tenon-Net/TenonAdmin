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
  /** 是否需强制改密(管理员建号/重置后首登为 true)。 */
  mustChangePassword: boolean
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
  orgName?: string | null
  positionName?: string | null
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

/** 字典类型行(后端 SysDictType;Code 创建后不可改)。int64 收敛为 number。 */
export interface SysDictType {
  id: number
  code: string
  name: string
  sort: number
  enabled: boolean
  remark?: string | null
  createTime?: string
}

/** 字典项行(后端 SysDictItem;管理端分页含停用项,带 id——区别于下拉投影 DictItem)。int64 收敛为 number。 */
export interface SysDictItem {
  id: number
  dictTypeCode: string
  label: string
  value: string
  sort: number
  enabled: boolean
  createTime?: string
}

/** 字典类型新增/编辑入参(后端 DictTypeInput;code 创建后不可改,更新时服务端忽略)。 */
export interface DictTypeInput {
  code: string
  name: string
  sort: number
  enabled: boolean
  remark?: string | null
}

/** 字典项新增/编辑入参(后端 DictItemInput;dictTypeCode = 当前类型,表单隐藏)。 */
export interface DictItemInput {
  dictTypeCode: string
  label: string
  value: string
  sort: number
  enabled: boolean
}

/** 文件记录行(后端 SysFile;列表返回,int64 收敛为 number)。 */
export interface SysFile {
  id: number
  originalName: string
  storagePath: string
  extension: string
  contentType?: string | null
  sizeBytes: number
  createTime?: string
}

/** 上传出参(后端 FileUploadOutput)。 */
export interface FileUploadOutput {
  id: number
  originalName: string
  storagePath: string
  sizeBytes: number
}

/** 分片上传初始化出参(后端 ChunkInitOutput)。 */
export interface ChunkInitOutput {
  /** 秒传命中(同内容哈希已存在,无需再传)。 */
  uploaded: boolean
  /** 秒传命中时的既有文件。 */
  file?: FileUploadOutput | null
  /** 上传会话 Id(= 文件哈希);非秒传时返回。 */
  uploadId?: string | null
  /** 服务端已收到的分片下标(断点续传:跳过已传)。 */
  receivedIndexes: number[]
}

/** 在线会话行(后端 OnlineSessionItem;只读 + 强退按 sessionId)。int64 收敛为 number。 */
export interface OnlineSessionItem {
  sessionId: string
  userId: number
  account: string
  ip?: string | null
  userAgent?: string | null
  loginTime?: string
  expiresAt?: string
}

/** 登录日志行(后端 SysLoginLog;只读,int64 收敛为 number)。 */
export interface SysLoginLog {
  id: number
  account: string
  success: boolean
  resultCode: number
  userId?: number | null
  name?: string | null
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
  exceptionMessage?: string | null
  elapsedMs: number
  operatorId?: number | null
  operatorName?: string | null
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
  category?: string | null
  sort: number
  enabled: boolean
  createTime?: string
}

/** 机构新增/编辑入参(后端 OrgInput;parentId 0=根,code 编辑禁用)。 */
export interface OrgInput {
  parentId: number
  name: string
  code: string
  category?: string | null
  sort: number
  enabled: boolean
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

/** 职位新增/编辑入参(后端 PositionInput;增改同一份字段,code 编辑禁用)。 */
export interface PositionInput {
  name: string
  code: string
  sort: number
  enabled: boolean
}

/** 角色行(后端 SysRole)。int64 收敛为 number。 */
export interface SysRole {
  id: number
  name: string
  code: string
  sort: number
  enabled: boolean
  remark?: string | null
  createTime?: string
}

/** 角色新增/编辑入参(后端 RoleInput;增改同一份字段,code 编辑禁用)。 */
export interface RoleInput {
  name: string
  code: string
  sort: number
  enabled: boolean
  remark?: string | null
}

/** 数据范围类型(后端 DataScopeType;存库为 int,枚举值须与后端一致)。 */
export enum DataScopeType {
  All = 1,
  Org = 2,
  OrgAndChildren = 3,
  Self = 4,
  Custom = 5,
}

/** 角色数据范围配置(后端 SysRoleDataScope;数据范围抽屉回显)。customOrgIds 逗号分隔。 */
export interface SysRoleDataScope {
  id: number
  roleId: number
  scopeType: DataScopeType
  customOrgIds: string
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

/** 通知类型(后端 NoticeType;存库 int,枚举值须与后端一致)。 */
export enum NoticeType {
  Notice = 1,
  Announcement = 2,
}

/** 通知行(后端 SysNotice;管理端列表全字段,int64 收敛为 number)。 */
export interface SysNotice {
  id: number
  title: string
  content?: string | null
  type: NoticeType
  createTime?: string
}

/** 我的通知项(后端 NoticeMineItem;含当前用户已读标记)。 */
export interface NoticeMineItem {
  id: number
  title: string
  content?: string | null
  type: NoticeType
  publishTime: string
  isRead: boolean
}

/** 发布通知入参(后端 NoticePublishInput)。 */
export interface NoticePublishInput {
  title: string
  content?: string | null
  type: NoticeType
}
