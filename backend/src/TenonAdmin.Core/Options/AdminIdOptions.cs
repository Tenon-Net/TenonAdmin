namespace TenonAdmin.Core;

/// <summary>
/// 雪花 ID 配置(对应 <c>TenonAdmin:Id</c> 节,设计 §12)。
/// </summary>
public class AdminIdOptions
{
    /// <summary>
    /// 机器号(0–63)。<b>单机部署不配即可</b>(回落 0);<b>多实例水平扩展时必须为每个实例配置不同值</b>,
    /// 否则不同实例同毫秒发号会撞 Id(主键冲突/数据错插)。对应 <c>TenonAdmin:Id:WorkerId</c>。
    /// <para><c>null</c> = 未显式配置。之所以要能区分"没配"与"配成 0",是为了在<b>明显的多实例意图</b>
    /// (<c>Cache:Provider=Redis</c>)下没给机器号时<b>启动即抛</b>——把一个静默的主键冲突换成一条可读的启动错误。
    /// 显式写 <c>0</c> 即视为运维已知情,放行。</para>
    /// </summary>
    public int? WorkerId { get; set; }
}
