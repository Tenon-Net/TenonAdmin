using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 密码哈希(§14)。重点锁死畸形/外部导入哈希串的解析健壮性:任何坏格式都必须按"不匹配"返回 false、
/// 绝不抛异常、更绝不误判通过——尤其空哈希段曾使 FixedTimeEquals(空,空)==true 令任意密码通过(P2-13)。
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    private static readonly Pbkdf2PasswordHasher Hasher = new();

    [Fact]
    public void Hash_then_verify_round_trips()
    {
        var h = Hasher.Hash("S3cret!pass");
        Assert.True(Hasher.Verify("S3cret!pass", h));
        Assert.False(Hasher.Verify("wrong", h));
    }

    [Fact]
    public void Same_password_hashes_differ_by_salt()
    {
        Assert.NotEqual(Hasher.Hash("same"), Hasher.Hash("same"));
    }

    [Theory]
    // 空终段:历史上会让任意密码通过——必须 false
    [InlineData("pbkdf2-sha256.600000.YWJjZGVmZ2hpamtsbW5v.")]
    // 空盐段
    [InlineData("pbkdf2-sha256.600000..YWJjZGVmZ2hpamtsbW5v")]
    // 段数不足 / 过多
    [InlineData("pbkdf2-sha256.600000.YWJj")]
    [InlineData("pbkdf2-sha256.600000.YWJj.ZGVm.extra")]
    // 未知算法
    [InlineData("argon2.600000.YWJj.ZGVm")]
    // 迭代次数非法
    [InlineData("pbkdf2-sha256.0.YWJj.ZGVm")]
    [InlineData("pbkdf2-sha256.-5.YWJj.ZGVm")]
    [InlineData("pbkdf2-sha256.notnum.YWJj.ZGVm")]
    // 非 base64 段
    [InlineData("pbkdf2-sha256.600000.@@@@.ZGVm")]
    // 完全无关的串
    [InlineData("")]
    [InlineData("plaintext-password")]
    public void Malformed_or_empty_segment_returns_false_without_throwing(string stored)
    {
        // 关键断言:任意密码(含空)对畸形哈希都不通过,且不抛异常
        Assert.False(Hasher.Verify("anything", stored));
        Assert.False(Hasher.Verify("", stored));
    }
}
