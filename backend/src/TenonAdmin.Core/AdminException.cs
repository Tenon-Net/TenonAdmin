namespace TenonAdmin.Core;

/// <summary>
/// 业务异常(设计 §6/§13.2)——框架内所有"可预期的业务失败"的唯一表达方式。
/// <para>
/// 用法:<c>throw new AdminException(ErrorCode.PasswordWrong);</c>
/// 只抛码,不在抛出处写死任何语言的文案。全局异常处理器(AspNetCore 层)会把它转换成统一返回:
/// <c>{ code, msgKey, args, message }</c>,前端拿 msgKey + args 渲染本地化提示。
/// </para>
/// <para>
/// 与普通异常的分界:业务规则不满足(密码错、无权限、账号锁定)抛 AdminException,
/// 会以 4xx/业务码温和返回、不记错误日志;程序缺陷或环境故障(空引用、连不上库)让原生异常抛出,
/// 由异常处理器按 <see cref="ErrorCode.SystemError"/> 兜底并记完整日志。
/// </para>
/// </summary>
public class AdminException : Exception
{
    /// <summary>业务错误码(分段规则见 <see cref="ErrorCode"/>)</summary>
    public ErrorCode Code { get; }

    /// <summary>语义 msgKey(由 Code 推导),前端 i18n 语言包的键</summary>
    public string MsgKey { get; }

    /// <summary>
    /// 文案插值参数(可选)。键名与语言包占位符对应,
    /// 如 key=<c>error.user.notFound</c>、模板 <c>"用户 {name} 不存在"</c> 时传 <c>new { name = "张三" }</c> 风格的字典。
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Args { get; }

    /// <summary>
    /// 以错误码构造业务异常。
    /// </summary>
    /// <param name="code">业务错误码</param>
    /// <param name="args">文案插值参数(可选,给前端 i18n 用)</param>
    /// <param name="fallbackMessage">
    /// 兜底文案(可选)。不传时 Exception.Message 为 msgKey——日志里可读、又不构成"写死文案"。
    /// 仅供日志与非浏览器调用方降级展示,浏览器端一律走 msgKey 翻译。
    /// </param>
    public AdminException(ErrorCode code, IReadOnlyDictionary<string, object?>? args = null, string? fallbackMessage = null)
        : base(fallbackMessage ?? code.GetMsgKey())
    {
        Code = code;
        MsgKey = code.GetMsgKey();
        Args = args;
    }

    /// <summary>便捷抛出:条件成立即抛。写法 <c>AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);</c></summary>
    public static void ThrowIf(bool condition, ErrorCode code, IReadOnlyDictionary<string, object?>? args = null)
    {
        if (condition) throw new AdminException(code, args);
    }
}
