# 外部登录（SSO）

第一次用企业微信扫码进来的人会被拒绝，报 `OAuthAccountNotBound`。这是默认策略，不是配错了：内核一贯的立场是账号只由管理员开，没有自注册，外部身份也照这条办。要让 SSO 自己开户，得按 provider 显式打开那个开关。

## 三个 provider，两种打包方式

| provider | 装在哪 | 对接方式 |
| --- | --- | --- |
| `oidc` | 内核内置（AspNetCore 层） | 标准 OIDC，通吃 Keycloak、Entra、Authing、Auth0 |
| `wecom` | 卫星包 `TenonAdmin.Auth.WeCom` | 企业微信 PC 扫码 / 网页授权 |
| `dingtalk` | 卫星包 `TenonAdmin.Auth.DingTalk` | 钉钉 PC 扫码 / 网页授权 |

内置 OIDC 零新增依赖：发现文档、JWKS、`id_token` 验签全用 JwtBearer 已经传递进来的 `Microsoft.IdentityModel.*`。两个厂商包各自只引 `Core` 加 Microsoft.\*，裸 `HttpClient` 对接厂商 API，所以它们能独立发版，也不把厂商 SDK 拖进内核。

卫星包按 `AddTenonAdmin()` 之前前置注册的老规矩接进来，按 `Code` 与内置的并存：

```csharp
builder.Services.AddTenonAdminWeComAuth(builder.Configuration);
builder.Services.AddTenonAdminDingTalkAuth(builder.Configuration);
builder.Services.AddTenonAdmin(builder.Configuration);
```

## 配置分两处放

密钥和运营项不放在一起，这是刻意的。

**连接与密钥走 `appsettings`**，和 Database、Jwt、Email 一个路子，密钥不进库：

```jsonc
{
  "TenonAdmin": {
    "ExternalAuth": {
      "Oidc": [ { "Code": "keycloak", "Authority": "...", "ClientId": "...", "ClientSecret": "..." } ]
    }
  }
}
```

`GET /api/v1/auth/external/providers` 只回非密钥字段（code、显示名、图标），够前端点亮按钮就行。

**运营项走 `sys_config`**，配置页上运行时可改，按 provider code 组键：

| 配置键 | 默认 | 管什么 |
| --- | --- | --- |
| `sys.externalauth.{code}.enabled` | 启用 | 这个 provider 开不开 |
| `sys.externalauth.{code}.provisioning` | 拒绝 | 未绑定账号首次登录时开不开户 |
| `sys.externalauth.{code}.defaultRoleIds` | 空 | 自动开户时给什么角色 |
| `sys.externalauth.{code}.defaultOrgId` | 空 | 自动开户时落哪个机构 |

一个键都不配，行为就是**启用 + 拒绝开户**：只碰 `appsettings` 就能跑起绑定优先的 SSO。

这几个键的读取收口在 `ISysUserExternalService`，控制器和 `AuthService` 都只调它，不各自散读配置键。

::: tip 没有 provider 管理页，这是有意的
后端不建 provider 表、不建管理页。厂商密钥本质是部署基建，入库要加密存储、脱敏、再配一套 CRUD，攻击面和工作量都不划算。将来真要一个独立的 Provider 管理页，前端叠一个就行，后端不用动。
:::

## 未绑定的账号怎么办

`sys_user_external` 表按 `(Provider, Subject)` 唯一，记的是「哪个外部身份对应哪个本地用户」。首次外部登录时查不到绑定，走的就是 `provisioning` 那个开关：

- **拒绝**（默认）：抛 `OAuthAccountNotBound`（40016）。用户得先有本地账号，再去个人中心把外部身份绑上。
- **自动开户**（JIT）：建一个本地账号，随机口令占位、不要求改密，角色和机构取上面那两个配置键。

解析这一步是 `virtual` 的。想要「按邮箱自动关联到已有账号」这类策略，覆写它即可，不必改内核。

## 端点

都挂在 `api/v1/auth/external` 下：

| 端点 | 用途 |
| --- | --- |
| `GET providers` | 列可用 provider，前端据此渲染登录按钮 |
| `GET {provider}/authorize` | 换取跳转地址，带上一次性 state |
| `GET {provider}/callback` | 厂商回调落点 |
| `POST exchange` | 用一次性票据换令牌 |
| `GET bindings` | 当前用户已绑定的外部身份 |
| `POST {provider}/bind` | 绑定一个外部身份 |

`state` 和一次性票据都复用短信验证码那套成法：进缓存、`GetAndRemoveAsync` 原子取删，单次有效。

外部登录解析出 `SysUser` 之后，接的是 `AuthService.CreateTokenAsync`——建会话、发令牌这段尾链和账密登录、短信登录完全共用，所以会话并发策略、强退、刷新令牌轮换对它一视同仁。

## 错误码

| 码 | 名 | 什么时候 |
| --- | --- | --- |
| 40013 | `OAuthProviderDisabled` | 这个 provider 被运营开关关了 |
| 40014 | `OAuthStateInvalid` | state 对不上或已被消费 |
| 40015 | `OAuthExchangeFailed` | 向厂商换令牌失败 |
| 40016 | `OAuthAccountNotBound` | 没绑定，且这个 provider 不许自动开户 |
| 40017 | `OAuthAlreadyBound` | 这个外部身份已经绑在别的账号上 |

按[前后端契约](/zh/frontend/api-contract)的规矩，这几个码在两份语言包里都要有对应的 `msgKey` 文案，漏配会让后端的一致性测试直接变红。
