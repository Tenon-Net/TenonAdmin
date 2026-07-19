using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 缓存管理端点(设计 §4 运维)——定向失效动作,<b>只清不看</b>:缓存值含明文 OTP、键内嵌 PII,
/// 故无键浏览 / 取值端点。全部 <c>[RolePermission]</c> 授权 + <c>[OperationLog]</c> 留痕;
/// 经 <c>Api:DisabledModules=["Cache"]</c> 可整体关闭。
/// </summary>
[ApiController]
[Route("api/v1/sys/cache")]
[Module("Cache")]
public class CacheController(ICacheAdminService cacheAdmin) : ControllerBase
{
    /// <summary>清全部用户的权限 / 数据范围缓存(返回被清理的用户数)</summary>
    [HttpPost("flush-auth")]
    [RolePermission]
    [OperationLog("清缓存-授权")]
    public async Task<Result<int>> FlushAuth(CancellationToken cancellationToken) =>
        Result<int>.Ok(await cacheAdmin.FlushAuthAsync(cancellationToken));

    /// <summary>清全部字典项缓存(返回被清理的字典类型数)</summary>
    [HttpPost("flush-dict")]
    [RolePermission]
    [OperationLog("清缓存-字典")]
    public async Task<Result<int>> FlushDict(CancellationToken cancellationToken) =>
        Result<int>.Ok(await cacheAdmin.FlushDictAsync(cancellationToken));

    /// <summary>清全部系统配置缓存(返回被清理的配置项数)</summary>
    [HttpPost("flush-config")]
    [RolePermission]
    [OperationLog("清缓存-配置")]
    public async Task<Result<int>> FlushConfig(CancellationToken cancellationToken) =>
        Result<int>.Ok(await cacheAdmin.FlushConfigAsync(cancellationToken));

    /// <summary>递增门户代际,令所有用户的门户菜单 / 模块缓存整体失效(返回新代际值)</summary>
    [HttpPost("rebuild-portal")]
    [RolePermission]
    [OperationLog("清缓存-门户菜单")]
    public async Task<Result<long>> RebuildPortal(CancellationToken cancellationToken) =>
        Result<long>.Ok(await cacheAdmin.RebuildPortalAsync(cancellationToken));
}
