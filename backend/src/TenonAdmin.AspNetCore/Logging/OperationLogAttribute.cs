namespace TenonAdmin.AspNetCore;

/// <summary>
/// 操作日志标记(设计 §4)。挂在控制器动作(或整个控制器)上,<see cref="OperationLogFilter"/>
/// 会在动作执行后自动记一条操作日志(入参脱敏、耗时、结果码、操作人/IP/UA)。
/// <para>opt-in 设计:<b>只有挂了本特性的接口才记日志</b>——查询类接口不挂,避免日志表被读操作淹没。</para>
/// <code>[OperationLog("新增用户")]  // 标题会原样写进日志的"操作名"列</code>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class OperationLogAttribute(string title) : Attribute
{
    /// <summary>操作名(人类可读,写进日志"操作名"列)</summary>
    public string Title { get; } = title;
}
