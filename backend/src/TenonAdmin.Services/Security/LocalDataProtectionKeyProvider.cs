using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IDataProtectionKeyProvider"/> 默认实现:从 <c>TenonAdmin:Security:DataProtection:Key</c>(Base64)
/// 读取主密钥;未配置时仅在开发环境自动生成并落盘到 <c>{ContentRoot}/data/dev-dataprotection.key</c>。
/// Level3 必须显式配置密钥——缺配在 <see cref="GetCurrentKey"/> 时抛 <see cref="InvalidOperationException"/>
/// (预检/启动将映射为 Level3Misconfigured)。
/// </summary>
public class LocalDataProtectionKeyProvider : IDataProtectionKeyProvider
{
    private const string DevKeyFile = "dev-dataprotection.key";
    private const int MinKeyBytes = 32;

    private readonly DataProtectionKeyMaterial _current;
    private readonly Dictionary<int, DataProtectionKeyMaterial> _byVersion;

    /// <summary>
    /// 构造并解析密钥材料。
    /// </summary>
    /// <param name="security">安全 Options(含 DataProtection 与 Profile)</param>
    /// <param name="contentRoot">内容根(开发自动密钥落盘基准);可空则无法自动落盘</param>
    /// <param name="isDevelopment">是否开发环境</param>
    /// <param name="logger">可选日志</param>
    public LocalDataProtectionKeyProvider(
        AdminSecurityOptions security,
        string? contentRoot,
        bool isDevelopment,
        ILogger? logger = null)
    {
        var opts = security.DataProtection ?? new AdminDataProtectionOptions();
        var version = opts.KeyVersion > 0 ? opts.KeyVersion : 1;

        byte[] keyBytes;
        if (!string.IsNullOrWhiteSpace(opts.Key))
        {
            try
            {
                keyBytes = Convert.FromBase64String(opts.Key.Trim());
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "TenonAdmin:Security:DataProtection:Key 不是合法 Base64。", ex);
            }

            if (keyBytes.Length < MinKeyBytes)
                throw new InvalidOperationException(
                    $"TenonAdmin:Security:DataProtection:Key 过短({keyBytes.Length} 字节 < {MinKeyBytes} 字节)。" +
                    "请配置至少 32 字节随机密钥的 Base64。");
        }
        else if (security.IsLegacyLevel3Profile
                 || ((security.Totp.Enabled || security.Session.CookieMode) && !isDevelopment))
        {
            // 历史 Level3、或生产启用 TOTP/Cookie 时必须显式主密钥
            throw new InvalidOperationException(
                "启用 TOTP/Cookie 会话或历史 Profile=Level3 时必须显式配置 " +
                "TenonAdmin:Security:DataProtection:Key(Base64,≥32 字节);禁止使用自动开发密钥。");
        }
        else if (isDevelopment && !string.IsNullOrEmpty(contentRoot))
        {
            var keyPath = Path.Combine(contentRoot, "data", DevKeyFile);
            if (File.Exists(keyPath))
            {
                keyBytes = Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
            }
            else
            {
                keyBytes = RandomNumberGenerator.GetBytes(32);
                Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
                File.WriteAllText(keyPath, Convert.ToBase64String(keyBytes));
            }

            logger?.LogWarning(
                "TenonAdmin: 未配置 TenonAdmin:Security:DataProtection:Key,正在使用开发密钥({Path})。" +
                "生产 / Level3 必须显式配置数据保护主密钥。", keyPath);
        }
        else
        {
            // 非开发且非 Level3:仍生成进程内临时密钥(不落盘),仅保证 ISecretProtector 可构造;
            // 重启后旧信封不可解密——适合从未真正启用秘密保护的默认部署。
            keyBytes = RandomNumberGenerator.GetBytes(32);
            logger?.LogWarning(
                "TenonAdmin: 未配置 DataProtection:Key 且非开发环境,已使用进程内临时密钥;" +
                "重启后既有信封将无法解密。请显式配置主密钥。");
        }

        _current = new DataProtectionKeyMaterial(version, keyBytes);
        _byVersion = new Dictionary<int, DataProtectionKeyMaterial> { [version] = _current };
    }

    /// <inheritdoc />
    public virtual DataProtectionKeyMaterial GetCurrentKey() => _current;

    /// <inheritdoc />
    public virtual DataProtectionKeyMaterial GetKey(int version)
    {
        if (_byVersion.TryGetValue(version, out var key)) return key;
        throw new CryptographicException($"未知的数据保护密钥版本: {version}");
    }
}
