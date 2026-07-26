# 给自己的实体接导入导出 (Wire Import/Export)

给已有实体接 xlsx 导入 / 导出。档案声明列与业务动作，编排走内核 `IImportRunner`，编解码走可选卫星包 `TenonAdmin.Excel`。

**先决条件**：实体与领域服务已经能 CRUD。没有实体先走 `create-entity.md`；没有服务先走 `create-crud-backend.md`。

**活样板（照抄，别自己编）**：

| 样板 | 路径 | 演示什么 |
|---|---|---|
| `SampleDoc`（`DataEntity`） | `backend/tests/TenonAdmin.TestHost/SampleDoc.cs` | 机构隔离业务表 |
| `SampleDocExportProfile` | `…/SampleDocExportProfile.cs` | 最小 `IExportProfile` |
| `SampleDocController.Export` | `…/SampleDocController.cs` | 导出端点：取数与列表**同源** |
| `UserImportProfile` | `backend/src/TenonAdmin.Services/ImportExport/UserImportProfile.cs` | 完整 `IImportProfile`（字典 + 按名查外键 + 越权） |
| `UserController` 导入导出段 | `backend/src/TenonAdmin.AspNetCore/Controllers/UserController.cs` | 六个端点形状 |
| `MinimalHost` | `backend/samples/MinimalHost/Program.cs` | `AddTenonAdminExcel()` 装配顺序 |

---

## 第 0 步：装卫星包（不装就全抛 46001）

`TenonAdmin` 元包**不**引用 Excel。默认 codec 是 `MissingExcelProvider`，任意读写 / 模板调用一律抛 `ErrorCode.ExcelProviderMissing`（46001）。这是可选性的定义，不是 bug。

```bash
dotnet add package TenonAdmin.Excel
```

开发内核仓内可用 `ProjectReference` 代替（照 `MinimalHost.csproj`）。

**必须在 `AddTenonAdmin()` 之前**调用 `AddTenonAdminExcel()`（`TryAdd` 先到者胜）：

```csharp
using TenonAdmin.Excel;

// ✅ 先装 codec，再装内核
builder.Services.AddTenonAdminExcel();
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
});
```

```csharp
// ❌ 顺序反了：内核已把 MissingExcelProvider 占坑，你的 TryAdd 被跳过 → 仍 46001
builder.Services.AddTenonAdmin(builder.Configuration);
builder.Services.AddTenonAdminExcel();
```

活样板：`backend/samples/MinimalHost/Program.cs`。

配置节 `TenonAdmin:Excel`（可选，有默认值）：

| 键 | 默认 | 含义 |
|---|---|---|
| `MaxImportRows` | 5000 | 单次导入最大数据行 |
| `MaxExportRows` | 50000 | 单次导出最大行；超限 `ExportRowLimitExceeded` |
| `MaxImportFileSizeMb` | 10 | 导入文件大小上限（**不**复用 `Upload.MaxSizeMb`） |

---

## 第 1 步：实现 `IExportProfile`（最小）

档案只声明「能导哪些列」；**取数仍走你自己的列表 / Export 方法**，与列表同源，才能白捡 `DataEntity` 的机构数据范围过滤。

照抄 `SampleDocExportProfile`：

```csharp
// 文件: 消费者项目/ImportExport/SampleDocExportProfile.cs
using TenonAdmin.Core;

namespace MyApp;

/// <summary>
/// 示例导出档案——消费方给自己的 DataEntity 接导出的抄写样板。
/// 档案只声明列；取数走 ISampleDocService.ListAsync，与列表同源。
/// </summary>
public class SampleDocExportProfile : IExportProfile
{
    public virtual string Code => "sample-doc";

    public virtual IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new() { Key = "Title", Title = "标题", Width = 24 },
        // 字典列加 DictTypeCode，端点组装时 ToLabelAsync 做 value→label
        // new() { Key = "Status", Title = "状态", DictTypeCode = "order_status", Width = 12 },
    ];
}
```

有字典列时 `DictTypeCode` 非空；前端「选列」默认勾选由 `DefaultSelected` 控制（默认 `true`）。

DI（业务模块用 `AddScoped` 即可）：

```csharp
builder.Services.AddScoped<SampleDocExportProfile>();
// 若希望按接口注入：builder.Services.AddScoped<IExportProfile, SampleDocExportProfile>();
// 注意：多档案时不要只注册 IExportProfile——控制器应注入具体类型
```

---

## 第 2 步：实现 `IImportProfile`（最小）

以「标题唯一」的文档表为例（字段对齐 `SampleDoc`）。完整版（字典 / 外键 / 越权）抄 `UserImportProfile`。

```csharp
// 文件: 消费者项目/ImportExport/SampleDocImportProfile.cs
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace MyApp;

public class SampleDocImportProfile(IRepository<SampleDoc> repo, ISampleDocService docs) : IImportProfile
{
    public virtual string Code => "sample-doc";

    public virtual IReadOnlyList<string> BusinessKeys { get; } = ["Title"];

    public virtual IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new()
        {
            Key = "Title", Title = "标题", Required = true, Width = 24,
            Hint = "唯一标题",
        },
        // 字典列示例：模板出下拉；Runner 校验时 label→value 就地写回 Cells
        // new() { Key = "Status", Title = "状态", DictTypeCode = "order_status", Width = 12 },
    ];

    /// <summary>跨列规则、按名查外键、越权。Runner 已做过必填 / 字典 / 文件内重复。</summary>
    public virtual Task<IReadOnlyList<CellError>> ValidateRowAsync(
        ImportRow row, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CellError>>([]);

    /// <summary>本批业务键一次查完，禁止逐行查库。</summary>
    public virtual async Task<IReadOnlySet<string>> FindExistingKeysAsync(
        IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0) return new HashSet<string>();
        var list = keys.ToList();
        var existing = await repo.AsQueryable()
            .ClearFilter<ISoftDelete>() // 软删行也占唯一键时必须清软删过滤
            .Where(d => list.Contains(d.Title))
            .Select(d => d.Title)
            .ToListAsync();
        return existing.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>必须复用领域服务，禁止直插实体绕过不变量。</summary>
    public virtual async Task CommitRowAsync(
        ImportRow row, bool overwrite, CancellationToken cancellationToken = default)
    {
        var title = row.Cells.GetValueOrDefault("Title")?.Trim()
            ?? throw new AdminException(ErrorCode.ImportCellRequired);

        if (overwrite)
        {
            var entity = await repo.AsQueryable()
                .ClearFilter<ISoftDelete>()
                .Where(d => d.Title == title)
                .FirstAsync();
            // 走你自己的 Update；这里示意 Rename
            await docs.RenameAsync(entity.Id, title);
        }
        else
        {
            await docs.CreateAsync(title);
        }
    }
}
```

```csharp
builder.Services.AddScoped<SampleDocImportProfile>();
```

`ImportColumn` 要点：

| 字段 | 作用 |
|---|---|
| `Key` | 属性名，前端按它索引单元格 |
| `Title` | 模板表头；列映射的匹配对象 |
| `Required` | 模板表头加 `*`；空值 → `ImportCellRequired` |
| `DictTypeCode` | 非空 = 字典列：模板下拉、导入 label→value、导出 value→label |
| `Hint` | 「填写说明」sheet 备注 |
| `BusinessKeys` | 判重列；空集合 = 不判重 |

---

## 第 3 步：六个端点怎么加

权限模型要求**每个资源显式路由**，不要做 `/import/{code}` 通用控制器（授了导入 A 就会自动能导入 B）。

路由挂在该资源控制器上（用户资源见 `UserController`；导出最小样板见 `SampleDocController.Export`）：

| 方法 | 路由后缀 | 鉴权 | 返回 |
|---|---|---|---|
| GET | `import/template` | **`[ActiveSession]`** | xlsx 流（不进信封） |
| POST | `import/preview` | `[RolePermission]` | `ImportPreview` 信封 |
| POST | `import/validate` | `[RolePermission]` | `ImportPreview` 信封 |
| POST | `import/error-report` | `[RolePermission]` | xlsx 流 |
| POST | `import/commit` | `[RolePermission]` + `[OperationLog]` | `ImportCommitResult` 信封 |
| GET | `export` | `[RolePermission]` + `[OperationLog]` | xlsx 流 |

鉴权别弄反：

- **`import/template` 用 `[ActiveSession]`**：只泄露列名和字典 label（表单里本就看得见），少一个权限节点。
- **`import/preview` 必须 `[RolePermission]`**：会调 `FindExistingKeysAsync` 查库判重，等于业务键是否存在的探测器。

### 导出（最小，照 `SampleDocController.Export`）

```csharp
[HttpGet("export")]
[RolePermission]
[OperationLog("导出示例文档")]
public async Task<IActionResult> Export(CancellationToken cancellationToken)
{
    // 与列表同源 → DataEntity 自动吃 IOrgScoped 过滤；业务代码不写 WHERE create_org_id
    var docs = await svc.ListAsync();
    var rows = docs
        .Select(d => (IReadOnlyDictionary<string, object?>)
            new Dictionary<string, object?> { ["Title"] = d.Title })
        .ToList();

    var stream = await writer.WriteAsync(new ExportSheet
    {
        SheetName = "示例文档",
        Columns = exportProfile.Columns,
        Rows = rows,
    }, cancellationToken);

    return File(stream,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "示例文档导出.xlsx");
}
```

有筛选 + 选列时照 `UserController.Export`：query 复用 `XxxPageInput` + `columns` 逗号分隔列 Key；字典列在组装行时 `IDictTextResolver.ToLabelAsync`。

### 导入五端点骨架（照 `UserController`）

控制器注入：`IImportRunner`、`IExcelTemplateBuilder`、`IExcelWriter`、`IDictTextResolver`、你的 `XxxImportProfile`。

```csharp
// template：字典列备好 DictOptions（label 列表）→ templates.BuildAsync(TemplateSpec)
// preview ：multipart file + 可选 mapping(JSON) → importRunner.PreviewAsync
// validate：ImportRowsInput → importRunner.ValidateAsync
// error-report：原列 + 「错误原因」列 → writer.WriteAsync → File(...)
// commit  ：ImportCommitInput → importRunner.CommitAsync（服务端会重跑校验，不信任前端 Errors）
```

xlsx 三个端点返回 `File(...)` / `FileStreamResult`，**不进** `Result<T>` 信封。中文文件名走 ASP.NET 的 `FileDownloadName`（含 RFC 5987）。

---

## 第 4 步：菜单种子怎么取号

`[RolePermission]` 端点必须有对应菜单按钮种子，否则 `PermissionCodeConsistencyTests` 反向锁变红（系统模块）。业务模块通常走后台「菜单管理」UI；要预置则自注册 `ISeedData<SysMenu>`，Id ∈ 消费方区间（`≥ TenonSeedIds.ConsumerMin`）。

### 系统模块（内核 `DefaultMenuSeed`）

规则与 `create-crud-backend.md` 相同，补进导入导出按钮：

1. 打开 `DefaultMenuSeed.cs` 头部 Id 登记 / 现有最大号。
2. **新号 = 当前最大 + 1 起编**；**不要回填空洞**（空洞可能是挪走的历史号，复用会撞老库）。
3. 内核种子 Id ∈ `[1, 999]`（`SeedIdRangeTests`）。
4. `ParentId` = 该资源管理页的菜单 Id；`Sort` 接该页已有按钮末尾。
5. `Permission` = `METHOD:/路由模板`，与控制器路由一字不差。

用户资源实装（excel-ledger §6.2，现最大号已用到 131）：

```csharp
// template 走 [ActiveSession]，无需权限节点
new SysMenu { Id = 126, ParentId = 15, Type = MenuType.Button, Title = "用户-导入预览",
    Permission = "POST:/api/v1/sys/user/import/preview", Sort = 17, Enabled = true },
new SysMenu { Id = 127, ParentId = 15, Type = MenuType.Button, Title = "用户-导入重验",
    Permission = "POST:/api/v1/sys/user/import/validate", Sort = 18, Enabled = true },
new SysMenu { Id = 128, ParentId = 15, Type = MenuType.Button, Title = "用户-导入错误报告",
    Permission = "POST:/api/v1/sys/user/import/error-report", Sort = 19, Enabled = true },
new SysMenu { Id = 129, ParentId = 15, Type = MenuType.Button, Title = "用户-导入提交",
    Permission = "POST:/api/v1/sys/user/import/commit", Sort = 20, Enabled = true },
new SysMenu { Id = 130, ParentId = 15, Type = MenuType.Button, Title = "用户-导出",
    Permission = "GET:/api/v1/sys/user/export", Sort = 21, Enabled = true },
```

消费方业务菜单种子 Id 从 `TenonSeedIds.ConsumerMin`（1000）起，**不要**占用 `[1, 999]`。

前端按钮门控：`web/` 用 `v-auth="'POST:/api/v1/...'"`，`web-react/` 用 `<Can code="POST:/api/v1/...">`，与种子 `Permission` 四处一致。

---

## 两个必写测试的坑（实走踩出来的）

### 坑 11 — 字典列 label→value 必须幂等

`ImportRunner` 校验成功后会把字典列的 **label 就地换成 value** 写回 `row.Cells`。预览返回给前端的已经是 value。前端改错后原样送回 Validate / Commit，会再跑同一段解析。

若你的档案或自定义 Runner 只做 `ToValueAsync(label)`、不接受「本身已是合法 value」，则：**凡带字典列的预览一旦通过，重验 / 提交必被判 `ImportCellDictInvalid`（46006）**，整条向导不可用。

内核 `ImportRunner` 已修（`ToValueAsync` 失败时再查是否已是合法 value）。你接自己的实体时，**测试必须把 Preview 的输出喂回 Validate / Commit**，不要每次手造带 label 的行：

```csharp
// 形状照 ImportExportTests.PreviewRows_FedBackTo_ValidateAndCommit_AreIdempotentOnDictColumns
var preview = await runner.PreviewAsync(file, mapping: null, profile);
// 断言预览无硬错后：
var again = await runner.ValidateAsync(preview.Rows, profile);
Assert.Equal(0, again.ErrorRows);
var commit = await runner.CommitAsync(preview.Rows, profile, DuplicateStrategy.Skip);
// 不应因字典列二次解析失败
```

### 坑 1 — 导出不能走 `PageAsync`（`MAX_SIZE=200` 静默截断）

`ToPagedListAsync` 内有 `size = Math.Min(size, MAX_SIZE)`，`MAX_SIZE = 200`。传 `Size = 50000` **不报错**，只静默返回 200 行——导出「成功」，数据少了 99%。

**做法**（照 `UserService`）：

1. 把列表查询从 `.AsQueryable()` 到投影抽成 `protected virtual ISugarQueryable<TItem> BuildListQuery(...)`。
2. `PageAsync` 与 `ExportAsync` **共用**它。
3. `ExportAsync` 用 `.Take(MaxExportRows + 1).ToListAsync()`（多取一条判断是否超限），**不要**调 `PageAsync`。

`SampleDoc` 列表本身无分页截断，可直接 `ListAsync()`；一旦你的列表走了 `ToPagedListAsync`，导出必须另开不截断路径，且过滤条件不得复制两份（漂移 = 导出范围与列表不一致，很难发现）。

---

## 检查清单

- [ ] 已 `dotnet add package TenonAdmin.Excel`（或 ProjectReference）
- [ ] `AddTenonAdminExcel()` 写在 `AddTenonAdmin()` **之前**
- [ ] `IExportProfile` 已注册；导出取数与列表同源
- [ ] `IImportProfile`：`CommitRowAsync` 走领域服务，不直插
- [ ] 六个端点鉴权正确（template=`ActiveSession`，其余 `RolePermission`；commit/export 有 `OperationLog`）
- [ ] 菜单按钮种子 Permission 与路由一致；系统模块 Id 不回填空洞
- [ ] 有字典列 → 幂等测试（Preview 输出 → Validate/Commit）
- [ ] 列表若分页 → Export 不走 `PageAsync` / `ToPagedListAsync`
- [ ] 后端跑着时前端 `npm run gen:api`；按钮 `v-auth` / `<Can>` 用真实权限码

```bash
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test  backend/TenonAdmin.slnx --filter "FullyQualifiedName~ImportExport"
```

前端接线（向导组件、blob 下载、multipart）见 `web/src/components/ImportWizard/` 与 `web-react/src/components/ImportWizard.tsx`，以及各自 `COMPONENTS.md`。两模板零共享，按你选的模板抄一遍即可。

---

## 相关

- 执行台账与库选型实测：`docs/excel-ledger.md`（§2 库选型、§8 坑全集）
- 替换 codec / Runner：`replace-service.md`（同一套 `TryAdd` 前置）
- 站点文档：`site/zh/guide/import-export.md`
