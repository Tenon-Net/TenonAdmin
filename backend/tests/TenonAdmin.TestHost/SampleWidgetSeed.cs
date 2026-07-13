using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.TestHost;

/// <summary>
/// 示例"用户自定义种子"——固定 Id 保证幂等(§5.7,§8 CustomSeedData 用例)。
/// <para>Id 从 <see cref="TenonSeedIds.ConsumerMin"/> 起取:这是消费者该守的区间(内核占低号段,雪花占高号段)。
/// 这个类是对外的抄写范本,别让它示范"随手挑个整数"。</para>
/// </summary>
public sealed class SampleWidgetSeed : ISeedData<SampleWidget>
{
    public const long SeedIdA = TenonSeedIds.ConsumerMin;
    public const long SeedIdB = TenonSeedIds.ConsumerMin + 1;

    public IEnumerable<SampleWidget> HasData() =>
    [
        new SampleWidget { Id = SeedIdA, Name = "widget-a" },
        new SampleWidget { Id = SeedIdB, Name = "widget-b" },
    ];
}
