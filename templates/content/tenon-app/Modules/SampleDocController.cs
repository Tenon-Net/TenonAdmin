using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonApp.Modules;

/// <summary>
/// 示例机构隔离业务控制器——消费方业务控制器与内置控制器同管道(统一信封 / 鉴权 / 数据范围)。
/// 全部 <c>[RolePermission]</c>:超管放行,普通用户需被授予对应路由权限码(权限码 = 规范化路由,无魔法串)。
/// </summary>
[ApiController]
[Route("api/v1/sample/doc")]
public class SampleDocController(ISampleDocService svc) : ControllerBase
{
    /// <summary>列出当前数据范围内可见的文档</summary>
    [HttpGet]
    [RolePermission]
    public async Task<Result<IReadOnlyList<SampleDoc>>> List() =>
        Result<IReadOnlyList<SampleDoc>>.Ok(await svc.ListAsync());

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
