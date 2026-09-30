using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonApp.Modules;

namespace TenonApp.Integrations;

/// <summary>
/// 后台接口示例(用户令牌 + 路由权限,与 <see cref="SampleDocController"/> 同管道):
/// 一个演示普通第三方调用,一个演示事务内可靠投递。
/// </summary>
[ApiController]
[Route("api/v1/sample/doc-sync")]
public class SampleDocSyncController(ISampleDocSyncService sync, PartnerClient partners) : ControllerBase
{
    /// <summary>新建文档,并在同一事务里登记「同步到对方」的投递</summary>
    [HttpPost]
    [RolePermission]
    public async Task<Result<SampleDocSynced>> Create([FromBody] SampleDocInput input, CancellationToken cancellationToken) =>
        Result<SampleDocSynced>.Ok(await sync.CreateAndSyncAsync(input.Title, cancellationToken));

    /// <summary>普通调用:按编码查询对方的合作方信息(对方没有时 data 为 null)</summary>
    [HttpGet("partner/{code}")]
    [RolePermission]
    public async Task<Result<PartnerInfo?>> Partner(string code, CancellationToken cancellationToken) =>
        Result<PartnerInfo?>.Ok(await partners.FindAsync(code, cancellationToken));
}
