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
// 用户覆写登录出参组装步骤(模板方法覆写)
private sealed class OverridingAuthService(/* 与基类相同的构造依赖 */)
    : AuthService(/* 透传依赖 */)
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
