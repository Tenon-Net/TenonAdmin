using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 种子升级同步(<see cref="ISeedData{TEntity}.SyncOnUpgrade"/> + <see cref="SysSchemaVersion.Current"/> 版本闸门)。
/// <para>解决的问题:种子默认只插不改,于是<b>新增</b>的种子行会流到老库,而<b>改动已有行</b>(挪菜单挂载点、
/// 给模块补图标)永远到不了——已经拉过代码的库停在旧结构上,只有删库重建才能看到。</para>
/// <para>这里锁的是一条<b>双向</b>契约,两半同样重要:</para>
/// <list type="bullet">
/// <item>升级时,结构性种子(菜单/模块)的已有行<b>会</b>被刷回种子值。</item>
/// <item>升级时,用户数据(配置中心/字典)的已有行<b>绝不</b>被回滚——这是本机制的全部安全性所在。
/// 无差别 upsert 会让用户存过的每一项配置在升级时静默还原,那比"结构同步不了"坏得多。</item>
/// <item>平时重启(版本没变)一行都不碰。</item>
/// </list>
/// </summary>
public class SeedUpgradeTests
{
    private const long WorkbenchMenuId = 108;    // DefaultMenuSeed:工作台(根级菜单)
    private const long SiteTitleConfigId = 1;    // ConfigSeed:站点标题
    private const long EnabledDictItemId = 1;    // DictSeed:通用状态「启用」

    /// <summary>把库退化成"老库":版本号打回旧值(模拟内核 bump 之前的那份库)</summary>
    private static async Task DowngradeVersionAsync(ISqlSugarClient db) =>
        await db.Updateable<SysSchemaVersion>()
            .SetColumns(x => new SysSchemaVersion { Version = "0.0.1" })
            .Where(x => x.Id == 1)
            .ExecuteCommandAsync();

    /// <summary>建库 → 用 <paramref name="tamper"/> 改库 → 在同一个库上重启宿主 → 用 <paramref name="assert"/> 断言</summary>
    private static async Task RestartWithAsync(
        Func<ISqlSugarClient, Task> tamper,
        Func<ISqlSugarClient, Task> assert)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tenon-seedup-{Guid.NewGuid():N}.db");

        try
        {
            // 1) 首启:建表 + 播种,得到一个"正常的库"
            using (var v1 = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false })
            {
                _ = v1.CreateClient();
                using var scope = v1.Services.CreateScope();
                await tamper(scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
            }

            // 2) 同库二次启动:种子再跑一遍(升不升级取决于 tamper 有没有把版本号打回去)
            using var v2 = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false };
            _ = v2.CreateClient();
            using var s2 = v2.Services.CreateScope();
            await assert(s2.ServiceProvider.GetRequiredService<ISqlSugarClient>());
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
        }
    }

    /// <summary>升级时:结构性种子(菜单)的已有行被刷回种子值,版本号落到 Current。</summary>
    [Fact]
    public async Task Upgrade_syncs_structural_seed_rows()
    {
        await RestartWithAsync(
            async db =>
            {
                await DowngradeVersionAsync(db);
                // 模拟"老库里的旧结构":标题和挂载点都跟当前种子不一致
                await db.Updateable<SysMenu>()
                    .SetColumns(x => new SysMenu { Title = "旧标题", ParentId = 999 })
                    .Where(x => x.Id == WorkbenchMenuId)
                    .ExecuteCommandAsync();
            },
            async db =>
            {
                var menu = await db.Queryable<SysMenu>().FirstAsync(x => x.Id == WorkbenchMenuId);
                Assert.Equal("工作台", menu.Title);   // 刷回种子值
                Assert.Equal(0, menu.ParentId);       // 挂载点也刷回来了——这正是老库拿不到的那类改动

                var version = await db.Queryable<SysSchemaVersion>().FirstAsync(x => x.Id == 1);
                Assert.Equal(SysSchemaVersion.Current, version.Version);   // 版本写回,下次启动不再当升级
            });
    }

    /// <summary>
    /// 升级时:用户数据<b>绝不</b>被回滚。这条是本机制的安全边界——挂了就说明有人给
    /// ConfigSeed/DictSeed 之类开了 SyncOnUpgrade,用户存过的配置会在升级时静默还原。
    /// </summary>
    [Fact]
    public async Task Upgrade_never_reverts_user_edited_rows()
    {
        await RestartWithAsync(
            async db =>
            {
                await DowngradeVersionAsync(db);
                // 用户在配置中心改了站点标题、在字典里改了标签——都是种子行(同 Id)
                await db.Updateable<SysConfig>()
                    .SetColumns(x => new SysConfig { ConfigValue = "我司后台" })
                    .Where(x => x.Id == SiteTitleConfigId)
                    .ExecuteCommandAsync();
                await db.Updateable<SysDictItem>()
                    .SetColumns(x => new SysDictItem { Label = "生效中" })
                    .Where(x => x.Id == EnabledDictItemId)
                    .ExecuteCommandAsync();
            },
            async db =>
            {
                var config = await db.Queryable<SysConfig>().FirstAsync(x => x.Id == SiteTitleConfigId);
                Assert.Equal("我司后台", config.ConfigValue);   // 不是 "TenonAdmin"

                var item = await db.Queryable<SysDictItem>().FirstAsync(x => x.Id == EnabledDictItemId);
                Assert.Equal("生效中", item.Label);             // 不是 "启用"
            });
    }

    /// <summary>平时重启(版本没变):连结构性种子的已有行也不碰——用户改的内置菜单标题照常留着。</summary>
    [Fact]
    public async Task Restart_without_version_bump_overwrites_nothing()
    {
        await RestartWithAsync(
            // 注意:不降版本 —— 库里已是 Current
            async db => await db.Updateable<SysMenu>()
                .SetColumns(x => new SysMenu { Title = "我的工作台" })
                .Where(x => x.Id == WorkbenchMenuId)
                .ExecuteCommandAsync(),
            async db =>
            {
                var menu = await db.Queryable<SysMenu>().FirstAsync(x => x.Id == WorkbenchMenuId);
                Assert.Equal("我的工作台", menu.Title);   // 版本没变 → 一行都不刷
            });
    }

    /// <summary>
    /// 连接表种子(sys_user_role)的代理主键被运行时授权改成雪花号后,重启不能崩。
    /// <para>真实事故:管理员在角色页把某默认用户的角色删了又重授,重授走 AOP 拿到<b>雪花号</b>,
    /// 于是同一 (UserId,RoleId) 在库里挂的不是种子的固定 Id。若种子仍按主键判存,会拿固定 Id 把这对
    /// 业务键再插一遍 → 撞 (UserId,RoleId) 唯一索引 → 启动直接失败。按业务键判存(DedupColumns)才对。</para>
    /// </summary>
    [Fact]
    public async Task Join_table_seed_dedups_by_business_key_not_drifted_surrogate_id()
    {
        const long UserId = 5, RoleId = 5;   // DefaultUserRoleSeed 里这对的固定 Id 是 4
        const long SnowflakeId = 68860291608576;   // 模拟运行时授权发的雪花号

        await RestartWithAsync(
            async db =>
            {
                // 模拟角色页"删了又重授"(RbacService 语义:物理删除 + 重插,Id 走雪花号):
                // 于是这对业务键在库里挂的不再是种子的固定 Id 4,而是雪花 Id。
                await db.Deleteable<SysUserRole>()
                    .Where(x => x.UserId == UserId && x.RoleId == RoleId).ExecuteCommandAsync();
                await db.Insertable(new SysUserRole { Id = SnowflakeId, UserId = UserId, RoleId = RoleId })
                    .ExecuteCommandAsync();
            },
            async db =>
            {
                // 起得来 = 没撞唯一索引(CreateClient 已在 RestartWithAsync 里跑过而未抛)
                var rows = await db.Queryable<SysUserRole>()
                    .Where(x => x.UserId == UserId && x.RoleId == RoleId).ToListAsync();
                Assert.Single(rows);                     // 没被种子重复插入
                Assert.Equal(SnowflakeId, rows[0].Id);   // 保留运行时那条,种子跳过
            });
    }

    /// <summary>
    /// 用户软删掉一条内置菜单后,应用还得起得来。
    /// <para>存在性判断若走全局软删过滤器,这行就"查不到"→ 判成缺失 → 走 INSERT → <b>撞主键</b>,
    /// 应用再也起不来。种子的判存必须看<b>物理</b>行(ClearFilter),故有此用例。</para>
    /// <para>顺带锁住:升级也不该把它<b>复活</b>(IsDelete 不在覆盖列里)——用户删了就是删了。</para>
    /// </summary>
    [Fact]
    public async Task Soft_deleted_seed_row_neither_crashes_startup_nor_resurrects()
    {
        await RestartWithAsync(
            async db =>
            {
                await DowngradeVersionAsync(db);   // 走升级路径:覆盖逻辑也一并经过这行
                await db.Updateable<SysMenu>()
                    .SetColumns(x => new SysMenu { IsDelete = true })
                    .Where(x => x.Id == WorkbenchMenuId)
                    .ExecuteCommandAsync();
            },
            async db =>
            {
                // 起得来 = 没撞主键(CreateClient 已在 RestartWithAsync 里跑过)
                var menu = await db.Queryable<SysMenu>().ClearFilter()
                    .FirstAsync(x => x.Id == WorkbenchMenuId);

                Assert.True(menu.IsDelete);   // 仍是删除态,没被升级复活
                Assert.Single(await db.Queryable<SysMenu>().ClearFilter()
                    .Where(x => x.Id == WorkbenchMenuId).ToListAsync());   // 没被重复插入
            });
    }
}
