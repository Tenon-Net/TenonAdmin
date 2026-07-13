namespace TenonAdmin.AspNetCore;

/// <summary>
/// 操作日志的<b>标题覆写</b>(设计 §4)。<see cref="OperationLogFilter"/> 默认就会记录一切写操作
/// (入参脱敏、耗时、结果码、操作人/IP/UA),<b>不挂本特性也照记</b>——审计是安全默认,不靠人记得加特性。
/// <para>挂本特性只做两件事:① 把"操作名"列从缺省的规范化路由串(如 <c>POST:/api/v1/sys/user</c>)
/// 换成人类可读标题;② 让本就不记的读操作(GET/HEAD/OPTIONS)破例留痕(如导出)。</para>
/// <code>[OperationLog("新增用户")]  // 标题会原样写进日志的"操作名"列</code>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class OperationLogAttribute(string title) : Attribute
{
    /// <summary>操作名(人类可读,写进日志"操作名"列)</summary>
    public string Title { get; } = title;
}
