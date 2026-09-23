namespace TenonAdmin.Core;

/// <summary>
/// 当前请求的<b>生效数据范围</b>环境载体(设计 §6)。授权管道对认证用户解析范围后写入,
/// SqlSugar 全局过滤器读取它对 <c>DataEntity</c> 查询注入机构过滤。
/// <para><see cref="Current"/> 恒非 null:<b>未显式设置 = 不受限</b>(系统/可信上下文)。
/// 因此认证请求必须在查询前显式解析并写入范围,否则按可信上下文放行(见授权管道)。</para>
/// </summary>
public interface IDataScopeContext
{
    /// <summary>当前生效范围;未设置时返回 <see cref="DataScopeResult.Unrestricted"/></summary>
    DataScopeResult Current { get; set; }
}
