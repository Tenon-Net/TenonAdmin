using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

internal sealed class DefaultUserRoleSeed : ISeedData<SysUserRole>
{
    /// <summary>连接表:代理主键是运行时雪花号、会漂,按 (UserId, RoleId) 判存(见 <see cref="ISeedData{T}.DedupColumns"/>)。</summary>
    public string[] DedupColumns => [nameof(SysUserRole.UserId), nameof(SysUserRole.RoleId)];

    public IEnumerable<SysUserRole> HasData() =>
    [
        new SysUserRole { Id = 1, UserId = 2, RoleId = 2 },
        new SysUserRole { Id = 2, UserId = 3, RoleId = 3 },
        new SysUserRole { Id = 3, UserId = 4, RoleId = 4 },
        new SysUserRole { Id = 4, UserId = 5, RoleId = 5 },
        new SysUserRole { Id = 5, UserId = 6, RoleId = 6 },
    ];
}
