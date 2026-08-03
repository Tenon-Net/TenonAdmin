using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TenonAdmin.Auth.DingTalk;
using TenonAdmin.Auth.WeCom;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>企微 / 钉钉卫星包:假 HttpMessageHandler 覆盖 happy path 与异常映射(对齐 GitHub 加固,不触网)。</summary>
public class WeComDingTalkAuthProviderTests
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
    public async Task WeCom_exchange_maps_userid()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"errcode":0,"access_token":"tok","expires_in":7200}""");
        h.Enqueue(HttpStatusCode.OK, """{"errcode":0,"userid":"zhangsan"}""");
        var p = new WeComExternalAuthProvider(
            new WeComAuthOptions { CorpId = "c", AgentId = "1", CorpSecret = "s" },
            new HttpClient(h),
            NullLogger<WeComExternalAuthProvider>.Instance);

        var id = await p.ExchangeAsync(Ex());
        Assert.Equal("wecom", id.Provider);
        Assert.Equal("zhangsan", id.Subject);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task WeCom_http_failure_maps_to_oauth_exchange_failed()
    {
        var h = new SeqHandler();
        h.EnqueueThrow(new HttpRequestException("net"));
        var p = new WeComExternalAuthProvider(
            new WeComAuthOptions { CorpId = "c", AgentId = "1", CorpSecret = "s" },
            new HttpClient(h),
            NullLogger<WeComExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task WeCom_missing_userid_fails()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"errcode":0,"access_token":"tok","expires_in":7200}""");
        h.Enqueue(HttpStatusCode.OK, """{"errcode":0}""");
        var p = new WeComExternalAuthProvider(
            new WeComAuthOptions { CorpId = "c", AgentId = "1", CorpSecret = "s" },
            new HttpClient(h),
            NullLogger<WeComExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task DingTalk_exchange_maps_unionId_and_nick()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"accessToken":"utok"}""");
        h.Enqueue(HttpStatusCode.OK, """{"unionId":"u1","nick":"钉钉用户","email":"a@b.c"}""");
        var p = new DingTalkExternalAuthProvider(
            new DingTalkAuthOptions { AppKey = "k", AppSecret = "s" },
            new HttpClient(h),
            NullLogger<DingTalkExternalAuthProvider>.Instance);

        var id = await p.ExchangeAsync(Ex());
        Assert.Equal("dingtalk", id.Provider);
        Assert.Equal("u1", id.Subject);
        Assert.Equal("钉钉用户", id.DisplayName);
        Assert.Equal("a@b.c", id.Email);
        Assert.Equal(2, h.Requests.Count);
        Assert.Contains("x-acs-dingtalk-access-token", h.Requests[1].Headers.Select(x => x.Key));
    }

    [Fact]
    public async Task DingTalk_http_failure_maps_to_oauth_exchange_failed()
    {
        var h = new SeqHandler();
        h.EnqueueThrow(new HttpRequestException("net"));
        var p = new DingTalkExternalAuthProvider(
            new DingTalkAuthOptions { AppKey = "k", AppSecret = "s" },
            new HttpClient(h),
            NullLogger<DingTalkExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task DingTalk_authorize_url_contains_client_and_state()
    {
        var p = new DingTalkExternalAuthProvider(
            new DingTalkAuthOptions { AppKey = "appk", AppSecret = "s" },
            new HttpClient(new SeqHandler()),
            NullLogger<DingTalkExternalAuthProvider>.Instance);
        var url = await p.BuildAuthorizeUrlAsync(new ExternalAuthorizeRequest("st", "n", "ch", "https://cb"));
        Assert.Contains("client_id=appk", url);
        Assert.Contains("state=st", url);
        Assert.Contains("redirect_uri=", url);
    }
}
