# 等保三级第一期修复后二次审查

- 审查日期：2026-07-29
- 审查基线：当前工作区相对 `HEAD` 的未提交改动
- 结论：暂不应提交一期实现。此前的短信/外部登录绕过、账密登录 TOTP 页面、闲置账户范围、再认证会话隔离、验证码下限与迁移失败处理均已修复；以下问题仍然存在。

## 阻断项

### 1. 外部 SSO 触发 TOTP 后没有可完成的登录流程

- 严重性：高
- 证据：`AuthService.LoginByExternalAsync` 调用 `CheckTotpSecondFactorAsync`，后者创建挑战并抛出 `40018`。`ExternalAuthController.Callback` 只重定向为 `error=40018`，不传递挑战 ID；两套前端 OAuth 回调页只显示错误，没有 TOTP 完成状态。
- 影响：被要求使用 TOTP 的用户无法完成外部 SSO 登录。绕过已关闭，但合法认证路径变为不可用。
- 修复：把 IdP 成功后的待完成状态转换成一次性 TOTP 挑战票据，使两套回调页进入正常 TOTP 完成流程，并增加后端及两套模板的回归测试。

### 2. Level3 Redis 校验信任实现类名

- 严重性：高
- 证据：`Level3PrecheckService.CheckActualCacheProvider` 以实现类名是否包含 `Redis` 或 `Memory` 判定实际缓存。
- 影响：内存包装器可通过命名伪造，合法消费者 Redis 实现也可能被误拒。预检无法证明实际缓存启用了 Redis、TLS 与认证。
- 修复：在 Core 定义 Redis/TLS/认证/健康状态的稳定安全能力契约，由 Redis provider 实现；预检只依赖该契约和真实连接探针。

### 3. 过短的数据保护密钥能通过预检，却会在首次使用时失败

- 严重性：高
- 证据：`Level3PrecheckService` 将任意有效 Base64 的 `DataProtection:Key` 报告为通过，而 `LocalDataProtectionKeyProvider` 拒绝小于 32 字节的密钥。
- 影响：Level3 能带无效密钥启动，首次使用 TOTP/秘密保护时才失败，未在启动阶段 fail-closed。
- 修复：预检与 provider 复用同一密钥材料校验，或在预检拒绝小于 32 字节的 Base64 值；增加启动/预检回归测试。

### 4. 绑定邀请缺少服务层发起人授权，且系统身份可直接绕过

- 严重性：高
- 证据：`MfaEnrollmentService.IssueBindInviteAsync` 只验证非零发起人启用且已绑定 TOTP，不验证其管理员授权；Level3 下 `issuedByUserId=0` 只写警告仍签发邀请。
- 影响：直接调用服务或未来接入点可绕过控制器路由权限；系统身份未被约束到经验证的紧急恢复流程。
- 修复：通过可替换授权服务验证发起人权限；将系统身份限制为明确且审计化的紧急恢复授权，普通调用拒绝传入 0。

## 非阻断项

### 5. TOTP 端点绕过生成的 OpenAPI 类型

- 严重性：中
- 证据：`web/src/api/index.ts` 与 `web-react/src/api/index.ts` 都以 `(client as any).POST('/api/v1/auth/login/totp', ...)` 调用新端点，并注明 schema 未重新生成。
- 影响：请求和响应契约不再受类型检查，违反项目的 API 合同规则。
- 修复：分别运行两端 `gen:api`，使用生成的路径和 `TotpChallengeLoginInput`，删除 `any` 包装。

### 6. 自定义高敏权限无法动态管理

- 严重性：中
- 证据：`HighSensitivityPermissions` 与 `MfaPolicyService` 提供不可变默认集和数据库读取逻辑，但没有管理 `SysHighSensitivityPermission` 追加项的控制器。
- 影响：管理员不能按一期约定动态维护消费者自定义追加项，只能直接改库。
- 修复：提供受权限和再认证保护的列表/新增/删除 API 与两套模板页面，同时保持默认集不可删除。

## 验证

- `git diff --check HEAD`：通过。
- `dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~MfaEnrollmentTests|FullyQualifiedName~MfaLoginFlowTests|FullyQualifiedName~Level3PrecheckTests|FullyQualifiedName~Level3SessionCsrfTests" --no-restore`：45/45 通过，约 67 秒；构建仍有 nullable 与 XML 文档警告。
- `web`、`web-react` 的 `npm run typecheck`：均通过。第 5 项仍然成立，因为当前代码明确以 `any` 绕过新端点的类型检查。
