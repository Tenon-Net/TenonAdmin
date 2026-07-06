#:project ../src/TenonAdmin.Services/TenonAdmin.Services.csproj
#:property ManagePackageVersionsCentrally=false
#:package Microsoft.Extensions.DependencyInjection@10.0.0
#:property PublishAot=false
// T2 自检(设计 §4 组织模块验收):用户/机构/职位 CRUD + 挂机构/职位/多角色 + 软删 + 安全护栏。
// 直接跑 UserService/OrgService/PositionService(含密码重置、账号唯一、超管保护等非平凡逻辑)。
// 引 SqlSugar 的 file-based 脚本必须 #:property PublishAot=false。

using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-t2-{Guid.NewGuid():N}.db");
var services = new ServiceCollection();
services.AddSingleton(new AdminCacheOptions());
services.AddTenonAdminSqlSugar(new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = $"Data Source={dbFile}" },
    [typeof(ServicesSetup).Assembly]);
services.AddTenonAdminServices();
var sp = services.BuildServiceProvider();

var db = sp.GetRequiredService<ISqlSugarClient>();
db.CodeFirst.InitTables(typeof(SysUser), typeof(SysRole), typeof(SysMenu), typeof(SysUserRole),
    typeof(SysRoleMenu), typeof(SysOrg), typeof(SysPosition));

var pass = 0; var fail = 0;
void Check(string name, bool ok) { if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); } else { fail++; Console.WriteLine($"  FAIL  {name}"); } }
async Task<bool> Throws(Func<Task> act, ErrorCode expected)
{
    try { await act(); return false; }
    catch (AdminException ex) { return ex.Code == expected; }
}

using var scope = sp.CreateScope();
var s = scope.ServiceProvider;
var orgSvc = s.GetRequiredService<IOrgService>();
var posSvc = s.GetRequiredService<IPositionService>();
var userSvc = s.GetRequiredService<IUserService>();
var hasher = s.GetRequiredService<IPasswordHasher>();
var userRepo = s.GetRequiredService<IRepository<SysUser>>();

Console.WriteLine("T2 组织模块自检:");

// 预备一个角色(挂给用户)+ 一个超管(护栏用)
await s.GetRequiredService<IRepository<SysRole>>().InsertAsync(new() { Id = 501, Name = "角色A", Code = "roleA", Enabled = true });
await userRepo.InsertAsync(new() { Id = 1, Account = "super", Password = hasher.Hash("x"), Name = "超管", Enabled = true, IsSuperAdmin = true });

// 机构:建父 + 建子
var parentOrg = await orgSvc.AddAsync(new() { ParentId = 0, Name = "总公司", Code = "root", Sort = 1 });
var childOrg = await orgSvc.AddAsync(new() { ParentId = parentOrg, Name = "分公司", Code = "branch", Sort = 1 });
Check("机构新增返回非零 Id", parentOrg != 0 && childOrg != 0);
Check("机构列表含 2 条", (await orgSvc.ListAsync()).Count == 2);
Check("父机构有子 → 删除被拒(OrgHasChildren)", await Throws(() => orgSvc.DeleteAsync(parentOrg), ErrorCode.OrgHasChildren));

// 职位
var pos = await posSvc.AddAsync(new() { Name = "工程师", Code = "eng", Sort = 1 });
Check("职位新增返回非零 Id", pos != 0);
Check("职位分页含 1 条", (await posSvc.PageAsync(new() { Current = 1, Size = 10 })).Total == 1);

// 用户:挂机构/职位/角色
var uid = await userSvc.AddAsync(new() { Account = "u1", Password = "P@ss1", Name = "用户一", OrgId = childOrg, PositionId = pos, RoleIds = [501] });
Check("用户新增返回非零 Id", uid != 0);
Check("重复账号 → AccountExists", await Throws(() => userSvc.AddAsync(new() { Account = "u1" }), ErrorCode.AccountExists));

var detail = await userSvc.GetAsync(uid);
Check("详情挂上机构/职位/角色", detail.OrgId == childOrg && detail.PositionId == pos && detail.RoleIds.Contains(501));
Check("详情非超管", !detail.IsSuperAdmin);

var page = await userSvc.PageAsync(new() { Account = "u1", Current = 1, Size = 10 });
Check("按账号分页命中 1 条", page.Total == 1);
Check("按机构过滤命中", (await userSvc.PageAsync(new() { OrgId = childOrg, Current = 1, Size = 10 })).Total == 1);

// 密码重置:默认 + 指定,均以库中哈希校验(证明真的改了且可验证)
var def = await userSvc.ResetPasswordAsync(uid, null);
Check("重置默认密码 → 库中哈希可校验", hasher.Verify(def, (await userRepo.GetByIdAsync(uid))!.Password));
await userSvc.ResetPasswordAsync(uid, "New@123");
Check("重置指定密码 → 库中哈希可校验新值", hasher.Verify("New@123", (await userRepo.GetByIdAsync(uid))!.Password));

// 启停用
await userSvc.SetEnabledAsync(uid, false);
Check("停用生效", !(await userRepo.GetByIdAsync(uid))!.Enabled);

// 超管护栏
Check("超管不可删除(SuperAdminProtected)", await Throws(() => userSvc.DeleteAsync(1), ErrorCode.SuperAdminProtected));
Check("超管不可停用(SuperAdminProtected)", await Throws(() => userSvc.SetEnabledAsync(1, false), ErrorCode.SuperAdminProtected));

// 更新资料 + 清角色
await userSvc.UpdateAsync(uid, new() { Name = "用户一改", OrgId = childOrg, PositionId = pos, Enabled = true, RoleIds = [] });
var d2 = await userSvc.GetAsync(uid);
Check("更新姓名生效且角色清空", d2.Name == "用户一改" && d2.RoleIds.Count == 0);

// 软删:分页不再可见 + 详情抛 UserNotFound
await userSvc.DeleteAsync(uid);
Check("软删后分页不可见", (await userSvc.PageAsync(new() { Account = "u1", Current = 1, Size = 10 })).Total == 0);
Check("软删后详情抛 UserNotFound", await Throws(() => userSvc.GetAsync(uid), ErrorCode.UserNotFound));

Console.WriteLine($"\n结果:{pass} 通过 / {fail} 失败");
try { File.Delete(dbFile); } catch { }
Environment.Exit(fail == 0 ? 0 : 1);
