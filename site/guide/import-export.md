# Wire Import/Export on Your Entity

Install `TenonAdmin.Excel`, write a profile, and hang six endpoints on the resource controller — that is enough for xlsx import/export on a business table. Without the package, every call that reads or writes xlsx returns `46001`, and publish size does not grow by a single byte.

If the entity and CRUD do not exist yet, start with [Add a Business Module](/guide/business-module). The step-by-step agent checklist lives in `skills/wire-import-export.md` in the repo.

## Install the satellite package, then the kernel

The `TenonAdmin` meta-package does **not** reference Excel. The default codecs are `MissingExcelProvider`: read, write, and template generation all throw `ErrorCode.ExcelProviderMissing` (`46001`). That is what “optional” means.

```bash
dotnet add package TenonAdmin.Excel
```

Registration order matches Redis and external login: call `AddTenonAdminExcel()` **before** `AddTenonAdmin()`. The kernel registers defaults with `TryAdd` (first registration wins):

```csharp
using TenonAdmin.Excel;

builder.Services.AddTenonAdminExcel(); // must come first
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
});
```

Reverse the order and nothing throws at startup, but codecs stay missing: template, preview, error-report and both exports fail with `46001` on the first call. `validate` and `commit` never touch a codec, yet with no preview to feed them rows they have nothing to do either. Live sample: `backend/samples/MinimalHost/Program.cs`.

Optional config under `TenonAdmin:Excel`: `MaxImportRows` (default 5000), `MaxExportRows` (default 50000), `MaxImportFileSizeMb` (default 10 — **not** shared with avatar upload limits).

## Export: profile declares columns; load the same rows as the list

The live sample is the `SampleDoc` (`DataEntity`) trio in the test host:

| File | Role |
|---|---|
| `backend/tests/TenonAdmin.TestHost/SampleDoc.cs` | Org-scoped business table |
| `SampleDocExportProfile.cs` | Minimal `IExportProfile` |
| `SampleDocController.Export` | Export endpoint |

The profile only declares which columns can be exported:

```csharp
public class SampleDocExportProfile : IExportProfile
{
    public virtual string Code => "sample-doc";
    public virtual IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new() { Key = "Title", Title = "Title", Width = 24 },
    ];
}
```

In the endpoint, load rows through the **same** path as the list. `SampleDoc` calls `ListAsync()`; the global `IOrgScoped` filter already trims rows to the current user's data scope. The business service never writes `WHERE create_org_id`:

```csharp
[HttpGet("export")]
[RolePermission]
[OperationLog("Export sample docs")]
public async Task<IActionResult> Export(CancellationToken cancellationToken)
{
    var docs = await svc.ListAsync(); // same source as the list
    var rows = docs
        .Select(d => (IReadOnlyDictionary<string, object?>)
            new Dictionary<string, object?> { ["Title"] = d.Title })
        .ToList();

    var stream = await writer.WriteAsync(new ExportSheet
    {
        SheetName = "Sample Docs",
        Columns = exportProfile.Columns,
        Rows = rows,
    }, cancellationToken);

    return File(stream,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "sample-docs.xlsx");
}
```

xlsx goes through `File(...)` and does **not** enter the `Result<T>` envelope. For filters, column picking, and dict value→label, copy `UserController.Export`.

::: danger Do not hang export off PageAsync
The paging helper caps size at `MAX_SIZE = 200`. A larger `Size` still returns 200 rows silently with HTTP 200. Export “succeeds” and quietly drops most of the data.
If the list uses `ToPagedListAsync`, extract the query into `BuildListQuery` shared by `PageAsync` and `ExportAsync`; export uses `Take(MaxExportRows + 1)` for the full set and overflow check. Kernel user export is exactly that shape (`UserService.ExportAsync`).
:::

## Import: implement IImportProfile

Orchestration lives in the kernel `IImportRunner` (parse → map → validate → dedupe → commit). Your profile only declares columns, business keys, row validation, bulk key lookup, and commit-one-row. The full sample is `UserImportProfile` (dict, name-based FK, org scope, commit via `IUserService`). Minimal shape:

```csharp
public class SampleDocImportProfile(IRepository<SampleDoc> repo, ISampleDocService docs) : IImportProfile
{
    public virtual string Code => "sample-doc";
    public virtual IReadOnlyList<string> BusinessKeys { get; } = ["Title"];
    public virtual IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new() { Key = "Title", Title = "Title", Required = true, Width = 24 },
    ];

    public virtual Task<IReadOnlyList<CellError>> ValidateRowAsync(
        ImportRow row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CellError>>([]);

    // Resolve the whole key batch in one query — never per row
    public virtual async Task<IReadOnlySet<string>> FindExistingKeysAsync(
        IReadOnlyCollection<string> keys, CancellationToken ct = default) { /* … */ }

    // Reuse the domain service; never insert the entity directly
    public virtual async Task CommitRowAsync(
        ImportRow row, bool overwrite, CancellationToken ct = default) { /* … */ }
}
```

A non-empty `DictTypeCode` marks a dictionary column: template dropdown, import label→value, export value→label. The runner already handles required fields, dict values, and in-file duplicates; `ValidateRowAsync` only adds cross-column and FK rules.

::: danger Dict column validation must be idempotent
After a successful check the runner **rewrites** labels in `row.Cells` to values before returning the preview. When the user fixes cells and resubmits the same rows to validate/commit, the cells already hold values. A parser that only accepts labels will fail every preview-green row with `46006` on the second pass, and the wizard is unusable.
When you wire your own entity, feed **Preview output** into Validate/Commit in tests — do not hand-build label rows every time. Regression shape: `PreviewRows_FedBackTo_ValidateAndCommit_AreIdempotentOnDictColumns`.
:::

## Six endpoints and menu seeds

Hang routes on each resource. Do not build a generic `/import/{code}` controller: permission codes are the normalized route, so a shared path would grant “import users” and “import orders” as one right.

| Method | Suffix | Auth | Notes |
|---|---|---|---|
| GET | `import/template` | `[ActiveSession]` | Download template; only leaks column titles and dict labels |
| POST | `import/preview` | `[RolePermission]` | Upload + preview; hits the DB for dedupe — must be per-resource |
| POST | `import/validate` | `[RolePermission]` | Re-validate after in-browser edits |
| POST | `import/error-report` | `[RolePermission]` | Error-report xlsx |
| POST | `import/commit` | `[RolePermission]` + `[OperationLog]` | Partial commit; server re-validates |
| GET | `export` | `[RolePermission]` + `[OperationLog]` | Full export under current filters |

`template` only needs an active session: the import entry is gated by the `preview` permission. Do not demote `preview` to `ActiveSession` — that turns it into an existence oracle for business keys.

Every `[RolePermission]` endpoint needs a menu button whose `Permission` is `METHOD:/route-template`, character-identical to the controller. System modules take the next free Id in `DefaultMenuSeed` (max + 1; **never backfill holes**); kernel range is `[1, 999]`. Consumer menu seeds start at `TenonSeedIds.ConsumerMin` (1000). The user resource's five buttons are Ids 126–130 — use them as the reference; the op-log export button is 131. Template download is `[ActiveSession]`-only and has no button.

`web/` and `web-react/` each ship an import wizard and export column picker (zero-shared; copy the template you use). After `gen:api`, gate buttons with `v-auth` or `<Can code>` and the real permission codes.

## Why MiniExcel + OpenXml

Magicodes.IE was considered and rejected after measurement: native assets in the dependency closure, compile-time attributes that cannot see runtime dict tables, and English error strings that fight “numeric ErrorCode only, copy on the frontend.” MiniExcel handles read/write; DocumentFormat.OpenXml is used **only** to build templates with dropdowns. Both are pure managed code — the stock ASP.NET image needs no change. Full dependency table: `docs/excel-ledger.md` §2.
