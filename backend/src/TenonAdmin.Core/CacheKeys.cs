namespace TenonAdmin.Core;

/// <summary>
/// 缓存逻辑键(禁硬编码字符串纪律,设计 §6/§15)——所有缓存键在此集中定义,不散落魔法串。
/// <para>这里给的是<b>不含前缀</b>的逻辑键;<c>Cache:KeyPrefix</c>(默认 <c>tenon:</c>)由
/// <see cref="ICacheProvider"/> 实现统一追加。故实际 Redis 键形如 <c>tenon:perm:123</c>(对应设计 §15 命名)。</para>
/// </summary>
public static class CacheKeys
{
    /// <summary>某用户当前生效的权限码集合(RBAC 聚合结果;授权变更时按 userId 精确失效)</summary>
    public static string UserPermissions(long userId) => $"perm:{userId}";
}
