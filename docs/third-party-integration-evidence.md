# 第三方接入模块：验收证据记录

本文按[实施计划](third-party-integration-goals.md)逐项记录完成证据：目标编号、实际文件、验收项与对应测试/演示、执行命令与结果、未解决限制。实现契约见[实现契约](third-party-integration-implementation.md)。只记录真实执行过的检查；未执行或缺环境的项目明确标注。

## G01 — 模块落点与实现契约

- 日期：2026-09-27
- 实际文件：`docs/third-party-integration-implementation.md`（新增）、本文（新增）。未生成任何接口、数据库表或页面。
- 核对方式：CodeGraph `codegraph_explore` 查询认证注册（`AddTenonAdmin`/`MapTenonAdmin`）、`RolePermissionAttribute`/`ActiveSessionAttribute`/`DataScopeRequestBinder`、`IDataScopeContext`/`ICurrentUser`/`SqlSugarSetup`，其余模块（`OperationLogFilter`、`RateLimitMiddleware`、`JobHttpFence`/`HttpAdminJob`、`WfOutbox*`、`WorkflowSetup`、`TestDb`/`WorkflowAppFactory`、`TenonAdminMiddlewareStartupFilter`、`SqlSugarRepository`、`Microsoft.AspNetCore.OpenApi` 10.0.9 公共类型）直接阅读源码。
- 验收项：
  - 决策有文件/符号依据：实现契约 §1 表格列出全部依据；以 `grep -rF` 逐一核对 38 个被引用符号在 `backend/src` 中存在（全部命中，命令见下）。
  - 后续任务映射到具体模块：实现契约 §14。
  - 无新增产品范围或未处理冲突：身份、事务、状态、多副本、限流与保留默认值均落在设计文档与 ADR 0008 的已确认范围内；唯一内核改动（操作日志豁免标记，§10）为通用扩展点，不改变现有审计默认行为。
- 执行命令：`for s in ...; do grep -rF "$s" backend/src --include=*.cs | grep -v /obj/ | wc -l; done`（38 项均 ≥1）。
- 限制：`db.Ado.IsAnyTran()` 在 `SqlSugarScope` 事务内的行为、`IOpenApiDocumentProvider` 的键控注册方式以代码证据为准，分别在 G08、G04 用测试确认。

## G02 — 接入应用与凭据生命周期

- 日期：2026-09-27
- 实际文件：
  - 新增卫星包 `backend/src/TenonAdmin.Integration/`：`TenonAdmin.Integration.csproj`、`IntegrationSetup.cs`；`Abstractions/`（`IntegrationOptions`、`IntegrationErrorCode`、`OpenAppIdentity`/`OpenAppClaimTypes`、`IOpenAppCredentialValidator`、`IOpenAppKeyGenerator`）；`Entities/IntegrationApp.cs`、`IntegrationAppCredential.cs`；`OpenApi/OpenAppApiKey.cs`、`OpenAppKeyGenerator.cs`、`OpenAppCredentialValidator.cs`；`Apps/`（模型、`IntegrationAppService`、`OpenAppCredentialService`、`IntegrationAppState`）；`Controllers/IntegrationAppController.cs`。
  - 测试：`backend/tests/TenonAdmin.IntegrationTestHost/`（新增）、`tests/TenonAdmin.Tests/IntegrationAppFactory.cs`、`IntegrationTestSupport.cs`、`IntegrationCredentialLifecycleTests.cs`、`IntegrationAdminApiTests.cs`、`IntegrationReplaceabilityTests.cs`（新增）；`TestDb.cs`（`integration` 模板类型）、`ApiAuthGapTests.cs`（`AuthProbe` 重载）、`TenonAdmin.Tests.csproj`、`TenonAdmin.slnx`（登记新工程）。
  - 文档：实现契约 §3/§4.1 与实际落点同步（不存秘密片段，`Code` 列宽 96）。
- 验收项与测试：
  - 缺失/格式错误/不存在/秘密不符/撤销/过期/应用停用/删除全部无效：`Default_validator_rejects_missing_malformed_unknown_wrong_revoked_disabled_and_deleted`、`Expiry_boundary_is_closed_at_the_expiry_instant`。
  - 轮换窗口与到期边界：`Rotation_keeps_old_and_new_valid_inside_window_then_old_expires_at_boundary`（窗口内新旧并存、窗口结束旧凭据恰好失效、并存窗口 0、已更早到期不被延长）、`Default_lifetime_explicit_expiry_rules_and_expiry_adjustment`、`Active_credential_limit_blocks_create_and_allows_one_extra_only_during_rotation`。
  - 两个宿主一致：`Two_hosts_sharing_one_database_agree_on_revocation_disable_and_enable`（两个 WebApplicationFactory 共享同一库、不同 WorkerId）。
  - 无凭据泄露：`Secret_never_appears_in_responses_database_operation_log_or_logs`（查询响应、`itg_app_credential`、`sys_op_log`、Trace 级全部日志均无秘密；发放响应 `Cache-Control: no-store`；`ToString` 不带秘密）。
  - 系统身份载体：`Identity_carrier_is_a_system_identity_without_user_claims`（无 `sub/sid/sadm`，内核 `HttpContextCurrentUser` 读出 `UserId=null`、非超管、`OrgId`=归属机构）。
  - 可替换：`IntegrationReplaceabilityTests`（校验服务、密钥生成、应用服务、凭据服务、选项前置注册胜出；非法配置启动即抛；内置服务方法 virtual）。
  - 管理接口使用既有权限体系并进入用户操作审计：`Admin_routes_require_login_and_route_permission`、`Full_lifecycle_over_http_is_audited_as_user_operations`。
  - 未启用不暴露端点、不建表：`Module_not_enabled_exposes_no_routes_and_creates_no_tables`。
  - 其他：`Last_used_is_written_at_most_once_per_interval`、`App_input_validation_code_uniqueness_and_owner_org`、`State_version_increments_on_runtime_relevant_changes_only`、`Key_format_is_strict_and_generator_output_round_trips`。
- 执行命令与结果：
  - `dotnet build backend/TenonAdmin.slnx -c Release` → 成功，0 警告。
  - `dotnet test ... --filter "FullyQualifiedName~Integration"`：SQLite 22/22；MySQL 8.0（模板模式）22/22；PostgreSQL 16（模板模式）22/22；SQL Server 2022（模板模式）22/22。本机容器凭据与 CI 相同。
  - 受影响既有测试 `ReplaceabilityTests|ApiAuthGapTests|AdminSurfaceAuthTests|WorkflowSurfaceAuthTests|SeedIdRangeTests|PermissionCodeConsistencyTests`（SQLite）57/57。
- 限制：本目标不开放任何业务端点；HTTP 认证拒绝行为在 G03 验收。

## G03 — 开放接口授权与业务数据隔离

- 日期：2026-09-27
- 实际文件：
  - 内核（唯一改动）：`backend/src/TenonAdmin.AspNetCore/Logging/ISkipOperationLogMetadata.cs`（新增标记接口）、`OperationLogFilter.ShouldLog`（识别该标记）。
  - 模块：`Abstractions/OpenApiAttributes.cs`（`[OpenApi]`、`[OpenApiDataScope]`、认证常量）、`IOpenApiDataScopePolicy.cs`（范围策略 SPI 与基类，默认拒绝内核机构维度）、`OpenAppDataScope.cs`、`IOpenAppContext.cs`（含端点清单契约）、`IOpenAppAuthorizationService.cs`、`IInboundLogService.cs`、错误码 49002–49004/49019–49021；`Entities/IntegrationAppGrant.cs`、`IntegrationAppScope.cs`、`IntegrationInboundLog.cs`；`OpenApi/OpenAppAuthenticationHandler.cs`、`OpenApiAuthorizationFilter.cs`、`OpenApiCallLogFilter.cs`、`OpenApiCallRecorder.cs`、`OpenApiCatalog.cs`、`OpenApiConvention.cs`、`OpenApiStartupValidator.cs`（含 `OpenApiConventionRules`）、`OpenApiRequestState.cs`、`HttpContextOpenAppContext.cs`、`OrgOpenApiDataScopePolicy.cs`、`OpenAppScopeQueryExtensions.cs`、`OpenApiDataScopePolicyRegistry.cs`、`InboundLogService.cs`；`Apps/OpenAppAuthorizationService.cs`；`Controllers/IntegrationCatalogController.cs`、`IntegrationAppController`（授权与范围接口）；`IntegrationSetup`（认证方案、约定、启动校验、防重复注册）。
  - 测试宿主（最小消费者）：`tests/TenonAdmin.IntegrationTestHost/Demo/`（`DemoTicket` 机构隔离实体、普通业务服务、自定义 `PartnerScopePolicy`、机构/合作方/无范围三组开放控制器，仅 DTO + 服务调用 + 范围声明）。
  - 测试：`IntegrationOpenApiTestKit.cs`、`IntegrationOpenApiAuthorizationTests.cs`（含 `IntegrationOpenApiStartupTests`）。
- 验收项与测试：
  - 应用 A 无法调用 B 专属接口、授权变更即时生效：`App_only_reaches_granted_endpoints_and_grant_changes_apply_immediately`。
  - 不能按 Id 读写范围外记录：`Org_scope_restricts_list_detail_update_delete_and_anchors_inserts_to_owner_org`（机构范围含下级；范围外 GET/PUT/DELETE 被拒且库不变；新增行 `CreateUserId=null`、`CreateOrgId`=归属机构、应用可读回；更新不冒充用户）、`Custom_partner_scope_filters_queries_and_rejects_out_of_scope_writes`（列表/详情过滤、越权写 49004、全量须显式绑定）。
  - 缺失范围策略拒绝、不退化为全量：`Missing_scope_binding_or_policy_is_rejected_and_never_widened`（未绑定 403 49003；空值、不存在机构、未知策略、保留键、重复键、未知权限码全部拒绝且不留半截）、`Startup_rules_reject_undeclared_misrouted_user_guarded_and_entity_returning_open_actions`、`Host_refuses_to_start_when_an_open_endpoint_is_misdeclared`（宿主级拒绝启动）。
  - 后台接口拒绝应用凭据、开放接口拒绝用户令牌：`App_credentials_cannot_reach_admin_routes_and_user_tokens_cannot_reach_open_routes`（专用头与 Bearer 两种出示方式；两者同时出示时各通道只认自己的凭据；缺失/无效/多值凭据统一 401）。
  - 审计与调用记录分离：`Open_calls_are_recorded_with_app_identity_and_kept_out_of_user_operation_log`（成功、未授予、未处理异常、秘密错误四类调用落库并带应用身份与追踪标识；标识不存在不落库；开放写调用不进 `sys_op_log`）、`Admin_authorization_changes_are_audited_and_catalog_lists_declared_endpoints`（授权/范围修改进用户操作审计；端点清单与范围候选值；后台文档不含开放端点）。
  - 多副本：`Authorization_changes_on_one_host_apply_to_another_host_on_next_request`。
- 执行命令与结果：
  - `dotnet build`（模块、测试宿主、测试工程）→ 成功，0 警告。
  - `--filter "FullyQualifiedName~Integration"`：SQLite 32/32；MySQL 31/31、PostgreSQL 31/31、SQL Server 31/31（宿主级启动用例加入前的 31 项全量；加入后 SQLite 复跑 32/32）。
  - 内核回归（SQLite）：`Session|DataScope|SoftDeleteAudit|OrgAuditEntity|OperationLog|Authorization|SampleDocScope|ImportExportScope|ReplaceabilityTests|HostEndpoints` 124/124；`PermissionRoutes|HostEndpoints|WorkflowSurfaceAuth|AdminSurfaceAuth|PermissionCodeConsistency` 11/11。
- 限制：自定义范围策略的查询与写入须使用 `IOpenAppContext.DataScope`（`WhereInScope`/`EnsureAllowed`），框架不识别任意 SQL 的业务归属（设计范围内的已知边界）。按应用限流、认证失败限流、调用记录查询与保留在 G04 完成。

## G04 — 开放 API 的标准与可观测性

- 日期：2026-09-27
- 实际文件：
  - 模块：`OpenApi/OpenApiDocumentSetup.cs`（每版本独立文档 `open-v{n}`、`ShouldInclude` 只收本分组、ApiKey 安全方案、通用约定说明、`X-Request-Id` 参数与 400/401/403/429/500 响应说明）、`OpenApiEnvelopeFilters.cs`（ProblemDetails → 49007 信封；未处理异常 → 500/50000 信封，`Order=int.MinValue` 保证内核异常留痕先执行）、`OpenApiPaging.cs`（`OpenApiPageInput` 上限 100、`ToOpenPagedListAsync`）、`CacheOpenAppRateLimiter.cs` + `Abstractions/IOpenAppRateLimiter.cs`（按应用每分钟限流、按来源 IP 认证失败限流）、`OpenAppAuthenticationHandler.cs`（查询串凭据拒绝、失败限流在查库前、429 挑战）、`OpenApiAuthorizationFilter.cs`（按应用限流）、`OpenApiCallRecorder.cs`、`InboundLogService.PageAsync` + `Controllers/IntegrationInboundLogController.cs`、`IntegrationCatalogController.OpenApiDocument`（生产受控下载，`IOpenApiDocumentProvider`）、`Abstractions/IIntegrationRetentionService.cs` + `Jobs/IntegrationRetentionService.cs`、`Jobs/IntegrationRetentionJob.cs`（含 `itg-retention` 种子）、`OpenApiStartupValidator`（开放端点必须返回 `Result<T>`）、选项 `OpenApi`/`Retention` 与错误码 49005/49006/49007/49022、`TenonAdmin.Integration.csproj`（登记 OpenApi 拦截器命名空间）。
  - 宿主：`backend/samples/MinimalHost`（启用模块）；测试宿主示例改用 `OpenApiPageInput` 与 `DateTimeOffset`。
  - 生成契约：`web/src/api/schema.d.ts`、`web-react/src/api/schema.d.ts`（`node scripts/check-contract-drift.mjs` 重新生成）。
  - 测试：`IntegrationOpenApiConventionTests.cs`；`IntegrationAppFactory` 增加环境名。
- 验收项与测试/演示：
  - 文档描述真实认证与输入输出、只含开放契约、字段白名单：`Open_document_describes_only_the_open_contract_with_real_auth_and_whitelisted_fields`（全部路径以 `/api/open/v1/` 开头；ApiKey 头方案；`TicketOpenDto` 恰为 6 个公开字段、`createdAt` 为 date-time；不含 internalNote/createOrgId/createUserId/isDelete/后台路径）。
  - 生产受控获取：`Production_serves_the_open_document_only_to_authorized_administrators`（生产不匿名映射 → 404；匿名 401、无权限 403、授权管理员下载 OpenAPI 3.1 JSON；未知版本 49022）。
  - 错误与分页一致：`Validation_errors_and_unhandled_exceptions_use_the_unified_envelope_with_trace`（400/49007 带字段错误；500/50000 带 traceId、不回显异常消息；内核异常表照常记录；调用记录区分原因）、`Paging_is_capped_normalized_and_keeps_the_kernel_shape_with_offset_times`。
  - 限流：`App_rate_limit_rejects_over_quota_with_retry_after_and_is_recorded`（固定时钟；超额 429/49005 + Retry-After；应用间独立；放宽即时生效；记录 `app_rate_limited`）、`Repeated_authentication_failures_from_one_source_are_throttled_before_credential_lookup`（第 4 次即便凭据正确也 429/49006，且校验服务未被调用；窗口滚动后恢复）。
  - 追踪与脱敏：`Trace_headers_are_present_on_every_outcome_and_unsafe_request_ids_are_dropped`、`Credentials_in_the_query_string_are_rejected_and_never_stored`。
  - 调用记录只向授权后台用户开放：`Call_log_query_is_limited_to_authorized_admins_and_filters_by_app_outcome_and_trace`；清理：`Retention_deletes_only_expired_call_logs_in_batches_and_the_job_is_seeded`（批大小 2，只删超期行，任务种子存在）。
  - 真实进程演示（非 TestServer）：`dotnet run` 启动 `TenonAdmin.IntegrationTestHost`，curl 依次完成管理员建应用 → 发凭据 → 未授权 403 → 授权并绑定范围 → 调用成功（响应带 `X-Trace-Id`、回显 `X-Request-Id`）→ 查询调用记录（含应用、路由、结果、原因、请求标识）→ 停用后 401/49001；开发环境 `/openapi/open-v1.json` 只含 7 个开放路径；宿主日志中凭据秘密出现 0 次。
- 执行命令与结果：
  - `dotnet build backend/TenonAdmin.slnx -c Release` → 成功，0 警告。
  - `--filter "FullyQualifiedName~Integration"`：SQLite 42/42、MySQL 42/42、PostgreSQL 42/42、SQL Server 42/42。
  - `node scripts/check-contract-drift.mjs`：退出码 1，原因是相对 HEAD 的预期契约变化（两套 `schema.d.ts` 各 +1238 行，仅新增 `/api/v1/integration/*` 管理接口与相关模型；两套差异逐行一致；显示的两行删除是插入对齐伪影，净变化为纯新增）。未手改生成文件。
  - `web` 与 `web-react` 的 `npm run typecheck`：均退出码 0。
- 限制：限流为一分钟固定窗口（窗口边界允许 2× 突发，与内核一致）；未装 Redis 时每副本独立计数。框架级请求日志若由宿主调到 Information 以上并记录 URL，不在本模块控制范围内——本模块拒绝并不落库查询串中的凭据。

## G05 — Vue 接入应用管理

- 日期：2026-09-27
- 实际文件：
  - 后端（支撑双前端）：`Seed/IntegrationMenuSeed.cs`（「系统」应用下「系统集成」目录：接入应用、开放调用记录两页 + 21 个按钮权限码）、`Controllers/OpenAppWhoAmIController.cs`（模块内置连通性检查 `GET /api/open/v1/whoami`，默认拒绝、须授予，范围 none）；测试 `Seeded_menu_buttons_match_every_admin_route_exactly`（种子按钮与管理路由双向一致）、`Built_in_whoami_is_default_deny_and_reports_only_public_identity_when_granted`，`Module_not_enabled_exposes_no_routes_and_creates_no_tables` 补充「无菜单、无任务」断言。
  - Vue：`web/src/types/integration.ts`、`web/src/api/integration.ts`、`web/src/views/integration/shared.ts`、`views/integration/app/index.vue` 与 `components/{AppFormModal,CredentialDrawer,SecretModal,GrantDrawer}.vue`、`views/integration/inbound-log/index.vue`、`locales/zh-CN.ts`/`en-US.ts`（`integration` 命名空间 + `error.code.49xxx`）、`e2e/integration-app.spec.ts`。
  - e2e 基础设施：`web/playwright.config.ts`、`web-react/playwright.config.ts` 给 e2e 宿主设置 `TenonAdmin__Security__RateLimit__Enabled=false`（见下方「限制与处理」）。
- 验收项与演示：
  - 真实后端交互闭环（`e2e/integration-app.spec.ts` 第 1 条，Playwright 自启 MinimalHost + Vite）：新增应用 → 重复编码显示本地化错误（接口失败）→ 凭据抽屉空状态 → 发放凭据（一次性弹窗读出完整凭据，断言 `localStorage`/`sessionStorage` 不含秘密）→ 未授权调用 `whoami` 403/49002 → 授权抽屉勾选端点保存 → 调用 200 且返回应用编码 → 「更多 → 调用记录」深链 `/integration/inbound-log?appId=` 显示「拒绝访问」「成功」两条 → 刷新后筛选保持 → 轮换（并存窗口 0）出现新秘密、旧凭据标记已过期、旧凭据 401、新凭据 200 → 行内开关停用并确认 → 新凭据 401/49001。
  - 无权限操作（第 2 条）：只授予「接入应用-分页」的角色用户看不到新增、下载文档、凭据、更多按钮，启停开关置灰。
  - 未启用模块不出现不可用菜单：后端 `Module_not_enabled_exposes_no_routes_and_creates_no_tables`（未启用时无「系统集成」菜单、无 `itg-*` 任务）。
  - 一次性凭据不持久化：`SecretModal.vue` 只存局部 ref、关闭即清空；e2e 断言浏览器存储不含秘密；代码无 console 输出。
  - 未引入 React 依赖：`web/package.json` 未改；Vue 代码不引用 `web-react/`。
- 执行命令与结果：
  - `npm run typecheck`（web）→ 通过；`npx oxlint` → 退出码 0；`npx vitest run` → 38 文件 206/206；`npm run build` → 成功（仅既有的 chunk 体积提示）。
  - `npx playwright test e2e/integration-app.spec.ts` → 2/2 通过；全量 `npx playwright test` → 18/18 通过。
  - 后端 `--filter "FullyQualifiedName~Integration"`（SQLite）→ 44/44。
- 限制与处理：首次全量 e2e 中 `workbench.spec.ts` 登录停在登录页；排除新用例的基线 16/16 通过；解开 trace 确认 `/api/v1/auth/login` 与 `/auth/external/providers` 返回 429——内核认证端点按 IP 每分钟 20 次，既有套件连续登录本就贴线，新增用例改变了时间分布后触发。按后端测试工厂同一做法在 e2e 宿主关闭限流（限流行为由后端 `RateLimit*` 与本模块限流测试覆盖），之后全量 18/18。

## G06 — React 接入应用管理

- 日期：2026-09-27
- 实际文件：
  - React：`web-react/src/types/integration.ts`、`web-react/src/api/integration.ts`、`web-react/src/views/integration/shared.ts`、`views/integration/app/index.tsx` 与 `components/{AppFormModal,CredentialDrawer,SecretModal,GrantDrawer}.tsx`、`views/integration/inbound-log/index.tsx`、`locales/zh-CN.ts`/`en-US.ts`（`integration` 命名空间 + `error.code.49xxx`，与 Vue 文案同义）、`scripts/icon-manifest.json`（+3 个菜单图标）与重新生成的 `src/assets/icons.generated.json`、`e2e/integration-app.spec.ts`。
  - 复用同一后端契约（G05 的菜单种子、`whoami` 端点、生成的 `schema.d.ts`），无后端改动。
- 验收项与演示：
  - 真实后端交互闭环（`e2e/integration-app.spec.ts` 第 1 条，Playwright 自启 MinimalHost + Vite）：与 Vue 同一业务、独立实现——新增 → 重复编码本地化错误且表单不关 → 凭据空状态 → 发放并一次性展示（断言浏览器存储不含秘密）→ 未授权 403/49002 → 授权保存 → 调用 200 → 深链调用记录（拒绝/成功两条）→ 刷新后筛选保持 → 轮换（并存窗口 0）旧凭据标记已过期、旧 401 新 200 → 停用确认后 401/49001。
  - 无权限操作（第 2 条，自备数据可单独运行）：只授予「接入应用-分页」的用户经侧栏菜单进入，看不到新增、下载文档、凭据、更多按钮，启停开关置灰。
  - Ant Design 6 API：`Drawer size`、`Space orientation`、`Alert title`、`Tag variant="filled"`；zustand 只用稳定选择器；按钮权限经 `Can`/`useHasPerm`。
  - 未引入 Vue 依赖、无跨模板引用：`web-react/package.json` 未改；`views/integration`、`api/integration.ts`、`types/integration.ts` 中无指向 `web/` 的导入。
- 执行命令与结果：
  - `npm run typecheck` → 退出码 0；`npm run lint`（oxlint）→ 退出码 0；`npm run build` → 成功（仅既有 chunk 体积提示）。
  - `npx vitest run` → 130 文件 967/967 通过。
  - `npx playwright test e2e/integration-app.spec.ts` → 连续 3 次 2/2（首轮调整前第 2 条单跑失败，见下）；全量 `npx playwright test` → 17/17。随后 Vue 全量再跑 18/18。
- 限制与处理：
  - 第 2 条最初依赖第 1 条产生的数据、且登录后立即硬跳转会打断应用壳装配动态路由；改为用例自建应用数据、按真实用户路径点侧栏菜单进入后稳定通过（Vue 用例同步修正）。
  - `WfConfigDrawer.spec.tsx`「Webhook 六个字段一起写回模型」在全量并行时偶发 5 秒超时：单跑约 2.2 秒；在 `git archive HEAD` 导出的干净副本上全量运行同样超时，属既有的负载相关用例，与本目标无关，未改动。

## G07 — 普通出站调用与第三方适配

- 日期：2026-09-27
- 实际文件：
  - 模块 `backend/src/TenonAdmin.Integration`：`Abstractions/IntegrationOutboundOptions.cs`（目标配置与启动校验，字面 IP 基础地址启动即判定）、`Abstractions/OutboundContracts.cs`（`OutboundOutcome` 含 `Cancelled`、`OutboundRequest/Response`、`OutboundClassification.Transient`、`IOutboundHttpInvoker`/`IOutboundTargetRegistry`/`IOutboundCredentialProvider`/`IOutboundAuthenticator`/`IOutboundResultClassifier`、`OutboundHeaders`）、`Abstractions/IOutboundLogService.cs`、`Entities/IntegrationOutboundLog.cs`、`Outbound/{OutboundAddressGuard,OutboundHttpClientFactory,OutboundTargetRegistry,ConfigurationOutboundCredentialProvider,ConfiguredOutboundAuthenticator,DefaultOutboundResultClassifier,OutboundHttpInvoker,OutboundLogService,OutboundAdapterBase,OutboundResponseExtensions}.cs`、`Controllers/IntegrationOutboundLogController.cs`（`page`、`targets`）、`Seed/IntegrationMenuSeed.cs`（「出站调用记录」页 + 2 个按钮）、`IntegrationErrorCode`（49030–49034）、`IntegrationOptions`（`Outbound`、`Retention:OutboundLogDays`）、`Jobs/IntegrationRetentionService.cs`（出站记录清理）、`IntegrationSetup.cs`（TryAdd 注册）。
  - 本地可控第三方 `backend/samples/IntegrationMockPartner/MockPartnerServer.cs`：新增 Bearer/自定义头/Basic/HMAC 签名四种凭据出示方式、`/api/status/{code}`、`/api/big`、`throttle`/`request-timeout`/`business-reject` 行为、按请求记录连接标识。
  - 测试宿主：`Demo/DemoPartnerClient.cs`（消费者适配器：HMAC 签名认证 + 识别「200 但业务拒绝」）。
  - 测试：`IntegrationOutboundGuardTests.cs`、`IntegrationOutboundTests.cs`（`MockPartnerFixture` 进程内 Kestrel 随机端口）、`IntegrationReplaceabilityTests.cs`（出站服务前置注册胜出、内置实现步骤 virtual）、`IntegrationAppFactory.SchemaNeutral`；测试工程引用 `IntegrationMockPartner`。
  - 前端：两套 `locales/zh-CN.ts`/`en-US.ts` 增加 `error.code.49030–49034`；`web-react/scripts/icon-manifest.json` 与重新生成的 `icons.generated.json`（出站记录菜单图标）；两套 `src/api/schema.d.ts` 由 `check-contract-drift.mjs` 重新生成。
  - 文档：`docs/third-party-integration-implementation.md` §3、§6、§11 按实现更新。
- 验收项与测试/演示（本地可控第三方为真实 Kestrel 进程内服务，走真实网络栈）：
  - 成功与凭据注入：`Credentials_are_injected_per_target_and_every_call_carries_its_call_id`（Bearer/自定义头/Basic 三种注入，对方收到的 `X-Request-Id` 等于调用 Id；缺秘密不发出、对方零请求；错误秘密 401→认证失败；未配置目标 `target_unknown`）；`Secret_changes_in_configuration_apply_to_the_next_call_without_restart`（运行中改配置秘密，下一次调用即生效）；`A_replaced_credential_source_is_used_without_touching_adapters`。
  - 超时、取消与错误映射：`Timeouts_and_caller_cancellation_are_reported_and_recorded_separately`（超时→结果未知 `timeout`，取消→`Cancelled`，记录分开）；`Remote_statuses_map_to_the_contract_outcomes`（17 个状态码逐一对表，429/503 带 `Retry-After` 且可重发）；`Failures_before_the_request_reaches_the_partner_are_not_sent_and_retryable`（连接拒绝、DNS 失败→未发出、可重发）；`Connect_timeouts_are_not_sent_rather_than_unknown`（积压队列已满的黑洞端口，建连超时→未发出）。
  - 不安全目标：`Unsafe_targets_are_refused_without_contacting_them`（未受信回环域名建连前拒绝；同一域名解析结果变为云元数据/未受信内网时写请求被拒且对方只执行一次；302 指向元数据不跟随、按拒绝；路径 `../`、绝对地址、CRLF 头、伪造 `X-Request-Id` 均为编程错误）；`IntegrationOutboundGuardTests`（20 个地址判定含 IPv4 映射与 NAT64 内嵌、受信网段不能放开恒拒段、配置错误启动即失败且消息不含秘密、`Retry-After` 解析、凭据 `ToString` 掩码）。
  - 写调用不被底层重复：`A_write_whose_response_is_lost_is_never_resent_by_the_transport`（读请求复用同一连接；写请求每次新连接且带 `Connection: close`；对方执行后断开→结果未知且对方恰好执行一次；带幂等标识的写只执行一次）。
  - 适配器替换无需复制 HTTP 代码：`A_custom_adapter_overrides_only_protocol_steps_and_reuses_the_infrastructure`（`DemoPartnerClient` 只覆写签名认证与结果识别：签名头、调用 Id、幂等标识照常；200 业务拒绝识别为 `Rejected`；202 为 `Accepted`；四次调用全部有记录）。
  - 出站记录查询与脱敏：`Outbound_records_are_admin_only_and_filterable`（匿名 401、无权限 403；按目标/结果/调用 Id/操作名/投递筛选）；`Secrets_never_reach_records_responses_admin_views_or_logs`（库表、后台分页与目标清单、宿主全部日志中三种秘密形式出现 0 次；目标清单只给「能否取到秘密」）；`Response_bodies_are_bounded_and_query_strings_are_never_recorded`；`Retention_also_deletes_expired_outbound_records`。
  - 菜单与路由一致：既有 `Seeded_menu_buttons_match_every_admin_route_exactly` 覆盖新增两个管理路由。
  - 本目标未启动可靠投递器。
- 执行命令与结果：
  - `dotnet build backend/TenonAdmin.slnx -c Release` → 成功，0 警告。
  - SQLite：`--filter "FullyQualifiedName~IntegrationOutbound"` 连续 3 次 39/39；`--filter "FullyQualifiedName~Integration|FullyQualifiedName~ReplaceabilityTests|FullyQualifiedName~OperationLog"` → 126/126。
  - `--filter "FullyQualifiedName~Integration"`：MySQL 84/84、PostgreSQL 84/84、SQL Server 84/84。
  - `node scripts/check-contract-drift.mjs`：退出码 1，为相对 HEAD 的预期差异；两套 `schema.d.ts` 完全一致，净变化为纯新增（11 行「删除」均在别处原样出现，属对齐伪影）。未手改生成文件。
  - `web`、`web-react`：`npm run typecheck` 退出码 0；`npm run gen:icons` 生成 85 个图标。
- 限制与处理：
  - 首轮 39 条中「对照组」一条失败：预设「默认池化客户端会在对方执行后断开时静默重发」不成立（.NET 10 实测直接抛出连接重置）。已删去该对照与相关表述；写请求每次独立连接保留为不依赖传输层内部重试判定的保证，并以连接标识验证。
  - DNS 变化用可覆写的解析步骤模拟（真实 DNS 不便操纵）；解析结果照常逐个经过地址策略。建连超时用例依赖 Linux 在监听积压满时丢弃 SYN（本机验证通过）。
  - 默认禁用代理（有代理时地址策略失效）；需要经代理出网的部署须在代理侧限制目标。出站记录与调用方共用数据库客户端：在事务内发起的外呼，其记录随事务回滚（文档已说明，原子外呼用可靠投递）。
  - SQL Server 全量用时 14 分钟（每个带配置的宿主都跑完整 CodeFirst）；此后为测试工厂增加 `SchemaNeutral`，只改模块运行选项的宿主沿用本进程模板库。

## G08 — 事务内投递记录

- 日期：2026-09-27
- 实际文件：
  - 模块：`Entities/IntegrationDelivery.cs`（`itg_delivery`，`DeliveryStatus` 八个状态；唯一 `DeliveryKey`、(`Status`,`NextAttemptAtUtc`)、(`Adapter`,`Status`)、`CompletedAtUtc` 索引）、`Entities/IntegrationDeliveryAttempt.cs`（`itg_delivery_attempt`，种类与触发方枚举）、`Abstractions/IDeliveryOutbox.cs`（`DeliveryRequest`、`DeliveryEnqueueResult`）、`Abstractions/IDeliveryAdapter.cs`（`IDeliveryAdapter`、`DeliveryContext`、`DeliverySendResult`、`DeliveryQueryResult`）、`Delivery/DeliveryOutbox.cs`、`Delivery/DeliveryAdapterBase.cs`、`Delivery/DeliveryAdapterStartupValidator.cs`、`IntegrationOptions`（`Delivery` 配置与租约 > 最长超时 + 30 秒的启动校验、`Retention:DeliveryDays`）、`IntegrationErrorCode`（49040–49044）、`IntegrationSetup.cs`。
  - 测试宿主（消费者示例）：`Demo/DemoDeliveryAdapters.cs`（可去重可查询、既不去重也不可查询两种适配器）、`DemoTicketService.CreateAndSyncAsync`（业务建单与入队同一事务，不手写投递表）、`Program.cs` 注册适配器。
  - 测试：`IntegrationDeliveryOutboxTests.cs`；`IntegrationAppFactory.SchemaNeutral`（带模块运行选项的宿主沿用模板库）。
  - 文档：实现契约 §7。
- 验收项与测试（真实数据库，不借助假仓储）：
  - 提交同时保留、重启仍在：`A_committed_business_write_and_its_delivery_persist_together_across_a_restart`（宿主 A 经消费者服务建单并入队 → 释放宿主 → 宿主 B 连同一个库读到工单与投递；标识、适配器、操作、业务键、规范化载荷、待处理状态、次数上限 8、截止 = 下次处理 + 24 小时）。
  - 回滚两者都不保留：`A_rolled_back_transaction_keeps_neither_the_business_row_nor_the_delivery`（消费者服务入队后业务校验失败；客户端事务内插入 + 入队后抛出——两种路径库中均无工单、无投递）。
  - 无事务边界：`Enqueue_without_a_transaction_is_refused_unless_explicitly_standalone`（49041 且不写行；`Standalone` 才独立入队）。
  - 重复标识：`Repeated_keys_are_idempotent_and_conflicting_reuse_is_refused`（同键同内容——含不同空白与转义写法的原始 JSON、适配器名大小写不同——返回同一记录；载荷/适配器/操作不同 → 49042；未给标识生成 `dlv_{雪花号}`）；`Concurrent_enqueues_of_one_key_leave_exactly_one_record`（未提交期间另一事务撞同一键，只留一行，后到者读到既有记录或以异常回滚）。
  - 入队校验：`Invalid_requests_are_rejected_before_anything_is_written`（未注册适配器 49043；操作、标识、业务键、次数、时限、载荷各类非法 49044 且不写行；覆盖项与延迟生效）。
  - 本目标未启动投递器（投递器在 G09）。
- 执行命令与结果：
  - `dotnet build backend/TenonAdmin.slnx -c Release` → 成功，0 警告。
  - SQLite：`--filter "FullyQualifiedName~IntegrationDeliveryOutbox"` 6/6；`--filter "FullyQualifiedName~Integration|FullyQualifiedName~ReplaceabilityTests|FullyQualifiedName~OperationLog"` → 132/132。
  - `--filter "FullyQualifiedName~Integration"`：MySQL 90/90、PostgreSQL 90/90、SQL Server 90/90（7 分 14 秒，较 G07 的 14 分钟减半）。
- 限制：跨库（业务在副库）不属于同一事务，不承诺原子性（契约与后续文档明示）。并发同键时后到者是读到既有记录还是以锁/唯一冲突异常回滚取决于方言的锁行为，用例只断言两者之一且始终只留一行。

## G09 — 投递恢复、核对与管理后端

- 日期：2026-09-27
- 实际文件：
  - 模块：`Delivery/DeliveryStateMachine.cs`（纯函数迁移规则：`AfterSend`、`AfterQuery`、`AllowedActions`、`Backoff`、`ResetBudget`）、`Delivery/DeliveryStore.cs`（栅栏 CAS：领取、迁移、迁移 + 尝试记录短事务）、`Delivery/DeliveryDispatcher.cs`（扫描 → 领取 → 事务外调用 → 按栅栏回写；租约过期恢复；迟到结果只记尝试、迟到成功可推进待核对）、`Delivery/DeliveryAdminService.cs`（列表、详情、汇总、五种人工操作的服务端复检）、`Delivery/DeliveryConfirmationService.cs`（回调确认）、`Delivery/DeliveryAlerts.cs`（`DeliveryAlertPublisher`、默认 `LoggingDeliveryAlertSink`）、`Jobs/IntegrationDeliveryJob.cs`（含 `itg-delivery` 种子，5 秒、串行跳过）、`Jobs/IntegrationRetentionService.cs`（已完结投递按完成时刻清理，连同尝试记录）、`Controllers/IntegrationDeliveryController.cs`、`Seed/IntegrationMenuSeed.cs`（「可靠投递」页 + 8 个按钮）、`Abstractions/{IDeliveryDispatcher,IDeliveryAdminService,IDeliveryConfirmationService}.cs`、`Entities/IntegrationDeliveryAttempt.cs`（新增 `Expired` 种类）、`IntegrationErrorCode`（49045–49047）、`IntegrationSetup.cs`。
  - 本地可控第三方：去重回放按工单当前状态返回（处理中回放 202，不把受理说成完成）。
  - 测试宿主：`Demo/DemoDeliveryAdapters.cs`（共用基类按响应体 `status` 识别受理/失败；去重 + 可查询、只可查询、两者皆无三种适配器）。
  - 测试：`IntegrationDeliveryDispatchTests.cs`（14 条）、`IntegrationDeliveryRulesTests.cs`（规则矩阵）、`IntegrationReplaceabilityTests.cs`（投递服务前置注册胜出、内置实现步骤 virtual）。
  - 前端契约与文案：两套 `schema.d.ts` 由 `check-contract-drift.mjs` 重新生成；两套 `locales` 增加 `error.code.49040–49047`；React 图标子集加入投递菜单图标。
  - 文档：实现契约 §3、§8。
- 验收项与测试（本地可控第三方 + 可拨时钟，真实数据库）：
  - 双宿主争抢与进程中断不丢记录：`Two_hosts_competing_for_the_same_records_deliver_each_exactly_once`（两宿主共享一个库并发各扫两轮，12 条对方不去重的投递每条恰好执行一次、各一条发送记录）；`An_interrupted_dispatch_is_recovered_by_capability_and_never_blindly_resent`（模拟「发出后、回写前崩溃」：去重对方以同一标识重发且对方只执行一次，可查询对方先查询后成功，两者皆无转待核对并告警；租约未过期的记录不被触碰）。
  - 迟到结果不覆盖新领取：`A_late_result_never_overwrites_a_newer_claim`（A 调用超时期间租约过期，B 恢复并成功；A 的迟到「结果未知」只追加尝试记录）。
  - 远端成功但响应丢失：`A_lost_response_is_resolved_by_partner_capability_and_never_duplicates_business`（去重：同一标识重发、对方回放、业务一次；可查询：查到已完成即成功、不再发送；两者皆无：待核对且不再自动发送）。
  - 异步受理继续等待确认：`Accepted_is_not_success_and_waits_for_poll_or_callback_confirmation`（受理不记成功；轮询期间对方仍处理中继续等待，对方完成后成功；回调确认、重复回调幂等、相反结果不自动改写、未知标识；不可查询的受理到确认时限转待核对并告警）。
  - 次数/时间上限：`Attempt_budget_backoff_and_deadline_are_enforced`（退避 30 秒起翻倍、未到期不处理、第 3 次后耗尽并告警；截止时刻已过即耗尽并记 `Expired`）。
  - 外部失败不改本地业务：`Deterministic_rejections_fail_without_touching_the_committed_business_row`。
  - 人工操作：`Manual_actions_are_rechecked_on_the_server_and_keep_the_idempotency_key`（可用操作由服务端给出；不可用 49045、页面栅栏过期 49046、确认缺说明 49047；重试重置预算且两次发送同一幂等标识；时间线含操作人与说明；汇总计数；操作进入 `sys_op_log`；匿名 401、无权限 403）；`Manual_query_and_confirm_not_executed_follow_the_remote_truth`；`Manual_retry_cannot_bypass_the_outbound_address_policy`（人工重试后仍在建连前被地址策略拒绝，对方零请求）。
  - 告警与默认可观察输出：`Alerts_reach_every_sink_and_the_default_warning_log_even_if_one_sink_fails`。
  - 保留：`Retention_removes_only_old_completed_deliveries_and_their_attempts`（只删超期的成功/已取消及其尝试记录，待核对、耗尽、失败、待确认永不自动删除）；任务：`The_seeded_job_drives_the_dispatcher`。
  - 规则矩阵：`IntegrationDeliveryRulesTests`（结果未知按对方能力分流、取消等同未知、受理等待方式、退避上限与 `Retry-After`、查询结果按阶段解释、人工操作可用性含「对方不去重的未知发送」禁止直接重试与适配器移除后的限制）。
- 执行命令与结果：
  - `dotnet build backend/TenonAdmin.slnx -c Release` → 成功，0 警告。
  - SQLite：`--filter "FullyQualifiedName~IntegrationDeliveryDispatch"` 14/14；`--filter "FullyQualifiedName~IntegrationDeliveryRules|FullyQualifiedName~IntegrationReplaceability"` 19/19；`--filter "FullyQualifiedName~Integration|FullyQualifiedName~ReplaceabilityTests|FullyQualifiedName~OperationLog"` → 157/157。
  - `--filter "FullyQualifiedName~Integration"`：MySQL 115/115、PostgreSQL 115/115、SQL Server 115/115（7 分 29 秒）。
  - `node scripts/check-contract-drift.mjs`：退出码 1（相对 HEAD 的预期差异）；两套 `schema.d.ts` 完全一致，净变化为纯新增（50 行「删除」均在别处原样出现）。`web`、`web-react` 的 `npm run typecheck` 退出码 0。
- 限制：
  - 进程崩溃用「记录停在处理中且租约过期 + 对方已执行」的库内状态模拟（与真实崩溃留下的状态一致）；多副本争抢在同一进程内以两个宿主共享一个库验证，跨进程多副本由 G13 的联合回归复核。
  - 领取者标识只供排障（缺省 `{机器名}#{进程号}`），正确性只依赖栅栏。CI 的 SQL Server PR 子集尚未列入投递类，G13 补入。

## G10 — Vue 出站调用与投递管理

- 日期：2026-09-27
- 实际文件：
  - 类型与接口：`web/src/types/integration.ts`（出站记录、目标、投递列表/详情/尝试/计数/操作入参均取自生成的 `schema.d.ts`；`OutboundOutcome`、`OutboundAuthType`、`DeliveryStatus`、`DeliveryAttemptKind`、`DeliveryAttemptTrigger`、`DeliveryActions` 与后端数值一致）、`web/src/api/integration.ts`（`integrationOutboundLogApi.page/targets`、`integrationDeliveryApi.page/summary/get/act`）。
  - 页面：`web/src/views/integration/shared.ts`（查看类按钮权限码、五种人工操作的权限码、结果/状态语义色、状态说明、尝试种类/触发方/结论文案映射）、`outbound-log/index.vue`（出站记录 ProTable、详情抽屉、目标清单抽屉，`?deliveryId=`/`?callId=` 深链且刷新保持）、`delivery/index.vue`（带计数的状态筛选条，`?status=`/`?deliveryKey=`/`?businessKey=` 深链）、`delivery/components/DeliveryDrawer.vue`（记录全貌、适配器能力、服务端判定的可用操作、时间线、载荷、跳转出站记录；操作弹窗回传栅栏，失败即刷新到最新状态）。
  - 文案：两套语言包的 `integration.outbound`、`integration.outboundLog`、`integration.delivery` 命名空间（错误码 49030–49047 见 G07/G09）。
  - e2e：`web/playwright.integration.config.ts`（后端为消费者示例 `IntegrationSample`，第三方为 `IntegrationMockPartner`，秘密经环境变量注入，确认轮询压到 5 秒）、`web/e2e/integration-delivery.spec.ts`（4 条）、`web/package.json` 的 `test:e2e:integration`；主配置 `testIgnore` 排除该用例（主套件仍用 MinimalHost）。
- 验收项与演示（真实后端 + 本地可控第三方，数据经示例的后台业务接口产生）：
  - 失败定位 → 允许的恢复 → 最终结果：用例 1。对方拒绝 → 列表深链打开详情，状态「失败」与说明；按钮可点与否只看 `allowedActions`（重试可点、确认已成功不可点）；填写说明重试 → 待处理 → 投递器发送成功；时间线含「对方拒绝」发送、带说明的人工操作；「查看出站调用」带 `deliveryId` 深链列出两次发送，刷新后筛选仍在。
  - 核对与不可绕过：用例 2。对方不去重且响应丢失 → 待核对；直调重试接口返回 49045；界面重试、查询按钮禁用；「确认已成功」缺说明时提示且弹窗不关，写明说明后成功，终态再无可用操作。
  - 受理 ≠ 成功：用例 3。已受理待确认并提示「这不是成功」；对方完成后由轮询确认成功，时间线出现查询记录。
  - 无权限：用例 4。只授予页面、汇总、详情查看的新角色能进入菜单、看到详情，看不到任何操作按钮与出站记录入口。
  - 不暴露秘密：用例 1 打开目标清单只显示「已配置」，整页 HTML 不含秘密；出站记录详情不含请求/响应正文与请求头（后端契约见 G07）。
- 执行命令与结果（`web/`）：
  - `npm run lint`、`npm run typecheck`、`npm run build` 退出码 0；`npx vitest run` 38 个文件 206/206。
  - `npx playwright test -c playwright.integration.config.ts`：连续 3 次 4/4（1.2m、1.2m、1.1m）；抽屉工具栏改为无操作权限时也保留「刷新」后再跑 1 次 4/4（1.1m），同时 `vue-tsc`、`oxlint` 退出码 0。
  - 主套件 `npx playwright test`：列出 18 条（投递用例已排除），17 通过、1 失败——`workflow-m2a.spec.ts`「M2a branch definition…」在第 153 行等不到「审批中」。工作区内复跑 2 次同样失败；在 `git archive HEAD` 导出的干净副本（HEAD 的 MinimalHost 与前端）上同样在第 153 行失败，属既有问题，与本目标无关，未改动。
- 限制：出站调用与投递管理的 e2e 依赖消费者适配器，故后端用消费者示例而非 MinimalHost；示例本身的交付证据在 G12 记录。

## G11 — React 出站调用与投递管理

- 日期：2026-09-28
- 实际文件（React 模板自有实现，未从 `web/` 导入任何代码）：
  - 类型与接口：`web-react/src/types/integration.ts`（取自生成的 `schema.d.ts`，枚举常量与后端数值一致）、`web-react/src/api/integration.ts`（`integrationOutboundLogApi`、`integrationDeliveryApi`，时间范围口径与开放调用记录一致）。
  - 页面：`web-react/src/views/integration/shared.ts`（权限码、五种人工操作的权限码、antd 语义色、状态说明与尝试文案映射）、`outbound-log/index.tsx`（`DataTable`、详情抽屉、目标清单抽屉、`?deliveryId=`/`?callId=` 深链）、`delivery/index.tsx`（带计数角标的状态筛选条，每次表格加载后刷新计数；`?status=`/`?deliveryKey=`/`?businessKey=` 深链）、`delivery/components/DeliveryDrawer.tsx`（服务端判定的可用操作、时间线、载荷、跳转出站记录；操作弹窗用 `FormContainer`，回传栅栏，失败即刷新）。
  - 权限与状态：按钮级权限用 `useHasPerm()`（内部按原始字段分别订阅，不返回新对象）与 `<Can>`；Ant Design 6 写法（`Drawer size`、`Tag variant`、`Alert title`）。
  - e2e：`web-react/playwright.integration.config.ts`、`web-react/e2e/integration-delivery.spec.ts`（4 条，antd 选择器按 `role=dialog` 取弹层）、`package.json` 的 `test:e2e:integration`；主配置 `testIgnore` 排除该用例。
  - 文案与图标在 G07/G09 已加入（`integration.outbound/outboundLog/delivery`、错误码 49030–49047、两个菜单图标）。
- 验收项与演示（独立执行，不以 Vue 结果代替）：
  - 允许的恢复：用例 1，失败 → 带说明重试 → 成功；时间线含拒绝发送与人工操作；深链到出站记录两次发送、刷新保持；目标清单只显示「已配置」且页面不含秘密。
  - 拒绝恢复：用例 2，待核对时重试、查询禁用，直调重试接口 49045；缺说明确认被拦且弹窗不关，写明说明后成功，终态无可用操作。
  - 远端受理后未完成：用例 3，已受理待确认并提示不是成功，重试不可用；对方完成后轮询确认成功，时间线出现查询。
  - 无权限：用例 4，只有查看权限的角色看得到详情，看不到任何操作按钮与出站记录入口。
- 执行命令与结果（`web-react/`）：
  - `npm run typecheck`、`npm run lint`、`npm run build` 退出码 0；跨模板导入与返回对象的 store 选择器检查均为 0 处。
  - `npx vitest run` 130 个文件 967/967。其中一次运行另报 1 个未处理错误（`system/config/structuredPanels.spec.tsx` 卸载后 React 调度访问 `window`）；随后两次全量运行与该文件单跑均为 0 错误，属与本目标无关的偶发卸载时序问题。
  - `npx playwright test -c playwright.integration.config.ts`：首跑用例 2 失败。原因是校验不通过时确认钮闪过加载态，antd 收起后把已不可见的加载图标节点留在按钮里，可访问名称变为「loading 确认已成功」，以 `^` 锚定的名称匹配不到；界面本身正常（录屏可见按钮可点）。用例改为在弹窗页脚内按结尾匹配后连续 3 次 4/4（1.5m、1.4m、1.6m）。
  - 主套件 `npx playwright test`：17/17（4.2m，投递用例已排除）。
- 限制：用例在实时连接协商期间跳转页面时，浏览器控制台会出现 SignalR「connection was stopped during negotiation」，来自模板既有的实时通知挂钩，不影响结果。

## G12 — 消费者示例、开发模板与文档

- 日期：2026-09-28
- 实际文件：
  - 消费者示例与本地可控第三方（G07–G10 期间建成，此处补文档）：`backend/samples/IntegrationSample/`（新增 `README.md`：运行方式、哪些是业务代码、三种用法的演示要点）、`backend/samples/IntegrationMockPartner/`。示例为通用「合作方工单」领域，无 ERP 实体；包含只含公开字段的 DTO、自定义「合作方」范围（查询过滤 + 写入校验）、事务外普通调用、事务内入队、去重可查询与两者皆无的两种适配器、回调确认。
  - 开发模板：`templates/content/tenon-app/.template.config/template.json`（布尔参数 `integration`，默认关；关时排除 `Integrations/**`）、`TenonApp.csproj`、`Program.cs`、`appsettings.json`（条件接线与 `partner` 目标，秘密不入文件）、`Integrations/`（`SampleDocOpenController`、`PartnerClient`、`SampleDocSyncAdapter`、`SampleDocSyncService`、`SampleDocSyncController`、`README.md`，需要按对方协议改的位置标注【按对方协议填写】）、`README.md` 与 `AGENTS.md`（可选项与铁律）。
  - 模板冒烟：`templates/smoke-test.ps1` 增加 `--integration` 生成物的精确版本还原、构建、运行到 `/health` 与开放端点无凭据 401 探测，默认生成物断言不含该包与目录；抽出 `Test-GeneratedHost`，启动前确认端口空闲（原脚本在 5100 被占用时会拿别的进程的 `/health` 误判通过），失败时附宿主日志；`-Port` 仅供本地端口被占时使用，CI 仍按原样 `dotnet run`。
  - 配方：`skills/wire-integration.md`，`.claude/skills/wire-integration/SKILL.md`、`.agents/skills/wire-integration/SKILL.md` 薄壳；`skills/README.md`（表格、命令、技能数 12）、`skills/new-module.md`（可选加挂）。
  - 站点：`site/zh/guide/integration.md`（中文母版：启用、开放端点规则、数据范围、应用/凭据/轮换/授权、调用约定与版本、普通调用目标与地址安全、结果分类、事务内入队、适配器能力、投递器恢复与状态、回调与告警、人工处理、部署、保留、故障排查）、`site/guide/integration.md`（英文，H2/H3 与表格一一对应，源码中的中文报错字面量保留原文）、`site/.vitepress/config.ts` 两侧侧栏、`site/{,zh/}community/agent-skills.md`。
  - 其他：`CHANGELOG.md` 的 Unreleased 条目；实现契约 §13 补模板与文档落点。
- 验收项与演示：
  - 从干净消费者目录走通三个流程（`scratchpad/g12-walkthrough.sh` + `g12-flows.py`）：只用本地源打包出的 9.9.9-g12 包（隔离的 `NUGET_PACKAGES`、独立模板 hive），`dotnet new tenon-app -n acme-portal --integration` → 精确版本还原 → `-warnaserror:NU1603` 构建（0 警告）→ 按模板 README 起本地可控第三方与应用（秘密经环境变量）→ 用首启打印的随机密码登录后，以前端调用的同一组管理接口执行：
    - 开放接口：建应用 → 发凭据 → 未授权 403 `49002` → 授权后未绑范围 403 `49003` → 绑定机构后开放 POST 建单、GET 列表只含 DTO 三个字段 → 开放调用记录含拒绝与成功 → 撤销后下一次调用 401 `49001`。
    - 普通调用：查到对方合作方；`missing` 返回 null；出站记录两条，地址无查询串，响应不含秘密；目标清单只显示「已配置」。
    - 可靠投递：建文档与入队同事务，投递转成功；给对方排「执行后断开」后，记录经「结果未知」按退避用同一标识重发并成功，对方对该标识只执行一次，时间线为两次发送（未知、成功）。
    - 共 21/21 项通过，应用日志中秘密出现 0 次。
  - 消费者不需要重写认证、日志与投递基础设施：生成物里除示例业务代码外无任何基础设施代码；不依赖工作流包与消息中间件（nuspec 依赖只有 `TenonAdmin.AspNetCore`）。
  - 双前端用法：G10/G11 的两套页面随菜单种子出现，站点页与模板 README 都说明无需另写前端。
- 执行命令与结果：
  - 打包内容：在只含已跟踪与未忽略文件的干净导出里 `dotnet pack backend/TenonAdmin.slnx` 与模板包，退出码 0（6 条警告均为不打包项目的「packaging has been disabled」提示）。`TenonAdmin.Integration` 包含 `lib/net10.0` 的 dll 与 XML 文档、README、图标，依赖 `TenonAdmin.AspNetCore` 同版本；模板包含 `Integrations/` 与盖好版本的 `template.json`；两个包均无 `.omc` 等本地运行时文件。
  - `pwsh templates/smoke-test.ps1 -Port 5199`（干净导出、隔离包缓存）：默认与 `--integration` 两种生成物均通过，`[OK] smoke passed`。首次按原样在 5100 运行时，该端口被一个 11 天前启动、与本任务无关的 MinimalHost 开发进程占用，脚本的 `/health` 实为该进程应答，第二个宿主的探测因此得到 404；据此加入端口空闲检查与 `-Port`，未改动那个进程。
  - 站点：`node scripts/lint-prose.mjs` 单页与全站（110 页）均无违规；`npx vitepress build --outDir <scratchpad>` 成功，无死链。
- 限制：
  - 走查中的后台操作通过管理接口完成（与页面调用同一组接口），页面本身的交互由 G05/G06、G10/G11 的双前端 e2e 覆盖；端口用 5198 而非 5100，原因同上。
  - 模板示例协议按本地可控第三方编写，对接真实系统时需改标注处；`dotnet new tenon-app` 生成的项目未内置本地可控第三方，需在内核仓库里运行它。
  - 本机未安装 PowerShell，冒烟脚本用临时目录里的 `dotnet tool` 版 PowerShell 7.6.6 执行。

## G13 — 完整验收、故障验证与独立评审

> §1–§9 保留前两轮的历史记录；最新修复与验证见 §10。历史的 503 安全重发结论和第七段去抖证据已被独立复核推翻，不能作为当前验收依据。

- 日期：2026-09-28 至 2026-09-29。

### 1. 设计验收矩阵

设计文档《验收标准》九项逐一对应到具体测试/消费者示例/管理界面（`scratchpad/g13-matrix.md`，未入库，摘要如下；各项详细文件与命令见 G02–G12 对应小节，本节只补记本轮新增或加固的证据）：

| # | 验收内容 | 主要落点 | 本轮加固 |
|---|---|---|---|
| 1 | 示例消费者只写业务接口+声明范围即可被授权调用 | G03/G04/G12；`IntegrationSample`、`--integration` 模板 | 消费者走查（本轮）21/21 全过，含 `SampleDocSyncAdapter` 能力声明注释与 404 判定修正 |
| 2 | 凭据生命周期、多副本一致 | G02/G05/G06；`IntegrationCredentialLifecycleTests` | 撤销/停用/发放全部改为按列条件更新（不再整行回写），新增 `A_metadata_only_update_never_writes_the_owner_org`；`scripts/smoke-multi-replica.sh` §7（新增，见下）直接对两个副本容器核对撤销/停用即时生效 |
| 3 | 应用权限与业务数据隔离，未配置范围不退化为全量 | G03；`IntegrationOpenApiAuthorizationTests` | **修复一处越权**：授权、绑定范围、发放/轮换凭据、调整到期、启用应用、改归属机构现仅限超级管理员（`41003`，对齐内核 QA09/QA36），新增 `Only_super_admins_can_expand_an_apps_reach`；机构范围候选值改按调用者数据范围过滤 |
| 4 | 独立 OpenAPI 文档准确、秘密不泄露 | G04；`IntegrationOpenApiConventionTests` | 追踪标识改为 `HttpContext.TraceIdentifier`，与调用记录、异常日志三处一致，新增 `Outbound_calls_made_inside_an_open_request_share_its_trace_id` |
| 5 | 普通调用可观察超时、无盲目重试 | G07；`IntegrationOutboundGuardTests` | 503 仅在带合法 `Retry-After` 时按未发出处理，否则按结果未知；新增 `Service_unavailable_counts_as_not_sent_only_with_a_valid_retry_after` |
| 6 | 事务原子性、多副本与中断不丢任务 | G08/G09；`IntegrationDeliveryOutboxTests`、`IntegrationDeliveryDispatchTests` | 无新增（本轮未改此段状态机之外的核心逻辑） |
| 7 | 响应丢失、去重能力决定重发策略 | G09；`IntegrationDeliveryDispatchTests` | 非暂时性未发出（凭据缺失、目标被拦截）不再占预算重试、直接转重试耗尽；新增 `A_configuration_failure_is_not_retried_automatically_but_can_be_retried_by_hand` |
| 8 | 重试耗尽可核查、人工恢复保持幂等、异步受理不误报成功 | G09；`IntegrationDeliveryRulesTests`、`IntegrationDeliveryDispatchTests` | **修复一处状态错误**：新一轮发送清空上一轮的确认时限与对方引用，否则重新受理后的记录带着已过期的时限立即被判超时；新增 `A_new_send_cycle_or_a_fresh_acceptance_never_reuses_an_old_confirmation_window`、`A_resent_delivery_accepted_again_gets_a_fresh_confirmation_window`；**新增一条回调确认专属约束**：`IDeliveryConfirmationService.ConfirmAsync` 改为要求声明适配器集合、可选核对业务键、拒绝确认从未发出的记录（否则任何被授权调用回调端点的接入应用都能确认走别的适配器、甚至从未发出的投递），新增 `A_callback_can_only_confirm_deliveries_inside_the_calling_apps_scope`、`Confirmation_is_limited_to_declared_adapters_business_key_and_sent_records`；示例回调端点（`IntegrationSample`、测试宿主）同步改为先按调用应用的范围核实业务行 |
| 9 | 双前端等价、可前置替换、未启用不占用资源 | G02–G06/G10–G12；`IntegrationReplaceabilityTests` | **修复一处暴露面**：模块控制器改标 `[NonController]`，仅在 `AddTenonAdminIntegration` 注册的特性提供者里挂回，只引用本包而未调用任何一步不再暴露任何路由（此前会暴露、只是认证失败），新增 `A_host_that_only_references_the_package_exposes_no_module_routes`；六个 SPI 接口（`IOpenApiCatalog`/`IOpenAppAuthorizationService`/`IOpenAppContext`/`IInboundLogService`/`IOpenAppRateLimiter`/`IIntegrationRetentionService`）补齐前置注册测试 |

### 2. 联合回归

- 全量后端套件（SQLite，`dotnet test backend/TenonAdmin.slnx -c Release --no-build`）：**1823/1823** 通过，0 跳过（G12 结束时为 1799；本轮迭代新增 24 个测试，见下）。
- 针对性子集（`FullyQualifiedName~Integration|ReplaceabilityTests|OperationLog|ApiAuthGap`）：**181/181**。
- 数据库方言矩阵（`FullyQualifiedName~Integration|OperationLogCoverageTests`，最终一轮修复后完整重跑）：MySQL **142/142**（52s）、PostgreSQL **142/142**（48s）、SQL Server **142/142**（8m34s）。三方言与 SQLite 结果一致，含新增的非 ASCII 范围编码比较测试（`String_scope_values_are_bound_as_parameters_so_non_ascii_codes_match`，验证 `WhereInScope` 改参数化比较后跨方言行为一致）。
- CI：`.github/workflows/backend-ci.yml` 的 SQL Server PR 子集追加 `IntegrationCredentialLifecycleTests`、`IntegrationOpenApiAuthorizationTests`、`IntegrationDeliveryOutboxTests`、`IntegrationDeliveryDispatchTests` 四个类；nightly 全量路径未改动，继续覆盖 PR 子集之外的方言敏感测试。

### 3. 多副本与故障注入联合回归

- `docker compose -f docker-compose.yml -f docker-compose.scale.yml up`（两个显式 `app`/`app2` 服务 + MySQL + Redis + Caddy）执行 `scripts/smoke-multi-replica.sh`：既有 6 段批次六保证（强制登出、锁定阈值不翻倍、雪花 WorkerId 不同、真实客户端 IP、限流跨副本共享、任务只触发一次+备用节点接管）全部通过。
- **本轮新增第 7 段**（脚本已改动，随批次六保证一起成为永久 CI 回归，不再是一次性手工验证）：直连两个副本容器 IP（绕开 Caddy 轮询，保证确定性而非「靠轮询碰运气」），核对——未授权前 B 拒绝（403）→ A 授权后 B 立即放行（200）→ A 停用后 B 立即拒绝（401）→ A 撤销凭据后 A 与 B 都立即拒绝（401）。命令：`bash scripts/smoke-multi-replica.sh http://localhost:18080`（本地把 8080/8081 换成 18080/18081，避开已占用端口；独立 compose 项目名，`down -v` 收尾）。
- 额外的集成模块专属核对（`scratchpad/g13-itg-multi.py`，未入库，是 §7 加入脚本前的手工验证版本，内容更全：还核对了 A 停用 B 启用、B 撤销授权 A 立即拒绝、经 Caddy 轮询 10 次全部 401、跨副本调用记录汇入同一张表）：18/18 通过。
- 执行时点：最终一轮修复（控制器发现、N1/N2/N4）落地后，基于干净导出重新构建镜像、重新执行，而非复用旧结果。
- **§7 首次落地时出现过一次假失败**：直连副本 A 的撤销后检查单次返回非 401，而同一时刻经 Caddy 轮询该撤销 10 次全部 401、容器日志无任何异常。写了一个独立复现脚本（`scratchpad/g13-repro-revoke.sh`，未入库）单独重放同一序列：直连 A 连续 10 次、直连 B 连续 3 次全部正确返回 401（6–60ms），应用日志无异常，凭据记录的 `revokedAt`/`status` 正确。确认这是直连容器 IP（不经 Caddy、没有其重试）的一次性网络抖动，不是产品缺陷；给 §7 的直连检查加了「连续两次读到相同结果才采信」的去抖（其余各段仍走 Caddy，天然有其重试，未改动）。加固后按上文命令重新跑通整份脚本（含 §1–§7）全部通过，不再复现。

### 4. 模板与消费者走查（最终代码）

- `pwsh templates/smoke-test.ps1 -Port 5199`（干净导出、隔离包缓存、最终代码打包为 `9.9.9-smoke`）：默认与 `--integration` 两种生成物均通过，`[OK] smoke passed`，耗时 66 秒。
- 消费者走查（`scratchpad/g13d-walkthrough.sh` + `g12-flows.py`，未入库；仅用最终代码打包的 `9.9.9-g13d` 本地包、隔离 `NUGET_PACKAGES`、独立模板 hive）：G12 记录的三个流程 **21/21** 全过，应用日志秘密出现 **0** 次；开放调用的 `X-Trace-Id` 现为 ASP.NET 请求标识格式（如 `0HNOTP773U68V:00000001`），印证追踪标识改动在生成物里同样生效。

### 5. 对修改文件执行 anti-slop-cleaner

两轮独立只读扫描（`backend/src/TenonAdmin.Integration/**`、两套前端 `views/integration/**` 及相关 `api`/`types`/`locales`/`e2e`），均为 **COMMENT** 结论（无阻断项），随后各自的写手单独一轮落地：

- 后端（25 项）：合并重复的 `Truncate`/`DeliveryContext` 构造/`Combine` 拼接/适配器查找等助手；补全 `IntegrationReplaceabilityTests` 的六个 SPI 前置注册用例；修正过时或夸大的注释（出站日志摘要、`OperationLogFilter` 豁免规则、人工重试适用范围等）；删除死代码（`CODE_MAX_LENGTH`、`DeliveryActions.All`、未用的 mock 路由）。**一处行为修正**：中断恢复在预算未尽但已过截止时刻时，原因码从 `max_attempts` 改为准确的 `deadline`，新增回归测试。落地后 Release 构建 0 警告，全量 1801/1801（1799+2）。
- 前端（23 项，双模板独立落地，互不导入）：出站结果/尝试种类/触发方标签统一为一个查键辅助函数；`rangeQuery`/`queryText` 去重；删除未用的 `IntegrationAppDetail`/`integrationAppApi.get`/4 个死 `PERM` 键/3 个未用本地化键；React `Descriptions` 迁移到 `items`（antd 6 弃用旧写法）；e2e helper 去重。落地后两套模板 lint/typecheck/vitest/build 全绿。

### 6. 独立评审（只读，禁止自评代替）

评审对象为清理落地之后的完整代码；每轮发现问题都在同一批次内修复并重新验证，共两轮：

**Round 1**（对 G02–G12 的实现 + 清理结果）：

- **架构评审**（`oh-my-claudecode:architect`，只读）— 结论 **BLOCK**，两个阻断项：
  - B1：合作方回调 `IDeliveryConfirmationService.ConfirmAsync(deliveryKey, ...)` 仅按标识查找，未校验回调端点声明的适配器与业务归属，被授权调用回调端点的应用能确认（甚至伪造成功/失败）任何投递，包括从未发给自己的。
  - B2：新一轮发送/人工确认未清空上一轮的确认时限与对方引用，受理后重新计时会立即命中已过期的旧时限，转「待核对」并误报告警。
  - 另有 13 条非阻断建议（见下方已处理/记录部分）。
- **后端代码评审**（`oh-my-claudecode:code-reviewer`，只读）— 结论 **NOT acceptable**，2 个阻断 + 2 个主要：
  - 阻断①：非超级管理员持「接入应用」按钮即可自助把范围改成「全部数据」、发凭据，等价于自制一把能读写全部机构的密钥（内核 QA09/QA36 曾关闭同类口子）。
  - 阻断②：同 B1（回调确认未限定调用方）。
  - 主要①：同 B2（确认时限未清空）。
  - 主要②：应用/凭据的整行回写可能覆盖并发的停用/撤销/软删（竞态窗口内，恰好是故障处置时最常用的操作）。
  - 另有 9 条次要问题。
- **前端代码评审**（`oh-my-claudecode:code-reviewer`，只读）— 结论 **NOT READY**，1 个阻断 + 4 个主要：
  - 阻断：授权抽屉 `save()` 在加载失败或切换应用时仍可能对着空状态或另一个应用的数据保存，能清空一个应用的全部授权，或把一个应用的授权错发到另一个应用。
  - 主要：授权抽屉的入口只按 1 个权限码控制，实际加载需要 5 个，权限不足的角色必进阻断态；凭据轮换并存窗口清空后按 0 处理（旧凭据立即失效，而非按系统默认）；投递详情对已失效/延迟到达的记录响应处理不当，可能对错误记录动作、可能在栅栏冲突后仍悄悄用刷新后的栅栏重发。
  - 另有 8 条次要问题。
  - **全部 16 条后端问题 + 13 条前端问题当轮修复**，各自新增能在旧代码上失败的回归测试；关键统计：后端 Release 构建 0 警告，全量 1820/1820；前端两套模板 lint/typecheck/vitest/build 全绿，双模板集成 e2e 各 9 条（原 6 条 + 新增授权加载失败/49046 冲突/清空轮换窗口三条）全过。

**Round 2**（复核 Round 1 的修复，同时发现并当轮处理的新问题）：

- **架构复核** — 结论 **APPROVE WITH RECOMMENDATIONS**：B1/B2 均验证已修复（含用探针程序重放状态机确认）；13 条建议中的多数已随本轮或上一轮修复覆盖（非暂时性未发出不占预算、503 判定、整行回写改列级、认证失败限流改按 (IP,KeyId)、半接线启动即报错、非 `[OpenApi]` 却用应用凭据方案的端点启动即拒绝、`ISkipOperationLogMetadata` 补测试）；指出一处遗留偏差——仅调用 `UseIntegration` 而未调用 `AddTenonAdminIntegration` 时无法在启动期拦截（见「已知限制」）；补充两条永久性记录到已知限制（调度器单批串行 20 条可能被单个卡死的目标拖慢全体、按次调用的超时上限不覆盖同一投递内的多次调用）。
- **后端复核** — 结论**可接受**：两个阻断与两个主要均已修复且有能失败的回归测试证据；复核中发现 6 条新次要问题（N1 新建应用未同步要求超级管理员、N2 更新时无条件回写未变的归属机构字段、N3 限流改按 (IP,KeyId) 后可通过轮换 KeyId 绕过、N4 出站记录的追踪标识与入站不一致、N5 适配器自建的未发出结果默认不重发需要消费者显式声明、N6 启动校验未解析具名授权策略里的应用凭据方案）。**N1/N2/N4 当轮修复**（见下），N3/N5/N6 记录为已知限制。
- **前端复核** — 结论：13 项中 12 项 FIXED、1 项 PARTIAL（下载失败信封已解析出错误码，版本号仍硬编码 `v1`，非阻断，记录为已知限制）；复核中发现 1 条新次要问题（授权抽屉保存过程中若应用被切换/关闭，`save()` 仍按旧引用继续，可能把保存结果套到已切换的应用上，或让另一个应用的抽屉被意外关闭）。**当轮修复**：改为保存前固化本次操作的应用 Id 与待写内容，仅当抽屉仍显示同一应用时才提示成功/收起；顺带修了保存一次性凭据未确认即离开页面会静默丢失的问题（改为路由离开前询问，Vue 用 `onBeforeRouteLeave`，React 按页面路径隐藏浮层而非直接卸载，避免遗留在下一页上方）。

**收尾修复**（架构复核的遗留偏差 + 后端复核的 N1/N2/N4，最后一轮）：

- 模块控制器改标 `[NonController]`，仅由 `AddTenonAdminIntegration` 注册的特性提供者挂回：只引用本包不再暴露任何端点；单独调用 `AddTenonAdminIntegration` 已有的启动校验保持不变；两步都调用行为不变。新增 `A_host_that_only_references_the_package_exposes_no_module_routes`（用一个仅引用程序集、未调用任何一步的宿主证明零路由零建表）。**遗留偏差**：单独调用 `UseIntegration` 无法在启动期拦截（该方法只拿到 `TenonAdminOptions`，无法注册标记或校验器，需要内核钩子，超出本模块范围），实测该接线路径本身不暴露任何端点、不启动任何任务，只留下空的 `itg_*` 表，已在实现契约与下方「已知限制」注明。
- N1：新建应用若指定归属机构，同样要求超级管理员（此前仅「修改」归属机构受限，「新建时设定」未受限）。
- N2：更新应用元数据（名称/说明/限流）不再连带回写归属机构列；仅当归属机构真的改变时才写入该列并要求超级管理员，与并发的止损操作不再互相覆盖。
- N4：出站调用记录的追踪标识改为请求内的 `HttpContext.TraceIdentifier`（与入站记录、`X-Trace-Id`、内核异常留痕一致），后台任务等无请求上下文的调用仍退回 W3C Activity 标识。
- 落地后 Release 构建 0 警告，全量 **1823/1823**；`dotnet publish` 重新发布、Docker 镜像用干净导出重新构建，§2–§4 的全部验证在这份最终代码上重新执行（而非复用修复前的结果）。

### 7. 双前端 e2e（最终代码，逐一执行）

| # | 模板 | 用例 | 结果 | 耗时 |
|---|---|---|---|---|
| 1 | web | `-c playwright.integration.config.ts`（5 条） | 5 passed | 1.5m |
| 2 | web | `integration-app`（4 条） | 4 passed | 1.0m |
| 3 | web-react | `-c playwright.integration.config.ts`（5 条） | 5 passed | 1.5m |
| 4 | web-react | `integration-app`（4 条，独立执行） | 4 passed | 1.2m |
| 5 | web | 主套件（20 条） | 19 passed，1 failed | 3.7m |
| 6 | web-react | 主套件（19 条，首次运行时 `integration-app.spec.ts` 的九步串行流程与套件内其余用例共享 worker 资源，从独立执行时的约 30s 压线到 31.3s 触发默认 30s 超时，导致该用例及其后 3 条同 `describe.configure({ mode: 'serial' })` 的用例一并未执行；加 `test.setTimeout(60_000)`（与仓库里 mfa-bind/module-switch/rbac-permission 长流程既有写法一致）后重跑，全部通过，与本轮评审修复无关） | 19 passed（重跑） | 4.1m |

第 5 项的失败是既有问题：`e2e/workflow-m2a.spec.ts:153`「M2a branch definition…」等不到「审批中」，与本次改动无关；G10 记录已在 `git archive HEAD` 干净导出上复现过同一失败，本轮结果与之一致，未改动该用例。

### 8. 已知限制（记录而非阻断）

- 仅调用 `UseIntegration()` 而不调用 `AddTenonAdminIntegration()` 时，无法在启动期报错；该接线本身不暴露端点、不启动任务，只留下未使用的空 `itg_*` 表（架构复核认可为非阻断，修复需要内核改动，超出本模块范围）。
- 认证失败限流改按 (IP, KeyId) 计数后，轮换随机 KeyId 可绕过针对已知 KeyId 的限流（每次尝试仍需一次索引查询，秘密是 256 位随机数，内核 IP 级限流仍生效）。
- 适配器自行构造「未发出」结果时，`Transient` 默认为 `false`（不自动重发）；这是本轮从「默认重发」改为更安全的默认值，消费者适配器需要显式声明 `Transient: true` 才能让自建的未发出结果参与自动重试，已在模板与站点指南写明。
- 已修复（见 §10）：启动校验解析具名、默认及回退授权策略中的 `TenonOpenApp` 方案。
- 已修复（见 §10）：前置注册的选项实例用于实际校验与文档注册。前置工厂仍可替换服务，但不能动态引入注册阶段未声明的文档版本；该情况启动时明确报错，避免下载时 500。
- 单次投递调用的超时上限已截断到「租约减安全余量」，但不覆盖同一次投递内适配器发起的多次调用（如先换令牌再发送）之和；调度器单批串行处理 20 条候选记录，单个持续超时的目标会拖慢同批其他目标的处理。
- 模板适配器 `SampleDocSyncAdapter` 的 `SupportsIdempotency`/`SupportsQuery` 默认声明为 `true`——因为随包演示用的本地可控第三方确实支持两者；对接真实第三方前必须核实协议，注释已加粗提示。
- 既有问题，与本次改动无关：`web/e2e/workflow-m2a.spec.ts` 的分支路由用例在等待「审批中」状态时超时（G10 起持续复现，含干净导出）；开发机 5100 端口被一个不相关的 11 天前启动的 MinimalHost 进程占用，冒烟与消费者走查改用 5198/5199 端口规避，未触碰该进程。

### 9. 汇总

- 本轮未发布版本、未推送代码、未打 NuGet 包到 nuget.org；打包与安装均在 `NUGET_PACKAGES`/`--debug:custom-hive` 隔离的临时目录内完成。
- 文档事实同步：`docs/third-party-integration-{goals,design,implementation}.md` 顶部状态行更新为「已完成/已通过验收」；`docs/third-party-integration-implementation.md` 修正了两处与代码不符的引用（`EnsureOrgAllowed`→`OpenAppDataScope.EnsureAllowed(long?)`；`ApplyTo`→`WhereInScope`），补记本轮新增的行为（超级管理员门槛、回调确认约束、非暂时性未发出不重试、503 判定、追踪标识、列级更新、控制器发现）；`site/{,zh/}guide/integration.md`、`skills/wire-integration.md`、`backend/samples/IntegrationSample/README.md` 同步给出示例代码与故障排查条目；两份指南均通过 `node site/scripts/lint-prose.mjs`。
- `scripts/smoke-multi-replica.sh` 新增第 7 段并入永久 CI 回归（`docker-smoke.yml` 的 `multi` job 每次 push/PR 触碰 `backend/**` 等路径都会执行），不再依赖本轮的一次性手工验证。
- 未解决限制均已在上方「已知限制」逐条列出；没有真实输出的检查未标记通过。


### 10. 第二模型供应商独立复核后的修复与重验（2026-09-29）

本轮基于代码重新核查，不沿用前两轮的通过结论。修复独立复核的 11 项，并在修复后交叉检查中补齐新建默认启用、未类型化输出两条遗漏；未提交、推送或发布。

| 问题 | 修复与回归落点 |
|---|---|
| 异常携带秘密进入响应、数据库或日志 | `OutboundHttpInvoker`、`DeliveryDispatcher`、告警与出站记录服务使用固定摘要；不向日志传第三方异常对象。`IntegrationOutboundTests` 以秘密哨兵覆盖认证、分类、适配器、告警异常与非法响应头。 |
| 503 + Retry-After 导致未知结果盲目重发 | 默认分类一律 `Unknown`，保留合法重试时间。可控第三方先提交再回 503；无去重能力的投递进入待核对，实际执行一次。旧 §1/G07 的相反结论失效。 |
| 具名策略绕过开放接口约束 | 启动校验解析具名、默认、回退策略；非开放动作使用应用认证方案启动失败。 |
| 前置选项实例未驱动文档注册 | 先选择实际实例，再校验和注册文档；前置工厂新增未注册文档版本时启动失败，工厂不提前执行。 |
| MockPartner 同键并发重复执行 | 同键判断、创建与执行计数在同一锁内；8 批各 32 个并发请求均只执行一次。 |
| 双副本第七段可能假通过 | 所有管理写操作直连 A 并检查业务成功；授权后验证 B 首次响应；撤销前重新启用并证实凭据有效，撤销后 A/B 首次请求都拒绝。移除去抖；只在状态变更前等待健康检查。旧 §3 的去抖结果不再证明即时一致性。 |
| 深层实体与未类型化输出绕过 | 类型图使用已访问集合终止循环，不按深度放行；拒绝 `Result<object>`、`Dictionary<string, object>`、非泛型集合及不可静态校验的接口/抽象输出。 |
| 模板标题未校验 | 开放/后台 DTO 与共享服务均拒绝空白或超过 128 字符；生成物真实 HTTP 调用验证 400 / 49007。 |
| 文档分组暴露非开放动作 | 文档要求匹配分组且具有 `[OpenApi]` 元数据；伪装分组的普通动作不收录。 |
| 双前端超级管理员门槛缺失 | 两端分别增加能力扩展操作门槛，普通管理员保留元数据编辑、停用、撤销；普通新建明确提交 `enabled: false`。后端也拒绝普通管理员显式或默认启用的新建请求。 |
| 模板清理误杀同名进程 | 仅清理当前临时生成目录内的程序；提取实际清理代码，用两个同名可执行进程验证只停止所属实例。 |

验证记录（本轮真实运行）：

- 修复主批次（收尾两条安全补修之前）：Release 构建 0 警告、0 错误；SQLite 全量 **1829/1829**。MySQL、PostgreSQL、SQL Server 集成模块各 **144/144**（SQL Server 11m21s）。
- 收尾重验曾在 `Result<object>` 规则断言失败，排查发现共享构建输出与源码不一致。补充直接断言，将精确拒绝放在标量放行之前，随后单节点统一重编，再使用同一份产物执行回归。中间轮结果不作为最终证据。最终 Release 单节点构建 0 警告、0 错误；MySQL、PostgreSQL、SQL Server 的 `IntegrationAdminApiTests|IntegrationOpenApiAuthorizationTests` 收尾回归各 **19/19**。
- 限流用例的偶发失败已定位并修复：原测试第二次请求预期 429，但固定窗口可能恰在两次请求之间换分钟，导致新窗口的首次请求合法返回 200。并发重复执行捕获该断言；用可控时钟把第二次请求移到下一窗口，稳定复现同样的 200（快照仍为启用、阈值 1）。`RateLimitTests` 统一固定注入时钟后，定向 **4/4**、SQLite 最终全量 **1829/1829**，0 跳过（4m10s）。运行时固定窗口语义未改。
- 完整 Docker 双副本冒烟 **§1–§7 全部通过**。本次发布的宿主程序集构建为独立运行时镜像，前端代理复用既有镜像；双前端源码另外分别构建验证。仅清理本轮 `tenon-integration-fix` Compose 项目及其卷。
- 在独立源码导出、包缓存和模板 hive 中执行完整 `templates/smoke-test.ps1 -Port 5199`，默认与 `--integration` 生成物均编译、启动、接口探测通过；清理进程隔离探针通过。
- 双前端 lint、typecheck、build 通过；新增普通管理员权限 E2E 分别通过；收尾加入真实新建、停用状态与无法重新启用断言后，两端定向 E2E 再次各 **1/1**，lint/typecheck 再次通过。
- `check-contract-drift.mjs` 生成两份 schema 成功，但相对 HEAD 的差异检查退出 1：Integration schema 原已未提交。本轮生成物与运行前快照用 TypeScript AST 按接口成员比较完全相同，仅成员顺序变化；已恢复运行前字节，保留原有修改。该项记录为契约语义一致，未声称脚本退出 0。
- 中英文指南同步 503 语义；两页 prose 检查通过。原有已知限制仍按 §8 记录，未把本轮未重跑的消费者完整走查或前端全套 E2E 标为新的通过证据。

最终检查：`git diff --check` 通过；保留用户原有工作区修改。所有本轮启动的独立 Compose 容器、卷与进程隔离探针均已清理，临时验证日志位于 `/tmp/tenon-integration-fix-xf1syxlz`。

### 11. 整理提交前的本地重验（2026-09-29）

- Release 单节点构建：0 警告、0 错误；SQLite 全量 **1830/1830**，0 跳过，TRX 位于 `/tmp/tenon-precommit-results/precommit.trx`。
- 双前端 lint、typecheck、生产构建通过；Vitest 使用 `--maxWorkers=2`，Vue **206/206**、React **966/966**。首次默认并发运行出现图标加载及表单测试的 5 秒超时；限制并发后通过，未修改这些测试的断言或超时。
- 双前端接入应用 E2E 各 **6/6**，投递 E2E 各 **5/5**，总计 **22/22**，四套串行执行。Vue 九步流程首次在第八步耗尽默认 30 秒总预算，现与 React 同类用例一样单独使用 60 秒预算；高负载重跑还曾在登录输入框处超时。当时内存紧张、8 GB swap 已耗尽；待全量测试结束后串行运行全部通过。串行 Vue 九步流程耗时 22.4 秒，未放宽步骤断言。资源竞争是本次超时的支持性证据，不据此宣称所有偶发失败均已消除。
- 分片脚本测试 **2/2**、中英文接入指南 prose 检查、暂存差异空白检查通过。两份 OpenAPI schema 重新生成，提交前契约脚本相对旧 HEAD 的检查退出 1；提交后再次执行 `node scripts/check-contract-drift.mjs`，退出 **0**，输出 `contract in sync`，日志位于 `/tmp/tenon-postcommit-contract.log`。
- 本次未重跑多数据库矩阵、Docker 双副本、模板冒烟及双前端非接入模块全套 E2E；相关历史结果见 §10，不能将本次重验称为完整 GitHub Actions 全绿。工作区同时有其他任务的品牌与主题修改，这些改动未纳入本次接入模块提交；本地测试结果也不替代干净提交的 CI。
- 本次日志：`/tmp/tenon-precommit-{backend,contract,vue-unit,vue-build,vue-app,vue-delivery,react-unit,react-build,react-app,react-delivery}.log`。
