using System.Reflection;
using SqlSugar;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary><see cref="IWorkflowNodeHandler"/> SPI 与 <see cref="WfNodeExecutionContext"/>/<see cref="WfNodeExecutionResult"/> 的契约测试(M3a-1 Task 2)。</summary>
public class WfNodeHandlerContractTests
{
    [Fact]
    public void Context_exposes_no_sqlsugar_entity_or_session()
    {
        var props = typeof(WfNodeExecutionContext).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(props);

        foreach (var prop in props)
        {
            var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            Assert.False(
                type.IsSubclassOf(typeof(PrimaryId)),
                $"{prop.Name} ({type.FullName}) 派生自 PrimaryId,不该出现在执行上下文里。");
            Assert.False(
                typeof(ISqlSugarClient).IsAssignableFrom(type),
                $"{prop.Name} ({type.FullName}) 可赋给 ISqlSugarClient,不该出现在执行上下文里。");
            Assert.False(
                type.Namespace is not null && type.Namespace.StartsWith("SqlSugar", StringComparison.Ordinal),
                $"{prop.Name} ({type.FullName}) 命名空间以 SqlSugar 开头,不该出现在执行上下文里。");
        }
    }

    [Fact]
    public void Result_factories_set_the_matching_result_type()
    {
        var succeeded = WfNodeExecutionResult.Succeeded();
        Assert.Equal(WfNodeExecutionResultType.Succeeded, succeeded.Type);
        Assert.Null(succeeded.ErrorCode);
        Assert.Null(succeeded.RetryAfter);

        var retryable = WfNodeExecutionResult.RetryableFailure();
        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, retryable.Type);

        var manualFallback = WfNodeExecutionResult.ManualFallback();
        Assert.Equal(WfNodeExecutionResultType.ManualFallback, manualFallback.Type);

        var terminalFailure = WfNodeExecutionResult.TerminalFailure();
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, terminalFailure.Type);
    }

    [Fact]
    public void ResultType_numeric_values_are_pinned()
    {
        Assert.Equal(1, (int)WfNodeExecutionResultType.Succeeded);
        Assert.Equal(2, (int)WfNodeExecutionResultType.RetryableFailure);
        Assert.Equal(3, (int)WfNodeExecutionResultType.ManualFallback);
        Assert.Equal(4, (int)WfNodeExecutionResultType.TerminalFailure);

        var values = Enum.GetValues<WfNodeExecutionResultType>().Cast<int>();
        Assert.DoesNotContain(0, values);
    }

    [Fact]
    public void Result_has_no_public_constructor()
    {
        var ctors = typeof(WfNodeExecutionResult).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(ctors);
    }

    [Theory]
    [InlineData(WfNodeExecutionResultType.Succeeded)]
    [InlineData(WfNodeExecutionResultType.RetryableFailure)]
    [InlineData(WfNodeExecutionResultType.ManualFallback)]
    [InlineData(WfNodeExecutionResultType.TerminalFailure)]
    public async Task FakeNodeHandler_returns_the_configured_result(WfNodeExecutionResultType type)
    {
        var expected = type switch
        {
            WfNodeExecutionResultType.Succeeded => WfNodeExecutionResult.Succeeded(summary: "ok"),
            WfNodeExecutionResultType.RetryableFailure => WfNodeExecutionResult.RetryableFailure(summary: "retry"),
            WfNodeExecutionResultType.ManualFallback => WfNodeExecutionResult.ManualFallback(summary: "fallback"),
            WfNodeExecutionResultType.TerminalFailure => WfNodeExecutionResult.TerminalFailure(summary: "dead"),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        var handler = new FakeNodeHandler(expected);

        var actual = await handler.ExecuteAsync(BuildContext(), CancellationToken.None);

        Assert.Same(expected, actual);
        Assert.Equal(type, actual.Type);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FakeNodeHandler_throws_when_token_already_cancelled()
    {
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.ExecuteAsync(BuildContext(), cts.Token));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Handler_node_type_matches_by_enum()
    {
        IEnumerable<IWorkflowNodeHandler> handlers =
        [
            new FakeNodeHandler(WfNodeExecutionResult.Succeeded(), WfNodeType.Webhook),
            new FakeNodeHandler(WfNodeExecutionResult.Succeeded(), WfNodeType.Approval),
        ];

        var matched = handlers.FirstOrDefault(h => h.NodeType == WfNodeType.Approval);
        Assert.NotNull(matched);

        var unmatched = handlers.FirstOrDefault(h => h.NodeType == WfNodeType.Branch);
        Assert.Null(unmatched);
    }

    [Fact]
    public void Context_deadline_is_absolute_utc()
    {
        var deadlineProp = typeof(WfNodeExecutionContext).GetProperty(nameof(WfNodeExecutionContext.DeadlineAtUtc))!;
        Assert.Equal(typeof(DateTimeOffset), deadlineProp.PropertyType);

        var ctx = BuildContext(DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(TimeSpan.Zero, ctx.DeadlineAtUtc.Offset);
    }

    [Fact]
    public void Context_variables_json_is_raw_passthrough()
    {
        var ctx = BuildContext(variablesJson: "{not valid json");
        Assert.Equal("{not valid json", ctx.VariablesJson);
    }

    private static WfNodeExecutionContext BuildContext(DateTimeOffset? deadline = null, string? variablesJson = null) => new()
    {
        ExecutionKey = "exec-1",
        InstanceId = 1,
        TokenId = 2,
        NodeId = "ap1",
        NodeType = WfNodeType.Webhook,
        DefinitionVersionId = 3,
        StarterUserId = 4,
        Attempt = 1,
        DeadlineAtUtc = deadline ?? DateTimeOffset.UtcNow.AddMinutes(5),
        VariablesJson = variablesJson,
    };
}
