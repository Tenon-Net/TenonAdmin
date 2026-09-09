namespace TenonAdmin.Workflow;

/// <summary>AI Provider 入口共享的最小请求校验；保持 internal，避免把未经验证的请求作为公共扩展面。</summary>
internal static class AiDecisionProviderRequestValidator
{
    internal static void Validate(AiDecisionProviderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NodeId);

        if (request.InstanceId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.InstanceId));
        }

        if (request.TokenId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.TokenId));
        }

        if (request.DefinitionVersionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.DefinitionVersionId));
        }

        if (request.StarterUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.StarterUserId));
        }

        if (request.Attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Attempt));
        }

        if (request.DeadlineAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("DeadlineAtUtc 必须是 UTC。", nameof(request.DeadlineAtUtc));
        }

        AiDecisionSafeInputProjector.Validate(request);
    }
}
