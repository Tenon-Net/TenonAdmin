using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 系统配置种子(T5)。播一条站点标题作为起步样例与冒烟锚点;固定 Id 保幂等,
/// 界面上改配置值不会被重启覆盖(种子只补缺失行)。
/// </summary>
internal sealed class ConfigSeed : ISeedData<SysConfig>
{
    /// <summary>站点标题配置键(浏览器标题 + 登录页/侧栏/顶栏/水印品牌词)</summary>
    internal const string SITE_TITLE_KEY = "sys.site.title";
    /// <summary>登录页副标题配置键(留空回退前端内置文案)</summary>
    internal const string SITE_SUBTITLE_KEY = "sys.site.subtitle";
    /// <summary>版权信息配置键(登录页页脚版权名)</summary>
    internal const string SITE_COPYRIGHT_KEY = "sys.site.copyright";
    /// <summary>版权链接配置键(版权名的超链接;留空则纯文本)</summary>
    internal const string SITE_COPYRIGHT_URL_KEY = "sys.site.copyrightUrl";

    public IEnumerable<SysConfig> HasData() =>
    [
        // 基础/品牌(GroupCode=sys):经匿名白名单 GetSiteInfoAsync 下发,前端登录页/框架消费。
        new SysConfig { Id = 1, ConfigKey = SITE_TITLE_KEY, ConfigValue = "TenonAdmin", Name = "站点标题", GroupCode = "sys", Sort = 1, Remark = "浏览器标题与登录页/侧栏/顶栏展示名" },
        new SysConfig { Id = 18, ConfigKey = SITE_SUBTITLE_KEY, ConfigValue = "", Name = "登录副标题", GroupCode = "sys", Sort = 2, Remark = "登录页副标题;留空则用前端内置文案" },
        new SysConfig { Id = 19, ConfigKey = SITE_COPYRIGHT_KEY, ConfigValue = "TenonAdmin", Name = "版权信息", GroupCode = "sys", Sort = 3, Remark = "登录页页脚版权名;留空则回退站点标题" },
        new SysConfig { Id = 20, ConfigKey = SITE_COPYRIGHT_URL_KEY, ConfigValue = "", Name = "版权链接", GroupCode = "sys", Sort = 4, Remark = "版权名的超链接(http/https);留空则纯文本" },

        // 安全策略(GroupCode=security):后端强制执行时经 ISecurityPolicyProvider 读取,改值即时生效。
        // 默认值须与 SecurityPolicyProvider 兜底一致(= 现 Options 默认)。
        new SysConfig { Id = 2, ConfigKey = SecurityPolicyProvider.KEY_MAX_FAIL, ConfigValue = "5", Name = "登录失败锁定次数", GroupCode = SecurityPolicyProvider.GROUP, Sort = 10, Remark = "连续密码错误达此次数即锁定;≤0 关闭" },
        new SysConfig { Id = 3, ConfigKey = SecurityPolicyProvider.KEY_LOCK_MIN, ConfigValue = "10", Name = "锁定时长(分钟)", GroupCode = SecurityPolicyProvider.GROUP, Sort = 11, Remark = "锁定时长,也是失败计数的滑动过期窗口" },
        new SysConfig { Id = 4, ConfigKey = SecurityPolicyProvider.KEY_MIN_LEN, ConfigValue = "8", Name = "密码最小长度", GroupCode = SecurityPolicyProvider.GROUP, Sort = 20, Remark = "新口令最小长度" },
        new SysConfig { Id = 5, ConfigKey = SecurityPolicyProvider.KEY_REQ_UPPER, ConfigValue = "true", Name = "密码须含大写字母", GroupCode = SecurityPolicyProvider.GROUP, Sort = 21, Remark = null },
        new SysConfig { Id = 6, ConfigKey = SecurityPolicyProvider.KEY_REQ_LOWER, ConfigValue = "true", Name = "密码须含小写字母", GroupCode = SecurityPolicyProvider.GROUP, Sort = 22, Remark = null },
        new SysConfig { Id = 7, ConfigKey = SecurityPolicyProvider.KEY_REQ_DIGIT, ConfigValue = "true", Name = "密码须含数字", GroupCode = SecurityPolicyProvider.GROUP, Sort = 23, Remark = null },
        new SysConfig { Id = 8, ConfigKey = SecurityPolicyProvider.KEY_REQ_SPECIAL, ConfigValue = "false", Name = "密码须含特殊字符", GroupCode = SecurityPolicyProvider.GROUP, Sort = 24, Remark = "特殊字符指非字母数字" },
        new SysConfig { Id = 22, ConfigKey = SecurityPolicyProvider.KEY_EXPIRE_DAYS, ConfigValue = "0", Name = "密码有效天数", GroupCode = SecurityPolicyProvider.GROUP, Sort = 25, Remark = "超过天数后登录强制改密(不拦登录);≤0 永不过期" },
        new SysConfig { Id = 9, ConfigKey = SecurityPolicyProvider.KEY_ACCESS_MIN, ConfigValue = "120", Name = "访问令牌时长(分钟)", GroupCode = SecurityPolicyProvider.GROUP, Sort = 30, Remark = "访问令牌有效期,到期需用刷新令牌换发" },
        new SysConfig { Id = 10, ConfigKey = SecurityPolicyProvider.KEY_REFRESH_MIN, ConfigValue = "10080", Name = "刷新令牌时长(分钟)", GroupCode = SecurityPolicyProvider.GROUP, Sort = 31, Remark = "刷新令牌有效期,决定最长免登录时长(默认 7 天)" },
        new SysConfig { Id = 13, ConfigKey = CaptchaService.KEY_ENABLED, ConfigValue = "false", Name = "启用登录验证码", GroupCode = SecurityPolicyProvider.GROUP, Sort = 40, Remark = "开启后登录须过验证码;账号级锁定已挡爆破主向,此为浏览器侧加固" },
        new SysConfig { Id = 21, ConfigKey = CaptchaService.KEY_TYPE, ConfigValue = "char", Name = "验证码类型", GroupCode = SecurityPolicyProvider.GROUP, Sort = 41, Remark = "char 字符 / path 描边(明文不入标记、更抗爬)/ math 算术;或消费方自注册的类型" },
        new SysConfig { Id = 23, ConfigKey = SmsOtpService.KEY_MFA_ENABLED, ConfigValue = "false", Name = "启用短信二次验证", GroupCode = SecurityPolicyProvider.GROUP, Sort = 42, Remark = "开启后绑定了手机号的用户密码登录须再验短信码;未绑手机号的用户不受影响。需接入真实短信通道(ISmsSender)" },
        new SysConfig { Id = 24, ConfigKey = SmsOtpService.KEY_LOGIN_ENABLED, ConfigValue = "false", Name = "启用短信验证码登录", GroupCode = SecurityPolicyProvider.GROUP, Sort = 43, Remark = "开启后登录页出现短信登录入口(手机号+验证码免密)。需接入真实短信通道(ISmsSender)" },

        // 请求限流(GroupCode=security):RuntimeRateLimit 快照读取,改值经事件刷新即时生效。默认须与 AdminRateLimitOptions 默认一致。
        new SysConfig { Id = 14, ConfigKey = AdminRateLimitOptions.KEY_ENABLED, ConfigValue = "true", Name = "启用请求限流", GroupCode = SecurityPolicyProvider.GROUP, Sort = 50, Remark = "按客户端 IP 固定窗口限流;Options 硬关时此项无效" },
        new SysConfig { Id = 15, ConfigKey = AdminRateLimitOptions.KEY_WINDOW, ConfigValue = "60", Name = "限流窗口(秒)", GroupCode = SecurityPolicyProvider.GROUP, Sort = 51, Remark = "全局与认证端点共用的窗口长度" },
        new SysConfig { Id = 16, ConfigKey = AdminRateLimitOptions.KEY_PERMIT, ConfigValue = "300", Name = "全局每窗口请求数", GroupCode = SecurityPolicyProvider.GROUP, Sort = 52, Remark = "单 IP 每窗口允许的请求数;≤0 不限全局" },
        new SysConfig { Id = 17, ConfigKey = AdminRateLimitOptions.KEY_AUTH_PERMIT, ConfigValue = "20", Name = "认证端点每窗口请求数", GroupCode = SecurityPolicyProvider.GROUP, Sort = 53, Remark = "/api/v1/auth/* 单 IP 每窗口允许数(更严);≤0 不限" },

        // 上传约束(GroupCode=upload):FileService.UploadAsync 强制执行,改值即时生效。默认须与 AdminUploadOptions 默认一致。
        new SysConfig { Id = 11, ConfigKey = FileService.KEY_MAX_SIZE, ConfigValue = "20", Name = "单文件大小上限(MB)", GroupCode = FileService.GROUP, Sort = 40, Remark = "超过即拒收上传" },
        new SysConfig { Id = 12, ConfigKey = FileService.KEY_ALLOWED_EXTS, ConfigValue = ".jpg,.png,.pdf,.xlsx,.docx,.zip", Name = "允许的文件后缀", GroupCode = FileService.GROUP, Sort = 41, Remark = "逗号分隔的后缀白名单(含点);留空则回退到 Options 默认" },
    ];
}
