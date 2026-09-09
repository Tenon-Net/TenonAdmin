using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>M3b-0 AI Decision 审计表的四库共享持久化契约。</summary>
public class WfAiDecisionPersistenceContractTests
{
    [Fact]
    public async Task Audit_rows_enforce_attempt_identity_and_round_trip_safe_unicode_payloads()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        // 这里只压测跨方言 Unicode 大文本列，不把该容量 fixture 当作可通过业务 parser 的 proposal。
        var proposal = $$"""{"schemaVersion":"1.0","rationale":"{{new string('审', 9000)}}"}""";
        var first = NewDecision(proposal);
        await db.Insertable(first).ExecuteCommandAsync();

        var duplicate = NewDecision("""{"duplicate":"must-not-overwrite"}""");
        Exception? failure = null;
        try
        {
            await db.Insertable(duplicate).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await db.Insertable(NewDecision(proposal, first.ExecutionId, attemptNo: 2)).ExecuteCommandAsync();
        await db.Insertable(NewDecision(proposal, executionId: 1002, attemptNo: first.AttemptNo)).ExecuteCommandAsync();

        var failed = NewDecision(proposal: null, executionId: 1003, attemptNo: 1);
        failed.ProviderResultType = AiDecisionProviderResultType.Failed;
        failed.Provider = null;
        failed.Model = null;
        failed.InputHash = null;
        failed.PromptVersion = null;
        failed.ProposalSchemaVersion = null;
        failed.SchemaValid = null;
        failed.PolicyVersion = null;
        failed.PolicyClassification = null;
        failed.Recommendation = null;
        failed.Confidence = null;
        failed.RiskFlagsJson = null;
        failed.EvidenceRefsJson = null;
        failed.PromptTokens = null;
        failed.CompletionTokens = null;
        failed.TotalTokens = null;
        failed.FallbackReason = AiDecisionFallbackReason.ProviderFailure;
        await db.Insertable(failed).ExecuteCommandAsync();

        var loaded = await db.Queryable<WfAiDecision>()
            .Where(x => x.ExecutionId == first.ExecutionId && x.AttemptNo == first.AttemptNo)
            .FirstAsync();

        Assert.NotNull(failure);
        Assert.Equal(1, await db.Queryable<WfAiDecision>()
            .Where(x => x.ExecutionId == first.ExecutionId && x.AttemptNo == first.AttemptNo)
            .CountAsync());
        Assert.Equal(2, await db.Queryable<WfAiDecision>().Where(x => x.ExecutionId == first.ExecutionId).CountAsync());
        Assert.Equal(1, await db.Queryable<WfAiDecision>()
            .Where(x => x.ExecutionId == 1002 && x.AttemptNo == first.AttemptNo)
            .CountAsync());
        Assert.Equal(proposal, loaded.ProposalJson);
        Assert.DoesNotContain('?', loaded.ProposalJson!);
        Assert.Equal("[\"高风险\"]", loaded.RiskFlagsJson);
        Assert.Equal("[{\"id\":\"e-1\",\"source\":\"规则库\",\"contentHash\":\"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}]", loaded.EvidenceRefsJson);
        Assert.Equal(AiDecisionProviderResultType.Proposal, loaded.ProviderResultType);
        Assert.Equal(AiDecisionPolicyClassification.HighRisk, loaded.PolicyClassification);
        Assert.Equal(AiDecisionRecommendation.Approve, loaded.Recommendation);
        Assert.Equal(0.91m, loaded.Confidence);
        Assert.Equal(42, loaded.LatencyMilliseconds);
        Assert.Equal(10, loaded.PromptTokens);
        Assert.Equal(20, loaded.CompletionTokens);
        Assert.Equal(30, loaded.TotalTokens);
        Assert.Equal(AiDecisionFallbackReason.HighRisk, loaded.FallbackReason);
        Assert.Equal("ai-review", loaded.NodeId);
        Assert.True(loaded.ShadowMode);

        var failedLoaded = await db.Queryable<WfAiDecision>().Where(x => x.ExecutionId == failed.ExecutionId).FirstAsync();
        Assert.Equal(AiDecisionProviderResultType.Failed, failedLoaded.ProviderResultType);
        Assert.Null(failedLoaded.ProposalJson);
        Assert.Null(failedLoaded.Provider);
        Assert.Null(failedLoaded.Model);
        Assert.Null(failedLoaded.InputHash);
        Assert.Null(failedLoaded.PromptVersion);
        Assert.Null(failedLoaded.SchemaValid);
        Assert.Null(failedLoaded.PolicyVersion);
        Assert.Null(failedLoaded.PolicyClassification);
        Assert.Null(failedLoaded.Confidence);
        Assert.Equal(AiDecisionFallbackReason.ProviderFailure, failedLoaded.FallbackReason);

        var actualColumns = db.DbMaintenance.GetColumnInfosByTableName("wf_ai_decision", false)
            .Select(x => x.DbColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in typeof(WfAiDecision).GetProperties())
        {
            Assert.Contains(property.Name, actualColumns);
        }
    }

    [Fact]
    public void Audit_shape_has_required_indexes_and_no_raw_input_or_secret_columns()
    {
        var attributes = typeof(WfAiDecision).GetCustomAttributesData()
            .Where(x => x.AttributeType == typeof(SugarIndexAttribute))
            .ToArray();

        AssertIndex(attributes, "uk_wf_ai_decision_attempt", isUnique: true,
            nameof(WfAiDecision.ExecutionId), nameof(WfAiDecision.AttemptNo));
        AssertIndex(attributes, "idx_wf_ai_decision_instance_time", isUnique: false,
            nameof(WfAiDecision.InstanceId), nameof(WfAiDecision.CreateTime));

        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "VariablesJson", "Instructions", "Prompt", "ApiKey", "Authorization",
            "ProviderResponse", "Exception", "ErrorText",
        };
        Assert.DoesNotContain(typeof(WfAiDecision).GetProperties(), property => forbidden.Contains(property.Name));
        Assert.Equal(typeof(AuditEntity), typeof(WfAiDecision).BaseType);
        Assert.Null(typeof(WfAiDecision).GetProperty("IsDelete"));
    }

    private static WfAiDecision NewDecision(
        string? proposal,
        long executionId = 1001,
        int attemptNo = 1) => new()
    {
        ExecutionId = executionId,
        AttemptNo = attemptNo,
        InstanceId = 2002,
        NodeId = "ai-review",
        ProviderResultType = AiDecisionProviderResultType.Proposal,
        Provider = "fake",
        Model = "fixture-v1",
        InputHash = "sha256:" + new string('b', 64),
        PromptVersion = "1",
        ProposalJson = proposal,
        ProposalSchemaVersion = "1.0",
        SchemaValid = true,
        PolicyVersion = "1",
        PolicyClassification = AiDecisionPolicyClassification.HighRisk,
        Recommendation = AiDecisionRecommendation.Approve,
        Confidence = 0.91m,
        RiskFlagsJson = "[\"高风险\"]",
        EvidenceRefsJson = "[{\"id\":\"e-1\",\"source\":\"规则库\",\"contentHash\":\"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}]",
        LatencyMilliseconds = 42,
        PromptTokens = 10,
        CompletionTokens = 20,
        TotalTokens = 30,
        FallbackReason = AiDecisionFallbackReason.HighRisk,
        ShadowMode = true,
    };

    private static void AssertIndex(
        IEnumerable<CustomAttributeData> attributes,
        string indexName,
        bool isUnique,
        params string[] fields)
    {
        var attribute = Assert.Single(attributes, x =>
            string.Equals(x.ConstructorArguments[0].Value as string, indexName, StringComparison.Ordinal));
        var arguments = attribute.ConstructorArguments.Select(x => x.Value as string).OfType<string>().ToArray();
        Assert.Equal(fields, arguments.Skip(1));
        var declaredUnique = attribute.ConstructorArguments.Any(x => x.ArgumentType == typeof(bool) && Equals(x.Value, true))
            || attribute.NamedArguments.Any(x => x.MemberName == "IsUnique" && Equals(x.TypedValue.Value, true));
        Assert.Equal(isUnique, declaredUnique);
    }
}
