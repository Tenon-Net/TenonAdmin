# Ops Endpoints

Once the system's running, an admin keeps needing to answer three questions: is this machine still healthy, what was that 500 a minute ago, and does the cache need clearing. The kernel gives each question its own small endpoint. What they share is that they're **read-only diagnostics or a single targeted action, not business features** — so all three can be switched off as a group via `Api.DisabledModules` without touching anything else.

## Server monitor

`GET /api/v1/sys/monitor/server` returns a one-shot snapshot of the process and host: CPU, memory, disk, runtime info. All of it comes straight from the BCL (`Process`/`GC`/`DriveInfo`/`RuntimeInformation`) — zero dependencies, nothing written to the database. It's a **snapshot**, not a time series; historical trends are out of scope for the kernel.

```csharp
public class MonitorService(TimeProvider time, ILogger<MonitorService> logger) : IMonitorService
{
    protected virtual TimeSpan CpuSampleWindow => TimeSpan.FromMilliseconds(500);

    public virtual async Task<ServerInfoOutput> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        // ...MachineName / OsDescription / ProcessorCount read directly
        ProcessCpuPercent = await SampleCpuPercentAsync(proc, cancellationToken),
        // ...
    }
}
```

CPU usage isn't a number the OS hands over directly — it's **computed from a sample**. Take `Process.TotalProcessorTime` once, wait 500 ms, take it again, then divide the delta by elapsed wall-clock time and core count to get a 0–100 percentage. That 500 ms window is `CpuSampleWindow`: a longer window smooths the number but drags out every request, and 500 ms is the tradeoff a manual-refresh click can tolerate.

The frontend page (`views/system/monitor`) has a single manual-refresh button and **doesn't poll**, for exactly the reason above: polling would mean the backend samples CPU every 500 ms continuously, and a tool for watching CPU usage that eats its own slice of CPU isn't worth it. For actual continuous monitoring, wire up an external observability stack — Prometheus/Grafana or similar — that's the right tool for that job. This endpoint's only job is "admin clicks once, sees the current state."

When the numbers look off, this endpoint won't page anyone — there's no threshold logic behind it. If CPU or memory looks high, the next step is `docker compose logs app` (or whatever your deployment's log sink is) to find which request is the culprit. This endpoint only gives you the first glance that decides whether it's worth digging further.

## Exception log

`sys_exception_log` is the third log table, sharing a controller (`SysLogController`, `GET /exception/page` + `DELETE /exception`) with the operation log and login log — but it's written differently. The first two are recorded deliberately by business code; this one is caught automatically by the global exception filter `ExceptionLogFilter` when an **unhandled exception** bubbles up.

```csharp
internal sealed class ExceptionLogFilter(ILogService logService) : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is AdminException) return;   // business exceptions never enter this table

        await logService.RecordExceptionAsync(new ExceptionLogEntry { /* method/path/trace id/type/message/stack */ });
        // deliberately doesn't set ExceptionHandled — the exception still bubbles to the framework's 500, response and stack behavior unchanged
    }
}
```

Two deliberate boundaries that are easy to miss reading the code:

- **`AdminException` is explicitly skipped.** A business exception is an expected branch — its envelope already carries an `ErrorCode`; it's not a defect. Letting it into the exception table would just drown real crashes in noise.
- **The filter never swallows the exception.** It doesn't set `ExceptionHandled` — the request still 500s, the stack still bubbles the same way it always did. This layer only leaves a trace on the side; it doesn't change the existing exception-handling flow. Writing the log is itself best-effort: `RecordExceptionAsync` swallows its own internal failures, so a logging failure on the way to handling a crash can never mask the original exception.

Persisted fields are length-truncated — 2000 characters for the message, 8000 for the stack trace — and **only the exception itself is recorded, never the request or response body** (a response body can carry a plaintext password or token, a line the kernel guards just as hard elsewhere, same as the login log). Clearing is a hard delete, unrecoverable, and the action itself gets written to the operation log — who cleared the exception table and when leaves its own trace.

When triaging one exception record, start with its trace ID (`TraceId`, i.e. `HttpContext.TraceIdentifier`). It's shared with that same request's application logs, so searching it there strings together everything the front and back end actually did around the crash, instead of guessing from the stack trace alone.

## Cache management

All four `CacheController` endpoints **clear**, they don't **inspect**: flush every user's permission/data-scope cache, flush the dict cache, flush the config cache, and bump the portal-menu generation (old cache entries stop being read and get reclaimed by TTL). There's no key-browsing endpoint and no value-reading endpoint — that's a deliberate gap, not something left unfinished:

- The default `MemoryCacheProvider` wraps `IMemoryCache`, which has no supported way to enumerate keys in the first place — key browsing would always be empty on a zero-config deployment anyway.
- Cache keys and values are both off-limits to touch. Keys embed PII like phone numbers and IPs; values can be plaintext verification codes or one-time tokens. Listing keys is already a leak, and reading values would hand an admin a backdoor around the normal flow for viewing an OTP.

```csharp
public virtual Task<long> RebuildPortalAsync(CancellationToken cancellationToken = default) =>
    // bump the generation counter — old portal:* keys stop being read and get reclaimed by TTL, same mechanism RbacService uses on authorization changes
    cache.IncrementAsync(CacheKeys.PortalGeneration, cancellationToken: cancellationToken);
```

None of these four actions come up in normal operation — a real authorization change or dict/config edit already invalidates the matching cache on its own. This page is an escape hatch for **out-of-band** scenarios: someone edited the database directly, bypassing the API, and now the cache and the database disagree — that's when an admin needs to clear it by hand. Every button on the frontend requires a second confirmation, and a toast reports how many entries got cleared afterward. It's built for "infrequent, deliberate action," not a daily-driver control panel.

If you actually get a "the database changed but the page didn't" report, it's most likely the out-of-band scenario described above: a write that went through the API already invalidated the matching cache on its own, so these four buttons aren't the fix for that. Click the clear button matching the data type in question — the symptom usually disappears on the spot. If it's still there afterward, the problem isn't the cache; go back to the database or the application logs.

::: tip The kernel has no field-level change log (a DiffLog)
The operation log already records every write request's full input JSON, who did it, when, and the result code. The audit columns cover every row — `CreateUserId`/`UpdateUserId`/`UpdateTime`. Soft-deleted rows are still there to query. Field-level "what was it before, what is it after" was evaluated and deliberately left out of the core. The reason is the write path: `IRepository<>` has no single choke point for writes — Insert/Update/Delete each call SqlSugar directly, so adding a hook would mean adding it to all six methods. And this kind of before-image auditing is inherently a need-it-or-you-don't feature — a consumer who wants it can wire up SqlSugar's own `Aop.OnDiffLogEvent` directly; the kernel doesn't need to do it for them.
:::
