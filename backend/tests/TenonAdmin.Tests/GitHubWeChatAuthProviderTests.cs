using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TenonAdmin.Auth.GitHub;
using TenonAdmin.Auth.WeChat;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>GitHub / WeChat 卫星包:假 HttpMessageHandler 覆盖 mapping、缺 subject、厂商错误(不触网)。</summary>
public class GitHubWeChatAuthProviderTests
{
    private sealed class SeqHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        public void Enqueue(HttpStatusCode status, string json) =>
            _steps.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            }));

        public void EnqueueThrow(Exception ex) =>
            _steps.Enqueue((_, _) => throw ex);

        public void EnqueueOnCancel() =>
            _steps.Enqueue(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_steps.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return _steps.Dequeue()(request, cancellationToken);
        }
    }

    private static ExternalExchangeRequest Ex(string code = "auth-code") =>
        new(code, "verifier", "https://app/cb", "nonce");

    [Fact]
    public void GitHub_code_is_fixed_github()
    {
        var p = new GitHubExternalAuthProvider(
            new GitHubAuthOptions { ClientId = "id", ClientSecret = "sec", DisplayName = "  " },
            new HttpClient(new SeqHandler()),
            NullLogger<GitHubExternalAuthProvider>.Instance);
        Assert.Equal("github", p.Code);
        Assert.Equal("GitHub", p.DisplayName);
    }

    [Fact]
    public async Task GitHub_exchange_maps_numeric_id_and_login()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"access_token":"tok","token_type":"bearer"}""");
        h.Enqueue(HttpStatusCode.OK, """{"id":12345,"login":"octocat","name":"The Octocat"}""");
        var p = new GitHubExternalAuthProvider(
            new GitHubAuthOptions { ClientId = "id", ClientSecret = "sec" },
            new HttpClient(h),
            NullLogger<GitHubExternalAuthProvider>.Instance);

        var id = await p.ExchangeAsync(Ex());
        Assert.Equal("github", id.Provider);
        Assert.Equal("12345", id.Subject);
        Assert.Equal("octocat", id.DisplayName);
        Assert.Null(id.Email);

        Assert.Equal(2, h.Requests.Count);
        Assert.Equal(HttpMethod.Post, h.Requests[0].Method);
        Assert.Contains("Bearer", h.Requests[1].Headers.Authorization?.ToString());
        // secret 不进 authorize 之外的 query
        Assert.DoesNotContain("sec", h.Requests[0].RequestUri!.Query);
        // 台账:token + /user 均须有效 User-Agent
        AssertGitHubUserAgent(h.Requests[0]);
        AssertGitHubUserAgent(h.Requests[1]);
    }

    private static void AssertGitHubUserAgent(HttpRequestMessage req)
    {
        Assert.True(req.Headers.Contains("User-Agent"), "GitHub request missing User-Agent");
        var ua = string.Join(" ", req.Headers.GetValues("User-Agent"));
        Assert.Contains(GitHubExternalAuthProvider.UserAgentValue, ua);
        Assert.DoesNotContain("sec", ua, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tok", ua, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHub_missing_id_fails()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"access_token":"tok"}""");
        h.Enqueue(HttpStatusCode.OK, """{"login":"x"}""");
        var p = new GitHubExternalAuthProvider(
            new GitHubAuthOptions { ClientId = "id", ClientSecret = "sec" },
            new HttpClient(h),
            NullLogger<GitHubExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Theory]
    [InlineData("""{"id":"not-a-number","login":"x"}""")]
    [InlineData("""{"id":0,"login":"x"}""")]
    [InlineData("""{"id":-1,"login":"x"}""")]
    [InlineData("""{"id":true,"login":"x"}""")]
    public async Task GitHub_invalid_id_shapes_fail(string userJson)
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"access_token":"tok"}""");
        h.Enqueue(HttpStatusCode.OK, userJson);
        var p = new GitHubExternalAuthProvider(
            new GitHubAuthOptions { ClientId = "id", ClientSecret = "sec" },
            new HttpClient(h),
            NullLogger<GitHubExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task GitHub_string_id_is_normalized_decimal()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"access_token":"tok"}""");
        h.Enqueue(HttpStatusCode.OK, """{"id":"00123","login":"z"}""");
        var p = new GitHubExternalAuthProvider(
            new GitHubAuthOptions { ClientId = "id", ClientSecret = "sec" },
            new HttpClient(h),
            NullLogger<GitHubExternalAuthProvider>.Instance);

        var id = await p.ExchangeAsync(Ex());
        Assert.Equal("123", id.Subject);
    }

    [Fact]
    public void ParseGitHubUserId_rejects_non_numeric_string()
    {
        using var doc = JsonDocument.Parse("""{"id":"abc"}""");
        Assert.Throws<AdminException>(() =>
            GitHubExternalAuthProvider.ParseGitHubUserId(doc.RootElement.GetProperty("id")));
    }

    [Fact]
    public async Task GitHub_malformed_json_maps_to_exchange_failed()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, "not-json{");
        var p = new GitHubExternalAuthProvider(
            new GitHubAuthOptions { ClientId = "id", ClientSecret = "sec" },
            new HttpClient(h),
            NullLogger<GitHubExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task GitHub_http_exception_maps_to_exchange_failed()
    {
        var h = new SeqHandler();
        h.EnqueueThrow(new HttpRequestException("network down"));
        var p = new GitHubExternalAuthProvider(
            new GitHubAuthOptions { ClientId = "id", ClientSecret = "sec" },
            new HttpClient(h),
            NullLogger<GitHubExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task GitHub_caller_cancellation_propagates()
    {
        var h = new SeqHandler();
        h.EnqueueOnCancel();
        var p = new GitHubExternalAuthProvider(
            new GitHubAuthOptions { ClientId = "id", ClientSecret = "sec" },
            new HttpClient(h),
            NullLogger<GitHubExternalAuthProvider>.Instance);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p.ExchangeAsync(Ex(), cts.Token));
    }

    [Fact]
    public async Task GitHub_token_http_error_fails()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.BadRequest, """{"error":"bad"}""");
        var p = new GitHubExternalAuthProvider(
            new GitHubAuthOptions { ClientId = "id", ClientSecret = "sec" },
            new HttpClient(h),
            NullLogger<GitHubExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task GitHub_authorize_url_has_read_user_scope_only()
    {
        var p = new GitHubExternalAuthProvider(
            new GitHubAuthOptions { ClientId = "cid", ClientSecret = "sec" },
            new HttpClient(new SeqHandler()),
            NullLogger<GitHubExternalAuthProvider>.Instance);
        var url = await p.BuildAuthorizeUrlAsync(new ExternalAuthorizeRequest("st", "n", "ch", "https://cb"));
        Assert.Contains("client_id=cid", url);
        Assert.Contains("scope=read%3Auser", url);
        Assert.DoesNotContain("user:email", url);
    }

    [Fact]
    public void WeChat_code_is_fixed_wechat()
    {
        var p = new WeChatExternalAuthProvider(
            new WeChatAuthOptions { AppId = "a", AppSecret = "s", DisplayName = "" },
            new HttpClient(new SeqHandler()),
            NullLogger<WeChatExternalAuthProvider>.Instance);
        Assert.Equal("wechat", p.Code);
        Assert.Equal("微信", p.DisplayName);
    }

    [Fact]
    public async Task WeChat_exchange_requires_unionid()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"openid":"o1","access_token":"t"}""");
        var p = new WeChatExternalAuthProvider(
            new WeChatAuthOptions { AppId = "a", AppSecret = "s" },
            new HttpClient(h),
            NullLogger<WeChatExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task WeChat_exchange_maps_unionid_only()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"openid":"o1","unionid":"u-99","access_token":"t"}""");
        var p = new WeChatExternalAuthProvider(
            new WeChatAuthOptions { AppId = "a", AppSecret = "s" },
            new HttpClient(h),
            NullLogger<WeChatExternalAuthProvider>.Instance);

        var id = await p.ExchangeAsync(Ex());
        Assert.Equal("wechat", id.Provider);
        Assert.Equal("u-99", id.Subject);
        Assert.Null(id.DisplayName);
    }

    [Fact]
    public async Task WeChat_authorize_url_is_qrconnect()
    {
        var p = new WeChatExternalAuthProvider(
            new WeChatAuthOptions { AppId = "wxapp", AppSecret = "s" },
            new HttpClient(new SeqHandler()),
            NullLogger<WeChatExternalAuthProvider>.Instance);
        var url = await p.BuildAuthorizeUrlAsync(new ExternalAuthorizeRequest("st", "n", "ch", "https://cb"));
        Assert.StartsWith("https://open.weixin.qq.com/connect/qrconnect?", url);
        Assert.Contains("appid=wxapp", url);
        Assert.Contains("scope=snsapi_login", url);
        Assert.EndsWith("#wechat_redirect", url);
    }

    [Fact]
    public async Task WeChat_errcode_fails()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"errcode":40029,"errmsg":"invalid code"}""");
        var p = new WeChatExternalAuthProvider(
            new WeChatAuthOptions { AppId = "a", AppSecret = "s" },
            new HttpClient(h),
            NullLogger<WeChatExternalAuthProvider>.Instance);
        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task WeChat_malformed_json_maps_to_exchange_failed()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, "<html>nope");
        var p = new WeChatExternalAuthProvider(
            new WeChatAuthOptions { AppId = "a", AppSecret = "s" },
            new HttpClient(h),
            NullLogger<WeChatExternalAuthProvider>.Instance);
        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task WeChat_http_exception_maps_to_exchange_failed()
    {
        var h = new SeqHandler();
        h.EnqueueThrow(new HttpRequestException("dns"));
        var p = new WeChatExternalAuthProvider(
            new WeChatAuthOptions { AppId = "a", AppSecret = "s" },
            new HttpClient(h),
            NullLogger<WeChatExternalAuthProvider>.Instance);
        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task WeChat_caller_cancellation_propagates()
    {
        var h = new SeqHandler();
        h.EnqueueOnCancel();
        var p = new WeChatExternalAuthProvider(
            new WeChatAuthOptions { AppId = "a", AppSecret = "s" },
            new HttpClient(h),
            NullLogger<WeChatExternalAuthProvider>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p.ExchangeAsync(Ex(), cts.Token));
    }

    [Fact]
    public void AddTenonAdminGitHubAuth_registers_fixed_code_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // 真实 DI 路径:配置有 ClientId+Secret 时必须能注册并可解析,不能 ArgumentException
        services.AddTenonAdminGitHubAuth(new GitHubAuthOptions
        {
            ClientId = "cid",
            ClientSecret = "csec",
            DisplayName = "GH",
        });

        using var sp = services.BuildServiceProvider();
        var providers = sp.GetServices<IExternalAuthProvider>().ToList();
        Assert.Contains(providers, p => p.Code == GitHubExternalAuthProvider.FixedCode);
        var gh = Assert.Single(providers, p => p.Code == "github");
        Assert.IsType<GitHubExternalAuthProvider>(gh);
        Assert.Equal("GH", gh.DisplayName);
    }

    [Fact]
    public void AddTenonAdminWeChatAuth_registers_fixed_code_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenonAdminWeChatAuth(new WeChatAuthOptions
        {
            AppId = "wxapp",
            AppSecret = "wxsec",
        });

        using var sp = services.BuildServiceProvider();
        var providers = sp.GetServices<IExternalAuthProvider>().ToList();
        Assert.Contains(providers, p => p.Code == WeChatExternalAuthProvider.FixedCode);
        var wx = Assert.Single(providers, p => p.Code == "wechat");
        Assert.IsType<WeChatExternalAuthProvider>(wx);
        Assert.Equal("微信", wx.DisplayName);
    }

    [Fact]
    public void AddTenonAdminGitHubAuth_from_configuration_is_noop_without_client_id()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TenonAdmin:ExternalAuth:GitHub:ClientId"] = "",
        }).Build();
        services.AddTenonAdminGitHubAuth(cfg);
        using var sp = services.BuildServiceProvider();
        Assert.Empty(sp.GetServices<IExternalAuthProvider>());
    }

    [Fact]
    public void AddTenonAdminGitHubAuth_from_configuration_registers_when_configured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TenonAdmin:ExternalAuth:GitHub:ClientId"] = "from-cfg",
            ["TenonAdmin:ExternalAuth:GitHub:ClientSecret"] = "secret",
        }).Build();
        services.AddTenonAdminGitHubAuth(cfg);
        using var sp = services.BuildServiceProvider();
        Assert.Contains(sp.GetServices<IExternalAuthProvider>(), p => p.Code == "github");
    }
}
