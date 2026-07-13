using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 批量删除约定的回归:普通用户整批软删生效;集合里混入超管则整体拒绝(SuperAdminProtected)、且一个都不删——
/// 守住"超管不可删"不变量不被批量入口绕过,以及批量删除的原子性(全成或全不动)。
/// </summary>
public class BatchDeleteTests
{
    private static async Task<long> AddUser(IServiceProvider sp, string suffix)
    {
        var users = sp.GetRequiredService<IUserService>();
        var added = await users.AddAsync(new AddUserInput
        {
            Account = "batch-" + suffix,
            Password = "Batch@123456",
            Name = "批量用户" + suffix,
            Enabled = true,
        });
        return added.Id;
    }

    [Fact]
    public async Task Batch_delete_soft_deletes_all_normal_users()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<IUserService>();
        var repo = sp.GetRequiredService<IRepository<SysUser>>();

        var id1 = await AddUser(sp, "a");
        var id2 = await AddUser(sp, "b");

        await users.DeleteBatchAsync([id1, id2]);

        // 全局软删过滤器让已删行对查询不可见 → 两个都查不到
        Assert.False(await repo.AnyAsync(u => u.Id == id1));
        Assert.False(await repo.AnyAsync(u => u.Id == id2));
    }

    [Fact]
    public async Task Batch_delete_rejects_whole_set_when_super_admin_included()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<IUserService>();
        var repo = sp.GetRequiredService<IRepository<SysUser>>();

        var normalId = await AddUser(sp, "c");
        var superId = (await repo.GetFirstAsync(u => u.IsSuperAdmin))!.Id;

        // 集合含超管 → 抛 SuperAdminProtected
        var ex = await Assert.ThrowsAsync<AdminException>(() => users.DeleteBatchAsync([normalId, superId]));
        Assert.Equal(ErrorCode.SuperAdminProtected, ex.Code);

        // 原子性:整批回滚,普通用户也没被删
        Assert.True(await repo.AnyAsync(u => u.Id == normalId));
        Assert.True(await repo.AnyAsync(u => u.Id == superId));
    }
}
