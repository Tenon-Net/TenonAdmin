using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// 接入应用与凭据管理(后台用户接口)。全部 <c>[RolePermission]</c>:权限码即规范化路由,超管放行,
/// 普通管理员需在角色授权里勾选;接入应用自身的凭据进不了这条通道,不能管理自己的授权。
/// <para>启用应用、发放与轮换凭据、调整凭据到期、修改授权与数据范围可能扩大应用的访问能力,另挂 <c>[RequireReauth]</c>
/// (仅在启用 TOTP 能力时强制);撤销、停用与删除是止损操作,不加再认证以免拖慢应急处置。返回秘密的响应一律 <c>no-store</c>。</para>
/// </summary>
[NonController]   // 默认发现跳过;由 AddTenonAdminIntegration 注册的特性提供者挂回(只引用本包不暴露端点)
[ApiController]
[Route("api/v1/integration/app")]
[Module("Integration")]
public class IntegrationAppController(
    IIntegrationAppService appService,
    IOpenAppCredentialService credentialService,
    IOpenAppAuthorizationService authorizationService) : ControllerBase
{
    /// <summary>分页查询接入应用</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<IntegrationAppListItem>>> Page([FromQuery] IntegrationAppPageInput input, CancellationToken cancellationToken) =>
        Result<PagedList<IntegrationAppListItem>>.Ok(await appService.PageAsync(input, cancellationToken));

    /// <summary>接入应用详情(含凭据视图,不含秘密)</summary>
    [HttpGet("{id}")]
    [RolePermission]
    public async Task<Result<IntegrationAppDetail>> Get(long id, CancellationToken cancellationToken) =>
        Result<IntegrationAppDetail>.Ok(await appService.GetAsync(id, cancellationToken));

    /// <summary>新增接入应用,返回 Id</summary>
    [HttpPost]
    [RolePermission]
    [OperationLog("新增接入应用")]
    public async Task<Result<long>> Add(IntegrationAppCreateInput input, CancellationToken cancellationToken) =>
        Result<long>.Ok(await appService.AddAsync(input, cancellationToken));

    /// <summary>修改接入应用(名称、说明、归属机构、限流)</summary>
    [HttpPut("{id}")]
    [RolePermission]
    [OperationLog("修改接入应用")]
    public async Task<Result<bool>> Update(long id, IntegrationAppUpdateInput input, CancellationToken cancellationToken)
    {
        await appService.UpdateAsync(id, input, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>启用接入应用</summary>
    [HttpPost("{id}/enable")]
    [RolePermission]
    [RequireReauth]
    [OperationLog("启用接入应用")]
    public async Task<Result<bool>> Enable(long id, CancellationToken cancellationToken)
    {
        await appService.SetEnabledAsync(id, true, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>停用接入应用:其全部凭据立即无效</summary>
    [HttpPost("{id}/disable")]
    [RolePermission]
    [OperationLog("停用接入应用")]
    public async Task<Result<bool>> Disable(long id, CancellationToken cancellationToken)
    {
        await appService.SetEnabledAsync(id, false, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除接入应用并撤销其全部凭据</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    [OperationLog("删除接入应用")]
    public async Task<Result<bool>> Delete(long id, CancellationToken cancellationToken)
    {
        await appService.DeleteAsync(id, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>列出应用凭据(不含秘密)</summary>
    [HttpGet("{id}/credentials")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<OpenAppCredentialView>>> Credentials(long id, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<OpenAppCredentialView>>.Ok(await credentialService.ListAsync(id, cancellationToken));

    /// <summary>发放凭据:完整凭据只在本次响应返回一次</summary>
    [HttpPost("{id}/credentials")]
    [RolePermission]
    [RequireReauth]
    [OperationLog("发放接入凭据")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<Result<OpenAppCredentialIssued>> CreateCredential(long id, OpenAppCredentialCreateInput input, CancellationToken cancellationToken) =>
        Result<OpenAppCredentialIssued>.Ok(await credentialService.CreateAsync(id, input, cancellationToken));

    /// <summary>轮换凭据:签发新凭据,旧凭据在并存窗口结束时过期</summary>
    [HttpPost("{id}/credentials/{credentialId}/rotate")]
    [RolePermission]
    [RequireReauth]
    [OperationLog("轮换接入凭据")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<Result<OpenAppCredentialIssued>> Rotate(long id, long credentialId, OpenAppCredentialRotateInput input, CancellationToken cancellationToken) =>
        Result<OpenAppCredentialIssued>.Ok(await credentialService.RotateAsync(id, credentialId, input, cancellationToken));

    /// <summary>撤销凭据(立即、永久)</summary>
    [HttpPost("{id}/credentials/{credentialId}/revoke")]
    [RolePermission]
    [OperationLog("撤销接入凭据")]
    public async Task<Result<bool>> Revoke(long id, long credentialId, CancellationToken cancellationToken)
    {
        await credentialService.RevokeAsync(id, credentialId, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除已撤销凭据(保留审计行)</summary>
    [HttpDelete("{id}/credentials/{credentialId}")]
    [RolePermission]
    [OperationLog("删除接入凭据")]
    public async Task<Result<bool>> DeleteCredential(long id, long credentialId, CancellationToken cancellationToken)
    {
        await credentialService.DeleteAsync(id, credentialId, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>应用授权(已授予的开放端点权限码,及已失效的码)</summary>
    [HttpGet("{id}/grants")]
    [RolePermission]
    public async Task<Result<OpenAppGrantView>> Grants(long id, CancellationToken cancellationToken) =>
        Result<OpenAppGrantView>.Ok(await authorizationService.GetGrantsAsync(id, cancellationToken));

    /// <summary>修改应用授权(整组替换)</summary>
    [HttpPut("{id}/grants")]
    [RolePermission]
    [RequireReauth]
    [OperationLog("修改接入应用授权")]
    public async Task<Result<bool>> SetGrants(long id, OpenAppGrantInput input, CancellationToken cancellationToken)
    {
        await authorizationService.SetGrantsAsync(id, input, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>应用数据范围绑定</summary>
    [HttpGet("{id}/scopes")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<OpenAppScopeBindingView>>> Scopes(long id, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<OpenAppScopeBindingView>>.Ok(await authorizationService.GetScopesAsync(id, cancellationToken));

    /// <summary>修改应用数据范围绑定(整组替换;全量须显式勾选)</summary>
    [HttpPut("{id}/scopes")]
    [RolePermission]
    [RequireReauth]
    [OperationLog("修改接入应用数据范围")]
    public async Task<Result<bool>> SetScopes(long id, OpenAppScopeInput input, CancellationToken cancellationToken)
    {
        await authorizationService.SetScopesAsync(id, input, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>调整凭据到期时间</summary>
    [HttpPut("{id}/credentials/{credentialId}/expiry")]
    [RolePermission]
    [RequireReauth]
    [OperationLog("调整接入凭据到期")]
    public async Task<Result<bool>> SetExpiry(long id, long credentialId, OpenAppCredentialExpiryInput input, CancellationToken cancellationToken)
    {
        await credentialService.SetExpiryAsync(id, credentialId, input, cancellationToken);
        return Result<bool>.Ok(true);
    }
}
