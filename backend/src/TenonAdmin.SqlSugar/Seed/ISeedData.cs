namespace TenonAdmin.SqlSugar;

/// <summary>
/// 种子数据非泛型标记——供 DI 统一收集(<c>GetServices&lt;ISeedData&gt;()</c>)。
/// 请勿直接实现本接口,实现泛型版 <see cref="ISeedData{TEntity}"/>。
/// </summary>
public interface ISeedData;

/// <summary>
/// 种子数据契约(扩展点,设计 §5.5/§5.7/§12)。首次启动自动执行、重复启动幂等。
/// <para><b>幂等规则:按主键判存,只插不存在的行、绝不更新已存在的行</b>——
/// 用户在界面上改过的数据不会被下次重启覆盖回种子值。
/// 因此种子实体<b>必须显式给定固定 Id</b>,且必须落在保留区间内(见 <see cref="TenonAdmin.Core.TenonSeedIds"/>:
/// 内核占 <c>[1, 999]</c>,<b>消费者从 1000 起、不超过 4095</b>;4096 以上是雪花运行时发号区,占了迟早主键冲突)。
/// 越界与 Id=0 都会被启动检查直接拒绝。</para>
/// <para>返回空集合是合法的:据此可实现"库里已有数据就不播种"(内置 <c>SuperAdminSeed</c> 正是这么做的)。</para>
/// <para>用法(用户侧一样,见设计 §5.7)——注意必须实现<b>泛型</b>版,非泛型 <see cref="ISeedData"/> 只是 DI 收集用的空标记:</para>
/// <code>
/// public class DeviceSeedData : ISeedData&lt;Device&gt;
/// {
///     public IEnumerable&lt;Device&gt; HasData() =&gt; [ new() { Id = TenonSeedIds.ConsumerMin, Name = "示例设备" } ];
/// }
///
/// // 在你自己的 Program.cs 里注册(按实现类型防重):
/// builder.Services.TryAddEnumerable(ServiceDescriptor.Transient&lt;ISeedData, DeviceSeedData&gt;());
/// </code>
/// <para>实现类支持构造注入(如 <c>IPasswordHasher</c>、<c>TenonAdminOptions</c>),注册为 <c>ISeedData</c> 的 DI 多实现。
/// <b>内核不扫描程序集找种子</b>——框架种子与用户种子都得显式注册(<c>options.ApplicationAssemblies</c> 只管实体建表与控制器挂载,
/// 不管种子)。忘了注册的后果是种子<b>静默不执行</b>,没有任何报错。</para>
/// </summary>
public interface ISeedData<out TEntity> : ISeedData where TEntity : BaseEntity, new()
{
    /// <summary>返回应当存在的种子行(带固定 Id)。启动时与库中现状按主键比对,缺哪行插哪行。</summary>
    IEnumerable<TEntity> HasData();
}
