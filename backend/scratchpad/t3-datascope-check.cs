#:project ../src/TenonAdmin.Services/TenonAdmin.Services.csproj
#:property ManagePackageVersionsCentrally=false
#:package Microsoft.Extensions.DependencyInjection@10.0.0
#:property PublishAot=false
// T3 自检(设计 §4/§8 数据范围验收):真跑 SqlSugar 全局过滤器 + DataScopeProvider 解析 + 缓存失效。
// 用一个临时 DataEntity(TestDoc)承载不同 CreateOrgId/CreateUserId 的行,切换生效范围看可见行集变化——
// 这等价于"两个不同机构的用户查同一列表接口得到不同数据集"(§8 测试点 2),且直接压 ORM 层过滤器最严谨。

using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-t3-{Guid.NewGuid():N}.db");
var services = new ServiceCollection();
services.AddSingleton(new AdminCacheOptions());
services.AddTenonAdminSqlSugar(new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = $"Data Source={dbFile}" },
    [typeof(ServicesSetup).Assembly]);
services.AddTenonAdminServices();
var sp = services.BuildServiceProvider();

var db = sp.GetRequiredService<ISqlSugarClient>();
db.CodeFirst.InitTables(typeof(TestDoc), typeof(SysUser), typeof(SysRole), typeof(SysUserRole),
    typeof(SysRoleMenu), typeof(SysRoleDataScope), typeof(SysOrg), typeof(SysMenu), typeof(SysPosition));

var ctx = sp.GetRequiredService<IDataScopeContext>();

var pass = 0; var fail = 0;
void Check(string name, bool ok) { if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); } else { fail++; Console.WriteLine($"  FAIL  {name}"); } }

// 装数据(默认 Unrestricted 上下文,插入不受过滤器影响)
using (var scope = sp.CreateScope())
{
    var docs = scope.ServiceProvider.GetRequiredService<IRepository<TestDoc>>();
    // CreateOrgId / CreateUserId 显式给定(审计填充是 T4 才接;这里直接落值)
    await docs.InsertRangeAsync(
    [
        new() { Title = "A", CreateOrgId = 10, CreateUserId = 100 },
        new() { Title = "B", CreateOrgId = 11, CreateUserId = 101 },
        new() { Title = "C", CreateOrgId = 20, CreateUserId = 102 },
        new() { Title = "D", CreateOrgId = null, CreateUserId = 101 },   // 无机构、用户101 创建
    ]);
}

// 用新作用域查(模拟一次请求);读的是同一个单例 AsyncLocal 上下文
async Task<int> CountDocs()
{
    using var scope = sp.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<IRepository<TestDoc>>().AsQueryable().CountAsync();
}

Console.WriteLine("T3 数据范围自检 —— 全局过滤器(直接切上下文):");

ctx.Current = DataScopeResult.Unrestricted;
Check("不受限 → 看到全部 4 行", await CountDocs() == 4);

ctx.Current = DataScopeResult.Restricted([10], includeSelf: false, userId: 999);
Check("本机构{10} → 只看到 A", await CountDocs() == 1);

ctx.Current = DataScopeResult.Restricted([10, 20], includeSelf: false, userId: 999);
Check("机构{10,20} → 看到 A+C(2 行)", await CountDocs() == 2);

ctx.Current = DataScopeResult.Restricted([], includeSelf: true, userId: 101);
Check("仅本人(101)→ 看到 B+D(2 行)", await CountDocs() == 2);

ctx.Current = DataScopeResult.Restricted([10], includeSelf: true, userId: 102);
Check("机构{10} ∪ 本人(102)→ A+C(2 行)", await CountDocs() == 2);

ctx.Current = DataScopeResult.Restricted([], includeSelf: false, userId: 0);
Check("空范围 → 默认拒绝,看到 0 行", await CountDocs() == 0);

// 招牌验收:两个不同机构用户查同一 TestDoc 列表得到不同数据集
ctx.Current = DataScopeResult.Restricted([11], false, 0);
var orgB = await CountDocs();
ctx.Current = DataScopeResult.Restricted([20], false, 0);
var orgC = await CountDocs();
Check("§8 测试点2:机构11用户见1行、机构20用户见1行且互不相同", orgB == 1 && orgC == 1);

// ── DataScopeProvider 解析 + 缓存失效(真实角色/机构树)──
Console.WriteLine("T3 数据范围自检 —— Provider 解析 + 缓存失效:");
ctx.Current = DataScopeResult.Unrestricted;   // 解析过程自身查的是 BaseEntity 表,不受影响;稳妥置不受限
using (var scope = sp.CreateScope())
{
    var s = scope.ServiceProvider;
    // 机构树:10 根 → 11 子 → 12 孙;另有独立 20
    await s.GetRequiredService<IRepository<SysOrg>>().InsertRangeAsync(
    [
        new() { Id = 10, ParentId = 0, Name = "根", Code = "o10" },
        new() { Id = 11, ParentId = 10, Name = "子", Code = "o11" },
        new() { Id = 12, ParentId = 11, Name = "孙", Code = "o12" },
        new() { Id = 20, ParentId = 0, Name = "独立", Code = "o20" },
    ]);
    await s.GetRequiredService<IRepository<SysRole>>().InsertAsync(new() { Id = 300, Name = "范围角色", Code = "scoped" });
    await s.GetRequiredService<IRepository<SysUser>>().InsertAsync(new() { Id = 200, Account = "u200", Password = "x", Name = "U", OrgId = 11 });
    await s.GetRequiredService<IRbacService>().SetUserRolesAsync(200, [300]);
}

async Task<DataScopeResult> Resolve()
{
    using var scope = sp.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<IDataScopeProvider>().ResolveAsync(200);
}
async Task SetScope(DataScopeType type, long[]? custom = null)
{
    using var scope = sp.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IRbacService>().SetRoleDataScopeAsync(300, type, custom);
}

await SetScope(DataScopeType.OrgAndChildren);
var r1 = await Resolve();
Check("本机构及以下:用户(org11)→ {11,12} 且不含 10/20", !r1.IsUnrestricted && r1.OrgIds.Contains(11) && r1.OrgIds.Contains(12) && !r1.OrgIds.Contains(10) && !r1.OrgIds.Contains(20));

await SetScope(DataScopeType.All);      // 改范围即失效缓存
Check("改为 All → 重解析为不受限(缓存已失效)", (await Resolve()).IsUnrestricted);

await SetScope(DataScopeType.Org);
var r3 = await Resolve();
Check("本机构 → 只 {11}", !r3.IsUnrestricted && r3.OrgIds.Count == 1 && r3.OrgIds.Contains(11));

await SetScope(DataScopeType.Self);
var r4 = await Resolve();
Check("仅本人 → IncludeSelf 且机构集为空、UserId=200", !r4.IsUnrestricted && r4.IncludeSelf && r4.OrgIds.Count == 0 && r4.UserId == 200);

await SetScope(DataScopeType.Custom, [10, 20]);
var r5 = await Resolve();
Check("自定义[10,20] → {10,20}", !r5.IsUnrestricted && r5.OrgIds.Contains(10) && r5.OrgIds.Contains(20) && !r5.OrgIds.Contains(11));

Console.WriteLine($"\n结果:{pass} 通过 / {fail} 失败");
try { File.Delete(dbFile); } catch { }
Environment.Exit(fail == 0 ? 0 : 1);

// 临时业务实体:继承 DataEntity 即自动进入全局数据范围过滤(CreateOrgId 锚点)。
// 顶级语句程序里类型声明须放在语句之后(文件末尾)。
[SugarTable("test_doc")]
public class TestDoc : DataEntity
{
    [SugarColumn(Length = 64)] public string Title { get; set; } = "";
}
