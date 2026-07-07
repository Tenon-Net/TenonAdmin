namespace TenonAdmin.Core;

/// <summary>
/// 雪花 ID 配置(对应 <c>TenonAdmin:Id</c> 节,设计 §12)。
/// </summary>
public class AdminIdOptions
{
    /// <summary>
    /// 机器号(0–1023)。<b>单机部署用默认 0 即可;多实例水平扩展时必须为每个实例配置不同值</b>,
    /// 否则不同实例同毫秒发号会撞 Id(主键冲突/数据错插)。对应 <c>TenonAdmin:Id:WorkerId</c>。
    /// </summary>
    public int WorkerId { get; set; }
}
