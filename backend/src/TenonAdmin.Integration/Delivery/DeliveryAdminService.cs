using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IDeliveryAdminService"/> 默认实现(实现契约 §8.6)。人工操作步骤:
/// 读最新记录 →(可选)比对页面栅栏 → 按状态、适配器能力与尝试历史复检可用性 → 按栅栏 CAS 迁移并追加人工尝试记录(同一短事务)。
/// 查询操作在任何事务之外调用适配器,再以查询前看到的栅栏回写;期间记录若被投递器或他人改动,则拒绝(49046),查询记录照常保留。
/// 所有操作保持原幂等标识,重新发送仍由投递器经同一出站基础设施完成(地址安全、凭据、分类不因人工而放宽)。
/// </summary>
public class DeliveryAdminService(
    ISqlSugarClient db,
    IEnumerable<IDeliveryAdapter> adapters,
    DeliveryAlertPublisher alerts,
    ICurrentUser currentUser,
    IntegrationOptions options,
    TimeProvider time) : IDeliveryAdminService
{
    /// <summary>详情中返回的尝试记录上限。</summary>
    public const int MaxAttemptsInDetail = 200;

    private const string ConflictNote = "人工操作未生效:记录已被投递器或他人改动。";

    /// <inheritdoc />
    public virtual async Task<PagedList<DeliveryListItem>> PageAsync(DeliveryPageInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var status = input.Status ?? DeliveryStatus.Pending;
        var adapter = input.Adapter?.Trim();
        var operation = input.Operation?.Trim();
        var key = input.DeliveryKey?.Trim();
        var businessKey = input.BusinessKey?.Trim();
        var start = input.StartTime ?? DateTime.MinValue;
        var end = input.EndTime ?? DateTime.MaxValue;

        var page = await db.Queryable<IntegrationDelivery>()
            .WhereIF(input.Status.HasValue, d => d.Status == status)
            .WhereIF(!string.IsNullOrEmpty(adapter), d => d.Adapter == adapter)
            .WhereIF(!string.IsNullOrEmpty(operation), d => d.Operation.Contains(operation!))
            .WhereIF(!string.IsNullOrEmpty(key), d => d.DeliveryKey == key)
            .WhereIF(!string.IsNullOrEmpty(businessKey), d => d.BusinessKey == businessKey)
            .WhereIF(input.StartTime.HasValue, d => d.CreateTime >= start)
            .WhereIF(input.EndTime.HasValue, d => d.CreateTime <= end)
            .OrderBy(d => d.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);

        return new PagedList<DeliveryListItem>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = page.Items.Select(ToListItem).ToList(),
        };
    }

    /// <inheritdoc />
    public virtual async Task<DeliveryDetail> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(id, cancellationToken);
        var attempts = await db.Queryable<IntegrationDeliveryAttempt>()
            .Where(a => a.DeliveryId == id)
            .OrderBy(a => a.Id, OrderByType.Desc)
            .Take(MaxAttemptsInDetail)
            .ToListAsync(cancellationToken);
        var operatorIds = attempts.Where(a => a.OperatorId.HasValue).Select(a => a.OperatorId!.Value).Distinct().ToList();
        var names = operatorIds.Count == 0
            ? new Dictionary<long, string>()
            : (await db.Queryable<SysUser>().Where(u => operatorIds.Contains(u.Id)).Select(u => new { u.Id, u.Name }).ToListAsync(cancellationToken))
                .ToDictionary(u => u.Id, u => u.Name);
        var capabilities = DeliveryAdapterCapabilities.Of(FindAdapter(row.Adapter));
        var allowed = DeliveryStateMachine.AllowedActions(row, capabilities, await HasUnsafeUnknownAsync(row, capabilities, cancellationToken));
        return ToDetail(row, capabilities, allowed, attempts.Select(a => ToAttemptView(a, names)).ToList());
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<DeliveryStatusCount>> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Queryable<IntegrationDelivery>()
            .GroupBy(d => d.Status)
            .Select(d => new { d.Status, Count = SqlFunc.AggregateCount(d.Id) })
            .ToListAsync(cancellationToken);
        var counts = rows.ToDictionary(r => r.Status, r => r.Count);
        return Enum.GetValues<DeliveryStatus>().Select(s => new DeliveryStatusCount(s, counts.GetValueOrDefault(s))).ToList();
    }

    /// <inheritdoc />
    public virtual async Task<DeliveryDetail> ExecuteAsync(long id, string action, DeliveryActionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = await LoadAsync(id, cancellationToken);
        IntegrationErrorCode.ThrowIf(input.Fence is { } fence && fence != row.Fence, IntegrationErrorCode.DeliveryConcurrentlyModified,
            fallbackMessage: "投递记录已变化,请刷新后再操作。");
        var adapter = FindAdapter(row.Adapter);
        var capabilities = DeliveryAdapterCapabilities.Of(adapter);
        var allowed = DeliveryStateMachine.AllowedActions(row, capabilities, await HasUnsafeUnknownAsync(row, capabilities, cancellationToken));
        IntegrationErrorCode.ThrowIf(action is null || !allowed.Contains(action), IntegrationErrorCode.DeliveryActionNotAllowed,
            new Dictionary<string, object?> { ["action"] = action, ["status"] = row.Status.ToString() },
            $"当前状态({row.Status})不允许执行 {action}。");
        var note = input.Note?.Trim();
        IntegrationErrorCode.ThrowIf(note is { Length: > 256 }, IntegrationErrorCode.DeliveryNoteRequired,
            new Dictionary<string, object?> { ["max"] = 256 }, "处理说明不能超过 256 字。");
        IntegrationErrorCode.ThrowIf(action is DeliveryActions.ConfirmSucceeded or DeliveryActions.ConfirmNotExecuted && string.IsNullOrEmpty(note),
            IntegrationErrorCode.DeliveryNoteRequired, fallbackMessage: "确认类操作须填写处理说明。");

        if (action == DeliveryActions.Query)
            await ManualQueryAsync(row, adapter!, capabilities, note, cancellationToken);
        else
            await ManualTransitionAsync(row, action!, note, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>重试、确认、取消:按看到的栅栏迁移并追加一条人工记录;冲突即 49046。</summary>
    protected virtual async Task ManualTransitionAsync(IntegrationDelivery row, string action, string? note, CancellationToken cancellationToken)
    {
        var now = Now();
        var start = DeliveryTransition.From(row) with { ResolvedBy = currentUser.UserId, ResolutionNote = note };
        var transition = action switch
        {
            // 重新发送前重置预算;幂等标识不变,发送仍由投递器走同一出站基础设施
            DeliveryActions.Retry or DeliveryActions.ConfirmNotExecuted =>
                DeliveryStateMachine.ResetBudget(start, row, now, options.Delivery) with { LastError = null, Reason = action },
            DeliveryActions.ConfirmSucceeded => start with
            {
                Status = DeliveryStatus.Succeeded, CompletedAtUtc = now, NextAttemptAtUtc = now, VerifyBeforeSend = false, LastError = null, Reason = action,
            },
            DeliveryActions.Cancel => start with { Status = DeliveryStatus.Cancelled, CompletedAtUtc = now, NextAttemptAtUtc = now, Reason = action },
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
        var attempt = ManualAttempt(row, DeliveryAttemptKind.Manual, now, action, note);
        var applied = await DeliveryStore.ApplyWithAttemptAsync(db, row, transition, attempt, ConflictNote, cancellationToken);
        IntegrationErrorCode.ThrowIf(!applied, IntegrationErrorCode.DeliveryConcurrentlyModified, fallbackMessage: "投递记录已变化,请刷新后再操作。");
    }

    /// <summary>立即查询:事务外调用适配器,再按查询前的栅栏回写(失败结果只记尝试,不改状态)。</summary>
    protected virtual async Task ManualQueryAsync(
        IntegrationDelivery row, IDeliveryAdapter adapter, DeliveryAdapterCapabilities capabilities, string? note, CancellationToken cancellationToken)
    {
        var started = time.GetUtcNow().UtcDateTime;
        DeliveryQueryResult result;
        try
        {
            result = await adapter.QueryAsync(DeliveryContext.From(row), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new DeliveryQueryResult(RemoteDeliveryState.Unknown, Reason: "adapter_error:" + ex.GetType().Name);
        }

        var transition = DeliveryStateMachine.AfterQuery(row, capabilities, result, DeliveryQueryPhase.Manual, Now(), options.Delivery);
        if (transition is not null) transition = transition with { ResolvedBy = currentUser.UserId, ResolutionNote = note ?? transition.ResolutionNote };
        var attempt = ManualAttempt(row, DeliveryAttemptKind.Query, started, result.State.ToString(), note, result.HttpStatus, result.CallId, result.Reason);
        var applied = await DeliveryStore.ApplyWithAttemptAsync(db, row, transition, attempt, ConflictNote, cancellationToken);
        IntegrationErrorCode.ThrowIf(transition is not null && !applied, IntegrationErrorCode.DeliveryConcurrentlyModified,
            fallbackMessage: "查询期间投递记录已变化,请刷新后查看。");
        if (applied) await alerts.PublishAsync(row, transition!, CancellationToken.None);
    }

    /// <summary>
    /// 本轮预算内是否存在「对方不去重、结果未知」的发送(此时耗尽记录不能直接重试,须先核实)。
    /// 只看本轮:更早的结果未知已在重置预算时由人工核对过(确认未执行、重试)或经查询确认对方没有记录,不再挡住重试。
    /// </summary>
    protected virtual async Task<bool> HasUnsafeUnknownAsync(IntegrationDelivery row, DeliveryAdapterCapabilities capabilities, CancellationToken cancellationToken)
    {
        if (row.Status != DeliveryStatus.Exhausted || capabilities.SupportsIdempotency) return false;
        var send = DeliveryAttemptKind.Send;
        var unknown = nameof(OutboundOutcome.Unknown);
        var cancelled = nameof(OutboundOutcome.Cancelled);
        var id = row.Id;
        var budgetStart = row.BudgetStartAttempt;
        return await db.Queryable<IntegrationDeliveryAttempt>()
            .AnyAsync(a => a.DeliveryId == id && a.Kind == send && a.AttemptNo > budgetStart && (a.Outcome == unknown || a.Outcome == cancelled),
                cancellationToken);
    }

    /// <summary>按 Id 读记录;不存在抛 49040。</summary>
    protected virtual async Task<IntegrationDelivery> LoadAsync(long id, CancellationToken cancellationToken) =>
        await db.Queryable<IntegrationDelivery>().Where(d => d.Id == id).FirstAsync(cancellationToken)
        ?? throw IntegrationErrorCode.Exception(IntegrationErrorCode.DeliveryNotFound, fallbackMessage: "投递记录不存在。");

    /// <summary>按名取适配器。</summary>
    protected virtual IDeliveryAdapter? FindAdapter(string name) => adapters.FindByName(name);

    /// <summary>实体 → 列表项。</summary>
    protected virtual DeliveryListItem ToListItem(IntegrationDelivery d) => new()
    {
        Id = d.Id,
        DeliveryKey = d.DeliveryKey,
        Adapter = d.Adapter,
        Operation = d.Operation,
        BusinessKey = d.BusinessKey,
        Status = d.Status,
        AttemptCount = d.AttemptCount,
        MaxAttempts = d.MaxAttempts,
        AttemptsInBudget = DeliveryStateMachine.SendsInBudget(d),
        NextAttemptAt = d.Status is DeliveryStatus.Pending or DeliveryStatus.AwaitingConfirmation ? IntegrationAppState.ToOffset(d.NextAttemptAtUtc) : null,
        LastOutcome = d.LastOutcome,
        LastError = d.LastError,
        RemoteReference = d.RemoteReference,
        CreateTime = d.CreateTime,
        CompletedAt = IntegrationAppState.ToOffset(d.CompletedAtUtc),
    };

    /// <summary>实体 → 详情。</summary>
    protected virtual DeliveryDetail ToDetail(
        IntegrationDelivery d, DeliveryAdapterCapabilities capabilities, IReadOnlyList<string> allowed, IReadOnlyList<DeliveryAttemptView> attempts)
    {
        var item = ToListItem(d);
        return new DeliveryDetail
        {
            Id = item.Id,
            DeliveryKey = item.DeliveryKey,
            Adapter = item.Adapter,
            Operation = item.Operation,
            BusinessKey = item.BusinessKey,
            Status = item.Status,
            AttemptCount = item.AttemptCount,
            MaxAttempts = item.MaxAttempts,
            AttemptsInBudget = item.AttemptsInBudget,
            NextAttemptAt = item.NextAttemptAt,
            LastOutcome = item.LastOutcome,
            LastError = item.LastError,
            RemoteReference = item.RemoteReference,
            CreateTime = item.CreateTime,
            CompletedAt = item.CompletedAt,
            PayloadJson = d.PayloadJson,
            Fence = d.Fence,
            DeadlineAt = IntegrationAppState.ToOffset(d.DeadlineAtUtc),
            ConfirmDeadlineAt = IntegrationAppState.ToOffset(d.ConfirmDeadlineAtUtc),
            VerifyBeforeSend = d.VerifyBeforeSend,
            LeaseUntil = IntegrationAppState.ToOffset(d.LeaseUntilUtc),
            LeaseOwner = d.LeaseOwner,
            ResolvedBy = d.ResolvedBy,
            ResolutionNote = d.ResolutionNote,
            AdapterRegistered = capabilities.Registered,
            SupportsIdempotency = capabilities.SupportsIdempotency,
            SupportsQuery = capabilities.SupportsQuery,
            AllowedActions = allowed,
            Attempts = attempts,
        };
    }

    /// <summary>尝试实体 → 视图。</summary>
    protected virtual DeliveryAttemptView ToAttemptView(IntegrationDeliveryAttempt a, IReadOnlyDictionary<long, string> operatorNames) => new()
    {
        Id = a.Id,
        AttemptNo = a.AttemptNo,
        Kind = a.Kind,
        Trigger = a.Trigger,
        OperatorId = a.OperatorId,
        OperatorName = a.OperatorId is { } op && operatorNames.TryGetValue(op, out var name) ? name : null,
        StartedAt = IntegrationAppState.ToOffset(a.StartedAtUtc),
        FinishedAt = IntegrationAppState.ToOffset(a.FinishedAtUtc),
        Outcome = a.Outcome,
        HttpStatus = a.HttpStatus,
        CallId = a.CallId,
        ErrorSummary = a.ErrorSummary,
        Note = a.Note,
        NodeName = a.NodeName,
    };

    private IntegrationDeliveryAttempt ManualAttempt(
        IntegrationDelivery row, DeliveryAttemptKind kind, DateTime startedUtc, string outcome, string? note,
        int? httpStatus = null, string? callId = null, string? summary = null) => new()
    {
        DeliveryId = row.Id,
        AttemptNo = row.AttemptCount,
        Kind = kind,
        Trigger = DeliveryAttemptTrigger.Manual,
        OperatorId = currentUser.UserId,
        StartedAtUtc = IntegrationAppState.FloorSeconds(startedUtc),
        FinishedAtUtc = Now(),
        Outcome = IntegrationText.Truncate(outcome, 32) ?? "",
        HttpStatus = httpStatus,
        CallId = callId,
        ErrorSummary = summary,
        Note = note,
    };

    private DateTime Now() => IntegrationAppState.FloorSeconds(time.GetUtcNow().UtcDateTime);
}
