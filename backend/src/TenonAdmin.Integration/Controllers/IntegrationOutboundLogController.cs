using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// 出站调用记录与目标清单(后台用户接口,<c>[RolePermission]</c>)。记录不含正文与请求头;目标清单只回答「是否取得到秘密」,永不回显秘密。
/// </summary>
[NonController]   // 默认发现跳过;由 AddTenonAdminIntegration 注册的特性提供者挂回(只引用本包不暴露端点)
[ApiController]
[Route("api/v1/integration/outbound-log")]
[Module("Integration")]
public class IntegrationOutboundLogController(
    IOutboundLogService logs,
    IOutboundTargetRegistry targets,
    IOutboundCredentialProvider credentials,
    ILogger<IntegrationOutboundLogController> logger) : ControllerBase
{
    /// <summary>分页查询出站调用记录(新在前)</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<OutboundLogView>>> Page([FromQuery] OutboundLogPageInput input, CancellationToken cancellationToken) =>
        Result<PagedList<OutboundLogView>>.Ok(await logs.PageAsync(input, cancellationToken));

    /// <summary>已配置的出站目标(不含秘密)</summary>
    [HttpGet("targets")]
    [RolePermission]
    public async Task<Result<List<OutboundTargetView>>> Targets(CancellationToken cancellationToken)
    {
        var views = new List<OutboundTargetView>();
        foreach (var target in targets.Targets)
            views.Add(new OutboundTargetView
            {
                Name = target.Name,
                BaseUrl = target.BaseUri.AbsoluteUri,
                TimeoutSeconds = target.Timeout.TotalSeconds,
                AuthType = target.AuthType,
                AuthHeaderName = target.AuthHeaderName,
                TrustedCidrs = target.TrustedCidrs,
                // 认证方式为 None 的目标也可能由适配器自行签名,仍如实回答「取得到秘密与否」
                HasCredential = await HasCredentialAsync(target, cancellationToken),
            });
        return Result<List<OutboundTargetView>>.Ok(views);
    }

    private async Task<bool> HasCredentialAsync(OutboundTarget target, CancellationToken cancellationToken)
    {
        try
        {
            return await credentials.GetAsync(target, cancellationToken) is { Secret.Length: > 0 };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 密钥服务不可用不应拖垮整张清单;只影响该目标的「有无凭据」显示
            logger.LogWarning("读取出站目标凭据状态失败。Target={Target} ExceptionType={ExceptionType}", target.Name, ex.GetType().Name);
            return false;
        }
    }
}
