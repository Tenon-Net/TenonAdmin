# Scheduled Jobs

Use scheduled jobs for reports, periodic cleanup, and endpoint checks. The default scheduler runs inside the API process through `AddTenonAdmin()`. Implement and register `IAdminJob` to make a handler available in the management interface. This example only writes a log entry to verify scheduling; call your own business service to generate a real report.

```csharp
public class DailyReportJob : IAdminJob
{
    public async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        context.Log?.Invoke("Daily report generated");
    }
}
```

One line to register it, the same path the kernel's own handlers take:

```csharp
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, DailyReportJob>());
```

Register the handler in your own `Program.cs` and restart the backend. In job management, create a compiled job and select the handler. Run it manually once before configuring its schedule. The run history should show success and the sample log entry; if the handler is missing, check that registration ran.


In the job list, check whether a job is enabled, then inspect the execution statistics. Open its run history to diagnose failures and confirm manual runs. This Chinese-interface screenshot contains sample jobs; click to enlarge.

[![Scheduled jobs and execution statistics](/screenshots/scheduled-jobs.png)](/screenshots/scheduled-jobs.png)

## Three kinds of payload

Besides compiled handlers there are two more, both created by filling in a form:

| Payload | Where parameters come from | When to use it |
|---|---|---|
| Compiled | Property bag + whatever you inject | Touching the database, reusing business services |
| HTTP | Property bag's `url` / `method` / `headers` / `body` | Poking another service's endpoint, health sweeps |
| SQL | Property bag's `sql` | One-off data fixes, **off by default** |

The property bag is a string dictionary stored with the job and passed through `context.Properties`. Use it for task parameters such as report dates or target addresses. Obtain database access and other business services through dependency injection.

SQL jobs are gated by `TenonAdmin:Jobs:Sql:Enabled`, `false` by default. Turning it on admits one thing: jobs execute SQL with the configured database account, so restrict job editing according to that account’s privileges.

## Cron has six fields, seconds first

```
sec min hour day month dow
0   30  3    *   *     ?     daily at 03:30
*/5 *   *    *   *     ?     every 5 seconds
0   0   0    L   *     ?     midnight on the last day of the month
0   0   9    ?   *     5L    09:00 on the last Friday of the month
```

`* , - / ?` are all supported. The day field also takes `L` (end of month), `L-3` (three days before it), `15W` (nearest weekday to the 15th) and `LW` (last weekday of the month); the day-of-week field takes `5L` (last Friday) and `5#3` (third Friday). A five-field expression gets a `0` seconds field prepended.

**Day and day-of-week can't both be restricted.** Pin one and the other must be `?`, otherwise you get `47003`. That's Quartz semantics, and the reason is that "the 15th" and "every Monday" have never had an agreed-upon answer when both hold.

The frontend's CronEditor offers every / range / step / specific per field, with a live preview of the next five occurrences underneath. It calls `POST /api/v1/sys/job/preview-cron`, available to any signed-in user, so it needs no permission of its own.

## Execution frequency and log retention {#second-level-precision-has-a-price}

Interval jobs bottom out at 5 seconds; 4 is rejected with `47004`. A cron may put `*` in the seconds field — the preview warns but doesn't block, because a job firing every second writes 86,400 run records a day.

Retention lives in the config center under `sys.job.logRetentionDays`, 30 days by default, and the kernel's own cleanup job deletes expired rows in batches at 03:30. It is itself a scheduled job: you can see it in the UI, pause it, change its schedule.

## Three survival shapes, zero code changes

"Jobs must not stop when the backend stops" is really three separate requirements:

**A restart doesn't lose jobs.** Trigger configuration and the next run time live in the database, so a restarted process picks up where it left off. Occurrences missed during downtime follow the job's misfire strategy: `Skip` (the default) advances to the next future time without catching up, `FireOnceNow` catches up exactly once no matter how many were missed. If the business requires every missed day to be covered, check and backfill those dates in your business logic.

**One replica dies, another takes over.** Both API replicas run a scheduler and elect a leader through a lease on `sys_job_lock`; only the leader scans. When a leader goes silent the standby takes over within 40 seconds (30s lease + 10s heartbeat). This layer needs a server database — SQLite won't hold up under two writing processes.

**Jobs must continue while the API is down.** Run the scheduler in a separate Worker process connected to the same database as the API. See `backend/samples/WorkerHost`; its minimal startup is:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTenonAdminWorker(builder.Configuration);
await builder.Build().RunAsync();
```

A worker has three configuration rules: `TenonAdmin:Id:WorkerId` must differ from every other process (unset on the same machine, a file lock claims a free slot; across machines still set it explicitly), table creation and seeding must be off (the API owns the schema), and its timezone must match the API's.

## One occurrence, one run, cluster-wide

The lease only answers "who scans" — it's an efficiency measure. What actually prevents a duplicate run is the **claim**: an atomic compare-and-set on the job row's next run time before every fire.

```sql
UPDATE sys_job SET NextRunTime=@next
WHERE Id=@id AND NextRunTime=@expected AND Status=1
```

One affected row means go. An old leader that wakes from a twenty-second GC pause will scan and try to claim, but that slot has already been advanced by the new leader, `@expected` no longer matches, and it gets nothing. Under split brain, clock drift or a stalled process, a given occurrence is claimed at most once — mathematically, not probabilistically.

For the multi-replica shape a container smoke test signs this off: create a 5-second job, let it run a few rounds, assert the scheduled times are pairwise distinct, then kill the leading replica and assert the job kept running and leadership moved.

Claiming prevents multiple nodes from claiming the same scheduled occurrence. Retries, manual runs, and external request timeouts can still repeat business operations. Make side effects such as payments or messages idempotent using business keys.

## After a job fails

Failure handling is per job, four knobs:

| Knob | Meaning |
|---|---|
| Retry count / interval | Retries within one fire; every attempt shares a fire-instance id |
| Timeout | Cancels the run and records a timeout; neither timeouts nor cancellations retry |
| Alert threshold | This many consecutive failures raises an alert and moves the job to a panic state, stopping the schedule |
| Notice / email | The notice targets the job's creator and the super admin rather than broadcasting; an empty email list falls back to `sys.job.alertEmails` in the config center |

Recovering from panic requires re-enabling the job by hand, deliberately: a job that has failed ten times in a row will only drown the log by failing every five minutes. The alert fires once, on the crossing.

Use asynchronous I/O and observe `CancellationToken` in job handlers. Avoid blocking thread-pool threads with `Thread.Sleep`, `.Result`, or `.Wait()`. `MaxConcurrentRuns` limits concurrent runs but does not remove resource consumption caused by blocking.

## Three things to settle before deploying

**Every scheduling process shares one timezone.** The module works in server local time, containers default to UTC, and hosts usually aren't. The repository's `docker-compose.yml` sets `TZ`; don't forget it in your own image.

**HTTP job targets go through a fence.** Only the cloud metadata range is blocked by default (`169.254.0.0/16` and its IPv6 forms), private networks are not — reaching internal services is the point of an HTTP job. Tighten it with the `TenonAdmin:Jobs:Http:AllowedHosts` allowlist. The fence checks at save time and at execution time, and re-checks the resolved IP when the connection is made, which is what catches a name that resolves publicly when saved and privately when run.

**Demo mode blocks "run once".** A job can issue arbitrary HTTP requests, so letting a demo site trigger one is a hole. That's a feature; don't disable it for the sake of a demo.

The whole module can be dropped: add `"Job"` to `TenonAdmin:Api:DisabledModules` and the controller's routes aren't even registered. To keep a replica out of scheduling while it still serves job queries and edits, set `TenonAdmin:Jobs:SchedulerEnabled=false`.
