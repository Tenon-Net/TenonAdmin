using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.TestHost;

/// <summary>
/// 示例机构隔离业务控制器——消费方业务控制器与内置控制器同管道(统一信封 / 鉴权 / 数据范围)。
/// 写操作挂 <c>[RolePermission]</c>(权限码 = 规范化路由);<c>GET mine</c> 挂 <c>[ActiveSession]</c> 锁仅登录也会写入数据范围。
/// </summary>
[ApiController]
[Route("api/v1/sample/doc")]
public class SampleDocController(
    ISampleDocService svc,
    SampleDocExportProfile exportProfile,
    IExcelWriter writer) : ControllerBase
{
    /// <summary>列出当前数据范围内可见的文档</summary>
    [HttpGet]
    [RolePermission]
    public async Task<Result<IReadOnlyList<SampleDoc>>> List() =>
        Result<IReadOnlyList<SampleDoc>>.Ok(await svc.ListAsync());

    /// <summary>
    /// 仅登录即可列当前范围内的文档——锁 <c>[ActiveSession]</c> 也会写入数据范围,
    /// 避免默认 Unrestricted 把全机构行漏给任意登录用户。
    /// </summary>
    [HttpGet("mine")]
    [ActiveSession]
    public async Task<Result<IReadOnlyList<SampleDoc>>> ListMine() =>
        Result<IReadOnlyList<SampleDoc>>.Ok(await svc.ListAsync());

    /// <summary>
    /// 导出当前数据范围内可见的文档(excel-ledger §5.2:xlsx 流,不进信封)。
    /// <para>
    /// 行集取自 <see cref="ISampleDocService.ListAsync"/> —— 与列表**同源**,所以导出的可见范围
    /// 由 <c>IOrgScoped</c> 全局过滤器决定,这里没有任何机构判断代码。这正是要演示的招牌能力,
    /// 也是消费方给自己的业务表接导出时该抄的形状(别另写一条查询,两条链一漂移就不一致了)。
    /// </para>
    /// </summary>
    [HttpGet("export")]
    [RolePermission]
    [OperationLog("导出示例文档")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
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

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "示例文档导出.xlsx");
    }

    /// <summary>新建文档</summary>
    [HttpPost]
    [RolePermission]
    public async Task<Result<long>> Create([FromBody] SampleDocInput input) =>
        Result<long>.Ok(await svc.CreateAsync(input.Title));

    /// <summary>改名(越权/不存在返回 data=false)</summary>
    [HttpPut("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Rename(long id, [FromBody] SampleDocInput input) =>
        Result<bool>.Ok(await svc.RenameAsync(id, input.Title));

    /// <summary>删除(越权/不存在返回 data=false)</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Delete(long id) =>
        Result<bool>.Ok(await svc.DeleteAsync(id));
}

/// <summary>示例业务入参</summary>
public record SampleDocInput(string Title);
