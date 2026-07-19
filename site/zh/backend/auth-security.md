# 认证与安全

三行 `Program.cs` 起来的服务，安全上并不裸奔：登录连错五次账号就锁，口令写进日志前已经打码。真正非配不可的只有一样，生产环境的 JWT 签名密钥，不配它服务直接起不来。其余各项都有默认值，多数还能在运行时用 `SysConfig` 盖过部署期 Options。

上线前逐条核对哪些必须改、哪些能默认放行，交给 [部署指南的安全基线](/zh/guide/deployment/)；那边是核对清单，这边是机制。

## JWT 令牌

访问令牌是短命 JWT，不落库；刷新令牌长期、服务端只存哈希、支持轮换吊销（`Core/Security/ITokenProvider.cs`）。配置在 `TenonAdmin:Jwt`(`AdminJwtOptions`):

| 配置键 | 默认 | 说明 |
| --- | --- | --- |
| `TenonAdmin:Jwt:SecretKey` | `null` | 签名密钥，至少 32 字节 |
| `TenonAdmin:Jwt:Issuer` | `TenonAdmin` | 签发者（`iss` claim） |
| `TenonAdmin:Jwt:ExpireMinutes` | `120` | 访问令牌有效期（分钟） |
| `TenonAdmin:Jwt:RefreshExpireMinutes` | `10080` | 刷新令牌有效期（分钟，7 天） |

**签名密钥有三条路径**（`JwtKeyResolver.cs`）：

- **已配置 `SecretKey`**（生产要求）：直接用；长度不足 32 字节立即抛错拒绝启动。弱密钥可被暴力破解，进而伪造超管令牌。
- **未配置 + 生产环境**：直接抛错、拒绝启动（fail-fast）。生产缺密钥若静默用自动生成的开发密钥，多副本各签各的密钥会导致随机 401，且密钥泄露即可伪造任意令牌。
- **未配置 + 开发环境**：生成 64 字节加密随机密钥并持久化到 `{ContentRoot}/data/dev-jwt.key`，重启后签验密钥不变、已发令牌不失效，同时打印醒目警告。该文件与默认 SQLite 同处 data 目录，天然进 `.gitignore`。

::: warning 生产必配签名密钥
这是安全基线，不是建议。生产环境不显式配置 `TenonAdmin:Jwt:SecretKey` 直接起不来。
:::

**验证参数**（`TenonAdminSetup.cs`）：`MapInboundClaims = false`（保留 `sub`/`sid`/`sadm`/`org` 原始 claim 名）；`ValidateAudience = false`（单体后台不启用 audience）；`ValidateLifetime = true`；`ClockSkew` 收紧为 30 秒（默认 5 分钟，贴合短命令牌）；`NameClaimType = unique_name`（`User.Identity.Name` = 登录账号）。

**访问 / 刷新令牌时长运行时可配**。签发时从 `ISecurityPolicyProvider.GetSessionTtlAsync()` 取有效分钟数，先读 `SysConfig` 再回退 Jwt 默认：

| 运行时配置键 | 回退默认 |
| --- | --- |
| `sys.security.session.accessMinutes` | `Jwt:ExpireMinutes` |
| `sys.security.session.refreshMinutes` | `Jwt:RefreshExpireMinutes` |

## 验证码

登录验证码由 `CaptchaService` + 三个零绘图依赖的内置生成器承担。配置在 `TenonAdmin:Security:Captcha`(`AdminCaptchaOptions`):

| 配置键 | 默认 | 说明 |
| --- | --- | --- |
| `...:Captcha:Enabled` | `false` | 是否启用登录验证码 |
| `...:Captcha:Type` | `char` | `char`（字符 SVG）/ `path`（描边字形）/ `math`（算术） |

**默认关**：三行零配置 API 登录直接能用；账号级登录锁定已挡爆破主向，验证码作浏览器侧的 opt-in 加固，Web 模板/生产按需开。

运行时可用 `SysConfig` 覆盖（改值即时生效）：`sys.security.captcha.enabled`（是否强制校验）、`sys.security.captcha.type`（签发哪种）。缺失时回退 Options 默认。

**一次性票据**（`CaptchaService.cs`）：明文进缓存（2 分钟 TTL），签发时用 GUID v7 作票据 Id；校验时**原子取删**：

```csharp
// 并发携同一 captchaId 时只有一个调用取到非空值,杜绝单张验证码放大成 N 次猜测
var stored = await cache.GetAndRemoveAsync<string>(CacheKeys.Captcha(captchaId!));
AdminException.ThrowIf(stored is null, ErrorCode.CaptchaExpired);   // 40002
AdminException.ThrowIf(!string.Equals(stored, code, StringComparison.OrdinalIgnoreCase),
    ErrorCode.CaptchaWrong);   // 40003
```

无论对错该票据都作废，同一张验证码不能重放或多次猜测。图片/滑块/行为验证码可自注册 `ICaptchaProvider` 前置替换。

## 登录锁定（防爆破）

`LoginLockService` 在**登录最前置**调用，先于账密校验，所以锁定期间正确密码也进不来。配置在 `TenonAdmin:Security:LoginLock`(`AdminLoginLockOptions`):

| 配置键 | 运行时键 | 默认 | 说明 |
| --- | --- | --- | --- |
| `...:LoginLock:MaxFailCount` | `sys.security.loginLock.maxFailCount` | `5` | 连续密码错误多少次后锁定；`<=0` 关闭 |
| `...:LoginLock:LockMinutes` | `sys.security.loginLock.lockMinutes` | `10` | 锁定时长，也是失败计数的滑动过期窗口 |

失败计数存缓存，原子自增并刷新 TTL：持续失败则窗口顺延，停手到 `LockMinutes` 后计数过期自动解锁。**只有「密码错误」计入失败锁定**。验证码错、已锁定、停用等都不累加，避免无限延长锁定窗口或误伤（`AuthService.OnLoginFailedAsync`）。

::: tip 账号规范化对齐数据库
锁定计数的 key 先做规范化（去首尾空白 + 转小写），等价类必须 ≥ 数据库匹配的等价类。否则大小写/尾空白变体（MySQL `utf8mb4_0900_ai_ci` / PAD SPACE 命中同一行）会拆成独立计数器，让攻击者绕过锁定无限猜测。
:::

**堵死账号枚举**（`AuthService.ValidateUserAsync`）：「账号不存在」与「密码错误」统一抛 `ErrorCode.PasswordWrong`，响应内容因此不可区分。账号不存在时还会执行一次等价代价的陪跑哈希，响应耗时于是也不可区分，两条通道一起堵。

## 会话与强制下线

会话由 `SessionService` 管理：库里存的那份是源，缓存里那份只为省掉热路径上的一次查询。刷新令牌只存 SHA-256 哈希，时间统一走 UTC。登录时用 GUID v7 生成 `sessionId`，写进令牌的 `sid` claim，作为在线用户列举与强退的稳定锚点。

**强退即时生效**。授权管道每请求校验 `sid` 对应会话是否仍活跃（见 [请求管线](/zh/backend/request-pipeline) 第 ② 步）。管理员在「在线用户」里踢人时：

```csharp
public virtual async Task RevokeAsync(string sessionId)
{
    // 标记会话行 RevokedAt
    // 标记刷新令牌 Status = Revoked
    await cache.RemoveAsync(CacheKeys.Session(sessionId));   // 缓存移除 → 下次校验查库得吊销 → 401
}
```

被踢用户手里的 access token 哪怕未到期，下一个请求就会 401。停用/删除用户走 `RevokeAllForUserAsync`，下线其全部会话。

**并发策略**（`TenonAdmin:Security:Session`、`AdminSessionOptions`）：

| 配置键 | 默认 | 说明 |
| --- | --- | --- |
| `...:Session:Mode` | `Multi` | `Multi`（多端并存）/ `Single`（新登录踢旧） |
| `...:Session:MaxConcurrent` | `0` | 最大并发会话数；`>0` 时超出按最早登录吊销最旧；`0` 不限 |

名额收敛采「**先插入、再收敛**」：新会话插库后才收敛，并发的两个登录都看得见对方的行、都算出同一个「只保留最新 N 个」的答案，天然收敛。它不靠进程内锁，因而跨得了多副本；换成进程内锁，多副本下单端就踢不掉旧会话。

**刷新令牌复用检测**（`SessionService.RefreshAsync`）：已轮换令牌再现 = 重放，吊销整个会话（攻击者与真用户一起下线，安全优先）；轮换用条件更新（仅当仍 `Active` 才置 `Used`）兼作并发保护。

## 密码策略

`SecurityPolicyProvider.GetPasswordPolicyAsync()` 每个值先读 `SysConfig` 再回退默认：

| 运行时配置键 | 默认 |
| --- | --- |
| `sys.security.password.minLength` | `8` |
| `sys.security.password.requireUpper` | `true` |
| `sys.security.password.requireLower` | `true` |
| `sys.security.password.requireDigit` | `true` |
| `sys.security.password.requireSpecial` | `false` |

不满足时抛 `ErrorCode.PasswordTooWeak`，`args` 携带各项要求供前端提示。密码用 PBKDF2 哈希（`Pbkdf2PasswordHasher`）。

**口令时效（默认关）**。复杂度之外还有一条运行时可配的过期策略：`sys.security.password.expireDays`（种子默认 `0` = 永不过期，>0 才启用）。登录第 4 步 `AuthService.CheckPasswordExpiryAsync` 拿 `SysUser.LastPasswordChangeTime` 加上有效天数比当前时间。**过期不拦登录**：只把该用户的 `MustChangePassword` 置真落库，并随登录出参回传前端，由前端强制跳改密页。这和管理员重置密码是同一个信号。自助改密成功会刷新 `LastPasswordChangeTime`、清掉标志，过期窗口从头计。

`LastPasswordChangeTime` 是后加字段，存量用户可能为 null。真判过期前会先给 null 锚点回填当前时间，窗口从升级后首次登录起算。不这样做，开启策略的当天就会有一批没有锚点的老用户被一起判过期、集体卡在改密页。替换过 `ISecurityPolicyProvider` 的二开代码不受影响：新增的 `GetPasswordExpireDaysAsync` 带一个返回 0 的默认接口实现，旧实现不改也编得过、等同于关闭。

**默认初始口令**（`TenonAdmin:Security:DefaultInitialPassword`）：默认 `null` → 新建用户/重置密码时按账号生成密码学随机强口令，杜绝「随公开 NuGet 包分发的固定默认口令」这一已知凭据弱点。重置密码会把随机口令返回给管理员当场转达。

::: tip 首次启动的超管口令
配了 `TenonAdmin:Seed:AdminPassword` 用配置值；没配（默认）则随机生成并**在启动日志醒目打印一次**。也就是说，只有真正建号的那次启动会打印，后续启动不再打印（打印一个已失效的随机密码只会误导人）。
:::

## 请求限流

按**客户端 IP** 固定窗口限流，经内置 `IStartupFilter` 挂载 `UseRateLimiter`，无需手动接中间件。配置在 `TenonAdmin:Security:RateLimit`(`AdminRateLimitOptions`):

| 配置键 | 运行时键 | 默认 | 说明 |
| --- | --- | --- | --- |
| `...:RateLimit:Enabled` | `sys.security.rateLimit.enabled` | `true` | 部署期硬总开关；`false` 时无论 DB 配置都不限流 |
| `...:RateLimit:WindowSeconds` | `sys.security.rateLimit.windowSeconds` | `60` | 窗口长度（秒） |
| `...:RateLimit:PermitPerWindow` | `sys.security.rateLimit.permitPerWindow` | `300` | 全局：单 IP 每窗口请求数（挡洪泛） |
| `...:RateLimit:AuthPermitPerWindow` | `sys.security.rateLimit.authPermitPerWindow` | `20` | 认证端点（`/api/v1/auth/*`）更严一档，挡在线爆破 |

`Enabled` 是部署期硬总开关；为 `true` 时实际开关与阈值由 `SysConfig` 运行时调控。

::: warning 反向代理后取的是代理 IP
上正式网关时需先接 `ForwardedHeaders` 中间件解析 `X-Forwarded-For`，否则同代理后所有客户端共用一个限流分区。
:::

## 演示模式（只读展示）

想把系统挂成一个谁都能进来点、却改不动数据的对外演示站，打开 `TenonAdmin:DemoMode`（默认 `false`）：

```jsonc
// appsettings.json —— 或环境变量 TenonAdmin__DemoMode=true
{ "TenonAdmin": { "DemoMode": true } }
```

只有此时内核才注册全局授权过滤器 `DemoModeFilter`，按 HTTP 方法放行：GET/HEAD/OPTIONS 照常读；`/api/v1/auth/*`（登录、登出、刷新）也放行，否则连登录都做不了；其余 POST/PUT/PATCH/DELETE 一律回 HTTP 403，信封码 `41002`(`DemoModeReadOnly`)，前端据码弹只读提示。

它拦的是「写」这个动作本身，和角色权限无关。判断落在授权阶段、只看请求方法，所以超管进来照样改不动。要放行个别写接口（比如演示站自己的留言反馈），别用这个总开关，在业务侧单独处理。

## 日志脱敏

操作日志默认记一切写操作（读操作/匿名端点除外）。入参在写库前脱敏，避免明文口令随日志落库（`SensitiveDataMasker.cs`，由 `OperationLogFilter` 调用）：

```csharp
var paramJson = SensitiveDataMasker.Mask(context.ActionArguments);
```

**按字段名脱敏，不看值**：任何名字含 `password` / `pwd` / `secret` / `token` / `credential` 的属性（大小写不敏感、子串匹配，`newPassword`、`access_token` 都命中）其值被替换为 `***`，递归处理嵌套对象与数组。序列化失败（含 `IFormFile` 等不可序列化入参）不阻断请求，记占位串 `<unserializable>`。

登录日志（`AuthService`）记原始输入账号（哪怕账号不存在）+ 具体失败码，供暴力破解/账号探测排查；IP/UA 由日志服务从当前请求补全。**绝不记密码**。
