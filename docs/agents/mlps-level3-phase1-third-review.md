# 等保三级第一期第三轮审查

- 审查日期：2026-07-29
- 审查基线：当前工作区相对 `HEAD` 的未提交改动
- 结论：上轮四项阻断问题已修复，但当前实现仍有一项阻断问题和三项中严重问题，暂不应提交。

## 已验证修复

- 外部 SSO 回调会传递 `totpChallenge`，两套前端回调页均跳转到 TOTP 登录完成态。
- Redis 校验改为 `ISecureCacheCapabilities` 加实际探针，不再按类型名判断。
- 预检会拒绝小于 32 字节的数据保护密钥。
- 绑定邀请服务层拒绝普通路径的 `issuedByUserId <= 0`，并在 Level3 校验发起人 TOTP 与邀请权限。

## 阻断项

### 1. TOTP 提交绕过统一请求客户端，现有 Cookie 会导致 403

- 证据：`web/src/api/index.ts` 和 `web-react/src/api/index.ts` 对 `/api/v1/auth/login/totp` 使用原始 `fetch`，没有调用客户端的 CSRF 注入逻辑。`CsrfMiddleware` 会在 Level3 的状态修改请求携带 refresh Cookie 时强制要求 `X-Tenon-CSRF`。
- 影响：浏览器仍有 refresh Cookie 时，TOTP 提交缺少 CSRF 请求头并被服务端拒绝为 403；这会使过期会话后重新登录或切换 SSO 的用户无法完成登录。
- 修复：运行两端 `gen:api` 生成 `/api/v1/auth/login/totp` 合同，改用 `client.POST(...).then(unwrap<LoginOutput>)`，让既有 Bearer/CSRF/刷新中间件接管。补充“存在 refresh Cookie 的 TOTP 提交”回归测试。

## 中严重问题

### 2. 新端点仍未走生成的 OpenAPI 合同

- 证据：两份 `schema.d.ts` 不包含 `/api/v1/auth/login/totp`，而两端 API wrapper 手写请求体、信封解析和输出断言。
- 影响：后端合同漂移不再由类型系统发现，违反项目的 `gen:api` 与 `openapi-fetch` 规则。
- 修复：与第 1 项一并生成合同并删除手写 `fetch`/`Envelope` 解析。

### 3. 邀请撤销缺少服务层授权校验

- 证据：`MfaEnrollmentService.RevokeBindInviteAsync` 接收 `operatorUserId`，但只撤销并记录该值，不读取操作者或校验其 TOTP、管理员资格和邀请权限。
- 影响：控制器以外的调用方可撤销任意邀请，并在审计记录中伪造操作者，造成可用性与审计完整性风险。
- 修复：撤销路径复用签发路径的发起人授权校验；系统紧急身份同样只允许已验证的紧急恢复流程。

### 4. 自定义高敏权限没有受保护的动态管理闭环

- 证据：`MfaPolicyService` 会读取 `SysHighSensitivityPermission` 并影响 MFA 强制范围，但没有服务、受 `[RolePermission]`/`[RequireReauth]` 保护的 API 或 Vue/React 管理页。
- 影响：管理员无法按一期约定动态维护消费者追加项，只能直接改库。
- 修复：提供受权限和再认证保护的列表/新增/删除接口与两套模板页面，默认高敏集合继续保持不可删除。

## 规范问题

### 5. 邀请路由权限码重复散落

- 证据：`MfaEnrollmentService` 与 `HighSensitivityPermissions` 都硬编码 `POST:/api/v1/sys/mfa/invite`。
- 影响：路由调整后服务层授权和高敏集合可能漂移。
- 修复：由单一集中常量定义该权限码，其他位置引用该常量。

## 验证

- `git diff --check HEAD`：通过。
- `dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~ExternalAuthTests|FullyQualifiedName~MfaEnrollmentTests|FullyQualifiedName~Level3PrecheckTests" --no-restore`：42/42 通过，约 65 秒；构建仍有 nullable 与 XML 文档警告。
- `web`、`web-react` 的 `npm run typecheck`：均通过；这不覆盖第 1 项的 CSRF 中间件绕过。
