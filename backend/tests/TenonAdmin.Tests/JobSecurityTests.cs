using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 定时任务的安全面回归(scheduling-ledger §7.1/§13-1;2026-07-26 三视角审查抓出的实证问题逐条钉死)。
/// 每条都对应一个曾经真实存在的绕过,删掉对应防线本类必红。
/// </summary>
public class JobSecurityTests
{
    private static readonly AdminJobsHttpOptions DefaultHttp = new();

    // ── 围栏:IP 判定 ────────────────────────────────────────────────

    [Theory]
    // 云元数据(IPv4)与它的各种归一化写法:.NET 的 Uri 会把非规范 IPv4 折成点分十进制
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://2852039166/")]
    [InlineData("http://0251.0376.0251.0376/")]
    [InlineData("http://[::ffff:169.254.169.254]/")]
    // 云元数据的 IPv6 形态(AWS IMDS)与链路本地——169.254/16 的 IPv6 孪生
    [InlineData("http://[fd00:ec2::254]/latest/meta-data/")]
    [InlineData("http://[fe80::1]/")]
    // 协议白名单
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    [InlineData("not-a-url")]
    public void Fenced_urls_are_rejected(string url)
    {
        var ex = Assert.Throws<AdminException>(() => JobHttpFence.ValidateUrl(url, DefaultHttp));
        Assert.Equal(ErrorCode.JobHttpUrlBlocked, ex.Code);
    }

    [Theory]
    [InlineData("http://10.0.0.5/health")]          // 内网是主用途,默认放行
    [InlineData("http://192.168.1.10:8080/ping")]
    [InlineData("http://[fd12:3456::1]/")]          // ULA 不封
    [InlineData("https://example.com/webhook")]
    public void Ordinary_targets_pass(string url) => JobHttpFence.ValidateUrl(url, DefaultHttp);

    [Fact]
    public void Allowed_hosts_whitelist_is_fail_closed()
    {
        var http = new AdminJobsHttpOptions { AllowedHosts = ["ok.example.com"] };
        JobHttpFence.ValidateUrl("https://ok.example.com/x", http);
        Assert.Throws<AdminException>(() => JobHttpFence.ValidateUrl("https://evil.example.com/x", http));
        // userinfo 不能冒充主机
        Assert.Throws<AdminException>(() => JobHttpFence.ValidateUrl("https://ok.example.com@evil.example.com/x", http));
    }

    [Theory]
    [InlineData("169.254.0.0/16", "169.254.169.254", true)]
    [InlineData("169.254.0.0/16", "169.253.1.1", false)]
    [InlineData("169.254.169.254", "169.254.169.254", true)]     // 无斜杠 = 单地址,不再静默失效
    [InlineData("fd00:ec2::/32", "fd00:ec2::254", true)]
    [InlineData("fe80::/10", "fe80::1", true)]
    [InlineData("fe80::/10", "fd12:3456::1", false)]
    [InlineData("0.0.0.0/0", "8.8.8.8", true)]
    public void Cidr_matching_is_correct(string cidr, string ip, bool expected) =>
        Assert.Equal(expected, JobHttpFence.IsBlocked(IPAddress.Parse(ip), [cidr]));

    [Theory]
    [InlineData("169.254.0.0/16", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("fd00:ec2::/32", true)]
    [InlineData("169.254.0.0/", false)]
    [InlineData("169.254.0.0/33", false)]
    [InlineData("not-an-ip/16", false)]
    public void Malformed_cidr_is_detected_not_silently_ignored(string cidr, bool valid) =>
        Assert.Equal(valid, JobHttpFence.TryParseCidr(cidr, out _, out _));

    // ── 围栏:请求头 CRLF 走私 ──────────────────────────────────────

    [Theory]
    [InlineData("X-Evil", "a\r\n\r\nGET /admin HTTP/1.1\r\nHost: internal")]   // 走私第二个请求
    [InlineData("X-Evil", "a\nX-Injected: 1")]
    [InlineData("X-Evil", "a\0b")]
    [InlineData("Bad Name", "v")]                                              // 名字含空格
    [InlineData("Bad:Name", "v")]
    [InlineData("", "v")]
    public void Header_injection_is_rejected(string name, string value)
    {
        var ex = Assert.Throws<AdminException>(() => JobHttpFence.ValidateHeader(name, value));
        Assert.Equal(ErrorCode.JobPropsInvalid, ex.Code);
    }

    [Theory]
    [InlineData("Authorization", "Bearer abc.def")]
    [InlineData("X-Trace", "a\tb")]     // 制表符是合法头值字符
    [InlineData("Accept", null)]
    public void Ordinary_headers_pass(string name, string? value) => JobHttpFence.ValidateHeader(name, value);

    // ── 端到端:入库侧的三道闸 ──────────────────────────────────────

    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Crlf_in_headers_is_rejected_at_save_time()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var body = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-crlf",
            name = "走私",
            handlerKind = 2,
            handlerName = "",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
            properties = new Dictionary<string, string?>
            {
                ["url"] = "http://10.0.0.5/ok",
                ["headers"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["X-Evil"] = "a\r\nHost: internal" }),
            },
        })).ReadEnvelope();
        Assert.Equal(47011, body.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Compiled_kind_cannot_impersonate_built_in_handlers()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // 选 Compiled + 填内置 HTTP 处理器全名 = 绕过入库侧围栏;必须拒
        var http = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-impersonate-http",
            name = "冒充 HTTP",
            handlerKind = 1,
            handlerName = "TenonAdmin.Services.HttpAdminJob",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
            properties = new Dictionary<string, string?> { ["url"] = "http://169.254.169.254/" },
        })).ReadEnvelope();
        Assert.Equal(47011, http.GetProperty("code").GetInt32());

        // SQL 版同理:总闸关着也能靠这条路把行存进库,等哪天打开就能跑
        var sql = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-impersonate-sql",
            name = "冒充 SQL",
            handlerKind = 1,
            handlerName = "TenonAdmin.Services.SqlAdminJob",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
            properties = new Dictionary<string, string?> { ["sql"] = "DELETE FROM sys_user" },
        })).ReadEnvelope();
        Assert.Equal(47011, sql.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Header_secrets_are_masked_on_read_and_preserved_on_write()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var add = await (await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-secret",
            name = "带密钥的任务",
            handlerKind = 2,
            handlerName = "",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
            properties = new Dictionary<string, string?>
            {
                ["url"] = "http://10.0.0.5/ok",
                ["headers"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["Authorization"] = "Bearer super-secret" }),
            },
        })).ReadEnvelope();
        var id = add.GetProperty("data").GetInt64();

        // 读:任务读权限弱于编辑权,列表里绝不能出现明文密钥
        var masked = await ReadPropsAsync(admin, "t-secret");
        Assert.DoesNotContain("super-secret", masked);
        Assert.Contains(JobService.SecretMask, masked);

        // 写:前端把掩码原样回传 = 没改这条,库里原值必须保住
        var maskedProps = JsonSerializer.Deserialize<Dictionary<string, string?>>(masked)!;
        var update = await (await admin.PutJson($"/api/v1/sys/job/{id}", new
        {
            code = "t-secret",
            name = "带密钥的任务(改名)",
            handlerKind = 2,
            handlerName = "",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
            properties = maskedProps,
        })).ReadEnvelope();
        Assert.Equal(0, update.GetProperty("code").GetInt32());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var stored = await db.Queryable<SysJob>().Where(j => j.Id == id).Select(j => j.PropsJson).FirstAsync();
        Assert.Contains("super-secret", stored);          // 原值还在
        Assert.DoesNotContain(JobService.SecretMask, stored);   // 掩码没被当成密钥存回去
    }

    [Fact]
    public async Task Operation_log_does_not_leak_header_secrets()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        await admin.PostJson("/api/v1/sys/job", new
        {
            code = "t-oplog",
            name = "看操作日志",
            handlerKind = 2,
            handlerName = "",
            triggerKind = 1,
            cronExpression = "0 30 3 * * ?",
            properties = new Dictionary<string, string?>
            {
                ["url"] = "http://10.0.0.5/ok",
                ["headers"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["Authorization"] = "Bearer oplog-secret" }),
            },
        });

        var logs = await (await admin.GetAsync("/api/v1/sys/log/op/page?size=20")).ReadEnvelope();
        var text = logs.ToString();
        Assert.DoesNotContain("oplog-secret", text);
    }

    private static async Task<string> ReadPropsAsync(HttpClient admin, string code)
    {
        var page = await (await admin.GetAsync("/api/v1/sys/job/page?size=100")).ReadEnvelope();
        return page.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == code)
            .GetProperty("propsJson").GetString()!;
    }
}
