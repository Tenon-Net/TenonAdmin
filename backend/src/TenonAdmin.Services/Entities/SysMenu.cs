using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 菜单/权限表(设计 §16)——目录/页面/按钮三级树(<see cref="MenuType"/>)。
/// <para>关键字段 <see cref="Permission"/>:节点绑定的<b>规范化路由权限码</b>(如 <c>GET:/api/v1/ping</c>),
/// 与 <c>[RolePermission]</c> 授权管道算出的码一致——不手写 <c>"sys:user:add"</c> 之类魔法串(设计 §6)。
/// 角色勾选菜单即完成配权,用户权限码 = 所属角色菜单的 Permission 并集。</para>
/// <para>前端展示字段(Path/Component/Icon/Visible 等)随 M2 前端接入时增列——CodeFirst 只增不改,先窄后宽。</para>
/// </summary>
[SugarTable("sys_menu", TableDescription = "菜单/权限")]
public class SysMenu : BaseEntity
{
    /// <summary>父节点 Id;0 表示根节点(顶级目录)</summary>
    [SugarColumn(ColumnDescription = "父菜单 Id(0=根)")]
    public long ParentId { get; set; }

    [SugarColumn(ColumnDescription = "节点类型(目录/页面/按钮)")]
    public MenuType Type { get; set; }

    [SugarColumn(Length = 64, ColumnDescription = "显示标题")]
    public string Title { get; set; } = "";

    /// <summary>
    /// 规范化路由权限码,形如 <c>GET:/api/v1/ping</c>(大写 Method + 冒号 + 小写路由模板)。
    /// 目录节点为空;页面/按钮节点带码。授权即比对此码与请求路由是否一致。
    /// </summary>
    [SugarColumn(Length = 256, ColumnDescription = "路由权限码(目录为空)")]
    public string Permission { get; set; } = "";

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }

    [SugarColumn(ColumnDescription = "是否启用(停用后其权限码不再授出)")]
    public bool Enabled { get; set; } = true;
}
