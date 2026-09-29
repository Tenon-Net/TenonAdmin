using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 接入凭据(<c>itg_app_credential</c>)——每个接入应用可有多把 API Key(轮换期间新旧并存)。
/// <para><b>只存摘要</b>:秘密原文仅在创建/轮换响应里出现一次,库里只有 <see cref="SecretHash"/>(SHA-256)。
/// 校验按唯一的公开标识 <see cref="KeyId"/> 定位行后比较摘要,不遍历全部密钥。</para>
/// <para>继承 <see cref="BaseEntity"/>:撤销后可软删,行与轮换来源关系仍保留作审计。
/// 业务时间列一律 UTC(<c>*Utc</c> 后缀);基类 <c>CreateTime</c> 是 AOP 填的本地时间,两者不得比较。</para>
/// </summary>
[SugarTable("itg_app_credential", TableDescription = "接入凭据")]
[SugarIndex("uk_itg_app_credential_key", nameof(KeyId), OrderByType.Asc, IsUnique = true)]
[SugarIndex("idx_itg_app_credential_app", nameof(AppId), OrderByType.Asc)]
public class IntegrationAppCredential : BaseEntity
{
    /// <summary>所属接入应用 Id。</summary>
    [SugarColumn(ColumnDescription = "所属接入应用 Id")]
    public long AppId { get; set; }

    /// <summary>公开凭据标识(非秘密,可展示;唯一索引)。</summary>
    [SugarColumn(Length = 32, ColumnDescription = "公开凭据标识(唯一)")]
    public string KeyId { get; set; } = "";

    /// <summary>秘密摘要(SHA-256 小写 hex)。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "秘密摘要(SHA-256)")]
    public string SecretHash { get; set; } = "";

    /// <summary>凭据名称(区分用途,如「生产」「测试」)。</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "凭据名称")]
    public string? Name { get; set; }

    /// <summary>到期时刻(UTC);null 表示不过期。到期时刻本身即视为已过期。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "到期时刻(UTC,空=不过期)")]
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>撤销时刻(UTC);非空即永久无效。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "撤销时刻(UTC)")]
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>撤销操作人用户 Id。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "撤销操作人")]
    public long? RevokedBy { get; set; }

    /// <summary>由哪把凭据轮换而来(轮换链)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "轮换来源凭据 Id")]
    public long? RotatedFromId { get; set; }

    /// <summary>最近使用时刻(UTC,节流回写,仅供参考)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "最近使用时刻(UTC)")]
    public DateTime? LastUsedAtUtc { get; set; }
}
