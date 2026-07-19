# 认证与安全

三行代码跑起来的服务，安全是不是就是一张白纸？并不是。登录连错五次账号就锁，密码写进日志之前已经打上了码。真正躲不过去的只有一件事：生产环境必须配 JWT 签名密钥。不配，服务直接起不来。剩下的每一项都有默认值。大半还能在运行时用 `SysConfig` 覆盖部署期的 Options。

上线前该核对哪些必须改、哪些能放着用默认值？交给[部署指南的安全基线](/zh/guide/deployment/)做清单。这一页讲的是这些机制本身怎么运作。

## JWT 令牌

访问令牌图的是短平快：一个短命 JWT，签发之后不落库。真正需要长期看管的是刷新令牌。服务端只存它的哈希，支持轮换和吊销，这套逻辑在 `Core/Security/ITokenProvider.cs` 里。配置项都在 `TenonAdmin:Jwt`（`AdminJwtOptions`）下：

| 配置键 | 默认 | 说明 |
| --- | --- | --- |
| `TenonAdmin:Jwt:SecretKey` | `null` | 签名密钥，至少 32 字节 |
| `TenonAdmin:Jwt:Issuer` | `TenonAdmin` | 签发者（`iss` claim） |
| `TenonAdmin:Jwt:ExpireMinutes` | `120` | 访问令牌有效期（分钟） |
| `TenonAdmin:Jwt:RefreshExpireMinutes` | `10080` | 刷新令牌有效期（分钟，7 天） |

这把签名密钥从哪来？三条路径任选一条，判定逻辑在 `JwtKeyResolver.cs` 里：

- **配了 `SecretKey`**（生产环境必须配这个）：直接用。但长度不够 32 字节就报错，拒绝启动。密钥一弱就能被暴力破解，进而伪造超管令牌。
- **没配 + 生产环境**：同样拒绝启动。为什么不用自动生成的开发密钥先顶上？因为多副本部署时各签各的，会导致用户随机 401；而且密钥一泄露，就能伪造任意令牌。
- **没配 + 开发环境**：自动生成 64 字节密钥，存在 `{ContentRoot}/data/dev-jwt.key`。重启也不换，令牌不失效，同时控制台会提醒这是临时密钥。这个文件和 SQLite 数据库同住 data 目录，天然被 `.gitignore` 挡在外面。

::: warning 生产必配签名密钥
这是安全基线，不是随口建议：生产环境不显式配置 `TenonAdmin:Jwt:SecretKey`，服务直接起不来。
:::

验证参数上有几处特意调整过，和框架默认值不一样。改动都在 `TenonAdminSetup.cs` 里：

- `MapInboundClaims = false`：保留 `sub`/`sid`/`sadm`/`org` 这些原始 claim 名，不被框架偷偷改名。
- `ValidateAudience = false`：单体后台用不上 audience 校验。
- `ValidateLifetime = true`：过期的令牌照样拒。
- `ClockSkew` 收紧到 30 秒（默认是 5 分钟）：贴合短命令牌的节奏。
- `NameClaimType = unique_name`：`User.Identity.Name` 取的就是登录账号。

访问令牌和刷新令牌各自能活多久，不是写进配置文件就一锤定音。签发时会先看 `ISecurityPolicyProvider.GetSessionTtlAsync()`。这个方法先查 `SysConfig`，查不到才回退到 Jwt 的默认值。

| 运行时配置键 | 回退默认 |
| --- | --- |
| `sys.security.session.accessMinutes` | `Jwt:ExpireMinutes` |
| `sys.security.session.refreshMinutes` | `Jwt:RefreshExpireMinutes` |

## 验证码

登录要不要加一道验证码？`CaptchaService` 内置了三种生成器，都不依赖绘图库。配置在 `TenonAdmin:Security:Captcha`（`AdminCaptchaOptions`）下：

| 配置键 | 默认 | 说明 |
| --- | --- | --- |
| `...:Captcha:Enabled` | `false` | 是否启用登录验证码 |
| `...:Captcha:Type` | `char` | `char`（字符 SVG）/ `path`（描边字形）/ `math`（算术） |

默认是关的。为什么敢关？三行代码跑起来的 API，得直接就能登录。账号级的登录锁定已经挡住了暴力破解的主攻方向。验证码只是浏览器侧再加一道保险，Web 模板和生产环境按需自己打开。

运行时还能用 `SysConfig` 覆盖，改完立即生效，不用重启。`sys.security.captcha.enabled` 管要不要强制校验，`sys.security.captcha.type` 管发哪一种。缺配置时都回退到 Options 默认值。

验证码票据只用一次。明文进缓存，2 分钟 TTL。签发时拿 GUID v7 当票据 Id。校验时玩的是**原子取删**（逻辑在 `CaptchaService.cs`）：

```csharp
// 并发携同一 captchaId 时只有一个调用取到非空值,杜绝单张验证码放大成 N 次猜测
var stored = await cache.GetAndRemoveAsync<string>(CacheKeys.Captcha(captchaId!));
AdminException.ThrowIf(stored is null, ErrorCode.CaptchaExpired);   // 40002
AdminException.ThrowIf(!string.Equals(stored, code, StringComparison.OrdinalIgnoreCase),
    ErrorCode.CaptchaWrong);   // 40003
```

不管填对填错，这张票据用一次就作废。同一张验证码，想重放或者多试几次都不行。想换成图片、滑块或者行为验证码？自己注册一个 `ICaptchaProvider` 提前接管就行。

## 短信验证（二次验证与免密登录）

短信能干两件不相关的事：登录时加一道二次验证，或者干脆免密码、靠短信登录。两个开关互相独立，**默认都关着**。在配置中心「安全策略」页运行时切换，改完立即生效，不用重启：

| 运行时配置键 | Options 兜底 | 默认 | 功能 |
| --- | --- | --- | --- |
| `sys.security.mfa.enabled` | `...:SmsOtp:MfaEnabled` | `false` | 短信二次验证：密码通过后再验一次短信码 |
| `sys.security.smsLogin.enabled` | `...:SmsOtp:LoginEnabled` | `false` | 免密登录：手机号 + 短信验证码 |

二次验证怎么走？密码这一侧全部过关之后（锁定 → 验证码 → 账密 → 策略），`AuthService.CheckSmsSecondFactorAsync` 才会接手。它开一张缓存挑战票据，**绑定的是用户 id，不是手机号**，然后发码。前端收到的是 `SmsCodeRequired`（40009）这个信令，意思是「还差一步」，`args` 里带着 `challengeId`/`phoneMask`/倒计时这些参数。后半程走 `POST /api/v1/auth/login/sms`（以及 `/resend`）。`/login` 接口本身的请求/响应契约没有变。40009 只是个信令，不是失败，也不计入登录锁定的失败次数。

::: warning 二次验证只对绑定了手机号的用户生效
开关打开了，没绑手机号的用户会不会登不进去？不会，**照样凭密码直登**。这是故意的，不是漏做。全局开关一旦打开，就绝不能把任何人锁在系统外面。种子超管本来就没有手机号，存量用户里没绑手机号的也大有人在。想让某个账号必须走二次验证？给它绑一个手机号就行，个人资料页或者用户管理里都能绑。

如果你想要更严格的语义，「没手机号就不让登」，覆写一步就够：

```csharp
public sealed class StrictAuthService(
    IRepository<SysUser> users, IPasswordHasher hasher, ITokenProvider tokens,
    ISessionService sessions, ILogService logService, ILoginLockService loginLock,
    ICaptchaService captcha, ISecurityPolicyProvider policy, ISmsOtpService smsOtp)
    : AuthService(users, hasher, tokens, sessions, logService, loginLock, captcha, policy, smsOtp)
{
    protected override async Task CheckSmsSecondFactorAsync(SysUser user)
    {
        // 内核默认对无手机号用户直通;严格模式改为拒登
        if (await smsOtp.IsMfaEnabledAsync() && string.IsNullOrWhiteSpace(user.Phone))
            throw new AdminException(ErrorCode.AccountDisabled);
        await base.CheckSmsSecondFactorAsync(user);
    }
}
```
:::

**免密登录呢？** `POST /api/v1/auth/sms/send` 发码，`POST /api/v1/auth/sms/login` 拿手机号加验证码换令牌。发码这一步会联动图形验证码的开关：验证码开着时，发码端点也一并受保护。**防的是枚举**：不管手机号是不存在、重复还是被停用，拿到的响应外形都和真实路径一模一样，连冷却都照做，只是从来不会真的发码。后续校验统一报 `SmsCodeExpired`，不告诉你到底是哪种情况。

有一个副作用得提前知道：**手机号重复的用户会被免密登录静默排除**。原因是解析逻辑要求手机号必须唯一命中一个启用用户。内核没有给 `Phone` 字段加唯一索引，存量数据可能本来就有重复。要用这个功能，请自己在录入侧保证手机号唯一。

防滥用这件事全交给服务端强制，前端管不了。配置在 `TenonAdmin:Security:SmsOtp`（`AdminSmsOtpOptions`）下，这是部署期配置，只有两个开关是运行时键：

| 配置项 | 默认 | 说明 |
| --- | --- | --- |
| `CodeLength` | `6` | 验证码位数（密码学随机） |
| `TtlSeconds` | `300` | 码（及 MFA 挑战）有效期 |
| `ResendSeconds` | `60` | 同号重发冷却，二次验证与免密发码共享 |
| `MaxAttempts` | `5` | 错码次数上限，达到即作废该码 |
| `DailySendLimitPerPhone` | `10` | 同号每日发送上限（防短信轰炸/费用失控） |

验证码的消费方式和图形验证码一个套路：**原子取删**，用一次就废，防重放。而且**只存缓存**，不建表，零 DDL。冷却时间和每日计数，默认内存缓存下按实例各算各的，和登录锁定一个道理。装上 Redis 包，就变成全局共享。

谁来真正发这条短信？内核只定义了一个 `ISmsSender` 接口，自己不接厂商。默认实现是 `LoggingSmsSender`，把验证码写进后端日志，开发阶段够用，生产环境显然不能这么干。在 `AddTenonAdmin()` **之前**注册一个真实厂商的实现就能接管。`purpose` 参数是 `mfa` 还是 `login`，映射到厂商那边的模板 id：

```csharp
builder.Services.AddSingleton<ISmsSender, AliyunSmsSender>();   // 你的实现
builder.Services.AddTenonAdmin(builder.Configuration);          // TryAdd 让位
```

涉及的错误码汇总一下：`SmsCodeRequired` 40009（信令）、`SmsCodeWrong` 40010（args 带 `attemptsLeft`）、`SmsCodeExpired` 40011（缺失/过期/已消费/次数耗尽，刻意不可区分）、`SmsLoginDisabled` 40012；发送过频复用 `TooManyRequests` 40008。

## 邮件通道

邮件呢？内核也带一个类似的抽象，`IEmailSender`，和 `ISmsSender` 一个路数。眼下**没有任何内置功能真的在用它**。这是先立好的通道，等以后邮件验证码登录、通知邮件这类功能落地时直接拿来用，不用再补一次可替换性设计。

配置在 `TenonAdmin:Email`（`AdminEmailOptions`）下，只看一个字段就决定用哪个实现：

| `Host` | 选中的实现 | 行为 |
| --- | --- | --- |
| 空（默认） | `LoggingEmailSender` | 把收件人/主题写进后端日志，不真正发信。开发期看得到内容，生产没用 |
| 非空 | `SmtpEmailSender` | BCL `System.Net.Mail` 直连 SMTP（STARTTLS，默认端口 587） |

`SmtpEmailSender` 的天花板，就是 BCL 自带的 `SmtpClient` 能做到的那些：基础 SMTP 没问题，但撑不住 OAuth2 SMTP，也接不了云厂商自己的 API。要这些能力，自己实现一个 `IEmailSender` 就行，比如接 MailKit。在 `AddTenonAdmin()` 之前注册，就能接管。这条替换路径已经进了回归测试，不用担心以后升级内核会把它顶掉：

```csharp
builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();  // 你的实现,TryAdd 让位
builder.Services.AddTenonAdmin(builder.Configuration);
```

## 登录锁定（防爆破）

账号被锁定的时候，就算密码输对了也进不去。原因是 `LoginLockService` 卡在**登录最前面**，比账密校验还靠前。配置在 `TenonAdmin:Security:LoginLock`（`AdminLoginLockOptions`）下：

| 配置键 | 运行时键 | 默认 | 说明 |
| --- | --- | --- | --- |
| `...:LoginLock:MaxFailCount` | `sys.security.loginLock.maxFailCount` | `5` | 连续密码错误多少次后锁定；`<=0` 关闭 |
| `...:LoginLock:LockMinutes` | `sys.security.loginLock.lockMinutes` | `10` | 锁定时长，也是失败计数的滑动过期窗口 |

失败次数存在缓存里，原子自增并刷新 TTL。只要一直在错，锁定窗口就跟着往后顺延。一旦停手，过了 `LockMinutes`，计数自动过期解锁。**但只有「密码错误」才计入失败次数**。验证码填错、账号已经锁定、账号已停用，这些都不算。不然锁定窗口能被无限拉长，还可能误伤本来没做错事的人。这段逻辑在 `AuthService.OnLoginFailedAsync` 里。

::: tip 账号规范化对齐数据库
锁定计数用的 key 要先规范化：去首尾空白，转小写。而且这个等价类必须不小于数据库自己认的等价类。不然大小写或者尾部空白的变体（MySQL 的 `utf8mb4_0900_ai_ci` / PAD SPACE 命中的是同一行）会被拆成两个独立的计数器。攻击者靠着这点差异，就能绕开锁定一直猜下去。
:::

怎么防止有人靠登录接口探测「这个账号存不存在」？不管是账号本身不存在，还是账号存在但密码错了，统一抛 `ErrorCode.PasswordWrong`，响应内容看不出区别。账号不存在的时候，后端还会陪跑一次等价代价的哈希计算，响应耗时也就看不出区别。两条能被拿来试探的通道，一起堵上。这段逻辑在 `AuthService.ValidateUserAsync` 里。

## 会话与强制下线

会话谁说了算？`SessionService` 管着。数据库里那份是源头，缓存里那份纯粹是为了省掉热路径上的一次查询，两者不对等。刷新令牌只存 SHA-256 哈希，不存明文。时间统一走 UTC。登录的时候用 GUID v7 生成一个 `sessionId`，写进令牌的 `sid` claim 里。以后列举在线用户、强制下线，都靠这个锚点。

管理员在「在线用户」里点一下踢人，是不是要等令牌自然过期才生效？不用，**强退是即时生效的**。授权管道在每个请求里都会校验 `sid` 对应的会话是不是还活着，见[请求管线](/zh/backend/request-pipeline)第 ② 步。踢人这一下具体做了什么？

```csharp
public virtual async Task RevokeAsync(string sessionId)
{
    // 标记会话行 RevokedAt
    // 标记刷新令牌 Status = Revoked
    await cache.RemoveAsync(CacheKeys.Session(sessionId));   // 缓存移除 → 下次校验查库得吊销 → 401
}
```

被踢的人手里那张 access token 就算还没过期，下一个请求照样 401。停用或者删除用户走的是 `RevokeAllForUserAsync`，一次性把这个人名下所有会话全部下线。

**并发策略**（`TenonAdmin:Security:Session`、`AdminSessionOptions`）：

| 配置键 | 默认 | 说明 |
| --- | --- | --- |
| `...:Session:Mode` | `Multi` | `Multi`（多端并存）/ `Single`（新登录踢旧） |
| `...:Session:MaxConcurrent` | `0` | 最大并发会话数；`>0` 时超出按最早登录吊销最旧；`0` 不限 |

同一个人挤爆并发会话上限，该踢谁？这里用的是「**先插入、再收敛**」：新会话先插进数据库，收敛动作在那之后才做。这样一来，并发发生的两个登录都能看到对方那一行，各自算出的都是同一个「只保留最新 N 个」的答案，自然收敛到一致结果，不需要额外协调。为什么不用进程内锁？锁只在单个进程里有效。换成多副本部署，一个副本锁着，另一个副本照样能把同一个名额抢走，锁挡不住跨副本的并发。

刷新令牌用过一次之后再出现，说明什么？只有一种解释：重放。处理很干脆：直接吊销整个会话，哪怕因此把真正的用户也一起下线，安全优先。轮换这一步用的是条件更新，只有当前状态还是 `Active` 才会被置成 `Used`，顺带也当了一层并发保护。这段逻辑在 `SessionService.RefreshAsync` 里。

## 密码策略

密码要多复杂才算数？`SecurityPolicyProvider.GetPasswordPolicyAsync()` 每一项都先读 `SysConfig`，读不到才回退默认值：

| 运行时配置键 | 默认 |
| --- | --- |
| `sys.security.password.minLength` | `8` |
| `sys.security.password.requireUpper` | `true` |
| `sys.security.password.requireLower` | `true` |
| `sys.security.password.requireDigit` | `true` |
| `sys.security.password.requireSpecial` | `false` |

不满足哪一条，就抛 `ErrorCode.PasswordTooWeak`，`args` 里把具体要求都带给前端去提示用户。密码本身用 PBKDF2 哈希存（`Pbkdf2PasswordHasher`）。

**密码会不会过期？** 复杂度要求之外，还有一条运行时可配的过期策略，默认是关的：`sys.security.password.expireDays`。种子默认 `0`，代表永不过期，大于 0 才启用。登录第 4 步会拿 `SysUser.LastPasswordChangeTime` 加上这个有效天数，和当前时间比一下，这段逻辑在 `AuthService.CheckPasswordExpiryAsync` 里。**但过期不拦登录**，只是把这个用户的 `MustChangePassword` 标记为真、落库，再随登录出参一起回传给前端，由前端强制跳到改密页。这和管理员主动重置密码，用的是同一个信号。自助改密成功之后，`LastPasswordChangeTime` 会刷新，标志会清掉，过期窗口从头重新计。

这里有个坑得注意：`LastPasswordChangeTime` 是后加的字段，存量用户身上很可能是 null。真正判过期之前，系统会先给这些 null 锚点回填成当前时间，过期窗口从升级后的首次登录才开始算。不这么处理会怎样？开启策略的当天，一大批没有锚点的老用户会被一起判定过期，集体卡在改密页上，等于误伤全体存量用户。已经替换过 `ISecurityPolicyProvider` 的二开代码不受影响。新增的 `GetPasswordExpireDaysAsync` 带了一个默认接口实现，默认返回 0。旧实现不改也能编译通过，效果等同于关闭这条策略。

**改密码的时候，允许改成上一个用过的密码吗？** 默认不行，但能开。把 `sys.security.password.historyCount` 调成 N，系统就会记住最近 N 个用过的口令。种子默认是 `0`。改密时拿新口令挨个比一下，撞上了就拒，抛 `ErrorCode.PasswordReused`（42025）。「当前口令」要单独判一次，为什么？历史表刚打开的时候是空的，光靠历史记录挡不住「改成当前正在用的这个」这种打擦边球的操作。`IPasswordHistoryService` 只存哈希，复用的是 `SysUser.Password` 那一套 `IPasswordHasher`。校验时逐条 `Verify(明文,哈希)`。每次写入之后，立刻把这个用户的历史记录裁到最新 N 条，多余的硬删掉，表不会随时间无限膨胀。

写入的位置有三处：自助改密（`PersonalService`），管理员建号，管理员重置密码（后两者都在 `UserService`）。后两处只记录、不校验：管理员指定的初始口令，不受「不能与历史重复」这条约束。这三处有一个共同的写法：都把 `IPasswordHistoryService` 声明成**默认为 `null` 的可选构造参数**：

```csharp
public class PersonalService(
    /* ...既有依赖... */
    IPasswordHistoryService? passwordHistory = null) : IPersonalService
{
    // 策略关闭或消费方未注入时,?. 直接短路成空操作,不抛也不查
    await (passwordHistory?.EnsureNotReusedAsync(userId, input.NewPassword) ?? Task.CompletedTask);
}
```

这么写是专门为可替换性让路的。参数带了默认值，继承 `PersonalService`/`UserService` 的二开子类，主构造器**不用跟着改**，旧的调用点照样编译通过，效果等同于「历史策略关闭」。反过来想，要是这里改成必需参数会怎样？内核往后每加一个可选的安全策略，所有下游子类的构造函数签名就得跟着改一遍。这个代价，谁都不想付。

**新建用户或者重置密码，给的初始口令从哪来？** 配置项是 `TenonAdmin:Security:DefaultInitialPassword`，默认是 `null`。这时候系统会按账号现生成一个密码学随机的强口令，不用写死的默认密码。为什么较真到这个地步？「随公开 NuGet 包分发一个固定默认口令」是一个已知的凭据弱点，谁都能翻源码找到它，等于给每个用这个内核的项目开了同一把后门钥匙。重置密码的时候，这个随机口令会原样返回给管理员，由管理员当场转达给用户。

::: tip 首次启动的超管口令
配了 `TenonAdmin:Seed:AdminPassword` 就用配置里给的值；没配（默认情况）就随机生成一个，并且**在启动日志里醒目打印一次**。只有真正建号的那一次启动才会打印，后面再启动就不打了。打印一个已经失效的随机密码只会误导人，没有意义。
:::

## 请求限流

限流按的是**客户端 IP**，固定窗口算法。靠内置的 `IStartupFilter` 自动挂上 `UseRateLimiter`，不用手动接中间件。配置在 `TenonAdmin:Security:RateLimit`（`AdminRateLimitOptions`）下：

| 配置键 | 运行时键 | 默认 | 说明 |
| --- | --- | --- | --- |
| `...:RateLimit:Enabled` | `sys.security.rateLimit.enabled` | `true` | 部署期硬总开关；`false` 时无论 DB 配置都不限流 |
| `...:RateLimit:WindowSeconds` | `sys.security.rateLimit.windowSeconds` | `60` | 窗口长度（秒） |
| `...:RateLimit:PermitPerWindow` | `sys.security.rateLimit.permitPerWindow` | `300` | 全局：单 IP 每窗口请求数（挡洪泛） |
| `...:RateLimit:AuthPermitPerWindow` | `sys.security.rateLimit.authPermitPerWindow` | `20` | 认证端点（`/api/v1/auth/*`）更严一档，挡在线爆破 |

`Enabled` 是部署期的硬总开关，一旦是 `true`，实际生不生效、阈值多少，就交给 `SysConfig` 在运行时说了算。

::: warning 反向代理后取的是代理 IP
上正式网关之前，记得先接 `ForwardedHeaders` 中间件解析 `X-Forwarded-For`。不接会怎样？同一个反向代理后面的所有客户端会被当成同一个 IP，共用一个限流分区。一个人把额度用完，所有人跟着被限流。
:::

## 演示模式（只读展示）

想挂一个谁都能点进去玩、但谁也改不动数据的对外演示站？打开 `TenonAdmin:DemoMode`（默认 `false`）就行：

```jsonc
// appsettings.json，或环境变量 TenonAdmin__DemoMode=true
{ "TenonAdmin": { "DemoMode": true } }
```

开关打开之后，内核才会注册一个全局授权过滤器 `DemoModeFilter`，按 HTTP 方法放行。GET/HEAD/OPTIONS 照常读。`/api/v1/auth/*`（登录、登出、刷新）也得放行，不然演示站连登录都进不去。剩下的 POST/PUT/PATCH/DELETE 一律回 HTTP 403，信封码 `41002`（`DemoModeReadOnly`），前端认这个码弹一个只读提示。

它拦的是「写」这个动作本身，跟角色权限没关系。判断发生在授权阶段，只看请求方法，所以哪怕是超管账号进来，照样改不动任何数据。想单独放行某个写接口（比如演示站自己的留言反馈功能）？别指望这个总开关帮忙，在业务代码那一侧单独处理。

## 日志脱敏

操作日志默认把所有写操作都记下来，读操作和匿名端点除外。这里有个明显的风险：入参里可能带着明文密码，直接落库就是日志里躺着一份密码。所以入参在写库之前会先脱敏，这段逻辑在 `SensitiveDataMasker.cs` 里，由 `OperationLogFilter` 调用：

```csharp
var paramJson = SensitiveDataMasker.Mask(context.ActionArguments);
```

脱敏靠的是**字段名，不看值**：属性名里只要含 `password` / `pwd` / `secret` / `token` / `credential`，值就被替换成 `***`。大小写不敏感，子串匹配，`newPassword`、`access_token` 都算命中。嵌套对象和数组，也会递归处理。序列化失败的话，比如入参里带着 `IFormFile` 这类没法序列化的东西，不会因此挡住请求，只记一个占位串 `<unserializable>`。

登录日志记的是原始输入的账号，哪怕这个账号根本不存在，加上具体的失败码，方便事后排查暴力破解或者账号探测。这段逻辑在 `AuthService` 里。IP 和 UA，由日志服务从当前请求里自动补全。**唯独密码，绝不记录**。
