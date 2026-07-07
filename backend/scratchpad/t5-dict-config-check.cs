#:project ../src/TenonAdmin.Services/TenonAdmin.Services.csproj
#:property ManagePackageVersionsCentrally=false
#:package Microsoft.Extensions.DependencyInjection@10.0.0
#:property PublishAot=false
// T5 自检(设计 §4 字典/配置 + §2.2 事件总线):真跑读穿透缓存 + 变更即失效 + Channels 事件总线端到端投递。
// 三块:A 事件总线机制(投递/退订);B 字典缓存(证明缓存是真的会发陈旧值,且服务变更后失效走新值);
// C 配置缓存(同上);D 生产者接线(服务变更确实向总线发了事件,订阅者收到)。

using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-t5-{Guid.NewGuid():N}.db");
var services = new ServiceCollection();
services.AddSingleton(new AdminCacheOptions());
services.AddTenonAdminSqlSugar(new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = $"Data Source={dbFile}" },
    [typeof(ServicesSetup).Assembly]);
services.AddTenonAdminServices();
var sp = services.BuildServiceProvider();

var db = sp.GetRequiredService<ISqlSugarClient>();
db.CodeFirst.InitTables(typeof(SysDictType), typeof(SysDictItem), typeof(SysConfig));

var pass = 0; var fail = 0;
void Check(string name, bool ok) { if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); } else { fail++; Console.WriteLine($"  FAIL  {name}"); } }

// 轮询等待异步条件成立(事件总线后台派发,非同步)——最多等 timeoutMs。
async Task<bool> WaitUntil(Func<bool> cond, int timeoutMs = 2000)
{
    var elapsed = 0;
    while (elapsed < timeoutMs) { if (cond()) return true; await Task.Delay(25); elapsed += 25; }
    return cond();
}

var bus = sp.GetRequiredService<IEventBus>();

// 常驻订阅者:统计服务变更时收到的事件(证明服务→总线→订阅者端到端接线)。
var dictEvents = new List<string>();
var configEvents = new List<string>();
using var dSub = bus.Subscribe<DictChangedEvent>((e, _) => { lock (dictEvents) dictEvents.Add(e.TypeCode); return Task.CompletedTask; });
using var cSub = bus.Subscribe<ConfigChangedEvent>((e, _) => { lock (configEvents) configEvents.Add(e.Key); return Task.CompletedTask; });

// ── A 事件总线机制:投递 + 退订 ────────────────────────────────────────────
Console.WriteLine("T5 —— A 事件总线机制:");
var received = 0;
var throwaway = bus.Subscribe<DictChangedEvent>((_, _) => { Interlocked.Increment(ref received); return Task.CompletedTask; });
await bus.PublishAsync(new DictChangedEvent("__probe__"));
Check("发布事件 → 订阅者后台收到", await WaitUntil(() => received == 1));
throwaway.Dispose();                                   // 退订
var beforeUnsub = received;
await bus.PublishAsync(new DictChangedEvent("__probe2__"));
await Task.Delay(200);                                  // 给足派发时间,证明确实不再收到
Check("退订后 → 不再收到(计数不变)", received == beforeUnsub);

int DictCount() { lock (dictEvents) return dictEvents.Count; }
int ConfigCount() { lock (configEvents) return configEvents.Count; }

// ── B 字典读穿透缓存 + 变更失效 ────────────────────────────────────────────
Console.WriteLine("T5 —— B 字典缓存(读穿透 + 变更即失效):");
var dict = sp.GetRequiredService<IDictService>();
var itemRepo = sp.GetRequiredService<IRepository<SysDictItem>>();

await dict.AddTypeAsync(new DictTypeInput { Code = "color", Name = "颜色", Sort = 1 });
var itemId = await dict.AddItemAsync(new DictItemInput { DictTypeCode = "color", Label = "红", Value = "1", Sort = 1 });

var list1 = await dict.GetItemsByTypeAsync("color");   // 首次:查库并回填缓存
Check("首查 → 1 项、值=1", list1.Count == 1 && list1[0].Value == "1");

// 绕过服务直接改库(不失效缓存)——再查应拿到陈旧缓存值,证明缓存确实生效
var raw = await itemRepo.GetByIdAsync(itemId);
raw!.Value = "2";
await itemRepo.UpdateAsync(raw);
var list2 = await dict.GetItemsByTypeAsync("color");
Check("直改库后再查 → 仍读到缓存陈旧值 1(证明缓存是真的)", list2[0].Value == "1");

// 经服务更新(会失效缓存)——再查必须走新值(验收:改字典后再查走新值)
var dictBefore = DictCount();
await dict.UpdateItemAsync(itemId, new DictItemInput { DictTypeCode = "color", Label = "红", Value = "3", Sort = 1 });
var list3 = await dict.GetItemsByTypeAsync("color");
Check("经服务改后再查 → 缓存失效走新值 3(★验收)", list3[0].Value == "3");
Check("服务改字典 → 向总线发了 DictChangedEvent", await WaitUntil(() => DictCount() > dictBefore));

// 新增项也失效
await dict.AddItemAsync(new DictItemInput { DictTypeCode = "color", Label = "蓝", Value = "9", Sort = 2 });
var list4 = await dict.GetItemsByTypeAsync("color");
Check("新增项后再查 → 2 项(缓存已失效)", list4.Count == 2);

// 删除项也失效
await dict.DeleteItemAsync(itemId);
var list5 = await dict.GetItemsByTypeAsync("color");
Check("删除项后再查 → 1 项(缓存已失效)", list5.Count == 1);

// 删除类型级联软删项
await dict.DeleteTypeAsync((await dict.PageTypesAsync(new DictTypePageInput { Code = "color" })).Items[0].Id);
var list6 = await dict.GetItemsByTypeAsync("color");
Check("删除类型 → 级联软删其项,再查 0 项", list6.Count == 0);

// 唯一码守护
try { await dict.AddTypeAsync(new DictTypeInput { Code = "dup", Name = "d1" }); await dict.AddTypeAsync(new DictTypeInput { Code = "dup", Name = "d2" }); Check("重复类型码被拒(DictTypeCodeExists)", false); }
catch (AdminException ex) { Check("重复类型码被拒(DictTypeCodeExists)", ex.Code == ErrorCode.DictTypeCodeExists); }

// ── C 配置读穿透缓存 + 变更失效 ────────────────────────────────────────────
Console.WriteLine("T5 —— C 配置缓存(读穿透 + 变更即失效):");
var config = sp.GetRequiredService<IConfigService>();
var cfgRepo = sp.GetRequiredService<IRepository<SysConfig>>();

var cfgId = await config.AddAsync(new ConfigInput { ConfigKey = "site.name", ConfigValue = "A", Name = "站名", Sort = 1 });
Check("首取配置值 → A", await config.GetValueByKeyAsync("site.name") == "A");   // 回填缓存

var rawCfg = await cfgRepo.GetByIdAsync(cfgId);
rawCfg!.ConfigValue = "B";
await cfgRepo.UpdateAsync(rawCfg);
Check("直改库后再取 → 仍读缓存陈旧值 A(证明缓存生效)", await config.GetValueByKeyAsync("site.name") == "A");

var cfgBefore = ConfigCount();
await config.UpdateAsync(cfgId, new ConfigInput { ConfigKey = "site.name", ConfigValue = "C", Name = "站名", Sort = 1 });
Check("经服务改后再取 → 缓存失效走新值 C(★验收)", await config.GetValueByKeyAsync("site.name") == "C");
Check("服务改配置 → 向总线发了 ConfigChangedEvent", await WaitUntil(() => ConfigCount() > cfgBefore));

Check("取不存在的键 → null", await config.GetValueByKeyAsync("no.such.key") is null);

try { await config.AddAsync(new ConfigInput { ConfigKey = "dupk", ConfigValue = "x", Name = "n1" }); await config.AddAsync(new ConfigInput { ConfigKey = "dupk", ConfigValue = "y", Name = "n2" }); Check("重复配置键被拒(ConfigKeyExists)", false); }
catch (AdminException ex) { Check("重复配置键被拒(ConfigKeyExists)", ex.Code == ErrorCode.ConfigKeyExists); }

Console.WriteLine($"\n结果:{pass} 通过 / {fail} 失败");
await (sp as IAsyncDisposable)!.DisposeAsync();   // 触发 ChannelEventBus 优雅停派发循环
try { File.Delete(dbFile); } catch { }
Environment.Exit(fail == 0 ? 0 : 1);
