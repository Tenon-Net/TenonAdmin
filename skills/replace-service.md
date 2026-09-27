# 替换/扩展内置服务 (Replace Service)

TenonAdmin 的核心卖点是可替换性：消费者无需 fork 即可定制框架行为。三种机制，按需选用。

## 机制一：DI 替换（整体换掉）

框架内置服务全部用 `TryAddScoped` 注册。消费者只需在 `AddTenonAdmin()` **之前**注册同接口的自己的实现，框架的 `TryAdd` 会让位。

```csharp
// 消费者 Program.cs
var builder = WebApplication.CreateBuilder(args);

// ① 先注册自己的实现 —— 抢占接口
builder.Services.AddSingleton<IPasswordHasher, MyBcryptHasher>();

// ② 再调框架 —— TryAdd 检测到已有注册，自动跳过
builder.Services.AddTenonAdmin(builder.Configuration);
```

适用场景：想完全替换某个服务的全部行为（如换密码哈希算法、换缓存提供者、换文件存储）。

### 可替换的关键接口

| 接口 | 默认实现 | 替换场景 |
|---|---|---|
| `IPasswordHasher` | `Pbkdf2PasswordHasher` | 换 bcrypt/argon2 |
| `ICacheProvider` | `MemoryCacheProvider` | 已有 Redis 包可直接用 |
| `ISmsSender` | `LoggingSmsSender`(码写日志) | 接真实短信厂商(阿里云/腾讯云),按 purpose 映射模板 |
| `IFileStorage` | `LocalFileStorage` | 换 OSS/S3 |
| `IAuthService` | `AuthService` | 定制登录流程 |
| `IDataScopeProvider` | `DataScopeProvider` | 定制数据范围规则 |
| `IPermissionProvider` | `RbacPermissionProvider` | 定制权限检查逻辑 |

Services 层的注册见 `backend/src/TenonAdmin.Services/ServicesSetup.cs`；其他层的替换点看各自 `*Setup.cs` 中的 `TryAdd*`。

---

## 机制二：子类覆写（改一步）

框架服务的公开方法都是 `virtual`，内部拆成小步骤（模板方法）。消费者继承后只覆写需要改的那一步，其余逻辑原样保留。

```csharp
// 消费者代码：只改登录返回值的组装步骤，其他登录流程不动
public class MyAuthService(
    IRepository<SysUser> users, IPasswordHasher hasher, ITokenProvider tokens,
    ISessionService sessions, ILogService logService, ILoginLockService loginLock,
    ICaptchaService captcha, ISecurityPolicyProvider policy, ISmsOtpService smsOtp)
    : AuthService(users, hasher, tokens, sessions, logService, loginLock, captcha, policy, smsOtp)
{
    protected override LoginOutput BuildLoginOutput(SysUser user, TokenPair pair)
    {
        var output = base.BuildLoginOutput(user, pair);
        // 在标准返回值基础上追加自定义字段
        return output with { Name = $"{user.Name} ({user.Account})" };
    }
}
```

注册方式——用 `Replace` 确保覆盖（不论注册顺序）：

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
// Replace 在 TryAdd 之后也能替换（不需要抢先注册）
builder.Services.Replace(ServiceDescriptor.Scoped<IAuthService, MyAuthService>());
```

适用场景：只想改服务的某个步骤（如：登录后额外记日志、分页查询额外加过滤条件、新增前额外校验）。子类构造函数仍需把基类依赖传给 `base`；收益是复用其余业务步骤。

### 怎么找可覆写的步骤

1. 打开目标 Service 源码（`backend/src/TenonAdmin.Services/`）
2. 找 `virtual` 或 `protected virtual` 方法——这些就是可覆写点
3. 要扩展原逻辑时调用 `base.Method()`；要替换该步骤时直接实现，避免原步骤产生重复副作用

---

## 机制三：禁用模块 + 接管路由

如果内置模块的 Controller 完全不满足需求，可以禁用它并用自己的 Controller 接管同一路由前缀。

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
    o.Api.DisabledModules = ["Dict"];  // 禁用内置字典模块(也可走配置 TenonAdmin:Api:DisabledModules)
});
```

然后在消费者 Assembly 中编写自己的 Controller，使用同样的路由：

```csharp
[ApiController]
[Route("api/v1/sys/dict")]   // 与被禁用的内置 DictController 同路由
public class CustomDictController : ControllerBase
{
    [HttpGet]
    [RolePermission]
    public IActionResult List() => Ok(); // 完全自定义的字典逻辑仍须逐 action 鉴权
}
```

可禁用的模块名对应 Controller 上的 `[Module("Name")]` 标注。查看已有的模块名：

```bash
rg '\[Module\(' backend/src/TenonAdmin.AspNetCore/Controllers/
```

---

## 消费者种子数据

消费者可以为自己的实体提供种子数据（首次启动自动插入，幂等不重复）。

```csharp
// 1. 实现泛型版 ISeedData<TEntity>（非泛型 ISeedData 只是 DI 收集用的空标记，别直接实现它）
public class ProductSeed : ISeedData<BizProduct>
{
    public IEnumerable<BizProduct> HasData() =>
    [
        new() { Id = TenonSeedIds.ConsumerMin, Name = "默认产品", Code = "default", Sort = 0, Enabled = true },
    ];
}

// 2. 注册（在消费者自己的 Program.cs；内核不扫描程序集找种子，忘注册＝静默不执行）
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Transient<ISeedData, ProductSeed>());
```

**Id 规则**：消费者种子固定 Id 从 `TenonSeedIds.ConsumerMin`（1000）起，并严格小于应用启动时 `SnowflakeIdGenerator.CurrentFloor()` 算出的动态雪花地板。`ConsumerMax` 只是历史兼容常量，不再是运行时上限。越界或与其他种子撞号都会被 `DatabaseInitializer` 当场拒绝。

---

## 消费者实体 + Controller 注册

确保消费者的 `Program.cs` 中已配置 `ApplicationAssemblies`：

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, o =>
    o.ApplicationAssemblies.Add(typeof(Program).Assembly));
```

这一行做两件事：
- 消费者实体加入 CodeFirst 自动建表
- 消费者 Controller 自动注册路由（`AddApplicationPart`）

不加这行，消费者的表不会被创建，Controller 会 404。

---

## 验证可替换性

框架有一组可替换性契约测试，位于 `backend/tests/TenonAdmin.Tests/ReplaceabilityTests.cs`：

| 测试 | 验证的机制 |
|---|---|
| `ReplaceService_ShouldUseUserImplementation` | DI 替换 |
| `ReplaceSmsSender_ShouldUseUserImplementation` | DI 替换(短信通道) |
| `OverrideAuthStep_ShouldAffectLoginFlow` | 子类覆写 |
| `DisabledModule_ShouldRemoveBuiltInController` | 模块禁用 |
| `CustomController_ShouldOwnSameRouteAfterModuleDisabled` | 路由接管 |
| `CustomSeedData_ShouldRunOnceAndBeIdempotent` | 消费者种子 |

消费者开发自己的替换后，建议参考这些测试的写法编写回归测试。测试工具类 `AdminAppFactory` 支持 `Overrides`（注入替换）和 `DisabledModules`（禁用模块）两个配置点。
