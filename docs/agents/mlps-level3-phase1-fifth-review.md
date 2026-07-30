# 等保三级第一期 ForceTotp 管理面复审

- 审查日期：2026-07-29
- 审查范围：本轮 `ForceTotp` 用户管理字段、其服务端策略与关联的 MFA 绑定闭环。
- 审查基线：当前工作区相对 `HEAD` 的未提交改动；未将此前一期的其他改动归责给本轮实现。
- 结论：暂不应批准该管理面。字段读写、登录拦截和权限保护已正确接通，但“将未绑定用户设为强制 TOTP”会在正常 Web 部署中制造无法完成绑定的账户。

## 修复状态（2026-07-29）

本记录中的阻断项已修复并完成定向复验：两套模板都提供受权限保护的一次性邀请签发/复制、公开绑定页、手动密钥与 `otpauth` URI、一次性恢复码展示及恢复码入口；API 包装直接使用 OpenAPI 生成类型。绑定挑战现在只在动态口令验证成功后原子消费，输错口令可用同一挑战重试，且已有回归测试。

### 关闭复验（2026-07-30）

本轮关闭项已按当前工作区复验：

- React `BindPage` 在 `bindComplete` 后经 `takeRecoveryCodes` 拒绝空/缺失恢复码，不进入空白成功屏；已有 `bindComplete.spec.ts` 与 `BindPage.spec.tsx`。
- Vue `mfaApi` 请求/响应类型均来自 `components['schemas']`，未手改 `schema.d.ts`。
- Vue 绑定页与 React 对齐：`web/src/views/mfa/bindComplete.ts` + `bindComplete.spec.ts`，空白恢复码同样拒绝进入成功屏。
- 后端新增 HTTP 回归：`Http_force_totp_user_write_requires_reauth`（用户 ForceTotp 写路径再认证 40024→授予后成功）与 `Http_force_totp_invite_bind_and_totp_login_roundtrip`（邀请→密码绑定→邀请一次性→TOTP 登录）。

### 收口计划验证矩阵（2026-07-30）

见 `mlps-level3-phase1-closeout-plan.md`（状态：**第一期已完成**）。矩阵结果摘要：

- `web-react` typecheck / test(789) / build：通过
- `web` typecheck / test(76) / build：通过
- `backend` build：通过
- 定向 `dotnet test`（MfaEnrollment + Level3* + ExternalAuth + PermissionCodeConsistency）：**69/69** 通过

浏览器 E2E 已补：`web` / `web-react` 的 `e2e/mfa-bind.spec.ts` + `e2e/login-totp.spec.ts`（各 3/3 通过，2026-07-30）。全量后端套件未在本轮重跑。完成一期内核收口 **不等于** 产品或任何部署已通过等保三级测评。

## 问题

### [高][Spec] ForceTotp 开关没有可用的受控绑定邀请与完成绑定闭环

Vue 和 React 均已把 `forceTotp` 写入用户新增/编辑表单，并只读显示 `totpEnabled`：

- `web/src/views/system/user/components/UserFormModal.vue:105-122,185-190`
- `web-react/src/views/system/user/index.tsx:451-460`
- `web-react/src/views/system/user/userForm.ts:63-110`

服务端会正确拒绝尚未绑定的强制 MFA 用户登录，已有覆盖：`backend/tests/TenonAdmin.Tests/MfaEnrollmentTests.cs:324-334`。管理员邀请和目标用户绑定端点也已经存在于 OpenAPI 合同中：`/api/v1/sys/mfa/invite`、`/api/v1/auth/mfa/bind/start`、`/api/v1/auth/mfa/bind/complete`。

但是两端 API 包装层均未暴露上述邀请/绑定端点，且两端页面中不存在签发并一次性展示/复制邀请的操作，也没有用户持邀请完成扫描、验证和恢复码展示的页面。由此，管理员可以把未绑定的用户置为 `ForceTotp=true`，该用户随后只会得到 `TotpNotBound`，而管理员和用户均无法通过交付的 Web 模板完成规定的绑定流程。

这不符合 `docs/adr/0005-mlps-kernel-assessment-boundary.md:20-23,41-43` 及 `docs/agents/mlps-level3-phase1-execution-prompt.md:57-63`：未绑定的强制 MFA 用户必须等待已完成 TOTP 的管理员发放 15 分钟一次性邀请，目标用户验证当前密码后完成绑定；内核需允许管理员在后台复制邀请并由消费者选择交付渠道。

修复建议：在两端以生成的 OpenAPI 合同增加类型化 API 包装；在用户管理的未绑定用户操作中增加受现有邀请权限和再次认证保护的“发放绑定邀请”操作，一次性展示并可复制链接/令牌；补充公共绑定页面（`start`/`complete`）及恢复码一次性展示。两套模板应行为一致。

### [中][测试] 未覆盖 HTTP 授权和完整的 ForceTotp 交互链路

**状态（2026-07-30）：后端 HTTP 缺口已补。** 现有后端测试覆盖服务层字段新增、回读、更新，以及密码、短信和外部登录路径的强制 MFA 拒绝；React 还覆盖了表单映射。关闭复验后另有：

- `MfaEnrollmentTests.Http_force_totp_user_write_requires_reauth`：Level3 下用户新增/更新 `ForceTotp` 无 reauth → `40024`，授予后成功。
- `MfaEnrollmentTests.Http_force_totp_invite_bind_and_totp_login_roundtrip`：邀请签发需 reauth → 密码绑定 → 邀请不可复用 → TOTP 登录。

仍缺：浏览器端到端、Vue 用户表单映射/组件测试、以及“前端邀请 UI”的专用组件测试（当前以管理面集成与绑定页测试为主）。

## 已验证

- `git diff --check HEAD`：通过。
- `dotnet test backend\\TenonAdmin.slnx --filter "FullyQualifiedName~MfaEnrollmentTests|FullyQualifiedName~ExternalAuthTests" --no-restore`：29/29 通过。
- `web/` 的 `npm run typecheck`：通过。
- `web-react/` 的 `npm run typecheck`：通过。

这些验证确认现有字段接线与登录策略没有明显回归；它们不覆盖上述 UI 绑定闭环。
