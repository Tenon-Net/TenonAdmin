# 等保三级应用安全基线：第一期执行提示词

将以下内容完整交给一个实现 agent。该 agent 只负责第一期；不要提前实现第二期或第三期功能。

```text
你正在 TenonAdmin 仓库中实现“等保三级应用安全基线”第一期。TenonAdmin 是可分发的管理系统内核，不是等保定级或测评对象。你的交付目标是：在不要求消费者 fork 内核的前提下，提供显式启用、可验证的第一期应用安全能力。

## 先读这些文件

1. AGENTS.md 和 CLAUDE.md：遵守分层、可替换性、依赖与测试约束。
2. docs/mlps-level3-kernel-plan.md：产品目标、一期范围和不可省略的实现边界。
3. docs/adr/0005-mlps-kernel-assessment-boundary.md：已采纳的安全决策。
4. 以下现有实现：
   - backend/src/TenonAdmin.Services/Auth/AuthService.cs
   - backend/src/TenonAdmin.Services/Session/SessionService.cs
   - backend/src/TenonAdmin.Services/Entities/SysSession.cs
   - backend/src/TenonAdmin.Core/Security/ITokenProvider.cs
   - backend/src/TenonAdmin.Core/Options/AdminSecurityOptions.cs
   - backend/src/TenonAdmin.Core/Options/AdminCacheOptions.cs
   - backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs
   - backend/src/TenonAdmin.Caching.Redis/RedisSetup.cs
   - backend/src/TenonAdmin.Caching.Redis/RedisCacheProvider.cs
   - web/src/stores/user.ts, web/src/api/client.ts
   - web-react/src/stores/user.ts, web-react/src/api/client.ts

仓库可能已有其他人未提交的改动。保留这些改动，不要回退或覆盖无关文件；若同一文件有并发修改，合并并适配它们。

## 总体约束

- 保持既有依赖方向：Core -> SqlSugar -> Services -> AspNetCore。Core 不引用 SqlSugar 或 ASP.NET。
- 内核运行时仅可依赖 Microsoft.* 和既有依赖；不要为 TOTP、加密或 CSRF 引入不必要的第三方库。
- 所有新内核服务均要有接口、`TryAdd` 注册和可替换实现；保持 `AuthService` 的可继承/源兼容合同。新增构造函数依赖应采用可选尾参数，或通过新接口服务接入，不能破坏既有消费者子类。
- Vue 和 React 是彼此独立的模板。两端都要适配 Level3 登录流，但不得抽共享代码，也不得把两端强制捆绑。
- 非 Level3 的 API、前端登录流程和 `localStorage` 令牌模式必须保持完全兼容。
- 不得宣称 TenonAdmin 或使用它的系统“通过等保三级”；第一期只能暴露“已实现的第一期能力”和尚未实现项。

## 第一期开关和策略

1. 新增显式部署配置 `TenonAdmin:Security:Profile=Level3`，默认不启用。
2. 未启用时不改变旧项目的行为；生产环境未启用时记录明确告警，并由预检报告为不合规。
3. 已启用时通过有效策略层（例如 `ISecurityPolicyProvider`）施加不可放宽的下限。`SysConfig` 和管理页面可以收紧消费者策略，但不能放宽 Level3 下限。
4. 不要声称通用 `IConfiguration` 能可靠识别配置来源。内核负责防止数据库/运行时配置放宽下限；部署配置的可信来源由宿主部署模板、CI 和预检证明。
5. Level3 下 Redis 必须启用认证和 TLS，且不得退回内存缓存；启动预检必须明确失败。

## 密钥和一次性凭据前置能力

第一期先提供 `IDataProtectionKeyProvider` 与窄接口 `ISecretProtector`，作为后续秘密保护的稳定扩展点。

- TOTP seed 必须以信封加密方式存储，不能明文、可逆裸编码或仅哈希。
- 恢复码、绑定邀请、超级管理员初始化/紧急恢复令牌仅存哈希；明文最多显示一次。
- 设计要保留外部 KMS/HSM 接入空间，但不要在本期强绑定具体供应商。

## TOTP、MFA 与账户恢复

实现原生 TOTP，兼容主流 Authenticator。短信可以作为可选补充，不能作为唯一的 Level3 合规路径。

1. MFA 强制对象：超级管理员、拥有高敏权限的用户、以及被管理员显式标记为“强制 TOTP”的用户。
2. 高敏权限集合要有不可移除的内核默认项；页面只能维护消费者自定义追加项，不能删除默认项。
3. 首个超级管理员通过部署期、短时、一次性初始化授权完成建立；其他用户由已完成 TOTP 的管理员发放 15 分钟一次性绑定邀请。
4. 绑定邀请是 bearer 凭据，但目标用户还必须验证当前密码后，才可写入 TOTP seed。
5. 每次绑定生成 10 个恢复码，仅展示一次，服务端只保存哈希。
6. 使用恢复码后必须强制重新绑定 TOTP，并撤销该用户全部会话。
7. 超级管理员 MFA 重置需另一名已启用 TOTP 的超级管理员批准；仅有一个超级管理员时使用部署期紧急恢复授权，并记录最高级别安全事件。
8. 高风险操作需要最近 5 分钟内的再次认证：通常为 TOTP，必要时再验证当前密码。实现可替换的再认证判定/授予接口，不要将业务路由硬编码到认证服务。

## 密码、登录、账户活动策略

当 Profile=Level3 时，最低要求为：

- 最少 12 个字符；四类字符中至少三类。
- 最近 5 个密码不得复用；最长 90 天有效。
- 初始密码与重置密码后必须修改。
- 5 次失败后锁定至少 15 分钟。
- MFA 用户 60 天未成功登录先告警，90 天自动停用；超级管理员仅告警，不自动停用。

为实现会话/闲置规则，新增并迁移 `SysSession.AbsoluteExpiresAt`、`SysSession.LastActivityAt` 和用户 `LastSuccessfulLoginAt`（或等价持久化模型）。

- 首次启用 Level3 时，对现有启用用户以启用时刻初始化 `LastSuccessfulLoginAt` 并记录审计，避免因历史数据缺失批量停用。
- 活动时间不得每请求写数据库；通过 Redis 节流后回写。Redis 不可用时 Level3 应失败，不得静默降低安全性。

## Cookie 会话和 CSRF

仅 Profile=Level3 时切换浏览器会话模型：

- refresh token 放在 `HttpOnly + Secure + SameSite` Cookie。
- access token 只存在浏览器内存，不得持久化到 `localStorage` 或 `sessionStorage`。
- 实现双提交 CSRF：随机 Cookie 加 `X-Tenon-CSRF` 请求头；所有改变状态的 Cookie 认证请求必须校验，并在关键生命周期操作中轮换。
- access token 最长 15 分钟；普通用户闲置 30 分钟，MFA 用户闲置 15 分钟；绝对会话最长 8 小时，刷新不得突破绝对期限。
- MFA 用户默认单会话，最多允许 2 个且需告警；普通用户最多 3 个。
- 非 Level3 仍保留当前请求体 refresh 和前端持久化令牌模式。
- Vue、React 两套模板均要实现内存 access token、静默刷新、CSRF 请求头、登录/注销/过期处理，并通过生成的 OpenAPI 合同或稳定兼容字段保持可用。

## 预检与可验证性

实现可供宿主、页面和 CI 使用的 Level3 第一期开关预检：

- 输出结构化、机器可读的结果，并提供安全基线页面/接口。
- 检查 Profile、Redis TLS/认证、秘密保护提供方、MFA 初始化状态、会话策略等第一期必要项。
- 结果必须带“能力版本”和“本期未实现的 Level3 强制项”，不能把一期报告显示为完整三级基线。
- 预检失败应给出可定位的检查项和修复建议；必要的运行依赖不满足时启动或 readiness 必须明确失败。

## 本期明确不做

不要实现以下第二/三期内容，最多只保留必要接口边界：

- 180 天审计留存、审计外送 Outbox、SIEM/WORM、分区审计哈希链与签名锚点。
- 完整 HTTP 安全响应头与生产入口 HTTPS 冒烟验证。
- 恶意文件扫描、敏感字段通用加密/查询哈希/密钥轮换。
- 第三方应用 `ClientId + HMAC` 接入、SBOM、漏洞门禁、国密档案。

## 测试与验证

1. 先补齐或更新聚焦测试，再实现功能。测试至少覆盖：
   - Profile 默认兼容和 Level3 有效策略下限不可被 `SysConfig` 放宽。
   - Redis 不满足 TLS/认证时的预检/启动失败。
   - TOTP 绑定必须验证当前密码；seed 加密；恢复码仅一次且使用后撤销会话。
   - MFA 强制对象、高敏权限默认集合不可删除、超级管理员恢复路径。
   - 密码历史/过期/锁定、首启存量用户迁移与闲置账户规则。
   - Cookie 属性、CSRF 拒绝与通过、access token 不持久化、绝对/闲置会话期限、并发会话限制。
   - Vue 与 React 的构建和类型检查。
2. 运行与改动相称的后端测试、`dotnet build backend/TenonAdmin.slnx -c Release`，以及两套前端的 typecheck/lint/build。若某项因环境无法运行，说明原因并列出已完成的替代验证。
3. 不要手改 `web/src/api/schema.d.ts` 或 `web-react/src/api/schema.d.ts`；若 API 合同变更，使用各自 `gen:api` 生成。

## 交付格式

完成后报告：

1. 变更文件与每项能力的简述。
2. 配置示例和从旧模式切换到 Level3 的操作步骤。
3. 已运行的测试/构建及结果。
4. 有意留给第二、三期的缺口与任何已知限制。
5. 不要以“已通过等保三级”作为结论。
```

## 交付边界

该提示词对应 [等保三级内核计划](../mlps-level3-kernel-plan.md) 的第一期。执行 agent 需要在完成前自行检查变更是否与 [ADR 0005](../adr/0005-mlps-kernel-assessment-boundary.md) 的已采纳决定一致。
