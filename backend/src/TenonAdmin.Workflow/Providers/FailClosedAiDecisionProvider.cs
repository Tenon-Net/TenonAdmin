namespace TenonAdmin.Workflow;

/// <summary>
/// 尚未配置真实 Adapter 时的默认 AI Provider。它不保留请求、不会发起网络调用，也不产生任何诊断正文；
/// 以类型化失败让 shadow-only handler 走人工兜底。
/// </summary>
public class FailClosedAiDecisionProvider : IAiDecisionProvider
{
    private const string ProviderName = "fail-closed";

    public virtual Task<AiDecisionProviderResult> ProposeAsync(
        AiDecisionProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AiDecisionProviderRequestValidator.Validate(request);
        return Task.FromResult(AiDecisionProviderResult.Failed(null, ProviderName));
    }
}
