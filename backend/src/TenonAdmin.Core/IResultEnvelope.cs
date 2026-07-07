namespace TenonAdmin.Core;

/// <summary>
/// 统一返回外壳的非泛型视图——只暴露业务码。
/// <para>用途:操作日志过滤器(T6)拿到动作返回的 <c>Result&lt;T&gt;</c> 时,无需知道泛型实参
/// 即可读出 <see cref="Code"/> 判成败,免去反射。</para>
/// </summary>
public interface IResultEnvelope
{
    /// <summary>业务码,0 为成功</summary>
    int Code { get; }
}
