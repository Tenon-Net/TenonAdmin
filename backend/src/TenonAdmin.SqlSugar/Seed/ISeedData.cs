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
/// 因此种子实体<b>必须显式给定固定 Id</b>(约定用小整数,雪花 ID 从时间戳生成不会与之冲突);
/// Id 为 0 会被启动检查直接拒绝(否则每次启动生成新雪花 Id,幂等失效、无限重复插入)。</para>
/// <para>用法(用户侧一样,见设计 §5.7):</para>
/// <code>
/// public class DeviceSeedData : ISeedData&lt;Device&gt;
/// {
///     public IEnumerable&lt;Device&gt; HasData() =&gt; [ new() { Id = 1, Name = "示例设备" } ];
/// }
/// </code>
/// <para>实现类支持构造注入(如 IPasswordHasher、IOptions),注册为 <c>ISeedData</c> 的 DI 多实现;
/// 框架种子显式注册,用户种子由程序集扫描接管(设计 §5.7 注册模型)。</para>
/// </summary>
public interface ISeedData<out TEntity> : ISeedData where TEntity : BaseEntity, new()
{
    /// <summary>返回应当存在的种子行(带固定 Id)。启动时与库中现状按主键比对,缺哪行插哪行。</summary>
    IEnumerable<TEntity> HasData();
}
