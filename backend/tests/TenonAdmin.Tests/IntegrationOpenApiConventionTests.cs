extern alias integrationhost;

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Integration;
using TenonAdmin.Services;
using static TenonAdmin.Tests.IntegrationOpenApiTestKit;

namespace TenonAdmin.Tests;

/// <summary>
/// G04:开放接口的独立文档、统一约定(错误/分页/时间/追踪)、按应用与认证失败限流、调用记录查询与保留清理。
/// </summary>
public class IntegrationOpenApiConventionTests
{
    [Fact]
    public async Task Open_document_describes_only_the_open_contract_with_real_auth_and_whitelisted_fields()
    {
        using var f = new IntegrationAppFactory();
        using var response = await f.CreateClient().GetAsync("/openapi/open-v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var paths = root.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/open/v1/org-tickets", paths);
        Assert.Contains("/api/open/v1/org-tickets/{id}", paths);
        Assert.Contains("/api/open/v1/partner-tickets/{id}/close", paths);
        Assert.Contains("/api/open/v1/ping", paths);
        Assert.All(paths, p => Assert.StartsWith("/api/open/v1/", p));   // 不含任何后台接口

        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());
        Assert.True(root.GetProperty("security")[0].TryGetProperty("ApiKey", out _));

        var description = root.GetProperty("info").GetProperty("description").GetString()!;
        foreach (var fact in new[] { "X-Api-Key", "49001", "49002", "49003", "49005", "49007", "X-Trace-Id", "ISO 8601", "100" })
            Assert.Contains(fact, description);

        var dto = root.GetProperty("components").GetProperty("schemas").GetProperty("TicketOpenDto").GetProperty("properties");
        var fields = dto.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "id", "title", "partnerCode", "amount", "closed", "createdAt" }, fields);
        Assert.Equal("date-time", dto.GetProperty("createdAt").GetProperty("format").GetString());

        var ping = root.GetProperty("paths").GetProperty("/api/open/v1/ping").GetProperty("get");
        Assert.Contains(ping.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "X-Request-Id");
        foreach (var status in new[] { "200", "401", "403", "429" })
            Assert.True(ping.GetProperty("responses").TryGetProperty(status, out _), $"缺少 {status} 响应说明");

        // 实体内部字段与后台接口都不出现在开放文档里
        var raw = root.GetRawText();
        foreach (var leaked in new[] { "internalNote", "createOrgId", "createUserId", "isDelete", "/api/v1/" })
            Assert.DoesNotContain(leaked, raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Production_serves_the_open_document_only_to_authorized_administrators()
    {
        using var f = new IntegrationAppFactory { EnvironmentName = "Production" };
        var anonymous = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/openapi/open-v1.json")).StatusCode);
        await AuthProbe.AssertUnauthorized(await anonymous.GetAsync("/api/v1/integration/catalog/open-api/v1"));
        await AuthProbe.AssertForbidden(await (await AuthProbe.PingOnly(f)).GetAsync("/api/v1/integration/catalog/open-api/v1"));

        var admin = await AuthProbe.SuperAdmin(f);
        using var download = await admin.GetAsync("/api/v1/integration/catalog/open-api/v1");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/json", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("open-v1.json", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using var doc = JsonDocument.Parse(await download.Content.ReadAsStringAsync());
        Assert.StartsWith("3.1", doc.RootElement.GetProperty("openapi").GetString());
        Assert.True(doc.RootElement.GetProperty("paths").TryGetProperty("/api/open/v1/ping", out _));

        var missing = await (await admin.GetAsync("/api/v1/integration/catalog/open-api/v9")).ReadEnvelope();
        Assert.Equal(IntegrationErrorCode.OpenApiVersionNotFound, missing.Code());
    }

    [Fact]
    public async Task Validation_errors_and_unhandled_exceptions_use_the_unified_envelope_with_trace()
    {
        using var f = new IntegrationAppFactory();
        var org = await NewOrgAsync(f.Services, "机构");
        var (_, key) = await NewAppAsync(f.Services, "envelope", [OrgCreate, Boom], [OrgScope(org)], ownerOrgId: org);
        var client = f.OpenClient(key);

        using var invalid = await client.PostAsync("/api/open/v1/org-tickets",
            new StringContent("{\"title\":\"x\",\"partnerCode\":\"P1\",\"amount\":\"不是数字\"}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidBody = await invalid.ReadEnvelope();
        Assert.Equal(IntegrationErrorCode.RequestInvalid, invalidBody.Code());
        Assert.True(invalidBody.GetProperty("args").GetProperty("errors").EnumerateObject().Any());
        Assert.NotEmpty(invalid.Headers.GetValues(OpenApiHeaders.TraceId).Single());

        using var boom = await client.PostAsync("/api/open/v1/ping/boom", null);
        Assert.Equal(HttpStatusCode.InternalServerError, boom.StatusCode);
        var boomBody = await boom.ReadEnvelope();
        Assert.Equal((int)ErrorCode.SystemError, boomBody.Code());
        var trace = boom.Headers.GetValues(OpenApiHeaders.TraceId).Single();
        Assert.Equal(trace, boomBody.GetProperty("args").GetProperty("traceId").GetString());
        Assert.DoesNotContain("demo failure", boomBody.GetRawText());          // 不回显异常消息与堆栈

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        // 内核异常留痕照常,且与对方拿到的追踪标识是同一个值:按它能同时查到调用记录与异常堆栈
        Assert.True(await db.Queryable<SysExceptionLog>().AnyAsync(e => e.Path == "/api/open/v1/ping/boom" && e.TraceId == trace));
        var logs = await db.Queryable<IntegrationInboundLog>().OrderBy(l => l.Id).ToListAsync();
        Assert.Equal((InboundCallOutcome.InvalidRequest, 400, IntegrationErrorCode.RequestInvalid), (logs[0].Outcome, logs[0].StatusCode, logs[0].ResultCode));
        Assert.Equal((InboundCallOutcome.Error, "exception:InvalidOperationException", trace), (logs[1].Outcome, logs[1].FailureReason, logs[1].TraceId));
    }

    [Fact]
    public async Task Paging_is_capped_normalized_and_keeps_the_kernel_shape_with_offset_times()
    {
        using var f = new IntegrationAppFactory();
        var org = await NewOrgAsync(f.Services, "机构");
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var rows = Enumerable.Range(0, 105).Select(i => new integrationhost::TenonAdmin.IntegrationTestHost.DemoTicket
            {
                Title = "单" + i, PartnerCode = "P1", Amount = i, CreateOrgId = org,
            }).ToList();
            await db.Insertable(rows).ExecuteCommandAsync();
        }
        var (_, key) = await NewAppAsync(f.Services, "pager", [OrgList], [OrgScope(org)]);
        var client = f.OpenClient(key);

        var big = (await client.Send(HttpMethod.Get, "/api/open/v1/org-tickets?current=1&size=500")).Body.GetProperty("data");
        Assert.Equal(100, big.GetProperty("size").GetInt32());
        Assert.Equal(100, big.GetProperty("items").GetArrayLength());
        Assert.Equal(105, big.GetProperty("total").GetInt32());
        Assert.Equal(2, big.GetProperty("pages").GetInt32());

        var normalized = (await client.Send(HttpMethod.Get, "/api/open/v1/org-tickets?current=0&size=0")).Body.GetProperty("data");
        Assert.Equal(1, normalized.GetProperty("current").GetInt32());
        Assert.Equal(20, normalized.GetProperty("size").GetInt32());

        var createdAt = normalized.GetProperty("items")[0].GetProperty("createdAt").GetString()!;
        Assert.Matches(new Regex(@"T\d{2}:\d{2}:\d{2}(\.\d+)?([+-]\d{2}:\d{2}|Z)$"), createdAt);
    }

    [Fact]
    public async Task App_rate_limit_rejects_over_quota_with_retry_after_and_is_recorded()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = new IntegrationAppFactory
        {
            Overrides = IntegrationTestSupport.UseClock(clock),
            Settings = new Dictionary<string, string?> { ["TenonAdmin:Integration:OpenApi:DefaultRateLimitPerMinute"] = "2" },
        };
        var (limitedId, limitedKey) = await NewAppAsync(f.Services, "limited", [Ping], []);
        var (_, otherKey) = await NewAppAsync(f.Services, "other", [Ping], []);
        var limited = f.OpenClient(limitedKey);

        Assert.Equal(200, (await limited.Send(HttpMethod.Get, "/api/open/v1/ping")).Status);
        Assert.Equal(200, (await limited.Send(HttpMethod.Get, "/api/open/v1/ping")).Status);
        using (var third = await limited.GetAsync("/api/open/v1/ping"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
            Assert.True(int.Parse(third.Headers.GetValues("Retry-After").Single()) is > 0 and <= 60);
            var body = await third.ReadEnvelope();
            Assert.Equal(IntegrationErrorCode.RateLimited, body.Code());
            Assert.True(body.GetProperty("args").GetProperty("retryAfterSeconds").GetInt32() > 0);
        }
        Assert.Equal(200, (await f.OpenClient(otherKey).Send(HttpMethod.Get, "/api/open/v1/ping")).Status);   // 各应用独立计数

        // 应用单独放宽(0 = 不限)下一次请求即生效;窗口滚动后恢复
        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIntegrationAppService>()
                .UpdateAsync(limitedId, new IntegrationAppUpdateInput { Name = "limited", RateLimitPerMinute = 0 });
        for (var i = 0; i < 5; i++)
            Assert.Equal(200, (await limited.Send(HttpMethod.Get, "/api/open/v1/ping")).Status);

        using var db = f.Services.CreateScope();
        var rejected = await db.ServiceProvider.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationInboundLog>()
            .Where(l => l.Outcome == InboundCallOutcome.RateLimited).ToListAsync();
        var row = Assert.Single(rejected);
        Assert.Equal(("app_rate_limited", 429, limitedId), (row.FailureReason, row.StatusCode, row.AppId));
    }

    [Fact]
    public async Task Authentication_failures_are_throttled_per_source_and_key_before_credential_lookup()
    {
        var clock = IntegrationTestSupport.NewClock();
        var lookups = new int[1];
        using var f = new IntegrationAppFactory
        {
            Settings = new Dictionary<string, string?> { ["TenonAdmin:Integration:OpenApi:AuthFailuresPerMinutePerIp"] = "3" },
            Overrides = s =>
            {
                IntegrationTestSupport.UseClock(clock)(s);
                s.RemoveAll<IOpenAppCredentialValidator>();
                s.AddScoped<OpenAppCredentialValidator>();
                s.AddScoped<IOpenAppCredentialValidator>(sp => new CountingValidator(sp.GetRequiredService<OpenAppCredentialValidator>(), lookups));
            },
        };
        var (_, key) = await NewAppAsync(f.Services, "victim", [Ping], []);
        var (_, bystander) = await NewAppAsync(f.Services, "bystander", [Ping], []);
        Assert.True(OpenAppApiKey.TryParse(key, out var keyId, out _));

        // 格式不对的凭据按来源计数:刷满后同一来源的格式错误请求在查库前被拒……
        var malformed = f.OpenClient("not-an-api-key");
        for (var i = 0; i < 3; i++)
            Assert.Equal(401, (await malformed.Send(HttpMethod.Get, "/api/open/v1/ping")).Status);
        Assert.Equal(429, (await malformed.Send(HttpMethod.Get, "/api/open/v1/ping")).Status);
        // ……但不牵连同一来源的合法凭据(反代后没还原真实 IP 时,所有调用方共用一个来源)
        Assert.Equal(200, (await f.OpenClient(key).Send(HttpMethod.Get, "/api/open/v1/ping")).Status);

        // 格式正确的凭据按(来源, 凭据标识)计数:对同一标识猜秘密,刷满后该标识(即便秘密正确)暂时被拒,且不再查库
        var guessing = f.OpenClient(OpenAppApiKey.Format(keyId, new string('A', OpenAppApiKey.SecretLength)));
        for (var i = 0; i < 3; i++)
            Assert.Equal(401, (await guessing.Send(HttpMethod.Get, "/api/open/v1/ping")).Status);
        var lookupsBefore = lookups[0];
        using (var throttled = await f.OpenClient(key).GetAsync("/api/open/v1/ping"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
            Assert.True(throttled.Headers.Contains("Retry-After"));
            Assert.Equal(IntegrationErrorCode.AuthenticationThrottled, (await throttled.ReadEnvelope()).Code());
        }
        Assert.Equal(lookupsBefore, lookups[0]);                                                                    // 被拒时未查库
        Assert.Equal(200, (await f.OpenClient(bystander).Send(HttpMethod.Get, "/api/open/v1/ping")).Status);      // 其他标识不受牵连

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(200, (await f.OpenClient(key).Send(HttpMethod.Get, "/api/open/v1/ping")).Status);
    }

    [Fact]
    public async Task Credentials_in_the_query_string_are_rejected_and_never_stored()
    {
        using var f = new IntegrationAppFactory();
        var (_, key) = await NewAppAsync(f.Services, "url-key", [Ping], []);
        var client = f.OpenClient(key);
        var (status, body) = await client.Send(HttpMethod.Get, "/api/open/v1/ping?api_key=" + Uri.EscapeDataString(key));
        Assert.Equal(401, status);
        Assert.Equal(IntegrationErrorCode.CredentialInvalid, body.Code());

        using var scope = f.Services.CreateScope();
        var logs = JsonSerializer.Serialize(await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationInboundLog>().ToListAsync());
        Assert.DoesNotContain(key[(4 + 16 + 1)..], logs);
        Assert.Equal(200, (await client.Send(HttpMethod.Get, "/api/open/v1/ping")).Status);   // 头部方式照常
    }

    [Fact]
    public async Task Trace_headers_are_present_on_every_outcome_and_unsafe_request_ids_are_dropped()
    {
        using var f = new IntegrationAppFactory();
        var (_, key) = await NewAppAsync(f.Services, "tracer", [Ping], []);

        foreach (var (client, path, expected) in new[]
        {
            (f.OpenClient(key), "/api/open/v1/ping", HttpStatusCode.OK),
            (f.CreateClient(), "/api/open/v1/ping", HttpStatusCode.Unauthorized),
            (f.OpenClient(key), "/api/open/v1/org-tickets", HttpStatusCode.Forbidden),
        })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(expected, response.StatusCode);
            Assert.False(string.IsNullOrEmpty(response.Headers.GetValues(OpenApiHeaders.TraceId).Single()));
        }

        var unsafeId = f.OpenClient(key);
        unsafeId.DefaultRequestHeaders.TryAddWithoutValidation(OpenApiHeaders.RequestId, "bad id with spaces");
        using var dropped = await unsafeId.GetAsync("/api/open/v1/ping");
        Assert.False(dropped.Headers.Contains(OpenApiHeaders.RequestId));
        using var scope = f.Services.CreateScope();
        var last = await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationInboundLog>()
            .OrderBy(l => l.Id, OrderByType.Desc).FirstAsync();
        Assert.Null(last.ClientRequestId);
    }

    [Fact]
    public async Task Call_log_query_is_limited_to_authorized_admins_and_filters_by_app_outcome_and_trace()
    {
        using var f = new IntegrationAppFactory();
        var (appA, keyA) = await NewAppAsync(f.Services, "log-a", [Ping], []);
        var (appB, keyB) = await NewAppAsync(f.Services, "log-b", [], []);
        using var ok = await f.OpenClient(keyA).GetAsync("/api/open/v1/ping");
        var okTrace = ok.Headers.GetValues(OpenApiHeaders.TraceId).Single();
        await f.OpenClient(keyB).GetAsync("/api/open/v1/ping");   // 未授予 → 403

        var admin = await AuthProbe.SuperAdmin(f);
        var byApp = (await admin.Send(HttpMethod.Get, $"/api/v1/integration/inbound-log/page?appId={appB}")).Body.GetProperty("data");
        Assert.Equal(1, byApp.GetProperty("total").GetInt32());
        Assert.Equal((int)InboundCallOutcome.Forbidden, byApp.GetProperty("items")[0].GetProperty("outcome").GetInt32());

        var byOutcome = (await admin.Send(HttpMethod.Get, $"/api/v1/integration/inbound-log/page?outcome={(int)InboundCallOutcome.Succeeded}")).Body.GetProperty("data");
        Assert.Equal(appA, byOutcome.GetProperty("items")[0].GetProperty("appId").GetInt64());
        var byTrace = (await admin.Send(HttpMethod.Get, $"/api/v1/integration/inbound-log/page?traceId={Uri.EscapeDataString(okTrace)}")).Body.GetProperty("data");
        Assert.Equal(1, byTrace.GetProperty("total").GetInt32());

        await AuthProbe.AssertUnauthorized(await f.CreateClient().GetAsync("/api/v1/integration/inbound-log/page"));
        await AuthProbe.AssertUnauthorized(await f.OpenClient(keyA).GetAsync("/api/v1/integration/inbound-log/page"));
        await AuthProbe.AssertForbidden(await (await AuthProbe.PingOnly(f)).GetAsync("/api/v1/integration/inbound-log/page"));
    }

    [Fact]
    public async Task Retention_deletes_only_expired_call_logs_in_batches_and_the_job_is_seeded()
    {
        using var f = new IntegrationAppFactory
        {
            Settings = new Dictionary<string, string?> { ["TenonAdmin:Integration:Retention:BatchSize"] = "2" },
        };
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ISqlSugarClient>();
        var now = DateTime.Now;
        var rows = new[] { -45, -31, -40, -29, -1, 0 }
            .Select(days => new IntegrationInboundLog { HttpMethod = "GET", Route = "r", Path = "/p", TraceId = "t" + days, CreateTime = now.AddDays(days) })
            .ToList();
        await db.Insertable(rows).ExecuteCommandAsync();

        var job = sp.GetServices<IAdminJob>().OfType<IntegrationRetentionJob>().Single();
        var messages = new List<string>();
        await job.ExecuteAsync(new JobExecutionContext
        {
            JobId = 1, JobCode = "itg-retention", JobName = "清理", FireInstanceId = 1,
            ScheduledTime = now, FireTime = now, Log = messages.Add,
        }, CancellationToken.None);

        var remaining = await db.Queryable<IntegrationInboundLog>().Select(l => l.TraceId).ToListAsync();
        Assert.Equal(new HashSet<string> { "t-29", "t-1", "t0" }, remaining.ToHashSet());
        Assert.Contains("3 行", Assert.Single(messages));

        var seeded = await db.Queryable<SysJob>().Where(j => j.Code == "itg-retention").FirstAsync();
        Assert.NotNull(seeded);
        Assert.Equal(typeof(IntegrationRetentionJob).FullName, seeded.HandlerName);
    }

    [Fact]
    public async Task Built_in_whoami_is_default_deny_and_reports_only_public_identity_when_granted()
    {
        using var f = new IntegrationAppFactory();
        var (_, denied) = await NewAppAsync(f.Services, "who-denied", [], []);
        var (status, deniedBody) = await f.OpenClient(denied).Send(HttpMethod.Get, "/api/open/v1/whoami");
        Assert.Equal(403, status);
        Assert.Equal(IntegrationErrorCode.PermissionDenied, deniedBody.Code());

        var (_, key) = await NewAppAsync(f.Services, "who-granted", ["GET:/api/open/v1/whoami"], []);
        var data = (await f.OpenClient(key).Send(HttpMethod.Get, "/api/open/v1/whoami")).Body.GetProperty("data");
        Assert.Equal("who-granted", data.GetProperty("appCode").GetString());
        Assert.True(OpenAppApiKey.TryParse(key, out var keyId, out var secret));
        Assert.Equal(keyId, data.GetProperty("keyId").GetString());
        Assert.DoesNotContain(secret, data.GetRawText());
        Assert.True(DateTimeOffset.TryParse(data.GetProperty("serverTime").GetString(), out _));
    }

    private sealed class CountingValidator(IOpenAppCredentialValidator inner, int[] counter) : IOpenAppCredentialValidator
    {
        public Task<OpenAppCredentialValidation> ValidateAsync(string? presentedKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref counter[0]);
            return inner.ValidateAsync(presentedKey, cancellationToken);
        }
    }
}
