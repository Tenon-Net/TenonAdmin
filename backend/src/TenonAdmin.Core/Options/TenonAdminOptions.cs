using System.Reflection;

namespace TenonAdmin.Core;

/// <summary>TenonAdmin 顶层配置(对应 appsettings 的 TenonAdmin 节 + 代码侧开关)</summary>
public class TenonAdminOptions
{
    public AdminDatabaseOptions Database { get; set; } = new();

    /// <summary>缓存配置(提供者/前缀/权限缓存过期,见 <see cref="AdminCacheOptions"/>)</summary>
    public AdminCacheOptions Cache { get; set; } = new();

    /// <summary>种子配置(超管账号/初始密码,见 <see cref="AdminSeedOptions"/>)</summary>
    public AdminSeedOptions Seed { get; set; } = new();

    /// <summary>JWT 配置(密钥/签发者/有效期,见 <see cref="AdminJwtOptions"/>)</summary>
    public AdminJwtOptions Jwt { get; set; } = new();

    /// <summary>安全配置(会话并发策略等,见 <see cref="AdminSecurityOptions"/>)</summary>
    public AdminSecurityOptions Security { get; set; } = new();

    /// <summary>上传配置(存储根/大小上限/后缀白名单,见 <see cref="AdminUploadOptions"/>)</summary>
    public AdminUploadOptions Upload { get; set; } = new();

    /// <summary>是否扫描入口程序集及其引用注册用户模块(设计 §5.7)。骨架暂未启用扫描。</summary>
    public bool ScanApplicationAssemblies { get; set; } = true;

    /// <summary>额外显式指定要扫描的程序集(代码侧,不从配置绑定)</summary>
    public List<Assembly> ApplicationAssemblies { get; set; } = new();
}
