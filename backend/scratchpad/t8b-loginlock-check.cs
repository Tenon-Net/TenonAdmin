// T8b 自检:LoginLockService —— 阈值锁定 / 成功重置 / 关闭开关。用真的 MemoryCacheProvider,不 mock。
// 运行:dotnet run t8b-loginlock-check.cs
#:project ../src/TenonAdmin.Services/TenonAdmin.Services.csproj
#:property PublishAot=false

using Microsoft.Extensions.Caching.Memory;
using TenonAdmin.Core;
using TenonAdmin.Services;

int passed = 0, total = 0;
void Check(string name, bool ok)
{
    total++;
    if (ok) { passed++; Console.WriteLine($"  ✓ {name}"); }
    else Console.WriteLine($"  ✗ {name}  <<< 失败");
}
static bool Locked(Func<Task> a)
{
    try { a().GetAwaiter().GetResult(); return false; }
    catch (AdminException e) when (e.Code == ErrorCode.AccountLocked) { return true; }
}

static LoginLockService Make(int maxFail)
{
    var cache = new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()), new AdminCacheOptions());
    var security = new AdminSecurityOptions { LoginLock = new AdminLoginLockOptions { MaxFailCount = maxFail, LockMinutes = 10 } };
    return new LoginLockService(cache, security);
}

// 阈值 = 3
var svc = Make(3);
Check("初始未锁定", !Locked(() => svc.EnsureNotLockedAsync("bob")));

await svc.RecordFailureAsync("bob");
await svc.RecordFailureAsync("bob");
Check("2 次失败(<3)仍未锁定", !Locked(() => svc.EnsureNotLockedAsync("bob")));

await svc.RecordFailureAsync("bob");   // 第 3 次 → 达阈值
Check("3 次失败达阈值 → 锁定", Locked(() => svc.EnsureNotLockedAsync("bob")));

// 账号隔离:alice 不受 bob 影响
Check("其他账号不受牵连", !Locked(() => svc.EnsureNotLockedAsync("alice")));

// 成功重置解锁
await svc.ResetAsync("bob");
Check("重置后解锁", !Locked(() => svc.EnsureNotLockedAsync("bob")));

// 关闭开关(MaxFailCount<=0):怎么失败都不锁
var off = Make(0);
for (var i = 0; i < 20; i++) await off.RecordFailureAsync("carol");
Check("关闭时永不锁定", !Locked(() => off.EnsureNotLockedAsync("carol")));

Console.WriteLine($"\n结果:{passed}/{total} 通过");
if (passed != total) Environment.Exit(1);
