# A. 后端

### A1. 实体　`Services/Entities/Product.cs`

蓝本:`Entities/SysDictType.cs`。选基类:普通表继承 `BaseEntity`;**需要按机构做数据隔离**的业务表继承 `DataEntity`(自动带 `CreateOrgId` 锚点 + 数据范围过滤)。

```csharp
[SugarTable("biz_product", TableDescription = "商品")]
[SugarIndex("idx_biz_product_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
public class Product : DataEntity   // 或 BaseEntity
{
    [SugarColumn(Length = 64, ColumnDescription = "商品编码(唯一)")]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 128, ColumnDescription = "名称")]
    public string Name { get; set; } = "";

    [SugarColumn(ColumnDescription = "是否上架")]
    public bool Enabled { get; set; } = true;
}
```

- 审计字段(Id/CreateTime/CreateUserId/CreateOrgId/UpdateTime/UpdateUserId)由 AOP 自动填,**不要手写**。
- CodeFirst 会自动建表:内核内加时该实体在 `TenonAdmin.Services` 程序集,已在扫描范围。

::: tip DataEntity 写路径已默认安全(P2-21)
数据范围全局过滤器只作用于查询(SELECT),但 `SqlSugarRepository` 对 `IOrgScoped` 实体的 `Update`/`Delete` **已内置写路径范围守卫**——写前确认目标行在当前数据范围内,越权改删他机构行会被拒(返回 0)。默认安全,无需手动加。

**仍建议**:改/删前先 `GetByIdAsync`(经范围过滤)校验存在,看不到即返回准确的"未找到/无权",再写。抄写样板见消费方范本 `backend/tests/TenonAdmin.TestHost/`(`SampleDoc` + `SampleDocService` + `SampleDocController` 全套 DataEntity CRUD)。绕过仓储走 `Db.Updateable/Deleteable` 逃生舱口的写不受守卫,需自行校验归属。
:::

### A2. DTO　`Services/Product/ProductModels.cs`

```csharp
// 基类是 PageInputBase(自带 Current/Size + SortField/SortOrder),不是 PageInput —— 后者不存在
public record ProductPageInput : PageInputBase { public string? Name { get; init; } }
public record ProductInput(string Code, string Name, bool Enabled);
```

### A3. 服务接口 + 实现　`Services/Product/IProductService.cs` + `ProductService.cs`

蓝本:`Dict/IDictService.cs` + `DictService.cs`。方法 `virtual`,校验用 `AdminException.ThrowIf`,多写包事务,热读加缓存(见 A7)。

```csharp
public class ProductService(IRepository<Product> repo) : IProductService
{
    public virtual async Task<PagedList<Product>> PageAsync(ProductPageInput input) =>
        await repo.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Name), p => p.Name.Contains(input.Name!))
            .OrderBy(p => p.Id)
            .ToPagedListAsync(input.Current, input.Size);

    public virtual async Task<long> AddAsync(ProductInput input)
    {
        // 查重带上软删行,否则撞库唯一索引抛原生 500
        AdminException.ThrowIf(
            await repo.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(p => p.Code == input.Code),
            ErrorCode.ProductCodeExists);
        var e = new Product { Code = input.Code, Name = input.Name, Enabled = input.Enabled };
        await repo.InsertAsync(e);
        return e.Id;
    }
    // Update/Delete 同 DictService 风格
}
```

### A4. 注册　`Services/ServicesSetup.cs`

```csharp
services.TryAddScoped<IProductService, ProductService>();
```

> **必须 `TryAdd`**(不是 `Add`),保证消费方可前置替换。

### A5. 控制器　`AspNetCore/Controllers/ProductController.cs`

蓝本:`Controllers/DictController.cs`。

```csharp
[ApiController]
[Route("api/v1/biz/product")]
[Module("Product")]
public class ProductController(IProductService svc) : ControllerBase
{
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<Product>>> Page([FromQuery] ProductPageInput input) =>
        Result<PagedList<Product>>.Ok(await svc.PageAsync(input));

    [HttpPost]
    [RolePermission]
    public async Task<Result<long>> Add(ProductInput input) =>
        Result<long>.Ok(await svc.AddAsync(input));
    // Put/Delete 同理
}
```

- 每个动作挂 `[RolePermission]`——**权限码自动等于路由**(如 `GET:/api/v1/biz/product/page`),无需写任何权限字符串。
- 需审计的写操作加 `[OperationLog(...)]`。

### A6. 错误码　`Core/ErrorCode.cs`

往枚举加 `ProductCodeExists`、`ProductNotFound` 等。**只加数字码,不写文案**(文案在前端 `locales/*` 按码补)。

### A7. 缓存决策

- **不是所有查询都要缓存**——列表分页、后台管理查询直接查库即可(现有 Dict/Config 的分页也没缓存)。
- **只对"高频读 + 低频变"的热点加缓存**:如某类下拉数据源、全局配置。加时按[代码规范](/zh/standard/backend)的缓存模板:`Core/CacheKeys.cs` 加逻辑键 → 服务内 cache-aside → 增删改后 `RemoveAsync` 显式失效(必要时广播事件)。
- 判断依据:这条读会不会被每个请求/每个页面反复打?会 → 缓存;只在管理页偶尔查 → 不缓存。

### A8. 种子(可选)　`Services/Seed/ProductSeed.cs`

需要出厂默认数据时实现 **`ISeedData<Product>`**(泛型版;非泛型 `ISeedData` 只是 DI 收集用的空标记,直接实现它能编译但启动会炸),固定 Id 保幂等。蓝本 `Seed/DictSeed.cs`,范例 `tests/TenonAdmin.TestHost/SampleWidgetSeed.cs`。

**Id 必须落在消费者保留区间 `[1000, 4095]`**(`TenonSeedIds.ConsumerMin` ~ `ConsumerMax`):

| 区间 | 归谁 | 为什么 |
|---|---|---|
| `[1, 999]` | 内核内置种子 | 内核每加一个鉴权端点就多一行菜单,号段只会往上涨 |
| `[1000, 4095]` | **你的种子** | |
| `[4096, ...]` | 雪花运行时发号区 | `id = 毫秒 × 4096 + 低位`,种子占了它,将来某次插入必然主键冲突 |

在这个区间外播种,**启动直接失败**并告诉你该用哪段。别沿用「随手挑个小整数」的老习惯——你和内核会往**同一批表**(`sys_menu` / `sys_config` …)里播种,今天不撞不代表升级内核包之后不撞,而那时你的库里已经有那行了,退不回去。

注册在**你自己的 `Program.cs`** 里(内核不扫描程序集找种子;`ApplicationAssemblies` 只管实体建表和控制器挂载,**不管种子**,忘了注册就静默不执行):

```csharp
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, ProductSeed>());
```

### A9. 菜单与授权(让接口"可被授权")

权限码 = 路由,授权靠在菜单上勾路由。所以新端点要能被普通用户访问,得有对应菜单节点:

1. 启动系统,进**菜单管理**页。
2. 建菜单节点:`Type=菜单`、`Path=/biz/product`、`Component=biz/product/index`、`所属应用`选顶级目录的模块。按钮级权限建 `Type=按钮` 节点,`Permission` 填对应路由码(如 `POST:/api/v1/biz/product`)。
3. 进**角色管理**,给角色勾选该菜单/按钮 → 该角色用户即获得对应路由权限(授权变更即时失效缓存生效)。
4. 也可用种子 `DefaultMenuSeed` 出厂预置菜单(蓝本 `Seed/DefaultMenuSeed.cs`)。

::: tip 让「配置权限」的路由下拉按应用过滤
在**模块管理**里给你的业务应用填「路由前缀 `apiPrefix`」= 控制器的路由段(如 `biz`,对应 `/api/v1/biz/...`)。之后在菜单页给页面点「配置权限」建按钮时,路由下拉默认只列该应用的路由,勾「显示全部应用路由」才看其余应用。**填的是路由段 `biz`,不是模块编码 `business`**(二者不一致,内核系统模块 code=`system` 而路由段=`sys`);留空 = 不过滤,退化为全量。此过滤只是 UI 降噪,非权限边界——模块不是权限轴,跨应用挂码仍会真实授权。
:::

::: warning 改了已有种子行要 bump `SysSchemaVersion.Current`
给内置模块新增 `ApiPrefix` 这类「同 Id 改字段」的种子改动,老库只有版本号变化时才会经 `SyncOnUpgrade` 回填(见 `SqlSugar/Entities/SysSchemaVersion.cs` 注释)。新增行不需要 bump。
:::

> 超管(`sadm`)自动见全部、放行全部,开发期无需配权。

### A10. 测试　`tests/TenonAdmin.Tests/`

用 `WebApplicationFactory`(蓝本 `ModulePortalTests.cs`)写 HTTP 级回归:造用户/授菜单 → 带 token 调端点 → 断言信封。SQLite/MySQL 双腿要绿(`TestDb.cs` 按环境变量派生隔离库)。

```bash
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~ProductTests"
```

### A11. 消费方路线(路线 B)

消费方不改内核,在自己的业务程序集里放实体/服务/控制器,然后:

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Product).Assembly);   // 实体建表 + 控制器挂载
});
// 自己的 IProductService 在 AddTenonAdmin() 之前 TryAdd/Add 即可
```

内核会把该程序集的实体并入 CodeFirst 建表、控制器 `AddApplicationPart`。其余(实体/服务/控制器/缓存/菜单)写法与路线 A 完全一致。

::: tip 不想手搭 host?
`dotnet new tenon-app` 直接生成已接好上面这段接线 + 一个 `DataEntity` 示例模块的可运行工程(见[快速开始](/zh/guide/new-business/))。此后新增模块 = 复制生成物 `Modules/SampleDoc*` 四件套改名 + `Program.cs` 补一行 `TryAddScoped`。
:::

::: warning 只有 `ApplicationAssemblies.Add(...)` 这条路生效
内核**不会自动发现**你的模块——必须显式 `Add` 程序集,否则实体不建表、控制器 404。(曾有个 `ScanApplicationAssemblies` 开关从未实现,已于 2026-07-14 在发包前删除。)
:::

**上一节:** [新建业务模块开发指南](/zh/guide/new-business/)
**下一节:** [B. 前端](/zh/guide/new-business/frontend)
