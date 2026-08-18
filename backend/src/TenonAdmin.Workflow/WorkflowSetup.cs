using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 工作流卫星包装配入口。不装本包 / 不调本方法 → 无工作流表、无相关端点(可选性定义)。
/// <para>
/// 接线两步(与 Excel/Redis 同属卫星包,但本包有实体与控制器,须额外挂 <see cref="TenonAdminOptions.ApplicationAssemblies"/>):
/// </para>
/// <list type="number">
/// <item><see cref="AddTenonAdminWorkflow"/> — <c>TryAdd</c> 注册 Options / SPI(可先于或后于 <c>AddTenonAdmin</c>;内核无同名服务)</item>
/// <item><see cref="UseWorkflow"/> — 在 <c>AddTenonAdmin(..., o =&gt; o.UseWorkflow())</c> 里调用,把本程序集并入 CodeFirst 与控制器 <c>AddApplicationPart</c></item>
/// </list>
/// </summary>
public static class WorkflowSetup
{
    /// <summary>
    /// 把工作流程序集挂入内核:<c>wf_*</c> 实体参与 CodeFirst 建表、控制器挂路由。
    /// 须在 <c>AddTenonAdmin</c> 的 configure 回调里调用(实体扫描与 ApplicationPart 只在那时发生)。
    /// </summary>
    public static TenonAdminOptions UseWorkflow(this TenonAdminOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var asm = typeof(WorkflowSetup).Assembly;
        if (!options.ApplicationAssemblies.Contains(asm))
            options.ApplicationAssemblies.Add(asm);
        return options;
    }

    /// <summary>
    /// 启用工作流 DI:<c>TryAdd</c> 注册 <see cref="WorkflowOptions"/> 与 SPI 占位(后续里程碑补齐引擎/服务/种子)。
    /// 消费者若要整体替换某 SPI,在本方法<strong>之前</strong>注册同接口即可胜出。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddTenonAdminWorkflow(builder.Configuration);
    /// builder.Services.AddTenonAdmin(builder.Configuration, o => o.UseWorkflow());
    /// </code>
    /// </example>
    public static IServiceCollection AddTenonAdminWorkflow(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = configuration?.GetSection("TenonAdmin:Workflow").Get<WorkflowOptions>()
                      ?? new WorkflowOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

        // 审批人 SPI:多实现按 Key 分发(对齐 IAdminJob + IJobHandlerResolver)。
        // 消费者可前置注册同 Key 的 IApproverProvider 覆盖内置,或前置 IApproverResolver 整体替换分发。
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApproverProvider, UserApproverProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApproverProvider, LeaderApproverProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApproverProvider, MultiLeaderApproverProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApproverProvider, RoleApproverProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApproverProvider, PositionApproverProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApproverProvider, SelfSelectApproverProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApproverProvider, InitiatorApproverProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApproverProvider, OrgLeaderApproverProvider>());
        services.TryAddScoped<IApproverResolver, DefaultApproverResolver>();

        // 引擎 + 表单挂载点(空操作默认);消费者前置同接口即可整体替换。
        // 发起校验 / 完结回写在引擎内调用;详情 API 透出 Model.FormComponent 供前端动态挂载。
        services.TryAddScoped<IWorkflowFormBinder, NoOpWorkflowFormBinder>();
        services.TryAddScoped<IWorkflowEngine, WorkflowEngine>();

        // 待办/已办 + 审批动词(同意/拒绝/转办)。
        services.TryAddScoped<IWfTaskService, WfTaskService>();

        // 定义 CRUD + 发布/版本。
        services.TryAddScoped<IWfDefinitionService, WfDefinitionService>();

        // 发起 / 我发起的 / 详情(含 FormBinder 挂载点) / 事件流。
        services.TryAddScoped<IWfInstanceService, WfInstanceService>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, WorkflowMenuSeed>());

        return services;
    }
}
