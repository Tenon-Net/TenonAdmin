# 等保三级应用安全基线第一期审查记录

- 审查日期：2026-07-29
- 审查基线：当前工作区相对于 `HEAD` 的未提交改动
- 结论：需要修改；修复并完成验证前不应提交
- 规格依据：[第一期执行提示词](mlps-level3-phase1-execution-prompt.md)、[Level3 内核计划](../mlps-level3-kernel-plan.md)、[ADR 0005](../adr/0005-mlps-kernel-assessment-boundary.md)

## 阻断项

### 1. 强制 TOTP 可经其他登录入口绕过

- 严重性：高
- 证据：[账号密码登录](../../backend/src/TenonAdmin.Services/Auth/AuthService.cs) 会调用 `CheckTotpSecondFactorAsync`，但 [短信免密登录](../../backend/src/TenonAdmin.Services/Auth/AuthService.cs) 和 [外部登录](../../backend/src/TenonAdmin.Services/Auth/AuthService.cs) 会直接签发令牌。
- 影响：已被强制 MFA 的超级管理员、高敏权限用户或显式强制 TOTP 用户，可通过短信免密或外部登录绕过 TOTP。
- 修复：将 Level3 MFA 门禁收口到所有签发令牌的登录路径；为非密码认证路径设计同样的 TOTP 挑战/完成流程，并增加旁路回归测试。

### 2. Vue 与 React 未实现 TOTP 登录完成流

- 严重性：高
- 证据：[Vue 登录页](../../web/src/views/login/LoginForm.vue) 和 [React 登录页](../../web-react/src/views/login/LoginForm.tsx) 仅处理 `40009` 短信挑战，未处理 `40018`，也没有调用 `/api/v1/auth/login/totp`。
- 影响：强制 TOTP 用户在密码验证后无法完成登录；后端新增的 TOTP 登录能力不可用。
- 修复：重新生成两端 OpenAPI 合同，补齐 API wrapper、TOTP 输入和挑战状态、成功/过期/返回账号密码的交互，以及两套模板的测试。

### 3. Redis Level3 门禁不能证明实际安全连接

- 严重性：高
- 证据：[Level3PrecheckService](../../backend/src/TenonAdmin.Services/Security/Level3PrecheckService.cs) 只依据 `Cache:Provider=Redis` 和连接串文本判断；[RedisSetup](../../backend/src/TenonAdmin.Caching.Redis/RedisSetup.cs) 仍要求消费者显式注册 Redis provider；[RedisCacheProvider](../../backend/src/TenonAdmin.Caching.Redis/RedisCacheProvider.cs) 未将 `RequireTls` 应用于 `ConfigurationOptions`。
- 影响：配置可写为 Redis，但实际 DI 仍在使用内存缓存，或 Redis 以明文连接，导致会话、锁定和挑战状态不满足 Level3 的 fail-closed 约束。
- 修复：预检确认实际 `ICacheProvider` 类型和可用连接；将 TLS 强制应用到 Redis 客户端配置；Level3 下发现内存 fallback、缺失认证或 TLS 时拒绝启动并使 readiness 失败。

### 4. 普通用户未纳入闲置账户治理

- 严重性：高
- 证据：[IdleAccountService](../../backend/src/TenonAdmin.Services/Security/IdleAccountService.cs) 仅查询 `TotpEnabled || ForceTotp` 的账户。
- 影响：普通启用账户即使超过 90 天未成功登录，也不会自动停用。
- 修复：扫描全部启用账户；普通账户执行 90 天停用，MFA 账户在 60 天告警、90 天停用，超级管理员始终只告警。

### 5. 高风险操作未普遍要求再次认证

- 严重性：高
- 证据：[RequireReauthAttribute](../../backend/src/TenonAdmin.AspNetCore/Security/RequireReauthAttribute.cs) 目前仅用于超级管理员 MFA 重置；用户、角色、权限、配置等一期列出的高风险操作未接入。
- 影响：攻击者获得仍有效的管理会话后，可直接执行高风险变更。
- 修复：建立明确的高风险动作清单，并为对应控制器动作应用再次认证门禁；同时保留可替换的业务扩展点。

### 6. MFA 绑定邀请未验证发起人 MFA 状态

- 严重性：高
- 证据：[MfaController](../../backend/src/TenonAdmin.AspNetCore/Controllers/MfaController.cs) 的邀请入口仅做路由权限校验；[MfaEnrollmentService](../../backend/src/TenonAdmin.Services/Mfa/MfaEnrollmentService.cs) 信任传入的发起人 ID。
- 影响：不满足“由已完成 TOTP 的管理员发放邀请”的既定边界；直接调用服务或遗留会话可能绕过约束。
- 修复：服务层读取并验证发起人身份、管理员权限和 TOTP 状态；邀请发放操作要求短时再次认证，并补充负向测试。

### 7. 存量账户迁移失败会被吞掉

- 严重性：高
- 证据：[Level3EnableMigrationHostedService](../../backend/src/TenonAdmin.Services/Security/Level3EnableMigrationHostedService.cs) 捕获迁移异常后只记录日志。
- 影响：`LastSuccessfulLoginAt` 未正确初始化时系统仍可进入 Level3，后续闲置账户治理的安全锚点不可信。
- 修复：将迁移失败转化为启动失败，或写入可由预检识别的 critical failure；不得继续以“已启用 Level3”状态运行。

## 需要修复的非阻断项

### 8. 再认证授权跨会话复用

- 严重性：中
- 证据：[CacheKeys](../../backend/src/TenonAdmin.Core/CacheKeys.cs) 以 `reauth:{userId}` 建键，[ReauthService](../../backend/src/TenonAdmin.Services/Mfa/ReauthService.cs) 与 [RequireReauthAttribute](../../backend/src/TenonAdmin.AspNetCore/Security/RequireReauthAttribute.cs) 也只按用户读取。
- 影响：同一用户任一浏览器会话完成再次认证后，其他并发会话也可执行高风险操作。
- 修复：改为基于 JWT `sid` 的再认证授权；会话吊销时删除对应授权。

### 9. Level3 缺少验证码有效策略下限

- 严重性：中
- 证据：现有验证码开关仍由可变更的 `sys.security.captcha.enabled` 控制，第一期没有在有效策略读取层施加 Level3 下限。
- 影响：管理员或运行时配置可关闭登录验证码，违背已采纳的 Level3 登录加固决策。
- 修复：在 Level3 有效策略层设置验证码的不可放宽规则，并覆盖 `SysConfig` 试图放宽的测试。

## 验证状态

- `git diff --check`：通过。
- 审查 agent 报告：后端 Release build、Vue/React typecheck 和 lint、64 个 Level3/MFA/TOTP 定向测试通过。
- 本地复跑定向后端测试：超过 124 秒未取得结束结果，修复后应重新执行并保存明确结果。
