namespace TenonAdmin.Integration;

/// <summary>按名查找投递适配器:入队、投递器与人工操作共用同一规则(不区分大小写;启动校验保证名称唯一)。</summary>
internal static class DeliveryAdapterLookup
{
    /// <summary>按名取适配器;未注册返回 null。</summary>
    public static IDeliveryAdapter? FindByName(this IEnumerable<IDeliveryAdapter> adapters, string name) =>
        adapters.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}
