using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>M3B0-14:AI 审计读取只返回脱敏投影,并沿用实例参与者/监控权限。</summary>
public class WfAiDecisionAuditApiTests
{
    private const string Password = "Test@123456";
    private const string ProviderSecret = "provider-response-secret";
    private const string RiskHash = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string EvidenceIdHash = "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string EvidenceSourceHash = "sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private const string ProviderHash = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private const string ModelHash = "sha256:2222222222222222222222222222222222222222222222222222222222222222";
    private const string PromptHash = "sha256:3333333333333333333333333333333333333333333333333333333333333333";
    private const string PolicyHash = "sha256:4444444444444444444444444444444444444444444444444444444444444444";

    [Fact]
    public async Task Ai_decision_audit_is_redacted_and_uses_instance_participant_boundary()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var approverId = await AddUser(admin, "wf-ai-audit-approver");
        await AddUser(admin, "wf-ai-audit-starter");
        await AddUser(admin, "wf-ai-audit-stranger");
        var monitorUserId = await AddUser(admin, "wf-ai-audit-monitor");
        await GrantMonitorPermission(f, monitorUserId);
        var definitionId = await Publish(admin, approverId);
        var starter = await ClientFor(f, "wf-ai-audit-starter");
        var approver = await ClientFor(f, "wf-ai-audit-approver");
        var stranger = await ClientFor(f, "wf-ai-audit-stranger");
        var monitorUser = await ClientFor(f, "wf-ai-audit-monitor");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();

        using (var scope = f.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepository<WfAiDecision>>().InsertAsync(
                new WfAiDecision
                {
                    ExecutionId = 9001,
                    AttemptNo = 1,
                    InstanceId = instanceId,
                    NodeId = "ai-review",
                    ProviderResultType = AiDecisionProviderResultType.Proposal,
                    Provider = ProviderHash,
                    Model = ModelHash,
                    InputHash = "sha256:" + new string('a', 64),
                    PromptVersion = PromptHash,
                    ProposalJson = "{\"schemaVersion\":\"1.0\",\"rationale\":\"" + ProviderSecret
                        + "\",\"recommendation\":\"approve\"}",
                    ProposalSchemaVersion = "1.0",
                    SchemaValid = true,
                    PolicyVersion = PolicyHash,
                    PolicyClassification = AiDecisionPolicyClassification.ShadowCandidate,
                    Recommendation = AiDecisionRecommendation.Approve,
                    Confidence = 0.95m,
                    RiskFlagsJson = "[\"" + RiskHash + "\"]",
                    EvidenceRefsJson = "[{\"id\":\"" + EvidenceIdHash + "\",\"source\":\"" + EvidenceSourceHash
                        + "\",\"contentHash\":\"sha256:" + new string('b', 64) + "\"}]",
                    LatencyMilliseconds = 12,
                    PromptTokens = 3,
                    CompletionTokens = 4,
                    TotalTokens = 7,
                    FallbackReason = AiDecisionFallbackReason.ShadowOnly,
                    ShadowMode = true,
                });
        }

        var starterAudit = await GetEnvelope(starter, $"/api/v1/workflow/instance/ai-decisions/{instanceId}");
        Assert.Equal(0, starterAudit.GetProperty("code").GetInt32());
        var row = Assert.Single(starterAudit.GetProperty("data").EnumerateArray());
        Assert.Equal("ai-review", row.GetProperty("nodeId").GetString());
        Assert.Equal(1, row.GetProperty("attemptNo").GetInt32());
        Assert.Equal(ProviderHash, row.GetProperty("provider").GetString());
        Assert.Equal(RiskHash, row.GetProperty("riskFlags")[0].GetString());
        Assert.Equal(EvidenceIdHash, row.GetProperty("evidenceRefs")[0].GetProperty("id").GetString());
        Assert.DoesNotContain(ProviderSecret, starterAudit.GetRawText(), StringComparison.Ordinal);
        Assert.False(row.TryGetProperty("proposalJson", out _));
        Assert.False(row.TryGetProperty("executionId", out _));

        // 办理人没有监控权限码,仍可按现有参与者边界读取本实例。
        var approverAudit = await GetEnvelope(approver, $"/api/v1/workflow/instance/ai-decisions/{instanceId}");
        Assert.Equal(0, approverAudit.GetProperty("code").GetInt32());

        var denied = await GetEnvelope(stranger, $"/api/v1/workflow/instance/ai-decisions/{instanceId}");
        Assert.Equal(WorkflowErrorCode.InstanceAccessDenied, denied.GetProperty("code").GetInt32());

        var monitorAudit = await GetEnvelope(monitorUser, $"/api/v1/workflow/instance/ai-decisions/{instanceId}");
        Assert.Equal(0, monitorAudit.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Monitor_permission_does_not_bypass_instance_org_scope()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var monitorId = await AddUser(admin, "wf-ai-audit-scoped-monitor", orgId: 1, roleIds: []);
        await GrantMonitorPermission(f, monitorId, restrictToOrg: true);

        long instanceId;
        using (var scope = f.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<WfInstance>>();
            var instance = new WfInstance
            {
                CreateOrgId = 3,
                StarterUserId = 999_001,
                DefinitionVersionId = 1,
                Status = WfInstanceStatus.Running,
            };
            await repo.InsertAsync(instance);
            instanceId = instance.Id;
            await scope.ServiceProvider.GetRequiredService<IRepository<WfAiDecision>>().InsertAsync(
                new WfAiDecision
                {
                    ExecutionId = 9002,
                    AttemptNo = 1,
                    InstanceId = instanceId,
                    NodeId = "ai-review",
                    ProviderResultType = AiDecisionProviderResultType.Failed,
                    FallbackReason = AiDecisionFallbackReason.ProviderFailure,
                });
        }

        var monitor = await ClientFor(f, "wf-ai-audit-scoped-monitor");
        var denied = await GetEnvelope(monitor, $"/api/v1/workflow/instance/ai-decisions/{instanceId}");
        Assert.Equal(WorkflowErrorCode.InstanceAccessDenied, denied.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Corrupt_ai_audit_json_returns_a_controlled_error()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-ai-audit-corrupt-starter");
        long instanceId;
        using (var scope = f.Services.CreateScope())
        {
            var instance = new WfInstance
            {
                CreateOrgId = 1,
                StarterUserId = starterId,
                DefinitionVersionId = 1,
                Status = WfInstanceStatus.Running,
            };
            await scope.ServiceProvider.GetRequiredService<IRepository<WfInstance>>().InsertAsync(instance);
            instanceId = instance.Id;
            await scope.ServiceProvider.GetRequiredService<IRepository<WfAiDecision>>().InsertAsync(
                new WfAiDecision
                {
                    ExecutionId = 9003,
                    AttemptNo = 1,
                    InstanceId = instanceId,
                    NodeId = "ai-review",
                    ProviderResultType = AiDecisionProviderResultType.Proposal,
                    RiskFlagsJson = "not-json",
                    FallbackReason = AiDecisionFallbackReason.ShadowOnly,
                });
        }

        var starter = await ClientFor(f, "wf-ai-audit-corrupt-starter");
        var response = await GetEnvelope(starter, $"/api/v1/workflow/instance/ai-decisions/{instanceId}");
        Assert.Equal(WorkflowErrorCode.OperationFailed, response.GetProperty("code").GetInt32());
    }

    [Theory]
    [MemberData(nameof(InvalidAuditJson))]
    public async Task Invalid_ai_audit_data_is_rejected(
        string? riskFlagsJson,
        string? evidenceRefsJson)
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-ai-audit-invalid-shape");
        long instanceId;
        using (var scope = f.Services.CreateScope())
        {
            var instance = new WfInstance
            {
                CreateOrgId = 1,
                StarterUserId = starterId,
                DefinitionVersionId = 1,
                Status = WfInstanceStatus.Running,
            };
            await scope.ServiceProvider.GetRequiredService<IRepository<WfInstance>>().InsertAsync(instance);
            instanceId = instance.Id;
            await scope.ServiceProvider.GetRequiredService<IRepository<WfAiDecision>>().InsertAsync(
                new WfAiDecision
                {
                    ExecutionId = 9010,
                    AttemptNo = 1,
                    InstanceId = instanceId,
                    NodeId = "ai-review",
                    ProviderResultType = AiDecisionProviderResultType.Proposal,
                    RiskFlagsJson = riskFlagsJson,
                    EvidenceRefsJson = evidenceRefsJson,
                    FallbackReason = AiDecisionFallbackReason.ShadowOnly,
                });
        }

        var starter = await ClientFor(f, "wf-ai-audit-invalid-shape");
        var response = await GetEnvelope(starter, $"/api/v1/workflow/instance/ai-decisions/{instanceId}");
        Assert.Equal(WorkflowErrorCode.OperationFailed, response.GetProperty("code").GetInt32());
    }

    public static IEnumerable<object?[]> InvalidAuditJson()
    {
        yield return ["[\"plain-text-risk\"]", null];
        yield return [null, "[{\"id\":\"sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd\",\"source\":\"sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\",\"contentHash\":\"not-a-hash\",\"extra\":true}]"];
        yield return ["", null];
        yield return ["   ", null];
        yield return [new string('x', AiDecisionProposalParser.MaximumJsonCharacters + 1), null];
    }

    [Fact]
    public async Task Ai_decision_audit_is_ordered_by_occurrence_across_executions()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-ai-audit-order-starter");
        long instanceId;
        using (var scope = f.Services.CreateScope())
        {
            var instances = scope.ServiceProvider.GetRequiredService<IRepository<WfInstance>>();
            var instance = new WfInstance
            {
                CreateOrgId = 1,
                StarterUserId = starterId,
                DefinitionVersionId = 1,
                Status = WfInstanceStatus.Running,
            };
            await instances.InsertAsync(instance);
            instanceId = instance.Id;

            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            await db.Insertable(new[]
            {
                new WfAiDecision
                {
                    ExecutionId = 9020,
                    AttemptNo = 2,
                    InstanceId = instanceId,
                    NodeId = "ai-first",
                    ProviderResultType = AiDecisionProviderResultType.Failed,
                    FallbackReason = AiDecisionFallbackReason.ProviderFailure,
                    CreateTime = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                },
                new WfAiDecision
                {
                    ExecutionId = 9021,
                    AttemptNo = 1,
                    InstanceId = instanceId,
                    NodeId = "ai-second",
                    ProviderResultType = AiDecisionProviderResultType.Failed,
                    FallbackReason = AiDecisionFallbackReason.ProviderFailure,
                    CreateTime = new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                },
            }).ExecuteCommandAsync();
        }

        var starter = await ClientFor(f, "wf-ai-audit-order-starter");
        var response = await GetEnvelope(starter, $"/api/v1/workflow/instance/ai-decisions/{instanceId}");
        Assert.Equal(0, response.GetProperty("code").GetInt32());
        var rows = response.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(["ai-first", "ai-second"], rows.Select(row => row.GetProperty("nodeId").GetString()));
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(
        HttpClient admin,
        string account,
        long orgId = 1,
        IReadOnlyCollection<long>? roleIds = null)
    {
        var env = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account,
            password = Password,
            name = account,
            enabled = true,
            orgId,
            roleIds = roleIds ?? [2L],
        });
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task GrantMonitorPermission(
        WorkflowAppFactory f,
        long userId,
        bool restrictToOrg = false)
    {
        using var scope = f.Services.CreateScope();
        var role = new SysRole
        {
            Name = "AI audit monitor",
            Code = "wf-ai-audit-monitor-" + Guid.NewGuid().ToString("N"),
            Enabled = true,
        };
        await scope.ServiceProvider.GetRequiredService<IRepository<SysRole>>().InsertAsync(role);

        var menu = await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().Queryable<SysMenu>()
            .Where(m => m.Permission == WfInstanceService.MonitorPermission)
            .FirstAsync();
        Assert.NotNull(menu);
        await scope.ServiceProvider.GetRequiredService<IRepository<SysRoleMenu>>().InsertAsync(
            new SysRoleMenu { RoleId = role.Id, MenuId = menu!.Id });
        await scope.ServiceProvider.GetRequiredService<IRepository<SysUserRole>>().InsertAsync(
            new SysUserRole { UserId = userId, RoleId = role.Id });
        if (restrictToOrg)
            await scope.ServiceProvider.GetRequiredService<IRbacService>()
                .SetRoleDataScopeAsync(role.Id, DataScopeType.Org);
    }

    private static async Task<long> Publish(HttpClient admin, long approverId)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new
        {
            name = "AI 审计读取",
            model = new
            {
                version = 1,
                root = new
                {
                    id = "start",
                    type = "start",
                    name = "",
                    next = new
                    {
                        id = "approve-1",
                        type = "approval",
                        name = "审批",
                        props = new
                        {
                            assignee = new
                            {
                                provider = "user",
                                @params = new Dictionary<string, object> { ["userIds"] = new[] { approverId } },
                            },
                            mode = "any",
                        },
                        next = (object?)null,
                    },
                },
            },
        });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
