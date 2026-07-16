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

Once the backend is running, the full steps on the frontend side (regenerating types, wrapping the API, writing the CRUD page, attaching the menu) are covered in the next guide: [Add a Frontend Page](/tutorial/frontend-page).

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

> For fuller specification details (paged DTO conventions, caching decisions, when to bump `SysSchemaVersion`, etc.), see [Business Module Development Guide: A. Backend](/guide/new-business/backend).
