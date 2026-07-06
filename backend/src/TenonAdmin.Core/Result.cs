namespace TenonAdmin.Core;

/// <summary>
/// 统一返回模型(设计 §6/§13.2)——所有接口的响应外壳。
/// <para>字段分工:<c>Code</c> 给机器判断;<c>MsgKey</c>+<c>Args</c> 给前端 i18n 渲染;
/// <c>Message</c> 是后端兜底文案(非浏览器调用方降级用,浏览器端应忽略它);<c>Data</c> 为业务载荷。</para>
/// <example>
/// 成功:<c>{ "code": 0, "msgKey": "common.success", "data": {...} }</c><br/>
/// 失败:<c>{ "code": 40001, "msgKey": "error.auth.passwordWrong", "args": {}, "message": "...", "data": null }</c>
/// </example>
/// </summary>
public class Result<T>
{
    /// <summary>业务码,0 为成功,其余见 <see cref="ErrorCode"/> 分段</summary>
    public int Code { get; init; }

    /// <summary>语义键(前端 i18n 语言包的键),如 <c>error.auth.passwordWrong</c></summary>
    public string? MsgKey { get; init; }

    /// <summary>文案插值参数,与语言包模板占位符对应;无参数时为 null(序列化省略)</summary>
    public IReadOnlyDictionary<string, object?>? Args { get; init; }

    /// <summary>兜底文案(仅降级用途,浏览器端一律走 MsgKey 翻译)</summary>
    public string? Message { get; init; }

    /// <summary>业务数据载荷</summary>
    public T? Data { get; init; }

    /// <summary>成功返回(码 0)</summary>
    public static Result<T> Ok(T data) => new()
    {
        Code = (int)ErrorCode.Success,
        MsgKey = ErrorCode.Success.GetMsgKey(),
        Data = data,
    };

    /// <summary>以错误码构造失败返回</summary>
    public static Result<T> Fail(ErrorCode code, IReadOnlyDictionary<string, object?>? args = null, string? message = null) => new()
    {
        Code = (int)code,
        MsgKey = code.GetMsgKey(),
        Args = args,
        Message = message ?? code.GetMsgKey(),
    };

    /// <summary>由业务异常直接转换(全局异常处理器用,保证"异常 → 响应"零信息丢失)</summary>
    public static Result<T> From(AdminException ex) => new()
    {
        Code = (int)ex.Code,
        MsgKey = ex.MsgKey,
        Args = ex.Args,
        Message = ex.Message,
    };
}
