using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Integration;

namespace TenonAdmin.Samples.Integration;

/// <summary>
/// 自定义开放接口数据范围「合作方」:接入应用只能看到、写入绑定的合作方编码下的工单。
/// 合作方编码即完整的业务分区,因此显式放开内核机构维度(默认是拒绝,放开必须显式写出)。
/// </summary>
public sealed class PartnerScopePolicy(ISqlSugarClient db) : OpenApiDataScopePolicyBase
{
    public const string ScopeKey = "partner";

    public override string Key => ScopeKey;

    public override string Name => "工单对接方（示例）";

    /// <summary>候选值:已有工单里出现过的合作方编码(管理员也可手工填写新编码)。</summary>
    public override async Task<IReadOnlyList<OpenApiScopeOption>> ListOptionsAsync(string? keyword, CancellationToken cancellationToken = default)
    {
        var codes = await db.Queryable<PartnerTicket>()
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), t => t.PartnerCode.Contains(keyword!))
            .Select(t => t.PartnerCode).Distinct().Take(200).ToListAsync(cancellationToken);
        return codes.Order(StringComparer.Ordinal).Select(c => new OpenApiScopeOption(c, c)).ToList();
    }

    protected override DataScopeResult ResolveKernelScope(OpenAppDataScope scope) => DataScopeResult.Unrestricted;
}
