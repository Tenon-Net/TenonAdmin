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

    // ── 50xxx 系统内部 ───────────────────────────────────────────────

    /// <summary>未知系统错误(未捕获异常的统一出口,详情只进日志不出接口)</summary>
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
