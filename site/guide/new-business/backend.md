# A. Backend

### A1. Entity: `Services/Entities/Product.cs`

Template: `Entities/SysDictType.cs`. Pick a base class: plain tables inherit `BaseEntity`; business tables that **need org-based data isolation** inherit `DataEntity` (which automatically carries the `CreateOrgId` anchor + data-scope filtering).

```csharp
[SugarTable("biz_product", TableDescription = "Product")]
[SugarIndex("idx_biz_product_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
public class Product : DataEntity   // or BaseEntity
{
    [SugarColumn(Length = 64, ColumnDescription = "Product code (unique)")]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 128, ColumnDescription = "Name")]
    public string Name { get; set; } = "";

    [SugarColumn(ColumnDescription = "Whether it's listed")]
    public bool Enabled { get; set; } = true;
}
```

- Audit fields (Id/CreateTime/CreateUserId/CreateOrgId/UpdateTime/UpdateUserId) are auto-filled by AOP — **don't set them by hand**.
- CodeFirst creates the table automatically: when added inside the kernel, the entity lives in the `TenonAdmin.Services` assembly, which is already scanned.

::: tip DataEntity write paths are secure by default (P2-21)
The data-scope global query filter only applies to reads (SELECT), but `SqlSugarRepository`'s `Update`/`Delete` for `IOrgScoped` entities **already has a built-in write-path scope guard** — it confirms the target row is within the current data scope before writing, and rejects (returning 0) any out-of-scope attempt to modify/delete another org's row. This is safe by default; no manual handling needed.

**Still recommended**: call `GetByIdAsync` (which is scope-filtered) before an update/delete to confirm the row exists — if it's not visible, return an accurate "not found/no permission" instead of writing. For boilerplate to copy from, see the consumer example `backend/tests/TenonAdmin.TestHost/` (the full `SampleDoc` + `SampleDocService` + `SampleDocController` DataEntity CRUD set). Writes that bypass the repository via the `Db.Updateable/Deleteable` escape hatch aren't covered by the guard and need their own ownership checks.
:::

### A2. DTOs: `Services/Product/ProductModels.cs`

```csharp
// The base class is PageInputBase (which already has Current/Size + SortField/SortOrder) — not PageInput, which doesn't exist
public record ProductPageInput : PageInputBase { public string? Name { get; init; } }
public record ProductInput(string Code, string Name, bool Enabled);
```

### A3. Service interface + implementation: `Services/Product/IProductService.cs` + `ProductService.cs`

Template: `Dict/IDictService.cs` + `DictService.cs`. Methods are `virtual`, validation uses `AdminException.ThrowIf`, writes are wrapped in transactions, and hot reads get cached (see A7).

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
        // Include soft-deleted rows in the duplicate check, or a unique-index collision throws a raw 500
        AdminException.ThrowIf(
            await repo.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(p => p.Code == input.Code),
            ErrorCode.ProductCodeExists);
        var e = new Product { Code = input.Code, Name = input.Name, Enabled = input.Enabled };
        await repo.InsertAsync(e);
        return e.Id;
    }
    // Update/Delete follow the same style as DictService
}
```

### A4. Registration: `Services/ServicesSetup.cs`

```csharp
services.TryAddScoped<IProductService, ProductService>();
```

> **Must use `TryAdd`** (not `Add`), so a consumer can replace it beforehand.

### A5. Controller: `AspNetCore/Controllers/ProductController.cs`

Template: `Controllers/DictController.cs`.

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
    // Put/Delete follow the same pattern
}
```

- Every action carries `[RolePermission]` — **the permission code automatically equals the route** (e.g. `GET:/api/v1/biz/product/page`), so no permission string needs to be written anywhere.
- Add `[OperationLog(...)]` to writes that need auditing.

### A6. Error codes: `Core/ErrorCode.cs`

Add entries to the enum such as `ProductCodeExists`, `ProductNotFound`. **Only add the numeric codes, no message text** (message text lives in the frontend's `locales/*`, keyed by code).

### A7. Caching decisions

- **Not every query needs caching** — list pagination and admin-page queries can hit the database directly (even the existing Dict/Config pagination isn't cached).
- **Only cache "hot read + rarely changes" hotspots**: e.g. a dropdown data source, global config. When you do, follow the caching template in the [Coding Standards](/standard/backend): add a logical key to `Core/CacheKeys.cs` → cache-aside inside the service → explicit `RemoveAsync` invalidation after writes (broadcast an event if needed).
- Rule of thumb: will this read get hit on every request/every page load? Cache it. Only queried occasionally on an admin page? Don't.

### A8. Seed data (optional): `Services/Seed/ProductSeed.cs`

Implement the **generic `ISeedData<Product>`** if you need factory-default data (the non-generic `ISeedData` is just an empty marker used for DI collection — implementing it directly compiles but crashes at startup), and use a fixed Id for idempotency. Template: `Seed/DictSeed.cs`; example: `tests/TenonAdmin.TestHost/SampleWidgetSeed.cs`.

**The Id must fall within the consumer-reserved range `[1000, 4095]`** (`TenonSeedIds.ConsumerMin` ~ `ConsumerMax`):

| Range | Belongs to | Why |
|---|---|---|
| `[1, 999]` | Kernel built-in seeds | Every new authenticated endpoint the kernel adds means one more menu row, so the range only ever grows |
| `[1000, 4095]` | **Your seeds** | |
| `[4096, ...]` | Snowflake runtime ID range | `id = milliseconds × 4096 + low bits`; a seed using this range will eventually collide with a real insert's primary key |

Seeding outside this range **fails startup outright** and tells you which range to use. Don't fall back on the old habit of "just pick a small integer" — you and the kernel are seeding into the **same tables** (`sys_menu` / `sys_config` …), and not colliding today doesn't mean you won't after the next kernel upgrade — by then the row is already in your database, with no way back.

Register it in **your own `Program.cs`** (the kernel doesn't scan assemblies looking for seeds; `ApplicationAssemblies` only handles entity table creation and controller mounting — **not seeding** — forgetting to register it means it silently never runs):

```csharp
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, ProductSeed>());
```

### A9. Menus and authorization (making the endpoint "authorizable")

The permission code equals the route, and authorization is granted by checking routes on a menu. So for the new endpoint to be accessible to a regular user, it needs a corresponding menu node:

1. Start the system and go to the **menu management** page.
2. Create the menu node: `Type=Menu`, `Path=/biz/product`, `Component=biz/product/index`, and pick the top-level directory's module for `Application`. For button-level permissions, create `Type=Button` nodes with `Permission` set to the corresponding route code (e.g. `POST:/api/v1/biz/product`).
3. Go to **role management** and check that menu/button for a role → users with that role get the corresponding route permission (authorization changes invalidate the cache immediately).
4. You can also ship default menus via a seed, `DefaultMenuSeed` (template: `Seed/DefaultMenuSeed.cs`).

::: tip Filtering the "configure permissions" route dropdown by application
In **module management**, fill in a "route prefix `apiPrefix`" for your business app — the controller's route segment (e.g. `biz`, matching `/api/v1/biz/...`). Then, when creating a button and clicking "configure permissions" on the menu page, the route dropdown lists only that application's routes by default; check "show all application routes" to see the rest. **You fill in the route segment `biz`, not the module code `business`** (the two don't match — the kernel's system module has code=`system` but route segment=`sys`); leaving it blank = no filtering, falling back to showing everything. This filter is purely a UI decluttering aid, not a permission boundary — a module isn't a permission axis, and mounting a code under a different application still grants real authorization.
:::

::: warning Editing an existing seed row requires bumping `SysSchemaVersion.Current`
For a "same Id, changed field" seed edit — like adding `ApiPrefix` to a built-in module — an existing database only gets that change backfilled via `SyncOnUpgrade` when the version number changes (see the comment on `SqlSugar/Entities/SysSchemaVersion.cs`). New rows don't need a version bump.
:::

> Super admin (`sadm`) automatically sees and can access everything, so no permission setup is needed during development.

### A10. Tests: `tests/TenonAdmin.Tests/`

Use `WebApplicationFactory` (template: `ModulePortalTests.cs`) to write HTTP-level regression tests: create a user/grant a menu → call the endpoint with a token → assert on the envelope. Both the SQLite and MySQL legs must pass (`TestDb.cs` derives an isolated database per environment variable).

```bash
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~ProductTests"
```

### A11. Consumer route (Route B)

A consumer doesn't touch the kernel — they place entities/services/controllers in their own business assembly, then:

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Product).Assembly);   // entity table creation + controller mounting
});
// Register your own IProductService with TryAdd/Add before AddTenonAdmin()
```

The kernel merges that assembly's entities into CodeFirst table creation and `AddApplicationPart`s its controllers. Everything else (entities/services/controllers/caching/menus) is written exactly as in Route A.

::: tip Don't want to wire up a host by hand?
`dotnet new tenon-app` directly generates a runnable project with the above wiring already done, plus a `DataEntity` sample module (see [Quick Start](/guide/new-business/)). After that, adding a new module = copy the generated `Modules/SampleDoc*` four-piece set, rename it, and add one `TryAddScoped` line to `Program.cs`.
:::

::: warning Only `ApplicationAssemblies.Add(...)` actually works
The kernel **doesn't auto-discover** your modules — you must explicitly `Add` the assembly, or entities won't get tables and controllers will 404. (There used to be a `ScanApplicationAssemblies` switch that was never implemented; it was removed before release, on 2026-07-14.)
:::

**Previous:** [Guide: Adding a New Business Module](/guide/new-business/)
**Next:** [B. Frontend](/guide/new-business/frontend)
