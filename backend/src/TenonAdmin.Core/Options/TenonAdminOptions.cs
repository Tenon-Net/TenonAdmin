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

    /// <summary>API 配置(禁用模块等,见 <see cref="AdminApiOptions"/>)</summary>
    public AdminApiOptions Api { get; set; } = new();

    /// <summary>雪花 ID 配置(机器号,见 <see cref="AdminIdOptions"/>)</summary>
    public AdminIdOptions Id { get; set; } = new();

    /// <summary>
    /// (已废弃,设计 §5.7 骨架从未实现)原意"自动扫描入口程序集及其引用注册用户模块"的开关——
    /// <b>全代码库无一处读取它,置 true/false 均无任何效果</b>。业务程序集请显式经
    /// <see cref="ApplicationAssemblies"/> 登记(实体建表 + 控制器挂载),那是唯一生效路径。
    /// </summary>
    [Obsolete("空开关:从未实现且不被读取,置任何值都无效。请改用 ApplicationAssemblies.Add(assembly) 显式登记业务程序集。将于后续大版本移除。")]
    public bool ScanApplicationAssemblies { get; set; } = true;

    /// <summary>显式指定要并入的业务程序集(代码侧,不从配置绑定):其实体参与 CodeFirst 建表、控制器 AddApplicationPart 挂载。</summary>
    public List<Assembly> ApplicationAssemblies { get; set; } = new();
}
