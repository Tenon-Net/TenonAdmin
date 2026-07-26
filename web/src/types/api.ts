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
  /** 机构名称(未分配/已删则 null) */
  orgName?: string | null
  /** 职位名称(未分配/已删则 null) */
  positionName?: string | null
  nickname?: string | null
  phone?: string | null
  email?: string | null
  /** 性别:字典 gender 的项 Value("1"男/"2"女/"0"未知) */
  gender?: string | null
  /** 头像(文件签名直链 ViewUrl,直接进 img) */
  avatar?: string | null
  isSuperAdmin: boolean
}

/** 我的会话项(后端 MySessionItem;个人视角,isCurrent 标记本次请求所用会话)。 */
export interface MySessionItem {
  sessionId: string
  ip?: string | null
  userAgent?: string | null
  loginTime: string
  expiresAt: string
  isCurrent: boolean
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
  nickname?: string | null
  phone?: string | null
  email?: string | null
  gender?: string | null
  avatar?: string | null
  orgId?: number | null
  positionId?: number | null
  directorId?: number | null
  orgName?: string | null
  positionName?: string | null
  directorName?: string | null
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
  /** 后端路由匹配前缀(如 sys/biz):菜单配按钮时权限路由下拉据此按应用软过滤;留空=不过滤。 */
  apiPrefix?: string | null
  sort: number
  enabled: boolean
  remark?: string | null
  createTime?: string
}

/** 工作台首页统计(后端 DashboardSummaryOutput)。趋势三条数组等长(近 7 日,含今天,无登录的日子为 0)。 */
export interface DashboardSummary {
  roles: number
  users: number
  perms: number
  onlineSessions: number
  trendDays: string[]
  trendLogins: number[]
  trendActiveUsers: number[]
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
  apiPrefix?: string | null
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
  /** 存储层相对路径。**别当 URL 用**:后端默认不静态托管上传目录,当 src 必然坏链。要展示图片用 viewUrl。 */
  storagePath: string
  sizeBytes: number
  /** 签名直链(匿名可取、不可伪造),可直接塞进 `<img src>`;后端签发,前端不自己拼。 */
  viewUrl?: string | null
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

/** 磁盘分区容量(后端 DiskInfo)。 */
export interface DiskInfo {
  name: string
  totalBytes: number
  freeBytes: number
}

/** 服务器运行快照(后端 ServerInfoOutput;进程/主机基础指标)。 */
export interface ServerInfoOutput {
  machineName: string
  osDescription: string
  frameworkDescription: string
  processArchitecture: string
  processorCount: number
  processUptimeSeconds: number
  processCpuPercent: number
  processWorkingSetBytes: number
  gcHeapBytes: number
  totalAvailableMemoryBytes: number
  threadCount: number
  disks: DiskInfo[]
}

/** 异常日志行(后端 SysExceptionLog;未捕获异常留痕,只读,分页项已含全字段)。 */
export interface SysExceptionLog {
  id: number
  httpMethod: string
  path: string
  traceId?: string | null
  exceptionType: string
  message?: string | null
  stackTrace?: string | null
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

/** 新增用户入参(后端 AddUserInput;account 建后不可改,password 留空=系统生成随机强口令)。 */
export interface AddUserInput {
  account: string
  password?: string | null
  name: string
  nickname?: string | null
  phone?: string | null
  email?: string | null
  gender?: string | null
  avatar?: string | null
  orgId?: number | null
  positionId?: number | null
  directorId?: number | null
  enabled: boolean
  roleIds: number[]
}

/** 新增用户出参(后端 AddUserOutput):新 Id + 实际生效的初始口令明文——留空口令时是随机生成的,不展示给管理员就没人知道。 */
export interface AddUserOutput {
  id: number
  initialPassword: string
}

/** 一条可授权路由(后端 PermissionRouteItem);喂菜单表单权限码下拉,code 即写进 SysMenu.Permission 的值。 */
export interface PermissionRouteItem {
  code: string
  method: string
  path: string
}

/** 更新用户入参(后端 UpdateUserInput;无 account/password。roleIds 由 detail 原样带回避免清空)。 */
export interface UpdateUserInput {
  name: string
  nickname?: string | null
  phone?: string | null
  email?: string | null
  gender?: string | null
  avatar?: string | null
  orgId?: number | null
  positionId?: number | null
  directorId?: number | null
  enabled: boolean
  roleIds: number[]
}

/** 用户详情(后端 UserDetail;列表字段 + roleIds,编辑回显用)。 */
export interface UserDetail {
  id: number
  account: string
  name: string
  nickname?: string | null
  phone?: string | null
  email?: string | null
  gender?: string | null
  avatar?: string | null
  orgId?: number | null
  positionId?: number | null
  directorId?: number | null
  enabled: boolean
  isSuperAdmin: boolean
  roleIds: number[]
  createTime?: string
}

/** 通知类型(后端 NoticeType;存库 int,枚举值须与后端一致)。 */
export enum NoticeType {
  Notice = 1,
  Announcement = 2,
  Message = 3,
}

/** 接收范围(后端 ReceiverType;All=0 对全体广播,Role/User 定向)。 */
export enum ReceiverType {
  All = 0,
  Role = 1,
  User = 2,
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
  /** 接收范围;缺省 All 广播全体。 */
  receiverType: ReceiverType
  /** 接收目标 Id(角色 Id 或用户 Id,取决于 receiverType);All 时忽略。 */
  receiverIds?: number[]
}

// ── 导入 / 导出(excel-ledger §4.3;与后端 Core DTO 对齐) ──

/** 导入列声明。 */
export interface ImportColumn {
  key: string
  title: string
  required?: boolean
  dictTypeCode?: string | null
  hint?: string | null
  width?: number
}

/** 导出列声明(前端选列弹窗用;DefaultSelected 决定默认勾选)。 */
export interface ExportColumnDef {
  key: string
  title: string
  defaultSelected?: boolean
}

/** 单元格级错误:只带码,文案由前端 translateError(code) 渲染(设计 §13.2)。 */
export interface CellError {
  columnKey: string
  code: number
  args?: Record<string, unknown> | null
}

/** 一行导入数据;单元格一律字符串。 */
export interface ImportRow {
  index: number
  cells: Record<string, string | null | undefined>
  errors: CellError[]
}

/** 预览 / 重验结果。 */
export interface ImportPreview {
  headers: string[]
  /** 表头文本 → 列 Key */
  mapping: Record<string, string>
  columns: ImportColumn[]
  rows: ImportRow[]
  total: number
  errorRows: number
  columnErrors: CellError[]
}

/** 重复处理策略(与后端 DuplicateStrategy 一致)。 */
export enum DuplicateStrategy {
  Skip = 0,
  Overwrite = 1,
  Error = 2,
}

/** 提交结果。 */
export interface ImportCommitResult {
  total: number
  inserted: number
  updated: number
  skipped: number
  failed: number
  failures: ImportRow[]
}
