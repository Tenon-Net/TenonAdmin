using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 默认角色种子(设计 §16,T1)。播一个空授权的"系统管理员"角色作为起步模板——
/// 超管不受 RBAC 约束(直接放行),此角色供管理员克隆/授权后分配给普通用户。
/// 固定 Id(幂等锚点);用户在界面上的授权改动不会被重启覆盖。
/// </summary>
internal sealed class DefaultRoleSeed : ISeedData<SysRole>
{
    /// <summary>默认角色固定主键</summary>
    internal const long DEFAULT_ROLE_ID = 1;

    public IEnumerable<SysRole> HasData() =>
    [
        new SysRole { Id = DEFAULT_ROLE_ID, Name = "系统管理员", Code = "sys_admin", Sort = 1, Enabled = true, Remark = "内置默认角色,授权后分配给普通管理员" },
        new SysRole { Id = 2, Name = "全部数据",   Code = "scope_all",          Sort = 2, Enabled = true, Remark = "数据范围:全部" },
        new SysRole { Id = 3, Name = "本机构数据",  Code = "scope_org",          Sort = 3, Enabled = true, Remark = "数据范围:仅本机构" },
        new SysRole { Id = 4, Name = "本机构及以下", Code = "scope_org_children", Sort = 4, Enabled = true, Remark = "数据范围:本机构及子机构" },
        new SysRole { Id = 5, Name = "仅本人数据",  Code = "scope_self",         Sort = 5, Enabled = true, Remark = "数据范围:仅本人" },
        new SysRole { Id = 6, Name = "自定义范围",  Code = "scope_custom",       Sort = 6, Enabled = true, Remark = "数据范围:自定义(总经办+产品部)" },
    ];
}
