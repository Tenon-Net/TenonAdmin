using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 测试用户种子——每种数据范围各一个用户,密码统一 123456。
/// 不设 SuperAdminSeed 的"表已有行就跳过"守卫:密码固定,靠 Storageable 幂等即可。
/// </summary>
internal sealed class DefaultUserSeed(IPasswordHasher hasher) : ISeedData<SysUser>
{
    public IEnumerable<SysUser> HasData()
    {
        var pwd = hasher.Hash("123456");
        return
        [
            new SysUser { Id = 2, Account = "scope_all",          Name = "全部数据",   Password = pwd, OrgId = 1, Enabled = true },
            new SysUser { Id = 3, Account = "scope_org",          Name = "本机构数据",  Password = pwd, OrgId = 3, Enabled = true },
            new SysUser { Id = 4, Account = "scope_org_children", Name = "本机构及以下", Password = pwd, OrgId = 3, Enabled = true },
            new SysUser { Id = 5, Account = "scope_self",         Name = "仅本人数据",  Password = pwd, OrgId = 4, Enabled = true },
            new SysUser { Id = 6, Account = "scope_custom",       Name = "自定义范围",  Password = pwd, OrgId = 1, Enabled = true },
        ];
    }
}
