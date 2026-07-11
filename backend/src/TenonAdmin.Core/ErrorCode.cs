using System.Collections.Frozen;
using System.Reflection;

namespace TenonAdmin.Core;

/// <summary>
/// 业务错误码(设计 §13.2)。
/// <para>
/// 核心纪律:<b>后端只抛码、不写文案</b>。每个码携带一个语义 msgKey(如 <c>error.auth.passwordWrong</c>),
/// 前端用 msgKey 查本地语言包渲染提示;后端返回的 Message 字段仅作非浏览器调用方的兜底降级。
/// 这样文案单源在前端,切语言零后端参与。
/// </para>
/// <para>分段规划(新增错误码时按段落位,勿跨段挪用):</para>
/// <list type="table">
///   <item><term>0</term><description>成功</description></item>
///   <item><term>40000–40999</term><description>认证与登录</description></item>
///   <item><term>41000–41999</term><description>权限与数据范围</description></item>
///   <item><term>42000–42999</term><description>用户 / 组织 / 角色 / 菜单</description></item>
///   <item><term>43000–43999</term><description>字典 / 配置</description></item>
///   <item><term>44000–44999</term><description>文件上传</description></item>
///   <item><term>45000–45999</term><description>消息通知</description></item>
///   <item><term>50000–50999</term><description>系统内部错误</description></item>
/// </list>
/// </summary>
public enum ErrorCode
{
    /// <summary>成功(统一返回的默认码)</summary>
    [MsgKey("common.success")]
    Success = 0,

    // ── 40xxx 认证与登录 ─────────────────────────────────────────────

    /// <summary>账号或密码错误。故意不区分"账号不存在/密码错",避免账号枚举攻击。</summary>
    [MsgKey("error.auth.passwordWrong")]
    PasswordWrong = 40001,

    /// <summary>验证码已过期(需刷新重取)</summary>
    [MsgKey("error.auth.captchaExpired")]
    CaptchaExpired = 40002,

    /// <summary>验证码不正确</summary>
    [MsgKey("error.auth.captchaWrong")]
    CaptchaWrong = 40003,

    /// <summary>账号因连续登录失败被临时锁定;args 可携带 lockMinutes</summary>
    [MsgKey("error.auth.accountLocked")]
    AccountLocked = 40004,

    /// <summary>账号已被停用(管理员操作)</summary>
    [MsgKey("error.auth.accountDisabled")]
    AccountDisabled = 40005,

    /// <summary>访问令牌无效或已过期(会话被强退也归此类)</summary>
    [MsgKey("error.auth.tokenInvalid")]
    TokenInvalid = 40006,

    /// <summary>刷新令牌无效 / 已轮换吊销 / 检测到重放</summary>
    [MsgKey("error.auth.refreshTokenInvalid")]
    RefreshTokenInvalid = 40007,

    /// <summary>请求过于频繁,已触发限流(RateLimiter,§12/§14);args 可携带 retryAfterSeconds</summary>
    [MsgKey("error.auth.tooManyRequests")]
    TooManyRequests = 40008,

    // ── 41xxx 权限与数据范围 ─────────────────────────────────────────

    /// <summary>无接口访问权限(权限码不在当前用户 PermissionCodeList 内)</summary>
    [MsgKey("error.perm.denied")]
    NoPermission = 41001,

    // ── 42xxx 用户 / 组织 / 角色 / 菜单 ──────────────────────────────

    /// <summary>目标用户不存在;args 可携带 name</summary>
    [MsgKey("error.user.notFound")]
    UserNotFound = 42001,

    /// <summary>目标角色不存在</summary>
    [MsgKey("error.role.notFound")]
    RoleNotFound = 42002,

    /// <summary>目标机构不存在</summary>
    [MsgKey("error.org.notFound")]
    OrgNotFound = 42003,

    /// <summary>机构下仍有子机构,不能删除(先移除或迁移子机构)</summary>
    [MsgKey("error.org.hasChildren")]
    OrgHasChildren = 42004,

    /// <summary>目标职位不存在</summary>
    [MsgKey("error.position.notFound")]
    PositionNotFound = 42005,

    /// <summary>登录账号已存在(账号唯一)</summary>
    [MsgKey("error.user.accountExists")]
    AccountExists = 42006,

    /// <summary>超级管理员受保护:不可删除/停用(防自锁死、防提权面被破坏)</summary>
    [MsgKey("error.user.superAdminProtected")]
    SuperAdminProtected = 42007,

    /// <summary>非法父机构:父节点不能是自身(也不应是自身子孙)</summary>
    [MsgKey("error.org.invalidParent")]
    OrgInvalidParent = 42008,

    /// <summary>机构编码已存在(编码唯一)</summary>
    [MsgKey("error.org.codeExists")]
    OrgCodeExists = 42009,

    /// <summary>职位编码已存在(编码唯一)</summary>
    [MsgKey("error.position.codeExists")]
    PositionCodeExists = 42010,

    /// <summary>目标模块/应用不存在</summary>
    [MsgKey("error.module.notFound")]
    ModuleNotFound = 42011,

    /// <summary>模块编码已存在(编码唯一)</summary>
    [MsgKey("error.module.codeExists")]
    ModuleCodeExists = 42012,

    /// <summary>内置模块受保护:不可删除(如 system 模块)</summary>
    [MsgKey("error.module.protected")]
    ModuleProtected = 42013,

    /// <summary>无该模块访问权(未被授权该模块下任何菜单;设默认应用时校验)</summary>
    [MsgKey("error.module.accessDenied")]
    ModuleAccessDenied = 42014,

    /// <summary>目标菜单不存在</summary>
    [MsgKey("error.menu.notFound")]
    MenuNotFound = 42015,

    /// <summary>菜单下仍有子节点,不能删除(先移除或迁移子节点)</summary>
    [MsgKey("error.menu.hasChildren")]
    MenuHasChildren = 42016,

    /// <summary>非法父菜单:父节点不存在,或指向自身/自身子孙(会成环导致子树在树上消失)</summary>
    [MsgKey("error.menu.invalidParent")]
    MenuInvalidParent = 42017,

    /// <summary>角色编码已存在(编码唯一)</summary>
    [MsgKey("error.role.codeExists")]
    RoleCodeExists = 42018,

    /// <summary>新口令不满足密码复杂度策略;args 携带 minLength/requireUpper/requireLower/requireDigit/requireSpecial</summary>
    [MsgKey("error.user.passwordTooWeak")]
    PasswordTooWeak = 42019,

    // ── 43xxx 字典 / 配置 ────────────────────────────────────────────

    /// <summary>字典类型不存在</summary>
    [MsgKey("error.dict.typeNotFound")]
    DictTypeNotFound = 43001,

    /// <summary>字典类型编码已存在(编码唯一)</summary>
    [MsgKey("error.dict.typeCodeExists")]
    DictTypeCodeExists = 43002,

    /// <summary>字典项不存在</summary>
    [MsgKey("error.dict.itemNotFound")]
    DictItemNotFound = 43003,

    /// <summary>系统配置不存在</summary>
    [MsgKey("error.config.notFound")]
    ConfigNotFound = 43004,

    /// <summary>配置键已存在(键唯一)</summary>
    [MsgKey("error.config.keyExists")]
    ConfigKeyExists = 43005,

    // ── 44xxx 文件上传 ───────────────────────────────────────────────

    /// <summary>空文件(未选择文件或文件长度为 0)</summary>
    [MsgKey("error.file.empty")]
    FileEmpty = 44001,

    /// <summary>文件超过大小上限;args 可携带 maxSizeMb</summary>
    [MsgKey("error.file.tooLarge")]
    FileTooLarge = 44002,

    /// <summary>文件后缀不在白名单;args 可携带 ext</summary>
    [MsgKey("error.file.extNotAllowed")]
    FileExtNotAllowed = 44003,

    /// <summary>文件记录不存在(或物理文件已丢失)</summary>
    [MsgKey("error.file.notFound")]
    FileNotFound = 44004,

    // ── 45xxx 消息通知 ───────────────────────────────────────────────

    /// <summary>通知不存在(或已被删除)</summary>
    [MsgKey("error.notice.notFound")]
    NoticeNotFound = 45001,

    // ── 50xxx 系统内部 ───────────────────────────────────────────────

    /// <summary>
    /// 未知系统错误。用于操作日志里记录未捕获异常的结果码(详情只进日志不出接口)。
    /// 注(P2-2):框架级 400(模型校验/ProblemDetails)与未捕获 500 目前<b>不</b>套本信封——
    /// 程序缺陷该大声失败(见 AdminExceptionFilter)。要让 400/500 也走统一信封,需自写
    /// InvalidModelStateResponseFactory + UseExceptionHandler,留待需要面向非浏览器调用方统一形状时再做。
    /// </summary>
    [MsgKey("error.system.internal")]
    SystemError = 50000,
}

/// <summary>
/// 为 <see cref="ErrorCode"/> 成员标注语义 msgKey(前端 i18n 语言包的键)。
/// 显式标注而非命名约定推导,保证"码 → 键"的映射一目了然、重命名枚举不破坏契约。
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class MsgKeyAttribute(string key) : Attribute
{
    /// <summary>语义键,形如 <c>error.auth.passwordWrong</c></summary>
    public string Key { get; } = key;
}

/// <summary><see cref="ErrorCode"/> 的辅助扩展</summary>
public static class ErrorCodeExtensions
{
    // 启动后首次访问时反射一次,固化为 FrozenDictionary(只读、查询最快),此后零反射开销。
    private static readonly FrozenDictionary<ErrorCode, string> MSG_KEYS =
        typeof(ErrorCode).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (Code: (ErrorCode)f.GetValue(null)!, Key: f.GetCustomAttribute<MsgKeyAttribute>()?.Key))
            .Where(x => x.Key is not null)
            .ToFrozenDictionary(x => x.Code, x => x.Key!);

    /// <summary>
    /// 取错误码的语义 msgKey;未标注的成员回退为 <c>error.code.{数值}</c>(可用但不该出现,新增码务必标注)。
    /// </summary>
    public static string GetMsgKey(this ErrorCode code) =>
        MSG_KEYS.TryGetValue(code, out var key) ? key : $"error.code.{(int)code}";
}
