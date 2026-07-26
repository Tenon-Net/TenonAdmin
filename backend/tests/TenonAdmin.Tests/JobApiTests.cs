using System.Net.Http.Headers;
using System.Text.Json;

namespace TenonAdmin.Tests;

/// <summary>
/// 定时任务 HTTP 端点回归(scheduling-ledger §8):CRUD、启停、执行一次、cron 预览、处理器清单、
/// 执行记录、仪表盘,以及 47xxx 各码的触发路径。
/// </summary>
public class JobApiTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static object CronJob(string code, string cron = "0 30 3 * * ?") => new
    {
        code,
        name = "测试任务 " + code,
        handlerKind = 1,
        handlerName = "TenonAdmin.Services.JobLogCleanupJob",
        triggerKind = 1,
        cronExpression = cron,
    };

    private static async Task<long> AddJobAsync(HttpClient admin, object body)
    {
        var add = await (await admin.PostJson("/api/v1/sys/job", body)).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        return add.GetProperty("data").GetInt64();
    }

    [Fact]
    public async Task Crud_round_trip_normalizes_cron_and_computes_next_run_time()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // 5 段输入 → 入库归一化为 6 段
        var id = await AddJobAsync(admin, CronJob("t-crud", "30 3 * * *"));

        var row = await ReadJobAsync(admin, "t-crud");
        Assert.Equal("0 30 3 * * *", row.GetProperty("cronExpression").GetString());
        Assert.Equal(1, row.GetProperty("status").GetInt32());                 // Ready
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("nextRunTime").ValueKind);

        // 传个不同的 code:更新时必须被忽略(Code 创建后不可变)
        var update = await (await admin.PutJson($"/api/v1/sys/job/{id}", CronJob("ignored-code", "0 0 4 * * ?"))).ReadEnvelope();
        Assert.Equal(0, update.GetProperty("code").GetInt32());

        // 按 code 回读:更新把 Name 改成了 "测试任务 ignored-code",按名字筛就找不着了
        var updated = await ReadJobAsync(admin, "t-crud");
        Assert.Equal("0 0 4 * * ?", updated.GetProperty("cronExpression").GetString());
        Assert.Equal("t-crud", updated.GetProperty("code").GetString());       // Code 创建后不可变

        var del = await (await admin.DeleteAsync($"/api/v1/sys/job/{id}")).ReadEnvelope();
        Assert.Equal(0, del.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Duplicate_code_is_rejected_with_47002()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        await AddJobAsync(admin, CronJob("t-dup"));

        var again = await (await admin.PostJson("/api/v1/sys/job", CronJob("t-dup"))).ReadEnvelope();
        Assert.Equal(47002, again.GetProperty("code").GetInt32());
    }

    [Theory]
    // cron 非法 → 47003
    [InlineData(1, "not a cron", null, 47003)]
    // cron 日周同限 → 47003
    [InlineData(1, "0 0 0 1 * MON", null, 47003)]
    // 间隔 < 5 秒 → 47004
    [InlineData(2, null, 3, 47004)]
    // 间隔缺失 → 47004
    [InlineData(2, null, null, 47004)]
    // cron 段为空 → 47004
    [InlineData(1, "", null, 47004)]
    public async Task Invalid_trigger_is_rejected(int triggerKind, string? cron, int? intervalSeconds, int expectedCode)
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var body = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = $"t-bad-{triggerKind}-{intervalSeconds}-{cron?.Length}",
            name = "非法触发",
            handlerKind = 1,
            handlerName = "TenonAdmin.Services.JobLogCleanupJob",
            triggerKind,
            cronExpression = cron,
            intervalSeconds,
        })).ReadEnvelope();
        Assert.Equal(expectedCode, body.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Past_one_shot_time_is_rejected_with_47004()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var body = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-past",
            name = "过去的一次性任务",
            handlerKind = 1,
            handlerName = "TenonAdmin.Services.JobLogCleanupJob",
            triggerKind = 3,
            oneShotTime = DateTime.Now.AddMinutes(-5),
        })).ReadEnvelope();
        Assert.Equal(47004, body.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Unknown_compiled_handler_is_rejected_with_47005()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var body = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-nohandler",
            name = "处理器不存在",
            handlerKind = 1,
            handlerName = "No.Such.Handler",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
        })).ReadEnvelope();
        Assert.Equal(47005, body.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Sql_job_is_rejected_while_disabled_with_47008()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var body = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-sql",
            name = "SQL 任务",
            handlerKind = 3,
            handlerName = "",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
            properties = new Dictionary<string, string?> { ["sql"] = "DELETE FROM sys_job_log" },
        })).ReadEnvelope();
        Assert.Equal(47008, body.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Http_job_url_hitting_the_fence_is_rejected_with_47009()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // 云元数据段是默认黑名单
        var blocked = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-http-blocked",
            name = "打元数据",
            handlerKind = 2,
            handlerName = "",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
            properties = new Dictionary<string, string?> { ["url"] = "http://169.254.169.254/latest/meta-data/" },
        })).ReadEnvelope();
        Assert.Equal(47009, blocked.GetProperty("code").GetInt32());

        // 非 http/https 同样拒绝
        var scheme = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-http-scheme",
            name = "file 协议",
            handlerKind = 2,
            handlerName = "",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
            properties = new Dictionary<string, string?> { ["url"] = "file:///etc/passwd" },
        })).ReadEnvelope();
        Assert.Equal(47009, scheme.GetProperty("code").GetInt32());

        // 内网地址默认放行(调度器打内网服务是主用途)
        var intranet = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-http-ok",
            name = "打内网",
            handlerKind = 2,
            handlerName = "",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
            properties = new Dictionary<string, string?> { ["url"] = "http://10.0.0.5/health" },
        })).ReadEnvelope();
        Assert.Equal(0, intranet.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Http_job_missing_url_is_rejected_with_47011()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var body = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-http-nourl",
            name = "没有 url",
            handlerKind = 2,
            handlerName = "",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
        })).ReadEnvelope();
        Assert.Equal(47011, body.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Built_in_job_cannot_be_deleted_47014()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var page = await (await admin.GetAsync("/api/v1/sys/job/page?size=50")).ReadEnvelope();
        var seeded = page.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == "sys-job-log-cleanup");
        Assert.True(seeded.GetProperty("isSystem").GetBoolean());
        var seededId = seeded.GetProperty("id").GetInt64();

        var single = await (await admin.DeleteAsync($"/api/v1/sys/job/{seededId}")).ReadEnvelope();
        Assert.Equal(47014, single.GetProperty("code").GetInt32());

        var batch = await (await admin.PostJson("/api/v1/sys/job/batch-delete", new { ids = new[] { seededId } })).ReadEnvelope();
        Assert.Equal(47014, batch.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Missing_job_returns_47001()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var body = await (await admin.DeleteAsync("/api/v1/sys/job/999999")).ReadEnvelope();
        Assert.Equal(47001, body.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Enable_toggles_status_and_completed_one_shot_cannot_be_revived_47010()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var id = await AddJobAsync(admin, CronJob("t-toggle"));

        var off = await (await admin.PutJson($"/api/v1/sys/job/{id}/enabled?enabled=false", new { })).ReadEnvelope();
        Assert.Equal(0, off.GetProperty("code").GetInt32());
        var paused = await ReadJobAsync(admin, "t-toggle");
        Assert.Equal(2, paused.GetProperty("status").GetInt32());              // Paused
        Assert.Equal(JsonValueKind.Null, paused.GetProperty("nextRunTime").ValueKind);

        var on = await (await admin.PutJson($"/api/v1/sys/job/{id}/enabled?enabled=true", new { })).ReadEnvelope();
        Assert.Equal(0, on.GetProperty("code").GetInt32());
        var ready = await ReadJobAsync(admin, "t-toggle");
        Assert.Equal(1, ready.GetProperty("status").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, ready.GetProperty("nextRunTime").ValueKind);

        // 一次性任务:先建将来时刻,暂停后把时刻甩到过去 → enable 无未来时刻 → 47010
        var oneShotId = await AddJobAsync(admin, new
        {
            code = "t-oneshot",
            name = "一次性",
            handlerKind = 1,
            handlerName = "TenonAdmin.Services.JobLogCleanupJob",
            triggerKind = 3,
            oneShotTime = DateTime.Now.AddSeconds(30),
        });
        await admin.PutJson($"/api/v1/sys/job/{oneShotId}/enabled?enabled=false", new { });
        await Task.Delay(1000);
        // 直接把时刻改到过去是非法输入(47004),故用等待跨过时刻的方式不现实;此处退一步:
        // 用 cron+EndTime 造"无未来时刻"
        var windowId = await AddJobAsync(admin, new
        {
            code = "t-window",
            name = "窗口即将关闭",
            handlerKind = 1,
            handlerName = "TenonAdmin.Services.JobLogCleanupJob",
            triggerKind = 2,
            intervalSeconds = 5,
            endTime = DateTime.Now.AddSeconds(2),
        });
        await admin.PutJson($"/api/v1/sys/job/{windowId}/enabled?enabled=false", new { });
        await Task.Delay(2500);
        var revive = await (await admin.PutJson($"/api/v1/sys/job/{windowId}/enabled?enabled=true", new { })).ReadEnvelope();
        Assert.Equal(47010, revive.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Preview_cron_returns_normalized_form_and_occurrences()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var ok = await (await admin.PostJson("/api/v1/sys/job/preview-cron",
            new { cron = "30 3 * * *", count = 3, from = new DateTime(2026, 7, 26, 0, 0, 0) })).ReadEnvelope();
        Assert.Equal(0, ok.GetProperty("code").GetInt32());
        Assert.Equal("0 30 3 * * *", ok.GetProperty("data").GetProperty("normalized").GetString());
        Assert.Equal(3, ok.GetProperty("data").GetProperty("occurrences").GetArrayLength());
        Assert.False(ok.GetProperty("data").GetProperty("everySecondWarning").GetBoolean());

        var everySecond = await (await admin.PostJson("/api/v1/sys/job/preview-cron", new { cron = "* * * * * ?", count = 2 })).ReadEnvelope();
        Assert.True(everySecond.GetProperty("data").GetProperty("everySecondWarning").GetBoolean());

        var bad = await (await admin.PostJson("/api/v1/sys/job/preview-cron", new { cron = "nope", count = 3 })).ReadEnvelope();
        Assert.Equal(47003, bad.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Handlers_endpoint_lists_built_in_handlers()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var body = await (await admin.GetAsync("/api/v1/sys/job/handlers")).ReadEnvelope();
        var names = body.GetProperty("data").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("TenonAdmin.Services.JobLogCleanupJob", names);
        Assert.Contains("TenonAdmin.Services.HttpAdminJob", names);
        Assert.Contains("TenonAdmin.Services.SqlAdminJob", names);
    }

    [Fact]
    public async Task Run_once_executes_locally_and_writes_a_log_row()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var id = await AddJobAsync(admin, CronJob("t-runnow"));
        var before = await ReadJobAsync(admin, "t-runnow");
        var nextBefore = before.GetProperty("nextRunTime").GetString();

        var run = await (await admin.PostJson($"/api/v1/sys/job/{id}/run", new { })).ReadEnvelope();
        Assert.Equal(0, run.GetProperty("code").GetInt32());

        JsonElement logs = default;
        for (var i = 0; i < 50; i++)
        {
            logs = await (await admin.GetAsync($"/api/v1/sys/job/log/page?jobId={id}")).ReadEnvelope();
            if (logs.GetProperty("data").GetProperty("items").GetArrayLength() > 0) break;
            await Task.Delay(100);
        }
        var log = logs.GetProperty("data").GetProperty("items").EnumerateArray().First();
        Assert.Equal(2, log.GetProperty("fireMode").GetInt32());               // Manual
        Assert.Equal(id, log.GetProperty("jobId").GetInt64());

        // 手动触发不动调度节奏
        var after = await ReadJobAsync(admin, "t-runnow");
        Assert.Equal(nextBefore, after.GetProperty("nextRunTime").GetString());
    }

    [Fact]
    public async Task Kill_on_finished_run_returns_47007_and_missing_run_returns_47012()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var id = await AddJobAsync(admin, CronJob("t-kill"));
        await admin.PostJson($"/api/v1/sys/job/{id}/run", new { });

        long logId = 0;
        for (var i = 0; i < 50; i++)
        {
            var logs = await (await admin.GetAsync($"/api/v1/sys/job/log/page?jobId={id}")).ReadEnvelope();
            var records = logs.GetProperty("data").GetProperty("items");
            if (records.GetArrayLength() > 0)
            {
                var row = records.EnumerateArray().First();
                if (row.GetProperty("endTime").ValueKind != JsonValueKind.Null)
                {
                    logId = row.GetProperty("id").GetInt64();
                    break;
                }
            }
            await Task.Delay(100);
        }
        Assert.True(logId > 0, "执行记录没有在 5 秒内闭合");

        var finished = await (await admin.PostJson($"/api/v1/sys/job/log/{logId}/kill", new { })).ReadEnvelope();
        Assert.Equal(47007, finished.GetProperty("code").GetInt32());

        var missing = await (await admin.PostJson("/api/v1/sys/job/log/999999/kill", new { })).ReadEnvelope();
        Assert.Equal(47012, missing.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Clear_logs_keeps_running_rows()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var id = await AddJobAsync(admin, CronJob("t-clear"));
        await admin.PostJson($"/api/v1/sys/job/{id}/run", new { });
        for (var i = 0; i < 50; i++)
        {
            var logs = await (await admin.GetAsync($"/api/v1/sys/job/log/page?jobId={id}")).ReadEnvelope();
            if (logs.GetProperty("data").GetProperty("items").GetArrayLength() > 0) break;
            await Task.Delay(100);
        }

        var cleared = await (await admin.PostJson("/api/v1/sys/job/log/clear", new { jobId = id })).ReadEnvelope();
        Assert.Equal(0, cleared.GetProperty("code").GetInt32());
        Assert.True(cleared.GetProperty("data").GetInt32() >= 0);
    }

    [Fact]
    public async Task Dashboard_reports_jobs_and_nodes()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        await AddJobAsync(admin, CronJob("t-dash"));

        var body = await (await admin.GetAsync("/api/v1/sys/job/dashboard")).ReadEnvelope();
        Assert.Equal(0, body.GetProperty("code").GetInt32());
        var data = body.GetProperty("data");
        Assert.True(data.GetProperty("totalJobs").GetInt32() >= 2);            // 种子任务 + 本例
        Assert.Equal(14, data.GetProperty("trend").GetArrayLength());
        Assert.True(data.GetProperty("statusCounts").GetProperty("Ready").GetInt32() >= 1);
        Assert.NotEmpty(data.GetProperty("upcoming").EnumerateArray());
    }

    [Fact]
    public async Task Deleted_job_is_restored_as_paused()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var id = await AddJobAsync(admin, CronJob("t-recycle"));
        await admin.DeleteAsync($"/api/v1/sys/job/{id}");

        var bin = await (await admin.GetAsync("/api/v1/sys/recycle/job/page")).ReadEnvelope();
        Assert.Equal(0, bin.GetProperty("code").GetInt32());
        Assert.Contains(bin.GetProperty("data").GetProperty("items").EnumerateArray(),
            r => r.GetProperty("id").GetInt64() == id);

        var restore = await (await admin.PostJson($"/api/v1/sys/recycle/job/{id}/restore", new { })).ReadEnvelope();
        Assert.Equal(0, restore.GetProperty("code").GetInt32());

        var restored = await ReadJobAsync(admin, "t-recycle");
        Assert.Equal(2, restored.GetProperty("status").GetInt32());            // 强制 Paused(§13-3)
        Assert.Equal(JsonValueKind.Null, restored.GetProperty("nextRunTime").ValueKind);
    }

    [Fact]
    public async Task Demo_mode_blocks_run_once_with_41002()
    {
        // 任务能执行任意 HTTP/SQL,演示站绝不放行——这是特性不是缺陷(§13-2)
        using var f = new AdminAppFactory { Settings = new Dictionary<string, string?> { ["TenonAdmin:DemoMode"] = "true" } };
        var admin = await SuperAdminClient(f);
        var body = await (await admin.PostJson("/api/v1/sys/job/1/run", new { })).ReadEnvelope();
        Assert.Equal(41002, body.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Module_can_be_disabled_wholesale()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Job"] };
        var admin = await SuperAdminClient(f);
        var response = await admin.GetAsync("/api/v1/sys/job/page");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<JsonElement> ReadJobAsync(HttpClient admin, string code)
    {
        var page = await (await admin.GetAsync("/api/v1/sys/job/page?size=100")).ReadEnvelope();
        return page.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == code);
    }
}
