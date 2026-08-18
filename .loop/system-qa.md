# Goal: 全面测试 TenonAdmin（后端 + Vue + React），修小 bug，存疑写入 docs/qa/findings.md
Done-condition: 全部 Tasks [x] 且 sweep: clear 且 docs/qa/findings.md 存在
round: 20 / max: 40
sweep: clear

## Tasks
- [x] 环境与基线：确认 backend + web + web-react 能起；建 docs/qa/findings.md 骨架；抽跑现有测试看是否已红
- [x] 认证核心：登录/登出/会话/强退/验证码/锁定
- [x] MFA + 密码策略/历史/过期/重置
- [x] 外部登录 + 短信登录
- [x] RBAC：权限码=路由、角色授权、超管旁路、RolePermission/ActiveSession
- [x] 数据范围 / 机构隔离 / CreateOrgId
- [x] 用户 / 机构 / 岗位 CRUD（后端 + 双前端）
- [x] 角色 / 菜单 / 应用模块门户
- [x] 字典 / 配置中心
- [x] 文件上传 / 预览安全 / 分片 / GC
- [x] 导入导出
- [x] 定时任务
- [x] 日志 / 回收站 / 监控 / 缓存
- [x] 通知 / 个人中心 / 实时
- [x] 限流 / 生产启动护栏 / 密钥保护 / WorkerId
- [x] 可替换性六件套 + CodeFirst 可空升级 + 多库
- [x] Vue 模板 UI 探测（登录皮肤、动态路由、v-auth、i18n）
- [x] React 模板 UI 探测（对等面、Can、zustand 选择器）
- [x] 双模板行为对等抽查
- [x] 终轮 P0/P1 清扫：再扫 findings + 高危面；本轮 0 个新 P0/P1 才勾选

## Round log
### Round 1 — 环境与基线 → 后端已起（:5100，/health 与 /ready curl 均 Healthy，OpenAPI 200）；本机 :5173 是另一仓 VitePress 不是 Vue 管理端；:5174 未起（本轮启动前端 dev server 被工具策略拒绝）。抽测：backend 20/20 绿；web vitest 90/90 绿；web-react 805 绿 / 2 红（LoginPage 品牌用例）。修了 `web/vite.config.ts` 补 `strictPort: true`。记 QA01（P1 测试红）、QA02（P2 已修）。NEXT: 认证核心：登录/登出/会话/强退/验证码/锁定
### Round 2 — 认证核心 → 后端测试 AuthFlow+LoginLock+Captcha+Session*+CookieCsrf 41 绿，AuthorizationTests 3 绿（含强退即 40006）。活探测：captcha 200；错密 40001 信封；无令牌 logout/session 401+40006；垃圾 refresh 40007。失败锁定只计 PasswordWrong；验证码一次性取删。双前端 login/logout/kick API 对齐。无新 P0/P1。更新 QA01：品牌用例单独跑 3 绿，全量红更像跨文件污染。未改业务代码。NEXT: MFA + 密码策略/历史/过期/重置
### Round 3 — MFA + 密码策略 → 后端相关测试 53 绿；修后 MfaEnrollmentTests 13 仍绿。活探测：TOTP 关时 bind/start→41001；匿名 challenge/verify→40019；password-policy 未登录→40006。双前端 mustChangePassword 守卫对齐。修 `MfaEnrollmentService` 陪跑哈希缓存（QA05）。记 QA03（匿名 verify 端点）、QA04（自助改密不踢其它会话）。NEXT: 外部登录 + 短信登录
### Round 4 — 外部登录 + 短信登录 → 相关后端测试 61 绿。活探测：providers 200；sms 关时 send/login→40012；垃圾 exchange→40014。pending-link 有 binder cookie；双前端 callback 都转发 pendingLink/provider/displayName 并清残留会话。未改代码。记 QA06（短信验码 40010/40011 可枚举是否有真用户）。NEXT: RBAC：权限码=路由、角色授权、超管旁路、RolePermission/ActiveSession
### Round 5 — RBAC → PermissionCodeConsistency/Authorization/RoleCrud/PermissionRoutes/UserRoleFilter/ModuleProtection 等 19 绿。未登录 ping/permissions → 40006。超管 sadm 绕过 + 接口永不建/改 IsSuperAdmin；改角色会 Invalidate 权限缓存。双前端 hasPerm 同规则（超管 fail-open / 未加载 fail-closed）；踢会话码 `{sessionid}` 与种子/PermissionCode.Build 小写一致。无新 finding，未改代码。NEXT: 数据范围 / 机构隔离 / CreateOrgId
### Round 6 — 数据范围 → DataScope+SampleDoc+ImportExportScope+OrgCycle+Authorization+PermissionRoutes 16 绿。过滤器/写守卫/CreateOrgId AOP/HTTP TryAdd 顺序均正确。修 `[ActiveSession]` 漏绑范围（QA07）：抽 `DataScopeRequestBinder`，TestHost `GET /sample/doc/mine` 锁定。记 QA08（用户/机构/岗位不走范围，设计如此）、QA09（配角色范围不校验调用者）。双前端角色页都有 datascope 抽屉。NEXT: 用户 / 机构 / 岗位 CRUD（后端 + 双前端）
### Round 7 — 用户/机构/岗位 CRUD → User/Org/Position/Copy/Sort/Profile/RoleFilter 30 绿。超管护栏（删/批删/停用）在；账号唯一含软删；用户写走 RequireReauth（TOTP 开才生效）；双前端 update 全量回传 + 行内启停走专用 setEnabled + 超管行禁删停 + 岗位编辑禁改 code。记 QA10（删机构/岗位不查挂靠用户；可删停自己）。用户树筛是精确 OrgId 不是子孙，与后端一致。未改代码。NEXT: 角色 / 菜单 / 应用模块门户
### Round 8 — 角色/菜单/门户 → Role/Menu/Module/Portal/Protection 27 绿（含新用例）。菜单成环/有子拒删、ModuleId 仅顶级、内置 system 不可删停、有菜单拒删模块、门户代际缓存、默认应用拒无权。修停用角色后门户仍露模块（QA11）：`ResolveEnabledRoleIdsAsync` 对齐权限提供者。双前端角色启停走全量 update、授权菜单三态/按模块过滤对齐。NEXT: 字典 / 配置中心
### Round 9 — 字典/配置 → Dict+ConfigCenter 11 绿（含新用例）。site 匿名白名单、batch 忽略未知键并失效缓存、写操作 RequireReauth+审计。修停用类型后下拉仍有项（QA14）。记 QA12（items 热路径要字典权限、表单静默空）、QA13（种子类型/配置可删、项值不唯一、job 进「其他」）。双前端配置 Tab 对齐（含 HighSens 嵌在安全）。NEXT: 文件上传 / 预览安全 / 分片 / GC
### Round 10 — 文件上传/预览/分片/GC → FileViewSecurity/Url/Chunk/Gc/Storage/UploadConfig/Root 28 绿。后缀白名单不信 Content-Type；view 按后缀权威 MIME+nosniff，svg/html 强制另存；路径重写+存储根围栏；秒传独立行+GC 共享判定；分片哈希校验/弃单 TTL。双前端 FileUpload 都支持 chunked。未改代码。记 QA15（文件管理不按上传人隔离）、QA16（直链无过期）。NEXT: 导入导出
### Round 11 — 导入导出 → ImportExport+Scope+ExcelCodec 24 绿（含 2 条新用例）。导出不进信封、Commit 不信前端 Errors、三种重复策略、部分提交、DemoMode 挡 commit、范围外机构拒插、三账号导出行集不同，均已有测试。修 QA17：Validate/Commit/错误报告补 `MaxImportRows`。修 QA18：覆盖且 RoleNames 空时回填原角色。双前端 ImportWizard 四步+默认 Skip、ExportColumnsModal 对齐。记 QA19（赋任意角色名/覆盖按账号全库命中/按名取第一条）。NEXT: 定时任务
### Round 12 — 定时任务 → Job* 全套 92 绿；Api+Security 复跑 67 绿（含新用例）。HTTP 围栏/CRLF/密钥掩码/Compiled 冒充内置/选主 CAS/SerialSkip/Drain 已钉。修 QA20：SQL 总闸关后存量任务可改非 sql 字段。双前端 sqlEnabled 禁选+handlers 下发对齐。记 QA21（DisabledModules≠停调度；IsSystem 可改 Handler/Props）。NEXT: 日志 / 回收站 / 监控 / 缓存
### Round 13 — 日志/回收站/监控/缓存 → LogQuery+Exception+OpCoverage+Recycle+Monitor+CacheAdmin+CacheInvalidation 18 绿。操作日志按人/时/路径筛、离职姓名快照、异常只记非业务、清空硬删、回收唯一冲突 42021、监控形状、缓存定向 flush 均过。修 QA22：双前端回收站补 `job` 页签+i18n。记 QA23（软删级联清关联，恢复后需重配权）。NEXT: 通知 / 个人中心 / 实时
### Round 14 — 通知/个人中心/实时 → Notice+Personal+Session+Realtime 19 绿（Notice 复跑 5，含新用例）。定向可见性、已读/未读、会话自助踢、Hub 开关映射、吊销推 force-logout、发布推 notice-changed 均过。修 QA24：MarkRead 必须 VisibleToMe。记 QA25（Hub 不验 sid；空定向幽灵通知；头像任意 URL）。自助改密不踢会话仍见 QA04。NEXT: 限流 / 生产启动护栏 / 密钥保护 / WorkerId
### Round 15 — 限流/护栏/密钥/WorkerId → 相关套件 38 绿（修后）。限流 429+40008、跨副本共享计数、生产缺表/缺列 fail-fast、JWT 生产必配、Redis 须显式 WorkerId、SecretProtector 往返均过。修 QA26：预检样本落盘改用系统临时目录。记 QA27（固定窗口 2× 突发；显式同号 WorkerId 仍可撞）。NEXT: 可替换性六件套 + CodeFirst 可空升级 + 多库
### Round 16 — 可替换性/可空升级/多库 → Replaceability+CodeFirstNullable+MultiConfigId+WorkerSetup **43** 绿。IRepository 始终主库、副库无种子、可空列靠 InitTables、生产缺列 fail-fast 均过。修 QA28：Worker 实体扫描补挂 Services 程序集。记 QA29（多数 Replace 不锁 TryAdd；副库 DbType 默认 Sqlite）。NEXT: Vue 模板 UI 探测（登录皮肤、动态路由、v-auth、i18n）
### Round 17 — Vue UI → vitest **90/90** 绿。三皮肤 registry+回落、F5 enterInitial 重建路由、v-auth 超管 fail-open/未加载 fail-closed、recycle.tabs.job 中英均在、server.strictPort 仍在。修 QA30：preview 补 strictPort。记 QA31（profile 失败超管按钮全藏）、QA32（皮肤中文硬编码；v-auth 仅 mounted remove）。无新 P0/P1。NEXT: React 模板 UI 探测（对等面、Can、zustand 选择器）
### Round 18 — React UI → 全量 vitest **807/807** 绿（含 LoginPage 31）。Can/zustand/F5 enterInitial/recycle.tabs.job 对齐。修 QA01：mount 钉桩+waitFor site；修 QA33：preview.strictPort。记 QA34（皮肤中文硬编码）。无新开放 P0/P1。NEXT: 双模板行为对等抽查
### Round 19 — 双模板对等 → 权限/门户 F5/皮肤+strictPort/mustChangePassword/recycle+job/Import Skip/datascope+setEnabled/hub force-logout 九面均对齐，无一侧缺功能。更新 QA31 表面为双模板；记 QA35（v-auth vs Can 重渲差）。无新 P0/P1。NEXT: 终轮 P0/P1 清扫：再扫 findings + 高危面；本轮 0 个新 P0/P1 才勾选
### Round 20 — 终轮 P0/P1 清扫 → 开放项仍准：QA06 open；QA09/12/15 question（产品拍板，本轮不改）。回归：ActiveSession 绑范围、MarkRead 可见性、Import 行上限+空 RoleNames、Worker 扫 Services、停用角色门户过滤均仍在。针对性后端测试绿。**0 个新 P0/P1** → 勾选终轮，`sweep: clear`。DONE.
