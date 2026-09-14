using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置长期委托规则服务。规则变更与幂等回执在同一事务中提交；任务解析只读规则，且只做一跳。
/// </summary>
public class WfDelegationService(
    IRepository<WfDelegationRule> rules,
    IRepository<WfDelegationRuleHistory> history,
    IRepository<SysUser> users,
    IWfOperationReceiptService receipts,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IWfDelegationNotifier notifier,
    ILogger<WfDelegationService>? logger = null) : IWfDelegationService
{
    public virtual async Task<PagedList<WfDelegationRuleOutput>> PageAsync(
        WfDelegationRulePageInput input,
        CancellationToken cancellationToken = default)
    {
        var scope = currentUser.OrgId;
        var callerId = currentUser.UserId;
        if (callerId is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationScopeDenied);
        if (scope is null && !currentUser.IsSuperAdmin)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationScopeDenied);
        var query = rules.AsQueryable()
            .WhereIF(scope.HasValue, r => r.ScopeOrgId == scope!.Value)
            .WhereIF(input.OriginalUserId.HasValue, r => r.OriginalUserId == input.OriginalUserId!.Value)
            .WhereIF(input.Enabled.HasValue, r => r.Enabled == input.Enabled!.Value)
            .ToPagedListAsync(input, q => q.OrderBy(r => r.Id, OrderByType.Desc));
        var page = await query;
        return await MapPageAsync(page, cancellationToken);
    }

    public virtual async Task<WfDelegationRuleOutput> AddAsync(
        WfDelegationRuleInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(input.RequestId);
        input = NormalizeInput(input);
        var payloadHash = PayloadHash(input);
        var replayIdentity = await FindAddReplayIdentityAsync(
            input.OriginalUserId, input.RequestId, payloadHash, cancellationToken);
        WfOperationIdentity identity;
        long scope = 0;
        if (replayIdentity is not null)
        {
            identity = replayIdentity;
            scope = long.Parse(identity.ScopeKey, CultureInfo.InvariantCulture);
        }
        else
        {
            scope = await RequireScopeAsync(input.OriginalUserId, allowOtherOwners: true, cancellationToken);
            identity = CreateIdentity(
                scope,
                WfCommandType.DelegationRuleAdd,
                input.OriginalUserId,
                input.RequestId,
                payloadHash,
                currentUser.UserId!.Value);
        }

        var transaction = await InTransactionAsync(identity, async () =>
        {
            await LockScopeAsync(scope, cancellationToken);
            await ValidateUsersAsync(scope, input.OriginalUserId, input.DelegateUserId, cancellationToken);
            ValidateWindow(input);
            await EnsureSlotAvailableAsync(scope, input.OriginalUserId, null, cancellationToken);
            if (input.Enabled)
                await EnsureNoCycleAsync(scope, input.OriginalUserId, input.DelegateUserId, null, cancellationToken);

            var existing = await rules.Db.Queryable<WfDelegationRule>()
                .ClearFilter<ISoftDelete>()
                .Where(r => r.ScopeOrgId == scope && r.OriginalUserId == input.OriginalUserId)
                .FirstAsync(cancellationToken);
            var reactivatingDeleted = existing?.IsDelete == true;

            WfDelegationRule rule;
            if (existing is { IsDelete: true })
            {
                var oldVersion = existing.Version;
                var affected = await rules.Db.Updateable<WfDelegationRule>()
                    .SetColumns(r => new WfDelegationRule
                    {
                        DelegateUserId = input.DelegateUserId,
                        Enabled = input.Enabled,
                        StartsAt = input.StartsAt,
                        EndsAt = input.EndsAt,
                        IsDelete = false,
                        Version = oldVersion + 1,
                    })
                    .Where(r => r.Id == existing.Id && r.Version == oldVersion && r.IsDelete)
                    .ExecuteCommandAsync(cancellationToken);
                if (affected != 1)
                    throw Conflict(existing.Id);

                existing.DelegateUserId = input.DelegateUserId;
                existing.Enabled = input.Enabled;
                existing.StartsAt = input.StartsAt;
                existing.EndsAt = input.EndsAt;
                existing.IsDelete = false;
                existing.Version++;
                rule = existing;
            }
            else
            {
                rule = new WfDelegationRule
                {
                    ScopeOrgId = scope,
                    OriginalUserId = input.OriginalUserId,
                    DelegateUserId = input.DelegateUserId,
                    Enabled = input.Enabled,
                    StartsAt = input.StartsAt,
                    EndsAt = input.EndsAt,
                };
                var savepoint = await BeginInsertSavepointAsync(cancellationToken);
                try
                {
                    await rules.InsertAsync(rule);
                    if (savepoint)
                        await ReleaseInsertSavepointAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException
                                           && WfDatabaseExceptionClassifier.IsUniqueConstraintViolation(ex))
                {
                    if (savepoint)
                        await RollbackInsertSavepointAsync(cancellationToken);
                    if (await SlotExistsAsync(scope, input.OriginalUserId, cancellationToken))
                        throw Conflict(0);
                    throw;
                }
            }

            await AppendHistoryAsync(
                rule,
                reactivatingDeleted ? WfDelegationRuleChangeType.Enabled : WfDelegationRuleChangeType.Created,
                input.RequestId,
                cancellationToken);
            return new DelegationChangeResult(
                await ToOutputAsync(rule, cancellationToken),
                reactivatingDeleted ? WfDelegationRuleChangeType.Enabled : WfDelegationRuleChangeType.Created);
        }, cancellationToken);
        if (!transaction.Replayed)
            await NotifyRuleChangedAsync(transaction.Data.Output, transaction.Data.ChangeType, cancellationToken);
        return transaction.Data.Output;
    }

    public virtual async Task<WfDelegationRuleOutput> UpdateAsync(
        long id,
        WfDelegationRuleInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(input.RequestId);
        input = NormalizeInput(input);
        var rule = await GetForIdentityAsync(id, cancellationToken);
        var scope = rule.ScopeOrgId;

        var identity = CreateIdentity(
            scope,
            WfCommandType.DelegationRuleUpdate,
            id,
            input.RequestId,
            PayloadHash(input),
            currentUser.UserId!.Value);

        var transaction = await InTransactionAsync(identity, async () =>
        {
            await LockScopeAsync(scope, cancellationToken);
            rule = await GetManagedAsync(id, cancellationToken, includeDeleted: true);
            if (rule.IsDelete)
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationRuleNotFound,
                    new Dictionary<string, object?> { ["id"] = id });
            if (rule.OriginalUserId != input.OriginalUserId)
                throw Invalid("originalUserImmutable");
            await ValidateUsersAsync(scope, input.OriginalUserId, input.DelegateUserId, cancellationToken);
            ValidateWindow(input);
            if (input.Enabled)
                await EnsureNoCycleAsync(scope, input.OriginalUserId, input.DelegateUserId, id, cancellationToken);
            var oldVersion = rule.Version;
            var affected = await rules.Db.Updateable<WfDelegationRule>()
                .SetColumns(r => new WfDelegationRule
                {
                    DelegateUserId = input.DelegateUserId,
                    Enabled = input.Enabled,
                    StartsAt = input.StartsAt,
                    EndsAt = input.EndsAt,
                    Version = oldVersion + 1,
                })
                .Where(r => r.Id == id && r.Version == oldVersion && !r.IsDelete)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
                throw Conflict(id);

            var changeType = rule.Enabled == input.Enabled
                ? WfDelegationRuleChangeType.Updated
                : input.Enabled ? WfDelegationRuleChangeType.Enabled : WfDelegationRuleChangeType.Disabled;
            rule.DelegateUserId = input.DelegateUserId;
            rule.Enabled = input.Enabled;
            rule.StartsAt = input.StartsAt;
            rule.EndsAt = input.EndsAt;
            rule.Version++;
            await AppendHistoryAsync(rule, changeType, input.RequestId, cancellationToken);
            return new DelegationUpdateResult(await ToOutputAsync(rule, cancellationToken), changeType);
        }, cancellationToken);
        if (!transaction.Replayed)
            await NotifyRuleChangedAsync(transaction.Data.Output, transaction.Data.ChangeType, cancellationToken);
        return transaction.Data.Output;
    }

    public virtual async Task DeleteAsync(
        long id,
        string? requestId,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(requestId);
        var rule = await GetForIdentityAsync(id, cancellationToken);
        var identity = CreateIdentity(
            rule.ScopeOrgId,
            WfCommandType.DelegationRuleDelete,
            id,
            requestId,
            WfIdentityHash.ComputePayloadHash(id, "delete"),
            currentUser.UserId!.Value);

        var transaction = await InTransactionAsync(identity, async () =>
        {
            await LockScopeAsync(rule.ScopeOrgId, cancellationToken);
            rule = await GetManagedAsync(id, cancellationToken, includeDeleted: true);
            if (rule.IsDelete)
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationRuleNotFound,
                    new Dictionary<string, object?> { ["id"] = id });
            var oldVersion = rule.Version;
            var affected = await rules.Db.Updateable<WfDelegationRule>()
                .SetColumns(r => new WfDelegationRule { IsDelete = true, Version = oldVersion + 1 })
                .Where(r => r.Id == id && r.Version == oldVersion && !r.IsDelete)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
                throw Conflict(id);

            await AppendHistoryAsync(rule, WfDelegationRuleChangeType.Deleted, requestId, cancellationToken);
            return await ToOutputAsync(rule, cancellationToken);
        }, cancellationToken);
        if (!transaction.Replayed)
            await NotifyRuleChangedAsync(transaction.Data, WfDelegationRuleChangeType.Deleted, cancellationToken);
    }

    public virtual async Task<IReadOnlyList<WfDelegationAssignment>> ResolveAsync(
        IReadOnlyList<long> originalUserIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = originalUserIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var owners = await users.AsQueryable()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.OrgId })
            .ToListAsync(cancellationToken);
        var ownerOrgIds = owners.Where(u => u.OrgId.HasValue).Select(u => u.OrgId!.Value).Distinct().ToList();
        if (ownerOrgIds.Count == 0)
            return ids.Select(id => new WfDelegationAssignment(id, null, null, null)).ToList();

        var candidates = await rules.AsQueryable()
            .Where(r => ids.Contains(r.OriginalUserId)
                        && ownerOrgIds.Contains(r.ScopeOrgId)
                        && r.Enabled
                        && r.StartsAt <= now
                        && r.EndsAt > now)
            .ToListAsync(cancellationToken);
        var targetIds = candidates.Select(r => r.DelegateUserId).Distinct().ToList();
        var enabledTargets = targetIds.Count == 0
            ? []
            : await users.AsQueryable()
                .Where(u => targetIds.Contains(u.Id) && u.Enabled)
                .Select(u => new { u.Id, u.OrgId })
                .ToListAsync(cancellationToken);
        var enabledTargetMap = enabledTargets.ToDictionary(u => u.Id, u => u.OrgId);
        var ownerMap = owners.ToDictionary(u => u.Id, u => u.OrgId);

        return ids.Select(originalId =>
        {
            if (!ownerMap.TryGetValue(originalId, out var ownerOrg) || ownerOrg is null)
                return new WfDelegationAssignment(originalId, null, null, null);

            var rule = candidates.FirstOrDefault(r => r.OriginalUserId == originalId && r.ScopeOrgId == ownerOrg.Value);
            if (rule is null
                || !enabledTargetMap.TryGetValue(rule.DelegateUserId, out var targetOrg)
                || targetOrg != ownerOrg)
                return new WfDelegationAssignment(originalId, null, null, null);

            return new WfDelegationAssignment(rule.DelegateUserId, originalId, rule.Id, ownerOrg);
        }).ToList();
    }

    protected virtual bool UseInsertSavepoint =>
        rules.Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL && rules.Db.Ado.IsAnyTran();

    protected const string InsertSavepointName = "wf_delegation_insert";

    protected virtual async Task<bool> BeginInsertSavepointAsync(CancellationToken cancellationToken)
    {
        if (!UseInsertSavepoint)
            return false;
        cancellationToken.ThrowIfCancellationRequested();
        await rules.Db.Ado.ExecuteCommandAsync($"SAVEPOINT {InsertSavepointName}");
        return true;
    }

    protected virtual Task RollbackInsertSavepointAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return rules.Db.Ado.ExecuteCommandAsync($"ROLLBACK TO SAVEPOINT {InsertSavepointName}");
    }

    protected virtual Task ReleaseInsertSavepointAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return rules.Db.Ado.ExecuteCommandAsync($"RELEASE SAVEPOINT {InsertSavepointName}");
    }

    protected virtual async Task<bool> SlotExistsAsync(
        long scope,
        long originalUserId,
        CancellationToken cancellationToken)
    {
        if (rules.Db.CurrentConnectionConfig.DbType == DbType.MySql)
        {
            using var isolated = rules.Db.CopyNew();
            return await isolated.Queryable<WfDelegationRule>()
                .Where(rule => rule.ScopeOrgId == scope && rule.OriginalUserId == originalUserId)
                .AnyAsync(cancellationToken);
        }

        return await rules.Db.Queryable<WfDelegationRule>()
            .Where(rule => rule.ScopeOrgId == scope && rule.OriginalUserId == originalUserId)
            .AnyAsync(cancellationToken);
    }

    protected virtual async Task<long> RequireScopeAsync(
        long? ownerId,
        bool allowOtherOwners,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationScopeDenied);

        if (ownerId is not { } id || id <= 0)
            throw Invalid("originalUserRequired");
        var owner = await users.GetByIdAsync(id);
        if (owner is null || owner.OrgId is null || (!allowOtherOwners && owner.Id != callerId))
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationScopeDenied,
                new Dictionary<string, object?> { ["originalUserId"] = id });
        if (!currentUser.IsSuperAdmin && currentUser.OrgId != owner.OrgId)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationScopeDenied,
                new Dictionary<string, object?> { ["originalUserId"] = id });
        if (currentUser.OrgId is { } callerScope && callerScope != owner.OrgId)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationScopeDenied,
                new Dictionary<string, object?> { ["originalUserId"] = id });
        return owner.OrgId.Value;
    }

    protected virtual async Task<WfDelegationRule> GetManagedAsync(
        long id,
        CancellationToken cancellationToken,
        bool includeDeleted = false)
    {
        var query = rules.Db.Queryable<WfDelegationRule>();
        if (includeDeleted)
            query = query.ClearFilter<ISoftDelete>();
        var rule = await query.Where(r => r.Id == id).FirstAsync(cancellationToken);
        if (rule is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationRuleNotFound,
                new Dictionary<string, object?> { ["id"] = id });
        var ownerScope = await RequireScopeAsync(rule.OriginalUserId, allowOtherOwners: true, cancellationToken);
        if (ownerScope != rule.ScopeOrgId)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationScopeDenied);
        return rule;
    }

    protected virtual async Task<WfDelegationRule> GetForIdentityAsync(
        long id,
        CancellationToken cancellationToken) =>
        await rules.Db.Queryable<WfDelegationRule>()
            .ClearFilter<ISoftDelete>()
            .Where(r => r.Id == id)
            .FirstAsync(cancellationToken)
        ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationRuleNotFound,
            new Dictionary<string, object?> { ["id"] = id });

    protected virtual async Task ValidateUsersAsync(
        long scope,
        long originalUserId,
        long delegateUserId,
        CancellationToken cancellationToken)
    {
        if (originalUserId <= 0 || delegateUserId <= 0 || originalUserId == delegateUserId)
            throw Invalid("userPairInvalid");

        var pair = await users.AsQueryable()
            .Where(u => u.Id == originalUserId || u.Id == delegateUserId)
            .Select(u => new { u.Id, u.OrgId, u.Enabled })
            .ToListAsync(cancellationToken);
        var owner = pair.FirstOrDefault(u => u.Id == originalUserId);
        var target = pair.FirstOrDefault(u => u.Id == delegateUserId);
        if (owner is null || target is null || !owner.Enabled || !target.Enabled
            || owner.OrgId != scope || target.OrgId != scope)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationScopeDenied,
                new Dictionary<string, object?> { ["delegateUserId"] = delegateUserId });
    }

    protected virtual async Task EnsureSlotAvailableAsync(
        long scope,
        long originalUserId,
        long? exceptId,
        CancellationToken cancellationToken)
    {
        var exists = await rules.AsQueryable()
            .Where(r => r.ScopeOrgId == scope && r.OriginalUserId == originalUserId)
            .WhereIF(exceptId.HasValue, r => r.Id != exceptId!.Value)
            .AnyAsync(cancellationToken);
        if (exists)
            throw Invalid("ruleSlotExists");
    }

    /// <summary>
    /// 锁定机构锚点行直到当前规则事务提交，串行化同一 scope 的规则图读写。
    /// 规则环检测不能只依赖应用内存锁；多副本必须共享数据库锁。
    /// </summary>
    protected virtual async Task LockScopeAsync(long scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var affected = await rules.Db.Ado.ExecuteCommandAsync(
            "UPDATE sys_org SET Code = Code WHERE Id = @scope",
            new SugarParameter("@scope", scope));
        if (affected != 1)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationScopeDenied,
                new Dictionary<string, object?> { ["scopeOrgId"] = scope });
    }

    protected virtual async Task EnsureNoCycleAsync(
        long scope,
        long originalUserId,
        long delegateUserId,
        long? exceptId,
        CancellationToken cancellationToken)
    {
        if (originalUserId == delegateUserId)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationCycle);

        var edges = await rules.AsQueryable()
            .Where(r => r.ScopeOrgId == scope && r.Enabled)
            .WhereIF(exceptId.HasValue, r => r.Id != exceptId!.Value)
            .Select(r => new { r.OriginalUserId, r.DelegateUserId })
            .ToListAsync(cancellationToken);
        edges.Add(new { OriginalUserId = originalUserId, DelegateUserId = delegateUserId });
        var map = edges.ToDictionary(e => e.OriginalUserId, e => e.DelegateUserId);
        foreach (var start in map.Keys)
        {
            var seen = new HashSet<long>();
            var cursor = start;
            while (map.TryGetValue(cursor, out var next))
            {
                if (!seen.Add(cursor))
                    throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationCycle,
                        new Dictionary<string, object?> { ["userId"] = cursor });
                cursor = next;
            }
        }
    }

    protected virtual async Task AppendHistoryAsync(
        WfDelegationRule rule,
        WfDelegationRuleChangeType changeType,
        string? requestId,
        CancellationToken cancellationToken) =>
        await history.InsertAsync(new WfDelegationRuleHistory
        {
            RuleId = rule.Id,
            ScopeOrgId = rule.ScopeOrgId,
            OriginalUserId = rule.OriginalUserId,
            DelegateUserId = rule.DelegateUserId,
            Enabled = rule.Enabled,
            StartsAt = rule.StartsAt,
            EndsAt = rule.EndsAt,
            ChangeType = changeType,
            ActorUserId = currentUser.UserId!.Value,
            RequestId = WfWriteCmd.NormalizeRequestId(requestId),
        });

    protected virtual async Task<PagedList<WfDelegationRuleOutput>> MapPageAsync(
        PagedList<WfDelegationRule> page,
        CancellationToken cancellationToken)
    {
        var ids = page.Items.SelectMany(r => new[] { r.OriginalUserId, r.DelegateUserId }).Distinct().ToList();
        var names = ids.Count == 0
            ? []
            : await users.AsQueryable().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Name }).ToListAsync(cancellationToken);
        var map = names.ToDictionary(x => x.Id, x => x.Name);
        return new PagedList<WfDelegationRuleOutput>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = page.Items.Select(r => ToOutput(r, map)).ToList(),
        };
    }

    protected virtual async Task<WfDelegationRuleOutput> ToOutputAsync(
        WfDelegationRule rule,
        CancellationToken cancellationToken)
    {
        var names = await users.AsQueryable()
            .Where(u => u.Id == rule.OriginalUserId || u.Id == rule.DelegateUserId)
            .Select(u => new { u.Id, u.Name })
            .ToListAsync(cancellationToken);
        return ToOutput(rule, names.ToDictionary(x => x.Id, x => x.Name));
    }

    private static WfDelegationRuleOutput ToOutput(WfDelegationRule rule, IReadOnlyDictionary<long, string> names) => new()
    {
        Id = rule.Id,
        ScopeOrgId = rule.ScopeOrgId,
        OriginalUserId = rule.OriginalUserId,
        DelegateUserId = rule.DelegateUserId,
        OriginalUserName = names.GetValueOrDefault(rule.OriginalUserId),
        DelegateUserName = names.GetValueOrDefault(rule.DelegateUserId),
        Enabled = rule.Enabled,
        StartsAt = rule.StartsAt,
        EndsAt = rule.EndsAt,
        Version = rule.Version,
    };

    private async Task<TransactionResult<T>> InTransactionAsync<T>(
        WfOperationIdentity? identity,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        // 委托环检测必须在取得 scope 锁后看到最新提交;MySQL 默认 RepeatableRead 会复用锁前的快照。
        await rules.Db.Ado.BeginTranAsync(System.Data.IsolationLevel.ReadCommitted);
        try
        {
            if (identity is not null)
            {
                var hit = await receipts.TryBeginAsync(identity, cancellationToken);
                if (hit is not null)
                {
                    if (identity.PayloadHash is not null && hit.PayloadHash != identity.PayloadHash)
                        throw WorkflowErrorCode.Exception(WorkflowErrorCode.RequestPayloadConflict);

                    var replay = new TransactionResult<T>(DeserializeReceiptResult<T>(hit), true);
                    await rules.Db.Ado.CommitTranAsync();
                    return replay;
                }
            }

            var result = await action();
            if (identity is not null)
                await receipts.CommitAsync(
                    identity,
                    0,
                    JsonSerializer.Serialize(result, WfModelJson.Options),
                    cancellationToken);
            await rules.Db.Ado.CommitTranAsync();
            return new(result, false);
        }
        catch
        {
            await rules.Db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task<WfOperationIdentity?> FindAddReplayIdentityAsync(
        long originalUserId,
        string? requestId,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorUserId)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DelegationScopeDenied);
        var requestKey = RequireRequestId(requestId);
        var receipt = await rules.Db.Queryable<WfOperationReceipt>()
            .Where(r => r.CommandType == WfCommandType.DelegationRuleAdd
                        && r.TargetType == WfTargetType.DelegationRule
                        && r.TargetId == originalUserId
                        && r.ActorUserId == actorUserId
                        && r.RequestKey == requestKey)
            .OrderBy(r => r.Id, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        if (receipt is null) return null;
        if (!long.TryParse(receipt.ScopeKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var receiptScope)
            || receiptScope <= 0)
            throw ReceiptError(receipt, "receiptScopeInvalid");

        var identity = WfOperationIdentity.Create(
            receipt.ScopeKey,
            WfCommandType.DelegationRuleAdd,
            WfTargetType.DelegationRule,
            receipt.TargetId,
            receipt.ActorUserId,
            receipt.RequestKey,
            payloadHash);
        if (!string.Equals(identity.IdentityHash, receipt.IdentityHash, StringComparison.Ordinal))
            throw ReceiptError(receipt, "receiptIdentityInvalid");
        return identity;
    }

    private static T DeserializeReceiptResult<T>(WfOperationReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.ResultJson))
            throw ReceiptError(receipt, "receiptResultMissing");
        try
        {
            return JsonSerializer.Deserialize<T>(receipt.ResultJson, WfModelJson.Options)
                ?? throw ReceiptError(receipt, "receiptResultMissing");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw ReceiptError(receipt, "receiptResultInvalid");
        }
    }

    private static AdminException ReceiptError(WfOperationReceipt receipt, string reason) =>
        WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
            new Dictionary<string, object?>
            {
                ["reason"] = reason,
                ["identityHash"] = receipt.IdentityHash,
            });

    private sealed record TransactionResult<T>(T Data, bool Replayed);

    private sealed record DelegationUpdateResult(
        WfDelegationRuleOutput Output,
        WfDelegationRuleChangeType ChangeType);

    private sealed record DelegationChangeResult(
        WfDelegationRuleOutput Output,
        WfDelegationRuleChangeType ChangeType);

    protected virtual async Task NotifyRuleChangedAsync(
        WfDelegationRuleOutput rule,
        WfDelegationRuleChangeType changeType,
        CancellationToken cancellationToken)
    {
        try
        {
            await notifier.RuleChangedAsync(rule, changeType, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "工作流长期委托规则已提交，但变更通知失败: RuleId={RuleId}, ChangeType={ChangeType}",
                rule.Id,
                changeType);
        }
    }

    private static WfOperationIdentity CreateIdentity(
        long scope,
        WfCommandType command,
        long targetId,
        string? requestId,
        string payloadHash,
        long actorUserId)
    {
        var key = RequireRequestId(requestId);
        return WfOperationIdentity.Create(
            scope.ToString(CultureInfo.InvariantCulture),
            command,
            WfTargetType.DelegationRule,
            targetId,
            actorUserId,
            key,
            payloadHash);
    }

    private static string PayloadHash(WfDelegationRuleInput input) =>
        WfIdentityHash.ComputePayloadHash(
            input.DelegateUserId,
            string.Join('|', input.OriginalUserId, input.Enabled, input.StartsAt.ToUniversalTime().Ticks, input.EndsAt.ToUniversalTime().Ticks));

    private static void ValidateRequest(string? requestId) => _ = RequireRequestId(requestId);

    private static string RequireRequestId(string? requestId) =>
        WfWriteCmd.NormalizeRequestId(requestId)
        ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.RequestIdInvalid);

    private static void ValidateWindow(WfDelegationRuleInput input)
    {
        if (input.StartsAt.ToUniversalTime() >= input.EndsAt.ToUniversalTime())
            throw Invalid("windowInvalid");
    }

    private static WfDelegationRuleInput NormalizeInput(WfDelegationRuleInput input) => input with
    {
        StartsAt = input.StartsAt.ToUniversalTime(),
        EndsAt = input.EndsAt.ToUniversalTime(),
    };

    private static AdminException Invalid(string reason) => WorkflowErrorCode.Exception(
        WorkflowErrorCode.DelegationRuleInvalid,
        new Dictionary<string, object?> { ["reason"] = reason });

    private static AdminException Conflict(long id) => WorkflowErrorCode.Exception(
        WorkflowErrorCode.DelegationRuleConflict,
        new Dictionary<string, object?> { ["id"] = id });
}

/// <summary>默认委托通知:提交成功后让规则双方刷新。</summary>
public class WfDelegationNotifier(IRealtimePublisher realtimePublisher) : IWfDelegationNotifier
{
    public virtual async Task RuleChangedAsync(
        WfDelegationRuleOutput rule,
        WfDelegationRuleChangeType changeType,
        CancellationToken cancellationToken = default)
    {
        var data = new { rule.Id, rule.ScopeOrgId, rule.Enabled, changeType = changeType.ToString() };
        foreach (var userId in new[] { rule.OriginalUserId, rule.DelegateUserId }.Distinct())
            await realtimePublisher.NotifyUserAsync(userId, "workflow-delegation-rule-changed", data, cancellationToken);
    }
}
