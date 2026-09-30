using TenonAdmin.Integration;

namespace TenonAdmin.Tests;

/// <summary>可靠投递迁移规则(纯函数)的矩阵:结果未知按对方能力分流、人工操作可用性、退避与上限。</summary>
public class IntegrationDeliveryRulesTests
{
    private static readonly IntegrationDeliveryOptions Options = new();
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DeliveryAdapterCapabilities Dedupe = new(true, true, true);
    private static readonly DeliveryAdapterCapabilities QueryOnly = new(true, false, true);
    private static readonly DeliveryAdapterCapabilities Neither = new(true, false, false);

    private static IntegrationDelivery Row(DeliveryStatus status = DeliveryStatus.Dispatching, int attempts = 1, bool verify = false) => new()
    {
        Id = 1, DeliveryKey = "k", Adapter = "a", Operation = "op", Status = status, AttemptCount = attempts, MaxAttempts = 8,
        NextAttemptAtUtc = Now, DeadlineAtUtc = Now.AddHours(24), VerifyBeforeSend = verify,
    };

    [Fact]
    public void Unknown_results_are_never_blindly_resent()
    {
        var unknown = new DeliverySendResult(OutboundOutcome.Unknown, Reason: "timeout");
        var viaDedupe = DeliveryStateMachine.AfterSend(Row(), Dedupe, unknown, Now, Options);
        Assert.Equal(DeliveryStatus.Pending, viaDedupe.Status);
        Assert.False(viaDedupe.VerifyBeforeSend);

        var viaQuery = DeliveryStateMachine.AfterSend(Row(), QueryOnly, unknown, Now, Options);
        Assert.Equal(DeliveryStatus.Pending, viaQuery.Status);
        Assert.True(viaQuery.VerifyBeforeSend);   // 先查询再决定

        var viaNeither = DeliveryStateMachine.AfterSend(Row(), Neither, unknown, Now, Options);
        Assert.Equal(DeliveryStatus.NeedsReconciliation, viaNeither.Status);
        Assert.True(viaNeither.NeedsAttention);

        var cancelled = DeliveryStateMachine.AfterSend(Row(), Neither, new DeliverySendResult(OutboundOutcome.Cancelled), Now, Options);
        Assert.Equal(DeliveryStatus.NeedsReconciliation, cancelled.Status);   // 取消等同结果未知
    }

    [Theory]
    [InlineData(OutboundOutcome.Succeeded, DeliveryStatus.Succeeded)]
    [InlineData(OutboundOutcome.Accepted, DeliveryStatus.AwaitingConfirmation)]
    [InlineData(OutboundOutcome.AuthenticationFailed, DeliveryStatus.Failed)]
    [InlineData(OutboundOutcome.Rejected, DeliveryStatus.Failed)]
    [InlineData(OutboundOutcome.NotSent, DeliveryStatus.Exhausted)]   // 未声明暂时性:按配置类失败,不自动重试
    public void Definite_results_map_to_the_contract_states(OutboundOutcome outcome, DeliveryStatus expected)
    {
        var transition = DeliveryStateMachine.AfterSend(Row(), Neither, new DeliverySendResult(outcome), Now, Options);
        Assert.Equal(expected, transition.Status);
        Assert.Equal(expected == DeliveryStatus.Succeeded, transition.CompletedAtUtc is not null);
    }

    [Fact]
    public void Accepted_waits_by_polling_or_until_the_confirmation_deadline()
    {
        var polled = DeliveryStateMachine.AfterSend(Row(), QueryOnly, new DeliverySendResult(OutboundOutcome.Accepted, "R1"), Now, Options);
        Assert.Equal(Now.AddSeconds(60), polled.NextAttemptAtUtc);
        Assert.Equal(Now.AddHours(72), polled.ConfirmDeadlineAtUtc);
        Assert.Equal("R1", polled.RemoteReference);

        var callbackOnly = DeliveryStateMachine.AfterSend(Row(), Neither, new DeliverySendResult(OutboundOutcome.Accepted), Now, Options);
        Assert.Equal(callbackOnly.ConfirmDeadlineAtUtc, callbackOnly.NextAttemptAtUtc);
        Assert.Null(callbackOnly.CompletedAtUtc);
    }

    [Fact]
    public void A_new_send_cycle_or_a_fresh_acceptance_never_reuses_an_old_confirmation_window()
    {
        var stale = Now.AddHours(-1);
        var reconciled = Row(DeliveryStatus.NeedsReconciliation);
        reconciled.ConfirmDeadlineAtUtc = stale;
        reconciled.RemoteReference = "OLD-1";

        // 人工重发/确认未执行开始新一轮发送:清掉上一轮的确认时限与对方引用
        var reset = DeliveryStateMachine.ResetBudget(DeliveryTransition.From(reconciled), reconciled, Now, Options);
        Assert.Null(reset.ConfirmDeadlineAtUtc);
        Assert.Null(reset.RemoteReference);

        // 发送被受理、人工查询得知已受理:一律新开确认窗口
        var accepted = new DeliveryQueryResult(RemoteDeliveryState.Accepted);
        var resent = Row();
        resent.ConfirmDeadlineAtUtc = stale;
        Assert.Equal(Now.AddHours(72),
            DeliveryStateMachine.AfterSend(resent, QueryOnly, new DeliverySendResult(OutboundOutcome.Accepted), Now, Options).ConfirmDeadlineAtUtc);
        Assert.Equal(Now.AddHours(72),
            DeliveryStateMachine.AfterQuery(reconciled, QueryOnly, accepted, DeliveryQueryPhase.Manual, Now, Options)!.ConfirmDeadlineAtUtc);

        // 自动查询沿用本轮已有的时限,没有才新开
        var polling = Row(DeliveryStatus.AwaitingConfirmation);
        polling.ConfirmDeadlineAtUtc = Now.AddHours(5);
        Assert.Equal(Now.AddHours(5),
            DeliveryStateMachine.AfterQuery(polling, QueryOnly, accepted, DeliveryQueryPhase.ConfirmPoll, Now, Options)!.ConfirmDeadlineAtUtc);
        Assert.Equal(Now.AddHours(72),
            DeliveryStateMachine.AfterQuery(Row(verify: true), QueryOnly, accepted, DeliveryQueryPhase.Verify, Now, Options)!.ConfirmDeadlineAtUtc);
    }

    [Fact]
    public void Backoff_doubles_is_capped_and_honours_retry_after()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), DeliveryStateMachine.Backoff(1, null, Options));
        Assert.Equal(TimeSpan.FromSeconds(60), DeliveryStateMachine.Backoff(2, null, Options));
        Assert.Equal(TimeSpan.FromSeconds(240), DeliveryStateMachine.Backoff(4, null, Options));
        Assert.Equal(TimeSpan.FromHours(1), DeliveryStateMachine.Backoff(20, null, Options));
        Assert.Equal(TimeSpan.FromMinutes(10), DeliveryStateMachine.Backoff(1, TimeSpan.FromMinutes(10), Options));
        Assert.Equal(TimeSpan.FromHours(24), DeliveryStateMachine.Backoff(1, TimeSpan.FromDays(3), Options));

        var last = DeliveryStateMachine.AfterSend(Row(attempts: 8), Dedupe, new DeliverySendResult(OutboundOutcome.NotSent, Transient: true), Now, Options);
        Assert.Equal(DeliveryStatus.Exhausted, last.Status);
        Assert.Equal("max_attempts", last.Reason);
    }

    [Fact]
    public void Not_sent_is_retried_automatically_only_when_transient()
    {
        // 暂时性(网络、限流):按退避回到待处理
        var transient = DeliveryStateMachine.AfterSend(Row(), Neither,
            new DeliverySendResult(OutboundOutcome.NotSent, Reason: "connection_failed", Transient: true), Now, Options);
        Assert.Equal(DeliveryStatus.Pending, transient.Status);
        Assert.Equal(Now.AddSeconds(30), transient.NextAttemptAtUtc);

        // 非暂时性(凭据缺失、目标被拦截):重试也不会好,第一次就耗尽并告警;什么都没发出,人工修好后可直接重试
        var permanent = DeliveryStateMachine.AfterSend(Row(), Dedupe,
            new DeliverySendResult(OutboundOutcome.NotSent, Reason: "credential_missing", ErrorSummary: "出站目标 partner 未配置凭据。"), Now, Options);
        Assert.Equal(DeliveryStatus.Exhausted, permanent.Status);
        Assert.Equal("credential_missing", permanent.Reason);
        Assert.StartsWith("credential_missing", permanent.LastError);
        Assert.True(permanent.NeedsAttention);
        Assert.Contains(DeliveryActions.Retry, DeliveryStateMachine.AllowedActions(Row(DeliveryStatus.Exhausted), Neither, hasUnsafeUnknown: false));

        // 出站结果的暂时性原样带进投递结果
        Assert.True(DeliverySendResult.From(new OutboundResponse { CallId = "c", Target = "t", Outcome = OutboundOutcome.NotSent, Transient = true }).Transient);
        Assert.False(DeliverySendResult.From(new OutboundResponse { CallId = "c", Target = "t", Outcome = OutboundOutcome.NotSent }).Transient);
    }

    [Fact]
    public void Query_results_follow_the_remote_truth_by_phase()
    {
        DeliveryTransition? After(RemoteDeliveryState state, DeliveryQueryPhase phase, DeliveryStatus status = DeliveryStatus.Pending) =>
            DeliveryStateMachine.AfterQuery(Row(status, verify: true), QueryOnly, new DeliveryQueryResult(state), phase, Now, Options);

        Assert.Equal(DeliveryStatus.Succeeded, After(RemoteDeliveryState.Succeeded, DeliveryQueryPhase.Verify)!.Status);
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, After(RemoteDeliveryState.Accepted, DeliveryQueryPhase.Verify)!.Status);
        Assert.Equal(DeliveryStatus.Failed, After(RemoteDeliveryState.Failed, DeliveryQueryPhase.Recovery)!.Status);
        var notFound = After(RemoteDeliveryState.NotFound, DeliveryQueryPhase.Verify)!;
        Assert.Equal(DeliveryStatus.Pending, notFound.Status);   // 对方没有记录:可安全发送
        Assert.False(notFound.VerifyBeforeSend);
        Assert.Equal(DeliveryStatus.NeedsReconciliation,
            After(RemoteDeliveryState.NotFound, DeliveryQueryPhase.ConfirmPoll, DeliveryStatus.AwaitingConfirmation)!.Status);   // 受理过却查不到
        var retryQuery = After(RemoteDeliveryState.Unknown, DeliveryQueryPhase.Verify)!;
        Assert.True(retryQuery.VerifyBeforeSend);
        Assert.Null(After(RemoteDeliveryState.Unknown, DeliveryQueryPhase.Manual, DeliveryStatus.NeedsReconciliation));   // 手动查询失败不改状态
    }

    [Fact]
    public void Allowed_actions_are_decided_by_state_capability_and_history()
    {
        string[] Actions(DeliveryStatus status, DeliveryAdapterCapabilities caps, bool unsafeUnknown = false, bool verify = false) =>
            [.. DeliveryStateMachine.AllowedActions(Row(status, verify: verify), caps, unsafeUnknown)];

        Assert.Empty(Actions(DeliveryStatus.Dispatching, Dedupe));
        Assert.Empty(Actions(DeliveryStatus.Succeeded, Dedupe));
        Assert.Empty(Actions(DeliveryStatus.Cancelled, Dedupe));
        Assert.Equal([DeliveryActions.Cancel], Actions(DeliveryStatus.Pending, Dedupe));
        Assert.Equal([DeliveryActions.Query, DeliveryActions.ConfirmSucceeded, DeliveryActions.Cancel], Actions(DeliveryStatus.AwaitingConfirmation, QueryOnly));
        Assert.Equal([DeliveryActions.ConfirmSucceeded, DeliveryActions.ConfirmNotExecuted, DeliveryActions.Cancel], Actions(DeliveryStatus.NeedsReconciliation, Neither));
        Assert.Equal([DeliveryActions.Retry, DeliveryActions.Cancel], Actions(DeliveryStatus.Failed, Neither));

        // 耗尽:只有全部尝试都可安全重发时才允许重试
        Assert.Contains(DeliveryActions.Retry, Actions(DeliveryStatus.Exhausted, Neither, unsafeUnknown: false));
        Assert.DoesNotContain(DeliveryActions.Retry, Actions(DeliveryStatus.Exhausted, Neither, unsafeUnknown: true));
        Assert.Contains(DeliveryActions.Retry, Actions(DeliveryStatus.Exhausted, Dedupe, unsafeUnknown: true));   // 对方去重,重发安全
        Assert.DoesNotContain(DeliveryActions.Retry, Actions(DeliveryStatus.Exhausted, QueryOnly, verify: true));

        // 适配器已移除:不能再发送或查询,只能确认或关闭
        Assert.Equal([DeliveryActions.ConfirmSucceeded, DeliveryActions.Cancel], Actions(DeliveryStatus.NeedsReconciliation, DeliveryAdapterCapabilities.Missing));
    }
}
