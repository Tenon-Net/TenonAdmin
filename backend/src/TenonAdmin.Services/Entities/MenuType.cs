namespace TenonAdmin.Services;

/// <summary>
/// 菜单节点类型(设计 §16 目录/页面/按钮三级)。用枚举而非魔法数;存库为 int。
/// </summary>
public enum MenuType
{
    /// <summary>目录:仅用于前端左侧菜单分组,自身不对应页面、不带权限码</summary>
    Catalog = 1,

    /// <summary>页面:对应一个前端路由页;可带权限码(进入页面的权限)</summary>
    Menu = 2,

    /// <summary>按钮:页面内的操作点(新增/删除等),权限码 = 该操作对应的后端路由码</summary>
    Button = 3,
}
