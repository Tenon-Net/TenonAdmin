using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.TestHost;

namespace TenonAdmin.Tests;

/// <summary>
/// 种子主键保留区间的契约测试(见 <see cref="TenonSeedIds"/>)。
/// <para>守几件事:内核自己不得越出 <c>[1, KernelMax]</c>(否则会吃掉消费者的号段,发包后无法回收);
/// 消费者种子不得低于 <c>ConsumerMin</c>;以及运行时上限——启动时刻动态算出的雪花地板
/// (<see cref="SnowflakeIdGenerator.CurrentFloor"/>)——既拦得住真正危险的越界 Id,又不再限制消费者只能用
/// 3096 个连续整数编号。</para>
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

    /// <summary>消费者种子(TestHost 的 SampleWidgetSeed)应不低于消费者下界——它是对外的正面范例,别让它示范"随手挑号"。
    /// 上限不再校验:见 <see cref="ConsumerSeed_LargeSemanticIdIsAccepted"/> 与
    /// <see cref="ConsumerSeed_IdAtOrAboveLiveFloorFailsAtStartup"/>。</summary>
    [Fact]
    public void ConsumerSeeds_AllIdsAtOrAboveConsumerMin()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();

        foreach (var seed in scope.ServiceProvider.GetServices<ISeedData>())
        {
            if (KernelAssemblies.Contains(seed.GetType().Assembly)) continue;

            foreach (var id in SeedIds(seed))
                Assert.True(id >= TenonSeedIds.ConsumerMin,
                    $"消费者种子 {seed.GetType().Name} 的 Id {id} 低于消费者下界 {TenonSeedIds.ConsumerMin}。");
        }
    }

    /// <summary>
    /// 消费者种子可以用远超 4095 的语义化编号(如按业务模块拼的号段)——这是本次改动要解决的真问题:
    /// 原来写死的 <c>[1000, 4095]</c> 只有 3096 个连续整数槽位,菜单多的系统很难编出有意义的号。
    /// 换成启动时刻动态算出的雪花地板后,只要不撞见 <see cref="ConsumerSeed_IdAtOrAboveLiveFloorFailsAtStartup"/>
    /// 那种量级,启动应正常通过。
    /// </summary>
    [Fact]
    public void ConsumerSeed_LargeSemanticIdIsAccepted()
    {
        using var factory = new AdminAppFactory
        {
            Overrides = services => services.TryAddEnumerable(
                ServiceDescriptor.Transient<ISeedData, LargeSemanticIdWidgetSeed>()),
        };

        using var client = factory.CreateClient(); // 触发启动;越界会在这里抛,不抛即通过
    }

    /// <summary>
    /// 关键回归测试:种子 Id 大到"未来一定会被这台实例的雪花号追上"必须在启动时被拒绝——
    /// 这是原静态上限 <c>ConsumerMax</c> 真正把牙留住的那一层,换成动态地板后不能丢。
    /// <para>Id 取"当前地板 + 一段安全余量"而非固定字面量:时间只会前进,固定字面量总有一天会低于地板而失去测试意义;
    /// 余量选得足够大(见 <see cref="FutureCollidingWidgetSeed"/>),盖过测试代码与 <c>DatabaseInitializer</c>
    /// 内部各自计算地板之间的毫秒级时间差,避免抖动。</para>
    /// </summary>
    [Fact]
    public void ConsumerSeed_IdAtOrAboveLiveFloorFailsAtStartup()
    {
        using var factory = new AdminAppFactory
        {
            Overrides = services => services.TryAddEnumerable(
                ServiceDescriptor.Transient<ISeedData, FutureCollidingWidgetSeed>()),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(nameof(FutureCollidingWidgetSeed), ex.Message);
    }

    /// <summary>
    /// <see cref="SnowflakeIdGenerator.CurrentFloor"/> 的位移换算:两个相隔 1 毫秒的时刻,算出的地板必须恰好相差
    /// <c>1 &lt;&lt; 12 = 4096</c>——不依赖具体纪元值也能验证公式没写错(纪元是私有实现细节,不需要在测试里知道)。
    /// </summary>
    [Fact]
    public void CurrentFloor_AdvancesByOneShiftedMillisecondPerMillisecond()
    {
        var t0 = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var floor0 = SnowflakeIdGenerator.CurrentFloor(new FixedTime(t0));
        var floor1 = SnowflakeIdGenerator.CurrentFloor(new FixedTime(t0.AddMilliseconds(1)));

        Assert.Equal(4096, floor1 - floor0);
    }

    /// <summary>
    /// <see cref="SnowflakeIdGenerator.CurrentFloor"/> 是"此刻起"的下界这条不变量的经验验证:
    /// 先算地板,再真发一个号,新号必须 &gt;= 地板(时钟只会前进,不会跑到地板算出来之前去)。
    /// </summary>
    [Fact]
    public void CurrentFloor_NeverExceedsAFreshlyGeneratedId()
    {
        var floor = SnowflakeIdGenerator.CurrentFloor();
        var id = new SnowflakeIdGenerator(workerId: 0).NextId();

        Assert.True(id >= floor, $"新发的雪花号 {id} 竟然低于刚算出的地板 {floor}。");
    }

    /// <summary>固定时钟,只为把地板算到确定的时间点做断言(仓库约定不引 FakeTimeProvider 包,自写最小实现)。</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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

    /// <summary>业务语义编号范例:产品线 9020 + 序号 010,远超原先写死的 4095 上限,现应被接受。</summary>
    private sealed class LargeSemanticIdWidgetSeed : ISeedData<SampleWidget>
    {
        public IEnumerable<SampleWidget> HasData() => [new SampleWidget { Id = 9_020_010, Name = "large-semantic-id-widget" }];
    }

    /// <summary>
    /// 越界范例:Id 定在"当前地板 + 10 亿"(约合 24 万毫秒、4 分钟后的雪花号段),必须在启动时被拒绝。
    /// 余量选得足够大,盖过测试自己算地板与 <c>DatabaseInitializer</c> 内部算地板之间的毫秒级时间差。
    /// </summary>
    private sealed class FutureCollidingWidgetSeed : ISeedData<SampleWidget>
    {
        public IEnumerable<SampleWidget> HasData() =>
            [new SampleWidget { Id = SnowflakeIdGenerator.CurrentFloor() + 1_000_000_000, Name = "future-colliding-widget" }];
    }
}
