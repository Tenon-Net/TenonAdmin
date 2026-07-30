namespace TenonAdmin.Core;

/// <summary>
/// 首次启用 Level3 时的一次性迁移:为存量启用用户初始化 <c>LastSuccessfulLoginAt</c>(启用时刻),
/// 避免历史缺失导致闲置规则批量停用;幂等(SysConfig 旗标)。
/// </summary>
public interface ILevel3EnableMigrator
{
    /// <summary>若尚未迁移则执行并打旗标;已完成则空操作。返回迁移用户数(已完成时 0)。</summary>
    Task<int> EnsureMigratedAsync(CancellationToken cancellationToken = default);
}
