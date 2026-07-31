namespace TenonAdmin.Core;

/// <summary>
/// 历史安全档访问器:读 <see cref="AdminSecurityOptions.Profile"/>。
/// <para><b>ADR 0006：</b>产品能力请读 <see cref="AdminSecurityOptions.IsTotpFeatureEnabled"/> /
/// <see cref="AdminSecurityOptions.IsCookieSessionEnabled"/> 等 helper。
/// 本接口仅服务预检、闲置账号 Job 等仍认 <c>Profile=Level3</c> 的过渡路径与测试桩。</para>
/// </summary>
public interface ISecurityProfileAccessor
{
    /// <summary>当前历史安全档</summary>
    SecurityProfile Profile { get; }

    /// <summary>是否配置了历史 Level3 总档(<see cref="AdminSecurityOptions.IsLegacyLevel3Profile"/>)</summary>
    bool IsLevel3 { get; }

    /// <summary>生产且未配历史 Level3(遗留语义;ADR 0006 后不再触发合规告警)</summary>
    bool IsProductionWithoutLevel3 { get; }
}
