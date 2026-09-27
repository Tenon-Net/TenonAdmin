namespace TenonAdmin.Workflow;

/// <summary>
/// 工作流全局配置(对应 <c>TenonAdmin:Workflow</c>)。
/// 节点 / 流程级配置可覆盖此处默认值(空审批人策略等,见设计方案)。
/// </summary>
public sealed class WorkflowOptions
{
    /// <summary>自动节点未配置时的内置安全总尝试次数(含首次执行)。</summary>
    public const int DefaultMaxAttempts = 3;

    /// <summary>自动节点总尝试次数允许的最小值。</summary>
    public const int MinMaxAttempts = 1;

    /// <summary>自动节点总尝试次数允许的最大值，防止配置制造近似无限重试。</summary>
    public const int MaxMaxAttempts = 100;

    /// <summary>节点执行 worker 每拍最多检视的 execution 数。</summary>
    public const int DefaultNodeExecutionScanBatchSize = 20;

    /// <summary>节点执行 worker 扫描批量允许的最大值。</summary>
    public const int MaxNodeExecutionScanBatchSize = 1000;

    /// <summary>节点执行租约默认时长(秒)。</summary>
    public const int DefaultNodeExecutionLeaseSeconds = 300;

    /// <summary>节点执行租约允许的最大时长(秒)。</summary>
    public const int MaxNodeExecutionLeaseSeconds = 3600;

    /// <summary>outbox worker 每拍最多检视的消息数。</summary>
    public const int DefaultOutboxScanBatchSize = 20;

    /// <summary>outbox worker 扫描批量允许的最大值。</summary>
    public const int MaxOutboxScanBatchSize = 1000;

    /// <summary>outbox 领取可见性超时默认时长(秒)。</summary>
    public const int DefaultOutboxVisibilityTimeoutSeconds = 60;

    /// <summary>outbox 可见性超时允许的最大时长(秒)。</summary>
    public const int MaxOutboxVisibilityTimeoutSeconds = 3600;

    /// <summary>outbox 投递总尝试次数默认值(含首次投递)。</summary>
    public const int DefaultOutboxMaxAttempts = DefaultMaxAttempts;

    /// <summary>
    /// 自动节点全局默认总尝试次数(含首次执行);节点 <c>props.maxAttempts</c> 可覆盖。
    /// 对应 <c>TenonAdmin:Workflow:MaxAttempts</c>，绑定期必须在
    /// <see cref="MinMaxAttempts"/> 与 <see cref="MaxMaxAttempts"/> 之间。
    /// </summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>
    /// 节点执行 worker 每拍最多扫描的 execution 数，对应
    /// <c>TenonAdmin:Workflow:NodeExecutionScanBatchSize</c>。
    /// </summary>
    public int NodeExecutionScanBatchSize { get; set; } = DefaultNodeExecutionScanBatchSize;

    /// <summary>
    /// 节点 execution 单次领取租约时长(秒)，对应
    /// <c>TenonAdmin:Workflow:NodeExecutionLeaseSeconds</c>。
    /// </summary>
    public int NodeExecutionLeaseSeconds { get; set; } = DefaultNodeExecutionLeaseSeconds;

    /// <summary>
    /// outbox worker 每拍最多扫描的消息数，对应
    /// <c>TenonAdmin:Workflow:OutboxScanBatchSize</c>。
    /// </summary>
    public int OutboxScanBatchSize { get; set; } = DefaultOutboxScanBatchSize;

    /// <summary>
    /// outbox 单次领取的可见性超时(秒)，对应
    /// <c>TenonAdmin:Workflow:OutboxVisibilityTimeoutSeconds</c>。
    /// <c>AvailableAtUtc</c> 被推到 <c>now + 该值</c>，超时未回写即可被其他 worker 重领。
    /// </summary>
    public int OutboxVisibilityTimeoutSeconds { get; set; } = DefaultOutboxVisibilityTimeoutSeconds;

    /// <summary>
    /// outbox 投递总尝试次数(含首次)，对应
    /// <c>TenonAdmin:Workflow:OutboxMaxAttempts</c>。耗尽后进入 <c>Failed</c> 死信。
    /// </summary>
    public int OutboxMaxAttempts { get; set; } = DefaultOutboxMaxAttempts;

    /// <summary>
    /// AI Decision 的服务端配置，对应 <c>TenonAdmin:Workflow:AiDecision</c>。V0 固定 shadow-only，
    /// 不暴露任何自动放行开关。
    /// </summary>
    public AiDecisionOptions AiDecision { get; set; } = new();

    /// <summary>
    /// 空审批人全局默认策略:<c>autoPass</c>(自动通过,出厂默认) /
    /// <c>transfer</c>(转指定人) / <c>block</c>(卡住并通知管理员)。
    /// </summary>
    public string Nobody { get; set; } = "autoPass";

    /// <summary>
    /// <see cref="WfTimeoutJob"/> 单次扫描的**处理**上限。每条要开一个引擎事务(读实例/版本 + CAS +
    /// 写 2–4 行),故比纯删除型任务保守;没处理完的下一拍继续,扫描按 <c>DueTime</c> 升序,最久的先处理。
    /// <para><b>这是「处理」预算而不是「取回行数」上限。</b>到期窗口里天然混着这一拍推不动的行——被防刷
    /// 间隔挡下的提醒(提醒不清 <c>DueTime</c>,那是「不改状态」契约的推论)最典型。若把它们也算进预算,
    /// 升序 + 永不消费 = 队头永久堵塞,更新的自动通过/拒绝/转办永远排不进队,而 Job 照样返回 Success。
    /// 故扫描按 <c>(DueTime, Id)</c> 游标翻页,只有真推动了的行才扣预算;翻页天花板见
    /// <c>WfTimeoutJob.MaxScanRounds</c>。</para>
    /// </summary>
    public int TimeoutScanBatchSize { get; set; } = 200;

    /// <summary>
    /// <see cref="WfTimeoutAction.Remind"/> 的最小提醒间隔(小时)。
    /// <c>0</c> = 跟随节点自己的 <see cref="WfTimeout.Hours"/>(下限 1 小时)——「配 24 小时超时的节点每
    /// 24 小时催一次」,不引入第二个要理解的旋钮。契约只写了「可重复触发」没写节奏,按字面实现的话
    /// 一件逾期三天的待办在 5 分钟一拍下会被提醒 864 次。
    /// <para>需要别的节奏(如只提醒一次)覆写 <see cref="WfTimeoutJob.ShouldRemindAsync"/>——但**光覆写
    /// 不生效**:调度器按 <c>sys_job.HandlerName</c> 解析处理器,种子写死的是基类全名,子类必须同时改那一行
    /// (后台可直接改)或自己覆写 <c>Name</c> 并前置注册。完整说明见 <see cref="WfTimeoutJob"/> 类级注释。</para>
    /// </summary>
    public int TimeoutRemindMinIntervalHours { get; set; }

    internal static bool IsValidMaxAttempts(int value) =>
        value is >= MinMaxAttempts and <= MaxMaxAttempts;
}

/// <summary>AI Decision 的服务端配置根。V0 固定 shadow-only，不暴露任何自动放行开关。</summary>
public sealed class AiDecisionOptions
{
    /// <summary>proposal 的服务端分类 policy，对应 <c>TenonAdmin:Workflow:AiDecision:Policy</c>。</summary>
    public AiDecisionPolicyOptions Policy { get; set; } = new();

    /// <summary>
    /// OpenAI-compatible Chat Completions Provider 配置，对应
    /// <c>TenonAdmin:Workflow:AiDecision:OpenAiCompatible</c>。
    /// </summary>
    public OpenAiCompatibleAiDecisionOptions OpenAiCompatible { get; set; } = new();
}

/// <summary>
/// OpenAI-compatible <c>/v1/chat/completions</c> Provider 的最小 v0 配置。启用后仍只生成
/// shadow-only proposal，绝不向模型授予推进工作流、批准或拒绝的权限。
/// </summary>
public sealed class OpenAiCompatibleAiDecisionOptions
{
    /// <summary>默认的 OpenAI Chat Completions 完整 endpoint。</summary>
    public const string DefaultEndpoint = "https://api.openai.com/v1/chat/completions";

    /// <summary>默认单次外呼超时（秒）。</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>允许的最小单次外呼超时（秒）。</summary>
    public const int MinimumTimeoutSeconds = 1;

    /// <summary>允许的最大单次外呼超时（秒）。</summary>
    public const int MaximumTimeoutSeconds = 120;

    /// <summary>默认的外部响应总字节上限。</summary>
    public const int DefaultResponseByteCap = 64 * 1024;

    /// <summary>允许的最小外部响应总字节上限。</summary>
    public const int MinimumResponseByteCap = 1024;

    /// <summary>允许的最大外部响应总字节上限。</summary>
    public const int MaximumResponseByteCap = 1024 * 1024;

    /// <summary>是否启用真实 HTTP Provider；默认关闭并保持 fail-closed。</summary>
    public bool Enabled { get; set; }

    /// <summary>完整 Chat Completions endpoint；本地 compatible server 可自行指定。</summary>
    public string Endpoint { get; set; } = DefaultEndpoint;

    /// <summary>启用时必填的模型标识。</summary>
    public string Model { get; set; } = "";

    /// <summary>可选 Bearer API key；空白值不发送 Authorization，供本地 compatible server 使用。</summary>
    public string? ApiKey { get; set; }

    /// <summary>单次外呼超时（秒），会再被执行的绝对 deadline 收紧。</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>流式读取外部响应时允许的总字节上限。</summary>
    public int ResponseByteCap { get; set; } = DefaultResponseByteCap;

    /// <summary>仅在受控本地环境显式开启时允许 HTTP endpoint；默认必须 HTTPS。</summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>验证可安全发送的 Provider 配置；异常不回显 endpoint query 或 API key。</summary>
    public void Validate()
    {
        if (TimeoutSeconds is < MinimumTimeoutSeconds or > MaximumTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds),
                $"TimeoutSeconds 必须在 {MinimumTimeoutSeconds} 到 {MaximumTimeoutSeconds} 之间。");
        }

        if (ResponseByteCap is < MinimumResponseByteCap or > MaximumResponseByteCap)
        {
            throw new ArgumentOutOfRangeException(nameof(ResponseByteCap),
                $"ResponseByteCap 必须在 {MinimumResponseByteCap} 到 {MaximumResponseByteCap} 之间。");
        }

        if (Enabled && string.IsNullOrWhiteSpace(Model))
        {
            throw new ArgumentException("启用 OpenAI-compatible Provider 时 Model 不能为空。", nameof(Model));
        }

        var endpoint = CreateValidatedEndpoint();
        if (string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new ArgumentException("HTTP endpoint 不能携带 ApiKey。", nameof(ApiKey));
        }

        if (!string.IsNullOrWhiteSpace(ApiKey) && ApiKey.Any(char.IsControl))
        {
            throw new ArgumentException("ApiKey 不能包含控制字符。", nameof(ApiKey));
        }
    }

    /// <summary>返回已验证的 endpoint；仅 adapter 使用，避免把 URI 解析策略复制到外呼路径。</summary>
    internal Uri CreateValidatedEndpoint()
    {
        if (string.IsNullOrWhiteSpace(Endpoint)
            || Endpoint.Any(char.IsControl)
            || !Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint)
            || !endpoint.IsWellFormedOriginalString()
            || string.IsNullOrEmpty(endpoint.Host)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException("Endpoint 必须是无 userinfo 或 fragment 的绝对 HTTP/HTTPS URI。", nameof(Endpoint));
        }

        var isHttp = string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        var isHttps = string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        if (!isHttp && !isHttps)
        {
            throw new ArgumentException("Endpoint 必须是无 userinfo 或 fragment 的绝对 HTTP/HTTPS URI。", nameof(Endpoint));
        }

        if (isHttp && !AllowInsecureHttp)
        {
            throw new ArgumentException("Endpoint 必须使用 HTTPS，除非显式允许 HTTP。", nameof(Endpoint));
        }

        return endpoint;
    }
}

internal static class WorkflowOptionsValidation
{
    public static void Validate(WorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!WorkflowOptions.IsValidMaxAttempts(options.MaxAttempts))
        {
            throw new InvalidOperationException(
                $"TenonAdmin:Workflow:MaxAttempts 配置无效:值为 {options.MaxAttempts}," +
                $"必须在 {WorkflowOptions.MinMaxAttempts}–{WorkflowOptions.MaxMaxAttempts} 之间。");
        }

        if (options.NodeExecutionScanBatchSize is < 1 or > WorkflowOptions.MaxNodeExecutionScanBatchSize)
        {
            throw new InvalidOperationException(
                $"TenonAdmin:Workflow:NodeExecutionScanBatchSize 配置无效:值为 {options.NodeExecutionScanBatchSize}," +
                $"必须在 1–{WorkflowOptions.MaxNodeExecutionScanBatchSize} 之间。");
        }

        if (options.NodeExecutionLeaseSeconds is < 1 or > WorkflowOptions.MaxNodeExecutionLeaseSeconds)
        {
            throw new InvalidOperationException(
                $"TenonAdmin:Workflow:NodeExecutionLeaseSeconds 配置无效:值为 {options.NodeExecutionLeaseSeconds}," +
                $"必须在 1–{WorkflowOptions.MaxNodeExecutionLeaseSeconds} 秒之间。");
        }

        if (options.OutboxScanBatchSize is < 1 or > WorkflowOptions.MaxOutboxScanBatchSize)
        {
            throw new InvalidOperationException(
                $"TenonAdmin:Workflow:OutboxScanBatchSize 配置无效:值为 {options.OutboxScanBatchSize}," +
                $"必须在 1–{WorkflowOptions.MaxOutboxScanBatchSize} 之间。");
        }

        if (options.OutboxVisibilityTimeoutSeconds is < 1 or > WorkflowOptions.MaxOutboxVisibilityTimeoutSeconds)
        {
            throw new InvalidOperationException(
                $"TenonAdmin:Workflow:OutboxVisibilityTimeoutSeconds 配置无效:值为 {options.OutboxVisibilityTimeoutSeconds}," +
                $"必须在 1–{WorkflowOptions.MaxOutboxVisibilityTimeoutSeconds} 秒之间。");
        }

        if (!WorkflowOptions.IsValidMaxAttempts(options.OutboxMaxAttempts))
        {
            throw new InvalidOperationException(
                $"TenonAdmin:Workflow:OutboxMaxAttempts 配置无效:值为 {options.OutboxMaxAttempts}," +
                $"必须在 {WorkflowOptions.MinMaxAttempts}–{WorkflowOptions.MaxMaxAttempts} 之间。");
        }

        if (options.AiDecision is null)
        {
            throw new InvalidOperationException("TenonAdmin:Workflow:AiDecision 配置无效:不能为 null。");
        }

        if (options.AiDecision.Policy is null)
        {
            throw new InvalidOperationException("TenonAdmin:Workflow:AiDecision:Policy 配置无效:不能为 null。");
        }

        if (options.AiDecision.OpenAiCompatible is null)
        {
            throw new InvalidOperationException("TenonAdmin:Workflow:AiDecision:OpenAiCompatible 配置无效:不能为 null。");
        }

        try
        {
            options.AiDecision.Policy.Validate();
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("TenonAdmin:Workflow:AiDecision:Policy 配置无效。");
        }

        try
        {
            options.AiDecision.OpenAiCompatible.Validate();
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("TenonAdmin:Workflow:AiDecision:OpenAiCompatible 配置无效。");
        }
    }
}
