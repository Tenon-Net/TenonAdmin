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

## 可选应用安全

> 产品定位见 [ADR 0006](docs/adr/0006-general-admin-optional-security.md)：通用后台内核 + **可选**安全能力；**不是**等保合规产品。  
> 历史等保评估边界见 [ADR 0005](docs/adr/0005-mlps-kernel-assessment-boundary.md)（内核不是定级对象等表述仍有效；完整 Level3 目标已废止）。

| 术语 | 定义 |
|---|---|
| 定级对象 | 实际承载业务、数据和用户的已部署信息系统。等保级别由业务与环境影响决定;**TenonAdmin 内核本身不是定级对象**，不得宣称内核「已通过等保」。 |
| 可选应用安全 | 身份鉴别加强、会话模式、密码/锁定策略等应用层控制。默认宽松、零配置可跑；消费者**显式打开**独立开关后启用，不设「一键完整合规档」。定稿键名见 `docs/agents/security-optional-config.md`。 |
| `Security:Totp:*` | TOTP 二因子独立配置节；`Enabled` 默认 false。 |
| `Security:Session:CookieMode` | 可选 Cookie+CSRF 会话；默认 false（body/localStorage 兼容）。 |
| TOTP | 基于时间的一次性动态口令。用户用认证器扫描绑定二维码，登录时在密码后输入。内核可选提供；**默认关闭**。 |
| TOTP 自助绑定 | 用户在安全设置中自行完成绑定；绑定成功后一次性展示恢复码。通用后台的默认绑定模型（见 ADR 0006）。 |
| TOTP 恢复码 | 绑定或重绑时生成的一组一次性码，仅展示一次，服务端只存哈希。使用后应强制重绑并吊销会话。 |
| 用户 MFA 要求 | 账号上的简单策略标记：登录须完成 TOTP。由用户自助绑定或管理员要求启用；**不是**高敏权限自动强制全集。 |
| Cookie 会话模式 | 可选：refresh 仅在 `HttpOnly` Cookie，access 仅内存；须配 CSRF。默认仍可为 localStorage 兼容模式。 |
| 双提交 CSRF | Cookie 会话下的防护：可读 csrf Cookie + 写请求 `X-Tenon-CSRF` 头，服务端常量时间比对。 |
| 高危操作再次确认 | 对改密、清 MFA、权限等高风险操作，短时内再次校验密码和/或 TOTP。可选、可配置窗口。 |
| 数据保护密钥提供方 | `IDataProtectionKeyProvider`：为 TOTP 种子等提供信封加密主密钥材料；可接文件密钥或 KMS。 |
| 窄域秘密保护 | `ISecretProtector`：保护 TOTP 等内核秘密，**不是**全库字段加密产品。 |

### 历史术语（已废止产品语义，仅防旧文档串味）

| 术语 | 状态 |
|---|---|
| Level3 安全档 / `Profile=Level3` | **废止为产品目标**（ADR 0006）。分支上若仍有实现，须瘦身成独立开关后再合入，不得以 fail-closed 总档合入 main。 |
| Level3 三期交付 | **废止**。二/三期整包不在路线图；单点加固按独立需求评估。 |
| Level3 预检与证据报告 / 测评材料 | **不做产品承诺**。内核不提供「等保测评证明」。 |
| InitGrant / 绑定邀请唯一迁入 / 双超管批准重置 | **不做通用产品默认路径**。 |
