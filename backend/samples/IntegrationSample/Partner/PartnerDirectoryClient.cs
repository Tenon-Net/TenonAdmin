using TenonAdmin.Integration;

namespace TenonAdmin.Samples.Integration;

/// <summary>合作方目录中的一条记录(对方协议)。</summary>
public sealed record PartnerInfo(string Code, string Name, string? Level);

/// <summary>
/// 普通第三方调用示例:查询合作方目录。消费者只写对方的路径与结果含义——地址安全、超时、凭据注入、
/// 调用记录全部由 <see cref="OutboundAdapterBase"/> 复用框架完成。
/// </summary>
public class PartnerDirectoryClient(OutboundAdapterServices services) : OutboundAdapterBase(services)
{
    protected override string TargetName => "partner";

    /// <summary>按编码查合作方;对方明确「不存在」返回 null,其他失败以 4903x 业务异常抛出(界面按码本地化提示)。</summary>
    public virtual async Task<PartnerInfo?> FindAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var response = await GetAsync($"partners/{Uri.EscapeDataString(code)}", new OutboundCallOptions { Operation = "partner.get" }, cancellationToken);
        if (response.StatusCode == 404) return null;
        response.EnsureSucceeded(allowAccepted: false);
        return ReadJson<PartnerInfo>(response);
    }

    /// <summary>合作方必须存在。</summary>
    public virtual async Task EnsurePartnerExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(code, cancellationToken) is null) throw SampleErrors.PartnerNotFoundException(code);
    }
}
