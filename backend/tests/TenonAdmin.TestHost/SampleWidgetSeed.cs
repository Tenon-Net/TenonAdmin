using TenonAdmin.SqlSugar;

namespace TenonAdmin.TestHost;

/// <summary>示例"用户自定义种子"——固定 Id 保证幂等(§5.7,§8 CustomSeedData 用例)。</summary>
public sealed class SampleWidgetSeed : ISeedData<SampleWidget>
{
    public const long SeedIdA = 1001;
    public const long SeedIdB = 1002;

    public IEnumerable<SampleWidget> HasData() =>
    [
        new SampleWidget { Id = SeedIdA, Name = "widget-a" },
        new SampleWidget { Id = SeedIdB, Name = "widget-b" },
    ];
}
