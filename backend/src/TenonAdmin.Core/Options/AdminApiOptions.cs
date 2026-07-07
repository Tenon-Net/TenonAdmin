namespace TenonAdmin.Core;

/// <summary>
/// API 配置(对应 <c>TenonAdmin:Api</c> 节,设计 §3.2/§5.4)。
/// <para>v1 先落 <see cref="DisabledModules"/>(按模块禁用内置控制器)。
/// <c>RoutePrefix</c>/<c>Version</c> 配置化因深度耦合权限码(<c>{METHOD}:/{路由}</c>)与菜单种子,
/// 留待专门一轮/ v1.x 处理;v1 内置路由固定 <c>api/v1</c>(见 dev-plan T8d-ii)。</para>
/// </summary>
public class AdminApiOptions
{
    /// <summary>
    /// 禁用的模块名。带 <c>[Module("名字")]</c> 的内置控制器若名字命中此列表,则整体不注册路由
    /// (等于关掉该模块的接口)。例:<c>["Upload","Dict"]</c> 关掉文件上传与字典模块。大小写不敏感。
    /// </summary>
    public string[] DisabledModules { get; set; } = [];

    /// <summary>CORS 跨源策略(前后端分离部署必配,见 <see cref="AdminCorsOptions"/>)</summary>
    public AdminCorsOptions Cors { get; set; } = new();
}

/// <summary>
/// CORS 配置(对应 <c>TenonAdmin:Api:Cors</c>,设计 §12/§14)。
/// <para><b>默认收紧</b>:<see cref="AllowedOrigins"/> 为空即不放行任何跨源(生产必须显式配置放行的前端源)。
/// 由框架 <c>IStartupFilter</c> 在管道前段挂载全局命名策略,无需用户手动 <c>UseCors</c>(保三行零配置宿主)。</para>
/// </summary>
public class AdminCorsOptions
{
    /// <summary>允许的跨源(如 <c>["https://admin.example.com"]</c>);空 = 不放行任何跨源(默认收紧)。</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>是否允许携带凭证(cookie/Authorization)。仅在显式列出 <see cref="AllowedOrigins"/> 时生效(严禁 AllowAnyOrigin + 凭证)。</summary>
    public bool AllowCredentials { get; set; } = true;
}
