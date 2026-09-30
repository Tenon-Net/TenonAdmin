using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 接入应用(<c>itg_app</c>)——以自身身份访问本系统业务数据的第三方应用,是独立授权和撤销访问权的对象(实现契约 §3)。
/// <para><b>刻意继承 <see cref="BaseEntity"/> 而非 <c>DataEntity</c></b>:应用管理由路由权限控制,开放请求的校验路径
/// 与后台任务都没有用户数据范围,<c>IOrgScoped</c> 过滤会让它们静默查不到行。软删由仓储释放唯一编码(追加 <c>_del_{id}</c>),
/// 故 <see cref="Code"/> 列宽比输入上限多留 32 位。</para>
/// </summary>
[SugarTable("itg_app", TableDescription = "接入应用")]
[SugarIndex("uk_itg_app_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
public class IntegrationApp : BaseEntity
{
    /// <summary>应用编码(唯一,调用记录与排障的稳定锚点;创建后不可修改)。</summary>
    [SugarColumn(Length = 96, ColumnDescription = "应用编码(唯一)")]
    public string Code { get; set; } = "";

    /// <summary>显示名称。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "应用名称")]
    public string Name { get; set; } = "";

    /// <summary>说明(对接方、用途等)。</summary>
    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "说明")]
    public string? Description { get; set; }

    /// <summary>是否启用;停用后该应用全部凭据立即无效。</summary>
    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;

    /// <summary>归属机构 Id:应用写入且未显式指定机构的业务行以它为数据范围锚点(<c>CreateOrgId</c>)。可空。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "归属机构 Id(数据范围锚点)")]
    public long? OwnerOrgId { get; set; }

    /// <summary>每分钟调用上限;null 用模块默认值,0 表示不限。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "每分钟调用上限(空=默认,0=不限)")]
    public int? RateLimitPerMinute { get; set; }

    /// <summary>
    /// 状态版本:启停、授权、范围、归属机构、限流变更时原子递增。授权/范围缓存以 (AppId, StateVersion) 为键,
    /// 版本随每次凭据校验从库里读出,旧缓存天然失效——多副本下无论内存缓存还是 Redis 行为一致。
    /// </summary>
    [SugarColumn(ColumnDescription = "状态版本(变更即递增)")]
    public long StateVersion { get; set; }
}
