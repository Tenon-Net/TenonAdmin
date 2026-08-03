# ADR 0002 — 批次 D:外部登录 / SSO 的三处取舍(未绑定默认拒绝、配置折中、含卫星包)

- 状态:已采纳(2026-07-18)
- 相关:`docs/refinement-ledger.md` 批次 D;`skills/replace-service`(消费者替换成法);[[ADR-0001]] 同属精致化台账决策存档

## 背景

登录页早有企业微信 / 钉钉 / SSO 三个占位按钮(`LoginForm.vue`,原只 toast "coming soon"),但全栈无任何外部登录代码。批次 D 把 SSO 从占位变为可用,并把"外部身份 provider"做成替换点(镜像 `ISmsSender` / captcha 通道成法)。开工前用 `/grill-with-docs` 做了一轮设计审问 + 三路代码探查 + `Microsoft.IdentityModel` 官方文档核对,三处取舍与用户逐一确认后定稿,记此防反复。

架构落点(探查已锚定):重接缝 = `AuthService.CreateTokenAsync`(外部登录解析出 `SysUser` 后复用建会话/发令牌尾链,同 `LoginByPhoneAsync`);一次性票据复用 `SmsOtpService` 的 `MfaChallenge` 成法(`CacheKeys.OAuthState/OAuthTicket` + `GetAndRemoveAsync` 原子单消费);绑定表 `SysUserExternal : BaseEntity`((Provider, Subject) 唯一),CodeFirst 零 DI 自动建表;ErrorCode 40013–40017。

## 决策一:未绑定账号默认策略 = 可配置,默认拒绝(Q1)

**默认拒绝。** 首次外部登录若 `sys_user_external` 无绑定 → 抛 `OAuthAccountNotBound`(40016),须先有本地账号并在个人中心绑定。提供 per-provider 开关"自动开户"(JIT):开启后首次登录建本地账号(随机口令占位、免改密、落配置的默认角色/机构)。解析步骤 `virtual`,消费者可覆写成 link-by-email 等。

理由:安全默认契合内核既有"账号只由管理员开、无自注册"的立场;但企业 SSO 的核心价值是 JIT,故不是"一律拒绝",而是把开户能力作为 per-provider 运营开关内建(只比纯拒绝多约 15 行,复用 `UserService.AddAsync` 成法)。默认拒绝 → 开箱最安全;要 JIT 的企业改一处运营配置即可,不必 fork。

## 决策二:配置形态 = 折中(Q2)

**连接与密钥走 appsettings,运营项走 `sys_config`。**

- **连接/密钥**(OIDC 的 authority/clientId/clientSecret、企业微信 corpId/corpSecret、钉钉 appKey/appSecret)→ appsettings `TenonAdmin:ExternalAuth`,与 Database/Jwt/Email/Sms 一个路子,**密钥不进库**;`GET providers` 只回非密钥字段(code/名称/图标)点亮按钮。
- **运营项**(启用开关、未绑定策略、开户默认角色/机构)→ 复用现有 `sys_config` + 配置页 + 批次 C 缓存失效,按 provider code 键 `sys.externalauth.{code}.{enabled|provisioning|defaultRoleIds|defaultOrgId}`,经 `IConfigService` 读穿透缓存,收口在 `ISysUserExternalService`(控制器与 AuthService 都只调它,不散读键)。

理由:OIDC/厂商密钥本质是部署基建而非业务数据,入库要加密存储 + 脱敏 + CRUD 页,面大且多攻击面;而"是否启用/怎么开户"是运营态,值得运行时可改。**不新建 provider 表、不新建管理页**;要漂亮的独立 Provider 管理页,后续叠一个前端页即可,后端不动。默认无 config 键 = 启用 + 拒绝开户 → 开箱只碰 appsettings 就能跑绑定优先 SSO。

## 决策三:范围 = 含企业微信 / 钉钉卫星包(Q3)

**做,按 PC 扫码 / 网页授权(OAuth2 授权码式,非 H5 免登)。**

内核只装内置 `OidcExternalAuthProvider`(AspNetCore 层,**零新包**——发现文档 / JWKS / id_token 验签全用 JwtBearer 已传递的 `Microsoft.IdentityModel.*`,通吃 Keycloak/Entra/Authing/Auth0)。企业微信 / 钉钉走**独立卫星包** `TenonAdmin.Auth.WeCom` / `.DingTalk`(照 `TenonAdmin.Caching.Redis` 成法:仅引 Core + Microsoft.*,裸 HttpClient 对接厂商 API,消费者 `AddTenonAdmin()` 前调 `AddTenonAdminXxxAuth` 前置注册,按 `Code` 与内置并存,独立发包节奏)。

## 约定与后果

- **回调路由约定**:前端固定 `/oauth/callback`(公开路由),与后端 `TenonAdmin:ExternalAuth:FrontendResultPath` 默认值对齐;后端回调机密客户端换会话后 302 回该页,**令牌不进 URL**,只带一次性 `ticket`(或 `bind` / `error` 码),前端 `POST /auth/external/exchange` 换令牌对。前后端分离(dev)时把 `FrontendResultPath` 配成前端完整 URL、`CallbackBaseUrl` 配成后端公网基址。
- **个人中心绑定** 走 `[ActiveSession]`,免菜单/权限种子;入口在顶栏用户下拉。
- **卫星包待真机验证**:WeCom/DingTalk 的 token 交换/取用户逻辑按厂商文档实现,但未对真实企业应用联调过;`BuildAuthorizeUrlAsync` 纯字符串,`ExchangeAsync` 的网络+解析部分需接真实厂商应用验证。内置 OIDC 已用配置化 provider + 假 provider 单测覆盖闭环(未绑定拒绝/开户/已绑复用/未知 provider/绑定唯一)。
- **企业微信 `corpsecret` 走 query**:`gettoken` 是企业微信的 GET API 契约(secret 进请求行),非本设计选择;走 TLS 到 `qyapi.weixin.qq.com` 传输中不可截,风险仅在 URL 易被沿途代理/访问日志记全。卫星包自身日志只记 status+body(不落 URL);部署侧勿对该域记完整请求行。无代码可改。
- **复查加固(第 12 轮)**:双 reviewer 对抗审后,补两处 Medium——① 删用户连带清外部绑定(否则孤儿绑定占唯一位使外部身份永久锁死);② login 模式 `state` 用 `HttpOnly;SameSite=Lax` binder cookie 绑定发起浏览器,防登录 CSRF(bind 模式已由 `[ActiveSession]` UserId 兜住)。另 fail-closed 收口:生产强制 OIDC https 元数据、生产必配 `CallbackBaseUrl`(不回退请求 Host)。核心令牌/身份校验面审后无改。
- 内核认证能力 = 账密 + 短信免密 + 短信 MFA + **外部登录 / SSO**;字段级审计仍交消费者按需扩展([[ADR-0001]] 决策一)。
- **后续(不改本 ADR 三处取舍)**:登录/绑定品牌化 UI 与 GitHub、个人微信卫星包见 `docs/external-login-brand/`（[[ADR-0007]] 指针 → 同目录 `decisions.md` + `ledger.md`）。
