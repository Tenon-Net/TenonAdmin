#:project ../src/TenonAdmin.Services/TenonAdmin.Services.csproj
#:property ManagePackageVersionsCentrally=false
#:package Microsoft.Extensions.DependencyInjection@10.0.0
#:property PublishAot=false
// T4 附:审计字段 AOP 填充自检——有登录上下文时 CreateUserId(插入)/UpdateUserId(更新)自动填当前用户。
// 用桩 ICurrentUser(UserId=42)前置注册覆盖兜底的 SystemCurrentUser。

using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-t4b-{Guid.NewGuid():N}.db");
var services = new ServiceCollection();
services.AddSingleton(new AdminCacheOptions());
services.AddSingleton<ICurrentUser>(new StubCurrentUser(42));   // 前置注册,压过 SqlSugar 层兜底
services.AddTenonAdminSqlSugar(new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = $"Data Source={dbFile}" },
    [typeof(ServicesSetup).Assembly]);
services.AddTenonAdminServices();
var sp = services.BuildServiceProvider();
sp.GetRequiredService<ISqlSugarClient>().CodeFirst.InitTables(typeof(SysRole));

var pass = 0; var fail = 0;
void Check(string name, bool ok) { if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); } else { fail++; Console.WriteLine($"  FAIL  {name}"); } }

Console.WriteLine("T4 审计字段填充自检:");
using (var scope = sp.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<IRepository<SysRole>>();
    var role = new SysRole { Name = "R", Code = "r1", Enabled = true };
    await repo.InsertAsync(role);
    var inserted = await repo.GetByIdAsync(role.Id);
    Check("插入 → CreateUserId 自动填当前用户(42)", inserted!.CreateUserId == 42);

    inserted.Name = "R2";
    await repo.UpdateAsync(inserted);
    var updated = await repo.GetByIdAsync(role.Id);
    Check("更新 → UpdateUserId 自动填当前用户(42)", updated!.UpdateUserId == 42);
}

Console.WriteLine($"\n结果:{pass} 通过 / {fail} 失败");
try { File.Delete(dbFile); } catch { }
Environment.Exit(fail == 0 ? 0 : 1);

public class StubCurrentUser(long id) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public long? UserId => id;
    public bool IsSuperAdmin => false;
}
