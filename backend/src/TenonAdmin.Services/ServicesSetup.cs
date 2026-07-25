using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
        // 统一时间源(§12):AspNetCore 层也 TryAdd 同一个,这里再兜一次,让本层单独装配也能自洽(测试可换 Fake)
        services.TryAddSingleton(TimeProvider.System);

        // 密码哈希:PBKDF2 默认实现,无状态 → Singleton;用户可前置注册替换(§5.2)
        services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // 认证:模板方法样板服务(§5.3);Scoped——按请求生命周期,与仓储一致
        services.TryAddScoped<IAuthService, AuthService>();

        // 登录失败锁定(§14,T8b):失败计数进缓存,达阈值锁定窗口内拒登
        services.TryAddScoped<ILoginLockService, LoginLockService>();

        // 验证码(§14,T8c):多生成器按类型可选——char 字符 / path 描边(明文不入标记、更抗爬)/ math 算术。
        // TryAddEnumerable 按实现类型防重;消费方前置注册自有 ICaptchaProvider(如滑块)会一并入集、改配置类型即选中。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICaptchaProvider, SvgCaptchaProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICaptchaProvider, PathCaptchaProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICaptchaProvider, MathCaptchaProvider>());
        services.TryAddScoped<ICaptchaService, CaptchaService>();   // 签发按配置选型 + 一次性校验

        // 短信验证码(§14 登录加固):OTP 签发/校验 + MFA 挑战;发送通道默认日志实现(内核零厂商 SDK),
        // 消费方前置注册真实 ISmsSender(阿里云/腾讯云等)即接管
        services.TryAddSingleton<ISmsSender, LoggingSmsSender>();
        services.TryAddScoped<ISmsOtpService, SmsOtpService>();

        // 邮件通道(§14 投递通道,同 ISmsSender 成法):默认日志实现(开发期);配了 SMTP 主机则用内置 SmtpEmailSender;
        // 消费方前置注册真实 IEmailSender(MailKit / 云厂商 API)即接管(TryAdd 前置替换)。为后续邮件 OTP/通知扇出铺路。
        services.TryAddSingleton<IEmailSender>(sp =>
        {
            var email = sp.GetRequiredService<AdminEmailOptions>();
            return string.IsNullOrWhiteSpace(email.Host)
                ? new LoggingEmailSender(sp.GetRequiredService<ILogger<LoggingEmailSender>>())
                : new SmtpEmailSender(email, sp.GetRequiredService<ILogger<SmtpEmailSender>>());
        });

        // 实时推送通道(§14 实时通知):默认空操作实现(关闭实时时业务照调不误)。开启 TenonAdmin:Realtime:Enabled 后
        // AspNetCore 层前置注册基于 SignalR 的真实现即接管(零新增 NuGet,SignalR 属共享框架);消费方可注册自有通道整体替换。
        services.TryAddSingleton<IRealtimePublisher, NoopRealtimePublisher>();

        // 导入/导出 codec(excel-ledger §3/§6):默认 MissingExcelProvider 一律抛 46001(fail-loud,与实时 Noop 不同)。
        // 装 TenonAdmin.Excel 并在 AddTenonAdmin() 之前调 AddTenonAdminExcel() 即经 TryAdd 前置替换接管。
        services.TryAddSingleton<IExcelReader, MissingExcelProvider>();
        services.TryAddSingleton<IExcelWriter, MissingExcelProvider>();
        services.TryAddSingleton<IExcelTemplateBuilder, MissingExcelProvider>();

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
        services.TryAddScoped<IRoleService, RoleService>();   // 角色生命周期 CRUD(授权/数据范围仍走 IRbacService)

        // 数据范围解析(§6 招牌能力,T3):合并用户多角色范围,结果按用户缓存
        services.TryAddScoped<IDataScopeProvider, DataScopeProvider>();

        // 密码历史防重用(§14):开关 sys.security.password.historyCount(默认 0=关);改密/重置挂点调用
        services.TryAddScoped<IPasswordHistoryService, PasswordHistoryService>();

        // 服务器监控(§4 运维):进程/主机基础指标,纯 BCL 无依赖
        services.TryAddScoped<IMonitorService, MonitorService>();

        // 缓存管理(§4 运维):管理员定向失效动作(清授权/字典/配置缓存、重建门户代际);只清不看(缓存含明文 OTP、键含 PII)
        services.TryAddScoped<ICacheAdminService, CacheAdminService>();

        // 外部登录 / SSO 绑定(批次 D):sys_user_external 增删查 + 运营配置读取(启用/未绑定策略/开户默认角色·机构)
        services.TryAddScoped<ISysUserExternalService, SysUserExternalService>();

        // 组织模块(§4,T2):用户 / 机构(树)/ 职位 —— Scoped,与仓储一致
        services.TryAddScoped<IUserService, UserService>();
        services.TryAddScoped<IOrgService, OrgService>();
        services.TryAddScoped<IPositionService, PositionService>();

        // 多应用门户(§4):模块 CRUD + 按模块的"我的模块/菜单树"派生(访问权反推自菜单授权)
        services.TryAddScoped<IModuleService, ModuleService>();
        services.TryAddScoped<IMenuService, MenuService>();

        // 字典与配置模块(§4,T5):读穿透缓存 + 变更即失效并广播事件
        services.TryAddScoped<IDictService, DictService>();
        services.TryAddScoped<IConfigService, ConfigService>();

        // 消息通知模块(§4 消息中心):管理员广播发布 + 用户端我的通知/未读数/标记已读(轮询模型)
        services.TryAddScoped<INoticeService, NoticeService>();

        // 安全策略读取层(§14):DB(SysConfig)优先、Options 兜底,收口锁定/会话时长/密码复杂度的取值
        services.TryAddScoped<ISecurityPolicyProvider, SecurityPolicyProvider>();

        // 日志模块(§4,T6):操作日志(过滤器写)+ 登录日志(AuthService 写);写入尽力而为
        services.TryAddScoped<ILogService, LogService>();

        // 文件模块(§4/§14,T7):本地存储(无状态,单例)+ 分片临时存储(无状态,单例)+ 上传服务(校验/重写名/记账,Scoped)
        services.TryAddSingleton<IFileStorage, LocalFileStorage>();
        services.TryAddSingleton<ChunkStorage>();
        services.TryAddScoped<IFileService, FileService>();
        // 磁盘回收(T-D2):软删文件过保留期后删盘 + 弃单分片按 TTL 清扫。双注册——单例可被覆写/直接调 SweepAsync,
        // 同一实例再作为后台任务托管(同 RuntimeRateLimit 的成法),避免被实例化两份。
        services.TryAddSingleton<FileGcService>();
        services.AddHostedService(sp => sp.GetRequiredService<FileGcService>());

        // 个人中心(§4,T8):当前用户对自己账号的读改(看/改资料、验旧改密)
        services.TryAddScoped<IPersonalService, PersonalService>();

        // 工作台首页统计(§4):四个计数 + 近 7 日登录趋势,每步一个 virtual 方法可单独覆写
        services.TryAddScoped<IDashboardService, DashboardService>();

        // 种子:多实现集合,TryAddEnumerable 按实现类型防重
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SuperAdminSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultRoleSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultModuleSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultMenuSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DictTypeSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DictItemSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, ConfigSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultOrgSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultPositionSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultUserSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultUserRoleSeed>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultDataScopeSeed>());

        return services;
    }
}
