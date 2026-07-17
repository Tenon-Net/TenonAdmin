using System.Reflection;

namespace TenonAdmin.Core;

/// <summary>TenonAdmin 顶层配置(对应 appsettings 的 TenonAdmin 节 + 代码侧开关)</summary>
public class TenonAdminOptions
{
    /// <summary>数据库配置(类型/连接串,见 <see cref="AdminDatabaseOptions"/>)</summary>
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

    /// <summary>邮件通道配置(SMTP 主机/端口/凭据;空主机走日志实现,见 <see cref="AdminEmailOptions"/>)</summary>
    public AdminEmailOptions Email { get; set; } = new();

    /// <summary>API 配置(禁用模块等,见 <see cref="AdminApiOptions"/>)</summary>
    public AdminApiOptions Api { get; set; } = new();

    /// <summary>演示模式:开启后仅允许 GET/HEAD/OPTIONS,其余写请求一律拒绝(41002)</summary>
    public bool DemoMode { get; set; }

    /// <summary>雪花 ID 配置(机器号,见 <see cref="AdminIdOptions"/>)</summary>
    public AdminIdOptions Id { get; set; } = new();

    /// <summary>诊断日志配置(文件日志开关/路径/保留期,见 <see cref="AdminLoggingOptions"/>;默认不写盘)</summary>
    public AdminLoggingOptions Logging { get; set; } = new();

    /// <summary>显式指定要并入的业务程序集(代码侧,不从配置绑定):其实体参与 CodeFirst 建表、控制器 AddApplicationPart 挂载。</summary>
    public List<Assembly> ApplicationAssemblies { get; set; } = new();
}
