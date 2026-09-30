# 给自己的实体接导入导出

给业务列表增加 Excel 导入导出时，需要分别确定允许读写的列、行校验规则和访问权限。`TenonAdmin.Excel` 提供 xlsx 读写，Profile 描述业务列，导入执行器负责预览、校验和提交。列表与导出应使用相同的数据范围。

实体和 CRUD 还没有的话，先走[加一个业务模块](/zh/guide/business-module)。Agent 施工用的逐步清单在仓库 `skills/wire-import-export.md`。

## 先装卫星包，再调内核

在自己的后端项目安装与 `TenonAdmin` 相同版本的 Excel 扩展。未安装时，默认 `MissingExcelProvider` 会在读写或生成模板时返回 `ErrorCode.ExcelProviderMissing`（`46001`），可据此检查扩展是否注册成功。

```bash
dotnet add package TenonAdmin.Excel
```

注册顺序和 Redis / 外部登录一样：**先** `AddTenonAdminExcel()`，**再** `AddTenonAdmin()`。内核用 `TryAdd` 挂默认实现，先到者胜：

```csharp
using TenonAdmin.Excel;

builder.Services.AddTenonAdminExcel(); // 必须在前面
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
});
```

写反了不会报错，codec 却仍是缺省实现：模板、预览、错误报告和两个导出一调就是 `46001`。`validate` 和 `commit` 不碰 codec，可没有预览喂行，它们也无事可做。活样板在 `backend/samples/MinimalHost/Program.cs`。

可选配置节 `TenonAdmin:Excel`：`MaxImportRows`（默认 5000）、`MaxExportRows`（默认 50000）、`MaxImportFileSizeMb`（默认 10，**不**与上传头像共用上限）。

## 导出：档案声明列，取数与列表同源

活样板是测试宿主里的 `SampleDoc`（`DataEntity`）三件套：

| 文件 | 作用 |
|---|---|
| `backend/tests/TenonAdmin.TestHost/SampleDoc.cs` | 机构隔离业务表 |
| `SampleDocExportProfile.cs` | 最小 `IExportProfile` |
| `SampleDocController.Export` | 导出端点 |

档案只声明「能导哪些列」：

```csharp
public class SampleDocExportProfile : IExportProfile
{
    public virtual string Code => "sample-doc";
    public virtual IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new() { Key = "Title", Title = "标题", Width = 24 },
    ];
}
```

端点里取数必须走和列表**同一条**查询。`SampleDoc` 直接调 `ListAsync()`，全局 `IOrgScoped` 过滤器已经按当前用户数据范围裁好行。业务服务里不必写 `WHERE create_org_id`：

```csharp
[HttpGet("export")]
[RolePermission]
[OperationLog("导出示例文档")]
public async Task<IActionResult> Export(CancellationToken cancellationToken)
{
    var docs = await svc.ListAsync(); // 与列表同源
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

xlsx 走 `File(...)`，不进 `Result<T>` 信封。带筛选、选列、字典 value→label 时，形状抄 `UserController.Export`。

::: danger 不要把导出接在 PageAsync 上
分页扩展里有 `MAX_SIZE = 200`：传再大的 `Size` 也只静默返回 200 行，接口仍 200。看起来导出成功，数据却少了大半。
列表若用了 `ToPagedListAsync`，把查询抽成 `BuildListQuery`，`PageAsync` 与 `ExportAsync` 共用它；导出用 `Take(MaxExportRows + 1)` 拉全量并判超限。内核用户导出就是这个形状（`UserService.ExportAsync`）。
:::

## 导入：实现 IImportProfile

导入执行器 `IImportRunner` 负责解析、映射、校验、判重和提交。业务 Profile 声明列与业务键，并实现业务校验、批量查重和单行保存。下面是接口结构示意，省略的方法需要按业务实现；完整参考是 `UserImportProfile`，包括字典、外键、机构权限与通过业务服务保存的处理。

```csharp
public class SampleDocImportProfile(IRepository<SampleDoc> repo, ISampleDocService docs) : IImportProfile
{
    public virtual string Code => "sample-doc";
    public virtual IReadOnlyList<string> BusinessKeys { get; } = ["Title"];
    public virtual IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new() { Key = "Title", Title = "标题", Required = true, Width = 24 },
    ];

    public virtual Task<IReadOnlyList<CellError>> ValidateRowAsync(
        ImportRow row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CellError>>([]);

    // 本批业务键一次查完，禁止逐行查库
    public virtual async Task<IReadOnlySet<string>> FindExistingKeysAsync(
        IReadOnlyCollection<string> keys, CancellationToken ct = default) { /* … */ }

    // 复用领域服务，禁止直插实体
    public virtual async Task CommitRowAsync(
        ImportRow row, bool overwrite, CancellationToken ct = default) { /* … */ }
}
```

列上 `DictTypeCode` 非空即为字典列：模板出真下拉，导入 label→value，导出 value→label。Runner 已做必填、字典、文件内重复；`ValidateRowAsync` 只补跨列与外键。

::: danger 字典列校验必须幂等
校验成功后，Runner 会把单元格里的 label **就地改成 value** 再返回前端。用户改完错把同一批行送回「重新校验 / 提交」时，Cells 里已经是 value。只认 label、不认合法 value 的解析，会让预览通过的行在第二步全部被判 `46006`，向导整条不可用。
给自己实体写测试时，把 **Preview 的输出**喂回 Validate / Commit，不要每次手造带 label 的行。回归形状见 `PreviewRows_FedBackTo_ValidateAndCommit_AreIdempotentOnDictColumns`。
:::

## 六个端点与菜单

每个资源自己挂路由，不要做 `/import/{code}` 通用控制器：权限码就是规范化路由，通用路径会让「能导入用户」的人自动能导入订单。

| 方法 | 后缀 | 鉴权 | 说明 |
|---|---|---|---|
| GET | `import/template` | `[ActiveSession]` | 下模板；只泄露列名与字典 label |
| POST | `import/preview` | `[RolePermission]` | 上传预览；会查库判重，必须按资源授权 |
| POST | `import/validate` | `[RolePermission]` | 前端改错后重验 |
| POST | `import/error-report` | `[RolePermission]` | 错误报告 xlsx |
| POST | `import/commit` | `[RolePermission]` + `[OperationLog]` | 部分提交；服务端重跑校验 |
| GET | `export` | `[RolePermission]` + `[OperationLog]` | 当前筛选全量导出 |

`template` 用会话即可：导入按钮本身由 `preview` 的权限码把门。`preview` 不能降成 `ActiveSession`，否则变成业务键是否存在的探测器。

`[RolePermission]` 端点要有菜单按钮节点，Permission 写成 `METHOD:/路由模板`，与控制器一字不差。系统模块在 `DefaultMenuSeed` 里取号：当前最大 Id + 1 起编，**不要回填空洞**，内核段 `[1, 999]`。消费方预置菜单 Id 从 `TenonSeedIds.ConsumerMin`（1000）起。用户资源的五颗按钮是 Id 126–130，可直接对照；操作日志导出在 131。模板下载走 `[ActiveSession]`，没有按钮。

前端 `web/` 与 `web-react/` 各有一套导入向导与导出选列弹窗（零共享，按你选的模板抄）。`gen:api` 后按钮用 `v-auth` 或 `<Can code>` 挂真实权限码。

## 库为什么是 MiniExcel + OpenXml

MiniExcel 负责 xlsx 读写，DocumentFormat.OpenXml 用于生成带字典下拉的模板。两者均为托管依赖，现有 ASP.NET 运行时镜像无需额外安装原生组件。业务校验仍由 Profile 和导入执行器完成，不能依赖 Excel 下拉阻止非法输入。

## 验证导入与导出

用普通用户准备包含有效行、重复业务键和无效字典值的文件。预览应指出错误，提交时服务端仍应校验；将预览结果再次校验，不能把已转换的字典值误判为非法。导出后对照同一筛选条件与数据范围检查记录，确认没有只导出当前分页，也没有其他机构的越权数据。
