namespace TenonAdmin.Core;

/// <summary>
/// 种子数据主键的<b>保留区间约定</b>(设计 §5.7)——内核与消费者各占一段,互不相撞,且都撞不到运行时雪花号。
///
/// <para><b>为什么必须有这个约定。</b>种子行的 Id 是幂等锚点,必须手工写死(见 <c>ISeedData&lt;T&gt;</c>)。
/// 内核自己就往 <c>sys_menu</c> / <c>sys_config</c> 等表里播种,消费者也会往<b>同样这些表</b>里追加自己的菜单和配置。
/// 若两边随手挑号,今天不冲突不代表明天不冲突:内核每加一个鉴权端点就要多一行菜单,号段只会往上涨——
/// 涨到消费者占用的号上,那个消费者<b>升级内核包时主键冲突、启动即崩</b>,且无法回退(他的库里已有那行)。
/// 所以区间必须在<b>发布首个 NuGet 包之前</b>钉死:此刻成本≈0,发布之后再改就是破坏性变更。</para>
///
/// <code>
/// [1, 999]        内核内置种子      (现用到 109,留 9 倍余量)
/// [1000, 4095]    消费者种子        (3096 个槽位)
/// [4096, ...]     雪花运行时保留区   —— 种子一律不得进入
/// </code>
///
/// <para><b>4096 这个上界不是拍脑袋的。</b>它等于 <see cref="SnowflakeIdGenerator.MinId"/>:雪花布局的低 12 位是
/// 「机器号 + 毫秒内序列」,故 <c>id = 相对纪元毫秒数 × 4096 + 低位</c>,纪元后第 1 毫秒发出的号就已经是 4096。
/// 也就是说<b>任何小于 4096 的 Id,雪花永远发不出来</b>——种子行与运行时插入的行在数学上不可能碰撞。
/// (反过来,4096 以上的号迟早会被时间推到,所以种子绝不能去那里挑号。)
/// 上界从雪花位宽派生而非写死,是为了让「有人把低位改窄」这件事当场炸掉,而不是无声地废掉整个约定。</para>
///
/// <para>约束由 <c>DatabaseInitializer</c> 在启动时强制(越界即抛,文档没人读但启动报错跑不掉),
/// 内核自己不越界则由 <c>SeedIdRangeTests</c> 守住。</para>
/// </summary>
public static class TenonSeedIds
{
    /// <summary>内核内置种子可用的最大 Id。内核新增种子行不得超过它(超了 <c>SeedIdRangeTests</c> 会红)。</summary>
    public const long KernelMax = 999;

    /// <summary>消费者种子可用的最小 Id。消费者的固定 Id 一律从这里起步,以免落进内核未来会用到的号段。</summary>
    public const long ConsumerMin = KernelMax + 1;

    /// <summary>
    /// 任何种子(含消费者)可用的最大 Id。等于雪花最小号减一——再往上就是运行时发号区,迟早撞车。
    /// </summary>
    public const long ConsumerMax = SnowflakeFloor - 1;

    /// <summary>
    /// 雪花运行时发号区的下界(= <see cref="SnowflakeIdGenerator.MinId"/>)。种子 Id 必须严格小于它。
    /// </summary>
    public const long SnowflakeFloor = SnowflakeIdGenerator.MinId;
}
