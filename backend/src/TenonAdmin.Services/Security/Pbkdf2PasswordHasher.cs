using System.Security.Cryptography;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IPasswordHasher"/> 默认实现:PBKDF2-SHA256(BCL <see cref="Rfc2898DeriveBytes"/>,零第三方依赖,设计 §14)。
/// <para>产出格式(自描述,点号分隔):<c>pbkdf2-sha256.{迭代次数}.{盐Base64}.{哈希Base64}</c></para>
/// <para>自描述的意义:算法与参数存进哈希串本身——将来把迭代次数加大、甚至换算法(argon2 等),
/// 旧哈希仍按其记录的参数校验通过,用户无感;需要时可在登录成功后按新参数重哈希平滑升级。</para>
/// <para>方法 virtual:对接已有用户库时继承并只改 <see cref="Verify"/> 即可兼容旧哈希格式(设计 §5.3)。</para>
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string ALGORITHM = "pbkdf2-sha256";
    private const int SALT_SIZE = 16;   // 128 位随机盐
    private const int HASH_SIZE = 32;   // 256 位哈希

    /// <summary>
    /// 迭代次数,OWASP 对 PBKDF2-SHA256 的当前建议值(60 万)。
    /// 单次计算约几十到一百毫秒——对登录接口是可接受的故意开销(暴力破解者要按次付费)。
    /// ponytail: 固定常量;需要按硬件调节时再升级为配置项(旧哈希不受影响,见类注释)。
    /// </summary>
    private const int ITERATIONS = 600_000;

    /// <inheritdoc />
    public virtual string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SALT_SIZE);   // 每次全新随机盐 → 同一明文两次哈希结果不同
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, ITERATIONS, HashAlgorithmName.SHA256, HASH_SIZE);
        return $"{ALGORITHM}.{ITERATIONS}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <inheritdoc />
    public virtual bool Verify(string password, string hashedPassword)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword)) return false;

        // 解析自描述格式;库中出现未知算法/坏格式按"不匹配"处理而非抛异常——登录路径要稳
        var parts = hashedPassword.Split('.');
        if (parts.Length != 4 || parts[0] != ALGORITHM) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }

        // 按哈希串里记录的参数重算(而非当前常量)→ 参数升级后旧哈希依旧可校验
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // 恒定时间比较,防时序侧信道(设计 §14 要求,普通 == 会在首个不同字节提前返回)
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
