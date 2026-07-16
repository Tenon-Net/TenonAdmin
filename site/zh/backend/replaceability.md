# 可替换性模型

可替换性是整个内核的重点:每个服务都接口背书、方法拆成 `virtual` 小步骤、用 `TryAdd` 注册,消费方不 fork 就能替换任意一环。这体现为三条约束,由 `ReplaceabilityTests`「六件套」测试锁定成契约。

## 约束一:`TryAdd` 注册,先到者胜

内置服务一律用 `TryAdd*` 注册,不用 `Add*`。`TryAdd` 的语义是「容器里已有同接口注册就不再添加」——所以消费方在 `AddTenonAdmin()` **之前**注册同一个接口,自己的实现就胜出,内置实现被跳过。

`ServicesSetup` 里全是这个写法:

```csharp
// backend/src/TenonAdmin.Services/ServicesSetup.cs
services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
services.TryAddScoped<IAuthService, AuthService>();
services.TryAddScoped<IPermissionProvider, RbacPermissionProvider>();
services.TryAddScoped<IDataScopeProvider, DataScopeProvider>();
services.TryAddScoped<IUserService, UserService>();
```

数据层同理:

```csharp
// backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs
services.TryAddSingleton<IIdGenerator>(sp =>
    new SnowflakeIdGenerator(sp.GetService<AdminIdOptions>()?.WorkerId ?? 0, sp.GetService<TimeProvider>()));
services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(SqlSugarRepository<>)));
```

::: warning `TryAdd` 依赖注册顺序
消费方必须在 `AddTenonAdmin()` **之前**注册才能赢。写在后面,内置服务已经占了坑,`TryAdd` 会跳过消费方注册——不报错,但替换悄悄没生效。
:::

可选包 `TenonAdmin.Caching.Redis` 就是标准范例:它在 `AddTenonAdmin()` 之前把 `ICacheProvider` 的 Redis 实现 `TryAdd` 进去,压过内核默认的进程内 `MemoryCacheProvider`。

```csharp
// backend/samples/MinimalHost/Program.cs
builder.Services.AddTenonAdminRedisCache(builder.Configuration); // 先注册,赢 TryAdd
builder.Services.AddTenonAdmin(builder.Configuration);
```

## 约束二:模板方法拆成 `virtual` 小步骤

长服务方法被拆成若干 `virtual` 小步骤(模板方法模式)。消费方想改行为时,继承内置服务、只重写**其中一步**,而不是整段复制方法。

以 `AuthService` 为例——登录流程里「组装登录出参」是一个独立的 `virtual` 步骤,消费方继承后只重写它:

```csharp
// backend/tests/TenonAdmin.Tests/ReplaceabilityTests.cs
// 覆写登录出参组装步骤:主构造器把基类的 8 个依赖原样透传,只多这一个覆写方法
private sealed class OverridingAuthService(
    IRepository<SysUser> users, IPasswordHasher hasher, ITokenProvider tokens, ISessionService sessions,
    ILogService logService, ILoginLockService loginLock, ICaptchaService captcha, ISecurityPolicyProvider policy)
    : AuthService(users, hasher, tokens, sessions, logService, loginLock, captcha, policy)
{
    protected override LoginOutput BuildLoginOutput(SysUser user, TokenPair pair) =>
        base.BuildLoginOutput(user, pair) with { Name = "OVERRIDDEN" };
}
```

登录的其余步骤(校验码、失败锁定、密码验证、签发令牌、建会话)全部走基类原逻辑,只有出参组装被替换。继承一步 vs 复制整段,升级内核时前者不会因为你抄了旧版方法体而错过上游修复。

## 约束三:业务程序集挂载

消费方的实体和控制器经 `options.ApplicationAssemblies` 挂进内核,不改内核就能扩展:实体加入 CodeFirst 建表,控制器 `AddApplicationPart` 进同一 MVC 管道。细节见[架构分层](./architecture.md#消费方的实体和控制器如何挂进来)。

配合模块禁用,消费方还能**接管**内置模块的路由:禁掉内置 `Dict` 模块后,自己的 `CustomDictController` 就能占用 `/api/v1/sys/dict/*` 这条路由。

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, options =>
{
    options.ApplicationAssemblies.Add(typeof(MyModule).Assembly);
});
```

## 「六件套」把这些锁成契约

`backend/tests/TenonAdmin.Tests/ReplaceabilityTests.cs` 是可替换机制的回归锁——用例名照设计写死,把上面三条约束当契约验证,不是普通测试:

| 测试 | 锁定什么 |
| --- | --- |
| `ReplaceService_ShouldUseUserImplementation` | 消费方 `Replace` 掉 `IPasswordHasher`,容器解析出的是消费方实现 |
| `OverrideAuthStep_ShouldAffectLoginFlow` | 重写 `AuthService` 的一个 `virtual` 步骤,登录流程返回被改写的结果 |
| `DisabledModule_ShouldRemoveBuiltInController` | 禁用的模块内置控制器被摘除(404),未禁的仍在 |
| `CustomController_ShouldOwnSameRouteAfterModuleDisabled` | 禁掉内置模块后,消费方控制器接管同一路由 |
| `CustomSeedData_ShouldRunOnceAndBeIdempotent` | 消费方种子首启插入、二启幂等不重复 |

::: tip 改内核前先看它们
这几个用例是产品承诺的可执行版本。改动 `TryAdd` 注册、`virtual` 拆分、或程序集挂载路径前,先确认它们仍绿——它们红了,意味着某个替换点被悄悄破坏了。
:::

## 有两样东西内核不让你动

前面几节的结论是「几乎什么都能换」,但门户的模块管理上有两道服务端闸门,直接调管理 API 也绕不过。先分清两个「模块」:约束三里的 `Api.DisabledModules` 是启动期开关,摘掉内置控制器好让你接管路由;这里说的是多应用门户里的应用记录(`SysModule`),经运行时 CRUD 增删改。闸门画在后者上。

**内置 system 模块不能停用。** 它承载全部内置管理页(组织、运维、日志、文件),停用即门户整体失联,而且没有 UI 恢复入口——等于把自己锁在门外。前端那行禁用态拦截只是提示,不是防线;真正的闸在服务端,按固定 Id(不随 Code 改动失守)判 `Enabled=false` 就拒。

**带菜单的模块不能删。** 删掉一个还挂着菜单的模块,这些菜单的顶级目录 `ModuleId` 会悬空、整棵子树从门户消失。删除前会查一遍它名下有没有菜单,有就拒,逼你先把挂靠的顶级目录迁走或删掉再删模块。

```csharp
// backend/src/TenonAdmin.Services/Module/ModuleService.cs
// 停用内置模块:按固定 Id 判(42013 ModuleProtected,与「不可删除」共用一个码)
AdminException.ThrowIf(id == DefaultModuleSeed.BUILTIN_MODULE_ID && !input.Enabled, ErrorCode.ModuleProtected);

// 删除带菜单的模块:走 Db 逃生舱查菜单(42023 ModuleHasMenus)
AdminException.ThrowIf(
    await modules.Db.Queryable<SysMenu>().AnyAsync(m => m.ModuleId == id),
    ErrorCode.ModuleHasMenus);
```

删菜单这条查询特意走 `modules.Db` 逃生舱,而不在构造器里加 `IRepository<SysMenu>`——给主构造器加参数会破坏继承本类的消费方的源码兼容。连加一道闸都不肯改子类签名,这正是可替换性约束在自我约束。两处都由 `ModuleProtectionTests` 锁定,不在上面的六件套里。

## 消费方替换一个服务的完整写法

以替换密码哈希算法为例:

```csharp
// 1. 实现内核接口
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    public string Hash(string password) => /* 你的算法 */;
    public bool Verify(string password, string hash) => /* 你的校验 */;
}

// 2. 在 AddTenonAdmin() 之前注册(顺序是关键)
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddTenonAdmin(builder.Configuration);
```

内核对 `IPasswordHasher` 用的是 `TryAddSingleton`,所以容器里已有你的注册,内置的 `Pbkdf2PasswordHasher` 就不会再进来。要换雪花 ID 为数据库自增或 GUID v7,同样实现 `IIdGenerator` 并前置注册即可;要改某个服务的一个环节而非整体,就继承它、重写那个 `virtual` 步骤。

整体替换、覆写单步、禁用接管、消费方种子这四条路的分步操作和踩坑,收在[替换内置服务](/zh/guide/replace-service);本页只解释这些替换点为什么立得住。
