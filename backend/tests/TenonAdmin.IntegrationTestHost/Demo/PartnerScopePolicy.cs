using TenonAdmin.Core;
using TenonAdmin.Integration;

namespace TenonAdmin.IntegrationTestHost;

/// <summary>
/// 消费者自定义范围:按合作方编码划分工单。合作方编码即完整业务分区,因此显式放开内核机构维度(全量须显式声明)。
/// </summary>
public sealed class PartnerScopePolicy : OpenApiDataScopePolicyBase
{
    public const string ScopeKey = "partner";

    public override string Key => ScopeKey;

    public override string Name => "合作方";

    protected override DataScopeResult ResolveKernelScope(OpenAppDataScope scope) => DataScopeResult.Unrestricted;
}
