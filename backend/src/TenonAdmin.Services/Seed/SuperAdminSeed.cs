using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 超级管理员种子(设计 §3.1 零配置默认行为 / §18 验收闭环第 5 步)。
/// <para>密码策略:配置了 <c>TenonAdmin:Seed:AdminPassword</c> 用配置值;
/// 没配(默认)则<b>随机生成并在启动日志醒目打印一次</b>——只在真正建号的那次启动打印,
/// 后续启动不再打印(打印一个已失效的随机密码只会误导人)。</para>
/// </summary>
internal sealed class SuperAdminSeed(
    IRepository<SysUser> users,
    IPasswordHasher hasher,
    TenonAdminOptions options,
    ILogger<SuperAdminSeed> logger) : ISeedData<SysUser>
{
    /// <summary>超管固定主键(种子幂等锚点)</summary>
    internal const long SUPER_ADMIN_ID = 1;

    public IEnumerable<SysUser> HasData()
    {
        // 表里已有任何用户就不再产种子:既保幂等,也避免每次启动重新生成/打印一个根本没写进库的密码
        if (users.AsQueryable().Any()) return [];

        var password = options.Seed.AdminPassword;
        var generated = string.IsNullOrEmpty(password);
        if (generated) password = GeneratePassword();

        if (generated)
            logger.LogWarning("""

                ╔══════════════════════════════════════════════════════╗
                ║  TenonAdmin 首次启动,已创建超级管理员                  ║
                ║  账号: {Account}
                ║  密码: {Password}
                ║  此密码仅本次显示,请登录后立即修改!                    ║
                ╚══════════════════════════════════════════════════════╝
                """, options.Seed.AdminAccount, password);
        else
            logger.LogInformation("TenonAdmin: 已按配置创建超级管理员 {Account}", options.Seed.AdminAccount);

        return
        [
            new SysUser
            {
                Id = SUPER_ADMIN_ID,
                Account = options.Seed.AdminAccount,
                Password = hasher.Hash(password!),
                Name = "超级管理员",
                Enabled = true,
                IsSuperAdmin = true,
            },
        ];
    }

    /// <summary>
    /// 生成 16 位随机密码。字符集刻意剔除易混淆字符(0/O、1/l/I)——这密码是要人从日志里抄出来敲的。
    /// 用加密安全随机源(<see cref="RandomNumberGenerator"/>),不用 Random。
    /// </summary>
    private static string GeneratePassword()
    {
        const string charset = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
        return string.Create(16, charset, static (span, set) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = set[RandomNumberGenerator.GetInt32(set.Length)];
        });
    }
}
