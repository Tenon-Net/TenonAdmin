namespace TenonAdmin.Core;

/// <summary>
/// 种子配置(对应 <c>TenonAdmin:Seed</c> 节,设计 §3.2)——控制首启内置数据的初始值。
/// </summary>
public class AdminSeedOptions
{
    /// <summary>超级管理员登录账号</summary>
    public string AdminAccount { get; set; } = "superAdmin";

    /// <summary>
    /// 超级管理员初始密码。<b>null(默认)= 首启随机生成并打印到启动日志</b>——
    /// 比固定默认密码安全得多(公网上跑着无数没改过 admin/123456 的系统)。
    /// 仅首次建号时生效,之后改密码走个人中心。
    /// </summary>
    public string? AdminPassword { get; set; }
}
