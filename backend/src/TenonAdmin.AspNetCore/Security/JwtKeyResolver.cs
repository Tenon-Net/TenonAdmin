using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// JWT 签名密钥解析(设计 §3.1 零配置默认行为 / §14 安全基线)。
/// <para>三条路径:</para>
/// <list type="bullet">
///   <item><b>已配置 SecretKey</b>(生产要求):直接使用,长度不足 32 字节立即抛错(弱密钥不给跑)。</item>
///   <item><b>未配置 + 生产环境</b>:<b>直接抛错、拒绝启动</b>(fail-fast)——生产缺密钥若静默用自动生成的
///   开发密钥,多副本会各签各的密钥导致随机 401,且密钥泄露即可伪造超管令牌(设计 §14)。</item>
///   <item><b>未配置 + 开发环境</b>:生成 64 字节加密随机密钥并持久化到 <c>{ContentRoot}/data/dev-jwt.key</c>——
///   重启后签验密钥不变、已发令牌不失效;同时打印醒目警告。密钥落在 data 目录(与 SQLite 同处),
///   天然进 .gitignore 不会被提交。路径相对 ContentRoot(非进程 CWD),从非项目目录/服务托管启动也稳定。</item>
/// </list>
/// </summary>
internal static class JwtKeyResolver
{
    /// <summary>开发密钥文件名(置于 ContentRoot 下的 data 目录,与默认 SQLite 同处)</summary>
    private const string DEV_KEY_FILE = "dev-jwt.key";

    /// <summary>HS256 的最小安全密钥长度(字节)</summary>
    private const int MIN_KEY_BYTES = 32;

    public static SymmetricSecurityKey Resolve(AdminJwtOptions options, IHostEnvironment env, ILogger logger)
    {
        // 模式一:显式配置(生产路径)
        if (!string.IsNullOrEmpty(options.SecretKey))
        {
            var bytes = Encoding.UTF8.GetBytes(options.SecretKey);
            if (bytes.Length < MIN_KEY_BYTES)
                throw new InvalidOperationException(
                    $"TenonAdmin:Jwt:SecretKey 过短({bytes.Length} 字节 < {MIN_KEY_BYTES} 字节)。" +
                    "弱密钥可被暴力破解进而伪造任意用户令牌,请配置至少 32 字节的随机串。");
            return new SymmetricSecurityKey(bytes);
        }

        // 模式二:未配置 + 生产 → fail-fast(生产绝不允许开发密钥,设计 §14)
        if (!env.IsDevelopment())
            throw new InvalidOperationException(
                "生产环境必须显式配置 TenonAdmin:Jwt:SecretKey(至少 32 字节随机串);" +
                "禁止使用自动生成的开发密钥——否则多副本密钥不一致会随机 401,密钥泄露可伪造任意用户令牌(设计 §14)。");

        // 模式三:开发密钥(相对 ContentRoot 持久化,保证重启后令牌仍有效)
        var keyPath = Path.Combine(env.ContentRootPath, "data", DEV_KEY_FILE);
        byte[] keyBytes;
        if (File.Exists(keyPath))
        {
            keyBytes = Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
        }
        else
        {
            keyBytes = RandomNumberGenerator.GetBytes(64);
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            File.WriteAllText(keyPath, Convert.ToBase64String(keyBytes));
        }

        logger.LogWarning(
            "TenonAdmin: 未配置 TenonAdmin:Jwt:SecretKey,正在使用开发密钥({Path})。" +
            "生产环境必须显式配置签名密钥!", keyPath);

        return new SymmetricSecurityKey(keyBytes);
    }
}
