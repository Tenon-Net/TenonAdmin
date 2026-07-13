namespace TenonAdmin.AspNetCore;

/// <summary>
/// 权限码的<b>唯一规范化真源</b>(设计 §6):<c>{大写 METHOD}:/{小写路由模板}</c>,如 <c>GET:/api/v1/ping</c>。
/// <para>用路由模板而非实际路径——带参数的路由(<c>user/{id}</c>)权限码稳定,不随参数值变化。</para>
/// <para>三处共用同一函数,防止"授权时算的码"与"种子/UI 里写的码"因大小写、前导斜杠、尾斜杠等
/// 差一个字符而静默对不上(授权成功、点了 403,且无任何报错):
/// <see cref="RolePermissionAttribute"/> 授权比对、<c>MenuController.Routes</c> 路由清单(喂菜单表单的权限码下拉)、
/// <see cref="OperationLogFilter"/> 的缺省操作名。public 是刻意的——消费方自建控制器时同样能算出自己的码。</para>
/// </summary>
public static class PermissionCode
{
    /// <summary>按规范拼权限码;<paramref name="routeTemplate"/> 为空时返回 <c>{METHOD}:/</c>(不会匹配任何授权)。</summary>
    public static string Build(string httpMethod, string? routeTemplate) =>
        $"{httpMethod.ToUpperInvariant()}:/{(routeTemplate ?? "").TrimStart('/').ToLowerInvariant()}";
}

/// <summary>
/// 一条可授权路由(<c>MenuController.Routes</c> 的出参)——喂菜单表单里"权限码"字段的下拉。
/// <c>Method</c>/<c>Path</c> 只为让人看得懂选的是哪个接口;真正写进菜单的是 <see cref="Code"/>。
/// </summary>
public record PermissionRouteItem
{
    /// <summary>权限码(= 规范化路由,直接写进 <c>SysMenu.Permission</c>)</summary>
    public required string Code { get; init; }

    /// <summary>HTTP 方法(大写)</summary>
    public required string Method { get; init; }

    /// <summary>路由模板(原样,含 <c>{id}</c> 占位符)</summary>
    public required string Path { get; init; }
}
