namespace TenonAdmin.Core;

/// <summary>
/// 分布式 ID 生成器(扩展点,设计 §5.5)。
/// <para>框架内所有实体主键(<c>BaseEntity.Id</c>)由它产生。默认实现为自写雪花算法
/// <see cref="SnowflakeIdGenerator"/>;要换数据库自增、GUID v7(<c>Guid.CreateVersion7()</c>)
/// 等策略,自行实现本接口并在 <c>AddTenonAdmin()</c> 之前注册即可(TryAdd 语义,用户注册优先)。</para>
/// </summary>
public interface IIdGenerator
{
    /// <summary>生成下一个全局唯一、趋势递增的 64 位 ID。要求线程安全。</summary>
    long NextId();
}
