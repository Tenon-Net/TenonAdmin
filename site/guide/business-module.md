# Add a Business Module End-to-End

Add a business table and a set of endpoints on top of TenonAdmin without changing a single line of kernel code. This guide walks through the full chain: entity → service → controller → mounting → authorization → callable from the frontend.

::: tip Two routes
- **Route A** — modify this repo directly, adding inside the kernel.
- **Route B** — a consumer (a project that has installed the `TenonAdmin` NuGet package) adds it in their own business assembly, mounted via `options.ApplicationAssemblies`, **without touching kernel source**.

The two routes are identical except for "where the code lives." This guide follows Route B — the recommended route for consumers.
:::

## Grounded in real code

This isn't a made-up "product" example — it walks you through two places in the repo that **genuinely exist and run in CI**:

- `backend/src/TenonAdmin.Services/Dict/` — the kernel's built-in dictionary module, a template for a plain table (one that doesn't need per-organization data isolation).
- `backend/tests/TenonAdmin.TestHost/` — the consumer host used for integration tests, where `SampleDoc` is a genuine "org-isolated business module," following exactly Route B.

Following the structure of this code, the module you add will naturally take the shape the kernel expects.

## 1. Choosing an entity base class: `BaseEntity` or `DataEntity`

First ask yourself: does this table need per-organization data isolation?

- **No** (globally shared data, e.g. dictionaries, config) → inherit `BaseEntity`. The dictionary type entity `SysDictType` is like this (`backend/src/TenonAdmin.SqlSugar/Entities/`).
- **Yes** (users from different organizations should only see/modify their own organization's data) → inherit `DataEntity`. It comes with a `CreateOrgId` anchor, and queries are automatically trimmed by the global filter according to the current user's data scope.

`backend/tests/TenonAdmin.TestHost/SampleDoc.cs` is a real example of the latter:

```csharp
using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.TestHost;

[SugarTable("sample_doc", TableDescription = "Sample org-isolated business entity (integration test)")]
public class SampleDoc : DataEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "Title")]
    public string Title { get; set; } = "";
}
```

Audit fields (`Id`/`CreateTime`/`CreateUserId`/`CreateOrgId`/`UpdateTime`/`UpdateUserId`) are auto-filled by AOP — **business code should never set them by hand**, especially `CreateOrgId`, which is the anchor that makes data scoping work. Leaving it unset means org-scoped queries return zero rows.

Swap `SampleDoc` for your own entity name and fields, and put it in your consumer project's own assembly (Route A would put it in `TenonAdmin.Services` instead).

## 2. Service interface + implementation

The contract, `ISampleDocService.cs`:

```csharp
public interface ISampleDocService
{
    Task<long> CreateAsync(string title);
    Task<IReadOnlyList<SampleDoc>> ListAsync();
    Task<bool> RenameAsync(long id, string title);
    Task<bool> DeleteAsync(long id);
}
```

The implementation, `SampleDocService.cs` — note the three key points about reading/writing with data scope:

```csharp
public class SampleDocService(IRepository<SampleDoc> repo) : ISampleDocService
{
    public virtual async Task<long> CreateAsync(string title)
    {
        var doc = new SampleDoc { Title = title };
        await repo.InsertAsync(doc);   // CreateOrgId is auto-filled from the current user's org by the audit AOP
        return doc.Id;
    }

    public virtual async Task<IReadOnlyList<SampleDoc>> ListAsync() =>
        await repo.AsQueryable().OrderBy(d => d.Id).ToListAsync();  // the global filter automatically trims by data scope

    public virtual async Task<bool> RenameAsync(long id, string title)
    {
        var doc = await repo.GetByIdAsync(id);   // out-of-scope/nonexistent → null (also filtered by scope)
        if (doc is null) return false;
        doc.Title = title;
        return await repo.UpdateAsync(doc) > 0;  // the repository write path also guards against out-of-scope updates/deletes as a second line of defense
    }

    public virtual async Task<bool> DeleteAsync(long id)
    {
        if (await repo.GetByIdAsync(id) is null) return false;
        return await repo.DeleteAsync(id) > 0;
    }
}
```

- **Reads** go through `AsQueryable()`, and the global filter automatically trims by the current request's data scope — business code never writes `WHERE` clauses by hand.
- **Updates/deletes check visibility with `GetByIdAsync` first** — if it can't be seen, treat it as "not found/no access"; the repository's `Update`/`Delete` for `DataEntity` also has a built-in write-path scope guard, so it's double-protected.
- All methods are `virtual` — if a consumer needs to override a step in the flow, they just subclass and override that single method, without copying the whole thing.

::: tip Do you need caching?
Not every query needs a cache. Only "high-frequency read + low-frequency change" hot spots (e.g. a dropdown data source) are worth caching — see the read-through cache pattern in `GetItemsByTypeAsync` in `backend/src/TenonAdmin.Services/Dict/DictService.cs` (`ICacheProvider` + explicit `RemoveAsync` invalidation). Cold paths like admin-panel pagination can just hit the database directly — neither `Dict`'s nor `SampleDoc`'s paging/list endpoints are cached.
:::

## 3. Controller

`SampleDocController.cs` — every action has `[RolePermission]` attached, and **the permission code is the normalized route itself**, with no permission strings written anywhere:

```csharp
[ApiController]
[Route("api/v1/sample/doc")]
public class SampleDocController(ISampleDocService svc) : ControllerBase
{
    [HttpGet]
    [RolePermission]
    public async Task<Result<IReadOnlyList<SampleDoc>>> List() =>
        Result<IReadOnlyList<SampleDoc>>.Ok(await svc.ListAsync());

    [HttpPost]
    [RolePermission]
    public async Task<Result<long>> Create([FromBody] SampleDocInput input) =>
        Result<long>.Ok(await svc.CreateAsync(input.Title));

    [HttpPut("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Rename(long id, [FromBody] SampleDocInput input) =>
        Result<bool>.Ok(await svc.RenameAsync(id, input.Title));

    [HttpDelete("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Delete(long id) =>
        Result<bool>.Ok(await svc.DeleteAsync(id));
}

public record SampleDocInput(string Title);
```

The permission code for the `GET /api/v1/sample/doc` action is `GET:/api/v1/sample/doc` — to grant it, an admin attaches this route to a menu item/button in menu management, then checks it for a role in role management, and users with that role gain the permission. The super admin (`sadm` claim) automatically bypasses this.

## 4. Register services + mount the assembly (the key step in Route B)

Without touching the kernel, a consumer does two things in their own `Program.cs` — see `backend/tests/TenonAdmin.TestHost/Program.cs` for the full example:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    // Hand your own assembly to the kernel: its entities join CodeFirst table creation, and its controllers are mounted via AddApplicationPart
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
});

// Your own services can be TryAdd'd before or after AddTenonAdmin() (for interfaces not already claimed by the kernel)
builder.Services.TryAddScoped<ISampleDocService, SampleDocService>();

var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

::: warning Only `ApplicationAssemblies.Add(...)` makes it work
The kernel **does not auto-discover** your module. Forget this one line, and the `SampleDoc` entity won't get a table and `SampleDocController` won't register its routes — a plain 404, with no fallback switch.
:::

::: tip Must use `TryAdd`, not `Add`
Keep registering services with `TryAddScoped` so consumers can register their own implementation of the same interface *before* `AddTenonAdmin()` to override the default behavior — this is the foundational rule behind the kernel's entire "replaceable" design. Built-in services (like `Dict`) are registered the same way in `ServicesSetup.cs`.
:::

## 5. Error codes (optional)

If you need to precisely distinguish "not found" from other failures, the kernel's built-in modules add a numeric code to the `Core/ErrorCode.cs` enum — codes only, no message text (the dictionary module's `DictTypeNotFound = 43001` and `DictTypeCodeExists = 43002` are examples); the message text is translated by code on the frontend. Since consumers can't extend `ErrorCode`, they can express results directly via return values as `SampleDocService` does (`false` meaning not found/no access), or use a custom exception handled by their own exception filter as a fallback.

## 6. Seed data (optional)

When you need out-of-the-box default data, implement the generic `ISeedData<T>`, and **fixed Ids must fall within the consumer reserved range `[1000, 4095]`** (`TenonSeedIds.ConsumerMin` to `ConsumerMax`). Seeding outside this range fails startup immediately and tells you exactly which range to use — the kernel's own built-in seeds occupy `[1, 999]`, and snowflake ID issuance at runtime starts from `4096`; seeding into either range will eventually collide with a primary key.

Seeds must be registered in **your own `Program.cs`** (the kernel doesn't scan assemblies for seeds — `ApplicationAssemblies` only handles entity table creation and controller mounting):

```csharp
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, YourSeed>());
```

## 7. Attach a menu and grant access (making the endpoint "reachable")

Since the permission code equals the route, authorization works by checking routes on the menu tree — so for a new endpoint to be callable by a regular user, it needs a corresponding menu node first:

1. Start the system, go to the **Menu Management** page, and create a menu node: `Type=Menu`, `Path` set to the frontend route (e.g. `/sample/doc`), `Component` set to the corresponding `.vue` file path (without prefix/suffix), and `App` set to a top-level directory.
2. If you need button-level permissions, create a `Type=Button` node with `Permission` set to the corresponding route code (e.g. `POST:/api/v1/sample/doc`).
3. Go to **Role Management** and check this menu/button for a role — users with that role gain the corresponding route permission, and the authorization change takes effect immediately (the kernel invalidates the corresponding cache).
4. During development the super admin doesn't need any permission configuration — all routes are allowed through automatically.

## 8. Testing

Write HTTP-level regression tests with `WebApplicationFactory` — use the existing tests under `backend/tests/TenonAdmin.Tests/` as a template: create a user/grant a menu → call the endpoint with a token → assert the envelope. Keep both the SQLite and MySQL legs green:

```bash
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~SampleDoc"
```

## 9. Frontend wiring

Once the backend is running, the full steps on the frontend side (regenerating types, wrapping the API, writing the CRUD page, attaching the menu) are covered in the next guide: [Add a Frontend Page](/guide/frontend-page).

## End-to-end checklist

**Backend**
- [ ] Entity (choose `BaseEntity`/`DataEntity`) + Sugar attributes
- [ ] `I*Service` + `*Service` (methods `virtual`, check visibility first on update/delete)
- [ ] Consumer `Program.cs`: `ApplicationAssemblies.Add(...)` + `TryAddScoped` service registration
- [ ] Controller (`[ApiController]`/`[Route]`, every action with `[RolePermission]`)
- [ ] Error codes (optional)
- [ ] Seeds (optional, fixed Ids within `[1000, 4095]`)
- [ ] Tests (`WebApplicationFactory`, SQLite/MySQL both green)

**Configure permissions (runtime)**
- [ ] Create nodes in Menu Management (`Path`/`Component` matching the frontend route and file)
- [ ] Check permissions in Role Management

> For fuller specification details (paged DTO conventions, caching decisions, when to bump `SysSchemaVersion`, etc.), see [Business Module Development Guide: A. Backend](/guide/business-module).


---

<!-- TODO(rewrite): merged from backend.md -->

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
`dotnet new tenon-app` directly generates a runnable project with the above wiring already done, plus a `DataEntity` sample module (see [Quick Start](/guide/business-module)). After that, adding a new module = copy the generated `Modules/SampleDoc*` four-piece set, rename it, and add one `TryAddScoped` line to `Program.cs`.
:::

::: warning Only `ApplicationAssemblies.Add(...)` actually works
The kernel **doesn't auto-discover** your modules — you must explicitly `Add` the assembly, or entities won't get tables and controllers will 404. (There used to be a `ScanApplicationAssemblies` switch that was never implemented; it was removed before release, on 2026-07-14.)
:::

