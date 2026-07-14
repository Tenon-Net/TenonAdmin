using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

internal sealed class DefaultOrgSeed : ISeedData<SysOrg>
{
    internal const long ROOT_ORG_ID = 1;

    public IEnumerable<SysOrg> HasData() =>
    [
        new SysOrg { Id = 1, ParentId = 0, Name = "榫卯科技", Code = "tenon",       Category = "1", Sort = 1, Enabled = true },
        new SysOrg { Id = 2, ParentId = 1, Name = "总经办",   Code = "tenon_gm",    Category = "2", Sort = 1, Enabled = true },
        new SysOrg { Id = 3, ParentId = 1, Name = "技术部",   Code = "tenon_tech",  Category = "2", Sort = 2, Enabled = true },
        new SysOrg { Id = 4, ParentId = 3, Name = "前端组",   Code = "tenon_fe",    Category = "3", Sort = 1, Enabled = true },
        new SysOrg { Id = 5, ParentId = 3, Name = "后端组",   Code = "tenon_be",    Category = "3", Sort = 2, Enabled = true },
        new SysOrg { Id = 6, ParentId = 1, Name = "产品部",   Code = "tenon_pm",    Category = "2", Sort = 3, Enabled = true },
        new SysOrg { Id = 7, ParentId = 1, Name = "人事部",   Code = "tenon_hr",    Category = "2", Sort = 4, Enabled = true },
    ];
}
