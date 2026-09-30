# 替换内置服务

默认文件存储、登录流程或字典接口不符合业务需要时，可以在自己的宿主项目中注册扩展实现。先确定要改变的范围，再选择替换服务、覆写步骤或接管路由，避免为一个局部需求复制整套框架代码。

你要改的范围有多大，决定走哪条路：

- **整个服务的实现全换掉**（比如 PBKDF2 换成 argon2、进程内缓存换成 Redis）→ 抢在 `AddTenonAdmin()` 之前注册自己的实现。
- **只改流程里的一步**（比如登录后多记一笔、账密校验改走 LDAP）→ 继承内置服务，覆写那一个 `virtual` 方法。
- **整块内置模块都不要、想自己接管**（比如字典模块的接口完全不合用）→ 禁用它的控制器，用自己的控制器占同一条路由。

示例中的注册代码放在自己的 `Program.cs`，扩展类放在业务项目的独立文件中。初始数据通过种子注册，方法见末尾。

框架通过接口注册、可覆写方法和程序集发现支持这些扩展。需要了解设计约束时，参考[可替换性模型](/zh/backend/replaceability)。

## 换掉整个服务：抢在 AddTenonAdmin 之前注册

内核所有内置服务都用 `TryAdd*` 注册，语义是「容器里已有同接口就不再添加」。所以你只要在 `AddTenonAdmin()` **之前**把自己的实现注册进去，内核那行 `TryAdd` 检测到坑已被占，就自动让位。

下面用密码哈希接口说明注册方式。算法部分为占位示意，不能直接运行；实际替换还需要兼容已有密码哈希，并验证旧账号仍能登录。

```csharp
// 消费方 Program.cs
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    public string Hash(string password) => /* 你的算法 */;
    public bool Verify(string password, string hashedPassword) => /* 你的校验 */;
}

// 先注册自己的 —— 抢占接口
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
// 再调内核 —— TryAdd 检测到已有注册,自动跳过内置的 Pbkdf2PasswordHasher
builder.Services.AddTenonAdmin(builder.Configuration);
```

::: warning 顺序反了会静默失效
如果使用 `TryAdd*` 注册替换服务，放在 `AddTenonAdmin()` 后面会因已有默认注册而跳过。上例使用的 `AddSingleton` 与 `TryAddSingleton` 语义不同，不能一概说晚注册都失效。推荐统一在内核之前注册替换；已注册后需要显式替换时，使用 `Replace(ServiceDescriptor...)` 并匹配原服务生命周期。
:::

常见的替换点：

| 接口 | 默认实现 | 什么时候换 |
|---|---|---|
| `IPasswordHasher` | `Pbkdf2PasswordHasher` | 换 bcrypt / argon2 |
| `ICacheProvider` | `MemoryCacheProvider` | 换 Redis（装 `TenonAdmin.Caching.Redis` 包即是这套写法） |
| `IFileStorage` | `LocalFileStorage` | 换 OSS / S3 |
| `IAuthService` | `AuthService` | 定制整套登录流程 |
| `IDataScopeProvider` | `DataScopeProvider` | 定制数据范围规则 |
| `IIdGenerator` | `SnowflakeIdGenerator` | 定制符合现有 `long` 主键契约的发号策略 |

大部分替换点就是 `backend/src/TenonAdmin.Services/ServicesSetup.cs` 里每一行 `TryAdd`。数据层与宿主层还各有一批，比如 `IIdGenerator` 注册在 `backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs`。那里注册的每个接口都是可替换点。

## 只改一步：子类覆写 virtual

整体替换要重新注入服务的全部依赖，大多数时候你并不想改那么多。内核把长方法拆成了若干 `protected virtual` 小步骤（模板方法），你继承后只覆写要改的那一步，其余原样走基类。

`AuthService.LoginAsync`（`backend/src/TenonAdmin.Services/Auth/AuthService.cs`）就是范本，通篇只编排一串 `virtual` 步骤：失败锁定检查 → 验证码 → `ValidateUserAsync` 账密校验 → 停用/锁定策略 → 密码过期 → `CheckSmsSecondFactorAsync` 短信二次验证 → 签发令牌 → `OnLoginSucceededAsync` 成功后置 → `BuildLoginOutput` 组装出参。

想对接 LDAP，只覆写 `ValidateUserAsync`。想给登录返回值加字段，只覆写 `BuildLoginOutput`。想让没绑手机号的用户也强制走二次验证，只覆写 `CheckSmsSecondFactorAsync`。内核默认对这种用户直通，原因见[短信验证](/zh/backend/auth-security#短信验证-二次验证与免密登录)：

```csharp
// 只改出参组装这一步,其余登录逻辑(验证码/锁定/密码校验/签发令牌)全走基类原样
public sealed class MyAuthService(
    IRepository<SysUser> users, IPasswordHasher hasher, ITokenProvider tokens,
    ISessionService sessions, ILogService logService, ILoginLockService loginLock,
    ICaptchaService captcha, ISecurityPolicyProvider policy, ISmsOtpService smsOtp,
    IEnumerable<IExternalAuthProvider>? externalProviders = null,
    ISysUserExternalService? externalBindings = null, IRbacService? rbac = null,
    TimeProvider? time = null, IMfaPolicyService? mfaPolicy = null,
    IMfaChallengeService? mfaChallenge = null, AdminSecurityOptions? security = null)
    : AuthService(users, hasher, tokens, sessions, logService, loginLock, captcha, policy, smsOtp,
        externalProviders, externalBindings, rbac, time, mfaPolicy, mfaChallenge, security)
{
    protected override LoginOutput BuildLoginOutput(SysUser user, TokenPair pair) =>
        base.BuildLoginOutput(user, pair) with { Name = $"{user.Name}({user.Account})" };
}

// 注册用 Replace,不受顺序影响
builder.Services.AddTenonAdmin(builder.Configuration);
builder.Services.Replace(ServiceDescriptor.Scoped<IAuthService, MyAuthService>());
```

在目标服务中查找 `protected virtual` 方法，选择与需求对应的一步。上例先调用基类方法，再调整显示名称，保留其余结果字段。构造函数也转发基类的可选依赖，包括外部登录和 MFA 服务，避免扩展时意外跳过既有安全处理。

是否调用 `base.Xxx()` 取决于这一步是追加行为还是整体替换。升级后对照基类签名，并验证登录、刷新令牌和已启用的 MFA 流程。

## 整块模块不要：禁用 + 接管路由

如果内置模块的控制器完全不合用，可以把它整块摘掉，再用自己的控制器占同一条路由。禁用走 `Api.DisabledModules`：

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);   // 挂载你的业务程序集(见下)
    o.Api.DisabledModules = ["Dict"];   // 也可走配置 TenonAdmin:Api:DisabledModules
});
```

被禁的控制器，路由不再注册，原接口会返回 404。这时你的同路由控制器就能接管：

```csharp
[ApiController]
[Route("api/v1/sys/dict")]   // 与被禁用的内置 DictController 同路由
public class CustomDictController : ControllerBase { /* 你的字典逻辑 */ }
```

只有标记 `[Module("Name")]` 的控制器能通过此配置禁用，例如 `Dict`、`Upload`、`Notice`、`Log`、`Config`、`Dashboard`、`Job`。以当前版本的控制器标记为准。`Upload` 对应文件路由 `/api/v1/sys/file`，配置值使用模块名，不是路由。认证、用户、机构、角色、菜单和门户等基础控制器没有该关闭开关。

别把 `Api.DisabledModules` 和门户里的「应用/模块」搞混，后者对应的是 `SysModule` 那张表。前者是启动时的路由开关，后者是运行时数据，也有自己的护栏。内置的 `system` 应用承载着全部管理页，想通过管理接口停用它会被拒，错误码 42013。原因很直接：门户会因此失联，且没有 UI 恢复入口。还挂着菜单的应用也不许删，错误码 42023。删了的话，那些顶级目录的 `ModuleId` 会悬空，整棵子树从门户消失。

## 给自己的实体播种：消费方种子

你的业务表也能带首次启动自动插入、重复启动幂等的初始数据，实现泛型版 `ISeedData<TEntity>` 就行。注意别直接实现非泛型 `ISeedData`，它只是 DI 收集用的空标记：

```csharp
public class ProductSeed : ISeedData<BizProduct>
{
    public IEnumerable<BizProduct> HasData() =>
    [
        new() { Id = TenonSeedIds.ConsumerMin, Name = "默认产品", Code = "default", Sort = 0, Enabled = true },
    ];
}

// 在你自己的 Program.cs 注册
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, ProductSeed>());
```

为业务种子分配固定 ID 时，从 `TenonAdmin.Core.TenonSeedIds.ConsumerMin`（1000）开始，并低于 `SnowflakeIdGenerator.CurrentFloor()` 返回的运行时发号下界。`[1, 999]` 留给内置种子；运行时无法区分种子来自哪个项目，这段保留范围仍需开发者遵守。

启动检查会拒绝 `Id = 0`、超出运行时下界或同实体重复的编号。不要复用历史种子 ID，避免升级时与已有记录冲突。

::: warning 忘了注册是静默不执行
内核不扫描程序集找种子。`options.ApplicationAssemblies` 只管实体建表和控制器挂载，不碰种子。种子必须显式注册，漏了这行，种子就不跑，也没有任何报错。
:::

验证替换时，先通过正常入口调用服务，确认扩展逻辑确实执行，再检查原有权限和错误处理。接管路由时，在 OpenAPI 中确认原控制器已移除、自己的端点已出现；种子则应在首次启动插入，重启后不重复。`ReplaceabilityTests.cs` 提供回归测试参考，完整业务接入见[添加业务模块](/zh/guide/business-module)。
