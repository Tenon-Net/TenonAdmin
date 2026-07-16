using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 种子主键保留区间的契约测试(见 <see cref="TenonSeedIds"/>)。
/// <para>守两件事:内核自己不得越出 <c>[1, KernelMax]</c>(否则会吃掉消费者的号段,发包后无法回收);
/// 以及保留区间的<b>数学地基</b>——雪花发不出小于 <c>SnowflakeFloor</c> 的号——在有人改雪花位宽后依然成立。</para>
/// </summary>
public class SeedIdRangeTests
{
    /// <summary>内核自己的程序集:种子出自这里就受 KernelMax 约束;其余(消费者/TestHost)受消费者区间约束。</summary>
    private static readonly HashSet<System.Reflection.Assembly> KernelAssemblies =
    [
        typeof(ISeedData).Assembly,    // TenonAdmin.SqlSugar —— SchemaVersionSeed 藏在这里,最容易漏
        typeof(RbacService).Assembly,  // TenonAdmin.Services —— 其余 6 个内置种子
    ];

    /// <summary>
    /// 内置种子的每一行 Id 都必须落在内核区间内。
    /// <para>经 DI 取种子(而非 <c>Activator.CreateInstance</c>):内置种子是 <c>internal sealed</c> 且部分有构造依赖
    /// (<c>SuperAdminSeed</c> 要仓储/哈希器/Options/Logger)。走 DI 还有个额外好处——<b>将来任何人加新种子类都自动被扫到,
    /// 这个测试不用改</b>。</para>
    /// </summary>
    [Fact]
    public void BuiltInSeeds_AllIdsWithinKernelRange()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();

        var checkedRows = 0;
        foreach (var seed in scope.ServiceProvider.GetServices<ISeedData>())
        {
            if (!KernelAssemblies.Contains(seed.GetType().Assembly)) continue;   // 消费者种子(TestHost)另有区间,见下一个用例

            foreach (var id in SeedIds(seed))
            {
                Assert.True(id is >= 1 and <= TenonSeedIds.KernelMax,
                    $"内置种子 {seed.GetType().Name} 的 Id {id} 越出内核保留区间 [1, {TenonSeedIds.KernelMax}]。" +
                    $"内核只能用低号段:{TenonSeedIds.ConsumerMin} 起是留给消费者的,占了它,消费者升级内核包时会主键冲突。");
                checkedRows++;
            }
        }

        Assert.True(checkedRows > 0, "一行内置种子都没扫到 —— 种子注册或反射取数坏了,这个测试正在空转。");
    }

    /// <summary>
    /// 超管种子的 Id 必须单独查库确认:它的 <c>HasData()</c> 在表里已有用户时返回空集合(幂等设计),
    /// 而宿主启动时已经把超管播下去了,所以上一个用例扫到它时拿到的是 0 行 —— 唯一能验的地方是库里那一行。
    /// </summary>
    [Fact]
    public async Task SuperAdmin_SeededIdWithinKernelRange()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();

        var admin = await users.AsQueryable().Where(u => u.IsSuperAdmin).FirstAsync();

        Assert.NotNull(admin);
        Assert.InRange(admin.Id, 1, TenonSeedIds.KernelMax);
    }

    /// <summary>消费者种子(TestHost 的 SampleWidgetSeed)应落在消费者区间——它是对外的正面范例,别让它示范"随手挑号"。</summary>
    [Fact]
    public void ConsumerSeeds_AllIdsWithinConsumerRange()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();

        foreach (var seed in scope.ServiceProvider.GetServices<ISeedData>())
        {
            if (KernelAssemblies.Contains(seed.GetType().Assembly)) continue;

            foreach (var id in SeedIds(seed))
                Assert.True(id >= TenonSeedIds.ConsumerMin && id <= TenonSeedIds.ConsumerMax,
                    $"消费者种子 {seed.GetType().Name} 的 Id {id} 越出消费者区间 " +
                    $"[{TenonSeedIds.ConsumerMin}, {TenonSeedIds.ConsumerMax}]。");
        }
    }

    /// <summary>
    /// 保留区间的数学地基:雪花发不出小于 <see cref="TenonSeedIds.SnowflakeFloor"/> 的号,故种子区间与运行时 ID 不可能相撞。
    /// <para>这条断言是整个约定的<b>承重墙</b>:谁把雪花低位从 12 bit 改窄(比如 8 bit),地板就从 4096 掉到 256,
    /// 消费者区间 [1000, 4095] 立刻骑到运行时发号区上——本用例会当场变红,而不是等某个消费者线上主键冲突才发现。</para>
    /// </summary>
    [Fact]
    public void SnowflakeFloor_StaysAboveTheSeedRange()
    {
        Assert.True(TenonSeedIds.ConsumerMax < TenonSeedIds.SnowflakeFloor,
            $"种子区间上界 {TenonSeedIds.ConsumerMax} 必须严格小于雪花地板 {TenonSeedIds.SnowflakeFloor} —— " +
            "雪花低位被改窄了?那样种子号段会与运行时发号区重叠。");

        // 经验验证:真发一个号,确认它确实在地板之上(纪元/位宽任何一处算错都会在这里露馅)
        var id = new SnowflakeIdGenerator(workerId: 0, TimeProvider.System).NextId();
        Assert.True(id >= TenonSeedIds.SnowflakeFloor,
            $"雪花发出的 ID {id} 低于声称的地板 {TenonSeedIds.SnowflakeFloor}。");
    }

    /// <summary>
    /// 同一实体上,全部种子(内核 + 消费者)的固定 Id 不得重复——撞号的破坏是静默的:
    /// 幂等判存把后来的行当"已存在"跳过,SyncOnUpgrade 的种子升级时还会覆盖别人的行。
    /// 运行时检查(DatabaseInitializer.EnsureSeedIdsUnique)会拦,这里让 CI 在宿主启动前就变红。
    /// </summary>
    [Fact]
    public void Seeds_IdsAreUniquePerEntity()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();

        var seen = new Dictionary<(Type Entity, long Id), string>();
        var checkedRows = 0;
        foreach (var seed in scope.ServiceProvider.GetServices<ISeedData>())
        {
            var entity = seed.GetType().GetInterfaces()
                .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISeedData<>))
                .GetGenericArguments()[0];
            foreach (var id in SeedIds(seed))
            {
                Assert.False(seen.TryGetValue((entity, id), out var owner),
                    $"种子 Id 撞号:{seed.GetType().Name} 与 {owner} 都声领了 {entity.Name} 的 Id={id}。" +
                    "固定 Id 在同一实体上必须全局唯一,请换一个未占用的号。");
                seen[(entity, id)] = seed.GetType().Name;
                checkedRows++;
            }
        }

        Assert.True(checkedRows > 0, "一行种子都没扫到 —— 种子注册或反射取数坏了,这个测试正在空转。");
    }

    /// <summary>反射取一个种子的全部固定 Id:经 <c>ISeedData&lt;T&gt;</c> 的泛型接口调 HasData()(与 DatabaseInitializer 同款)。</summary>
    private static IEnumerable<long> SeedIds(ISeedData seed)
    {
        var iface = seed.GetType().GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISeedData<>));
        var rows = (IEnumerable)iface.GetMethod(nameof(ISeedData<SysUser>.HasData))!.Invoke(seed, null)!;
        return rows.Cast<BaseEntity>().Select(r => r.Id);
    }
}
