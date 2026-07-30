using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// Level3 部署期一次性授权(InitGrant / EmergencyGrant)的持久 first-seen 与消费状态。
/// 不放在缓存:Redis 清空不得恢复已消费/已过期授权。明文永不入库,只存哈希。
/// AbsoluteNotAfterUtc 在 first-seen 时固化,原子消费条件会比对当前时间。
/// </summary>
[SugarTable("sys_level3_deploy_grant", TableDescription = "Level3 部署授权状态")]
[SugarIndex("uk_sys_level3_deploy_grant", nameof(Kind), OrderByType.Asc, nameof(GrantHash), OrderByType.Asc, IsUnique = true)]
public class SysLevel3DeployGrant : BaseEntity
{
    /// <summary>授权种类:<c>init</c> | <c>emergency</c></summary>
    [SugarColumn(Length = 32, ColumnDescription = "授权种类")]
    public string Kind { get; set; } = "";

    /// <summary>配置明文的哈希(SecretHash)</summary>
    [SugarColumn(Length = 128, ColumnDescription = "授权哈希")]
    public string GrantHash { get; set; } = "";

    /// <summary>首次被系统观测/使用的时刻(TTL 起算点)</summary>
    [SugarColumn(ColumnDescription = "首次观测时刻")]
    public DateTime FirstSeenAt { get; set; }

    /// <summary>
    /// 绝对到期时刻(UTC, first-seen 时从配置固化)。
    /// 原子消费 WHERE 要求本列 &gt; 提交时的当前 UTC。
    /// </summary>
    [SugarColumn(ColumnDescription = "绝对到期 UTC")]
    public DateTime AbsoluteNotAfterUtc { get; set; }

    /// <summary>消费时刻;非 null 后不可再用于初始化/紧急恢复</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "消费时刻")]
    public DateTime? ConsumedAt { get; set; }
}

/// <summary>部署授权种类常量。</summary>
public static class Level3DeployGrantKinds
{
    public const string Init = "init";
    public const string Emergency = "emergency";
}
