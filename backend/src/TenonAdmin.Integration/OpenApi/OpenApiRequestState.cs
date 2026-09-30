using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace TenonAdmin.Integration;

/// <summary>
/// 一次开放请求在管线各段之间传递的状态(<c>HttpContext.Items</c> 载体):认证结果、授权结果、生效范围、计时与追踪标识。
/// 认证处理器最先创建它;授权过滤器与调用记录读写它;<see cref="HttpContextOpenAppContext"/> 对业务代码只读暴露。
/// </summary>
internal sealed partial class OpenApiRequestState
{
    private static readonly object ItemKey = new();

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex ClientRequestIdPattern();

    public long StartTimestamp { get; init; }
    public string TraceId { get; init; } = "";
    public string? ClientRequestId { get; init; }

    public OpenAppCredentialValidation? Validation { get; set; }
    public OpenAppIdentity? Identity { get; set; }
    public OpenApiEndpointInfo? Endpoint { get; set; }
    public OpenAppDataScope? Scope { get; set; }

    /// <summary>授权过滤器全部通过后置真;<see cref="IOpenAppContext.IsOpenAppRequest"/> 只认它。</summary>
    public bool Authorized { get; set; }

    /// <summary>本请求是否已写过调用记录(拒绝路径与完成路径只记一次)。</summary>
    public bool Recorded { get; set; }

    /// <summary>认证阶段因来源失败过多被拒时的建议重试间隔(秒);null 表示未被限流。</summary>
    public int? AuthThrottledRetryAfter { get; set; }

    /// <summary>凭据出现在查询串里(不接受,也不落库原文)。</summary>
    public bool CredentialInUrl { get; set; }

    /// <summary>被开放异常出口转成 500 信封的未处理异常类型名(调用记录用)。</summary>
    public string? ExceptionType { get; set; }

    /// <summary>取或建本请求的状态;首次创建时记下起始时刻与追踪标识。</summary>
    public static OpenApiRequestState GetOrCreate(HttpContext http, TimeProvider time)
    {
        if (http.Items.TryGetValue(ItemKey, out var existing) && existing is OpenApiRequestState state) return state;

        var created = new OpenApiRequestState
        {
            StartTimestamp = time.GetTimestamp(),
            TraceId = ResolveTraceId(http),
            ClientRequestId = ReadClientRequestId(http),
        };
        http.Items[ItemKey] = created;
        return created;
    }

    /// <summary>只读取,不创建。</summary>
    public static OpenApiRequestState? Peek(HttpContext? http) =>
        http is not null && http.Items.TryGetValue(ItemKey, out var existing) ? existing as OpenApiRequestState : null;

    /// <summary>
    /// 追踪标识 = ASP.NET 请求标识(<see cref="HttpContext.TraceIdentifier"/>),与内核异常日志记录的是同一个值:
    /// 对方报来的标识能同时查到开放调用记录与异常堆栈。
    /// </summary>
    private static string ResolveTraceId(HttpContext http) => http.TraceIdentifier;

    /// <summary>调用方 <c>X-Request-Id</c>:只接受短而安全的字符集,否则丢弃(不回显、不入库)。</summary>
    private static string? ReadClientRequestId(HttpContext http)
    {
        var values = http.Request.Headers[OpenApiHeaders.RequestId];
        if (values.Count != 1) return null;
        var value = values[0];
        return value is not null && ClientRequestIdPattern().IsMatch(value) ? value : null;
    }
}

/// <summary>开放接口使用的请求/响应头名。</summary>
public static class OpenApiHeaders
{
    /// <summary>响应头:本次调用的追踪标识(与开放调用记录、内核异常日志的 TraceId 一致)。</summary>
    public const string TraceId = "X-Trace-Id";

    /// <summary>请求/响应头:调用方自带的请求标识(≤64,字符集 <c>[A-Za-z0-9._:-]</c>),原样回显并写入调用记录。</summary>
    public const string RequestId = "X-Request-Id";
}
