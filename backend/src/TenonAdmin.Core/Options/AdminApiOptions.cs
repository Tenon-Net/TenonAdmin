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

    /// <summary>反向代理转发头(部署在 nginx/Caddy/网关之后必配,见 <see cref="AdminForwardedHeadersOptions"/>)</summary>
    public AdminForwardedHeadersOptions ForwardedHeaders { get; set; } = new();
}

/// <summary>
/// 反向代理转发头配置(对应 <c>TenonAdmin:Api:ForwardedHeaders</c>,设计 §14)。
/// <para><b>为什么必须有</b>:反代之后,<c>Connection.RemoteIpAddress</c> 是<b>代理</b>的 IP 而非客户端的。
/// 而限流分区、登录日志、爆破防护全挂在这一个值上——不解析 <c>X-Forwarded-For</c> 时,
/// 全体用户共享同一个限流桶(一个人狂点登录就能把所有人限死),审计日志里的 IP 列也全是代理地址。</para>
/// <para><b>默认关</b>,且开启后必须显式声明受信来源:无条件采信 <c>X-Forwarded-For</c> 比不解析<b>更糟</b>——
/// 攻击者每个请求伪造一个不同的 IP,即可无限开新限流分区(限流被完全绕过),还能把爆破失败记到别人头上。
/// 故 <see cref="Enabled"/> 为 true 却未给任何受信来源时<b>启动即抛</b>。</para>
/// <para>代理无关:受信的是<b>来源地址/网段</b>,nginx / Caddy / Traefik / k8s ingress 一视同仁。</para>
/// </summary>
public class AdminForwardedHeadersOptions
{
    /// <summary>是否解析 <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c>。默认 false(未在代理后面时开启 = 允许任何人伪造 IP)。</summary>
    public bool Enabled { get; set; }

    /// <summary>受信代理的 IP(如 <c>["10.0.0.8"]</c>)。只有来自这些地址的请求,其转发头才被采信。</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>受信代理所在网段,CIDR 形式(容器编排下代理 IP 不固定,用网段更实际;如 <c>["172.16.0.0/12"]</c>)。</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    /// 最多回溯几跳。默认 1 = 只信<b>最靠近本服务</b>的那一跳(即我们自己的代理写下的那个值)。
    /// 调大意味着采信更外层代理写的值——只有当外层那些代理也全部受你控制时才可以。
    /// </summary>
    public int ForwardLimit { get; set; } = 1;
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
