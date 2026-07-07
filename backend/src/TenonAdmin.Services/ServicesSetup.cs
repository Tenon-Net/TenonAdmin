using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 领域服务装配(设计 §2.2)。框架内置服务在此<b>显式 TryAdd</b>——不靠扫描,可预测、
/// 可被用户前置注册整体替换(设计 §5.7 注册模型:内置显式、用户扫描)。
/// </summary>
public static class ServicesSetup
{
    public static IServiceCollection AddTenonAdminServices(this IServiceCollection services)
    {
        // 密码哈希:PBKDF2 默认实现,无状态 → Singleton;用户可前置注册替换(§5.2)
        services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // 认证:模板方法样板服务(§5.3);Scoped——按请求生命周期,与仓储一致
        services.TryAddScoped<IAuthService, AuthService>();

        // 会话与刷新令牌(§15):登录建会话、每请求校验、刷新轮换+复用检测、登出/强退
        services.TryAddScoped<ISessionService, SessionService>();

        // 缓存:进程内 MemoryCache 默认实现(§5.5);IMemoryCache 需 AddMemoryCache 落地
        services.AddMemoryCache();
        services.TryAddSingleton<ICacheProvider, MemoryCacheProvider>();

        // 事件总线(§2.2/§5.5,T5):Channels 进程内实现,单例;字典/配置变更即发布,订阅者做失效日志/扩展
        services.TryAddSingleton<IEventBus, ChannelEventBus>();
        services.AddHostedService<CacheChangeLogSubscriber>();

        // RBAC(§6):权限码提供者真实现(取代 AspNetCore 层的空占位)+ 角色菜单授权服务
        services.TryAddScoped<IPermissionProvider, RbacPermissionProvider>();
        services.TryAddScoped<IRbacService, RbacService>();

        // 数据范围解析(§6 招牌能力,T3):合并用户多角色范围,结果按用户缓存
        services.TryAddScoped<IDataScopeProvider, DataScopeProvider>();

        // 组织模块(§4,T2):用户 / 机构(树)/ 职位 —— Scoped,与仓储一致
        services.TryAddScoped<IUserService, UserService>();
        services.TryAddScoped<IOrgService, OrgService>();
        services.TryAddScoped<IPositionService, PositionService>();

        // 字典与配置模块(§4,T5):读穿透缓存 + 变更即失效并广播事件
        services.TryAddScoped<IDictService, DictService>();
        services.TryAddScoped<IConfigService, ConfigService>();

        // 日志模块(§4,T6):操作日志(过滤器写)+ 登录日志(AuthService 写);写入尽力而为
        services.TryAddScoped<ILogService, LogService>();

        // 文件模块(§4/§14,T7):本地存储(无状态,单例)+ 上传服务(校验/重写名/记账,Scoped)
        services.TryAddSingleton<IFileStorage, LocalFileStorage>();
        services.TryAddScoped<IFileService, FileService>();

        // 个人中心(§4,T8):当前用户对自己账号的读改(看/改资料、验旧改密)
        services.TryAddScoped<IPersonalService, PersonalService>();

        // 种子:多实现集合,TryAddEnumerable 按实现类型防重
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SuperAdminSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultRoleSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultMenuSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DictTypeSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DictItemSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, ConfigSeed>());

        return services;
    }
}
