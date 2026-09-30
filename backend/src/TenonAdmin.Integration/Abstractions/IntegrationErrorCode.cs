using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// 第三方接入业务错误码(49xxx)。卫星包自管常量,强转 <see cref="ErrorCode"/> 抛 <see cref="AdminException"/>;
/// msgKey 回落为 <c>error.code.&lt;数字&gt;</c>,双前端按该键各自翻译。开放接口调用方读 <c>code</c> 与兜底 <c>message</c>。
/// <para>分段:4900x 开放接口认证/授权/范围;4901x–4902x 接入应用、凭据、授权与范围管理;4903x 普通出站调用;4904x 可靠投递。</para>
/// </summary>
public static class IntegrationErrorCode
{
    /// <summary>接入凭据无效:缺失、格式错误、不存在、秘密不符、已撤销、已过期或应用已停用(对外不区分原因)。</summary>
    public const int CredentialInvalid = 49001;

    /// <summary>接入应用未被授予当前开放端点(默认拒绝)。</summary>
    public const int PermissionDenied = 49002;

    /// <summary>接入应用未绑定当前端点声明的数据范围(未绑定即拒绝,不退化为全量)。</summary>
    public const int ScopeNotBound = 49003;

    /// <summary>目标数据不在接入应用的数据范围内。</summary>
    public const int DataOutOfScope = 49004;

    /// <summary>接入应用调用超过每分钟上限;args.retryAfterSeconds 为建议重试间隔。</summary>
    public const int RateLimited = 49005;

    /// <summary>同一来源认证失败过多,暂时拒绝;args.retryAfterSeconds 为建议重试间隔。</summary>
    public const int AuthenticationThrottled = 49006;

    /// <summary>请求参数不合法(模型校验失败等);args.errors 为字段错误。</summary>
    public const int RequestInvalid = 49007;

    /// <summary>接入应用不存在(或已删除)。</summary>
    public const int AppNotFound = 49010;

    /// <summary>接入应用编码已存在。</summary>
    public const int AppCodeExists = 49011;

    /// <summary>接入应用输入不合法;args.field 指明字段。</summary>
    public const int AppInputInvalid = 49012;

    /// <summary>归属机构不存在。</summary>
    public const int OwnerOrgNotFound = 49013;

    /// <summary>凭据不存在或不属于该应用。</summary>
    public const int CredentialNotFound = 49014;

    /// <summary>有效凭据数量已达上限;args.max 为上限。</summary>
    public const int CredentialLimitExceeded = 49015;

    /// <summary>凭据已撤销或已过期,不能执行本操作。</summary>
    public const int CredentialNotActive = 49016;

    /// <summary>到期时间不合法(早于当前或超过最长有效期);args.maxDays 为最长有效期。</summary>
    public const int CredentialExpiryInvalid = 49017;

    /// <summary>轮换并存窗口不合法;args.max 为上限(小时)。</summary>
    public const int RotationOverlapInvalid = 49018;

    /// <summary>授权的权限码不对应任何现存开放端点;args.permission 为该码。</summary>
    public const int GrantPermissionUnknown = 49019;

    /// <summary>范围策略不存在(或为保留键 none);args.scope 为该键。</summary>
    public const int ScopePolicyUnknown = 49020;

    /// <summary>范围绑定值不合法(为空、过多、过长或不存在);args.scope 为策略键。</summary>
    public const int ScopeValueInvalid = 49021;

    /// <summary>开放接口文档版本不存在;args.version 为请求的版本。</summary>
    public const int OpenApiVersionNotFound = 49022;

    /// <summary>凭据尚未撤销,不能删除。</summary>
    public const int CredentialNotRevoked = 49023;

    /// <summary>出站目标未配置;args.target 为目标名。</summary>
    public const int OutboundTargetNotConfigured = 49030;

    /// <summary>第三方调用未发出(连接、DNS、目标被拦截、凭据缺失或对方繁忙),可稍后重试;args.target、args.reason。</summary>
    public const int OutboundNotSent = 49031;

    /// <summary>第三方拒绝了请求(认证失败或业务拒绝);args.target、args.reason。</summary>
    public const int OutboundRejected = 49032;

    /// <summary>第三方调用结果未知(超时、连接中断或对方出错),对方可能已执行,须核实后再处理;args.target、args.callId。</summary>
    public const int OutboundResultUnknown = 49033;

    /// <summary>第三方已受理、结果待确认;args.target、args.callId。</summary>
    public const int OutboundAccepted = 49034;

    /// <summary>投递记录不存在。</summary>
    public const int DeliveryNotFound = 49040;

    /// <summary>入队时没有活动事务(无法与业务写入原子);确需独立入队请设置 Standalone。</summary>
    public const int DeliveryRequiresTransaction = 49041;

    /// <summary>同一投递标识已用于不同的适配器、操作或载荷;args.deliveryKey。</summary>
    public const int DeliveryKeyConflict = 49042;

    /// <summary>投递适配器未注册;args.adapter。</summary>
    public const int DeliveryAdapterUnknown = 49043;

    /// <summary>入队请求不合法;args.field 指明字段。</summary>
    public const int DeliveryRequestInvalid = 49044;

    /// <summary>当前状态不允许该人工操作(服务端判定);args.action、args.status。</summary>
    public const int DeliveryActionNotAllowed = 49045;

    /// <summary>投递记录已被投递器或他人改动(页面过期或并发操作),请刷新后再操作。</summary>
    public const int DeliveryConcurrentlyModified = 49046;

    /// <summary>确认类操作须填写处理说明,或说明超长;args.max 为上限。</summary>
    public const int DeliveryNoteRequired = 49047;

    /// <summary>以错误码构造业务异常(可带 i18n 插值参数与兜底文案)。</summary>
    public static AdminException Exception(int code, IReadOnlyDictionary<string, object?>? args = null, string? fallbackMessage = null) =>
        new((ErrorCode)code, args, fallbackMessage);

    /// <summary>条件成立即抛出对应错误码的业务异常。</summary>
    public static void ThrowIf(bool condition, int code, IReadOnlyDictionary<string, object?>? args = null, string? fallbackMessage = null)
    {
        if (condition) throw Exception(code, args, fallbackMessage);
    }
}
