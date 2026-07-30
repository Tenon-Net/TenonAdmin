using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 用户管理端点(设计 §4 组织模块)。全部 <c>[RolePermission]</c>——超管放行,普通用户需被授予对应路由权限码。
/// 安全细节(账号唯一、不出密码哈希、不建/改超管、超管不可删停)由 <see cref="IUserService"/> 保证。
/// <para>导入/导出(excel-ledger §5):每个资源显式路由,不做通用 <c>/import/{code}</c> 控制器(§5.4 权限模型要求)。
/// 三个 xlsx 端点返回 <see cref="FileStreamResult"/>,不进 <c>Result&lt;T&gt;</c> 信封(§5.2)。</para>
/// </summary>
[ApiController]
[Route("api/v1/sys/user")]
public class UserController(
    IUserService users,
    IImportRunner importRunner,
    UserImportProfile importProfile,
    UserExportProfile exportProfile,
    IExcelTemplateBuilder templates,
    IExcelWriter writer,
    IDictTextResolver dict,
    AdminExcelOptions excel) : ControllerBase
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>分页查询用户</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<UserItem>>> Page([FromQuery] UserPageInput input) =>
        Result<PagedList<UserItem>>.Ok(await users.PageAsync(input));

    /// <summary>用户详情(含角色 Id)</summary>
    [HttpGet("{id}")]
    [RolePermission]
    public async Task<Result<UserDetail>> Get(long id) =>
        Result<UserDetail>.Ok(await users.GetAsync(id));

    /// <summary>新增用户,返回新用户 Id + 实际生效的初始口令(留空口令时由系统随机生成,不回传则无人知晓)</summary>
    [HttpPost]
    [RolePermission]
    [RequireReauth]
    [OperationLog("新增用户")]   // 入参含 Password,操作日志里会被脱敏为 ***;出参明文不进日志(过滤器只记入参)
    public async Task<Result<AddUserOutput>> Add(AddUserInput input) =>
        Result<AddUserOutput>.Ok(await users.AddAsync(input));

    /// <summary>更新用户资料与角色</summary>
    [HttpPut("{id}")]
    [RolePermission]
    [RequireReauth]
    public async Task<Result<bool>> Update(long id, UpdateUserInput input)
    {
        await users.UpdateAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>软删除用户</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    [RequireReauth]
    public async Task<Result<bool>> Delete(long id)
    {
        await users.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }

    /// <summary>批量软删除用户(集合含超管则整体拒绝)</summary>
    [HttpPost("batch-delete")]
    [RolePermission]
    [RequireReauth]
    [OperationLog("批量删除用户")]
    public async Task<Result<bool>> BatchDelete(BatchDeleteInput input)
    {
        await users.DeleteBatchAsync(input.Ids);
        return Result<bool>.Ok(true);
    }

    /// <summary>重置密码;返回实际生效的初始密码(供管理员当场转达)</summary>
    [HttpPut("{id}/password")]
    [RolePermission]
    [RequireReauth]
    [OperationLog("重置用户密码")]   // 入参 NewPassword 会被脱敏;返回的明文密码不进日志(过滤器只记入参)
    public async Task<Result<string>> ResetPassword(long id, ResetPasswordInput input) =>
        Result<string>.Ok(await users.ResetPasswordAsync(id, input.NewPassword));

    /// <summary>启用/停用</summary>
    [HttpPut("{id}/enabled")]
    [RolePermission]
    [RequireReauth]
    public async Task<Result<bool>> SetEnabled(long id, SetEnabledInput input)
    {
        await users.SetEnabledAsync(id, input.Enabled);
        return Result<bool>.Ok(true);
    }

    // ── 导入 / 导出(excel-ledger §5.1) ──────────────────────────────────

    /// <summary>
    /// 下载用户导入模板(xlsx)。只泄露列名与字典 label(表单里本就可见),故
    /// <c>[ActiveSession]</c> 而非 <c>[RolePermission]</c>(excel-ledger §5.3);
    /// 导入入口由 <c>import/preview</c> 的权限码管着。
    /// </summary>
    [HttpGet("import/template")]
    [ActiveSession]
    public async Task<IActionResult> ImportTemplate(CancellationToken cancellationToken)
    {
        // DictOptions:列 Key → label 列表(模板下拉展示 label;导入时 Runner 再 label→value)
        var dictOptions = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var col in importProfile.Columns.Where(c => !string.IsNullOrEmpty(c.DictTypeCode)))
        {
            var items = await dict.GetItemsAsync(col.DictTypeCode!, cancellationToken);
            dictOptions[col.Key] = items.Select(kv => kv.Value).ToList();
        }

        var stream = await templates.BuildAsync(new TemplateSpec
        {
            SheetName = "数据",
            Columns = importProfile.Columns,
            DictOptions = dictOptions,
        }, cancellationToken);

        // FileDownloadName 走 ASP.NET 的 Content-Disposition 编码(含 RFC 5987 filename*=UTF-8'')
        return File(stream, XlsxContentType, "用户导入模板.xlsx");
    }

    /// <summary>
    /// 上传 xlsx 并全量预览校验。<c>multipart/form-data</c>:<c>file</c> + 可选 <c>mapping</c>(JSON 字符串:表头→列 Key)。
    /// 必须 <c>[RolePermission]</c>——会调 <c>FindExistingKeysAsync</c> 查库判重,等于账号枚举面(§5.3)。
    /// </summary>
    [HttpPost("import/preview")]
    [RolePermission]
    [RequestSizeLimit(32 * 1024 * 1024)] // 上限由业务 MaxImportFileSizeMb 再卡;此处挡畸形超大 body
    public async Task<Result<ImportPreview>> ImportPreview(
        IFormFile file,
        [FromForm] string? mapping,
        CancellationToken cancellationToken)
    {
        ValidateImportFile(file);
        IReadOnlyDictionary<string, string>? map = null;
        if (!string.IsNullOrWhiteSpace(mapping))
        {
            map = JsonSerializer.Deserialize<Dictionary<string, string>>(mapping)
                  ?? new Dictionary<string, string>();
        }

        await using var stream = file.OpenReadStream();
        // 拷到可 Seek 内存流:codec 先读表头再读行,ImportRunner 可能复位 Position
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        var preview = await importRunner.PreviewAsync(ms, map, importProfile, cancellationToken);
        return Result<ImportPreview>.Ok(preview);
    }

    /// <summary>对前端改过的行重新校验(不碰文件)。</summary>
    [HttpPost("import/validate")]
    [RolePermission]
    public async Task<Result<ImportPreview>> ImportValidate(
        ImportRowsInput input, CancellationToken cancellationToken)
    {
        var preview = await importRunner.ValidateAsync(input.Rows, importProfile, cancellationToken);
        return Result<ImportPreview>.Ok(preview);
    }

    /// <summary>下载错误报告 xlsx(原列 +「错误原因」列)。返回文件流,不进信封(§5.2)。</summary>
    [HttpPost("import/error-report")]
    [RolePermission]
    public async Task<IActionResult> ImportErrorReport(
        ImportRowsInput input, CancellationToken cancellationToken)
    {
        var cols = importProfile.Columns
            .Select(c => new ExportColumn { Key = c.Key, Title = c.Title, Width = c.Width })
            .Append(new ExportColumn { Key = "_errors", Title = "错误原因", Width = 40 })
            .ToList();

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var row in input.Rows)
        {
            var cells = new Dictionary<string, object?>();
            foreach (var col in importProfile.Columns)
                cells[col.Key] = row.Cells.TryGetValue(col.Key, out var v) ? v : null;
            // 只带码不带文案(设计 §13.2);前端/人眼都能对码
            cells["_errors"] = string.Join("; ",
                row.Errors.Select(e => $"{e.ColumnKey}:{e.Code}({(int)e.Code})"));
            rows.Add(cells);
        }

        var stream = await writer.WriteAsync(new ExportSheet
        {
            SheetName = "错误报告",
            Columns = cols,
            Rows = rows,
        }, cancellationToken);

        return File(stream, XlsxContentType, "用户导入错误报告.xlsx");
    }

    /// <summary>按策略提交导入。<b>部分提交</b>:有错行跳过。服务端会重跑完整校验,不信任前端 Errors(坑 6)。</summary>
    [HttpPost("import/commit")]
    [RolePermission]
    [OperationLog("导入用户")]
    public async Task<Result<ImportCommitResult>> ImportCommit(
        ImportCommitInput input, CancellationToken cancellationToken)
    {
        var result = await importRunner.CommitAsync(
            input.Rows, importProfile, input.Strategy, cancellationToken);
        return Result<ImportCommitResult>.Ok(result);
    }

    /// <summary>
    /// 导出用户 xlsx。query 复用 <see cref="UserPageInput"/> 全部筛选 + <c>Columns</c>(逗号分隔列 Key,缺省=全部 DefaultSelected)。
    /// GET 默认不进操作日志,显式挂 <c>[OperationLog]</c>(§7)。返回文件流,不进信封(§5.2)。
    /// </summary>
    [HttpGet("export")]
    [RolePermission]
    [OperationLog("导出用户")]
    public async Task<IActionResult> Export(
        [FromQuery] UserPageInput input,
        [FromQuery] string? columns,
        CancellationToken cancellationToken)
    {
        var selected = ResolveExportColumns(exportProfile, columns);
        var items = await users.ExportAsync(input);

        var rows = new List<IReadOnlyDictionary<string, object?>>(items.Count);
        foreach (var u in items)
        {
            var cells = new Dictionary<string, object?>();
            foreach (var col in selected)
            {
                var raw = GetUserCell(u, col.Key);
                if (!string.IsNullOrEmpty(col.DictTypeCode) && raw is string s)
                    cells[col.Key] = await dict.ToLabelAsync(col.DictTypeCode, s, cancellationToken);
                else
                    cells[col.Key] = raw;
            }
            rows.Add(cells);
        }

        var stream = await writer.WriteAsync(new ExportSheet
        {
            SheetName = "用户",
            Columns = selected,
            Rows = rows,
        }, cancellationToken);

        return File(stream, XlsxContentType, "用户导出.xlsx");
    }

    /// <summary>校验导入文件非空、后缀 .xlsx、不超过 MaxImportFileSizeMb(复用 44xxx 码,§6.3)。</summary>
    private void ValidateImportFile(IFormFile? file)
    {
        AdminException.ThrowIf(file is null || file.Length <= 0, ErrorCode.FileEmpty);
        var ext = Path.GetExtension(file!.FileName);
        AdminException.ThrowIf(
            !string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase),
            ErrorCode.FileExtNotAllowed,
            new Dictionary<string, object?> { ["ext"] = ext });
        var maxBytes = (long)excel.MaxImportFileSizeMb * 1024 * 1024;
        AdminException.ThrowIf(file.Length > maxBytes, ErrorCode.FileTooLarge,
            new Dictionary<string, object?> { ["maxSizeMb"] = excel.MaxImportFileSizeMb });
    }

    /// <summary>解析导出列:空 = 档案里 DefaultSelected;非空 = 逗号分隔 Key,非法 Key → ExportColumnInvalid。</summary>
    internal static IReadOnlyList<ExportColumn> ResolveExportColumns(IExportProfile profile, string? columnsCsv)
    {
        if (string.IsNullOrWhiteSpace(columnsCsv))
            return profile.Columns.Where(c => c.DefaultSelected).ToList();

        var keys = columnsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var byKey = profile.Columns.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        var list = new List<ExportColumn>(keys.Length);
        foreach (var key in keys)
        {
            AdminException.ThrowIf(!byKey.TryGetValue(key, out var col), ErrorCode.ExportColumnInvalid,
                new Dictionary<string, object?> { ["column"] = key });
            list.Add(col!);
        }
        return list;
    }

    private static object? GetUserCell(UserItem u, string key) => key switch
    {
        "Account" => u.Account,
        "Name" => u.Name,
        "Nickname" => u.Nickname,
        "Phone" => u.Phone,
        "Email" => u.Email,
        "Gender" => u.Gender,
        "OrgName" => u.OrgName,
        "PositionName" => u.PositionName,
        "DirectorName" => u.DirectorName,
        "Enabled" => u.Enabled ? "启用" : "停用",
        "IsSuperAdmin" => u.IsSuperAdmin ? "是" : "否",
        "CreateTime" => u.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"),
        _ => null,
    };
}
