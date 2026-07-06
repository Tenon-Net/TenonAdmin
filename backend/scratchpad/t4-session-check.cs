#:project ../src/TenonAdmin.Services/TenonAdmin.Services.csproj
#:property ManagePackageVersionsCentrally=false
#:package Microsoft.Extensions.DependencyInjection@10.0.0
#:property PublishAot=false
// T4 自检(设计 §15 会话/令牌验收):刷新轮换 + 旧 refresh 失效 + 强退即失活 + 重放触发整会话吊销 +
// 单端/限并发。直接跑 SessionService(令牌只需唯一 refresh 串,故用桩 ITokenProvider,不牵扯 JWT)。

using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-t4-{Guid.NewGuid():N}.db");
var secOptions = new AdminSecurityOptions();   // 持有引用以便中途切换单端/限并发模式
var services = new ServiceCollection();
services.AddSingleton(new AdminCacheOptions());
services.AddSingleton(secOptions);
services.AddSingleton(TimeProvider.System);
services.AddSingleton<ITokenProvider, StubTokenProvider>();
services.AddTenonAdminSqlSugar(new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = $"Data Source={dbFile}" },
    [typeof(ServicesSetup).Assembly]);
services.AddTenonAdminServices();
var sp = services.BuildServiceProvider();

sp.GetRequiredService<ISqlSugarClient>().CodeFirst.InitTables(typeof(SysUser), typeof(SysSession), typeof(SysRefreshToken));

var pass = 0; var fail = 0;
void Check(string name, bool ok) { if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); } else { fail++; Console.WriteLine($"  FAIL  {name}"); } }
async Task<bool> Throws(Func<Task> act, ErrorCode expected)
{
    try { await act(); return false; }
    catch (AdminException ex) { return ex.Code == expected; }
}

// 预备用户
using (var scope = sp.CreateScope())
    await scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>()
        .InsertAsync(new() { Id = 1, Account = "u1", Password = "x", Name = "U", Enabled = true });
var user = new SysUser { Id = 1, Account = "u1", Name = "U", Enabled = true };

async Task<(string sid, TokenPair pair)> Open()
{
    using var scope = sp.CreateScope();
    var s = scope.ServiceProvider;
    var sid = Guid.CreateVersion7().ToString("N");
    var pair = s.GetRequiredService<ITokenProvider>().Create(new TokenSubject(user.Id, user.Account, sid, false));
    await s.GetRequiredService<ISessionService>().OpenAsync(user, sid, pair);
    return (sid, pair);
}
async Task<bool> Active(string sid) { using var sc = sp.CreateScope(); return await sc.ServiceProvider.GetRequiredService<ISessionService>().IsActiveAsync(sid); }
async Task<RefreshedSession> Refresh(string rt) { using var sc = sp.CreateScope(); return await sc.ServiceProvider.GetRequiredService<ISessionService>().RefreshAsync(rt); }
async Task Revoke(string sid) { using var sc = sp.CreateScope(); await sc.ServiceProvider.GetRequiredService<ISessionService>().RevokeAsync(sid); }
Task<bool> RefreshThrows(string rt) => Throws(() => Refresh(rt), ErrorCode.RefreshTokenInvalid);

Console.WriteLine("T4 会话/令牌自检:");

// 开会话 → 活跃
var (sid1, pair1) = await Open();
Check("登录开会话 → 活跃", await Active(sid1));

// 刷新:换发新对,旧串再用即失败
var refreshed = await Refresh(pair1.RefreshToken);
Check("刷新换发新令牌对(新 refresh ≠ 旧)", refreshed.Pair.RefreshToken != pair1.RefreshToken);
Check("新 refresh 可继续换发", (await Refresh(refreshed.Pair.RefreshToken)) is not null);

// 重放旧 refresh(pair1,已轮换)→ 触发整会话吊销
Check("重放已轮换的旧 refresh → 40007", await RefreshThrows(pair1.RefreshToken));
Check("重放后整会话被吊销(失活)", !await Active(sid1));
Check("会话已吊销 → 连最新 refresh 也失效", await RefreshThrows(refreshed.Pair.RefreshToken));

// 强退:另开会话,强退后立即失活,原 refresh 失效
var (sid2, pair2) = await Open();
Check("新会话活跃", await Active(sid2));
await Revoke(sid2);
Check("强退后会话立即失活(原 token 下次请求即 401)", !await Active(sid2));
Check("强退后原 refresh 失效", await RefreshThrows(pair2.RefreshToken));

// 无效/乱填 refresh
Check("乱填 refresh → 40007", await RefreshThrows("not-a-real-token"));

// 单端模式:新登录踢掉旧会话
secOptions.Session.Mode = SessionMode.Single;
var (sidA, _) = await Open();
var (sidB, _) = await Open();
Check("单端:新登录后旧会话失活", !await Active(sidA));
Check("单端:新会话活跃", await Active(sidB));

// 限并发模式:最多 2 个,开第 3 个踢掉最旧
secOptions.Session.Mode = SessionMode.Multi;
secOptions.Session.MaxConcurrent = 2;
await Revoke(sidB);   // 清场,保证下面从 0 起
var (sidC, _) = await Open();
var (sidD, _) = await Open();
var (sidE, _) = await Open();
Check("限并发2:最旧(C)被踢", !await Active(sidC));
Check("限并发2:D、E 仍活跃", await Active(sidD) && await Active(sidE));

Console.WriteLine($"\n结果:{pass} 通过 / {fail} 失败");
try { File.Delete(dbFile); } catch { }
Environment.Exit(fail == 0 ? 0 : 1);

// 桩令牌提供者:只需产出唯一 refresh 串 + 过期时刻;不牵扯 JWT(会话逻辑与令牌格式无关)
public class StubTokenProvider(TimeProvider time) : ITokenProvider
{
    public TokenPair Create(TokenSubject subject)
    {
        var now = time.GetUtcNow();
        return new TokenPair("access-" + subject.SessionId, now.AddHours(2), Guid.NewGuid().ToString("N"), now.AddDays(7));
    }
}
