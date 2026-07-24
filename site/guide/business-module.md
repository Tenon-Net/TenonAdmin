# Add a Business Module (Backend)

Add a business table and a set of endpoints on top of TenonAdmin without changing a single line of kernel code. This page walks from choosing an entity base class all the way to an endpoint that can be authorized and called from the frontend.

::: tip Two routes, differing only in "where the code lives"
- **Route A** — modify this repo directly, adding inside the kernel; the new code goes into `TenonAdmin.Services` / `TenonAdmin.AspNetCore`.
- **Route B** — your project has installed the `TenonAdmin` NuGet package, and you add it in your own business assembly, mounted via `options.ApplicationAssemblies`, without touching a line of kernel source.

Apart from "where," the two routes are identical. Below follows Route B — also the recommended route for consumers.
:::

## Grounded in real code

Rather than inventing a "product" example, this page walks you through two places in the repo that genuinely exist and run in CI:

- `backend/src/TenonAdmin.Services/Dict/` — the kernel's built-in dictionary module, a template for a plain table (not org-isolated).
- `backend/tests/TenonAdmin.TestHost/` — the consumer host used for integration tests, where `SampleDoc` is a genuine org-isolated business module, following exactly Route B.

Follow the structure of these two, and the module you add will naturally grow into the shape the kernel expects.

## Choosing an entity base class: `BaseEntity` or `DataEntity`

Which base class you pick comes down to one thing: whether this table needs per-organization data isolation.

- **No** (globally shared, e.g. dictionaries, config) → inherit `BaseEntity`. The dictionary-type entity `SysDictType` (`backend/src/TenonAdmin.SqlSugar/Entities/`) is like this.
- **Yes** (users in different orgs should only see and modify their own org's data) → inherit `DataEntity`. It carries a `CreateOrgId` anchor, and queries are automatically trimmed by the global filter according to the current user's data scope.

`backend/tests/TenonAdmin.TestHost/SampleDoc.cs` is a real example of the latter:

```csharp
[SugarTable("sample_doc", TableDescription = "示例机构隔离业务实体(集成测试)")]
public class SampleDoc : DataEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "标题")]
    public string Title { get; set; } = "";
}
```

Audit fields (`Id` / `CreateTime` / `CreateUserId` / `CreateOrgId` / `UpdateTime` / `UpdateUserId`) are auto-filled by AOP — business code shouldn't write them by hand, especially `CreateOrgId`, the anchor that makes data scoping work: leave it unset and org-scoped queries return zero rows. When you have a unique column, add a unique index on the entity, e.g. `[SugarIndex("idx_sample_doc_title", nameof(Title), OrderByType.Asc, IsUnique = true)]`.

Swap `SampleDoc` for your own entity name and fields, and drop it into your consumer project's own assembly (only Route A puts it in `TenonAdmin.Services`).

## Service: reads go through the filter, writes check visibility first

The full template for the contract and implementation is `backend/tests/TenonAdmin.TestHost/SampleDocService.cs`. Three read/write points decide whether it "holds up" as org-isolated:

```csharp
public class SampleDocService(IRepository<SampleDoc> repo) : ISampleDocService
{
    public virtual async Task<long> CreateAsync(string title)
    {
        var doc = new SampleDoc { Title = title };
        await repo.InsertAsync(doc);   // CreateOrgId is backfilled from the current user's org by the audit AOP
        return doc.Id;
    }

    public virtual async Task<IReadOnlyList<SampleDoc>> ListAsync() =>
        await repo.AsQueryable().OrderBy(d => d.Id).ToListAsync();  // the global filter trims by data scope

    public virtual async Task<bool> RenameAsync(long id, string title)
    {
        var doc = await repo.GetByIdAsync(id);   // out-of-scope / nonexistent → null (also passes through the scope filter)
        if (doc is null) return false;
        doc.Title = title;
        return await repo.UpdateAsync(doc) > 0;
    }

    public virtual async Task<bool> DeleteAsync(long id)
    {
        if (await repo.GetByIdAsync(id) is null) return false;
        return await repo.DeleteAsync(id) > 0;
    }
}
```

- **Reads** go through `AsQueryable()`; the global filter trims by the current request's data scope, and business code writes no `WHERE`.
- **Updates/deletes check visibility with `GetByIdAsync` first**: if it can't be seen, treat it as "not found / no access" and return `false`. This isn't just politeness — the data-scope global filter only applies to queries (SELECT); the write path relies on `SqlSugarRepository`'s built-in scope guard on `Update`/`Delete` for `DataEntity`, which returns 0 for an out-of-scope attempt to modify or delete another org's row. It takes both layers stacked to be airtight. Note that writes bypassing the repository, straight through the `Db.Updateable` / `Db.Deleteable` escape hatch, are **not** covered by this guard — you have to check ownership yourself.
- All methods are `virtual`. A consumer wanting to override one step just subclasses and overrides that single method, without copying the whole thing.

A real admin list usually needs paging, so swap `ListAsync` for `PageAsync`, with the input inheriting `PageInputBase` (which already carries `Current` / `Size` / `SortField` / `SortOrder` — there's no base class called `PageInput`, don't mix them up). Build conditions with `WhereIF(condition, expression)`: it splices in that `Where` clause only when `condition` is true, skips it otherwise, so you're not hand-rolling a chain of `if`s. Page with `.ToPagedListAsync(input.Current, input.Size)` directly — feed it the page number and page size, and it hands back the already-sliced page plus the total count. `UserService.PageAsync` and `DictService.PageTypesAsync` in the kernel are ready-made blueprints.

With a unique column, the pre-insert duplicate check must include soft-deleted rows: `repo.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(x => x.Code == input.Code)`. Without clearing the soft-delete filter, a soft-deleted row with the same code slips past the application-layer check and collides on the database's unique index as a raw 500; on a duplicate, throw a business code with `AdminException.ThrowIf(dup, ErrorCode.XxxExists)`.

Caching isn't needed on every query. Cold paths like lists and pagination can just hit the database (neither the kernel's `Dict` nor `Config` pagination is cached); only "high-frequency read + low-frequency change" hot spots (say, a dropdown data source) are worth caching, following the read-through cache in `DictService.GetItemsByTypeAsync` (`ICacheProvider` + explicit `RemoveAsync` invalidation after writes) — see the [Backend Coding Standards](/standard/backend) for the finer rules.

## Controller: the permission code is the route

`backend/tests/TenonAdmin.TestHost/SampleDocController.cs` — every action carries `[RolePermission]`, the permission code is the normalized route itself, and no permission string is written anywhere in code:

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

The permission code for the `GET /api/v1/sample/doc` action is `GET:/api/v1/sample/doc`. To authorize, an admin attaches this route to a menu/button and then checks it for a role, and users in that role have the permission; the super admin (`sadm` claim) bypasses automatically. A controller may return `Result<T>` or `return dto` directly — the envelope is wrapped uniformly by `ResultEnvelopeFilter`.

Two optional attributes, added as needed: for a write that needs auditing, attach `[OperationLog("Create document")]` — sensitive fields in the input (a password, say) are automatically masked before being written to the operation log (blueprint: `UserController`); to let a consumer switch the whole module off in one move, attach `[Module("SampleDoc")]` to the controller, after which setting `Api:DisabledModules=["SampleDoc"]` skips registering its routes entirely (blueprint: `SysLogController`).

## Hand the service and assembly to the kernel

Without touching the kernel, a consumer does two things in their own `Program.cs` — full template at `backend/tests/TenonAdmin.TestHost/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    // Hand your assembly to the kernel: its entities join CodeFirst table creation, its controllers are mounted via AddApplicationPart
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
});

// Register your own services with TryAdd (for interfaces the kernel hasn't claimed, before or after AddTenonAdmin is fine)
builder.Services.TryAddScoped<ISampleDocService, SampleDocService>();

var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

You must use `TryAdd`, not `Add`. That's what lets a consumer register a custom implementation of the same interface *before* `AddTenonAdmin()` to override the default behavior — the root rule behind the kernel's entire "replaceable" design, and how built-in services (like `Dict`) are registered in `ServicesSetup.cs` too. For your own service, if nobody's competing for the interface, `TryAdd` and `Add` behave the same; write `TryAdd` uniformly and you won't get bitten when something does come along to override it later.

::: warning Forget `ApplicationAssemblies.Add(...)` and it's a silent 404
The kernel **does not auto-discover** your module. Miss this one line and `SampleDoc` gets no table, `SampleDocController` registers no routes — a flat 404, with no fallback switch or hint. (There was once a `ScanApplicationAssemblies` auto-scan switch that was never implemented; it was removed before the packages shipped — don't go looking for it.)
:::

## Error codes (optional)

To distinguish "not found" from other failures precisely, the kernel's built-in modules add a numeric code to the `Core/ErrorCode.cs` enum — **codes only, no message text** — as with the dictionary module's `DictTypeNotFound = 43001` and `DictTypeCodeExists = 43002`; the text is translated by code on the frontend. A consumer is constrained by `ErrorCode` being a kernel enum that can't be extended, so they can express results directly through return values the way `SampleDocService` does (`false` meaning not found / no access), or fall back on a custom exception handled by their own exception filter.

## Seed data (optional)

When you need out-of-the-box default data, implement the **generic** `ISeedData<T>` (the non-generic `ISeedData` is just an empty marker for DI collection — implementing it directly compiles, but crashes at startup when the entity type can't be inferred), and give each row a fixed `Id` for idempotency. Blueprint: `Seed/DictSeed.cs`; consumer example: `backend/tests/TenonAdmin.TestHost/SampleWidgetSeed.cs`:

```csharp
public sealed class SampleWidgetSeed : ISeedData<SampleWidget>
{
    public IEnumerable<SampleWidget> HasData() =>
    [
        new() { Id = TenonSeedIds.ConsumerMin,     Name = "widget-a" },
        new() { Id = TenonSeedIds.ConsumerMin + 1, Name = "widget-b" },
    ];
}
```

Seeds must be registered in **your own `Program.cs`** — the kernel doesn't scan assemblies for seeds (`ApplicationAssemblies` only handles entity table creation and controller mounting), and forgetting to register one means it silently never runs:

```csharp
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SampleWidgetSeed>());
```

The fixed `Id` must fall within the consumer-reserved range `Id >= 1000` (the constant `TenonSeedIds.ConsumerMin`); the ceiling isn't a hardcoded number — it's the live snowflake floor computed at startup. Don't fall back on the old habit of "just grab a small integer" — you and the kernel seed into the same set of tables (`sys_menu` / `sys_config` …), and not colliding today doesn't mean you won't after a kernel-package upgrade — by which point that row is already in your database, with no way back:

| Range | Belongs to | Why |
|---|---|---|
| `[1, 999]` | Kernel built-in seeds | Every authenticated endpoint the kernel adds means one more menu row, so the range only ever climbs |
| `[1000, live floor)` | **Your seeds** | Start from `ConsumerMin`; the ceiling is the snowflake floor computed at startup (`SnowflakeIdGenerator.CurrentFloor()`), already a 15-digit number today |
| `[live floor, …)` | Snowflake runtime ID range | A seed occupying it means some future insert by this instance is bound to collide on the primary key |

Within one seed set you may lay down several rows at once (especially when copy-pasting); number each row the way the kernel's menu seeds do: remember the highest number in use, and **always take "highest + 1" for a new row, never backfilling a gap**. Gaps are usually numbers that were once moved or deleted, and reusing one collides with a leftover row in an older database.

::: warning A seed Id that collides or goes out of range: startup now refuses, no longer silent
A colliding fixed Id used to break **silently**: the idempotent existence check skips the later row as "already there" (a piece quietly missing from the menu tree), and a seed with `SyncOnUpgrade` on would even overwrite the other row on upgrade. Now `DatabaseInitializer` registers, per entity at startup, every fixed Id claimed by any seed (kernel + consumer), and the moment it finds one out of range or duplicated within an entity it **throws on the spot and the app won't start**, telling you which range to move to; `SeedIdRangeTests` carries the matching contract test, so CI goes red before any host even boots. This covers both the self-collision of "copied a row and forgot to change the Id" and cross-seed collisions.
:::

## Attach a menu, grant access

The permission code equals the route, and authorization is granted by checking routes on the menu tree, so for a new endpoint to be callable by a regular user there has to be a matching menu node first. Configure it at runtime in the admin UI:

1. Go to **Menu Management** and create a menu node: `Type=Menu`, `Path` set to the frontend route (e.g. `/sample/doc`), `Component` set to the corresponding `.vue` file's relative path (e.g. `sample/doc/index`), and `App` set to a top-level directory.
2. For button-level permissions, create a `Type=Button` node with `Permission` set to the corresponding route code (e.g. `POST:/api/v1/sample/doc`) — the frontend's `v-auth` shows or hides the button by it.
3. Go to **Role Management** and check the menu/button for a role; users in that role gain the corresponding route permission, and the authorization change takes effect immediately (the kernel invalidates the relevant cache).
4. The super admin needs no permission setup during development — all routes are let through automatically.

To ship menus preset out of the factory (instead of clicking through them in every environment), seed `SysMenu` rows in bulk the way `DefaultMenuSeed` does (both menu nodes and button nodes are `SysMenu`), numbering them per the previous section's convention. When you **change** an existing seed row for a built-in module (say, add a field under the same Id), remember to bump `SysSchemaVersion.Current` — an old database only backfills via `SyncOnUpgrade` once the version number changes (see the comment on `SqlSugar/Entities/SysSchemaVersion.cs`); a pure new-row addition needs no bump.

In **Module Management**, filling a "route prefix `apiPrefix`" for a business app (= the controller's route segment, e.g. `sample`, matching `/api/v1/sample/...`) makes the "configure permissions" route dropdown on the menu page list only that app's routes by default — decluttering, not a permission boundary; leave it blank and there's no filtering. Note you fill in the route segment, not the module code, and the two need not match.

## Testing

Write HTTP-level regression tests with `WebApplicationFactory` (blueprint: `backend/tests/TenonAdmin.Tests/ModulePortalTests.cs`): create a user and grant a menu → call the endpoint with a token → assert the envelope. Both the SQLite and MySQL legs must be green (`TestDb.cs` derives an isolated database per environment variable):

```bash
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~SampleDoc"
```

Once this backend set works and the new endpoints show up in `/openapi/v1.json`, building an admin page for this table is the next chapter: [Add a Frontend Page](/guide/frontend-page).

## Pre-commit checklist

- [ ] Entity base class chosen correctly (need org isolation → `DataEntity`); unique index added for unique columns; audit fields not set by hand
- [ ] Service methods `virtual`; updates/deletes check visibility with `GetByIdAsync` first; duplicate check on unique columns carries `ClearFilter<ISoftDelete>`
- [ ] Every controller action carries `[RolePermission]`; writes needing audit carry `[OperationLog(...)]`
- [ ] `ApplicationAssemblies.Add(...)` and service `TryAdd` both in place in `Program.cs` (missing assembly = silent 404)
- [ ] Seeds implement the generic `ISeedData<T>`, are registered in your own `Program.cs`, and use fixed Ids `>= 1000` with no collisions
- [ ] Changed an existing built-in seed row → bumped `SysSchemaVersion.Current`
- [ ] Tests green on both SQLite and MySQL
- [ ] Runtime: menu node created in Menu Management, grant checked in Role Management
