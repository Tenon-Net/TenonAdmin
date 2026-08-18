using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置引擎:一条 Cmd → 一个 DB 事务 → Agenda 循环直至空。
/// 方法拆成 <c>virtual</c> 小步,消费者可继承覆写单步或前置 <c>TryAdd</c> 整体替换。
/// </summary>
public class WorkflowEngine(
    IRepository<WfInstance> instances,
    IApproverResolver approverResolver,
    IWorkflowFormBinder formBinder,
    WorkflowOptions options,
    TimeProvider timeProvider) : IWorkflowEngine
{
    /// <inheritdoc />
    public virtual async Task<WfEngineResult> ExecuteAsync(
        IWfCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var db = instances.Db;
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var ctx = command switch
            {
                StartInstanceCmd start => await BeginStartAsync(db, start, cancellationToken),
                CompleteTaskCmd complete => await BeginCompleteAsync(db, complete, cancellationToken),
                TransferTaskCmd transfer => await BeginTransferAsync(db, transfer, cancellationToken),
                _ => throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
                    new Dictionary<string, object?> { ["command"] = command.GetType().Name }),
            };

            await RunAgendaAsync(ctx, cancellationToken);
            return ctx.ToResult();
        });

        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);

        return tran.Data;
    }

    /// <summary>Agenda 出队循环;某 Op 不再 plan 则自然停(等人审批)。</summary>
    protected virtual async Task RunAgendaAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        while (ctx.Agenda.TryTake(out var op))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await op.ExecuteAsync(ctx, cancellationToken);
        }
    }

    /// <summary>发起:校验挂载点 → 插 instance/token → 历史 → 入队 EnterNode(start)。</summary>
    protected virtual async Task<WfExecutionContext> BeginStartAsync(
        ISqlSugarClient db,
        StartInstanceCmd cmd,
        CancellationToken cancellationToken)
    {
        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == cmd.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);
        if (model.Root.Type != WfNodeType.Start)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "rootNotStart" });
        if (string.IsNullOrWhiteSpace(model.Root.Id))
            model.Root.Id = "start";

        await formBinder.ValidateOnStartAsync(
            new WfFormBindContext
            {
                InstanceId = 0,
                DefinitionVersionId = cmd.DefinitionVersionId,
                BusinessKey = cmd.BusinessKey,
                VariablesJson = cmd.VariablesJson,
                Status = WfInstanceStatus.Running,
                StarterUserId = cmd.StarterUserId,
            },
            cancellationToken);

        var instance = new WfInstance
        {
            DefinitionVersionId = cmd.DefinitionVersionId,
            BusinessKey = cmd.BusinessKey,
            StarterUserId = cmd.StarterUserId,
            Status = WfInstanceStatus.Running,
            VariablesJson = cmd.VariablesJson,
            SelectedUserIdsJson = cmd.SelectedUserIdsByNode is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(cmd.SelectedUserIdsByNode, WfModelJson.Options),
        };
        await db.Insertable(instance).ExecuteCommandAsync();

        var token = new WfToken
        {
            InstanceId = instance.Id,
            NodeId = model.Root.Id,
            Status = WfTokenStatus.Active,
        };
        await db.Insertable(token).ExecuteCommandAsync();

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = cmd.SelectedUserIdsByNode
                                    ?? new Dictionary<string, List<long>>(StringComparer.Ordinal),
            StarterOrgId = cmd.StarterOrgId,
        };

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.InstanceStarted,
            model.Root.Id,
            new { starterUserId = cmd.StarterUserId, businessKey = cmd.BusinessKey },
            cancellationToken);

        agenda.Plan(new EnterNodeOp(model.Root));
        return ctx;
    }

    /// <summary>完成待办:加载运行态 → 入队 CompleteTaskOp。</summary>
    protected virtual async Task<WfExecutionContext> BeginCompleteAsync(
        ISqlSugarClient db,
        CompleteTaskCmd cmd,
        CancellationToken cancellationToken)
    {
        var task = await db.Queryable<WfTask>().Where(t => t.Id == cmd.TaskId).FirstAsync();
        if (task is null)
        {
            var completed = await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.TaskId == cmd.TaskId);
            throw WorkflowErrorCode.Exception(
                completed ? WorkflowErrorCode.TaskConflict : WorkflowErrorCode.TaskNotFound);
        }

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == task.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == task.TokenId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        long? starterOrgId = null;
        var starter = await db.Queryable<TenonAdmin.Services.SysUser>()
            .Where(u => u.Id == instance.StarterUserId)
            .FirstAsync();
        if (starter is not null)
            starterOrgId = starter.OrgId;

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            StarterOrgId = starterOrgId,
        };

        agenda.Plan(new CompleteTaskOp(task, cmd.UserId, cmd.Action, cmd.Comment));
        return ctx;
    }

    /// <summary>转办:加载运行态 → 入队 TransferTaskOp(不推进 token)。</summary>
    protected virtual async Task<WfExecutionContext> BeginTransferAsync(
        ISqlSugarClient db,
        TransferTaskCmd cmd,
        CancellationToken cancellationToken)
    {
        var task = await db.Queryable<WfTask>().Where(t => t.Id == cmd.TaskId).FirstAsync();
        if (task is null)
        {
            var completed = await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.TaskId == cmd.TaskId);
            throw WorkflowErrorCode.Exception(
                completed ? WorkflowErrorCode.TaskConflict : WorkflowErrorCode.TaskNotFound);
        }

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == task.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == task.TokenId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
        };

        agenda.Plan(new TransferTaskOp(task, cmd.UserId, cmd.ToUserId, cmd.Comment));
        return ctx;
    }

    protected virtual IReadOnlyDictionary<string, List<long>> DeserializeSelectedUsers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, List<long>>(StringComparer.Ordinal);
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<long>>>(
                       json, WfModelJson.Options)
                   ?? new Dictionary<string, List<long>>(StringComparer.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "selectedUsersInvalid" });
        }
    }
}
