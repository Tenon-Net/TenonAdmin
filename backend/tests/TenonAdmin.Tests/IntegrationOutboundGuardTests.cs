using System.Net;
using TenonAdmin.Integration;

namespace TenonAdmin.Tests;

/// <summary>
/// 出站目标地址安全与配置校验(实现契约 §6.2):内部地址须显式受信,恒拒网段即使列入受信也不放行,
/// IPv4 映射 / NAT64 内嵌地址不能绕过;配置错误启动即失败,且消息不带秘密。
/// </summary>
public class IntegrationOutboundGuardTests
{
    [Theory]
    [InlineData("93.184.216.34", true)]      // 公网
    [InlineData("2606:4700::1111", true)]    // 公网 IPv6
    [InlineData("127.0.0.1", false)]         // 回环
    [InlineData("::1", false)]
    [InlineData("10.1.2.3", false)]          // 私网
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("100.64.0.9", false)]        // CGNAT
    [InlineData("fd12::1", false)]           // IPv6 唯一本地
    [InlineData("169.254.169.254", false)]   // 云元数据(链路本地)
    [InlineData("fe80::1", false)]
    [InlineData("0.0.0.0", false)]           // 未指定
    [InlineData("::", false)]
    [InlineData("224.0.0.1", false)]         // 组播
    [InlineData("ff02::1", false)]
    [InlineData("255.255.255.255", false)]   // 广播
    [InlineData("::ffff:169.254.169.254", false)]   // IPv4 映射
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("64:ff9b::a9fe:a9fe", false)]       // NAT64 内嵌 169.254.169.254
    [InlineData("64:ff9b::808:808", true)]          // NAT64 内嵌公网 8.8.8.8
    public void Untrusted_internal_and_special_addresses_are_refused_by_default(string address, bool allowed) =>
        Assert.Equal(allowed, OutboundAddressGuard.IsAllowed(IPAddress.Parse(address), []));

    [Fact]
    public void Trusted_cidrs_open_internal_ranges_but_never_the_always_blocked_ones()
    {
        string[] trusted = ["10.0.0.0/8", "127.0.0.1/32", "fd00::/8", "169.254.0.0/16", "224.0.0.0/4"];
        Assert.True(OutboundAddressGuard.IsAllowed(IPAddress.Parse("10.20.30.40"), trusted));
        Assert.True(OutboundAddressGuard.IsAllowed(IPAddress.Parse("127.0.0.1"), trusted));
        Assert.True(OutboundAddressGuard.IsAllowed(IPAddress.Parse("::ffff:10.0.0.1"), trusted));
        Assert.True(OutboundAddressGuard.IsAllowed(IPAddress.Parse("fd12::1"), trusted));
        Assert.False(OutboundAddressGuard.IsAllowed(IPAddress.Parse("127.0.0.2"), trusted));      // 只信任了单个地址
        Assert.False(OutboundAddressGuard.IsAllowed(IPAddress.Parse("192.168.0.1"), trusted));    // 未列入
        Assert.False(OutboundAddressGuard.IsAllowed(IPAddress.Parse("169.254.169.254"), trusted)); // 恒拒,列入也无效
        Assert.False(OutboundAddressGuard.IsAllowed(IPAddress.Parse("224.0.0.1"), trusted));
        Assert.False(OutboundAddressGuard.IsAllowed(IPAddress.Parse("64:ff9b::a9fe:a9fe"), trusted));
    }

    [Fact]
    public void Static_validation_rejects_non_http_schemes_userinfo_and_blocked_literals()
    {
        Assert.Throws<OutboundTargetBlockedException>(() => OutboundAddressGuard.Validate(new Uri("ftp://example.com/x"), []));
        Assert.Throws<OutboundTargetBlockedException>(() => OutboundAddressGuard.Validate(new Uri("http://user:pw@example.com/x"), []));
        Assert.Throws<OutboundTargetBlockedException>(() => OutboundAddressGuard.Validate(new Uri("http://169.254.169.254/latest"), []));
        Assert.Throws<OutboundTargetBlockedException>(() => OutboundAddressGuard.Validate(new Uri("http://[::1]:8080/x"), []));
        OutboundAddressGuard.Validate(new Uri("http://[::1]:8080/x"), ["::1/128"]);
        OutboundAddressGuard.Validate(new Uri("https://erp.example.com/api/"), []);   // 域名留到建连时逐个复检
    }

    [Fact]
    public void Target_configuration_errors_fail_fast_without_echoing_secrets()
    {
        static void Check(string expected, Action<OutboundTargetOptions> change)
        {
            var target = new OutboundTargetOptions { BaseUrl = "https://erp.example.com/api/", Secret = "s3cr3t-value" };
            change(target);
            var options = new IntegrationOptions();
            options.Outbound.Targets["erp"] = target;
            var ex = Assert.Throws<InvalidOperationException>(() => IntegrationOptionsValidation.Validate(options));
            Assert.Contains(expected, ex.Message);
            Assert.DoesNotContain("s3cr3t-value", ex.Message);
        }

        Check("Targets:erp:BaseUrl", t => t.BaseUrl = "ftp://erp.example.com/");
        Check("Targets:erp:BaseUrl", t => t.BaseUrl = "https://user:pw@erp.example.com/");
        Check("Targets:erp:BaseUrl", t => t.BaseUrl = "https://erp.example.com/api?token=x");
        Check("Targets:erp:BaseUrl", t => t.BaseUrl = "http://169.254.169.254/");
        Check("Targets:erp:BaseUrl", t => t.BaseUrl = "http://10.1.2.3/");
        Check("Targets:erp:BaseUrl", t => { t.BaseUrl = "http://169.254.169.254/"; t.TrustedCidrs = ["169.254.0.0/16"]; });
        Check("Targets:erp:TrustedCidrs", t => t.TrustedCidrs = ["10.0.0.0/33"]);
        Check("Targets:erp:TimeoutSeconds", t => t.TimeoutSeconds = 0);
        Check("Targets:erp:Auth:Type=Header", t => t.Auth = new OutboundAuthOptions { Type = OutboundAuthType.Header, HeaderName = "Bad Header" });
        Check("Targets:erp:Auth:Type=Basic", t => t.Auth = new OutboundAuthOptions { Type = OutboundAuthType.Basic });
        Check("Targets:erp:IdempotencyHeader", t => t.IdempotencyHeader = "Idem\r\nX");

        var bad = new IntegrationOptions();
        bad.Outbound.Targets["bad name"] = new OutboundTargetOptions { BaseUrl = "https://erp.example.com/" };
        Assert.Contains("目标名", Assert.Throws<InvalidOperationException>(() => IntegrationOptionsValidation.Validate(bad)).Message);

        // 内部地址显式受信后可用
        var trusted = new IntegrationOptions();
        trusted.Outbound.Targets["erp"] = new OutboundTargetOptions { BaseUrl = "http://10.1.2.3:8080/api", TrustedCidrs = ["10.1.2.0/24"] };
        IntegrationOptionsValidation.Validate(trusted);
        var target = OutboundTargetRegistry.FromOptions("erp", trusted.Outbound.Targets["erp"], trusted.Outbound);
        Assert.Equal("http://10.1.2.3:8080/api/", target.BaseUri.AbsoluteUri);   // 统一补齐结尾斜杠
        Assert.Equal(TimeSpan.FromSeconds(30), target.Timeout);
    }

    [Fact]
    public void Retry_after_accepts_seconds_and_http_dates()
    {
        var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(7), DefaultOutboundResultClassifier.ParseRetryAfter("7", now));
        Assert.Equal(TimeSpan.FromSeconds(90), DefaultOutboundResultClassifier.ParseRetryAfter(now.AddSeconds(90).ToString("r"), now));
        Assert.Equal(TimeSpan.Zero, DefaultOutboundResultClassifier.ParseRetryAfter(now.AddSeconds(-5).ToString("r"), now));
        Assert.Null(DefaultOutboundResultClassifier.ParseRetryAfter("soon", now));
        Assert.Null(DefaultOutboundResultClassifier.ParseRetryAfter("-3", now));
    }

    [Fact]
    public void Service_unavailable_is_always_unknown_and_retry_after_is_only_a_delay_hint()
    {
        var classifier = new DefaultOutboundResultClassifier(TimeProvider.System);
        OutboundClassification Classify(int status, string? retryAfter = null) =>
            classifier.Classify(new OutboundHttpResult(status, null, retryAfter is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = retryAfter }));

        var announced = Classify(503, "5");
        Assert.Equal((OutboundOutcome.Unknown, false, TimeSpan.FromSeconds(5)), (announced.Outcome, announced.Transient, announced.RetryAfter));
        // 网关在上游可能已执行后返回 503;Retry-After 只指示何时重试,不能证明请求未执行
        Assert.Equal((OutboundOutcome.Unknown, false), (Classify(503).Outcome, Classify(503).Transient));
        Assert.Equal(OutboundOutcome.Unknown, Classify(503, "soon").Outcome);
        // 429 明确是「没处理」:有没有 Retry-After 都可稍后重发
        Assert.Equal((OutboundOutcome.NotSent, true), (Classify(429).Outcome, Classify(429).Transient));
    }

    [Fact]
    public void Credential_values_never_render_their_secret()
    {
        var credential = new OutboundCredential("s3cr3t-value", "tenon");
        Assert.DoesNotContain("s3cr3t-value", credential.ToString());
        Assert.DoesNotContain("s3cr3t-value", $"{credential}");
    }
}
