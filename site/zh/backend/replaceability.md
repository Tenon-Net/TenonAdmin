# 可替换性模型

内核注册内置服务时不写 `Add*`，一律写 `TryAdd*`。就靠这一个前缀，你不必 fork 也能换掉密码哈希、缓存、登录流程里的某一步。做法是抢在 `AddTenonAdmin()` 前面注册自己的实现。

先按改动范围选路径：整个接口都要换，用前置 DI 注册；只改服务流程的一步，继承实现并覆写 `virtual` 方法；要占用内置模块的原路由，先禁用模块再挂自己的控制器；只增加实体和端点，把业务程序集加入 `ApplicationAssemblies`。四条路径可以组合，但都不要求修改内核源码。

## 约束一：`TryAdd` 注册，先到者胜

内置服务一律用 `TryAdd*` 注册，不用 `Add*`。`TryAdd` 的语义是「容器里已经有同一个接口的注册，就不再添加」。所以消费方只要在 `AddTenonAdmin()` **之前**注册同一个接口，自己的实现就胜出，内置的那个被跳过。不过这条只适用于单实现的接口。`ICaptchaProvider`、`ISeedData` 这类走的是 `TryAddEnumerable`，按实现类型防重，语义是「入集」，不是「替换」。你前置注册的滑块验证码，会和内置那三种一起入集。最后选中哪一个，另由 `TenonAdmin:Security:Captcha:Type` 决定。

`ServicesSetup` 里全是这个写法：

```csharp
// backend/src/TenonAdmin.Services/ServicesSetup.cs
services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
services.TryAddScoped<IAuthService, AuthService>();
services.TryAddScoped<IPermissionProvider, RbacPermissionProvider>();
services.TryAddScoped<IDataScopeProvider, DataScopeProvider>();
services.TryAddScoped<IUserService, UserService>();
```

数据层同理：

```csharp
// backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs
services.TryAddSingleton(sp => WorkerIdAssignment.Resolve(sp.GetService<AdminIdOptions>()));
services.TryAddSingleton<IIdGenerator>(sp =>
    new SnowflakeIdGenerator(sp.GetRequiredService<WorkerIdAssignment>().WorkerId, sp.GetService<TimeProvider>()));
services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(SqlSugarRepository<>)));
```

::: warning `TryAdd` 依赖注册顺序
消费方必须在 `AddTenonAdmin()` **之前**注册才能赢。写在后面，内置服务已经占了坑，`TryAdd` 会跳过消费方注册。整个过程一声不响，替换却没生效。
:::

可选包 `TenonAdmin.Caching.Redis` 就是个标准范例。它在 `AddTenonAdmin()` 之前，把 `ICacheProvider` 的 Redis 实现 `TryAdd` 进去，压过内核默认的进程内 `MemoryCacheProvider`。

```csharp
// backend/samples/MinimalHost/Program.cs
builder.Services.AddTenonAdminRedisCache(builder.Configuration); // 先注册,赢 TryAdd
builder.Services.AddTenonAdmin(builder.Configuration);
```

## 约束二：模板方法拆成 `virtual` 小步骤

长的服务方法被拆成若干 `virtual` 小步骤，用的是模板方法模式。消费方想改行为，就继承内置服务，只重写**其中一步**，而不是整段复制方法。

以 `AuthService.BuildLoginOutput` 为例，子类可以只重写登录出参组装，其余校验、锁定、令牌和会话步骤继续走基类。继承认证服务时还有一条安全约束：构造函数必须把当前基类的全部能力依赖继续传入。`AuthService` 的外部登录、时钟和 TOTP 依赖采用尾随可选参数来保持源码兼容，省略它们虽然能编译，却会关闭对应步骤。以当前构造函数和 `ReplaceabilityTests.OverridingAuthService` 为准，不要复制旧参数列表。

## 约束三：业务程序集挂载

消费方的实体和控制器经 `options.ApplicationAssemblies` 挂进内核，不改内核就能扩展。实体会加入 CodeFirst 建表，控制器经 `AddApplicationPart` 进同一条 MVC 管道。细节见[架构分层](./architecture.md#消费方的实体和控制器如何挂进来)。

配合模块禁用，消费方还能**接管**内置模块的路由。禁掉内置 `Dict` 模块之后，自己的 `CustomDictController` 就能占用 `/api/v1/sys/dict/*` 这条路由。禁用那一步不能省。要是两个控制器同时注册在这条路由上，请求打过来就是 `AmbiguousMatchException`，而且启动期不报，只在命中的时候才炸。

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, options =>
{
    options.ApplicationAssemblies.Add(typeof(MyModule).Assembly);
    options.Api.DisabledModules = ["Dict"];   // 少这行就是路由冲突,不是接管
});
```

## 「六件套」把这些锁成契约

`backend/tests/TenonAdmin.Tests/ReplaceabilityTests.cs` 把可替换性当作公开契约验证。目前覆盖九条路径，包括服务替换、单步覆写、外部 provider、模块接管和消费方种子：

| 测试 | 锁定什么 |
| --- | --- |
| `ReplaceService_ShouldUseUserImplementation` | 消费方 `Replace` 掉 `IPasswordHasher`，容器解析出的是消费方实现 |
| `ReplaceSmsSender_ShouldUseUserImplementation` | 消费方 `Replace` 掉 `ISmsSender`，容器解析出的是消费方实现 |
| `ReplaceEmailSender_ShouldUseUserImplementation` | 消费方 `Replace` 掉 `IEmailSender`，容器解析出的是消费方实现 |
| `ReplaceRealtimePublisher_ShouldUseUserImplementation` | 消费方 `Replace` 掉 `IRealtimePublisher`，容器解析出的是消费方实现 |
| `OverrideAuthStep_ShouldAffectLoginFlow` | 重写 `AuthService` 的一个 `virtual` 步骤，登录流程返回被改写的结果 |
| `ExternalAuthProvider_ShouldBePluggable` | 消费方前置注册的外部登录 provider，最终出现在容器解析出的 provider 集合里（加法式，不覆盖内置的） |
| `DisabledModule_ShouldRemoveBuiltInController` | 禁用的模块内置控制器被摘除（404），未禁的仍在 |
| `CustomController_ShouldOwnSameRouteAfterModuleDisabled` | 禁掉内置模块后，消费方控制器接管同一路由 |
| `CustomSeedData_ShouldRunOnceAndBeIdempotent` | 消费方种子首启插入、二启幂等不重复 |

::: tip 改内核前先看它们
这几个用例是产品承诺的可执行版本。改动 `TryAdd` 注册、`virtual` 拆分、或者程序集挂载路径之前，先确认它们还是绿的。它们一旦变红，就说明某个替换点被悄悄破坏了。
:::

## 有两样东西内核不让你动

前面几节的结论是「几乎什么都能换」。但门户的模块管理上有两道服务端闸门，直接调管理 API 也绕不过。先分清两个「模块」。约束三里的 `Api.DisabledModules` 是启动期开关，摘掉内置控制器，好让你接管路由。这里说的是另一样东西：多应用门户里的应用记录，经运行时 CRUD 增删改，实体是 `SysModule`。闸门画在后者上。

**内置 system 模块不能停用。** 它承载了全部内置管理页，也就是组织、运维、日志、文件。停用它，门户就整体失联，而且没有 UI 恢复入口，等于把自己锁在门外。前端那行禁用态拦截只是提示，不是防线。真正的闸在服务端，它按固定 Id 判断，只要 `Enabled=false` 就拒，这样不会随 Code 改动而失守。

**带菜单的模块不能删。** 删掉一个还挂着菜单的模块，这些菜单的顶级目录 `ModuleId` 就会悬空，整棵子树从门户消失。所以删除前会先查一遍它名下有没有菜单，有就拒，逼你先把挂靠的顶级目录迁走或者删掉，再来删模块。

```csharp
// backend/src/TenonAdmin.Services/Module/ModuleService.cs
// 停用内置模块:按固定 Id 判(42013 ModuleProtected,与「不可删除」共用一个码)
AdminException.ThrowIf(id == DefaultModuleSeed.BUILTIN_MODULE_ID && !input.Enabled, ErrorCode.ModuleProtected);

// 删除带菜单的模块:走 Db 逃生舱查菜单(42023 ModuleHasMenus)
AdminException.ThrowIf(
    await modules.Db.Queryable<SysMenu>().AnyAsync(m => m.ModuleId == id),
    ErrorCode.ModuleHasMenus);
```

校验菜单时直接使用已有仓储的 `modules.Db`，避免给主构造器增加 `IRepository<SysMenu>` 后破坏消费方子类的源码兼容。这两条保护由 `ModuleProtectionTests` 验证。

## 消费方替换一个服务的完整写法

以替换密码哈希算法为例：

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

内核对 `IPasswordHasher` 用的是 `TryAddSingleton`。所以容器里已经有你的注册，内置的 `Pbkdf2PasswordHasher` 就不会再进来。想把雪花 ID 换成数据库自增或者 GUID v7？一样是实现 `IIdGenerator` 再前置注册。想改某个服务的一个环节、而不是整体？就继承它，重写那个 `virtual` 步骤。

启动后先从容器解析一次目标接口，确认得到的是自定义类型，再跑一条真实业务路径。只检查注册代码不够：注册顺序写反时应用仍能启动，只会继续使用内置实现；禁用模块漏配时也要等请求命中重复路由才会报错。

整体替换、覆写单步、禁用接管、消费方种子，这四条路的分步操作和踩坑，都收在[替换内置服务](/zh/guide/replace-service)里。本页只解释这些替换点为什么立得住。
