namespace TenonAdmin.Workflow;

/// <summary>
/// 自动节点执行失败后的人工兜底(M3a-1 Task 6,<c>WfNodeExecutionResultType.ManualFallback</c> 分支唯一 Op)。
/// <para><b>复用 <see cref="EnterNodeOp.CreateTaskAsync"/>,但绕开 <see cref="EnterNodeOp.EnterApprovalAsync"/>
/// 与它的三条自动放行出口</b>(<see cref="EnterNodeOp.ApplyNobodyAsync"/> 默认 <c>autoPass</c> / 解析出 0 人 /
/// 去重后 0 人剩余):建人工待办不是插一行 <c>wf_task</c>——它同时要建 <c>wf_task_actor</c>(含顺序会签的
/// <c>ActivatedTime</c> 规则)、写 <c>TaskCreated</c> 历史、算 <c>DueTime</c>、把 <c>NodeVisitId</c>/<c>TokenId</c>
/// 从 token 拷过来、把「待办到达」通知排进提交后派发,复用 <see cref="EnterNodeOp.CreateTaskAsync"/> 就把这六件
/// 事的一致性一并拿到,自己重写等于再维护一份。但 <see cref="EnterNodeOp.EnterApprovalAsync"/> 的三条出口最终
/// 都会 <c>Plan(new TakeTransitionOp(Node))</c> 自动放行——「自动节点执行失败后自动放行」与语义契约 §4.7
/// 「任何异常全部转人工,不自动放行」正面冲突,是本里程碑最危险的一种静默 bug,所以本类刻意从更上一层进入,
/// 把三条出口全部换成「什么都不做」。</para>
/// <para><b>不 <c>override ExecuteAsync</c> 里调 <c>base.ExecuteAsync</c></b>:那会重新做 token 级 CAS、
/// 重新生成 <see cref="WfToken.NodeVisitId"/>、重写 <c>NodeEnter</c> 历史——这是「进入」节点的语义,而本
/// Op 表达的是「停在原地,原地建一件兜底待办」,token 本就没有离开过这个节点。</para>
/// <para><b>没配 <c>assignee</c>(或解析出 0 人)时不建任务,也不抛异常</b>:execution 仍落
/// <c>ManualFallback</c> 终态、attempt 与 outbox 照写,token 原地停住——「停住且可见」是诚实状态。抛异常会让
/// 整个回写事务回滚,execution 行退回 <c>Running</c>、租约到期后被重新领取、handler 再跑一次、再失败——
/// 无限活锁,这正是 <see cref="EnterNodeOp.ApplyNobodyAsync"/> 系的坑要避开的同一类问题。</para>
/// <para><c>internal sealed</c>:生产内没有第二个调用方(<see cref="WorkflowEngine.BeginNodeExecutionCompletedAsync"/>
/// 是唯一入口);要覆写的缝在被继承的 <see cref="EnterNodeOp.CreateTaskAsync"/> 上。</para>
/// </summary>
internal sealed class WfManualFallbackOp(WfNode node) : EnterNodeOp(node)
{
    public override async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ctx.CurrentNode = Node;

        var assignee = Node.Props?.Assignee;
        var providerKey = assignee?.Provider;
        if (string.IsNullOrWhiteSpace(providerKey))
            return; // 未配置办理人来源——不建任务、也不自动放行。

        var users = await ctx.ApproverResolver.ResolveAsync(
            providerKey,
            new ApproverResolveContext
            {
                InitiatorUserId = ctx.Instance.StarterUserId,
                InitiatorOrgId = ctx.StarterOrgId,
                Params = assignee?.Params,
                SelectedUserIds = ctx.GetSelectedUserIds(Node.Id),
                LeaderChainByLevel = ctx.LeaderChainByLevel,
            },
            cancellationToken);

        if (users.Count == 0)
            return; // 解析出 0 人——同上,不建任务、也不自动放行。

        await CreateTaskAsync(ctx, users, WfSignMode.Any, cancellationToken);
    }
}
