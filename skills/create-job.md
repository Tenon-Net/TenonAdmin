# 给自己的模块加定时任务 (Create Job)

消费者要「每天三点跑一次对账」时的完整路径:写一个 `IAdminJob`、注册一行、在后台建任务。**不需要写调度代码、不需要建表、不需要加端点**——内核的定时任务模块已经把这些做完了(施工台账 `docs/scheduling-ledger.md`,面向读者的文档 `site/zh/guide/scheduled-jobs.md`)。

## 一、写处理器

```csharp
using TenonAdmin.Core;

public class ReconcileJob(IRepository<BizOrder> orders, ILogger<ReconcileJob> logger) : IAdminJob
{
    // Name 默认 = 类型全名,数据库里的 HandlerName 按它匹配。除非要改名,别覆写。
    public async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        // 参数从属性包来:界面上填的键值对,这里按键取
        var days = int.TryParse(context.Properties.GetValueOrDefault("days"), out var d) ? d : 1;

        var since = context.FireTime.AddDays(-days);
        var count = await orders.AsQueryable().Where(o => o.CreateTime >= since).CountAsync();

        // 写进本次执行记录,前端「执行记录」页看得到
        context.Log?.Invoke($"对账完成:{count} 单");
    }
}
```

四条硬规矩:

1. **必须真异步。** `Thread.Sleep` / `.Result` / `.Wait()` 会占死线程池线程,八个这样的任务同时在飞就能让进程失去响应。
2. **取消令牌要往下传。** 超时、手动终止、宿主停机都经它;不传,任务的「超时」旋钮对你这个处理器就是摆设(执行器会在 await 返回后复查取消状态,把它记成超时而不是成功,但那时活已经干完了)。
3. **Scoped 生命周期**,可以注入 `IRepository<T>`、你自己的业务服务。执行器每次触发开新 scope。
4. **异常直接抛。** 执行器负责记 Failed、按任务配置重试、连败到阈值转 Panic 并告警。自己 try/catch 吞掉 = 任务永远「成功」。

## 二、注册一行

```csharp
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, ReconcileJob>());
```

`TryAddEnumerable` 按实现类型防重,与内核内置的三个处理器同一条路。注册完 `GET /api/v1/sys/job/handlers` 就返回它,前端下拉里能选到。

## 三、在后台建任务

定时任务页新建:载荷类型选「编译类」→ 处理器下拉选你的类 → 属性包填 `days=7` → 触发选 Cron 并用 CronEditor 拼表达式。存完即生效(集群下最长 30 秒)。

要预置一条任务(装完就有,不用人工建),写种子:

```csharp
internal sealed class ReconcileJobSeed : ISeedData<SysJob>
{
    // job 行含运行态(下次执行时刻、计数器)与用户调过的参,升级刷回种子值会清空它们
    public bool SyncOnUpgrade => false;

    public IEnumerable<SysJob> HasData() =>
    [
        new SysJob
        {
            Id = TenonSeedIds.ConsumerMin,             // 消费者段 ≥ 1000
            Code = "biz-reconcile",
            Name = "每日对账",
            HandlerKind = JobHandlerKind.Compiled,
            HandlerName = typeof(ReconcileJob).FullName!,
            TriggerKind = JobTriggerKind.Cron,
            CronExpression = "0 0 3 * * ?",
            Status = JobStatus.Ready,
            // NextRunTime 留空:调度器重载时按触发配置补算(种子编写期没有时钟)
        },
    ];
}
```

别名地雷:`IsSystem = true` 会让这条任务在界面上禁删(47014)。给内核自己的任务用,消费者的任务一般不要。

## 四、不写代码的两种任务

界面上直接建,不用发版:

- **HTTP 任务**:属性包填 `url`(必)、`method`、`headers`(JSON 对象串)、`body`、`successStatuses`。目标地址过 SSRF 围栏——默认只封云元数据段,内网放行;要收紧配 `TenonAdmin:Jobs:Http:AllowedHosts`。
- **SQL 任务**:属性包填 `sql`。总闸 `TenonAdmin:Jobs:Sql:Enabled` 默认关,**打开等于承认「能编辑任务的人 = DBA」**。

## 五、验证

```bash
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Job"
dotnet run --project backend/samples/MinimalHost     # 建任务 → 执行一次 → 看执行记录
```

写单元测试直接调 `ExecuteAsync`,不必启调度器:处理器是个普通 Scoped 服务,`JobExecutionContext` 是个可 `new` 的记录快照。要测调度行为(推格触发、misfire、重试)照抄 `backend/tests/TenonAdmin.Tests/JobEngineHost.cs` 的裸容器 + 可拨时钟成法。

## 六、常见的坑

| 现象 | 原因 |
|---|---|
| 建任务时处理器下拉里没有你的类 | 忘了 `TryAddEnumerable` 注册,或消费者程序集没进 `options.ApplicationAssemblies` |
| 任务一直「跳过」,一次都没执行 | 该任务有未闭合的执行记录(上次崩在中途)。主节点每拍会回收失联节点的孤儿行;本机崩溃后重启即恢复 |
| 改了 cron 但没立刻生效 | 集群下别的副本是主节点,最长 `ReloadSeconds`(默认 30 秒)后重载 |
| 超时设了却不生效 | 处理器没传取消令牌给下游(见第一节规矩 2) |
| 多副本下任务跑了两次 | 不该发生——领取 CAS 保证同一时刻至多一发。真遇到先查两副本时钟与时区是否一致 |
