# 新建业务模块开发指南

> 以现有「字典」模块为蓝本，一步步走完“加一个业务（如 `Product` 商品）”的端到端流程。
> 规范细则见 [`coding-standards.md`](./coding-standards.md)。
> 两条路线：**A. 在内核内加**（本仓库直接加 `Sys*`/内置控制器）；**B. 消费方在自己的业务程序集里加**（推荐给使用方，靠 `ApplicationAssemblies` 挂载，不改内核）。步骤基本一致，差异见 A11。

以下以 `Product` 为例（把 `Product` 换成你的实体名）。

---

## A. 后端

### A1. 实体　`Services/Entities/Product.cs`

蓝本：`Entities/SysDictType.cs`。选基类：普通表继承 `BaseEntity`；**需要按机构做数据隔离**的业务表继承 `DataEntity`（自动带 `CreateOrgId` 锚点 + 数据范围过滤）。

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

- 审计字段（Id/CreateTime/CreateUserId/CreateOrgId/UpdateTime/UpdateUserId）由 AOP 自动填，**不要手写**。
- CodeFirst 会自动建表：内核内加时该实体在 `TenonAdmin.Services` 程序集，已在扫描范围。

> ✅ **`DataEntity` 写路径已默认安全（P2-21）**：数据范围全局过滤器只作用于查询（SELECT），但 `SqlSugarRepository` 对 `IOrgScoped` 实体的 `Update`/`Delete` **已内置写路径范围守卫**——写前确认目标行在当前数据范围内，越权改删他机构行会被拒（返回 0）。默认安全，无需手动加。
> **仍建议**：改/删前先 `GetByIdAsync`（经范围过滤）校验存在，看不到即返回准确的"未找到/无权"，再写。抄写样板见消费方范本 `backend/tests/TenonAdmin.TestHost/`（`SampleDoc` + `SampleDocService` + `SampleDocController` 全套 DataEntity CRUD）。绕过仓储走 `Db.Updateable/Deleteable` 逃生舱口的写不受守卫，需自行校验归属。

### A2. DTO　`Services/Product/ProductModels.cs`

```csharp
public record ProductPageInput : PageInput { public string? Name { get; init; } }
public record ProductInput(string Code, string Name, bool Enabled);
```

### A3. 服务接口 + 实现　`Services/Product/IProductService.cs` + `ProductService.cs`

蓝本：`Dict/IDictService.cs` + `DictService.cs`。方法 `virtual`，校验用 `AdminException.ThrowIf`，多写包事务，热读加缓存（见 A7）。

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

> **必须 `TryAdd`**（不是 `Add`），保证消费方可前置替换。

### A5. 控制器　`AspNetCore/Controllers/ProductController.cs`

蓝本：`Controllers/DictController.cs`。

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

- 每个动作挂 `[RolePermission]`——**权限码自动等于路由**（如 `GET:/api/v1/biz/product/page`），无需写任何权限字符串。
- 需审计的写操作加 `[OperationLog(...)]`。

### A6. 错误码　`Core/ErrorCode.cs`

往枚举加 `ProductCodeExists`、`ProductNotFound` 等。**只加数字码，不写文案**（文案在前端 `locales/*` 按码补）。

### A7. 缓存决策

- **不是所有查询都要缓存**——列表分页、后台管理查询直接查库即可（现有 Dict/Config 的分页也没缓存）。
- **只对“高频读 + 低频变”的热点加缓存**：如某类下拉数据源、全局配置。加时按 `coding-standards §1.8` 模板：`Core/CacheKeys.cs` 加逻辑键 → 服务内 cache-aside → 增删改后 `RemoveAsync` 显式失效（必要时广播事件）。
- 判断依据：这条读会不会被每个请求/每个页面反复打？会 → 缓存；只在管理页偶尔查 → 不缓存。

### A8. 种子（可选）　`Services/Seed/ProductSeed.cs`

需要出厂默认数据时实现 `ISeedData<Product>`，**固定小整数 Id 保幂等**，并在 `ServicesSetup` 用 `TryAddEnumerable` 注册。蓝本 `Seed/DictSeed.cs`。

### A9. 菜单与授权（让接口“可被授权”）

权限码 = 路由，授权靠在菜单上勾路由。所以新端点要能被普通用户访问，得有对应菜单节点：

1. 启动系统，进**菜单管理**页。
2. 建菜单节点：`Type=菜单`、`Path=/biz/product`、`Component=biz/product/index`、`所属应用`选顶级目录的模块。按钮级权限建 `Type=按钮` 节点，`Permission` 填对应路由码（如 `POST:/api/v1/biz/product`）。
3. 进**角色管理**，给角色勾选该菜单/按钮 → 该角色用户即获得对应路由权限（授权变更即时失效缓存生效）。
4. 也可用种子 `DefaultMenuSeed` 出厂预置菜单（蓝本 `Seed/DefaultMenuSeed.cs`）。

> 超管（`sadm`）自动见全部、放行全部，开发期无需配权。

### A10. 测试　`tests/TenonAdmin.Tests/`

用 `WebApplicationFactory`（蓝本 `ModulePortalTests.cs`）写 HTTP 级回归：造用户/授菜单 → 带 token 调端点 → 断言信封。SQLite/MySQL 双腿要绿（`TestDb.cs` 按环境变量派生隔离库）。

```bash
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~ProductTests"
```

### A11. 消费方路线（路线 B）

消费方不改内核，在自己的业务程序集里放实体/服务/控制器，然后：

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Product).Assembly);   // 实体建表 + 控制器挂载
});
// 自己的 IProductService 在 AddTenonAdmin() 之前 TryAdd/Add 即可
```

内核会把该程序集的实体并入 CodeFirst 建表、控制器 `AddApplicationPart`。其余（实体/服务/控制器/缓存/菜单）写法与路线 A 完全一致。

> ⚠️ **只有 `ApplicationAssemblies.Add(...)` 这条路生效**。`TenonAdminOptions.ScanApplicationAssemblies` 虽默认 `true`，但其文档注明“骨架暂未启用扫描”（当前未被读取，是空开关）。别指望它自动发现你的模块——必须显式 `Add` 程序集。

---

## B. 前端

### B1. 重新生成 API 类型

后端跑起来后：

```bash
cd web && npm run gen:api     # 从 /openapi/v1.json 重生成 src/api/schema.d.ts（勿手改）
```

新端点即出现在类型里。

### B2. 封装　`web/src/api/index.ts`

按域加一组：

```ts
export const productApi = {
  page: (params: { page: number; pageSize: number; name?: string }) =>
    client.GET('/api/v1/biz/product/page', {
      params: { query: { Current: params.page, Size: params.pageSize, Name: params.name } }, // PascalCase
    }).then((r) => unwrap<PagedList<Product>>(r)).then((p) => ({ items: p.items, total: p.total })),
  add: (body: ProductInput) => client.POST('/api/v1/biz/product', { body }).then((r) => unwrap<number>(r)),
  // update/remove 同 menuApi 风格
}
```

### B3. CRUD 视图　`web/src/views/biz/product/index.vue`

蓝本：`views/system/menu/index.vue`（含 `NDataTable` + `NModal` 表单 + `NPopconfirm`）。列表逻辑用 `useTable`：

```ts
const { loading, rows, pagination, search, onPage } = useTable(productApi.page, {
  initParams: { name: '' },
  onError: (e) => message.error(translateError(e)),
})
```

- 列用 `h()` 渲染，操作列放编辑/删除按钮。
- 所有可见文本走 `t('...')`，i18n key 见 B6。
- 危险按钮可挂 `v-auth="'POST:/api/v1/biz/product'"`（当前 fail-open，见规范 §2.6）。

### B4. 挂载菜单（页面才可见）

到**菜单管理**页建/确认菜单节点，关键是 `Component` 必须与文件路径对应：

| 字段 | 值 | 说明 |
|---|---|---|
| Type | 菜单 | 目录只作父节点，按钮只承载权限码 |
| Path | `/biz/product` | 即路由地址 |
| Component | `biz/product/index` | → `/src/views/biz/product/index.vue`（**不带前后缀**） |
| 所属应用 | 选模块 | 仅顶级目录有效 |

### B5. 路由如何解析（原理，无需手动加路由）

`composables/useAuthMenu.ts` 用 `import.meta.glob('/src/views/**/*.vue')` 把 `Component` 串映射到 `.vue` 文件，登录/刷新后自动注册为动态路由（名 `menu-${id}`，挂 `layout` 下）。**所以你不用动 `router/`**——建好 `.vue` + 配好菜单即可。若控制台报 `[menu] 缺少视图组件`，是 `Component` 串与文件路径没对上。

### B6. i18n　`web/src/locales/zh-CN.ts` / `en-US.ts`

加该页文案 key，以及 A6 里新错误码对应的翻译（`translateError` 按 code/msgKey 取）。

### B7. 提交前

```bash
npm run lint && npm run typecheck
```

---

## C. 端到端清单

**后端**
- [ ] 实体（选 `BaseEntity`/`DataEntity`）+ Sugar 特性 + 唯一索引
- [ ] `*Models.cs` DTO（record）
- [ ] `I*Service` + `*Service`（virtual、事务、查重带软删）
- [ ] `ServicesSetup` 里 `TryAddScoped` 注册
- [ ] 控制器（`[ApiController]`/`[Route]`/`[Module]`，每动作 `[RolePermission]`）
- [ ] `ErrorCode` 加码
- [ ] 热读才加缓存（`CacheKeys` + cache-aside + 失效）
- [ ] 种子（可选，固定 Id）
- [ ] 测试（`WebApplicationFactory`，SQLite/MySQL 双绿）

**前端**
- [ ] `npm run gen:api` 重生成类型
- [ ] `api/index.ts` 加一组
- [ ] `views/<模块>/<实体>/index.vue`（`useTable` + Naive 表格/表单）
- [ ] i18n 文案 + 错误码翻译
- [ ] `lint` + `typecheck` 通过

**配置权限（运行时）**
- [ ] 菜单管理建节点（Path/Component 对应）
- [ ] 角色管理勾选授权
