using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

internal sealed class DefaultPositionSeed : ISeedData<SysPosition>
{
    public IEnumerable<SysPosition> HasData() =>
    [
        new SysPosition { Id = 1, Name = "总经理", Code = "gm",        Sort = 1, Enabled = true },
        new SysPosition { Id = 2, Name = "副总经理", Code = "vp",       Sort = 2, Enabled = true },
        new SysPosition { Id = 3, Name = "部门经理", Code = "dm",       Sort = 3, Enabled = true },
        new SysPosition { Id = 4, Name = "组长",   Code = "lead",      Sort = 4, Enabled = true },
        new SysPosition { Id = 5, Name = "主管",   Code = "supervisor", Sort = 5, Enabled = true },
        new SysPosition { Id = 6, Name = "专员",   Code = "specialist", Sort = 6, Enabled = true },
    ];
}
