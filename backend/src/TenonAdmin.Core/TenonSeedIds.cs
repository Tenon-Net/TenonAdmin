namespace TenonAdmin.Core;

/// <summary>
/// 种子数据主键的<b>保留区间约定</b>(设计 §5.7)——内核与消费者各占一段,互不相撞。
///
/// <para><b>为什么必须有这个约定。</b>种子行的 Id 是幂等锚点,必须手工写死(见 <c>ISeedData&lt;T&gt;</c>)。
/// 内核自己就往 <c>sys_menu</c> / <c>sys_config</c> 等表里播种,消费者也会往<b>同样这些表</b>里追加自己的菜单和配置。
/// 若两边随手挑号,今天不冲突不代表明天不冲突:内核每加一个鉴权端点就要多一行菜单,号段只会往上涨——
/// 涨到消费者占用的号上,那个消费者<b>升级内核包时主键冲突、启动即崩</b>,且无法回退(他的库里已有那行)。
/// 所以下界必须在<b>发布首个 NuGet 包之前</b>钉死:此刻成本≈0,发布之后再改就是破坏性变更。</para>
///
/// <code>
/// [1, 999]      内核内置种子   (现用到 109,留 9 倍余量)
/// [1000, ...)   消费者种子     (上限见下)
/// </code>
///
/// <para><b>上限不再是写死的 4095。</b>运行时校验(<c>DatabaseInitializer</c>)改用
/// <see cref="SnowflakeIdGenerator.CurrentFloor"/>——启动那一刻算出的动态雪花地板:时钟只会前进,
/// 严格小于这个值的种子 Id 从此刻起往后永远不会被本实例真实产生的雪花号撞上。这样消费者不必再挤在
/// 3096 个连续整数里,可以用语义化的大号段编号(现在雪花号已是 15 位数量级,可用空间随之暴涨)。</para>
///
/// <para><see cref="ConsumerMax"/> / <see cref="SnowflakeFloor"/> 这两个常量保留,仅作历史参考与
/// 结构性测试用(<c>SeedIdRangeTests</c> 里验证"雪花号任何时刻都 &gt;= <see cref="SnowflakeIdGenerator.MinId"/>"
/// 这条数学地基仍然成立),<b>不再是运行时强制的种子上限</b>。</para>
///
/// <para>下界(内核 <see cref="KernelMax"/> / 消费者 <see cref="ConsumerMin"/>)与"内核自己不得越界"仍由
/// <c>DatabaseInitializer</c> 启动强制 + <c>SeedIdRangeTests</c> 守住。</para>
/// </summary>
public static class TenonSeedIds
{
    /// <summary>内核内置种子可用的最大 Id。内核新增种子行不得超过它(超了 <c>SeedIdRangeTests</c> 会红)。</summary>
    public const long KernelMax = 999;

    /// <summary>消费者种子可用的最小 Id。消费者的固定 Id 一律从这里起步,以免落进内核未来会用到的号段。</summary>
    public const long ConsumerMin = KernelMax + 1;

    /// <summary>
    /// 历史遗留常量,<b>不再是运行时强制的上限</b>(现由 <see cref="SnowflakeIdGenerator.CurrentFloor"/> 动态决定)。
    /// 仅保留给引用过它的旧代码与结构性测试用。
    /// </summary>
    public const long ConsumerMax = SnowflakeFloor - 1;

    /// <summary>
    /// 雪花运行时发号区的结构性下界(= <see cref="SnowflakeIdGenerator.MinId"/>):任何时刻都不可能更小的理论值,
    /// 不随时间变化。种子上限的实际校验用 <see cref="SnowflakeIdGenerator.CurrentFloor"/>,不用这个。
    /// </summary>
    public const long SnowflakeFloor = SnowflakeIdGenerator.MinId;
}
