# Multi-Org Data Scope

Data scope carries the whole org-filtering burden on behalf of business code, and that is where the kernel makes its name. It answers one question: given the same endpoint and the same SQL, why do different users see different rows? The answer sits in a global query filter, which trims results automatically by the **effective org set** resolved for the current request.

## Five data-scope types

Data-scope types are defined in `Core/Security/DataScopeType.cs` — the org dimension of what data a role can see, stored as an `int`:

| Value | Type | Meaning |
| --- | --- | --- |
| 1 | `All` | All data, not constrained by org |
| 2 | `Org` | Current org only — the user's primary org |
| 3 | `OrgAndChildren` | Current org and below — primary org plus all descendant orgs |
| 4 | `Self` | Self only — data created by the user themself |
| 5 | `Custom` | Custom orgs — an explicitly specified set of orgs |

Scope is attached to **roles** (`sys_role_data_scope`); a user can hold multiple roles. Merging follows a **widest-wins** rule:

- If any role is `All` → unrestricted overall, sees everything;
- Otherwise, union the org sets across roles (current org = primary org; current org and below = primary org + descendants; custom = the specified set);
- If any role is `Self` → an additional "self only" dimension is unioned in (can also see data they created themselves).

Say a user holds two roles: one `Org` role whose primary org is "East China Branch," and one `Custom` role scoped to "South China Branch" and "North China Branch." Neither role is `All`, so the merge is the union of all three orgs — queries filter to just those three. Neither role is `Self`, so this user doesn't see the extra "created by me" layer on top.

## Resolution: merging multiple roles into one result

`IDataScopeProvider.ResolveAsync(userId)` merges a user's multi-role scopes into a single immutable `DataScopeResult`. The default implementation, `DataScopeProvider`, caches results per user (invalidated on permission/org changes), only aggregating from the DB on a cache miss:

```csharp
protected virtual async Task<DataScopeResult> ComputeAsync(long userId)
{
    // fetch the user's enabled roles
    var roleIds = ...;
    if (roleIds.Count == 0) return DataScopeResult.Restricted([], includeSelf: true, userId); // no roles → self only

    var scopes = ...;   // each role's data-scope configuration
    if (scopes.Any(s => s.ScopeType == DataScopeType.All)) return DataScopeResult.Unrestricted; // widest wins
    if (scopes.Count == 0) return DataScopeResult.Restricted([], includeSelf: true, userId);    // has roles but none configured → self only

    // accumulate org set per role: Org adds the primary org; OrgAndChildren expands descendants; Self sets includeSelf; Custom unions the specified set
    return DataScopeResult.Restricted(orgSet, includeSelf, userId);
}
```

::: tip Secure by default
No roles, or roles with no scope configured, both collapse to "self only" — never widened. When both are empty (org set empty + self not included), that means "sees no data at all" — deny by default, not allow by default.
:::

`DataScopeResult` (`Core/Security/DataScopeResult.cs`) is an immutable value object, safe to cache across requests:

```csharp
public sealed record DataScopeResult
{
    public bool IsUnrestricted { get; init; }                       // unrestricted: sees everything
    public IReadOnlyCollection<long> OrgIds { get; init; } = [];    // visible org Id set (matched against CreateOrgId)
    public bool IncludeSelf { get; init; }                          // whether "self only" is appended
    public long UserId { get; init; }                              // matched against CreateUserId when IncludeSelf is true

    public static readonly DataScopeResult Unrestricted = new() { IsUnrestricted = true };
    public static DataScopeResult Restricted(IReadOnlyCollection<long> orgIds, bool includeSelf, long userId) => ...;
}
```

It's a `record` with `init` properties whose parameter names match property names so it round-trips cleanly through `System.Text.Json` — needed because when switching to Redis for multi-instance deployment, this result must serialize into the cache and back out.

## Anchor field: `CreateOrgId`

The anchor for data-scope filtering is the `CreateOrgId` field on the entity — **the org the creator belonged to at creation time**. Business tables that inherit `DataEntity` (`SqlSugar/Entities/DataEntity.cs`) get this field automatically:

```csharp
public abstract class DataEntity : BaseEntity, IOrgScoped
{
    [SugarColumn(IsNullable = true, ColumnDescription = "归属机构 Id(数据范围锚点)")]
    public long? CreateOrgId { get; set; }
}
```

Tables that don't need org isolation (global dictionaries, the org tree itself) continue to use `BaseEntity` — no such field, not subject to data-scope filtering.

`CreateOrgId` **is never set by business code**; it's auto-filled on insert by the audit AOP hook from the current user's `org` claim (`SqlSugarSetup.cs`):

```csharp
// CreateOrgId not specified → fill with the current user's owning org (data-scope anchor)
else if (info is { PropertyName: nameof(DataEntity.CreateOrgId),
                   EntityValue: DataEntity { CreateOrgId: null } }
         && currentUser.OrgId is { } insOrgId)
    info.SetValue(insOrgId);
```

::: warning An unfilled anchor means you can't see the row you just inserted
If `CreateOrgId` isn't filled (e.g. the kernel's token flow was bypassed, or the user has no `org` claim), that row's org-dimension visibility ends up empty — org-scoped users can't query it. This is exactly why the audit AOP's auto-fill is a prerequisite for data scope to work at all.
:::

## Global filter: zero filter conditions in business code

The filter is registered once in `SqlSugarSetup.cs` against entities implementing `IOrgScoped` (`DataEntity` and its subclasses), and takes effect on every query:

```csharp
client.QueryFilter.AddTableFilter<IOrgScoped>(e =>
    scope.Current.IsUnrestricted == true
    || (e.CreateOrgId != null && scope.Current.OrgIds.Contains(e.CreateOrgId.Value))
    || (scope.Current.IncludeSelf == true && e.CreateUserId == scope.Current.UserId));
```

The three branches correspond to the three visibility cases: if unrestricted, the whole predicate is always true (no filtering); otherwise `CreateOrgId ∈ org set`, or (when "self only" is enabled) `CreateUserId == current user`.

::: details Two implementation details
**Matched by interface, not by base class**: SqlSugar's `AddTableFilter<T>` matches an interface or an exact type, not a base class — which is why the anchor field is exposed through the `IOrgScoped` interface. The soft-delete filter `AddTableFilter<ISoftDelete>` works the same way.

**Boolean flags written as `== true`**: the three `scope.Current` properties in the expression are independent of the entity parameter, so SqlSugar evaluates them locally into constants first (the org set becomes a SQL `IN`), then splices them into the `WHERE` clause. The two booleans are written as `== true` rather than as bare booleans because SqlServer's predicate context doesn't accept a bare scalar — it must be a comparison expression.
:::

With this in place, business code that writes the plainest possible query is already constrained by org isolation:

```csharp
// In a business service: no org-filter condition written anywhere
var orders = await orderRepo.AsQueryable()
    .Where(o => o.Status == OrderStatus.Pending)
    .ToListAsync();
// The actual SQL executed already has this appended: AND (CreateOrgId IN (...) OR CreateUserId = ...)
```

With the exact same code, a user with `All` scope sees every pending order, a user with `Org` scope sees only their own org's, and a `Self`-scoped user sees only what they created — the difference comes entirely from the `DataScopeResult` resolved earlier in the request; not a single line of business logic changes.

You can click through what this looks like in a real project. The customer list in the reference app [tenon-example](https://github.com/Tenon-Net/tenon-example) is written exactly like the snippet above, and three accounts logging into the [live demo](https://tenonadmin.52moyu.net/login) see 214, 128, and 42 rows respectively.

::: warning Write-path guard
The global filter only applies to **queries (SELECT)** — not to primary-key-based `Updateable` / `Deleteable`. To cover this, the `SqlSugarRepository` repository has a built-in write-path guard for `UpdateAsync` / `DeleteAsync` on `IOrgScoped` entities: before writing, it queries through the scope-filtered path to confirm the target row is within the current scope; attempting to modify/delete a row from another org is rejected (returns 0 rows). Writes that bypass the repository via the `Db.Updateable/Deleteable` escape hatch aren't covered by this guard and must validate ownership themselves.
:::

## Context carrier: why not `AsyncLocal`

The effective scope is passed through the request via `IDataScopeContext`. `Current` is never null — **not explicitly set means unrestricted** (a system/trusted context, e.g. startup, seeding, self-checks), which is why an authenticated request must explicitly resolve and write it before any query, a step guaranteed by the authorization pipeline.

The kernel provides two implementations, depending on whether an HTTP context is available:

- **`HttpContextDataScopeContext`** (AspNetCore layer, HTTP path): stores the current request's scope in `HttpContext.Items`.
- **`DataScopeContext`** (SqlSugar layer, non-HTTP fallback): `AsyncLocal`-based, used by background jobs / self-checks that have no HTTP context.

The HTTP path **deliberately avoids `AsyncLocal`**, for the reason documented in `HttpContextDataScopeContext.cs`:

> The authorization filter is a callee within the MVC pipeline; an `AsyncLocal` set after an `await` inside it won't flow back up to the pipeline's caller (a classic pitfall), so queries made in the action wouldn't see it. `HttpContext.Items` is attached to the request object itself and is reliably visible across the whole pipeline, avoiding this problem.

In other words, scope is written in the **authorization filter**, while the query happens in the **action further up the call chain**. An `AsyncLocal`'s value only flows downstream along the execution flow — a value set in a downstream filter is invisible to an upstream action; whereas `HttpContext.Items` is attached to the request object, so the entire pipeline can reliably read the same value. This is exactly why `Items` has to be chosen over `AsyncLocal`.

## Extension point

`IDataScopeProvider` is the replacement point. The default implementation aggregates `sys_role_data_scope`; a typical replacement swaps in a different isolation dimension (e.g. per-tenant). Both `ResolveAsync` and the internal `ComputeAsync` are `virtual`, so a consumer can override just one step. Per the kernel's replaceability convention, a consumer registers its own `IDataScopeProvider` before `AddTenonAdmin()` to take over, without forking anything.
