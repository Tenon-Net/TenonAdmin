# 定时任务模块审查记录 · 2026-07-29

审查范围:`backend/src/TenonAdmin.Core/Scheduling/`、`backend/src/TenonAdmin.Services/Jobs/`、任务实体、任务 API、独立 Worker 装配及对应测试。

审查结论:**REQUEST CHANGES**。调度器的领取 CAS、选主租约、HTTP 任务 SSRF 围栏、取消轮询和执行日志闭合等主干措施已经具备；但以下三个 P1 问题会在进程重启、并发手动执行或独立 Worker 部署时破坏任务的关键语义，建议修复后再合并相关改动。

严重度:`P0` 阻断发版 / `P1` 应在合并前修复 / `P2` 尽快修复并补回归。

---

## 发现

### F1 · P1 · 同名节点重启后，遗留运行记录不能被回收

**位置**

- `backend/src/TenonAdmin.Services/Jobs/JobSchedulerService.cs:142-160`，`ReapOrphanRunsAsync`
- `backend/src/TenonAdmin.Services/Jobs/JobSchedulerService.cs:202-225`，`UpsertNodeAsync`
- `backend/src/TenonAdmin.Services/Jobs/JobSchedulerService.cs:308-310`，`SerialSkip` 判定

**问题**

孤儿回收仅以 `NodeName` 是否存在于新鲜心跳节点集合中判活。进程以相同 `NodeName` 重启时，新进程会先把原有 `sys_job_node` 行的 `LastHeartbeat` 更新为当前时间，随后才执行孤儿回收；崩溃前运行日志中的同一 `NodeName` 因而被误认为仍存活。

旧进程已经 `kill -9` 或非正常退出时，遗留的 `SysJobLog.EndTime IS NULL` 无法被回收。对于 `SerialSkip` 任务，后续每次触发都会看到这条未闭合日志并被跳过。日志清理任务也明确保留未闭合记录，因此该任务可能永久停摆，且没有 API 级恢复路径。

**复现条件**

1. 以默认节点名或固定 `Jobs:NodeName` 启动一个 `SerialSkip` 长任务。
2. 在运行中强制杀掉进程。
3. 以相同节点名重启调度进程。
4. 等待超过孤儿判死窗口后观察：旧日志仍为 `Running`，后续触发只留下 `Skipped` 或完全无法进入执行。

**建议处置**

将“节点名”与“进程实例”分开建模：每次启动生成不可复用的实例标识，并把该标识快照写入执行日志；回收时按实例标识和心跳判活，而不能仅比较 `NodeName`。只更新 `SysJobNode.StartTime` 不足以修复，因为当前日志没有保存可与之比较的实例快照。

**缺失测试**

现有 `JobSchedulerTests.Orphan_running_rows_from_dead_nodes_are_reaped` 只覆盖节点从未存在的情况；`JobElectionTests.Restarted_leader_with_same_node_name_reclaims_without_waiting_lease` 只覆盖锁租约接管。应新增“同名新实例启动后回收旧实例运行行，并恢复 SerialSkip 调度”的回归测试。

### F2 · P1 · 并发手动执行可绕过 SerialSkip 和全局并发上限

**位置**

- `backend/src/TenonAdmin.Services/Jobs/JobService.cs:175-184`，`RunOnceAsync`
- `backend/src/TenonAdmin.Services/Jobs/JobExecutor.cs:55-76`，`FireAndTrack`

**问题**

`RunOnceAsync` 先查询数据库中是否存在未闭合执行日志，再检查 `executor.InFlightCount`，最后调用 `FireAndTrack`。这是典型的 check-then-act：两个同时到达的 `POST /api/v1/sys/job/{id}/run` 都可能在首条日志写入前通过检查。

`FireAndTrack` 虽然同步写入本地 `_busyJobs`，但它只是递增计数，没有做条件性占位，也没有在执行器内部预留 `MaxConcurrentRuns` 容量。因此并发手动请求可以让同一个 `SerialSkip` 任务同时执行多次，也可以突破全局在飞上限。调度循环自身串行扫表，风险主要在手动执行 API，以及手动执行与调度触发同时发生时。

**建议处置**

把“并发模式校验、单任务占位、全局容量预留、创建 fire task”收敛为 `JobExecutor` 内的原子操作，例如返回“已执行 / 已在运行 / 已达容量”的 `TryFire...` 接口。服务层不应在调用执行器前自行完成分离的检查。

**缺失测试**

使用可阻塞的 `SerialSkip` handler 并发发送多个 `/run` 请求，断言仅一条请求进入执行，其余返回“任务已运行”；再以多个不同任务验证在飞数永远不超过 `MaxConcurrentRuns`。

### F3 · P1 · 独立 Worker 漏掉 HTTP 围栏 CIDR 的启动期校验

**位置**

- `backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs:65-69`，Web 宿主已有校验
- `backend/src/TenonAdmin.Services/WorkerSetup.cs:42-50`，Worker 未做对应校验
- `backend/src/TenonAdmin.Services/Jobs/JobHttpFence.cs:79-94`，非法 CIDR 在运行期只会被视为不匹配

**问题**

普通 API 宿主会在启动时验证 `TenonAdmin:Jobs:Http:BlockedCidrs`，写错即拒绝启动。独立 Worker 复用了 HTTP 任务处理器和 `JobHttpClient`，却没有执行同一校验。若 Worker 的自定义黑名单存在错误 CIDR，`JobHttpFence.IsBlocked` 会静默忽略该条规则，围栏配置会在真正执行 HTTP 任务时失效。

这与 Worker 的官方用途冲突：它正是“API 停了任务照跑”或隔离调度负载的部署路径。部署方若只检查 API 的启动日志，可能不会发现实际执行任务的 Worker 已带着失效围栏运行。

**建议处置**

抽取统一的 Jobs 选项校验方法，由 `AddTenonAdmin` 和 `AddTenonAdminWorker` 共同调用。Worker 测试应覆盖非法 CIDR 使装配立即抛出，而非仅验证 WorkerId 和租约约束。

### F4 · P2 · 多个调度数值选项不校验正数，错误配置会被静默转换为异常行为

**位置**

- `backend/src/TenonAdmin.Core/Options/AdminJobsOptions.cs:18-34`
- `backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs:61-69`
- `backend/src/TenonAdmin.Services/WorkerSetup.cs:42-44`
- `backend/src/TenonAdmin.Services/Jobs/JobSchedulerService.cs:386-400`

**问题**

当前只验证 `LeaseSeconds > 2 * HeartbeatSeconds`。`HeartbeatSeconds <= 0` 仍可能通过该约束，并在睡眠处被压成 50ms 循环，造成高频扫表；`ReloadSeconds <= 0` 会导致每拍重载；`MisfireThresholdSeconds < 0` 会把所有到期任务判作错过；`MaxConcurrentRuns <= 0` 会使调度和手动执行都无法开始。

**建议处置**

在同一个共享配置校验入口中要求 `HeartbeatSeconds`、`ReloadSeconds`、`MisfireThresholdSeconds`、`MaxConcurrentRuns` 均为正数；`MaxResponseLogBytes` 至少为非负数。各项分别补启动失败测试，避免未来用“运行时兜底”掩盖配置错误。

---

## 已核查且未发现的问题

- `NextRunTime` 的领取采用带期望值的 CAS，双节点竞争同一触发时刻不会重复领取。
- 调度与执行日志中的时刻统一截断到整秒，避免 MySQL `datetime(0)` 舍入破坏 CAS。
- HTTP 任务禁用代理和自动重定向，在连接回调中对解析后的地址复检；请求头 CR/LF 注入和执行日志中的控制字符也有防护。
- SQL 任务默认关闭，且任务读接口会掩码 HTTP 请求头中的密钥。
- 正常的死节点孤儿记录回收、活节点运行记录保留、错过策略、取消轮询、一次性任务完成和 Panic 告警均已有覆盖。

## 验证记录

- `dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj --no-build --filter "FullyQualifiedName~CronExpressionTests|FullyQualifiedName~JobApiTests|FullyQualifiedName~JobClaimTests|FullyQualifiedName~JobElectionTests|FullyQualifiedName~JobExecutorTests|FullyQualifiedName~JobSchedulerTests|FullyQualifiedName~JobSecurityTests|FullyQualifiedName~WorkerSetupTests"`
- `JobSchedulerTests` 详细运行：10/10 通过。
- 目标测试集进程以成功状态结束；审查没有修改业务代码或测试代码。

## 建议修复顺序

1. F1：避免同名重启后 `SerialSkip` 永久停摆。
2. F2：把执行容量与单任务并发控制收敛为执行器原子操作。
3. F3：统一 API 宿主与 Worker 的 Jobs 安全配置校验。
4. F4：补齐数值选项的 fail-fast 校验与回归测试。

