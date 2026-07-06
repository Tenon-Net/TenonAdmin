#:project ../src/TenonAdmin.Services/TenonAdmin.Services.csproj
#:property ManagePackageVersionsCentrally=false
#:package Microsoft.Extensions.DependencyInjection@10.0.0
#:property PublishAot=false
// T1 RBAC 自检(设计 §6 验收):非超管用户 + 角色,只授某接口权限码 →
//   ① 已授码在集合内、未授码不在 ② 改授权后缓存即时失效重算 ③ 收回角色后归零。
// 直接跑 RbacPermissionProvider(热路径)+ RbacService(授权变更),不经 HTTP——
// [RolePermission] 过滤器只做 codes.Contains(code)(M1 已验),故验准这份码集 = 验准 200/403。
// 引 SqlSugar 的 file-based 脚本必须 #:property PublishAot=false(否则 Reflection.Emit 报错)。

using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

const string PING = "GET:/api/v1/ping";
const string SECRET = "POST:/api/v1/secret";
const long ROLE = 201, USER = 301, PING_MENU = 101, SECRET_MENU = 102;

var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-t1-{Guid.NewGuid():N}.db");
var services = new ServiceCollection();
services.AddSingleton(new AdminCacheOptions());   // 权限缓存 TTL 用默认,失效走显式
services.AddTenonAdminSqlSugar(new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = $"Data Source={dbFile}" },
    [typeof(ServicesSetup).Assembly]);
services.AddTenonAdminServices();
var sp = services.BuildServiceProvider();

// 建表(无宿主,手动触发 CodeFirst)
var db = sp.GetRequiredService<ISqlSugarClient>();
db.CodeFirst.InitTables(typeof(SysUser), typeof(SysRole), typeof(SysMenu), typeof(SysUserRole), typeof(SysRoleMenu));

// 备好数据:两个带码菜单、一个角色、一个非超管用户(挂上角色)
using (var scope = sp.CreateScope())
{
    var s = scope.ServiceProvider;
    await s.GetRequiredService<IRepository<SysMenu>>().InsertRangeAsync(
    [
        new() { Id = PING_MENU, ParentId = 0, Type = MenuType.Button, Title = "探针", Permission = PING, Enabled = true },
        new() { Id = SECRET_MENU, ParentId = 0, Type = MenuType.Button, Title = "机密", Permission = SECRET, Enabled = true },
    ]);
    await s.GetRequiredService<IRepository<SysRole>>().InsertAsync(new() { Id = ROLE, Name = "测试角色", Code = "tester", Enabled = true });
    await s.GetRequiredService<IRepository<SysUser>>().InsertAsync(new() { Id = USER, Account = "tester", Password = "x", Name = "测试", Enabled = true, IsSuperAdmin = false });
    await s.GetRequiredService<IRbacService>().SetUserRolesAsync(USER, [ROLE]);
}

// 每次在新作用域取权限码 = 模拟一次独立请求(scoped provider,共享单例缓存)
async Task<IReadOnlyCollection<string>> Codes()
{
    using var scope = sp.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<IPermissionProvider>().GetPermissionCodesAsync(USER);
}
async Task Grant(params long[] menuIds)
{
    using var scope = sp.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IRbacService>().SetRoleMenusAsync(ROLE, menuIds);
}

var pass = 0; var fail = 0;
void Check(string name, bool ok) { if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); } else { fail++; Console.WriteLine($"  FAIL  {name}"); } }

Console.WriteLine("T1 RBAC 自检:");

// 未授权:非超管用户权限码为空(默认拒绝)
Check("未授权 → 空集合(默认拒绝)", (await Codes()).Count == 0);

// 只授 ping:命中 ping、不含 secret(= /ping 200、/secret 403)
await Grant(PING_MENU);
var c1 = await Codes();
Check("授 ping → 含 GET:/api/v1/ping", c1.Contains(PING));
Check("授 ping → 不含未授的 secret", !c1.Contains(SECRET));

// 再授 secret:缓存即时失效,重算后两码都在(验证"改授权即时生效")
await Grant(PING_MENU, SECRET_MENU);
var c2 = await Codes();
Check("加授 secret → 缓存失效后含两码", c2.Contains(PING) && c2.Contains(SECRET));

// 收回全部菜单:归零
await Grant();
Check("收回菜单 → 归零", (await Codes()).Count == 0);

// 再授 ping 后解除用户的角色:也归零(验证 SetUserRoles 一侧的失效)
await Grant(PING_MENU);
Check("恢复授 ping → 含 ping", (await Codes()).Contains(PING));
using (var scope = sp.CreateScope())
    await scope.ServiceProvider.GetRequiredService<IRbacService>().SetUserRolesAsync(USER, []);
Check("解除用户角色 → 归零", (await Codes()).Count == 0);

Console.WriteLine($"\n结果:{pass} 通过 / {fail} 失败");
try { File.Delete(dbFile); } catch { }
Environment.Exit(fail == 0 ? 0 : 1);
