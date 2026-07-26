# CONTEXT.md — 领域术语表

> 本文件是术语表,只回答「这个词在本仓精确指什么」,不含实现细节(实现去各模块台账)。按 `docs/agents/domain.md` 的懒创建约定,首批词条来自定时任务模块(2026-07-26,`docs/scheduling-ledger.md`)。

## 定时任务(Scheduling)

| 术语 | 定义 |
|---|---|
| 任务(Job) | `sys_job` 一行:触发配置 + 载荷 + 失败策略 + 运行状态的完整声明。一个任务恰有一份触发配置。 |
| 触发配置(Trigger) | 任务行上的调度规则(Cron / 固定间隔 / 一次性 + 生效窗口)。合并进任务行,不是独立实体——本仓不说「触发器」。 |
| 载荷 / 处理器(Handler) | 任务被触发时真正执行的东西:编译类(`IAdminJob` 实现)、HTTP 请求、SQL 语句三类。 |
| 属性包(PropsJson) | 任务行上的字符串字典 JSON,处理器参数的唯一入口(HTTP 的 url/headers、SQL 的语句、编译类的自定义参数)。 |
| 执行记录(JobLog) | `sys_job_log` 一行 = 一次执行尝试;`EndTime` 为空即「运行中」。 |
| 一次触发(FireInstance) | 一个到期时刻被领取后的完整执行过程(含全部重试),同一 `FireInstanceId` 聚合多行执行记录。 |
| 领取(Claim) | 触发前对任务行 `NextRunTime` 的原子 CAS 更新;防双发的**唯一**正确性来源(租约只管效率)。 |
| 主节点(Leader) | 持有 `sys_job_lock` 租约的节点,唯一扫表发起调度者;其余节点为备(standby)。 |
| 心跳(Heartbeat) | 每节点每 10s 一次:upsert 自己的 `sys_job_node` 行;主节点续租,备节点尝试夺租。 |
| 租约(Lease) | `sys_job_lock` 行上的 `LeaseUntil`;过期未续即可被备节点夺取(默认 30s)。 |
| 错过 / 补偿(Misfire) | 到期时刻迟到超过阈值(默认 60s,重启/切主/卡顿所致)。Skip = 不补跑只推进(默认);FireOnceNow = 立即补跑一次,错过再多也只补一次。 |
| 崩溃(Panic) | 任务连续失败达阈值后的停摆态:不再调度、已发过告警,等人工恢复。 |
| 串行跳过(SerialSkip) | 默认并发模式:上次触发未结束则本次跳过并记 Skipped 记录;另一模式为并行(Parallel),无排队。 |
| 执行一次(Run-now) | 手动触发:在收到请求的副本本机执行,不经选主、不做领取、不动 `NextRunTime`。 |

## 通用(既有约定,收录防歧义)

| 术语 | 定义 |
|---|---|
| 内核 | `backend/src/TenonAdmin.{Core,SqlSugar,Services,AspNetCore}` 四包 + 元包 `TenonAdmin`。 |
| 卫星包 | 可选独立 NuGet 包(`TenonAdmin.Excel`、`TenonAdmin.Caching.Redis`、`TenonAdmin.Auth.*`),装了才有。 |
| 消费者 | 安装这些包搭自己后台的开发者;其程序集经 `options.ApplicationAssemblies` 挂入。 |
| 六件套 | `ReplaceabilityTests` 锁死的可替换性契约:内置服务 TryAdd 注册、前置注册即胜出、方法 `virtual` 可继承覆写。 |
