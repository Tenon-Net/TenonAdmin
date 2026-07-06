using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// JWT 签名密钥解析(设计 §3.1 零配置默认行为 / §14 安全基线)。
/// <para>两种模式:</para>
/// <list type="bullet">
///   <item><b>已配置 SecretKey</b>(生产要求):直接使用,长度不足 32 字节立即抛错(弱密钥不给跑)。</item>
///   <item><b>未配置</b>(开发模式):生成 64 字节加密随机密钥并持久化到 <c>./data/dev-jwt.key</c>——
///   重启后签验密钥不变、已发令牌不失效;同时打印醒目警告。密钥落在 data 目录(与 SQLite 同处),
///   天然进 .gitignore 不会被提交。</item>
/// </list>
/// </summary>
internal static class JwtKeyResolver
{
    /// <summary>开发密钥文件路径(相对进程工作目录,与默认 SQLite 的 ./data 同目录)</summary>
    private const string DEV_KEY_PATH = "./data/dev-jwt.key";

    /// <summary>HS256 的最小安全密钥长度(字节)</summary>
    private const int MIN_KEY_BYTES = 32;

    public static SymmetricSecurityKey Resolve(AdminJwtOptions options, ILogger logger)
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

        // 模式二:开发密钥(持久化保证重启后令牌仍有效)
        byte[] keyBytes;
        if (File.Exists(DEV_KEY_PATH))
        {
            keyBytes = Convert.FromBase64String(File.ReadAllText(DEV_KEY_PATH).Trim());
        }
        else
        {
            keyBytes = RandomNumberGenerator.GetBytes(64);
            Directory.CreateDirectory(Path.GetDirectoryName(DEV_KEY_PATH)!);
            File.WriteAllText(DEV_KEY_PATH, Convert.ToBase64String(keyBytes));
        }

        logger.LogWarning(
            "TenonAdmin: 未配置 TenonAdmin:Jwt:SecretKey,正在使用开发密钥({Path})。" +
            "生产环境必须显式配置签名密钥!", DEV_KEY_PATH);

        return new SymmetricSecurityKey(keyBytes);
    }
}
