# 内置 API 功能与健壮性测试补齐记录

关联实现：`c9d1e2a0f6406918305d354a73c8f145222da806`（`test(api): lock auth and error contracts for built-in endpoints`）。

整理基线：`dev 89121ec54457bf95b395049a3d9d231c627405ae`，2026-10-22。本次核对确认 29 个控制器任务全部完成，记录引用的 63 个测试类均有对应定义；部分测试类共用 `AdminSurfaceAuthTests.cs` 或 `ApiAuthGapTests.cs`。

下文“新测试”、退出码与轮次日志均为历史执行记录。本次整理只核对清单完整性和测试类引用，未重新执行产品测试。

## 控制器清单
- [x] AuthController — 见结论
- [x] CacheController — 见结论
- [x] ConfigController — 见结论
- [x] DashboardController — 见结论
- [x] DictController — 见结论
- [x] ExternalAuthController — 见结论
- [x] JobController — 见结论
- [x] MenuController — 见结论
- [x] MfaController — 见结论
- [x] ModuleController — 见结论
- [x] MonitorController — 见结论
- [x] NoticeController — 见结论
- [x] OrgController — 见结论
- [x] PersonalController — 见结论
- [x] PingController — 见结论
- [x] PositionController — 见结论
- [x] RecycleBinController — 见结论
- [x] SecurityController — 见结论
- [x] SysFileController — 见结论
- [x] SysLogController — 见结论
- [x] SysRoleController — 见结论
- [x] SysSessionController — 见结论
- [x] UserController — 见结论
- [x] WfCcController — 见结论
- [x] WfDefinitionController — 见结论
- [x] WfDelegationController — 见结论
- [x] WfInstanceController — 见结论
- [x] WfOutboxController — 见结论
- [x] WfTaskController — 见结论
- [x] 核对：rg 无未完成项，且 29 个控制器都有测试结论

## 结论

### AuthController
- 新测试：`AuthFlowTests`（空账号/空密码与密码错误同为 40001；匿名登出 401 + 40006）、`AuthTotpLoginTests`（未绑定强制 TOTP 登录 40020；已绑定 40018；错码不消费挑战；对码出票；重放 40019；空挑战/未知挑战 40019）。
- 已有：`AuthFlowTests` 成功登录 / 40001 / 刷新 / 五次失败锁定；`MfaLoginFlowTests` 的 `login/sms` 与 `login/sms/resend`；`SmsLoginFlowTests` 的 `sms/send` 与 `sms/login`；`CookieSessionCsrfTests` 的 refresh/logout CSRF；`RateLimitTests` 打到 `GET /api/v1/auth/captcha`。
- `dotnet test` 退出码 0（与本轮其它类一起，28 通过）。

### CacheController
- 新测试：`CacheEndpointTests`。未登录 401 + 40006；仅有 ping 权限 403 + 41001（四条路由）；`DisabledModules` 含 Cache 时 404；超管 `flush-config` / `flush-auth` / `flush-dict` 清掉哨兵且返回数 > 0；`rebuild-portal` 代际加 1；操作日志标题为「清缓存-配置」。
- 已有：`CacheAdminTests` 只覆盖服务层 `FlushConfigAsync`，没有 HTTP。
- 退出码 0。

### DashboardController
- 新测试：`DashboardTests` 增加匿名 401 + 40006、仅登录用户可读（`[ActiveSession]`，趋势 7 天）、关闭 Dashboard 模块 404。
- 已有：`Summary_returns_real_counts_and_seven_day_trend`。
- 退出码 0。

### MonitorController
- 新测试：`MonitorTests` 增加匿名 401 + 40006、无监控权限 403 + 41001、关闭 Monitor 模块 404。
- 已有：`Server_info_returns_sane_snapshot`。
- 退出码 0。

### PingController
- 无缺口。唯一路由 `GET /api/v1/ping`。已有：`AuthorizationTests`（授码 200、未授码 403 + 41001、强退后 401 + 40006）、`ModulePortalTests`、`CacheInvalidationTests`。
- 本轮未新增测试。

### SecurityController
- 新测试：`SecurityEndpointAuthTests`。`/api/v1/sys/security/baseline` 与 `/api/v1/sys/level3/precheck` 匿名 401 + 40006、无权限 403 + 41001、关闭 Security 模块 404。
- 已有：`SecurityBaselinePrecheckTests.Baseline_api_returns_structured_precheck_for_super_admin`。
- 退出码 0。

未改产品代码。未发现需记为产品缺陷的失败。

### ConfigController
- 新测试：`ConfigRobustnessTests`。退出码 0。
- 已有：`ConfigCenterTests`、`PasswordPolicyEndpointTests`、`ConfigAuditLogTests`。

### DictController
- 新测试：`DictRobustnessTests`。退出码 0。
- 已有：`DictCrudTests`。`type/page` 在测试宿主上与 `CustomDictController` 冲突，见 `ReplaceabilityTests`。

### ExternalAuthController
- 新测试：`ExternalAuthRobustnessTests`。退出码 0。
- 已有：`ExternalAuthTests`。

### JobController
- 新测试：`JobAuthTests`。退出码 0。
- 已有：`JobApiTests`。

### MenuController
- 新测试：`MenuAuthTests`。退出码 0。
- 已有：`MenuCrudTests`。

### MfaController
- 新测试：`MfaAdminAuthTests`。退出码 0。
- 已有：`MfaEnrollmentTests`、`MfaLoginFlowTests`。

### ModuleController
- 新测试：`ModuleAuthTests`。退出码 0。
- 已有：`ModuleCrudTests`。

### NoticeController
- 新测试：`NoticeAuthTests`。退出码 0。
- 已有：`NoticeTests`。

### OrgController
- 新测试：`OrgAuthTests`。退出码 0。
- 已有：`OrgCrudTests`。

### PersonalController
- 新测试：`PersonalAuthTests`。退出码 0。
- 已有：`PersonalCenterTests`、`PersonalSessionTests`。

### PositionController
- 新测试：`PositionAuthTests`。退出码 0。
- 已有：`PositionCrudTests`。

### RecycleBinController
- 新测试：`RecycleBinAuthTests`。退出码 0。
- 已有：`RecycleBinTests`。

### SysFileController
- 新测试：`FileAuthTests`。退出码 0。
- 已有：`FileOwnerTests`、`FileViewSecurityTests`、`ChunkUploadTests`、`ReplaceabilityTests`（关闭 Upload）。

### SysLogController
- 新测试：`LogAuthTests`。退出码 0。
- 已有：`LogQueryTests`、`ExceptionLogTests`。

### SysRoleController
- 新测试：`RoleAuthTests`。退出码 0。
- 已有：`RoleCrudTests`。

### SysSessionController
- 新测试：`SessionAuthTests`。退出码 0。
- 已有：`SessionEndpointTests`。

### UserController
- 新测试：`UserAuthTests`。退出码 0。
- 已有：`UserCrudTests`、`DeleteGuardTests`、`ImportExportTests`。

### WfCcController / WfInstanceController / WfTaskController / WfDefinitionController / WfDelegationController / WfOutboxController
- 新测试：`WorkflowSurfaceAuthTests`。退出码 0。
- 已有：`WfCcTests`、`LeaveWorkflowE2ETests`、`WfDelegationTests`、`WfOutboxTests` 以及其余 `Wf*` 回归。本轮只补登录与权限边界，没有重写办理成功路径。

## 轮次记录
### Round 1 — AuthController → 补空凭证、匿名登出、TOTP HTTP 契约，`AuthFlowTests` + `AuthTotpLoginTests` 通过. NEXT: CacheController
### Round 2 — CacheController → 新增 `CacheEndpointTests`，四条 flush 的鉴权、关闭模块、清哨兵与操作日志通过. NEXT: DashboardController
### Round 3 — DashboardController → `DashboardTests` 补匿名、普通登录用户、关闭模块. NEXT: MonitorController
### Round 4 — MonitorController → `MonitorTests` 补匿名、无权限、关闭模块. NEXT: PingController
### Round 5 — PingController → 无缺口，依据 `AuthorizationTests` / `ModulePortalTests` / `CacheInvalidationTests`. NEXT: SecurityController
### Round 6 — SecurityController → 新增 `SecurityEndpointAuthTests`，两条路径鉴权与关闭模块通过. NEXT: ConfigController
### Round 7 — ConfigController → `ConfigRobustnessTests`：匿名 401、受限用户 403 但可读密码策略、不存在 43004、重复键 43005、缺键 data 为 null、关闭模块连 site 一起 404。退出码 0. NEXT: DictController
### Round 8 — DictController → `DictRobustnessTests`：默认禁用时 type/1 与 items 为 404；启用后匿名 401、仅登录可读 items、重复编码 43002、缺项 43003。`type/page` 与 TestHost 自定义控制器冲突，未拿它做鉴权断言。退出码 0. NEXT: ExternalAuthController
### Round 9 — ExternalAuthController → `ExternalAuthRobustnessTests`：`providers/all` 要权限，`bindings` 只要登录。功能路径仍在 `ExternalAuthTests`。退出码 0. NEXT: JobController
### Round 10 — JobController → `JobAuthTests`：`page` 要权限，`preview-cron` 只要登录。其余管理路由同为 `[RolePermission]`，成功/失败路径在 `JobApiTests`。退出码 0. NEXT: MenuController
### Round 11 — MenuController → `MenuAuthTests`：`tree` 与 `routes` 匿名 401、无权限 403。增删改与不存在见 `MenuCrudTests`。退出码 0. NEXT: MfaController
### Round 12 — MfaController → `MfaAdminAuthTests`：高敏列表、清除、reauth 未登录 401；高敏与清除无权限 403。绑定成功路径在 `MfaEnrollmentTests`。退出码 0. NEXT: ModuleController
### Round 13 — ModuleController → `ModuleAuthTests`：列表要权限，缺 Id 为 42011。内置保护与重复编码在 `ModuleCrudTests`。无 `[Module]`，不能按模块关闭。退出码 0. NEXT: NoticeController
### Round 14 — NoticeController → `NoticeAuthTests`：管理列表要权限，未读数只要登录，缺 Id 删除 45001，关闭模块 404。收件人隔离在 `NoticeTests`。退出码 0. NEXT: OrgController
### Round 15 — OrgController → `OrgAuthTests`：列表要权限，复制不存在机构 42003。增删改与子机构保护在 `OrgCrudTests`。退出码 0. NEXT: PersonalController
### Round 16 — PersonalController → `PersonalAuthTests`：匿名资料 401，登录可读，撤销不存在会话 42024。改密与踢自己其它会话在 `PersonalCenterTests` / `PersonalSessionTests`。退出码 0. NEXT: PositionController
### Round 17 — PositionController → `PositionAuthTests`：分页要权限，缺 Id 的改/删为 42005，仍有用户占用时删除 42028 且记录仍在。重复编码在 `PositionCrudTests`。退出码 0. NEXT: RecycleBinController
### Round 18 — RecycleBinController → `RecycleBinAuthTests`：列表要权限，恢复/彻底删除不存在行 42020。回环与非法 type 在 `RecycleBinTests`。退出码 0. NEXT: SysFileController
### Round 19 — SysFileController → `FileAuthTests`：分页要权限；无签名的匿名 view 直接 403，不进统一信封。归属与内容类型在 `FileOwnerTests` / `FileViewSecurityTests`。关闭 Upload 已有 `ReplaceabilityTests`。退出码 0. NEXT: SysLogController
### Round 20 — SysLogController → `LogAuthTests`：三类分页未登录 401，操作日志无权限 403，清空登录日志后列表为空，关闭 Log 模块 404。查询与异常清空在 `LogQueryTests` / `ExceptionLogTests`。退出码 0. NEXT: SysRoleController
### Round 21 — SysRoleController → `RoleAuthTests`：分页要权限，缺 Id 为 42002。增删改与重复编码在 `RoleCrudTests`。退出码 0. NEXT: SysSessionController
### Round 22 — SysSessionController → `SessionAuthTests`：在线列表要权限。强退与不存在会话的幂等成功在 `SessionEndpointTests`。退出码 0. NEXT: UserController
### Round 23 — UserController → `UserAuthTests`：分页要权限；导入模板只要登录，测试宿主未装 Excel 时为 46001 而不是 403；缺 Id 为 42001。CRUD、自操作与超管保护在 `UserCrudTests` / `DeleteGuardTests`。退出码 0. NEXT: WfCcController
### Round 24–29 — 六个工作流控制器 → `WorkflowSurfaceAuthTests`（同一轮命令，退出码 0）。匿名全部 401。仅登录用户可读抄送、可发起列表、待办/已办；监控、定义、委托、发件箱、加签/减签/拿回为 403。功能路径在既有 `Wf*` 测试。NEXT: 核对
### Round 30 — 核对 → 29 个控制器任务均已勾选并写有结论。`rg -n "^- \[ \]" .loop/api-robustness.md` 应无输出。
