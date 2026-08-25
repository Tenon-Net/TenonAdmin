using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 工作流卫星包可替换性「八件套」——锁 <see cref="WorkflowSetup.AddTenonAdminWorkflow"/> 里
/// 八个 <c>TryAddScoped</c> 面:<see cref="IApproverResolver"/> /
/// <see cref="IWorkflowFormBinder"/> / <see cref="IWorkflowEngine"/> /
/// <see cref="IWfConditionEvaluator"/> /
/// <see cref="IWfDefinitionService"/> / <see cref="IWfTaskService"/> /
/// <see cref="IWfInstanceService"/> / <see cref="IWorkflowNotifier"/>。
/// <para>
/// 真判据是<strong>前置</strong>注册即胜出(裸容器,不走 <c>ConfigureTestServices</c>+Replace):
/// 把任一 <c>TryAdd</c> 退化成 <c>Add</c> → 内置后注册覆盖 → 对应本条红。
/// </para>
/// </summary>
public class WorkflowReplaceabilityTests
{
    /// <summary>变异:WorkflowSetup 里 IApproverResolver 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredApproverResolver_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IApproverResolver, FakeApproverResolver>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeApproverResolver>(scope.ServiceProvider.GetRequiredService<IApproverResolver>());
    }

    /// <summary>变异:WorkflowSetup 里 IWorkflowFormBinder 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredFormBinder_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWorkflowFormBinder, FakeFormBinder>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeFormBinder>(scope.ServiceProvider.GetRequiredService<IWorkflowFormBinder>());
    }

    /// <summary>变异:WorkflowSetup 里 IWorkflowEngine 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredEngine_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWorkflowEngine, FakeEngine>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeEngine>(scope.ServiceProvider.GetRequiredService<IWorkflowEngine>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfConditionEvaluator 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredConditionEvaluator_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfConditionEvaluator, FakeConditionEvaluator>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeConditionEvaluator>(
            scope.ServiceProvider.GetRequiredService<IWfConditionEvaluator>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfDefinitionService 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredDefinitionService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfDefinitionService, FakeDefinitionService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeDefinitionService>(scope.ServiceProvider.GetRequiredService<IWfDefinitionService>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfTaskService 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredTaskService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfTaskService, FakeTaskService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeTaskService>(scope.ServiceProvider.GetRequiredService<IWfTaskService>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfInstanceService 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredInstanceService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfInstanceService, FakeInstanceService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeInstanceService>(scope.ServiceProvider.GetRequiredService<IWfInstanceService>());
    }

    /// <summary>变异:WorkflowSetup 里 IWorkflowNotifier 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredWorkflowNotifier_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWorkflowNotifier, FakeWorkflowNotifier>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeWorkflowNotifier>(scope.ServiceProvider.GetRequiredService<IWorkflowNotifier>());
    }

    /// <summary>
    /// 消费者前置注册 → <c>AddTenonAdminWorkflow</c> → 解析断言。
    /// 不启宿主、不挂 SqlSugar:只验 DI 描述符,不解析内置实现(它们依赖仓储)。
    /// </summary>
    private static ServiceProvider BuildProvider(Action<IServiceCollection> preRegister)
    {
        var services = new ServiceCollection();
        preRegister(services);
        services.AddTenonAdminWorkflow();
        return services.BuildServiceProvider();
    }

    private sealed class FakeApproverResolver : IApproverResolver
    {
        public Task<IReadOnlyList<long>> ResolveAsync(
            string providerKey, ApproverResolveContext context, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeFormBinder : IWorkflowFormBinder
    {
        public Task ValidateOnStartAsync(WfFormBindContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task OnInstanceCompletedAsync(WfFormBindContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeEngine : IWorkflowEngine
    {
        public Task<WfEngineResult> ExecuteAsync(IWfCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeConditionEvaluator : IWfConditionEvaluator
    {
        public bool Evaluate(WfConditionExpr? expr, string? variablesJson) => false;
    }

    private sealed class FakeDefinitionService : IWfDefinitionService
    {
        public Task<PagedList<WfDefinition>> PageAsync(WfDefinitionPageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfDefinitionDetailOutput> GetAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<long> AddAsync(WfDefinitionInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task UpdateAsync(WfDefinitionInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<int> PublishAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DisableAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<WfDefinitionVersionOutput>> ListVersionsAsync(
            long definitionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeTaskService : IWfTaskService
    {
        public Task<PagedList<WfTodoItemOutput>> PageTodoAsync(
            long userId, WfTaskPageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<PagedList<WfDoneItemOutput>> PageDoneAsync(
            long userId, WfTaskPageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> ApproveAsync(
            long taskId, long userId, string? comment = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> RejectAsync(
            long taskId, long userId, string? comment = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> TransferAsync(
            long taskId, long userId, long toUserId, string? comment = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> DelegateAsync(
            long taskId, long userId, long toUserId, string? comment = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task UrgeAsync(
            long taskId, long callerUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> ReturnAsync(
            long taskId, long userId, string? targetNodeId, string? comment = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeInstanceService : IWfInstanceService
    {
        public Task<IReadOnlyList<WfStartableDefinitionOutput>> ListStartableAsync(
            long userId, long? orgId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfStartableDefinitionDetailOutput> GetStartableAsync(
            long definitionId, long userId, long? orgId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> StartAsync(
            WfStartInput input, long starterUserId, long? starterOrgId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<PagedList<WfInstanceListItemOutput>> PageMineAsync(
            long starterUserId, WfInstancePageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfInstanceDetailOutput> GetAsync(
            long instanceId, long currentUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<WfHistoryItemOutput>> ListHistoryAsync(
            long instanceId, long currentUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> CancelAsync(
            long instanceId, long callerUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> ResubmitAsync(
            long instanceId, long callerUserId, string? variablesJson,
            IReadOnlyDictionary<string, List<long>>? selectedUserIdsByNode,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeWorkflowNotifier : IWorkflowNotifier
    {
        public Task TaskAssignedAsync(
            WfNotifyContext ctx, IReadOnlyList<long> userIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task InstanceCompletedAsync(WfNotifyContext ctx, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task TaskUrgedAsync(
            WfNotifyContext ctx, long taskId, long? fromUserId, IReadOnlyList<long> toUserIds,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
