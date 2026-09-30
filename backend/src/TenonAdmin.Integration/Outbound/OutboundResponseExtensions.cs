namespace TenonAdmin.Integration;

/// <summary>出站结果的便捷方法。</summary>
public static class OutboundResponseExtensions
{
    /// <summary>
    /// 未完成即抛业务异常(4903x),便于后台接口把第三方失败以本地化提示返回界面;args 含 <c>target</c>、<c>reason</c>、<c>callId</c>(不含秘密与响应正文)。
    /// <para><paramref name="allowAccepted"/> 为真(默认)时远端受理也视为通过——注意受理只表示对方接下了,最终结果仍待确认。</para>
    /// </summary>
    public static OutboundResponse EnsureSucceeded(this OutboundResponse response, bool allowAccepted = true)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Outcome == OutboundOutcome.Succeeded || (allowAccepted && response.Outcome == OutboundOutcome.Accepted))
            return response;

        var code = response.Outcome switch
        {
            OutboundOutcome.NotSent when response.Reason == "target_unknown" => IntegrationErrorCode.OutboundTargetNotConfigured,
            OutboundOutcome.NotSent => IntegrationErrorCode.OutboundNotSent,
            OutboundOutcome.AuthenticationFailed or OutboundOutcome.Rejected => IntegrationErrorCode.OutboundRejected,
            OutboundOutcome.Accepted => IntegrationErrorCode.OutboundAccepted,
            _ => IntegrationErrorCode.OutboundResultUnknown,
        };
        throw IntegrationErrorCode.Exception(code, new Dictionary<string, object?>
        {
            ["target"] = response.Target,
            ["reason"] = response.Reason,
            ["callId"] = response.CallId,
        }, $"第三方调用未完成:{response.Target} {response.Outcome} {response.Reason}");
    }
}
