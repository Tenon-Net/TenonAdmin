# 导入导出执行台账 · `TenonAdmin.Excel`

> **来源**:2026-07-25 grilling 定向。库选型、契约落点、范围、功能取舍已钉死,执行期不回炉。
> **驱动方式**:仿 `docs/refinement-ledger.md` —— 逐条执行、每条独立英文 conventional commit、可断点续跑。

---

## 0. 给执行者的话(先读这段)

本文件是**可直接施工的规格**,不是方向性描述。§4/§5 的签名、§6 的接线、§9 的批次都已按仓内现状核对过(2026-07-25),**照做即可,不要重新调研**。

三条硬规矩:

1. **一次只做一条 G 项,一条一个独立提交**(英文 conventional commit)。做完在本文件勾选并把提交号写进 §12 轮次日志,再做下一条。
2. **验证 = 跑出来的证据。** 每条 G 项在 §9 里都写了「验收判据」和「变异判据」。变异判据的意思是:**故意把实现改坏,确认对应测试真的变红,再改回来**。跑绿的用例什么都不证明,直到它被变异证伪。
3. **遇到本文件没写到的设计取舍,停下来问维护者**,不要自行发挥。特别是:改动既有 `virtual` 方法的签名、给 Core 加新的 NuGet 依赖、往两个前端模板之间抽共享层 —— 这三件都是明令禁止的,见 §8。

**术语**:「内核」= `backend/src/TenonAdmin.{Core,SqlSugar,Services,AspNetCore}` 四包;「卫星包」= 可选的独立 NuGet 包(本次新增 `TenonAdmin.Excel`);「消费者」= 装了这些包来做自己后台的人。

---

## 1. 决策全表(grilling 钉死,不回炉)

| 维度 | 结论 | 依据 |
|---|---|---|
| xlsx 底座 | **MiniExcel + DocumentFormat.OpenXml**,各管一段 | §2 |
| 契约落点 | **Core 定契约 + 卫星包只放 codec 实现** | §3 |
| 内核页面 | **用户导入 + 用户导出 + 操作日志导出** | 一个例子演完字典翻译 + 按名查外键 + 数据范围 |
| 前端范围 | **`web/` 与 `web-react/` 都做** | 用户裁定;零共享是硬约束,**真写两遍** |
| 第一版功能 | 列映射 UI · 预览页内改错+重验 · 导出=当前筛选全量+选列 · 重复检测+更新策略 | 用户四条全选 |
| 默认包含 | 错误报告下载 · 部分提交 · 流式读+行数上限 · 导入记操作日志 · 演示模式写保护 | 不占名额 |
| 行的表示 | **`Dictionary<string, string?>` 字符串单元格**,不做泛型 `TRow` | §4.3 |
| 路由 | **每个资源显式路由**,不做 `/import/{code}` 通用控制器 | §5.4(安全,不是风格) |

---

## 2. 库选型(2026-07-25 实测,`rebuild-design.md:165` 的「Magicodes 已定稿」**推翻**)

net10 依赖闭包,当日从 nuget.org 拉的:

| 库 | 依赖数 | 原生库 | 结论 |
|---|---|---|---|
| **MiniExcel 1.45.0** | **0** | 无 | ✅ 取 —— 读写主力 |
| **DocumentFormat.OpenXml 3.5.1** | 1(自家 `.Framework`,**Microsoft 官方**) | 无 | ✅ 取 —— 只生成模板 |
| Magicodes.IE.Excel 2.9.0 | 5(`Magicodes.IE.EPPlus`/`Core`/`DynamicExpresso.Core`/`System.Linq.Dynamic.Core`/**`SkiaSharp.NativeAssets.Linux`**) | **有,38MB** | ❌ 弃 |
| NPOI 2.8.0 | 12(含 SkiaSharp 原生) | **有** | ❌ 弃 |
| ClosedXML 0.105.0 | 7(SixLabors.Fonts / RBush / ExcelNumberFormat …) | 无 | ❌ 弃(依赖多于 OpenXml 且能力重叠) |

**为什么弃 Magicodes**(别只记「依赖多」,这两条才是承重的):

1. **它的校验是编译期特性,够不到 DB 字典。** 榫卯的字典在 `sys_dict_item` 表里,运行期才知道有哪些值 —— `[ValueMapping]` 这类特性天然做不到。而字典翻译正是本次的核心能力,买的东西正好用不上。
2. **它的错误信息是英文字符串,要二次匹配翻译。** 活证据:`C:\Project\SimpleAdmin` 的 `ImportExportService.cs` 里写着 `if (it.Value.Contains("Invalid")) errrVaule = $"{it.Key}格式错误"`。这和榫卯「错误是数字 `ErrorCode`、文案在前端按码渲染」(设计 §13.2)正面冲突。

**分工(别混,这是两个库并存的唯一理由)**:

- **MiniExcel** —— 导出写数据行、导入流式读。手写 xlsx 读取要自己处理 sharedStrings / 内联字符串 / 空列跳号 / 日期序列号,几百行全是坑,不自己造。
- **DocumentFormat.OpenXml** —— **只**生成模板。MiniExcel **写不了 `dataValidation`**(单元格下拉),这是取 OpenXml 的唯一理由。模板永远只有几十个单元格,直接用 SDK 建最干净,不要「MiniExcel 写完再回去改」的两步。

**部署影响:零。** 两个都是纯托管,无 `.so`、不要 fontconfig/libgdiplus、**不要装 Office**(要装 Office 的是 `Microsoft.Office.Interop.Excel`,COM 互操作,微软自己写明不支持服务端,不碰)。现有 `mcr.microsoft.com/dotnet/aspnet:10.0` 镜像原样能跑,非 root 能跑,Alpine 能跑,`templates/content/tenon-app/Dockerfile` 一个字不用改。**卫星包是可选的** —— 不装的消费者 publish 里一个字节都不多。

`DocumentFormat.OpenXml` 是 Microsoft 官方包,正好落在「运行时只有 SqlSugarCore + Microsoft.\*」这条规矩**里面**,不破品牌承诺 —— 这是它和 Magicodes 的根本差别,不只是数量差。

---

## 3. 分层与文件清单

### 3.1 谁放哪(这是本方案最容易做错的地方)

```
TenonAdmin.Core          ← 全部契约 + DTO + Options + ErrorCode。零新依赖。
   ↑
TenonAdmin.Services      ← DictTextResolver(over IDictService)、ImportRunner(纯编排)、
                            UserImportProfile / UserExportProfile / OpLogExportProfile
   ↑
TenonAdmin.AspNetCore    ← 端点(每个资源显式路由)

TenonAdmin.Excel(卫星包) ← 只有 3 个 codec 实现 + 1 个装配扩展。只 ProjectReference Core。
```

**关键**:`ImportRunner`(编排:解析→映射→校验→判重→落库)**放 Services,不放卫星包**。它对 xlsx 一无所知(只操作已解析的行),却需要调 `IDictService`、查库判重、调 `IUserService.AddAsync` —— 而卫星包只引 Core,看不见这些。**卫星包里只放"把 xlsx 变成行/把行变成 xlsx"这一件事**,换 codec 就是换它。

`IDictTextResolver` 必须单独定义在 Core:`IDictService` 住在 Services 层,Core 与卫星包都看不见它。这是分层约束逼出来的接缝,不是过度设计。

### 3.2 新增/修改文件全清单

**新增 —— `backend/src/TenonAdmin.Core/ImportExport/`**

| 文件 | 内容 |
|---|---|
| `IExcelReader.cs` | codec:读 |
| `IExcelWriter.cs` | codec:写 |
| `IExcelTemplateBuilder.cs` | codec:模板 |
| `ExcelModels.cs` | `ImportColumn` / `ExportColumn` / `ImportRow` / `CellError` / `ImportPreview` / `ImportCommitResult` / `ExportSheet` / `TemplateSpec` / `DuplicateStrategy` |
| `IImportProfile.cs` | 领域:导入档案 |
| `IExportProfile.cs` | 领域:导出档案 |
| `IImportRunner.cs` | 领域:编排 |
| `IDictTextResolver.cs` | 字典 code ↔ label 双向 |
| `MissingExcelProvider.cs` | 三个 codec 的默认实现,一律抛 `ErrorCode.ExcelProviderMissing` |

**修改 —— Core**

| 文件 | 改动 |
|---|---|
| `ErrorCode.cs` | 新增 46xxx 段(§6.3),段落表注释同步加一行 |
| `Options/AdminExcelOptions.cs`(新) | 行数上限等 |
| `Options/TenonAdminOptions.cs` | 加 `public AdminExcelOptions Excel { get; set; } = new();` |

**新增 —— 卫星包 `backend/src/TenonAdmin.Excel/`**

| 文件 | 内容 |
|---|---|
| `TenonAdmin.Excel.csproj` | 照 `TenonAdmin.Caching.Redis.csproj` 形状;`ProjectReference` 仅 Core;`IsPackable` |
| `MiniExcelReader.cs` | `IExcelReader` |
| `MiniExcelWriter.cs` | `IExcelWriter` |
| `OpenXmlTemplateBuilder.cs` | `IExcelTemplateBuilder` |
| `ExcelSetup.cs` | `AddTenonAdminExcel()` —— TryAdd 三个 codec |

**新增 —— `backend/src/TenonAdmin.Services/ImportExport/`**

| 文件 | 内容 |
|---|---|
| `DictTextResolver.cs` | `IDictTextResolver`,over `IDictService.GetItemsByTypeAsync`(已有读穿透缓存) |
| `ImportRunner.cs` | `IImportRunner`,方法拆 `protected virtual` 小步 |
| `UserImportProfile.cs` | 用户导入档案 |
| `UserExportProfile.cs` | 用户导出档案 |
| `OpLogExportProfile.cs` | 操作日志导出档案 |

**修改 —— Services**

| 文件 | 改动 |
|---|---|
| `ServicesSetup.cs` | TryAdd 上述四个 + `MissingExcelProvider` 三件 |
| `User/IUserService.cs` + `User/UserService.cs` | **加** `ExportAsync`;把 `PageAsync` 的查询构造抽成 `protected virtual`(§8 坑 1) |
| `Log/ILogService.cs` + `LogService.cs` | 同上,加操作日志的 `ExportOpLogsAsync` |
| `Seed/DefaultMenuSeed.cs` | 6 个按钮节点(§6.2) |

**修改 —— AspNetCore**

| 文件 | 改动 |
|---|---|
| `TenonAdminSetup.cs` | `AddSingleton(options.Excel)`,与 Email / ExternalAuth / Realtime 同型 —— **G3 就要改**(`ImportRunner` / `ExportAsync` 要注入 `AdminExcelOptions`),不是 G4 才动 |
| `Controllers/UserController.cs` | 5 个导入端点 + 1 个导出端点 |
| `Controllers/SysLogController.cs` | 1 个导出端点 |

**修改 —— 样例宿主**

| 文件 | 改动 |
|---|---|
| `backend/samples/MinimalHost/MinimalHost.csproj` | 加 `TenonAdmin.Excel` 的 `ProjectReference` |
| `backend/samples/MinimalHost/Program.cs` | `AddTenonAdminExcel()` 放在 `AddTenonAdmin()` **之前**(TryAdd 语义:先注册者胜) |

不接这一步,样例站点七个导入导出端点全抛 46001。**§2 的「不装卫星包部署零影响」承诺,兑现方式正是这个开关** —— 样例宿主必须演示怎么打开,否则消费者照抄 MinimalHost 会得到一套全抛 46001 的端点而不知道差在哪。

**修改 —— 构建**

| 文件 | 改动 |
|---|---|
| `backend/Directory.Packages.props` | `MiniExcel` / `DocumentFormat.OpenXml` 两个 `PackageVersion` + 注释说明「仅卫星包引用」 |
| `backend/TenonAdmin.slnx` | `/src/` 下加 `TenonAdmin.Excel` |
| `backend/src/TenonAdmin/TenonAdmin.csproj` | **不动** —— 元包绝不引用卫星包(装了才有,这是可选性的定义) |

前端文件清单见 G6 / G7。

---

## 4. 契约(完整签名,照抄)

> 全部放 `namespace TenonAdmin.Core;`。XML 注释按仓内风格用中文写满,说清**为什么**,不只是**是什么**。

### 4.1 codec 层(卫星包实现,消费者可整体替换)

```csharp
/// <summary>
/// xlsx 读取(codec 层)。内核只定义抽象、不带任何 Excel 实现(运行时依赖纪律:仅 SqlSugarCore + Microsoft.*);
/// 默认实现 <c>MissingExcelProvider</c> 一律抛 <see cref="ErrorCode.ExcelProviderMissing"/>——
/// 这里刻意 fail-loud 而非静默(与 <c>NoopRealtimePublisher</c> 不同:实时是纯增强,导入没有 codec 就是不能用)。
/// 装 TenonAdmin.Excel 并在 AddTenonAdmin() 之前调 AddTenonAdminExcel() 即接管(TryAdd 前置替换,§5.2)。
/// </summary>
public interface IExcelReader
{
    /// <summary>只读首行表头(用于列映射建议),不读数据行。</summary>
    Task<IReadOnlyList<string>> ReadHeadersAsync(Stream file, CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式读数据行。<paramref name="headerToKey"/> 把表头文本映射到列 Key;未映射的表头列丢弃。
    /// <b>必须流式</b>(不得先 ToList 再返回),行数上限由调用方边读边计,超限即停 —— 这样恶意大文件
    /// 打不爆内存(设计 §14 稳健性)。
    /// </summary>
    IAsyncEnumerable<IReadOnlyDictionary<string, string?>> ReadRowsAsync(
        Stream file, IReadOnlyDictionary<string, string> headerToKey, CancellationToken cancellationToken = default);
}

/// <summary>xlsx 写出(codec 层)。同 <see cref="IExcelReader"/> 的替换模型。</summary>
public interface IExcelWriter
{
    /// <summary>把一张表写成 xlsx 字节。返回可直接交给 <c>FileResult</c> 的流(定位在 0)。</summary>
    Task<Stream> WriteAsync(ExportSheet sheet, CancellationToken cancellationToken = default);
}

/// <summary>导入模板生成(codec 层)。字典列要出真下拉,故此实现走 OpenXml 而非 MiniExcel(§2)。</summary>
public interface IExcelTemplateBuilder
{
    Task<Stream> BuildAsync(TemplateSpec spec, CancellationToken cancellationToken = default);
}
```

### 4.2 领域层(Services 实现;消费者按此写自己的实体)

```csharp
/// <summary>
/// 字典文本解析:<c>value ↔ label</c> 双向。<b>为什么单独一个接口</b>:字典表住在 Services 层
/// (<c>IDictService</c>),而 Core 与卫星包都看不见它;导入导出又必须在运行期查真字典
/// (不能用编译期特性,§2)。故在 Core 立此抽象,Services 用 <c>IDictService</c> 实现。
/// </summary>
public interface IDictTextResolver
{
    /// <summary>取某字典类型下"启用中"的全部项(value → label),按 Sort 升序。走 IDictService 的读穿透缓存。</summary>
    Task<IReadOnlyList<KeyValuePair<string, string>>> GetItemsAsync(string dictTypeCode, CancellationToken cancellationToken = default);

    /// <summary>value → label(导出用)。查不到返回原值,<b>不抛</b>——历史脏数据不该让整个导出失败。</summary>
    Task<string?> ToLabelAsync(string dictTypeCode, string? value, CancellationToken cancellationToken = default);

    /// <summary>label → value(导入用)。查不到返回 null,由调用方记 <see cref="ErrorCode.ImportCellDictInvalid"/>。
    /// 比对<b>去空白 + 大小写不敏感</b>(用户手敲的表格里"男 "和"男"必须同解)。</summary>
    Task<string?> ToValueAsync(string dictTypeCode, string? label, CancellationToken cancellationToken = default);
}

/// <summary>
/// 导入档案:一个实体"怎么导"的全部声明。**这是消费者要实现的接口**(内核给 <c>UserImportProfile</c> 作范例)。
/// 编排在 <see cref="IImportRunner"/>,本接口只声明规则与两个业务动作(查重、落行)。
/// </summary>
public interface IImportProfile
{
    /// <summary>档案编码,用于模板文件名与日志,如 <c>sys-user</c>。</summary>
    string Code { get; }

    /// <summary>模板与预览的列定义,<b>顺序即模板列顺序</b>。</summary>
    IReadOnlyList<ImportColumn> Columns { get; }

    /// <summary>业务键列 Key(判重依据,如 <c>["Account"]</c>);空集合 = 不判重。</summary>
    IReadOnlyList<string> BusinessKeys { get; }

    /// <summary>
    /// 行级自定义校验:跨列规则、按名查外键、越权检查都在这里。返回该行的全部错误(无错返回空)。
    /// <b>Runner 已先做过</b>必填、字典值、行内重复三项通用校验,本方法不必重复。
    /// </summary>
    Task<IReadOnlyList<CellError>> ValidateRowAsync(ImportRow row, CancellationToken cancellationToken = default);

    /// <summary>库内已存在的业务键集合。Runner 把本批全部业务键一次传入,<b>实现须一次查完</b>,不得逐行查库。</summary>
    Task<IReadOnlySet<string>> FindExistingKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 落一行。<paramref name="overwrite"/> 为 true 表示业务键已存在且策略是覆盖。
    /// <b>实现必须复用既有领域服务</b>(如 <c>IUserService.AddAsync</c>),不得直插实体绕过其安全不变量(§8 坑 5)。
    /// </summary>
    Task CommitRowAsync(ImportRow row, bool overwrite, CancellationToken cancellationToken = default);
}

/// <summary>导出档案:一个实体"能导哪些列"的声明。</summary>
public interface IExportProfile
{
    string Code { get; }
    IReadOnlyList<ExportColumn> Columns { get; }
}

/// <summary>
/// 导入编排(解析 → 映射 → 校验 → 判重 → 落库)。对 xlsx 一无所知,只调 <see cref="IExcelReader"/>。
/// 实现类 public、各步 <c>protected virtual</c>,消费者覆写一步即可(模板方法,§5.3)。
/// </summary>
public interface IImportRunner
{
    /// <summary>解析文件并全量校验。<paramref name="mapping"/> 为 null 时按表头模糊匹配自动生成并回传。</summary>
    Task<ImportPreview> PreviewAsync(Stream file, IReadOnlyDictionary<string, string>? mapping,
        IImportProfile profile, CancellationToken cancellationToken = default);

    /// <summary>对前端改过的行重新校验(不碰文件)。</summary>
    Task<ImportPreview> ValidateAsync(IReadOnlyList<ImportRow> rows, IImportProfile profile,
        CancellationToken cancellationToken = default);

    /// <summary>按策略落库。<b>部分提交</b>:有错的行跳过,不影响无错行;返回逐行结果。</summary>
    Task<ImportCommitResult> CommitAsync(IReadOnlyList<ImportRow> rows, IImportProfile profile,
        DuplicateStrategy strategy, CancellationToken cancellationToken = default);
}
```

### 4.3 DTO(`ExcelModels.cs`)

```csharp
/// <summary>导入列声明。</summary>
public sealed class ImportColumn
{
    /// <summary>列 Key = 属性名,如 <c>Account</c>。前端按它索引单元格。</summary>
    public required string Key { get; init; }
    /// <summary>表头文本(模板里写这个,也是列映射的匹配对象),如 <c>登录账号</c>。</summary>
    public required string Title { get; init; }
    /// <summary>必填(模板表头加 <c>*</c>,空值记 ImportCellRequired)。</summary>
    public bool Required { get; init; }
    /// <summary>非空 = 字典列:模板出下拉、导入 label→value、导出 value→label。</summary>
    public string? DictTypeCode { get; init; }
    /// <summary>「填写说明」sheet 的备注,如「填机构名称,如:深圳分公司」。</summary>
    public string? Hint { get; init; }
    /// <summary>模板列宽(字符数)。</summary>
    public int Width { get; init; } = 16;
}

/// <summary>导出列声明。</summary>
public sealed class ExportColumn
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    /// <summary>非空 = 字典列,导出时 value→label。</summary>
    public string? DictTypeCode { get; init; }
    /// <summary>前端「选列」弹窗的默认勾选态。</summary>
    public bool DefaultSelected { get; init; } = true;
    public int Width { get; init; } = 16;
}

/// <summary>
/// 一行导入数据。<b>单元格一律是字符串</b>——刻意不做泛型 <c>TRow</c>:数据来自表格本就是文本,
/// 又要在"预览→前端改错→重验"之间经 JSON 往返,字符串是唯一诚实的模型;类型转换在
/// <see cref="IImportProfile.CommitRowAsync"/> 里一次完成。这个决定省掉了整套泛型编排,别改回去。
/// </summary>
public sealed class ImportRow
{
    /// <summary>数据行号(1 起,对应 Excel 里表头之后的第几行)。前端展示用,错误定位靠它。</summary>
    public int Index { get; set; }
    /// <summary>列 Key → 单元格原文。</summary>
    public Dictionary<string, string?> Cells { get; set; } = [];
    /// <summary>服务端算出的错误;前端只读,提交时原样带回也会被服务端覆盖重算。</summary>
    public List<CellError> Errors { get; set; } = [];
}

/// <summary>单元格级错误。<b>只带码不带文案</b>(设计 §13.2):前端按 code 查 i18n 渲染。</summary>
public sealed record CellError(string ColumnKey, ErrorCode Code, IReadOnlyDictionary<string, object?>? Args = null);

/// <summary>预览/重验结果。</summary>
public sealed class ImportPreview
{
    /// <summary>文件里的原始表头(列映射 UI 的左侧)。重验时为空。</summary>
    public IReadOnlyList<string> Headers { get; set; } = [];
    /// <summary>实际生效的映射:表头文本 → 列 Key。前端据此回显并允许改。</summary>
    public IReadOnlyDictionary<string, string> Mapping { get; set; } = new Dictionary<string, string>();
    /// <summary>列定义(前端建预览表用,免得再拉一次档案)。</summary>
    public IReadOnlyList<ImportColumn> Columns { get; set; } = [];
    public IReadOnlyList<ImportRow> Rows { get; set; } = [];
    public int Total { get; set; }
    public int ErrorRows { get; set; }
    /// <summary>必填列没有被任何表头映射上 —— 这类错误不属于任何一行,单列在此。</summary>
    public IReadOnlyList<CellError> ColumnErrors { get; set; } = [];
}

/// <summary>重复处理策略。</summary>
public enum DuplicateStrategy
{
    /// <summary>跳过已存在的行(默认)。</summary>
    Skip = 0,
    /// <summary>覆盖已存在的行。</summary>
    Overwrite = 1,
    /// <summary>已存在即记为错误行,不导。</summary>
    Error = 2,
}

/// <summary>提交结果。</summary>
public sealed class ImportCommitResult
{
    public int Total { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    /// <summary>失败行(含原单元格 + 失败原因码),前端直接接回预览表继续改。</summary>
    public IReadOnlyList<ImportRow> Failures { get; set; } = [];
}

/// <summary>导出的一张表。</summary>
public sealed class ExportSheet
{
    public string SheetName { get; set; } = "Sheet1";
    public IReadOnlyList<ExportColumn> Columns { get; set; } = [];
    /// <summary>已完成字典翻译的行(列 Key → 单元格值)。codec 只负责写,不做业务转换。</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; set; } = [];
}

/// <summary>模板规格。</summary>
public sealed class TemplateSpec
{
    public string SheetName { get; set; } = "数据";
    public IReadOnlyList<ImportColumn> Columns { get; set; } = [];
    /// <summary>字典列的候选值(列 Key → label 列表),由调用方经 IDictTextResolver 备好;codec 只管写下拉。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DictOptions { get; set; }
        = new Dictionary<string, IReadOnlyList<string>>();
}

// —— 以下两个是 §5.1 三个 POST 的请求体(validate / error-report 收前者,commit 收后者)。
// 它们同属 Core 契约:前端按这个形状回传改过的行,不是 G4 越界改 Core。

/// <summary>回传行(重验、错误报告)。</summary>
public sealed class ImportRowsInput
{
    public List<ImportRow> Rows { get; set; } = [];
}

/// <summary>提交(回传行 + 重复策略)。</summary>
public sealed class ImportCommitInput
{
    public List<ImportRow> Rows { get; set; } = [];
    public DuplicateStrategy Strategy { get; set; } = DuplicateStrategy.Skip;
}
```

### 4.4 `ImportRunner` 的分步(每步 `protected virtual`)

```
PreviewAsync
 ├─ ReadHeadersAsync                       (codec)
 ├─ SuggestMapping(headers, columns)       ← 模糊匹配:去空白/去尾部 *「」()后精确比 Title,再比 Key(大小写不敏感)
 ├─ CheckRequiredColumns(mapping, columns) → ColumnErrors
 ├─ ReadRowsAsync 流式,边读边计数,超 MaxImportRows 抛 ImportRowLimitExceeded
 └─ ValidateAllAsync(rows, profile)
ValidateAllAsync                            ← ValidateAsync 也走这里,是唯一校验入口(避免两条链漂移)
 ├─ 通用:必填空 → ImportCellRequired
 ├─ 通用:字典列 label→value 解析失败 → ImportCellDictInvalid;成功则**就地把 Cells 里的 label 换成 value**
 ├─ 通用:业务键在文件内重复 → ImportCellDuplicateInFile(第 2 次及以后出现的那行记错)
 ├─ profile.ValidateRowAsync                ← 按名查外键、越权检查、跨列规则
 └─ profile.FindExistingKeysAsync(一次)     → 命中的行打 ImportCellDuplicateInDb(**仅作标记**,是否算错由 CommitAsync 的策略定)
CommitAsync
 ├─ 先跑一遍 ValidateAllAsync(**不信任前端送来的 Errors**,§8 坑 6)
 ├─ Error 策略:DuplicateInDb 视为错误 → 该行进 Failed
 ├─ Skip 策略 :DuplicateInDb 行不落库 → Skipped++
 ├─ Overwrite :DuplicateInDb 行 CommitRowAsync(row, overwrite: true) → Updated++
 └─ 逐行 try/catch:单行抛 AdminException → 记 Failed + 该码,**不中断整批**(部分提交)
```

**字典就地替换**是个要留意的细节:校验阶段把 `Cells["Gender"]` 从 `"男"` 换成 `"1"`,`CommitRowAsync` 拿到的就已经是 value。所以 `ValidateAsync` 返回给前端的行里,字典列显示的是 **value**,前端要按 `DictTypeCode` 用现成的 `DictTag` 渲染回 label —— 两个模板都有这个组件,直接用。

---

## 5. 端点契约

### 5.1 用户(`UserController`,`[Route("api/v1/sys/user")]`)

| 方法 | 路由 | 鉴权 | 说明 |
|---|---|---|---|
| GET | `import/template` | **`[ActiveSession]`** | 下模板。见 §5.3 为什么不用 `[RolePermission]` |
| POST | `import/preview` | `[RolePermission]` | `multipart/form-data`:`file` + 可选 `mapping`(JSON 字符串) |
| POST | `import/validate` | `[RolePermission]` | JSON:`{ rows: ImportRow[] }` |
| POST | `import/error-report` | `[RolePermission]` | JSON:`{ rows: ImportRow[] }` → xlsx(原列 + 「错误原因」列) |
| POST | `import/commit` | `[RolePermission]` + `[OperationLog("导入用户")]` | JSON:`{ rows: ImportRow[], strategy: 0\|1\|2 }` |
| GET | `export` | `[RolePermission]` + `[OperationLog("导出用户")]` | query:`UserPageInput` 全部字段 + `Columns`(逗号分隔的列 Key,缺省=全部 DefaultSelected) |

`SysLogController` 加一个:`GET api/v1/sys/log/op/export`,`[RolePermission]` + `[OperationLog("导出操作日志")]`。

### 5.2 返回形态

- **xlsx 三个端点**(`template` / `error-report` / `export`)返回 `FileStreamResult`,`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`,`FileDownloadName` 用 **RFC 5987** 编码(`filename*=UTF-8''...`)否则中文名在部分浏览器乱码。**它们不进 `Result<T>` 信封** —— `ResultEnvelopeFilter` 只包裸返回值,`FileStreamResult` 是 `IActionResult`,天然绕开,但要写测试钉住(§7)。
- **其余端点**返回裸 DTO,由 `ResultEnvelopeFilter` 包信封,与现有端点一致。

### 5.3 两个鉴权决定(写下来,免得 review 时被当疏漏)

- **`import/template` 用 `[ActiveSession]`**:它只泄露列名和字典 label,而这两样在页面上本就看得见(表单里就是那些字段和那些下拉)。而且导入按钮本身受 `POST .../import/preview` 的 `v-auth` 管着 —— 没权限的人根本看不到入口。少一个权限节点,角色-菜单 UI 清爽一点。
- **`import/preview` 必须 `[RolePermission]`**,不能图省事给 `[ActiveSession]`:它会调 `FindExistingKeysAsync` 查库判重,等于**账号是否存在的探测器**(账号枚举面)。这条别弄反。

### 5.4 为什么不做 `/api/v1/sys/import/{code}/preview` 通用控制器

想过,**否决,理由是安全不是风格**:榫卯的权限码就是规范化路由,通用路由会算出 `POST:/api/v1/sys/import/{code}/preview` **一个**权限码,于是"授了导入用户"的人自动也能导入客户、导入订单。每个资源显式路由 = 每个资源独立权限码,这是权限模型的必然要求。

---

## 6. 内核接线

### 6.1 `AdminExcelOptions`(新,挂 `TenonAdminOptions.Excel`,对应 `TenonAdmin:Excel` 节)

```csharp
public class AdminExcelOptions
{
    /// <summary>单次导入最大数据行数;超过拒收(ImportRowLimitExceeded)。防恶意大文件打爆内存。</summary>
    public int MaxImportRows { get; set; } = 5000;

    /// <summary>单次导出最大行数;超过拒绝(ExportRowLimitExceeded),提示先收窄筛选条件。</summary>
    public int MaxExportRows { get; set; } = 50000;

    /// <summary>导入文件大小上限(MB)。不复用 Upload.MaxSizeMb:导入文件不进存储、生命周期完全不同,
    /// 二者共用一个数字会让"调大头像上限"意外放开导入面。</summary>
    public int MaxImportFileSizeMb { get; set; } = 10;
}
```

### 6.2 菜单种子(`DefaultMenuSeed.cs`)

现最大 Id = **125**,内核区 `[1,999]`,新号从 126 起。**Sort 接各自页面下已有按钮的末尾**。

```csharp
// 用户导入导出(ParentId=15 用户管理页)。template 走 [ActiveSession] 无需权限节点(见 excel-ledger §5.3)。
new SysMenu { Id = 126, ParentId = 15, Type = MenuType.Button, Title = "用户-导入预览",   Permission = "POST:/api/v1/sys/user/import/preview",      Sort = 17, Enabled = true },
new SysMenu { Id = 127, ParentId = 15, Type = MenuType.Button, Title = "用户-导入重验",   Permission = "POST:/api/v1/sys/user/import/validate",     Sort = 18, Enabled = true },
new SysMenu { Id = 128, ParentId = 15, Type = MenuType.Button, Title = "用户-导入错误报告", Permission = "POST:/api/v1/sys/user/import/error-report", Sort = 19, Enabled = true },
new SysMenu { Id = 129, ParentId = 15, Type = MenuType.Button, Title = "用户-导入提交",   Permission = "POST:/api/v1/sys/user/import/commit",       Sort = 20, Enabled = true },
new SysMenu { Id = 130, ParentId = 15, Type = MenuType.Button, Title = "用户-导出",       Permission = "GET:/api/v1/sys/user/export",               Sort = 21, Enabled = true },
// 操作日志导出(ParentId=66 操作日志页)
new SysMenu { Id = 131, ParentId = 66, Type = MenuType.Button, Title = "操作日志-导出",   Permission = "GET:/api/v1/sys/log/op/export",             Sort = 5,  Enabled = true },
```

### 6.3 ErrorCode 46xxx 段(`ErrorCode.cs`)

顶部段落表注释加一行:`<item><term>46000–46999</term><description>导入 / 导出</description></item>`。

```csharp
// ── 46xxx 导入 / 导出 ────────────────────────────────────────────

/// <summary>未安装 TenonAdmin.Excel(或未在 AddTenonAdmin() 之前调 AddTenonAdminExcel())</summary>
[MsgKey("error.excel.providerMissing")]      ExcelProviderMissing      = 46001,
/// <summary>导入文件为空</summary>
[MsgKey("error.import.fileEmpty")]           ImportFileEmpty           = 46002,
/// <summary>导入行数超过 TenonAdmin:Excel:MaxImportRows</summary>
[MsgKey("error.import.rowLimitExceeded")]    ImportRowLimitExceeded    = 46003,
/// <summary>必填列没有被任何表头映射上(列级,不属于任何一行)</summary>
[MsgKey("error.import.columnMissing")]       ImportColumnMissing       = 46004,
/// <summary>单元格必填但为空</summary>
[MsgKey("error.import.cellRequired")]        ImportCellRequired        = 46005,
/// <summary>字典列的值不在该字典的启用项里</summary>
[MsgKey("error.import.cellDictInvalid")]     ImportCellDictInvalid     = 46006,
/// <summary>按名查外键失败(机构名/岗位名/角色名/主管姓名在库里找不到)</summary>
[MsgKey("error.import.cellRefNotFound")]     ImportCellRefNotFound     = 46007,
/// <summary>单元格格式不合法(日期/数字/邮箱/手机号)</summary>
[MsgKey("error.import.cellFormatInvalid")]   ImportCellFormatInvalid   = 46008,
/// <summary>业务键在本文件内重复</summary>
[MsgKey("error.import.duplicateInFile")]     ImportDuplicateInFile     = 46009,
/// <summary>业务键在库中已存在(Error 策略下才算错误)</summary>
[MsgKey("error.import.duplicateInDb")]       ImportDuplicateInDb       = 46010,
/// <summary>导入行指定的机构不在当前用户的数据范围内(越权写入,§3.4)</summary>
[MsgKey("error.import.orgOutOfScope")]       ImportOrgOutOfScope       = 46011,
/// <summary>导出结果超过 TenonAdmin:Excel:MaxExportRows,请先收窄筛选条件</summary>
[MsgKey("error.export.tooManyRows")]         ExportRowLimitExceeded    = 46012,
/// <summary>请求导出的列不在该档案的可导列里</summary>
[MsgKey("error.export.columnInvalid")]       ExportColumnInvalid       = 46013,
```

**文件过大 / 后缀不对不要新开码** —— 复用 44xxx 已有的 `FileTooLarge` 等(先读 `ErrorCode.cs` 的 44xxx 段确认名字)。语义相同的码开两个,只会让前端文案也重复两遍。

### 6.4 i18n 键

zh-CN 与 en-US **各 13 条**,两个模板**都要**:
`web/src/locales/{zh-CN,en-US}.ts` 与 `web-react/src/locales/{zh-CN,en-US}.ts`。
⚠ `ErrorCodeLocaleConsistencyTests` **只查 `web/`**(见 §7),`web-react/` 漏了不会红 —— 靠纪律,别漏。

---

## 7. 闸门测试(不满足会红,先知道)

| 测试 | 它要求什么 | 对本次的影响 |
|---|---|---|
| `PermissionCodeConsistencyTests` | **双向锁**:①种子里每个权限码必须对应真实的 `[RolePermission]` 端点;②每个 `[RolePermission]` 端点必须有种子节点,**或**显式登记进 `KnownUnseededEndpoints` | 6 个新端点必须**同批**加 §6.2 的菜单种子,少一个即红。路由字符串一字不差(含大小写、`{id}` 占位) |
| `ErrorCodeLocaleConsistencyTests` | 每个 `[MsgKey]` 的**叶子段**在 `web/src/locales/{zh-CN,en-US}.ts` 里都能找到 `叶子:` | 13 个新码的 zh/en 文案漏一条即红。**注意它按叶子名子串匹配**,所以叶子名别和已有的撞(撞了会假绿) |
| （规则，非测试） | **新增错误码的 MsgKey 叶子名必须在整个 `ErrorCode` 枚举里唯一** | 否则该闸门对其中一个码静默失效（G1 返工：`export.rowLimitExceeded` 与 `import.rowLimitExceeded` 同叶子 → 假绿；已改为 `error.export.tooManyRows`）。G2–G8 新增码时先自查 |
| `SeedIdRangeTests` | 内核种子 Id 必须落在 `[1, KernelMax]` | 126–131 合规 |
| `OperationLogCoverageTests` | 写操作默认留痕(opt-out) | `import/commit` 自动留痕;**导出是 GET,默认不记**,必须显式挂 `[OperationLog]` |
| `ReplaceabilityTests` | 新增可替换接口要有替换用例 | G5 补 `IExcelReader/Writer/TemplateBuilder` + `IImportRunner` + `IDictTextResolver` 五条 |

---

## 8. 已知的坑(全部实测过,别重新踩)

**坑 1 — `PagedListExtensions.MAX_SIZE = 200`,导出不能走 `PageAsync`。**
`ToPagedListAsync` 里有 `size = Math.Min(size, MAX_SIZE)`。传 `Size = 50000` **不会报错**,会静默返回 200 行 —— 导出看起来成功,数据少了 99%。
**做法**:给 `UserService` 加 `ExportAsync`,把 `PageAsync` 里从 `.AsQueryable()` 到 `.Select(投影)` 那一段抽成 `protected virtual ISugarQueryable<UserItem> BuildListQuery(UserPageInput input, List<long>? holders)`,`PageAsync` 和 `ExportAsync` **共用**它,后者 `.Take(MaxExportRows + 1).ToListAsync()`(多取一条用来判断是否超限)。
**不要复制一份过滤条件** —— 两条链一旦漂移,导出的数据范围就和列表不一致,而这正是要演示的招牌能力,错了没人看得出来。
**不要改 `PageAsync` 的签名**(它是 `public virtual`,消费者可能已覆写),只抽内部。

**坑 2 — `unwrap()` 处理不了二进制。**
`web/src/api/index.ts` 的 `unwrap` 假设 body 是 JSON 信封。下载 xlsx 照抄同文件里 `fileApi.download` 的写法:`parseAs: 'blob'` + 手动查 `r.response.ok`。

**坑 3 — 上传 multipart 要自定义 `bodySerializer`。**
照抄 `fileApi.upload`:`body: { file: file as unknown as string }` + `bodySerializer` 建 `FormData`。openapi-fetch 见到 `FormData` 不会注入 json header,浏览器自动补 boundary。

**坑 4 — 演示模式会挡住 preview/validate。**
`DemoModeFilter` 只放行 GET/HEAD/OPTIONS 与 `/api/v1/auth/*`,而 preview/validate/commit 都是 POST → 演示站点根本进不了导入向导。**这是可接受的**(演示站不该让人上传文件),但前端要给出像样的提示而不是一个裸 403,且要写测试钉住这个行为是有意的。导出是 GET,演示模式下正常可用。

**坑 5 — 导入必须走 `IUserService.AddAsync`,不许直插实体。**
`AddUserInput` **刻意没有 `IsSuperAdmin` 字段**(防提权),`AddAsync` 里还有:软删行也参与账号查重、密码策略校验、`MustChangePassword = true`、用户+角色包事务、密码历史记录。绕过去自己 `InsertAsync` 会一次性丢掉全部这些不变量。

**坑 6 — `CommitAsync` 不能信任前端送来的 `Errors`。**
行数据从浏览器往返过,`Errors` 字段是客户端可控的。提交时必须**重新跑一遍完整校验**,把送上来的 `Errors` 直接丢弃。否则把 `errors: []` 一改,越权机构、非法字典值全部长驱直入。

**坑 7 — 两个前端模板零共享是硬约束。**
`web/` 与 `web-react/` 之间不许有任何 import,不许抽 `web-shared`,不许写「必须同时装两个」之类的耦合说明。这个方向被推翻过一次,理由记在 `docs/react-template-ledger.md`。导入向导要**真写两遍**,文案和 token 也维护两遍,这是有意为之。

**坑 8 — antd v6 的改名在 `tsc` 下是静默的。**
写 `web-react/` 任何组件前先 `antd info <C> --version 6.x` / `antd demo`,写完 `antd lint <file>`。凭 v5 记忆写会编译通过、运行时样式全错。

**坑 9 — Core 不许加 NuGet 依赖。**
G1 只加接口和 DTO,`TenonAdmin.Core.csproj` 一行不改。`MiniExcel` / `DocumentFormat.OpenXml` 只出现在卫星包的 csproj 里,`Directory.Packages.props` 的两条 `PackageVersion` 要写注释说明「仅 TenonAdmin.Excel 引用」(照 `StackExchange.Redis` 那条的写法)。

**坑 10 — 元包不引卫星包。**
`backend/src/TenonAdmin/TenonAdmin.csproj` 不动。装了 `TenonAdmin` 不等于装了 Excel,这是「可选」的定义,也是部署零影响承诺的兑现方式。

**坑 11 — 字典列的 label→value 必须幂等(G6 实走发现,已修)。**
`ImportRunner` 校验时会把字典 label 就地换成 value 写回 `row.Cells`,**预览返回给前端的已经是 value**。前端改完错原样送回,「重新校验」和「提交」会再走一遍同一段代码 —— 这次拿到的是 value 不是 label。原实现只做 `ToValueAsync(label)`,于是**任何带字典列的档案在预览通过之后,必然在重新校验和提交上被判 46006**,整条向导对用户导入根本不可用。
现已在 `ImportRunner` 里补:`ToValueAsync` 返回 null 时,再用 `GetItemsAsync` 判断 raw 是否本身就是合法 value,是则幂等接受(非法值仍照旧拦下)。
**为什么单测没抓到**:G5 的字典用例每次都手工造带 label 的行,永远走不到第二遍。回归用例是 `PreviewRows_FedBackTo_ValidateAndCommit_AreIdempotentOnDictColumns` —— 它把 **Preview 的输出**喂回 Validate/Commit。给自己的实体接导入时,凡有字典列都照这个形状写测试。

**坑 13 — 判重的 `IN` 查询有参数上限,必须分批(已在 Runner 兜住)。**
档案的常规写法 `Where(x => keys.Contains(x.Key))` 翻成 `IN (@p0, @p1, …)`,**一个业务键一个参数**。SQL Server 单语句参数上限 **2100(硬限)**、老版 SQLite 999、PostgreSQL 65535。而 `MaxImportRows` 默认 **5000** —— 不分批的话,在 SqlServer 上导入超过约 2100 行**必然抛异常**,且默认配置主动允许到五千行。
**分批放在 `ImportRunner.FindExistingKeysBatchedAsync`(`ExistingKeyBatchSize` 默认 500),不放在各档案里** —— 消费者照 `skills/wire-import-export.md` 抄出来的档案会原样带同一个缺陷,兜在编排层所有档案(含消费者自己的)才一并免疫,档案实现保持「一条 `IN` 查询」的简单形状。写自己的档案时**不必也不该**再自己分批。
**为什么之前没发现**:G5 第 10 条测行数上限时把 `MaxImportRows` 调到 2、传 3 行,从没有一条测试真发几千行;而 SqlServer 那条腿在 push/PR 上只跑方言子集,这类用例大概率也不在子集内 —— 即**上线前 CI 也未必会红**。

**坑 12 — 四条前端闸门全绿 ≠ 页面能用。**
`typecheck / lint / test / build` 都不进浏览器,**渲染期才失效的东西一个都照不出来**。G6 实走撞到的具体例子:错误格红底写成 `color-mix(in srgb, var(--n-error-color) 18%, transparent)`,而 `--n-error-color` 是 naive 组件内部变量、在那层普通 div 上未定义 → 整条声明被判无效丢弃 → **红底从未渲染过,四条闸门全绿**。用仓库自己的 `--color-danger-bg`(亮暗都有定义)。
G7 的 antd 版同理:**别只信闸门,一定起 dev server 把向导从上传点到提交走一遍**,并核实错误格是真的有底色(读 `getComputedStyle().backgroundColor`,别只看类名挂上了)。

**坑 14 — 要给某列加下拉,先查内核有没有现成字典,别加平行机制。**
`ImportColumn` 出下拉**只认 `DictTypeCode`** 这一条路。碰到布尔/枚举列没下拉,第一反应容易是「给 `ImportColumn` 加个非字典的固定候选值属性」—— 第 11 轮就这么派工出去过,写到一半才发现 `DictSeed.cs` 里 `common_status`(启用=1/停用=0)**早就种好了,只是没人用**。挂上字典即得模板下拉 + 向导 `DictSelect` + label→value 翻译,零契约改动、零前端改动、不用 `gen:api`;加平行机制则是给同一件事造第二套表达,还剥夺了消费者在字典管理界面改选项的能力。
**先查 `DictSeed.cs`**(现有 `common_status` / `org_category` / `gender` 三个),没有再考虑新种一个字典类型;真到了「候选值不该进字典」的场景再谈扩契约,并把理由写进 §1 决策全表。

---

## 9. 批次(每条一个独立提交)

> 每条的「变异判据」必须真跑:把实现改坏 → 确认对应测试变红 → 改回。**没红过的测试不算数。**

### - [x] G1 · Core 契约 + Options + ErrorCode 段
**改**:§3.2 的 Core 新增 9 个文件 + `ErrorCode.cs` + `AdminExcelOptions.cs` + `TenonAdminOptions.cs` + `ServicesSetup.cs`(TryAdd 三个 `MissingExcelProvider`)+ 两个模板各 2 个 locale 文件(13 条 zh/en)+ `refinement-ledger.md` 未排期备忘里定时任务的 46xxx 改 47xxx。
**验收**:`dotnet build` 绿;`dotnet test --filter "FullyQualifiedName~ErrorCodeLocaleConsistency"` 绿;`TenonAdmin.Core.csproj` **零改动**(`git diff --stat` 里不出现)。
**变异**:删掉任意一条新码的 zh-CN 文案 → `ErrorCodeLocaleConsistencyTests` 必须红。

### - [x] G2 · 卫星包 `TenonAdmin.Excel`(codec)
**改**:新建 4 个文件 + `Directory.Packages.props` + `TenonAdmin.slnx`。
**内容**:`MiniExcelReader`(`ReadHeadersAsync` / 流式 `ReadRowsAsync`)、`MiniExcelWriter`、`OpenXmlTemplateBuilder`(表头行 + `*` 标必填 + 列宽 + 字典列 `dataValidation` 下拉 + 「填写说明」sheet)、`ExcelSetup.AddTenonAdminExcel()`。
**验收**:`dotnet build` 绿;写一个小程序或测试真生成一份模板并**用 Excel/WPS 打开确认下拉能点开**(下拉是取 OpenXml 的唯一理由,不验等于没做)。
**变异**:把 `AddTenonAdminExcel()` 的调用去掉 → 模板端点必须返回 `46001`,而不是 500 或空文件。

### - [x] G3 · `DictTextResolver` + `ImportRunner` + 三个 Profile
**改**:§3.2 的 Services 新增 5 个文件 + `ServicesSetup.cs` + `IUserService`/`UserService`(坑 1 的抽取 + `ExportAsync`)+ `ILogService`/`LogService`(`ExportOpLogsAsync`)。
**`UserImportProfile` 的列**:`Account*` / `Name*` / `Nickname` / `Phone` / `Email` / `Gender`(字典 `gender`)/ `OrgName*`(按名查)/ `PositionName`(按名查)/ `DirectorName`(按名查)/ `RoleNames`(按名查,逗号分隔)/ `Enabled`(字典或"是/否")。业务键 = `["Account"]`。
`ValidateRowAsync` 负责:四处按名查外键(查不到 → `ImportCellRefNotFound`)、**机构越权检查**(解析出的 OrgId 不在当前用户数据范围 → `ImportOrgOutOfScope`)、手机/邮箱格式。
`CommitRowAsync` 调 `IUserService.AddAsync`(新增)/ `UpdateAsync`(覆盖),**不直插实体**(坑 5)。
**验收**:`dotnet build` 绿;单测层面能跑通"一批行 → 预览 → 提交"。
**变异**:把 `CommitAsync` 里的"重新校验"那一步注释掉 → G5 里那条「篡改 Errors 仍被拦」必须红。

### - [x] G4 · 端点 + 菜单种子 + `gen:api`
**改**:`UserController`(6 个端点)、`SysLogController`(1 个)、`DefaultMenuSeed.cs`(6 行)、两个模板各跑一次 `npm run gen:api`(需后端在跑)。
**验收**:`dotnet test --filter "FullyQualifiedName~PermissionCodeConsistency"` 绿;起 MinimalHost 用 curl 真打一遍 7 个端点(带 Bearer),xlsx 三个要能下到真文件并打开;两个 `schema.d.ts` 都已更新。
**变异**:把 §6.2 里任意一行菜单种子删掉 → `PermissionCodeConsistencyTests` 的反向锁必须红。

### - [x] G5 · 后端测试(新建 `ImportExportTests.cs`,必要时拆几个文件)
必须覆盖(每条都写清「变异什么会让它红」):
1. **六件套**:`IExcelReader`/`IExcelWriter`/`IExcelTemplateBuilder`/`IImportRunner`/`IDictTextResolver` 各一条 Replace 用例,补进 `ReplaceabilityTests.cs`。
2. **未装卫星包 → 46001**:默认宿主(不调 `AddTenonAdminExcel`)打模板端点,断言码 46001。
3. **字典双向**:导出时 `Gender="1"` 出「男」;导入时「男」进库成 `"1"`;导入「男性」→ `ImportCellDictInvalid`。
4. **按名查外键**:机构名对 → OrgId 正确;机构名错 → `ImportCellRefNotFound`。
5. **越权机构被拒**(安全):用一个数据范围受限的账号导入,行里填范围外的机构名 → `ImportOrgOutOfScope`,且**库里没多出这一行**。
6. **不建超管**:导入行里塞 `IsSuperAdmin` 之类的列 → 建出来的用户 `IsSuperAdmin == false`。
7. **篡改 Errors 无效**(坑 6):构造一个真有错的行,把 `errors` 手动清空后提交 → 仍被拦。
8. **三种重复策略**:Skip / Overwrite / Error 各一条,断言 `Inserted/Updated/Skipped/Failed` 计数。
9. **部分提交**:10 行里 3 行有错 → 7 行入库、`Failed == 3`,且 7 行是真的能查到。
10. **行数上限**:`MaxImportRows` 调到 2,传 3 行 → `ImportRowLimitExceeded`。
11. **导出上限**:`MaxExportRows` 调到 1,库里 2 行 → `ExportRowLimitExceeded`。
12. **导出经数据范围过滤**(招牌能力):三个不同数据范围的账号打**同一个导出端点** → 三个不同行数,且各自看不到对方的行。**这条是本批最重要的测试。**
    ⚠ **立项时写错了一件事,G5 已订正**:`sys_user` 继承 `BaseEntity`,**不受 `IOrgScoped` 全局过滤器约束** —— 用户导出按权限码放行,不按机构裁剪;全内核没有一张 `DataEntity`(那是给消费方业务表用的)。所以这条只能挂在 TestHost 的 `SampleDoc` 上:给它加 `SampleDocExportProfile` + `GET /api/v1/sample/doc/export`,证明**任何建在本内核导出路径上的业务导出都白捡数据范围过滤** —— 这才是接 CRM 的那句话。
    ⚠ **断言对象必须是导出回来的 xlsx 内容**,不许拿列表端点的返回值抄进 writer 再断言 —— 那样测的是列表过滤,导出链路整条没覆盖(初版正是如此:把导出端点改成 `ClearFilter<IOrgScoped>()` 仍然全绿)。
13. **导出不被信封包裹**:断言 `Content-Type` 是 xlsx、body 前两字节是 `PK`。
14. **演示模式**:开 `DemoMode` → `import/commit` 403 + 码 41002;`export` 200。
15. **操作日志**:commit 与 export 后,`/api/v1/sys/log/op/page` 里查得到对应条目。
**验收**:`dotnet test backend/TenonAdmin.slnx` 全绿,总数比现在(320)多出新增条数。

### - [x] G6 · `web/` 导入向导 + 接线
**新增**:`web/src/components/ImportWizard/`(`index.vue` + `README.md`)、`web/src/components/ExportColumnsModal/`。
**改**:`web/src/api/index.ts`(userApi 加 6 个方法 + logApi 加 1 个,下载走坑 2、上传走坑 3)、`web/src/views/system/user/index.vue`(`#toolbar` 加两个按钮,`v-auth` 用 §6.2 的权限码)、`web/src/views/system/log/op/index.vue`(导出按钮)、`web/src/locales/*`、`web/COMPONENTS.md`。
**向导四步**(`n-steps`):①上传(`n-upload`,拖拽)②列映射(左=文件表头,右=`n-select` 选目标列;自动匹配的预选上)③预览改错(**裸 `n-data-table`**,不用 ProTable —— 要可编辑单元格 + 错误高亮;错误格红底 + `n-tooltip` 显示 `translateError(code)`;顶部「只看错误行」开关;「重新校验」按钮)④结果(计数 + 失败行可回到第③步继续改 + 「下载错误报告」)。
**导出**:按钮弹 `ExportColumnsModal`(列勾选,默认按 `DefaultSelected`),确认后带**当前 ProTable 的筛选条件**请求,拿到 blob 触发下载。
**验收**:`npm run typecheck && npm run lint && npm test && npm run build` 全绿;**`npm run dev` 真点一遍**:下模板 → 填 3 行(含 1 行错)→ 上传 → 看到错误格标红 → 就地改对 → 重验转绿 → 提交 → 列表里查得到新用户。
**变异**:把「重新校验」按钮的请求改成不带 rows → 必须能观察到明确失败,而不是静默显示旧结果。

### - [x] G7 · `web-react/` 同款
**新增**:`web-react/src/components/ImportWizard.tsx` + `ExportColumnsModal.tsx`(+ 各自 `.spec.tsx`)。
**改**:`web-react/src/api/index.ts`、`views/system/user/index.tsx`(`toolbar` 加按钮,用 `<Can code="...">`)、`views/system/log/op/index.tsx`、`locales/*`、`web-react/COMPONENTS.md`。
用 antd `Steps` + `Table`(**不要塞进 `DataTable`** —— 它是 pro-components 的薄封装,向导要的是可编辑表格,性质不同)。
**⚠ 写之前 `antd info Steps --version 6.x` / `antd demo`,写完 `antd lint <file>`**(坑 8)。
**⚠ 命令必须在 `web-react/` 目录跑**(cd 错了 build 会 ENOENT 静默跑错)。
**验收**:`npm run typecheck && npm run lint && npm test && npm run build` 全绿;`npm run dev`(:5174)真点一遍同样的流程。

### - [x] G8 · 文档
**改**:`skills/` 新增一篇「给自己的实体接导入导出」(消费者视角:实现 `IImportProfile` 的最小样板 + 6 个端点怎么加 + 菜单种子怎么取号)并在 `skills/README.md` 挂上;`.claude/skills/` 加薄包装;`rebuild-design.md:165` 订正库选型(指向本文件 §2)、`:305`/`:320` 的卫星包清单标注 Excel 已做;`site/` 加文档页(**先读 `skills/write-docs.md`**,写完 `cd site && npm run lint:prose -- <page>`);`CHANGELOG.md`。

---

## 10. 验证命令

```bash
# 后端(仓库根)
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test  backend/TenonAdmin.slnx
dotnet test  backend/TenonAdmin.slnx --filter "FullyQualifiedName~ImportExport"
dotnet run   --project backend/samples/MinimalHost      # :5100,配合 curl 实打

# 前端(各自目录,两个重进程不并发)
cd web        && npm run typecheck && npm run lint && npm test && npm run build && npm run gen:api
cd web-react  && npm run typecheck && npm run lint && npm test && npm run build && npm run gen:api
```

`gen:api` 需要后端在跑。**两个模板的 `gen:api` 是各自独立的脚本**,都要跑。

---

## 11. 明确不做(有依据,防反复)

| 项 | 理由 |
|---|---|
| 异步导出中心 / 导出任务队列 | 同步下载够用(`refinement-ledger.md` 不做清单已记);真需要时定时任务是现成载体 |
| 服务端导入会话 / 临时表 | 零状态设计已免掉它;加了就要管 TTL、多副本、PII 落缓存 |
| 通用 `/import/{code}` 控制器 | §5.4,权限模型不允许 |
| 泛型 `TRow` 导入 | §4.3,字符串单元格才是诚实模型 |
| 导出脱敏内核内置 | 业务策略;消费者在 `IExportProfile` 里自己决定,内核替人猜脱敏规则是越界 |
| 前端解析 xlsx | 要引 SheetJS 进两个模板,且前端导出**证明不了数据范围**(只能导当前页已拿到的数据) |
| 导入全量事务回滚 | 部分提交 + 错误报告已覆盖实用面;大文件全量事务是锁灾难 |
| 从两个前端模板抽共享向导组件 | 坑 7,方向已推翻,别再抽 |
| Magicodes / EPPlus / NPOI / ClosedXML | §2 |

---

## 12. 轮次日志

### 第 11 轮 — 启用状态改挂 `common_status` 字典(2026-07-26)
**起因**:用户实测两条反馈。①「5173 所有页面 404」—— 排查后**不是缺陷**:后端菜单接口正常、19 个路由菜单的 `component` 逐个核对 `web/src/views` **一个不缺**、全新隔离会话登录→选应用→逐个点菜单全通、深链接硬刷新也通。真因是当时两个 vite 进程已死(只剩后端在监听);杀掉 vite 后在已打开的页面里点菜单可复现:`ERR_CONNECTION_REFUSED` + `Unhandled error during execution of async component loader` → 内容区空白。②「用户管理里状态是 switch,导入模板里却是自由文本框」—— **是真缺陷**。

**根因**:`ImportColumn` 出下拉**只有 `DictTypeCode` 一条路**(`OpenXmlTemplateBuilder` 见到非字典列直接 `continue`,两个向导的单元格也是 `col.dictTypeCode ? 下拉 : 文本框`),而 `Enabled` 是布尔列没挂字典。

**改**:`UserImportProfile` 的 `Enabled` 列挂上 `DictTypeSeed.COMMON_STATUS_CODE`(启用=1 / 停用=0)。**这个字典内核早就种了**(`DictSeed.cs`,备注写着「启用/停用等二态开关的通用字典」),但在此之前**没有任何代码在用它**。挂上即得:模板下拉、向导 `DictSelect`、label→value 翻译,全是现成机制。`ValidateRowAsync` 里的 `ParseEnabled` 检查保留作纵深防御(Commit 走前端回传的 Cells,不能只信上游),但加了「该格已被字典判过错就不再重复报」,免得一个单元格出两条错。

**走过又退掉的路(别重走)**:先按「给 `ImportColumn` 加非字典的固定候选值 `Options` + 改 codec + 改两个向导 + 两边 `gen:api`」派了工,写到一半才发现 `common_status` 已存在。**加平行机制是错的**:内核已有闭合二态的表达方式,消费者也该被引导去用字典(它还能在字典管理界面改)。派出去的改动已 `git checkout` 全退。教训与坑 12 同源 —— 动手改契约前先查内核已有什么。

**顺带**:导出侧本来就是对的(`UserController.GetUserCell` 里 `"Enabled" => u.Enabled ? "启用" : "停用"`),导出文件写的就是中文 label,与新的导入下拉正好对得上,导出→改→再导入闭环通。

**行为收窄(照实说)**:`Hint` 从「启用/停用,或 是/否」改成「下拉选择」。挂字典后手写的「是/否/true/yes」会被字典闸门判 `ImportCellDictInvalid`,不再被接受(`1`/`0` 仍可,它们是字典 value)。本特性从未合并发布,不存在存量文件,故无兼容负担。

**验收**:全量 `dotnet test` 347/347 绿(第 9 轮基线 345 + 2)。**变异**:摘掉 `Enabled` 列的 `DictTypeCode` → 两条新用例都红(模板那条报「K 列没有 dataValidation」,链路那条报单元格仍是「启用」而非「1」)→ 改回绿。链路那条刻意断言在 `ValidateAsync` 的**返回行**上 —— `Commit`/`Validate` 都深拷贝入参(坑 6 防篡改),入参 `Cells` 永远不会被改;且只断言落库结果会**假绿**,因为 `ParseEnabled` 本来就认「停用」。

### 第 13 轮 — 分支首次推送,CI 15 个检查全绿(2026-07-26)
八批做完后第一次把 `feat/excel` 推上远端(14 个提交),开草稿 PR **#23**(base `dev`)。

**先踩了个预期错误**:原以为「推分支就会跑 CI」。实际 `backend-ci.yml` 与 `docker-smoke.yml` 的 `on.push.branches` 都是 `[main, dev]`,推 `feat/excel` **一个 workflow 都没触发**(`gh run list --branch feat/excel` 为空)。要拿检查只有 `pull_request` 一条路,**必须开 PR**。

**触发的是 15 个检查,不是预估的 7 个** —— 漏算了两个前端模板各自的 CI(`lint` / `lint-build` / `test (1..3)` / `drift`)。结果:**15 pass / 0 fail / 2 skipping**(`deploy`、`nightly-alert` 按条件跳过)。后端七个全过:`build-test` 的 sqlite / mysql / sqlserver / postgres 四条腿、`template-smoke`、docker `single` 与 `multi`。

**这一轮的实质收获**:此前八批只在 SQLite 上验过(§12 第 9 轮列的合并风险②),MySQL / PostgreSQL / SqlServer 的方言差异是真空。现在三条腿都绿了,`template-smoke` 也证明消费者的 `dotnet new tenon-app` 没被这批改动弄坏。

**仍未覆盖(别当成全绿就万事大吉)**:SqlServer 腿在 push/PR 上只跑方言子集,**完整套件靠 `schedule` 夜间跑,而 `schedule` 只在默认分支上跑,不会跑 `feat/excel`** —— 即本批代码的 SqlServer 全量验证要等合并进默认分支后的那一夜才会真正发生。

### 第 12 轮 — 字典下拉的弹层不再被列宽挤窄(2026-07-26)
**起因**:第 11 轮实走时顺手看到的 —— 向导预览表里点开字典下拉,选项被截成「启..」「停..」。性别列(男/女)同款,**是 G6/G7 就有的,不是第 11 轮引入的**。

**改**:两个模板各在向导的单元格 `DictSelect` 上把弹层宽度与触发器解耦(`web` 用 naive 2.44.1 的 `consistentMenuWidth: false`,`web-react` 用 antd v6 的 `popupMatchSelectWidth={false}`;两个属性名都对着 `node_modules` / `antd info` 核实过,不是凭记忆写的)。**只改向导用法,不动共用的 `DictSelect`** —— 普通表单里下拉够宽,跟随触发器反而更整齐。

**两侧不对称,照实说**:实测 `web` 触发器宽 **66px**,弹层跟随后选项真被截断,改完弹层 72px、`启用`/`停用` 完整(`scrollWidth <= clientWidth`);`web-react` 触发器 **119px**,**开关这个属性前后测得完全一样(119/119,均未截断)** —— 那边本来就没这个毛病,加上是防列数增多。反事实是真跑出来的:把 `popupMatchSelectWidth` 临时翻成 `true` 重走一遍向导再量,不是推的。
**顺带修回一处误伤**:用 `sed` 做反事实时把 `web-react` 列映射 Select 上**原有的** `popupMatchSelectWidth={false}`(G7 就写了)一起翻成了 `true`,已复原;`git diff` 确认最终改动只有 4 行新增。

**验收**:`web` typecheck / lint / 62 用例 / build 全绿;`web-react` typecheck / lint / `antd lint` / 730 用例 / build 全绿。两个模板都起 dev server 实走了向导(上传 → 列映射 → 预览 → 点开启用状态下拉),坑 12 要求的实走不可省。

### 第 10 轮 — 下载触发收敛成带测试的工具(2026-07-25)
**起因**:第 7 轮记「下载按钮四条路径全过」时,顺带记下 `triggerDownload` 那 9 行在两个模板里各抄了三份(向导 + 用户页 + 操作日志页,均为 G6/G7 本批引入)。浏览器实走**走完就没了**,而这段的两种错法都是静默的 —— 漏 `revokeObjectURL` 内存泄漏、漏 `download` 名把文件存成一串 uuid,都不报错、不影响构建、`typecheck`/`lint`/`build` 全绿(坑 12 同款)。
**改**:两模板各建 `src/utils/download.ts` 的 `triggerBlobDownload`,六处内联全部改为引用;`web/` 新增 `download.spec.ts`(镜像 `web-react` 既有的 `fileFormat.spec` 那条:断言 `createObjectURL` 收到该 blob、`<a download>` 是给定文件名、`click()` 被调、`revokeObjectURL` 收到同一 URL、用完从 DOM 移除);`web-react` 侧原实现住在 `views/system/file/fileFormat.ts`(位置不对,组件不该往视图目录里 import),移到 `utils/download.ts` 并在原处**同名再导出**,文件页与它既有的用例一行不动。
**验收**:`web` typecheck / lint / test **62 通过(16 文件,较前 +1 文件 +1 条)**;`web-react` typecheck / lint 绿。
**边界(照实说)**:这是把已实走验证过的行为**固化成每次 CI 都跑的用例**,不是新验证 —— 四条路径的实走结论以第 7 轮那张表为准。另:`web/` 的文件页(`views/system/file/index.vue`)另有一份更早的同款内联(非本次引入),未动。

### 第 9 轮 — 合并前风险处置:判重查询分批(2026-07-25)
提交 `fix(excel): batch the duplicate-key lookup under the parameter limit`。**起因**:八批做完后过一遍合并风险,发现 `MaxImportRows` 默认 5000 而判重走 `keys.Contains(...)` → `IN` 一键一参,**SqlServer 参数上限 2100 是硬限**,即默认配置下导入两千多行必炸(详见 §8 坑 13)。**改**:`ImportRunner` 新增 `ExistingKeyBatchSize`(默认 500,`protected virtual`)与 `FindExistingKeysBatchedAsync`,分批调档案并合并结果 —— **兜在编排层而非各档案**,消费者照指南抄的档案一并免疫;同步改 `IImportProfile.FindExistingKeysAsync` 的契约注释与 `skills/wire-import-export.md` 的样板注释(「Runner 已分批,别自己再分」)。**验收**:全量 `dotnet test` 345/345 绿(G8 基线 344+1)。**变异**:把分批换回 `profile.FindExistingKeysAsync(keys, …)` 单次调用 → `ExistingKeyLookup_IsBatched_BelowDatabaseParameterLimit` 红(单批 1200 > 500)→ 改回绿。新用例同时钉住**合并结果不丢**:三个「已存在」的键刻意分落第 1/2/3 批,全部要被判 `ImportDuplicateInDb`。
**仍未处置的合并风险(按序)**:①`backend-release.yml` 的 pack 不枚举项目,合进 main 后首个 `v*` tag 会把 `TenonAdmin.Excel` **永久发布到 nuget.org**(包名与 `AddTenonAdminExcel()` 入口届时定死)—— 产品决定,未动 CI;②本分支**从未推送**,七个检查一次没跑,只在 SQLite 上验过;③「已存在」被当错误呈现(第 7 轮记);④向导里**手动改列映射、覆盖/报错两种策略走 UI、必填列未映射的 `columnErrors` 分支**三条路径从未实走(实走只覆盖「自动映射全中 + 跳过策略」主路)。

### 第 8 轮 — G8 文档(2026-07-25)
提交 `docs(excel): consumer guide, site page, and ledger close-out for import/export`。**改**:`skills/wire-import-export.md`(消费者接入指南:装 `TenonAdmin.Excel` + `AddTenonAdminExcel()` 前置、`IImportProfile`/`IExportProfile` 最小样板照 `SampleDoc`/`UserImportProfile`、六个端点、菜单种子取号 §6.2、**坑 11 字典幂等**与**坑 1 导出不得走 PageAsync**)+ `skills/README.md` 挂表与斜杠命令 + `.claude/skills/wire-import-export/` 薄包装;`docs/rebuild-design.md` 订正 Magicodes 定稿为指向本台账 §2、v1.x 清单与「明确不做」标注 Excel 已做;`site/zh|en/guide/import-export.md` + 侧栏 + `community/agent-skills` 挂 skill;`CHANGELOG.md` Unreleased 段。**闸门**:`cd site && npm run lint:prose -- zh/guide/import-export.md` 硬拦 0(见提交说明 / 轮次贴出的结果)。**未动**:代码与 CI;台账「留给 G8 裁定」的码表/导出列一致性测试**原样留下、本轮不裁定**。G 批全部勾完。

### 第 7 轮 — G7 `web-react/` 同款导入向导 + 接线(2026-07-25)
提交 `feat(web-react): add the import wizard and export column picker`。**改**:新增 `ImportWizard.tsx`(四步 antd `Steps` + 裸 `Table` 可编辑预览;错误格 `background: var(--color-danger-bg)` 坑 12;API 由父级注入)+ `ExportColumnsModal.tsx`(+ 各自 `.spec.tsx`);`api/index.ts` 加 userApi 六方法 + logApi 一方法(下载走 `unwrapDownload`/坑 2,上传走 `bodySerializer`/坑 3)+ 归一 Preview/Commit;`utils/error.ts` 补数值码→msgKey 表(与 web 有意重复,坑 7);`views/system/user/index.tsx` / `log/op/index.tsx` 加按钮(`<Can code>` 用 §6.2 权限码,fetcher 截 lastQuery 带当前筛选);两个 locale 的 `import.*`/`export.*` UI 键、`COMPONENTS.md`、`types/api.ts` 导入导出 DTO。写前 `antd info Steps/Table/Modal/Upload --version 6.x`,写完 `antd lint` 清掉 v6 弃用(`Space.orientation`、`Alert.title`、受控 Upload `onChange`)。**闸门**:`typecheck` / `lint` / `test 730/730` / `build` 四条全绿(均在 `web-react/` 目录)。**浏览器实走(维护者做,chrome-devtools 驱动 :5174 + :5100)**:上传 3 行(2 对 1 错,账号前缀 `rct-` 以避开 G6 已导入的)→ 表头 11/11 自动映射 → 预览「共 3 行,其中 1 行有错误」→ **错误格底色实测 `rgb(249, 230, 234)` = `--color-danger-bg`(真渲染,不是只挂上了类名)** → 改对机构名 → 重新校验 0 错 → 提交「新增 3 / 失败 0」→ 经后端 `user/page` 核实三行确已落库、机构正确、`gender` 已是字典值 1/2;筛选 `Account=rct-ok-1` 后导出请求为 `?Account=rct-ok-1&columns=…`(带上了当前筛选);`antd lint` 四个文件均 0 issue;控制台无 `Maximum update depth exceeded`(zustand selector 没造成无限重渲染)。
**维护者补记 —— `ImportWizard.spec.tsx` 里那条坑 12 用例是假守卫**:它断言的是 `errCell.style.background` 这个**字符串**含 `var(--color-danger-bg)`,而 jsdom 根本不解析 CSS 变量 —— **变量哪怕压根没定义,这条也照绿**,正是 G6 踩的形状。真正钉住它的是上面浏览器里读 `getComputedStyle().backgroundColor`。渲染期才失效的东西**没有单测能替代实走**(坑 12),别因为 spec 绿了就跳过点检。
**遗留 —— 「已存在」被当成错误呈现(两个模板都有,后端是对的)**:对着**已导过一次**的库重跑向导时,预览显示「共 3 行,其中 3 行有错误」,登录账号列全部标红。查下来后端没问题 —— `CommitAsync` 把 `ImportDuplicateInDb` 排除在 `hardErrors` 之外,再按策略分流(Skip→跳过 / Overwrite→更新 / Error→失败),G5 清单 8 正钉住这个。问题在**预览端只按「有无 errors」判定并标红**,而 §6.3 明写 46010 是「Error 策略下才算错误」。于是在跳过/覆盖策略下,一批完全正常的行会被显示成必须修的错误 —— 尤其覆盖策略,用户看到满屏红却其实会被正常更新。
**改法建议(未做)**:`ImportPreview` 把「硬错误」与「已存在」分开计数,前端对后者用警示色 + 「已存在,将按策略处理」而非错误红;或预览请求带上当前策略。属于呈现层缺陷,不影响落库正确性,故未在 G7 顺手改 —— 动它要同时改后端 DTO 和两个模板,该单独一批。

**遗留(沿第 6 轮,已扩)**:前端有**两处**镜像后端档案且无任何闸门钉住 —— ①两份 `CODE_MSG_KEY`(数值码→msgKey);②两份 `userExportColumns`(导出列清单;后端没有列清单端点,两个模板只能各自硬编码)。后端一旦改码值 / MsgKey / `UserExportProfile.Columns`,四个前端文件要跟着改,漏改只会静默显示错文案或少给一列。要不要加一致性测试(比对 OpenAPI 或后端导出的码表/列表),留给 G8 或后续裁定。G8 未碰。

### 第 6 轮 — G6 `web/` 导入向导 + 接线(2026-07-25)
提交 `feat(web): add the import wizard and export column picker`。**改**:新增 `components/ImportWizard/`(590 行 `index.vue` + README,四步 `n-steps`;第③步裸 `n-data-table` + 可编辑单元格 + `NTooltip`;API 由父级经 `ImportWizardApi` 注入,组件对资源无感知)与 `components/ExportColumnsModal/`;`api/index.ts` 加 userApi 六方法 + logApi 一方法(下载走 `unwrapDownload`/坑 2,上传走 `bodySerializer`/坑 3);`views/system/user/index.vue`、`views/system/log/op/index.vue` 加按钮(`v-auth` 用 §6.2 权限码);`utils/error.ts`、两个 locale、`COMPONENTS.md`。**闸门**:`typecheck` / `lint` / `test 61/61` / `build` 四条全绿。
**浏览器实走(维护者做,chrome-devtools 驱动 :5173 + :5100)**:上传 3 行(2 对 1 错)→ 表头 11/11 自动映射 → 预览「共 3 行,其中 1 行有错误」→ 改对机构名 → 重新校验 0 错 → 提交「新增 3 / 失败 0」→ 列表查得到三个新用户且机构正确;「只看错误行」3→1;导出弹窗按 `DefaultSelected` 预选,请求带上当前筛选(`?Account=imp-ok-1&columns=…`)。
**实走查出两个缺陷,闸门全绿也照不出来(详见 §8 坑 11 / 坑 12)**:①**后端**字典 label→value 不幂等 → 预览通过的字典单元格在重新校验/提交上必被判 46006,向导对用户导入完全不可用;修 `ImportRunner` 并补 `PreviewRows_FedBackTo_ValidateAndCommit_AreIdempotentOnDictColumns`,**变异**(去掉幂等接受那段)→ 该用例红(`Assert.Equal() Failure`)→ 改回绿。②**前端**错误格红底用了未定义的 `--n-error-color`,`color-mix()` 整条失效、红底从未渲染;改用 `--color-danger-bg` 后实测 `rgb(255, 236, 237)`。
**下载按钮实走验证(两个模板各两个按钮,四条路径全过)**。做法:先 hook `URL.createObjectURL` 与 `HTMLAnchorElement.prototype.click`,再点按钮,读 hook 记录 —— 直接证明「请求 → Blob → 触发下载」整条通,比翻浏览器下载目录可靠。结果:

| 模板 | 下载模板 | 下载错误报告 |
|---|---|---|
| `web/`(Vue) | Blob 3348B | Blob 5218B |
| `web-react/` | Blob 3349B | Blob 5218B |

四者 MIME 均为 spreadsheet,`<a download="用户导入模板.xlsx" / "用户导入错误报告.xlsx">` 均指向 `blob:`。(模板差 1 字节是 xlsx 内嵌时间戳,无意义。)

**遗留(未修,记账)**:`web/src/utils/error.ts` 里手抄了一张 `数值码 → msgKey` 表(46001–46013 等)。这是 §4.3「`CellError` 只带码不带文案」的必然结果 —— 信封有 `msgKey`,单元格错误没有,前端只能自己查。但**没有任何闸门钉住这张表与后端 `ErrorCode` 枚举一致**:改了码值或 MsgKey,向导会静默显示错文案。G7 按零共享约定会再抄一份(两份都得改)。要不要加一条一致性测试(比对 OpenAPI 或后端导出的码表),留给 G8 或后续批次裁定。G7 及以后未碰。

### 第 5 轮 — G5 后端测试(2026-07-25)
提交 `test(excel): cover import/export domain, data-scope export, and replaceability`。**改**:`ReplaceabilityTests` 补五件套(IExcelReader/Writer/TemplateBuilder/IImportRunner/IDictTextResolver);`ImportExportTests` 补字典双向/外键/不建超管/三种重复策略/部分提交/导入上限/导出上限/演示模式/操作日志(G3–G4 已有的 2/7/13 与 200 截断保留);新建 `ImportExportScopeTests`(清单 5 越权机构 HTTP 真账号 + 清单 12 三账号数据范围导出)。清单 12 用 `SampleDoc`(DataEntity)经真登录管道列再写 xlsx——`sys_user` 不走 IOrgScoped,招牌能力挂业务表。**验收**:全量 `dotnet test backend/TenonAdmin.slnx` **343/343 绿**(G4 基线 327+16)。**变异**:①`IsOrgInScope` 恒 true → `Import_OrgOutOfScope_Rejected_And_NotInserted` 红(`Expected: 0 Actual: 1` on inserted) → 改回绿;②注释 `CommitAsync` 里 `ValidateAllAsync` → `CommitAsync_TamperedErrors_StillBlocked` 红(`Expected: 0 Actual: 1` on Inserted) → 改回绿;③`IOrgScoped` 过滤器改 `e => true` → `Export_ThreeAccounts_DifferentDataScopes_SeeDifferentRows` 红(`Expected: 3 Actual: 6`) → 改回绿。

**维护者返工(清单 12 初版是假绿)**:初版 `ExportAs` 打的是**列表**端点 `/api/v1/sample/doc`,把返回的 titles 自己塞进 `IExcelWriter` 再断言 —— 全文 0 处提到导出路径,改坏导出照样绿(与已有 `SampleDocScopeTests` 重复,却挂着「招牌导出」的名)。**改法**:TestHost 新增 `SampleDocExportProfile` + `SampleDocController.Export`(`GET:/api/v1/sample/doc/export`,取数走 `ListAsync` 与列表同源),测试改为真打该端点、用 `IExcelReader` 从**返回的 xlsx** 里读回行断言,角色同时授予列表与导出两个权限码。**新变异(旧版抓不住的那个)**:把 `Export` 改成 `mutationRepo.AsQueryable().ClearFilter<IOrgScoped>()` → 红(`Expected: 3 Actual: 6`,前端组账号看到全部 6 行)→ 改回绿。**顺带订正立项错误**:§12 第 0 轮原写「`UserService` 里一行 `WHERE org_id` 都没有」,而 `sys_user` 是 `BaseEntity` 不受机构裁剪 —— 招牌能力成立但例子举错,已在第 0 轮与清单 12 就地标注。G6 及以后未碰。

### 第 4 轮 — G4 端点 + 菜单种子 + gen:api(2026-07-25)
提交 `feat(excel): wire import/export endpoints, menu seeds, and OpenAPI schemas`。**改**:`UserController` 六端点(template ActiveSession;preview/validate/error-report/commit RolePermission;export+OperationLog)+ `SysLogController` op/export + `DefaultMenuSeed` Id 126–131 六行(照抄 §6.2)+ `ImportRowsInput`/`ImportCommitInput` + MinimalHost 引卫星包并 `AddTenonAdminExcel()` + 两模板 `schema.d.ts` gen:api。测试:`Export_ReturnsXlsxStream_NotResultEnvelope`(§5.2:非信封、spreadsheet Content-Type、`filename*=UTF-8''`、body 以 PK 开头)。**验收**:`PermissionCodeConsistency` 2/2 绿;MinimalHost curl 七端点均 200(template 3347B / error-report 4967B / user-export 5359B / op-export 5260B,均含 RFC 5987);preview/validate/commit 信封 code=0(commit inserted=1);两模板 schema 含七条路径;全量 `dotnet test` 327/327 绿(G3 基线 326+1)。**变异**:删 §6.2 Id=131 菜单种子 → 反向锁红(`GET:/api/v1/sys/log/op/export` 既无种子也未登记 KnownUnseededEndpoints) → 改回绿。**台账回填(两处是台账自己漏写,不是实现越界)**:§4.3 补 `ImportRowsInput`/`ImportCommitInput`(§5.1 三个 POST 的请求体,原 DTO 清单没写);§3.2 补「修改 —— 样例宿主」表(MinimalHost 引卫星包 + `AddTenonAdminExcel()` 前置,§2 的「不装卫星包零影响」承诺就靠这个开关兑现)。G5 及以后未碰。

### 第 3 轮 — G3 DictTextResolver + ImportRunner + 三个 Profile(2026-07-25)
提交 `feat(excel): add ImportRunner, DictTextResolver, and user/op-log profiles`。**改**:`Services/ImportExport/` 五文件(`DictTextResolver` over `IDictService`、`ImportRunner` 分步 virtual 编排含 Commit 重校验、`UserImportProfile`/`UserExportProfile`/`OpLogExportProfile`)+ `ServicesSetup` TryAdd 五件 + `IUserService.ExportAsync`/`UserService.BuildListQuery` 抽取(坑 1,不改 PageAsync 签名)+ `ILogService.ExportOpLogsAsync`/`LogService.BuildOpListQuery` + `TenonAdminSetup` 注册 `options.Excel` 单例;`UserImportProfile.CommitRowAsync` 只走 `IUserService.AddAsync`/`UpdateAsync`(坑 5)。测试:`ImportExportTests` 两条(坑 6 篡改 Errors / 坑 1 导出不截断 200)。**验收**:`dotnet build -c Release` 绿(0 error);全量 `dotnet test` 326/326 绿(G2 基线 324+2)。**变异**:①注释 `CommitAsync` 里 `ValidateAllAsync` → `CommitAsync_TamperedErrors_StillBlocked` 红(`Assert.Equal() Failure: Expected: 0 Actual: 1` on Inserted) → 改回绿;②`ExportAsync` 改走 `PageAsync(Size=50000)` → `ExportAsync_NotTruncatedAt200` 红(`导出不得被 200 截断,实际 200`) → 改回绿。G4 及以后未碰。

### 第 2 轮 — G2 卫星包 TenonAdmin.Excel codec(2026-07-25)
提交 `feat(excel): add TenonAdmin.Excel satellite with MiniExcel and OpenXml codecs`。**改**:新建 `backend/src/TenonAdmin.Excel/`(`MiniExcelReader` / `MiniExcelWriter` / `OpenXmlTemplateBuilder` / `ExcelSetup.AddTenonAdminExcel` / csproj,仅 ProjectReference Core)+ `Directory.Packages.props` 增 MiniExcel 1.45.0 与 DocumentFormat.OpenXml 3.5.1(注释「仅卫星包」)+ `TenonAdmin.slnx` 纳入;`TenonAdmin.csproj` 元包未动。测试:`ExcelCodecTests` 四条(缺包 46001 / TryAdd 前置胜出 / 模板 dataValidation+字典 label / 读写 round-trip);模板落盘仓库外 `C:\Project\HuHuHu\excel-artifacts\user-template.xlsx`。**验收**:`dotnet build -c Release` 绿(0 error);全量 `dotnet test` 324/324 绿(G1 基线 320+4)。**变异**:①注释 `ExcelSetup` 里 `IExcelTemplateBuilder` 的 `TryAddSingleton` → `AddTenonAdminExcel_BeforeKernel_WinsTryAdd` 红(`Expected OpenXmlTemplateBuilder, Actual MissingExcelProvider`) → 改回绿;②让 builder 写 dataValidation 但不往 `_dict` 写 label(「填写说明」hint 仍有男/女)→ 旧断言假绿;收紧后 `OpenXmlTemplateBuilder_WritesDataValidation_WithDictLabels` 沿 formula1→workbook sheet 名→r:id→rels Target 解析到真正 worksheet,断言**那张** sheet 含「男」「女」且 `state="hidden"` → 红(`Assert.Contains() Failure: Sub-string not found / Not found: "男"`) → 改回绿,ExcelCodecTests 4/4。G3 及以后未碰。**报告(未改 CI)**:`backend-release.yml` 的 Pack 是 `dotnet pack backend/TenonAdmin.slnx`(按 slnx 内 `IsPackable` 打),**未**显式枚举包项目;新卫星包进 slnx 且 `IsPackable=true` 即会被 pack/push,无需改 workflow——是否接受由维护者裁定。

### 第 1 轮 — G1 Core 契约 + Options + ErrorCode(2026-07-25)
提交 `feat(excel): add Core import/export contracts and ErrorCode 46xxx`(`git log --grep=ErrorCode.46xxx`)。**改**:`ImportExport/` 九文件(codec/领域/DTO/`MissingExcelProvider`)+ `AdminExcelOptions` + `TenonAdminOptions.Excel` + `ErrorCode` 46xxx(13 码;导出上限 MsgKey 为 `error.export.tooManyRows`,叶子唯一)+ `ServicesSetup` TryAdd 三 codec 默认实现 + 两模板各 13 条 zh/en + `refinement-ledger` 定时任务段位 46→47。**验收**:`dotnet build backend/TenonAdmin.slnx -c Release` 绿(0 error);`ErrorCodeLocaleConsistency` 1/1 绿;`git diff --stat` 无 `TenonAdmin.Core.csproj`。**变异**:①删 `web/src/locales/zh-CN.ts` 的 `cellRequired` → 红:`error.import.cellRequired (zh-CN 缺 cellRequired)` → 改回绿;②`orgOutOfScope` 叶子唯一、删该行 → 红;③删 `export.tooManyRows` → 红:`error.export.tooManyRows (zh-CN 缺 tooManyRows)` → 改回绿(若仍用同名 `rowLimitExceeded`,删 export 行仍假绿)。G2 未碰。

### 第 0 轮 — 立项(2026-07-25)
`/grill-with-docs` 走完:候选池只剩定时任务 / Excel / 分发三条,用户选 Excel。评估证据:14 条 issue 无一条要这两个功能(都是 push 不是 pull),但**导出能接上 CRM 头条第二幕**(同一个导出按钮,总部 214 行 / 深圳 42 行,业务 Service 里一行 `WHERE org_id` 都没有)〔**G5 订正**:原文写的是「`UserService` 里一行 `WHERE org_id` 都没有」,**这句是错的** —— `sys_user` 是 `BaseEntity`,用户导出根本不经机构裁剪。该说法对**消费方的 `DataEntity` 业务表**成立(CRM 的客户表正是这种),对内核自带表不成立;招牌能力本身没问题,举错了例子。见 §9 G5 清单 12〕、定时任务接不上;定时任务另有归属打架未决 + 自写 cron 与多副本租约是最危险的代码面,故排后。库选型经实测推翻 `rebuild-design.md:165` 的 Magicodes 定稿(§2)。范围与功能取舍见 §1。执行期改由外部 agent 施工、维护者审批,故本文件按「可直接施工」的粒度重写(§4 完整签名 / §5 端点契约 / §7 闸门 / §8 十个坑 / §9 逐条变异判据)。**尚未开工。**
