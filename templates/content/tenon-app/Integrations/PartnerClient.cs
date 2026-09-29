using TenonAdmin.Integration;

namespace TenonApp.Integrations;

/// <summary>对方返回的合作方信息。【按对方协议填写】字段名与对方响应体一致。</summary>
public sealed record PartnerInfo(string Code, string Name, string? Level);

/// <summary>
/// 普通第三方调用示例(同步、在事务外):按编码查询对方的合作方信息。
/// 地址安全、超时、凭据注入、<c>X-Request-Id</c> 与出站调用记录由 <see cref="OutboundAdapterBase"/> 完成,不会自动重试;
/// 这里只写对方的路径和结果含义。示例按内核仓库里的本地可控第三方(IntegrationMockPartner)的协议编写。
/// </summary>
public class PartnerClient(OutboundAdapterServices services) : OutboundAdapterBase(services)
{
    /// <summary>出站目标名,对应配置 <c>TenonAdmin:Integration:Outbound:Targets:partner</c>。</summary>
    protected override string TargetName => "partner";

    /// <summary>按编码查询;对方明确「不存在」返回 null,其他失败以 4903x 业务码抛出(前端按码提示)。</summary>
    public virtual async Task<PartnerInfo?> FindAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        // 【按对方协议填写】路径相对目标的 BaseUrl,不能写成绝对地址或跳出 BaseUrl
        var response = await GetAsync($"partners/{Uri.EscapeDataString(code)}", new OutboundCallOptions { Operation = "partner.get" }, cancellationToken);
        if (response.StatusCode == 404) return null;
        response.EnsureSucceeded(allowAccepted: false);
        return ReadJson<PartnerInfo>(response);
    }
}
