using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="SmsOtpService"/> 机制粒度(同 <c>CaptchaServiceTests</c> 层级):
/// 码长与随机性、日发送上限、陪跑签发不存码、挑战票据生命周期。流程级行为见 Mfa/SmsLogin 两套流程测试。
/// </summary>
public class SmsOtpServiceTests
{
    private static (AdminAppFactory f, MfaLoginFlowTests.CapturingSmsSender sms) Factory()
    {
        var sms = new MfaLoginFlowTests.CapturingSmsSender();
        var f = new AdminAppFactory { Overrides = s => s.Replace(ServiceDescriptor.Singleton<ISmsSender>(sms)) };
        return (f, sms);
    }

    [Fact]
    public async Task Issue_sends_fixed_length_numeric_code_and_verify_consumes_it()
    {
        var (f, sms) = Factory();
        using var _ = f;
        using var scope = f.Services.CreateScope();
        var otp = scope.ServiceProvider.GetRequiredService<ISmsOtpService>();

        await otp.IssueAsync(ISmsOtpService.PURPOSE_LOGIN, "13800001234", "13800001234");
        Assert.Equal(6, sms.LastCode!.Length);
        Assert.True(sms.LastCode.All(char.IsAsciiDigit));

        await otp.VerifyAsync(ISmsOtpService.PURPOSE_LOGIN, "13800001234", sms.LastCode);   // 不抛 = 通过
        var replay = await Assert.ThrowsAsync<AdminException>(() =>
            otp.VerifyAsync(ISmsOtpService.PURPOSE_LOGIN, "13800001234", sms.LastCode));
        Assert.Equal(ErrorCode.SmsCodeExpired, replay.Code);   // 已消费
    }

    [Fact]
    public async Task Daily_send_limit_is_enforced_per_phone()
    {
        var (f, sms) = Factory();
        using var _ = f;
        using var scope = f.Services.CreateScope();
        var otp = scope.ServiceProvider.GetRequiredService<ISmsOtpService>();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheProvider>();

        // 默认日上限 10:直接把当日计数顶到上限,下一次签发应拒
        var day = DateTime.Now.ToString("yyyyMMdd");
        for (var i = 0; i < 10; i++) await cache.IncrementAsync(CacheKeys.SmsDailyCount("13800001234", day));

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            otp.IssueAsync(ISmsOtpService.PURPOSE_LOGIN, "13800001234", "13800001234"));
        Assert.Equal(ErrorCode.TooManyRequests, ex.Code);
        Assert.Equal(0, sms.SendCount);
    }

    [Fact]
    public async Task Pretend_issue_stores_no_code_but_sets_cooldown()
    {
        var (f, sms) = Factory();
        using var _ = f;
        using var scope = f.Services.CreateScope();
        var otp = scope.ServiceProvider.GetRequiredService<ISmsOtpService>();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheProvider>();

        var output = await otp.PretendIssueAsync("13999999999");
        Assert.True(output.ExpiresSeconds > 0);
        Assert.Equal(0, sms.SendCount);
        Assert.Null(await cache.GetAsync<string>(CacheKeys.SmsCode(ISmsOtpService.PURPOSE_LOGIN, "13999999999")));
        Assert.True(await cache.GetAsync<bool>(CacheKeys.SmsCooldown("13999999999")));
    }

    [Fact]
    public async Task Mfa_challenge_roundtrip_and_atomic_consume()
    {
        var (f, _) = Factory();
        using var __ = f;
        using var scope = f.Services.CreateScope();
        var otp = scope.ServiceProvider.GetRequiredService<ISmsOtpService>();

        var id = await otp.CreateMfaChallengeAsync(42);
        Assert.Equal(42, await otp.GetMfaChallengeAsync(id));    // 查看不消费
        Assert.Equal(42, await otp.ConsumeMfaChallengeAsync(id));
        Assert.Equal(0, await otp.ConsumeMfaChallengeAsync(id)); // 已消费 → 0
        Assert.Equal(0, await otp.GetMfaChallengeAsync("missing"));
    }
}
