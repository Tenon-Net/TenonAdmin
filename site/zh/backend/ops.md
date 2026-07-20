# 运维端点

系统跑起来之后，管理员总要回答三个问题：这台机器还好吗、刚才那个 500 是什么、缓存是不是该清了。内核给这三个问题各配了一个小端点。它们的共同点是**只读诊断或定向动作，不是业务功能**。所以都能整体经 `Api.DisabledModules` 关掉，不影响其余模块。

## 服务器监控

`GET /api/v1/sys/monitor/server` 返回一次进程与主机的运行快照：CPU 占用、内存、磁盘、运行时信息。全部读自 BCL（`Process`/`GC`/`DriveInfo`/`RuntimeInformation`），零依赖，也不落库。这是一个**快照**，不是一条时间序列，历史趋势不在内核范围内。

```csharp
public class MonitorService(TimeProvider time, ILogger<MonitorService> logger) : IMonitorService
{
    protected virtual TimeSpan CpuSampleWindow => TimeSpan.FromMilliseconds(500);

    public virtual async Task<ServerInfoOutput> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        // ...MachineName / OsDescription / ProcessorCount 直接读
        ProcessCpuPercent = await SampleCpuPercentAsync(proc, cancellationToken),
        // ...
    }
}
```

CPU 占用不是系统 API 直接给的数字，是**采样算出来的**。先取一次 `Process.TotalProcessorTime`，等 500 毫秒，再取一次。用两次的差值除以经过的墙钟时间和核数，换算成 0–100 的百分比。那 500 毫秒的窗口就是 `CpuSampleWindow`。窗口越长，数字越平滑，但每次请求也拖得越久。500 毫秒是页面点一下刷新能接受的折中。

前端页面只有一个手动刷新按钮，**不做轮询**，页面在 `views/system/monitor`。原因就在上面那次采样。轮询等于让后端持续每 500 毫秒抽一次 CPU。用来监控 CPU 占用的手段，自己先占了一份 CPU，得不偿失。真要连续监控，接一套外部可观测栈（Prometheus/Grafana 之类）才是对的工具。这个端点只负责「管理员点一下，看一眼当前状态」。

数字异常时，这个端点不会替你报警，它没有阈值判断——看到 CPU 或内存偏高，下一步是去 `docker compose logs app`（或部署环境对应的日志出口）里找是哪个请求在拖，这个端点只负责给你第一眼判断值不值得往下查。

## 异常日志

`sys_exception_log` 是第三种日志表，和操作日志、登录日志共用同一个控制器 `SysLogController`（`GET /exception/page` + `DELETE /exception`）。但它的写入方式不一样。前两种由业务代码主动记，这一种由全局异常过滤器 `ExceptionLogFilter` 自动接住**未捕获异常**再写。

```csharp
internal sealed class ExceptionLogFilter(ILogService logService) : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is AdminException) return;   // 业务异常不进异常表

        await logService.RecordExceptionAsync(new ExceptionLogEntry { /* 方法/路径/追踪号/类型/消息/堆栈 */ });
        // 刻意不设 ExceptionHandled —— 异常继续冒泡走框架 500,响应与堆栈行为不变
    }
}
```

两个刻意的边界，读代码时容易忽略：

- **`AdminException` 被显式跳过**。业务异常是可预期的分支，信封里已经带了 `ErrorCode`，不是程序缺陷。让它混进异常表，只会把真正的崩溃淹没在噪音里。
- **过滤器不吞异常**。它不设置 `ExceptionHandled`，请求该 500 还是 500，堆栈该往上抛还是往上抛。这一层只是「旁路留一条痕迹」，不改变原有的异常处理流程。写日志本身也是尽力而为。`RecordExceptionAsync` 会把自己内部的异常吞掉。处理一次崩溃的路上，落日志再失败，也不能反过来把原始异常盖掉。

落库的字段做了长度截断，消息 2000 字符，堆栈 8000 字符。而且**只记异常本身，绝不记请求/响应体**。响应体里可能有明文口令或令牌，这条线内核在别处也反复守，登录日志同理。清空操作是硬删，不可恢复。动作本身还会被记进操作日志。谁在什么时候清了异常表，这件事也留痕。

排查一条异常记录时，先看它的追踪号（`TraceId`，即 `HttpContext.TraceIdentifier`）。这个号和同一请求的应用日志共用一份，拿它去日志里搜，能把「这次崩溃发生前后端到底做了什么」串成一条完整链路，而不是只盯着这一条记录里的堆栈猜。

## 缓存管理

`CacheController` 的四个端点都是「清」，不是「看」：清全部用户的权限/数据范围缓存、清字典缓存、清配置缓存、把门户菜单代际加一（旧缓存不再被读到，靠 TTL 自然回收）。没有键浏览端点，也没有取值端点。这是设计上刻意留白，不是漏做：

- 默认的 `MemoryCacheProvider` 包着 `IMemoryCache`，它本来就没有受支持的键枚举方式。所以键浏览在零配置部署上永远是空的。
- 缓存的键和值都碰不得。键里嵌着手机号、IP 这类 PII，值里可能是明文验证码或一次性令牌。列出键已经算泄露，读值等于给管理员开了一个绕过正常流程看 OTP 的后门。

```csharp
public virtual Task<long> RebuildPortalAsync(CancellationToken cancellationToken = default) =>
    // 自增代际,旧 portal:* 键不再被读到、由 TTL 回收,和 RbacService 授权变更时的机制一致
    cache.IncrementAsync(CacheKeys.PortalGeneration, cancellationToken: cancellationToken);
```

这四个动作平时都用不上。正常的授权变更、字典/配置修改，内核自己就会同步失效对应的缓存。这个页面是留给**旁路场景**的逃生舱。有人直接改了库、跳过了 API，缓存和数据库对不上了，这时候才需要管理员手动点一下清掉。前端每个按钮都要二次确认，点完 toast 弹出被清理的条数。这就是照着「低频、有意识地执行」设计的，不是日常操作面板。

真遇到「数据库改了、页面却没变」的报障，多半就是这页开头说的那种旁路场景在发生：正常走 API 的写操作，内核自己已经把对应缓存失效了，用不着这四个按钮。按数据类型点对应的那个清理按钮，现象通常当场消失；清完还在，说明问题不在缓存，得回到数据库或应用日志继续查。

::: tip 内核里没有字段级变更日志（DiffLog）
操作日志已经记了每个写请求的完整入参 JSON、操作人、时间、结果码。审计字段盖住每一行，就是 `CreateUserId`/`UpdateUserId`/`UpdateTime` 这几个。软删的行也还留着可查。字段级「改之前是什么、改之后是什么」这个能力，内核评估过后没做进核心。原因出在写入路径上：`IRepository<>` 没有单一的写入咽喉，Insert/Update/Delete 分头直调 SqlSugar，要补钩子就得在六个方法上都补。而且这类前像审计天然是「按需」的。消费方需要时，用 SqlSugar 原生的 `Aop.OnDiffLogEvent` 自己接就行，不必内核代劳。
:::
