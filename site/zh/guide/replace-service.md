# 替换内置服务

你想改内核的某个行为：换掉密码哈希算法、给登录流程插一步、把内置字典模块整块换成自己的。不用 fork，也不用复制内核代码。

先问自己一个问题：你要动的范围有多大？

- **整个服务的实现全换掉**（比如 PBKDF2 换成 argon2、进程内缓存换成 Redis）→ 抢在 `AddTenonAdmin()` 之前注册自己的实现。
- **只改流程里的一步**（比如登录后多记一笔、账密校验改走 LDAP）→ 继承内置服务，覆写那一个 `virtual` 方法。
- **整块内置模块都不要、想自己接管**（比如字典模块的接口完全不合用）→ 禁用它的控制器，用自己的控制器占同一条路由。

还有一件相关的事：给你自己的业务表灌初始数据，走消费方种子。四种都在下面。

这几条路为什么成立？靠的是三条约束：`TryAdd` 先到者胜、`virtual` 拆步、程序集挂载，外加锁死它们的「六件套」测试。原理见[可替换性模型](/zh/backend/replaceability)。

## 换掉整个服务：抢在 AddTenonAdmin 之前注册

内核所有内置服务都用 `TryAdd*` 注册，语义是「容器里已有同接口就不再添加」。所以你只要在 `AddTenonAdmin()` **之前**把自己的实现注册进去，内核那行 `TryAdd` 检测到坑已被占，就自动让位。

以换密码哈希算法为例：

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
写在 `AddTenonAdmin()` **后面**，内置实现已经占了坑，你的 `TryAdd` 会被跳过。它不报错，替换却没生效。要不受顺序影响，用 `builder.Services.Replace(ServiceDescriptor.Scoped<IAuthService, MyAuthService>())`，它是「覆盖已有注册」，写在 `AddTenonAdmin()` 之后也照样赢。
:::

常见的替换点：

| 接口 | 默认实现 | 什么时候换 |
|---|---|---|
| `IPasswordHasher` | `Pbkdf2PasswordHasher` | 换 bcrypt / argon2 |
| `ICacheProvider` | `MemoryCacheProvider` | 换 Redis（装 `TenonAdmin.Caching.Redis` 包即是这套写法） |
| `IFileStorage` | `LocalFileStorage` | 换 OSS / S3 |
| `IAuthService` | `AuthService` | 定制整套登录流程 |
| `IDataScopeProvider` | `DataScopeProvider` | 定制数据范围规则 |
| `IIdGenerator` | `SnowflakeIdGenerator` | 换数据库自增 / GUID v7 |

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
    ICaptchaService captcha, ISecurityPolicyProvider policy, ISmsOtpService smsOtp)
    : AuthService(users, hasher, tokens, sessions, logService, loginLock, captcha, policy, smsOtp)
{
    protected override LoginOutput BuildLoginOutput(SysUser user, TokenPair pair) =>
        base.BuildLoginOutput(user, pair) with { Name = $"{user.Name}({user.Account})" };
}

// 注册用 Replace,不受顺序影响
builder.Services.AddTenonAdmin(builder.Configuration);
builder.Services.Replace(ServiceDescriptor.Scoped<IAuthService, MyAuthService>());
```

找可覆写的步骤，就是打开目标服务源码，搜 `protected virtual`。那几个方法就是给你留的口子。覆写时先调 `base.Xxx()` 保留原逻辑，再追加自己的。继承一步而不是复制整段，好处很直接：升级内核时，基类那步的上游修复你会自动吃到。不会因为抄了旧版方法体而错过它。

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

能被禁的只有带 `[Module("Name")]` 标注的控制器，目前是这六个：`Dict`、`Upload`、`Notice`、`Log`、`Config`、`Dashboard`。`Upload` 稍微特殊：字面上禁的是文件控制器 `/api/v1/sys/file`，模块名和路由并不一致。身份认证、用户、机构、角色、菜单、门户这些控制器没有这个标注。没有开关能把它们关掉，因为关了整个系统就登不进去了。

别把 `Api.DisabledModules` 和门户里的「应用/模块」搞混，后者对应的是 `SysModule` 那张表。前者是编译期的路由开关，后者是运行时数据，也有自己的护栏。内置的 `system` 应用承载着全部管理页，想通过管理接口停用它会被拒，错误码 42013。原因很直接：门户会因此失联，且没有 UI 恢复入口。还挂着菜单的应用也不许删，错误码 42023。删了的话，那些顶级目录的 `ModuleId` 会悬空，整棵子树从门户消失。

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

种子行的固定 Id 必须落在消费方保留区间 `[1000, 4095]`。常量在 `TenonAdmin.Core.TenonSeedIds` 里：`ConsumerMin`=1000、`ConsumerMax`=4095。`[1, 999]` 归内核内置种子，`4096` 往上是雪花运行时发号区。`Id = 0` 或 `≥ 4096` 会被启动检查当场拒绝，应用直接起不来，不会静默吞掉，这个检查在 `DatabaseInitializer` 里。但落进内核段 `[1, 999]` 不会报错。运行时不区分谁是消费方，这条下界只能靠自觉。挑号务必从 `TenonSeedIds.ConsumerMin` 起，撞了内核将来的号，代价是升级时主键冲突，而且无法回退。

::: warning 忘了注册是静默不执行
内核不扫描程序集找种子。`options.ApplicationAssemblies` 只管实体建表和控制器挂载，不碰种子。种子必须显式注册，漏了这行，种子就不跑，也没有任何报错。
:::

`ApplicationAssemblies` 那行是消费方接入的总开关，它同时让你的实体加入 CodeFirst 建表、让你的控制器进同一 MVC 管道。完整链路见[端到端加一个业务模块](/zh/guide/business-module)。真要动手替换前，看一眼 `backend/tests/TenonAdmin.Tests/ReplaceabilityTests.cs`。它的五个用例把上面四种机制逐一验成了契约。照着它们的写法，给自己的替换补一层回归测试，最稳。
