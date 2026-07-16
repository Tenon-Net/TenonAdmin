# 后端规范(.NET 10 内核)

> 本页是可执行清单,规则均从现有代码提炼。完整版(含每条的正反例)见仓库 [`docs/coding-standards.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/coding-standards.md)。

::: tip 第一原则
内核以 NuGet 分发,消费方不改源码即可替换任一部件。凡新增可替换服务,一律 `TryAdd*` 注册、接口背书、方法拆 `virtual` 步 —— 这是硬约束,不是建议。
:::

## 分层与依赖方向

依赖只能自上而下,越层禁止:

```
Core        契约层:接口(I*Provider/I*Service)、Options、Result<T>、ErrorCode、AdminException。无 SqlSugar、无 ASP.NET。
  ↑
SqlSugar    数据层:ISqlSugarClient 单例、IRepository<>、实体基类、CodeFirst 初始化、种子运行器。
  ↑
Services    领域层:实体(Sys*)、*Service 实现、RBAC/数据范围 Provider、事件总线。★实体放这里,不放 SqlSugar 层。
  ↑
AspNetCore  宿主集成:AddTenonAdmin/MapTenonAdmin、JWT、过滤器、内置控制器。
  ↑
TenonAdmin  元包:只引用 AspNetCore,消费方装它即拉全栈。
```

新增代码先想清楚落在哪层。运行时依赖**仅** SqlSugarCore + Microsoft.\*,核心包不得引入其它第三方框架。每层装配是一个 `*Setup.cs` 扩展方法:`SqlSugarSetup` → `ServicesSetup` → `TenonAdminSetup`(组合根)。

## 可替换性三件套

`ReplaceabilityTests`(「六件套」测试)锁定的契约,当作硬约束,不是普通测试:

| 手段 | 做法 | 参照 |
|---|---|---|
| `TryAdd` 注册 | 内置服务全部 `TryAdd*`;消费方在 `AddTenonAdmin()` 之前注册同接口即胜出。**严禁**对可替换服务用裸 `Add*`。 | `ServicesSetup.cs`、`SqlSugarSetup.cs` |
| `virtual` 模板方法 | 长方法拆成小 `virtual` 步,消费方覆写一步而非抄整方法。 | `SessionService.EnforceConcurrencyAsync` |
| 接口背书 | 每个服务先有 `I*Service`,实现类 `virtual`。 | `Services/*/I*.cs` |
| 消费方装配 | 业务程序集经 `options.ApplicationAssemblies` 并入:实体参与建表、控制器 `AddApplicationPart`。改实体扫描/控制器注册时**务必保留此路径**。 | `TenonAdminSetup.cs` |

## 实体规范

- **位置**:实体定义在 `Services/Entities/`,不在 SqlSugar 层。系统内核表命名 `Sys*`。
- **基类**:
  - `BaseEntity`(`SqlSugar/Entities/BaseEntity.cs`):主键 + 审计四件套(`CreateTime`/`CreateUserId`/`UpdateTime`/`UpdateUserId`)+ 软删 `IsDelete`。这些字段由 AOP 自动填,业务代码零感知。
  - `DataEntity`:需要按机构做数据隔离的业务表继承它,带 `CreateOrgId` 锚点(数据范围过滤的依据)。
- **主键**:统一使用 `Id`(雪花 ID,AOP 自动填,业务代码不手动赋值)。
- **软删除**:统一用 `IsDelete` 字段。全局查询过滤器自动加 `IsDelete == false`;查已删数据要显式 `.ClearFilter<ISoftDelete>()`。
- **扩展字段**:不在表结构里预留的额外信息,存进 `ExtJson`,不新开列。
- **SqlSugar 特性**:`[SugarTable("表名", TableDescription=…)]`、唯一索引 `[SugarIndex(..., IsUnique=true)]`、列 `[SugarColumn(Length=…, ColumnDescription=…, IsNullable=…)]`,参照 `Entities/SysDictType.cs`。
- **不可变约定写进注释**:比如「Code 创建后不可变」,并在 Service 的 Update 方法里落实(不改该字段)。

## 服务规范

- 一服务一目录:`I{X}Service.cs` + `{X}Service.cs` + `{X}Models.cs`(DTO:`{X}Input`/`{X}PageInput`/`{X}Output`,用 `record`)。
- 实现类构造函数注入依赖(主构造函数语法),方法 `virtual`,异步方法 `Async` 后缀。
- 分页统一 `PagedList<T>` + `.ToPagedListAsync(current, size)`(`SqlSugar/Paging/`)。
- 校验用 `AdminException.ThrowIf(条件, ErrorCode.X)`。

## 错误处理

::: warning 错误是数字码
`ErrorCode` 是数字枚举,**永不带本地化文案**(`Core/ErrorCode.cs`)。i18n 完全在前端按码翻译,后端不下发任何文案。新增错误码往枚举里加即可。
:::

- 业务错误抛 `AdminException(ErrorCode)` 或返回 `ErrorCode`,由 `AdminExceptionFilter` 统一转信封。
- 控制器可直接 `return dto`,`ResultEnvelopeFilter` 兜底包成 `Result<T>`;内置控制器为了 OpenAPI 契约清晰,显式返回 `Result<T>.Ok(...)`。

## 控制器规范

参照 `Controllers/DictController.cs`:

```csharp
[ApiController]
[Route("api/v1/sys/dict")]
[Module("Dict")]                       // 可经 Api:DisabledModules 关停整模块
public class DictController(IDictService svc) : ControllerBase
{
    [HttpGet("type/page")]
    [RolePermission]                   // 权限码 = 规范化路由,无字符串
    public async Task<Result<PagedList<SysDictType>>> PageTypes([FromQuery] DictTypePageInput input) =>
        Result<PagedList<SysDictType>>.Ok(await svc.PageTypesAsync(input));
}
```

- **`[RolePermission]` 无参**:权限码就是 `{METHOD}:/{路由模板}`(如 `GET:/api/v1/sys/dict/type/page`),在角色-菜单界面勾路由即配权。**代码里永远不写 `"sys:user:add"` 之类魔法串**。超管(`sadm` claim)直接放行。
- **`[ActiveSession]`**:任意已登录用户可访问、但无需特定权限的端点用它。
- **`[OperationLog(...)]`**:需要审计的写操作挂它,由 `OperationLogFilter` 记录。
- **`[Module("X")]`**:模块化开关,可被配置摘除。
- 匿名端点显式加 `[AllowAnonymous]`(登录/刷新/验证码)。默认拒绝:`MapControllers().RequireAuthorization()` 全局兜底,漏挂 `[RolePermission]` 也不会静默公开。

## 数据访问

- 注入 `IRepository<T>`;复杂查询走 `.AsQueryable()`,需要逃生时走 `.Db`(如 `Db.Deleteable<>()`、`Db.Ado.UseTranAsync`)。
- **全局过滤器**(业务代码无需重复写):软删(`ISoftDelete` 实体自动过滤)、数据范围(`IOrgScoped`/`DataEntity` 按当前请求生效机构集过滤,招牌特性)。
- **唯一性查重要带上软删行**:`.ClearFilter<ISoftDelete>().AnyAsync(...)`,否则会撞库唯一索引抛原生 500。
- **多写操作包事务**:`Db.Ado.UseTranAsync`,失败整体回滚;**缓存失效放在事务提交之后**。
- 审计字段(`Id` 雪花、`CreateTime`/`User`/`Org`、`UpdateTime`/`User`)由 AOP 自动填,业务代码只设业务字段。

::: danger CreateOrgId 不能手动绕过
`CreateOrgId` 是机构维度数据范围的锚点,不填则该行在机构范围查询里恒为 0 行 —— 不要手动绕过 AOP 自己赋值。
:::

- 雪花 `WorkerId` 来自 `TenonAdmin:Id:WorkerId`,**多实例部署必须各配不同值**,详见 [常见问题](/zh/faq)。

## 缓存规范(性能核心)

系统采用**读穿透 / cache-aside + 显式失效**模型,不是每次查库。新增热读路径按此模板:

```csharp
public virtual async Task<T> GetHotAsync(string k)
{
    var key = CacheKeys.Xxx(k);               // ①逻辑键集中定义
    var cached = await cache.GetAsync<T>(key); // ②命中即返回
    if (cached is not null) return cached;
    var v = await LoadFromDb(k);               // ③未命中查库
    await cache.SetAsync(key, v, ttl);         // ④回填(TTL 仅兜底,主靠显式失效)
    return v;
}
// 任何增删改后:await cache.RemoveAsync(CacheKeys.Xxx(k));  ⑤显式失效
```

- **键集中在 `Core/CacheKeys.cs`,禁散落魔法串**。前缀 `Cache:KeyPrefix`(默认 `tenon:`)由 provider 统一追加。
- 变更时**既失效缓存也广播事件**(如 `DictService.InvalidateAsync` → `DictChangedEvent`),供跨节点失效/审计/推送订阅。
- 默认 `MemoryCacheProvider`(进程内);多实例共享装可选包 `TenonAdmin.Caching.Redis`,业务代码零改动,只需在 `AddTenonAdmin` **之前**注册以赢过 `TryAdd`:

```csharp
builder.Services.AddTenonAdminRedisCache(builder.Configuration); // 须在 AddTenonAdmin 之前
builder.Services.AddTenonAdmin(builder.Configuration);
```

## DI 装配

- 装配写进 `*Setup.cs` 扩展方法。内置服务**显式** `TryAdd`(不靠扫描,可预测、可替换);种子用 `TryAddEnumerable`(按实现类型防重)。
- 无状态服务 `Singleton`(哈希、验证码生成器、文件存储、缓存 provider、事件总线);按请求 `Scoped`(多数业务服务,与仓储一致)。

## 种子数据

- 实现 `ISeedData<TEntity>`(泛型版),`HasData()` 返回默认行。返回空集合合法(「库里已有就不播种」)。
- **固定 Id 保幂等**:种子只在缺失时补,不回改已存在行 —— 界面上的改动不会被重启覆盖。
- **Id 必须落在保留区间**(`Core/TenonSeedIds.cs`):内核 `[1, 999]`、消费者 `[1000, 4095]`、`4096+` 归雪花运行时。越界或 `Id=0` 启动即拒。

## 命名 / 组织

- 命名空间随目录;一类型一文件;`Sys*` 实体、`I*` 接口、`*Service`/`*Provider`/`*Filter`/`*Attribute` 后缀。
- 启用可空引用类型;`async` 方法带 `Async` 后缀并接受 `CancellationToken`(热路径)。
- 时间统一走注入的 `TimeProvider`(可测试),不用 `DateTime.Now` 裸调。

## 包管理

版本**集中管理**:增/改依赖在 `backend/Directory.Packages.props` 的 `<PackageVersion>` 里加,**不在各 `.csproj` 写版本号**。共享构建/NuGet 元数据在 `backend/Directory.Build.props`。

## 注释规范

- 公共类型/成员用 `/// <summary>` 说清职责与边界;关键取舍引用设计文档节号(`§N`/`TN`)。
- 行内注释解释 WHY(并发、事务顺序、边界、跨方言坑),不复述 WHAT。
- 中文注释,与既有代码一致。
- 刻意简化 / 有上限的实现用 `// ponytail:` 注释标注上限与升级路径。

---

> 更完整的说明与正反例见 [`docs/coding-standards.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/coding-standards.md)。
