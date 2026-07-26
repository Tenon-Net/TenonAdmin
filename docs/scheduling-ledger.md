# 定时任务执行台账 · 内核 Job 模块

> **来源**:2026-07-26 `/grill-with-docs` 定向。归属、引擎选型、领域模型、集群模式、可视化范围已逐题钉死,执行期不回炉。决策存档见 `docs/adr/0004-scheduling-in-kernel-self-built.md`。
> **驱动方式**:仿 `docs/excel-ledger.md` —— 逐条执行、每条独立英文 conventional commit、可断点续跑。
> **参考实现**:Furion `framework/Furion/Schedule` + `TimeCrontab`(MIT,本地副本 `D:\MoYu\Furion`)。抄它的**思路**(deadline 睡眠、属性包、状态随行落库),不抄它的依赖 —— 本模块零第三方。

---

## 0. 给执行者的话(先读这段)

本文件是**可直接施工的规格**,不是方向性描述。§3 的表、§5 的协议、§6 的签名、§8 的端点、§9 的接线都已按仓内现状核对过(2026-07-26,基线 `dev` @ `a47ff97`),**照做即可,不要重新调研**。

三条硬规矩(与 excel-ledger 同款):

1. **一次只做一条 G 项,一条一个独立提交**(英文 conventional commit)。做完在本文件勾选并把提交号写进 §17 轮次日志,再做下一条。
2. **验证 = 跑出来的证据。** 每条 G 项在 §14 里都写了「验收判据」和「变异判据」。变异判据的意思是:**故意把实现改坏,确认对应测试真的变红,再改回来**。本模块最值钱的一条:把领取 CAS 的 `AND NextRunTime=@expected` 删掉,双发测试必须红(§12)。
3. **遇到本文件没写到的设计取舍,停下来问维护者。** 特别是:给 Core 加新 NuGet 依赖、往两个前端模板之间抽共享层、改既有 `virtual` 方法签名、把"排队"并发模式或负载均衡分片加回来(§16 明确不做)——都是明令禁止的。

**术语速查**(完整术语表在仓库根 `CONTEXT.md`):「任务」= `sys_job` 一行 = 触发配置 + 载荷 + 失败策略;「领取(Claim)」= 对 `NextRunTime` 的原子 CAS,防双发的唯一正确性来源;「主节点(Leader)」= 持有 `sys_job_lock` 租约、唯一扫表发起调度的节点;「属性包」= `PropsJson` 字符串字典,载荷参数的入口。

---

## 0.1 当前状态与换机接手(2026-07-26)

**施工中。已完成:G1(Core 契约 + Cron 引擎)、G2(实体 + 种子 + Options)、G3(调度引擎 + 三处理器)、G4(13 端点 + 菜单种子 + 回收站)——见 §17 各轮。** 下一条:G5(六件套追加 + TestHost SampleJob + CI TEST_FILTER)。接手从 §14 对应批次开工,施工期在本节滚动更新状态。

---

## 1. 决策全表(grilling 钉死,不回炉)

| 维度 | 结论 | 依据 |
|---|---|---|
| 归属 | **进内核**(Core 契约 + Services 引擎/实体 + AspNetCore 端点),不做卫星包 | ADR-0004 决策一;`rebuild-design.md` 卫星包条目作废 |
| 引擎 | **自研零依赖**,不吃 Quartz/Hangfire;cron 行为对照 TimeCrontab | ADR-0004 决策二;运行时依赖红线 |
| 领域模型 | **一任务 = 一触发**,单表 `sys_job`,不设触发器表 | ADR-0004 决策三;xxl-job 同款,UI/状态机减半 |
| 触发类型 | Cron / 固定间隔(≥5s)/ 一次性 三种 | §2 |
| Cron 完整度 | **6 段秒级**(5 段自动补秒),全套 `* , - / ? L W #`;不做年段 | §4;月末结算(L)与秒级监控是真刚需 |
| 集群 | **DB 租约选主 + 触发 CAS**,故障转移不做负载均衡;弃 FileGc 缓存桶租约 | ADR-0004 决策四;§5 |
| 存活语义 | 持久化重启恢复 + 双副本互备 + 可选独立 Worker,三层全要 | §10;用户三选全选 |
| 动态载体 | 编译 `IAdminJob` 类 + HTTP 任务 + SQL 任务(**默认关**);属性包模式 | §7;Furion 删 HttpJob 的官方结论就是属性包 |
| 失败处理 | 每任务可配:重试(次数+间隔)、超时中断+手动终止、连败告警→站内信+邮件 | §5.4 |
| 可视化 | 管理页 + 执行记录页 + CronEditor + 监控仪表盘,**双模板真写两遍** | §11;零共享是硬约束 |
| 时间语义 | **服务器本地时间**、`DateTime`、写库前整秒截断;全副本同 TZ 是部署前提 | §4.3 / §13-9 |
| 落库方式 | **同步 SqlSugar 写**,不抄 Furion 的异步持久化通道 | §5.5 论证 |

**对 `refinement-ledger.md:71` 旧稿的两处偏离(记账,防"哪个是真的"之争)**:

1. 旧稿「复用 `ICacheProvider.IncrementAsync` 多副本租约」→ **推翻**。FileGc 那把时间桶租约自己的文档就写明「各副本时钟若差到跨桶,两边可能都领到」,只适合幂等任务;任务执行普遍非幂等,改为 DB 选主(效率)+ 触发 CAS(正确性),见 §5.2。
2. 旧稿「自写 5 段 cron ~100 行,不做秒级/L/W/#」→ **推翻**。用户裁定功能强大优先,升级为 6 段全套,代价是解析器 ~400–600 行 + 向量测试,见 §4。
3. 旧稿「表 sys_job / sys_job_log 两张」→ **修正为四张**(加 `sys_job_lock`/`sys_job_node`,选主与节点展示所需),见 §3。

---

## 2. 领域模型与状态机

### 2.1 一行就是一个任务

`sys_job` 一行完整声明:**触发配置**(TriggerKind + cron/间隔/一次性时刻 + 生效窗口)+ **载荷**(HandlerKind + HandlerName + 属性包)+ **失败策略**(重试/超时/告警)+ **运行状态**(Status/NextRunTime/计数器)。同一段业务逻辑要两套时刻表 = 建两行(编译类任务只是多一行配置,不重复代码)。

### 2.2 任务状态机(4 态,无 Running)

```
              enable(重算 NextRunTime,清连败)
   Paused ◄──────────────────────────────► Ready ──连续失败达阈值──► Panic
     ▲                                       │                        │
     │ 回收站恢复强制进这里                     │ OneShot 成功/过 EndTime  │ enable(视作人工确认修复)
     └───────────────────────────────────────►│                        │
                                          Completed ◄─── enable 重算后若已无未来时刻
```

- **Ready(1)**:参与调度,`NextRunTime` 非空。**Paused(2)**:人工暂停,不调度。**Completed(3)**:一次性任务已执行 / 已过 `EndTime`,终态但可 enable 复活(重算得出未来时刻才回 Ready,否则维持 Completed)。**Panic(4)**:连续失败达 `FailAlertThreshold`,停止调度、已发告警,等人工 enable 恢复。
- **刻意没有 Running 态**:「正在运行」由**未闭合的执行记录行**(`sys_job_log.EndTime IS NULL`)推导。好处:进程崩溃不会留下卡死的 Running 任务,无需启动期修复扫描;状态列永远回答"要不要调度",不回答"此刻在不在跑"(后者是记录的事)。
- 进入 Paused/Panic/Completed 时 **`NextRunTime` 置 NULL**(Furion 同款纪律:异常态不留下次时刻),扫表条件天然排除。

### 2.3 并发与错过

- **ConcurrencyMode**:`SerialSkip(1,默认)` = 上次触发未结束(存在未闭合 log 行)则本次**跳过**并记一行 `RunStatus=Skipped`;`Parallel(2)` = 不检查,放行并跑。**不做「排队」**:慢任务 + 短间隔 = 无界积压,是事故放大器,进 §16。
- **MisfireStrategy**(错过 = 到期时刻迟到超过 `MisfireThresholdSeconds`,默认 60s;重启/主备切换/循环卡顿都可能造成):`Skip(1,默认)` = 不补跑,直接推进到首个未来时刻,把错过合并记**一行** `FireMode=MissedSkipped`(MessageText 记错过次数,不刷表);`FireOnceNow(2)` = 立即补跑**一次**(`FireMode=Misfire`),再推进到未来时刻——错过 N 次也只补一次,不回放。
- **RunOnStart 不做**:Furion 的「启动时跑一次」语义与「重启恢复」纠缠不清,「执行一次」按钮完全覆盖该需求。

---

## 3. 数据模型(四张表)

### 3.1 基类选型(先想清楚再建表)

| 表 | 基类 | 理由 |
|---|---|---|
| `sys_job` | **`BaseEntity`**(软删 + 回收站) | 任务是全局运维对象。**不用 `DataEntity`**:调度循环在后台线程跑、无 HTTP 上下文,若挂 `IOrgScoped` 会被数据范围过滤器搅局;「分机构各管各的任务」也不是内核承诺的场景 |
| `sys_job_log` | **`AuditEntity`**(物理删) | 只增日志,镜像 `SysOpLog` 语义;新表直接享受 #10 重构后的新基类,不背 `IsDelete 恒 false` 的历史包袱 |
| `sys_job_lock` | **`PrimaryId`** | 单行基础设施,不要审计四件套;不走 `IRepository<>`(约束 `AuditEntity`),经 `ISqlSugarClient` 直写 |
| `sys_job_node` | **`PrimaryId`** | 同上;Id 由代码显式赋雪花 |

### 3.2 `sys_job`(索引:`uk_sys_job_code(Code)` 唯一、`idx_sys_job_next(NextRunTime)`)

| 列 | 类型 | 说明 |
|---|---|---|
| Code | string(64),唯一 | 任务编码(种子/日志/排障的稳定锚点;冲突 → 47002) |
| Name | string(128) | 显示名 |
| HandlerKind | enum int | 1=Compiled / 2=Http / 3=Sql |
| HandlerName | string(256) | Compiled:处理器标识(默认 = 类型全名,经 GET /handlers 下拉选);Http/Sql 由服务端固定填内置处理器名 |
| PropsJson | text,可空 | **属性包**:`Dictionary<string,string?>` JSON。Http/Sql 的全部参数、编译类的自定义参数都在这(键表见 §7) |
| TriggerKind | enum int | 1=Cron / 2=Interval / 3=OneShot |
| CronExpression | string(64),可空 | 入库前归一化为 6 段(§4.2) |
| IntervalSeconds | int,可空 | **≥5**(<5 拒 47004,理由 §13-7) |
| OneShotTime | DateTime,可空 | 一次性执行时刻(已过去 → 47004) |
| StartTime / EndTime | DateTime,可空 ×2 | 生效窗口;过 EndTime 置 Completed |
| MisfireStrategy | enum int | 1=Skip(默认)/ 2=FireOnceNow |
| ConcurrencyMode | enum int | 1=SerialSkip(默认)/ 2=Parallel |
| Status | enum int | 1=Ready / 2=Paused / 3=Completed / 4=Panic(§2.2) |
| NextRunTime | DateTime,可空,索引 | **领取列**(§5.2);所有写入路径先整秒截断(§13-9) |
| LastRunTime | DateTime,可空 | 最近一次领取时刻 |
| NumberOfRuns / NumberOfErrors | long ×2 | 累计触发 / 累计失败 |
| ConsecutiveErrors | int | 连续失败计数,成功清零 |
| TimeoutSeconds | int | 0=不限;超时 → 取消该次执行,记 Timeout |
| RetryCount / RetryIntervalSeconds | int ×2 | 单次触发内的重试(默认 0 / 0) |
| FailAlertThreshold | int | 连败达此值 → 发告警 + 转 Panic;0=不告警不 Panic |
| AlertByNotice | bool | 告警走站内信(Notice 定向 `ReceiverType.User`,发任务创建人 + 超管,不广播) |
| AlertEmails | string(512),可空 | 告警收件人(逗号分隔);空 → 回退 `sys_config` 的 `sys.job.alertEmails` |
| IsSystem | bool | 内核种子任务 = true,禁删(47014);可暂停、可改触发配置 |
| Remark | string(512),可空 | |

### 3.3 `sys_job_log`(索引:`idx_sys_job_log(JobId, CreateTime desc)`)

| 列 | 类型 | 说明 |
|---|---|---|
| JobId | long | |
| JobName | string(128) | 快照(任务删了记录仍可读) |
| FireInstanceId | long | **一次触发**的关联 Id(雪花);重试各占一行,靠它聚合 |
| RetryIndex | int | 0 = 首次 |
| FireMode | enum int | 1=Schedule / 2=Manual(执行一次)/ 3=Misfire(FireOnceNow 补跑)/ 4=MissedSkipped |
| ScheduledTime | DateTime | 计划触发时刻(整秒) |
| StartTime / EndTime | DateTime / 可空 | **EndTime 为空 = 运行中**(§2.2 的推导源) |
| RunStatus | enum int | 1=Running / 2=Success / 3=Failed / 4=Timeout / 5=Cancelled / 6=Skipped |
| ElapsedMs | long | |
| NodeName | string(128) | 执行节点 |
| KillRequested | bool | 跨节点终止旗标(§5.4) |
| MessageText / ErrorText | text 可空 ×2 | 各截 8KB;HTTP 响应体截 `Http.MaxResponseLogBytes`(默认 4KB);**永不落请求头**——header 常含密钥(§13-1) |

### 3.4 `sys_job_lock`(单行选主锁)与 `sys_job_node`(节点注册表)

`sys_job_lock`:`Id`(恒 1)、`OwnerNodeName string(128)`、`LeaseUntil DateTime`、`Term long`(第几任主,纯诊断,不承担正确性)。启动时幂等 ensure-insert(INSERT 撞主键即吞异常)。

`sys_job_node`:`Id`(雪花)、`NodeName string(128) 唯一`(默认 `{MachineName}#{WorkerId}`)、`HostName`、`Pid int`、`WorkerId int`、`StartTime`、`LastHeartbeat DateTime`。每个节点(含备节点、Worker)每次心跳 upsert 自己一行。**不落 IsLeader 列**——谁是主由查询时与 lock 行比对得出,避免双写不一致。心跳超 24h 的陈尸行由狗粮任务顺手清(§7.3)。

---

## 4. Cron 引擎(自研,`TenonAdmin.Core`)

### 4.1 语法矩阵

6 段:`秒 分 时 日 月 周`。各段支持:

| 符号 | 语义 | 段限制 |
|---|---|---|
| `*` | 任意 | 全段 |
| `,` | 枚举 | 全段 |
| `-` | 区间(支持环绕,如周 `5-1`) | 全段 |
| `/` | 步长(`*/5`、`10/3`、`10-40/5`) | 全段 |
| `?` | 不指定(日/周互斥占位) | 仅日、周 |
| `L` | 日段:月末;`L-3` = 月末前 3 天。周段:`5L` = 当月最后一个周五 | 日、周 |
| `W` | 最近工作日(`15W`;`1W` 落周六 → 顺延周一;`31W` 落周日 → 回退周五;**不跨月**);`LW` = 月末最后工作日 | 仅日 |
| `#` | `5#3` = 第 3 个周五;不存在(如第 5 个)→ 该月无解,跳下月 | 仅周 |
| 名字 | `JAN-DEC` / `SUN-SAT`,大小写不敏感;周 `7` 等价 `0`(周日) | 月、周 |

**日与周同时受限即拒**(两段都不是 `*`/`?` → 47003,Quartz 同款语义;三视角验证实测 TimeCrontab 对日+周是 **AND** 而非 Vixie 的 OR——我们的拒绝是其严格子集行为,不存在语义反转)。**不做年段**(第 7 段)、不做 TimeCrontab 的 `R` 随机段、不做枚举与 L/W/# 的混用(`L,15`),均进 §16。

**对照 TimeCrontab 的四处钉死语义(2026-07-26 三视角对抗验证,G1 已落码 + 向量锁定)**:

1. **步长锚点 = 段最小值**(Quartz/Vixie 同款):月 `*/5` = {1,6,11}、日 `*/10` = {1,11,21,31}。**此处明示豁免「以 TimeCrontab 实测为准」**——TimeCrontab 把 `*/n` 一律锚 0(月 `*/5` = {5,10},锚在不存在的 0 月),连它自己 `*` 段的全集起点都对不上,属其独有怪癖;向量 `0 0 0 1 */5 ?` 已钉死我们的锚点。
2. **`7` 等价 `0` 适用一切出现位置**(单值/区间端点/步长起点/`nL`/`n#`)。周环只有 7 格:区间两端折叠后相等而原文不等(`0-7`/`7-0`)= 绕整环 = 全周;带步长的环绕区间必须在 7 格环上数相位(`6-1/2` = {六,一},8 格轮会数成 {六,日}——有向量锁死)。TimeCrontab 只在裸值折 7,区间里的 7 是哑端点、`7L`/`7#2` 直接抛,自身不一致,不跟。
3. **周段孤立 `L` = SAT**(Quartz 同款,纯周六约束、非"最后");TimeCrontab 拒绝解析,我们的接受是超集,无错误触发风险。
4. **`L-n` 按本表规格**(月末前 n 天,n∈[1,30]);TimeCrontab 无此能力、把 `L-3` **静默当 `L`**(用户输入被无声改义是它的缺陷),「以 TimeCrontab 实测为准」对 L-n 不适用。

名字只收 3 字母缩写(Quartz 同款);TimeCrontab 前缀匹配连 `SUNXYZ` 垃圾都静默收下,不跟。

### 4.2 归一化与 API

- 5 段输入(`分 时 日 月 周`)自动升 6 段:秒位补 `0`。**入库的永远是 6 段**,前端回显无歧义。
- Core 契约(全静态纯函数 + 不可变实例,无 DI):

```csharp
public sealed class CronExpression
{
    public static CronExpression Parse(string expression);      // 非法抛 FormatException(带段位与原因,供 47003 args)
    public static bool TryParse(string expression, out CronExpression? cron);
    public static string Normalize(string expression);          // 5 段→6 段;合法性顺带校验
    public DateTime? GetNextOccurrence(DateTime after);         // 严格大于 after 的下一次;100 年内无解返回 null
    public IReadOnlyList<DateTime> GetNextOccurrences(DateTime after, int count); // preview 用;count 上限 1000
}
```

- **100 年搜索边界**(2026-07-26 订正,原稿 4 年):逐候选推进超过 `after + 100 年` 仍无解(如 `0 0 0 30 2 ?`)返回 null → 调用侧把任务置 Completed 并记 Warning。**4 年不够**——`SUN#5 2月`(闰 2 月第 5 个周日)真实间隔 6 年起步(2026→2032,有向量),跨 2100 平年的 `29 2` 达 8 年,窄窗口会把合法任务误判死;原稿「TimeCrontab 同款上界」说法核实为**假**(它无界搜到 DateTime.MaxValue)。100 年下真不可能的表达式仍亚毫秒判死;逼近 DateTime.MaxValue 一律收口返回 null 不抛(after 落在 9999 年也安全)。
- **归一化统一大写**:`? * mon` 与 `? * MON` 入库同形,前端去重/比对不因大小写失效;`\r\n` 视作空白分隔符(textarea 尾巴不炸)。

### 4.3 时间语义(全模块统一,不止 cron)

- **服务器本地时间**、`DateTime`(不管 Kind)、**写库前一律整秒截断**——与仓内 `CreateTime`/`GetLocalNow()` 现状一致,不引 UTC 双轨。截断不是洁癖:MySQL `datetime(0)` 对毫秒**四舍五入**,500ms 会进位到下一秒,内存值与库值不等 → 领取 CAS 永不命中 → 任务无声停摆(§13-9,有变异测试锁死)。
- **DST 语义**(cron 算法本身与时区无关,语义在调度层):春跳(2:30 不存在)= 该次按 misfire 策略处理;秋回(2:30 出现两次)= 不重复触发——`GetNextOccurrence` 算的是日历下一次,CAS 保证同一 `NextRunTime` 只领一次。
- **全部参与调度的副本(含 Worker)必须同一时区**——容器默认 UTC、宿主机常是 +8,compose 里给 `TZ` 环境变量(§10.4)。

### 4.4 测试向量(G1 验收面,表驱动)

`L`(1/31、2/28、闰 2/29)· `L-3` · `1W` 落周六→周一 3 号 · `31W` 落周日→29 号周五(不跨月)· `LW` · `5L`(最后一个周五)· `5#5` 不存在→跳月 · 名字与 `7=0` · 步长/区间/环绕区间 · 5 段升 6 段 · 日周同限→FormatException · `0 0 0 30 2 ?` 4 年无解→null · 秒级 `*/5` 连续推进。行为分歧时以 TimeCrontab 实测为准(本地 `D:\MoYu\Furion` 可直接跑)。

---

## 5. 调度引擎(`TenonAdmin.Services`)

### 5.1 宿主与注册

`JobSchedulerService : BackgroundService`,复用 `FileGcService` 骨架(`PeriodicTimer` 换成 deadline 睡眠;`TimeProvider` 注入;循环体 try/catch 不掀翻宿主;`OperationCanceledException` 干净退出)。注册用仓内双注册成法(`ServicesSetup.cs:127-128` 同款):

```csharp
services.TryAddSingleton<JobSchedulerService>();
services.AddHostedService(sp => sp.GetRequiredService<JobSchedulerService>());
```

可替换 + 可注入 + 不被实例化两次。**API 稳定性纪律**(`dev-plan.md` §2.5):该类及 `JobExecutor` 是可订阅类型,后续新增构造参数一律给默认值。

### 5.2 选主协议与防双发(本模块的正确性心脏)

**分工**:租约回答「谁来扫表」(效率——避免 N 副本重复扫);**触发 CAS 回答「这一次到底谁执行」(正确性)**。两层独立,租约烂掉也不会双发。

- **续约**(每 `HeartbeatSeconds`=10s):`UPDATE sys_job_lock SET LeaseUntil=@now+30s WHERE Id=1 AND OwnerNodeName=@me`,影响行数=1 即续上;=0 → **立刻自认失主**,停发新触发(在飞的不杀——它们已被 CAS 领走,天然安全)。
- **夺取**(备节点每 10s):`UPDATE sys_job_lock SET OwnerNodeName=@me, LeaseUntil=@now+30s, Term=Term+1 WHERE Id=1 AND LeaseUntil<@now`。租约 30s = 3 个心跳,容一次 GC 停顿或一次 DB 抖动;接管最坏 30+10s。
- 全部是参数化 `UPDATE ... WHERE` 按影响行数判定——四库(SQLite/MySQL/SqlServer/PG)通吃,零方言锁、零 `SELECT FOR UPDATE`。
- **领取(Claim)**——每个到期任务触发前:

```sql
UPDATE sys_job
SET NextRunTime=@next, LastRunTime=@now, NumberOfRuns=NumberOfRuns+1
WHERE Id=@id AND NextRunTime=@expected AND Status=1 AND IsDelete=0
```

影响行数=1 才允许触发。**fencing 问题的回答**:旧主 GC 停顿 20s 醒来后照旧扫表发领取——但那一格 `NextRunTime` 已被新主推进,`@expected` 对不上,影响行数=0,放弃。脑裂/停顿/时钟漂移下,同一个 occurrence **数学上至多被领走一次**。`Term` 不参与任何判定,只进日志。

### 5.3 主循环(伪码;`TimeProvider` 贯穿,FakeTimeProvider 可推格)

```
ExecuteAsync(stop):
  if (!options.SchedulerEnabled) { Log("本副本不参与调度(执行一次/查询/编辑照常)"); return; }
  EnsureLockRow(); UpsertNodeRow();
  using var sub = eventBus.Subscribe<JobChangedEvent>(_ => { dirty = true; wake.Cancel(); });
  while (!stop):
    now = Truncate(time.GetLocalNow());                 // 整秒
    Heartbeat(now);                                     // node upsert + 续约/夺取(§5.2)
    if (IsLeader):
      if (dirty || now >= lastReload + ReloadSeconds) ReloadJobs();   // 全量拉 Status=Ready 行;
                                                        // 任务量级=几十行,整表重载零成本,不做版本戳行
      foreach job in cache where NextRunTime <= now:
        if (registry.Count >= MaxConcurrentRuns) { LogWarning(47013 语义); break; }   // 不领取,下拍再来
        (fire, next) = PlanFire(job, now);              // 迟到≤MisfireThreshold:正常触发;
                                                        // 超过:按 MisfireStrategy(§2.3)
        if (Claim(job, next)):                          // §5.2 的 CAS
          if (fire != null) _ = executor.FireAndTrack(job, fire);     // fire-and-forget,registry 登记
          if (misfireSkipped) InsertMissedSkippedLog(job);
        else RefreshSingle(job);                        // 被别人领走/被改,单行回读
    deadline = Min(cache.Min(NextRunTime), now + HeartbeatSeconds);
    await DelayUntil(deadline, time, linked(stop, wake));  // 变更/run-now 即刻唤醒
  StopAsync: 停发新触发 → 宿主宽限期内 drain registry → 逾期逐个 Cancel。
```

**唤醒三通道**:①本进程改任务 → `JobChangedEvent` → `wake.Cancel()` 立即生效;②别的副本改任务 → 30s 周期重载兜底(`ChannelEventBus` 是进程内的,跨副本传不过去——`RuntimeRateLimit` 同款已文档化的失效模式与同款解法);③什么都没发生 → 心跳节拍(10s)本身就是睡眠上限。

### 5.4 执行器(`JobExecutor`,public,步骤全 `protected virtual`)

```
FireAndTrack(job, fire):
  fireInstanceId = 雪花
  for retryIndex in 0..job.RetryCount:
    await using scope = scopeFactory.CreateAsyncScope()
    handler = await resolver.ResolveAsync(job.HandlerName, scope.ServiceProvider)   // null → 记 Failed 行(47005 语义),break
    logId = InsertLogRow(Running, fireInstanceId, retryIndex, NodeName)
    cts = Linked(stop, killCts);  if (TimeoutSeconds>0) cts.CancelAfter(...)
    try   { await handler.ExecuteAsync(BuildContext(job, ...), cts.Token); CloseLog(Success); break; }
    catch (OperationCanceledException) { CloseLog(超时?Timeout:Cancelled); break; }   // 取消不重试
    catch (Exception ex) { CloseLog(Failed, ex); if (retryIndex<RetryCount) await Delay(RetryInterval, time); }
  成功 → ConsecutiveErrors=0;全败 → NumberOfErrors++/ConsecutiveErrors++
    达 FailAlertThreshold → Status=Panic + NextRunTime=NULL + 发告警(仅跨阈那一次:
    AlertByNotice → Notice 定向 ReceiverType.User(创建人+超管);AlertEmails/sys.job.alertEmails → IEmailSender)
    —— Panic 后不再被调度,告警天然不刷屏
  OneShot 成功 → Status=Completed。
```

- **跨节点终止**:kill 端点写目标 log 行 `KillRequested=true` + 顺手取消本机 registry 命中项;执行侧每 `KillPollSeconds`(5s)按主键读**自己**那行的旗标,真则 Cancel。执行节点与收到请求的节点不同副本也能停,代价一条主键点查/5s,可忽略。
- **运行注册表**(`ConcurrentDictionary<long /*logId*/, (Task, CTS)>`):MaxConcurrentRuns 兜底、优雅停机 drain、kill 快路径。

### 5.5 为什么同步落库(对 Furion 异步通道的否决,记录论证)

Furion 的持久化是有界 Channel(12000,`DropWrite`)+ 单独 drain 线程——为万级任务吞吐设计,代价是**极端压力与崩溃时丢尾**。本模块:①领取 UPDATE 本身必须同步(它就是锁);②每次触发只有 2–3 笔写(开行/闭行/计数);③fire-and-forget 已把慢写隔离在调度循环外。管理系统量级(几十个任务、最密 5s 级)拿"可能丢执行记录"换"不存在的吞吐",是亏的。**同步 SqlSugar 仓储写,不建通道。**

---

## 6. Core 契约(完整签名,G1 照抄)

```csharp
// Core/Scheduling/IAdminJob.cs —— 消费者唯一必须实现的东西
public interface IAdminJob
{
    /// <summary>处理器标识,sys_job.HandlerName 按它匹配(Ordinal);默认 = 类型全名。</summary>
    string Name => GetType().FullName!;
    Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken);
}
// 注册(内核内置与消费者同一路径;Scoped——任务普遍要仓储,执行器每次触发开 scope):
//   services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, MyCleanupJob>());
// 不用 keyed DI:TryAddEnumerable 有"按实现类型防重"语义、六件套契约现成;keyed 两者皆无,
// 且类型名匹配让 DB 行 ↔ 代码的对应肉眼可查。

// Core/Scheduling/JobExecutionContext.cs(sealed;只读快照 + Log 回调)
public sealed class JobExecutionContext
{
    public long JobId; public string JobCode; public string JobName;
    public long FireInstanceId; public int RetryIndex;
    public JobFireMode FireMode; public DateTime ScheduledTime; public DateTime FireTime;
    public IReadOnlyDictionary<string, string?> Properties;   // PropsJson;编译类任务的参数入口
    public Action<string>? Log;                               // 追加到本次 log 行 MessageText(截断由执行器管)
}

// Core/Scheduling/IJobHandlerResolver.cs —— 六件套成员(消费者可整体替换解析策略)
public interface IJobHandlerResolver
{
    Task<IAdminJob?> ResolveAsync(string handlerName, IServiceProvider scopedProvider, CancellationToken cancellationToken = default);
}
// 默认实现:scopedProvider.GetServices<IAdminJob>() 按 Name 匹配;找不到返回 null(执行器记失败行,47005 语义)。
```

**`AdminJobsOptions`**(`Core/Options/AdminJobsOptions.cs`,对应 `TenonAdmin:Jobs` 节;`TenonAdminOptions` 加 `public AdminJobsOptions Jobs { get; set; } = new();`,`TenonAdminSetup` 加 `services.AddSingleton(options.Jobs);`):

| 属性 | 默认 | 说明 |
|---|---|---|
| SchedulerEnabled | true | 本副本是否参与选主/调度;false = 纯 API 副本(执行一次/查询/编辑照常)。**没有第二个总开关**——整模块下线走既有 `Api:DisabledModules=["Job"]` |
| NodeName | null | 空 → `{MachineName}#{WorkerId}` |
| HeartbeatSeconds / LeaseSeconds | 10 / 30 | §5.2;LeaseSeconds 必须 > 2×HeartbeatSeconds,绑定时校验 |
| ReloadSeconds | 30 | 跨副本配置收敛上限 |
| MisfireThresholdSeconds | 60 | 迟到超过才算「错过」 |
| MaxConcurrentRuns | 8 | 在飞上限(线程池保护,§13-8) |
| KillPollSeconds | 5 | 终止旗标轮询 |
| Http.AllowedHosts | null | 白名单(空=不限,收紧用) |
| Http.BlockedCidrs | `["169.254.0.0/16"]` | 默认只封云元数据段;**不封内网**——调度器打内网服务是主用途(§7.1 围栏细节) |
| Http.MaxResponseLogBytes | 4096 | |
| Sql.Enabled | **false** | SQL 任务总闸(§7.2) |

**进 `sys_config` 的旋钮**(运行期可调、不值得重启,两层配置约定):`sys.job.logRetentionDays`(Id=27,默认 `"30"`)、`sys.job.alertEmails`(Id=28,默认 `""`,全局兜底收件人);GroupCode 新增 `"job"`。仪表盘刷新间隔 = 前端常量 15s,不进配置。

---

## 7. 内置处理器(属性包模式:一个通用 IAdminJob + PropsJson 键表)

> Furion 2026-05 删除框架级 HttpJob 的官方结论:「属性包 + 20 行 IJob」就够。本节三个内置处理器全按此模式,消费者照样能写自己的。

### 7.1 `HttpAdminJob`(HandlerKind=Http)

属性包键:`url`(必)、`method`(默认 GET)、`headers`(JSON 对象串)、`body`、`contentType`(默认 application/json)、`timeoutSeconds`(默认取任务 TimeoutSeconds)、`successStatuses`(默认 `2xx`,可 `200,204,302`)。响应状态不符 → 本次 Failed,响应体截断进 ErrorText。

**SSRF 围栏**(新增/更新入库时校验一次 + 执行时再校验一次,防事后改 DNS):仅 http/https;`Http.AllowedHosts` 非空则只允许名单内;**禁跟随重定向**;`SocketsHttpHandler.ConnectCallback` 里对**解析后的 IP** 复检 `Http.BlockedCidrs`(防 DNS rebinding——校验时解析 A 记录、执行时解析成另一个内网地址的把戏)。命中围栏 → 47009。`IHttpClientFactory` 命名客户端 `"TenonAdmin.Jobs"`。

### 7.2 `SqlAdminJob`(HandlerKind=Sql,默认关)

属性包键:`sql`(必)。`Sql.Enabled=false`(默认)时:新建/更新选 Sql → 47008,已存量行触发 → 记 Failed(47008 语义)。开启即承认**任务编辑权限 = DBA 权限**,台账与文档站都要原句写明。执行经 `ISqlSugarClient.Ado.ExecuteCommandAsync`,受任务超时约束;影响行数写 MessageText。不做结果集查询(要报表另有正路)。

### 7.3 `JobLogCleanupJob`(狗粮,内核自带)

删 `sys_job_log.CreateTime < now - sys.job.logRetentionDays` 的行,**分批 500** 防长事务;顺手删 `sys_job_node.LastHeartbeat < now-24h` 陈尸行。种子(`DefaultJobSeed : ISeedData<SysJob>`):Id=**1**(sys_job 自有 Id 空间,内核段 <1000,SeedIdRangeTests 自动看护)、Code=`sys-job-log-cleanup`、cron `0 30 3 * * ?`、SerialSkip、Skip、IsSystem=true、Status=Ready。

**`SyncOnUpgrade => false`,与菜单种子相反,理由必须留档**:job 行是**运行态可变数据**——NextRunTime/计数器/用户改过的 cron 全在同一行,升级刷回种子值 = 清空运行态 + 吞掉用户调参。菜单是内核拥有的结构件才敢 `true`。

---

## 8. 端点契约(`[ApiController][Route("api/v1/sys/job")][Module("Job")]`,13 端点)

写侧全挂 `[OperationLog]`;错误只抛 `ErrorCode`(47xxx,§9.2),文案前端按 msgKey 渲染;返回裸 DTO 由信封过滤器包 `Result<T>`。

| # | 动词 + 路由 | 鉴权 | 用途 |
|---|---|---|---|
| 1 | GET `/api/v1/sys/job/page` | [RolePermission] | 分页(name/status/handlerKind 筛选)。行含全列,编辑表单直接用行数据,**不设 GET /{id}**(Dict/Position 成法) |
| 2 | POST `/api/v1/sys/job` | [RolePermission] | 新增。入库前:cron 归一化+校验(47003)、触发配置校验(47004,含 Interval≥5s)、属性包校验(47011)、HTTP 围栏(47009)、Sql 闸(47008);算首个 NextRunTime;发 `JobChangedEvent` |
| 3 | PUT `/api/v1/sys/job/{id}` | [RolePermission] | 更新,同上校验;触发配置变更 → 重算 NextRunTime |
| 4 | DELETE `/api/v1/sys/job/{id}` | [RolePermission] | 软删;IsSystem → 47014 |
| 5 | POST `/api/v1/sys/job/batch-delete` | [RolePermission] | `BatchDeleteInput`;IsSystem 行整批拒(47014) |
| 6 | PUT `/api/v1/sys/job/{id}/enabled` | [RolePermission] | 启停一体(对齐前端 StatusSwitch):true = Paused/Panic/Completed→Ready(重算 NextRunTime、清 ConsecutiveErrors;无未来时刻则维持 Completed→47010);false = →Paused |
| 7 | POST `/api/v1/sys/job/{id}/run` | [RolePermission] | **执行一次:在收到请求的副本本机执行**,不经选主、不做 CAS、不动 NextRunTime;SerialSkip 先查未闭合行,有 → 47006;FireMode=Manual |
| 8 | POST `/api/v1/sys/job/preview-cron` | **[ActiveSession]** | body `{cron, count=5, from?}` → `{normalized, occurrences[]}`。POST 因 cron 含 `?#` 的 query 逃逸坑;ActiveSession 免种子节点(表单里人人要用,不值得单独授权) |
| 9 | GET `/api/v1/sys/job/handlers` | [RolePermission] | 已注册编译处理器清单(`IAdminJob.Name[]`,来自 DI 集合),前端下拉数据源 |
| 10 | GET `/api/v1/sys/job/log/page` | [RolePermission] | 执行记录分页(jobId/runStatus/时间范围) |
| 11 | POST `/api/v1/sys/job/log/{id}/kill` | [RolePermission] | 终止:目标行非运行中 → 47007;写 KillRequested + 取消本机 registry 命中 |
| 12 | POST `/api/v1/sys/job/log/clear` | [RolePermission] | 清空(body `{beforeDays?, jobId?}`),硬删,照 SysLog Clear 成法 |
| 13 | GET `/api/v1/sys/job/dashboard` | [RolePermission] | 聚合:今日成/败/在飞、按状态任务数、近 14 日成败趋势、未来 10 次(内存态)、节点表(NodeName/角色/LastHeartbeat/WorkerId/Pid,角色=与 lock 行比对)。单端点单权限,前端 15s 轮询 |

**服务层**:`IJobService` / `IJobLogService`(DTO record + `{ get; init; }`、`XxxPageInput : PageInputBase`、实现全 `public virtual`、`TryAddScoped`)——Dict 模块成法逐条照抄,`skills/create-crud-backend.md` 是操作手册。

---

## 9. 内核接线

### 9.1 DI 全清单(`ServicesSetup` / `TenonAdminSetup`)

```csharp
// ServicesSetup(全部 TryAdd,消费者前置注册即胜出):
services.TryAddScoped<IJobService, JobService>();
services.TryAddScoped<IJobLogService, JobLogService>();
services.TryAddSingleton<IJobHandlerResolver, DefaultJobHandlerResolver>();
services.TryAddSingleton<JobExecutor>();
services.TryAddSingleton<JobSchedulerService>();
services.AddHostedService(sp => sp.GetRequiredService<JobSchedulerService>());
services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, HttpAdminJob>());
services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, SqlAdminJob>());
services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, JobLogCleanupJob>());
services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DefaultJobSeed>());   // 种子一律 Transient(ServicesSetup 既有成法)
// TenonAdminSetup:services.AddSingleton(options.Jobs);(照 Upload/Email 等既有行)
```

事件:`Services/Events` 加 `record JobChangedEvent(long JobId)`(增/删/改/启停/enabled 后发;仅本进程即时性,跨副本靠 §5.3 周期重载)。

### 9.2 ErrorCode 47xxx(段注释追加「47000–47999 定时任务」)

| 码 | 枚举名 | msgKey(`error.` 前缀省略) | 语义 |
|---|---|---|---|
| 47001 | JobNotFound | job.notFound | 任务不存在 |
| 47002 | JobCodeExists | job.codeExists | 任务编码已存在 |
| 47003 | JobCronInvalid | job.cronInvalid | cron 不合法(args:段位/原因) |
| 47004 | JobTriggerInvalid | job.triggerInvalid | 触发配置不合法(间隔<5s / 一次性时刻已过 / 字段缺失) |
| 47005 | JobHandlerNotFound | job.handlerNotFound | 编译处理器未注册(args:handlerName) |
| 47006 | JobAlreadyRunning | job.alreadyRunning | 串行任务上次执行未结束 |
| 47007 | JobRunNotAlive | job.runNotAlive | 目标执行记录不在运行中 |
| 47008 | JobSqlDisabled | job.sqlDisabled | SQL 任务未启用 |
| 47009 | JobHttpUrlBlocked | job.httpUrlBlocked | URL 被围栏拒绝 |
| 47010 | JobStatusConflict | job.statusConflict | 状态流转非法 |
| 47011 | JobPropsInvalid | job.propsInvalid | 属性包缺键/畸形(args:key) |
| 47012 | JobLogNotFound | job.logNotFound | 执行记录不存在 |
| 47013 | JobRunLimitReached | job.runLimitReached | 在飞数已达上限 |
| 47014 | JobProtected | job.protected | 内置任务禁删 |

前端 `error.job.*` 键 zh/en 两模板逐字对齐(ErrorCodeLocaleConsistencyTests 闸门)。

### 9.3 菜单种子(`DefaultMenuSeed`,现最大 Id=131,新行 132–146,不回填空洞)

三个页面平铺挂 **ParentId=20(系统运维)**,照缓存管理(121–125)/服务器监控(119–120)先例;Sort 接目录下现有最大值续排:

```csharp
new SysMenu { Id = 132, ParentId = 20,  Type = MenuType.Menu,   Title = "定时任务", Permission = "", Path = "/system/job",         Component = "system/job/index",         Icon = "ph:clock-countdown-duotone", Sort = <续>, Enabled = true, Visible = true },
new SysMenu { Id = 133, ParentId = 132, Type = MenuType.Button, Title = "任务-分页",     Permission = "GET:/api/v1/sys/job/page", Sort = 1, Enabled = true },
new SysMenu { Id = 134, ParentId = 132, Type = MenuType.Button, Title = "任务-新增",     Permission = "POST:/api/v1/sys/job", Sort = 2, Enabled = true },
new SysMenu { Id = 135, ParentId = 132, Type = MenuType.Button, Title = "任务-更新",     Permission = "PUT:/api/v1/sys/job/{id}", Sort = 3, Enabled = true },
new SysMenu { Id = 136, ParentId = 132, Type = MenuType.Button, Title = "任务-删除",     Permission = "DELETE:/api/v1/sys/job/{id}", Sort = 4, Enabled = true },
new SysMenu { Id = 137, ParentId = 132, Type = MenuType.Button, Title = "任务-批量删除", Permission = "POST:/api/v1/sys/job/batch-delete", Sort = 5, Enabled = true },
new SysMenu { Id = 138, ParentId = 132, Type = MenuType.Button, Title = "任务-启停",     Permission = "PUT:/api/v1/sys/job/{id}/enabled", Sort = 6, Enabled = true },
new SysMenu { Id = 139, ParentId = 132, Type = MenuType.Button, Title = "任务-执行一次", Permission = "POST:/api/v1/sys/job/{id}/run", Sort = 7, Enabled = true },
new SysMenu { Id = 146, ParentId = 132, Type = MenuType.Button, Title = "任务-处理器清单", Permission = "GET:/api/v1/sys/job/handlers", Sort = 8, Enabled = true },
new SysMenu { Id = 140, ParentId = 20,  Type = MenuType.Menu,   Title = "执行记录", Permission = "", Path = "/system/job-log",     Component = "system/job-log/index",     Icon = "ph:list-checks-duotone", Sort = <续>, Enabled = true, Visible = true },
new SysMenu { Id = 141, ParentId = 140, Type = MenuType.Button, Title = "记录-分页", Permission = "GET:/api/v1/sys/job/log/page", Sort = 1, Enabled = true },
new SysMenu { Id = 142, ParentId = 140, Type = MenuType.Button, Title = "记录-终止", Permission = "POST:/api/v1/sys/job/log/{id}/kill", Sort = 2, Enabled = true },
new SysMenu { Id = 143, ParentId = 140, Type = MenuType.Button, Title = "记录-清空", Permission = "POST:/api/v1/sys/job/log/clear", Sort = 3, Enabled = true },
new SysMenu { Id = 144, ParentId = 20,  Type = MenuType.Menu,   Title = "任务监控", Permission = "", Path = "/system/job-monitor", Component = "system/job-monitor/index", Icon = "ph:pulse-duotone", Sort = <续>, Enabled = true, Visible = true },
new SysMenu { Id = 145, ParentId = 144, Type = MenuType.Button, Title = "监控-总览", Permission = "GET:/api/v1/sys/job/dashboard", Sort = 1, Enabled = true },
```

(146 挂在 132 页下、Id 却大于 140-145 —— 因 /handlers 是后补决策,**Id 按取号顺序不按归属排**,这正是「接着最大值往后取」纪律的正确形状。)preview-cron 走 [ActiveSession],PermissionCodeConsistencyTests 只查 [RolePermission] 端点,天然豁免。类型登记:`RecycleBinController` 的 type 分发表加 `SysJob`(否则回收站页 42022)。

---

## 10. 集群与部署形态

### 10.1 消费者默认零动作

**默认不需要新建任何项目**。三种姿态递进,代码零改动:

1. **单副本(绝大多数)**:`AddTenonAdmin()` 3 行,调度器跑在 API 进程内;重启后从 DB 恢复,错过按 misfire 策略。
2. **多副本**:两个 API 副本,DB 选主自动互备,主挂 40s 内接管。docker-smoke 的 multi 就是这形态。
3. **独立 Worker(可选进阶)**:想要「API 停了任务照跑」或隔离任务负载才用——进程内调度器物理上不可能比进程活得久,这半句需求只能靠第二个进程。

### 10.2 Worker 配方

Services 层新增组合根 `WorkerSetup.AddTenonAdminWorker(IConfiguration)`(必要性:选项 POCO 的 `AddSingleton` 目前全内联在 AspNetCore 层 `TenonAdminSetup`,纯 Services 宿主无处复用):绑定 `TenonAdminOptions` → `AddSingleton` Services 依赖的各 POCO(Database/Cache/Id/Jobs/Email/Upload/Security/Logging)→ `AddTenonAdminSqlSugar(…)` + `AddTenonAdminServices()`;**WorkerId 未显式配置直接抛**(worker 必是多实例意图,比 API 侧的 Redis 守卫更严)。

`samples/WorkerHost/Program.cs`:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTenonAdminWorker(builder.Configuration);
await builder.Build().RunAsync();
```

`appsettings.json` 纪律:`TenonAdmin:Id:WorkerId` 与所有 API 副本互异(6 bit 共 64 个号);**建表与种子关掉**(worker 不拥有 schema,API 侧负责 DDL + 种子;具体键名施工时按 `AdminSeedOptions`/`Database` 节实测钉死);`Jobs:SchedulerEnabled` 保持 true。

API 副本两种姿态(§10.1 之上的部署选择):①什么都不配 —— API 与 worker 同台竞选,天然故障转移(**推荐**);②API 侧 `Jobs:SchedulerEnabled=false` —— 任务负载完全隔离出 API 进程,但单 worker 即单点,要故障转移得跑两个 worker。

### 10.3 告警到达面矩阵(防「配了告警没收到」工单)

| 部署形态 | 站内信 | 邮件 | SignalR 即时角标 |
|---|---|---|---|
| 单副本 API | ✅ | ✅(配了 SMTP;没配走 LoggingEmailSender=只进日志) | ✅(Realtime 开启时) |
| 多副本 API | ✅ | ✅ | 仅连到执行副本的连接即时,其余靠轮询(ADR-0003 无 backplane) |
| Worker 执行 | ✅(写表) | ✅ | ❌ Worker 内 `IRealtimePublisher` 是 Noop——角标靠前端既有轮询兜底,**不是缺陷** |

### 10.4 部署纪律

- **同一时区**:所有参与调度的进程 TZ 必须一致;`docker-compose*.yml` 给 `TZ` 环境变量(施工批次 G6 顺手)。
- **SQLite 不支持集群**:两进程共写一个 SQLite 文件 = 锁争用 + 损坏风险。集群/Worker 形态必须服务器库(MySQL/SqlServer/PG)。单副本 SQLite 完全没问题:选主是自竞自得,心跳+领取写频(每 10s 约两笔)WAL 轻松扛。启动时 `DbType==Sqlite` 且发现第二个活跃 node 行 → LogWarning。

---

## 11. 前端(双模板真写两遍,规格同一;零共享是硬约束)

> 动手前先读 `skills/create-crud-frontend.md`(Vue)/ `create-crud-frontend-react.md`(React)和各自 `COMPONENTS.md`。api 追加进各自 `api/index.ts`(内核内置模块,不走 ext 通道);i18n 直进 `locales/zh-CN.ts`/`en-US.ts`;`gen:api` 在 G4 后端点就绪后各刷一次。

### 11.1 定时任务页(`system/job/index`,persistKey/storage-key `sys-job`)

- 列:名称(副行灰字 Code)· 载荷类型 tag(Compiled/HTTP/SQL)· 触发描述(cron 原文 / 「每 N 秒」/ 一次性时刻)· 状态(StatusSwitch 绑端点 6;Panic 显红 tag,悬浮出 ConsecutiveErrors)· 下次执行 · 上次执行 · 成/败计数。搜索:名称、状态、载荷类型。
- 行动作:编辑 · 执行一次(useConfirm)· 记录(携 jobId 跳执行记录页)· 删除(IsSystem 行禁用,tooltip 说明)。工具栏:新增、批量删除。
- 表单(FormContainer)四分节:**基本**(Code/Name/Remark;编辑时 Code 只读)· **触发**(TriggerKind 单选切换:Cron→CronEditor;Interval→数字输入(min 5,后缀"秒");OneShot→时刻选择;+ 生效窗口、Misfire、并发模式)· **载荷**(HandlerKind 切换:Compiled→处理器下拉(GET /handlers)+属性包键值对编辑;Http→url/method/headers(kv)/body/successStatuses;Sql→SQL 文本框,`Sql.Enabled` 关时禁选并提示)· **失败处理**(重试次数/间隔、超时秒、告警阈值、站内信开关、邮件收件人)。
- 保存成功提示追加一句「集群下最长 30 秒后生效」(§13-12)。

### 11.2 CronEditor(新共享组件,**两侧 `COMPONENTS.md` 必须登记**——漏登记 = 文档漂移)

- 契约:`modelValue: string`(6 段)/ `previewCount = 5`;emits `update:modelValue`(React 侧 `value`/`onChange`)。
- 6 个页签(秒/分/时/日/月/周),每段:每 · 区间 · 步长 · 指定值;日页签加 L/`L-n`/W/LW 专项,周页签加 L(最后周几)/`#`(第几个周几)专项;日周互斥自动落 `?`。
- 「表达式」页签支持直填;底部预览区防抖 400ms 调 preview-cron,显示归一化结果 + 未来 5 次时刻;非法显示 47003 文案;秒段为 `*` 时给告警文案(等效每秒执行,§13-7)。

### 11.3 执行记录页(`system/job-log/index`,persistKey `sys-job-log`)

筛选:任务(下拉,来源 job/page)、状态、时间范围;列:任务名 · FireMode tag · 计划时刻 · 开始 · 耗时 · 状态 tag · 重试次(RetryIndex)· 节点;行动作:详情抽屉(ErrorText/MessageText + 同 FireInstanceId 的各次尝试列表)、终止(仅 Running 行);工具栏:清空(弹窗选 beforeDays)。

### 11.4 任务监控页(`system/job-monitor/index`)

顶部 4 张 stat 卡(今日成功/今日失败/运行中/任务总数)· 近 14 日成败趋势(既有 Chart 封装,双序列柱线,Bar/Line 已注册无需动 echarts.ts)· 「即将执行」表(未来 10 次:任务/时刻)· 「集群节点」表(NodeName、角色 tag(leader/standby)、最后心跳相对时间、WorkerId、Pid)。整页 15s 轮询 dashboard 端点。

### 11.5 i18n 键(两模板 zh/en 四份,逐字对齐)

顶层命名空间 `job`:`job.title / job.form.* / job.status.{ready,paused,completed,panic} / job.trigger.* / job.handler.* / job.log.* / job.monitor.* / job.cron.*`;错误键 `error.job.*` 与 §9.2 msgKey 逐字对齐。共享键(`common.add/edit/delete/...`)不重复声明。React 侧不加 `proTable.*` 键(pro-components 自带 intl)。

---

## 12. 闸门测试(不满足会红,先知道)

| 闸门 | 影响 |
|---|---|
| PermissionCodeConsistencyTests | 12 个 [RolePermission] 端点必须都有菜单种子节点(§9.3 覆盖;preview-cron 走 ActiveSession 豁免) |
| SeedIdRangeTests / SeedUpgradeTests | DefaultJobSeed Id=1、菜单 132–146、ConfigSeed 27/28 全在内核段 [1,1000) |
| ReplaceabilityTests(六件套) | 新增:前置注册自定义 `IJobService` / `IJobHandlerResolver` 胜出;`JobSchedulerService` 子类经 TryAddSingleton 覆写 |
| ErrorCodeLocaleConsistencyTests | 47xxx 的 msgKey 在两模板 zh/en 四份文件逐字存在 |
| OperationLogCoverage | 写端点全挂 [OperationLog] |
| TestHost 消费者证明 | `SampleJob : IAdminJob` + 断言默认 resolver 可按 Name 解析(消费者程序集装配路径) |

**专属测试**(引擎正确性,G3/G5):

- **领取 CAS 并发**:同一行同一 `NextRunTime`,两路并发 UPDATE,恰一路影响行数=1。**全模块最值钱的变异判据:删掉 `AND NextRunTime=@expected`,此测必须红。**
- 选主:两个 `JobSchedulerService` 实例共用同一 TestDb → 仅一个成主;dispose 主 + FakeTimeProvider 拨过租约 → 备接管、Term+1。
- FakeTimeProvider 引擎:推格触发 / 暂停不触发 / misfire 两策略(拨快 10 分钟)/ SerialSkip 跳过并记 Skipped / 重试-超时-取消 / Panic 转移且告警只发一次(fake `IEmailSender` + 查 sys_notice 定向行)/ OneShot→Completed。
- 整秒截断变异:去掉截断,MySQL/SqlServer 腿必须红(§13-9)。
- **backend-ci TEST_FILTER 必须追加** `FullyQualifiedName~JobElectionTests|FullyQualifiedName~JobClaimTests`:DateTime 等值 CAS 恰是 SqlServer 方言敏感面(精度/舍入),不进推送腿子集 = 最要命的一条只有 nightly 兜底。
- **docker-smoke multi 第 6 断言**(`scripts/smoke-multi-replica.sh` 追加,不许静默跳过——先断言 nodes==2,不满足即 fail):登录 → 建 `IntervalSeconds=5` 的 HTTP 任务(打自身 `/health`)→ 等 ~20s → log/page 行数 ≥3 **且 ScheduledTime 两两互异**(出现重复 = 双副本双发,断言消息写明"修前长这样")→ dashboard 取 leader 名 → `docker stop` 对应容器 → 等 45s(租约 30 + 心跳 10)→ 断言有新 log 行且 leader 已易主 → `docker start` 复原。

---

## 13. 已知的坑(全部预判,别踩)

1. **属性包明文密钥**:HTTP 任务 headers 常含 token,PropsJson 明文入库并随 page 回显——有任务读权限即可见。v1 接受(内部运维工具定位)但文档写明;log 侧已定死**不落请求头**。后续候选:列级掩码。
2. **DemoMode**:run-now/kill 是 POST,被 DemoModeFilter 拦(41002)。**这是特性**——任务能执行任意 HTTP/SQL,演示站绝不能放行,别为演示开洞。
3. **回收站恢复**:软删任务 Restore 后 NextRunTime 是过去时刻——规则:**恢复后强制置 Paused**,人工 enable 才重算复跑;`RecycleBinController` type 表登记 `SysJob`(漏了 = 42022)。
4. **SQLite 集群不成立**:见 §10.4。
5. **WorkerId 碰撞**:worker 未配号直接抛(§10.2);6 bit 只有 64 个号,部署文档列清单管理。
6. **时区漂移**:副本 TZ 不一致 = 错点执行(CAS 保证不双发,但时刻错)。§10.4 部署纪律 + compose TZ 变量。
7. **日志表增长**:5s 间隔任务 ≈ 1.7 万行/天;下限 5s(47004)挡住误配置,cron 秒段全 `*`(等效每秒)不硬拦、preview 给告警文案(故意写的算深思熟虑);保留 30 天 + 狗粮清理兜底。
8. **线程池饥饿**:同步阻塞的编译类任务 8 个在飞就占 8 线程。文档写明「任务必须真异步,禁 `Thread.Sleep`/`.Result`/`.Wait()`」(Furion 在 IJob 文档里同款告诫);MaxConcurrentRuns 是兜底不是解药。
9. **NextRunTime 精度陷阱(最阴)**:MySQL `datetime(0)` 毫秒**四舍五入**——内存 `12:00:00.500` 入库变 `12:00:01`,CAS `@expected` 永不命中,任务无声停摆且无任何报错。纪律:**所有写 NextRunTime/ScheduledTime 的路径先整秒截断**,并由变异测试锁死(§12)。
10. **run-now 的 check-then-act 竞态**:两副本同时点「执行一次」,SerialSkip 的未闭合行检查非原子——接受(人为触发,advisory 语义)。可选加固(不排期):插行后重查同任务未闭合行数,多者按 FireInstanceId 大者自弃。
11. **告警到达面差异**:§10.3 矩阵,文档站照抄,防工单。
12. **跨副本生效延迟**:改 cron 后最长 `ReloadSeconds`(30s)才在 leader 生效(事件总线进程内的文档化盲区);前端保存提示带一句(§11.1)。
13. **HandlerName 拼错**:前端走 /handlers 下拉已基本闭死;绕过前端直调 API 拼错 → 每次触发记 47005 失败行,执行记录页可见,不静默。

---

## 14. 施工批次(G1–G9,一批一提交;**本轮只出设计,未排期实施**)

| 批 | 内容 | 验收判据 | 变异判据 |
|---|---|---|---|
| ✅G1 | Core:`IAdminJob`/`JobExecutionContext`/`IJobHandlerResolver`/`CronExpression`/`AdminJobsOptions`/ErrorCode 47xxx | §4.4 向量全绿(70 例);`Normalize` 5→6 段 | 删 `31W` 不跨月约束 → 3 向量红(已实测) |
| ✅G2 | 实体四张 + `DefaultJobSeed` + ConfigSeed 27/28 + `TenonAdminOptions.Jobs` 接线 | sqlite 全新建库启动成功;SeedIdRange 绿 | 把种子 Id 改 1001 → SeedIdRange 红(已实测) |
| ✅G3 | 引擎:选主/循环/执行器/registry/三个内置处理器/`JobChangedEvent`/DI | §12 FakeTimeProvider + 选主 + CAS 测试全绿(21 例) | 删 CAS 的 `AND NextRunTime=@expected` → 双发测试红(已实测) |
| ✅G4 | `JobController` 13 端点 + 菜单种子 132–146 + `RecycleBin` 登记 + `gen:api` | PermissionCodeConsistency/OperationLogCoverage 绿;24 例 HTTP + 40 例安全测试绿 | 摘掉任一 [OperationLog] → 红 |
| ✅G5 | HTTP 级测试 + Replaceability 追加 + TestHost `SampleJob` + backend-ci TEST_FILTER | 47xxx 各码有用例;SqlServer 子集含 Election/Claim | 前置注册假 `IJobService` 不生效 → 六件套红(已实测) |
| ✅G6 | Worker:`WorkerSetup` + `samples/WorkerHost` + compose TZ + smoke 第 6 断言 | multi 腿 6 断言绿(本机彩排:5s 任务 25s 内 5 次、时刻互异、全 Success) | 注释掉 standby 夺取 → 杀主断言红 |
| G7 | `web/`:三页面 + CronEditor + COMPONENTS.md + i18n | typecheck/lint 绿;浏览器实走建任务→执行→看记录 | — |
| G8 | `web-react/`:同款 | 同上 + 既有测试套件绿 | — |
| G9 | 文档收口:本台账 §0.1 更新、CHANGELOG、site 文档、`skills/new-module.md` 交叉引用 | `lint:prose` 绿(site 侧) | — |

---

## 15. 验证命令

```bash
dotnet test backend/TenonAdmin.slnx                                        # 默认 SQLite
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Cron"     # 向量单测
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~JobElectionTests|FullyQualifiedName~JobClaimTests"
dotnet run  --project backend/samples/MinimalHost                          # :5100,建表+种子+狗粮任务可见
dotnet run  --project backend/samples/WorkerHost                           # G6 后:纯 worker 竞选
cd web       && npm run dev                                                # :5173
cd web-react && npm run dev                                                # :5174
docker compose -f docker-compose.yml -f docker-compose.scale.yml up -d --build
bash scripts/smoke-multi-replica.sh http://localhost:8080                  # 含第 6 断言
```

---

## 16. 明确不做(防膨胀,再提先翻这里)

| 项 | 理由 |
|---|---|
| 排队并发模式(Queue) | 慢任务+短间隔=无界积压,事故放大器;SerialSkip 的 Skipped 记录已让丢弃可见 |
| 负载均衡/分片(xxl-job 式路由) | 要执行器注册+RPC 分发+路由策略一整套;主备已满足「不断档」,管理系统任务量撑不起分片 |
| 一任务多触发器 | ADR-0004 决策三;要两套时刻表建两行 |
| cron 年段(第 7 段)/ R 随机段 | 后台任务没有"2027 年才跑"的真需求;R 是 TimeCrontab 独有玩具 |
| cron 枚举与 L/W/# 混用(`L,15`、`1W,15W`、`1#1,3#2`) | TimeCrontab 支持并集;我们 Day/Dow 单模式、响亮拒绝(47003 报错说明白)。CronEditor 不产此形态;双月结账类真需求 = 建两行任务 |
| cron 全名月/周(`SUNDAY`/`JANUARY`) | Quartz 同款只收 3 字母缩写;TimeCrontab 的前缀匹配连垃圾都收,不学 |
| 回放全部错过次 | FireOnceNow 只补一次;停机 3 天的日报任务补 3 份没有意义,要补数走业务侧 |
| C# 脚本任务(Roslyn) | 依赖巨大+安全面最广,与零依赖内核冲突;grilling 已否 |
| 任务依赖 DAG / 工作流 | 属应用域产品(`rebuild-design.md` 非目标同款理由) |
| 秒级以下(毫秒)调度 | 整秒截断是 4 库可移植性的地基;毫秒级要的是消息队列不是 cron |
| 调度器专用 SignalR 推送 | 15s 轮询对监控页足够;Realtime 的公告/强退语义不扩容(ADR-0003 范围) |
| 异步持久化通道 | §5.5 论证:同步写在此量级严格更优 |

---

## 17. 轮次日志(施工期追加)

### 第 0 轮 — 设计定稿(2026-07-26)

`/grill-with-docs` 全流程:三路探索(Furion Schedule 全解剖 / 内核扩展点 / 双前端模式)→ 9+2 轮逐题拍板 → 规划核查(表结构/选举协议/端点/风险)→ 本台账 + ADR-0004 + `CONTEXT.md` 落盘,`refinement-ledger.md:75` ⚠ 归属待决项消账。未写一行实现代码,施工从 G1 开始。

### 第 1 轮 — G1 Core 契约 + Cron 引擎(2026-07-26,提交即本条对应的 feat(core) 提交)

- 落码:`Core/Scheduling/{IAdminJob,JobExecutionContext,IJobHandlerResolver,JobFireMode,CronExpression}.cs`、`Core/Options/AdminJobsOptions.cs`、ErrorCode 47001–47014、四份语言包 `error.job.*` 14 键(locale 闸门只查叶子段,`cronInvalid` 等 10 个新叶子不随批就红——i18n 错误键因此从 G7/G8 前置到 G1,页面键仍留 G7/G8)。向量测试 70 例全绿;31W 变异判据实测 3 红后还原。
- **三视角对抗验证**(TimeCrontab 源码+双侧探针实跑 / 边角攻击 130+ 输入 / 规格逐条核对)抓出并已修 4 个真缺陷:①周段带步长的环绕区间在 0..7 八格轮上数错相位(`6-1/2` 给出{六,日},应为{六,一})→ 改 7 格周环专用解析;②4 年搜索上界会把 `SUN#5 2月`(下次 2032-02-29)误判无解 → 任务被错置 Completed,改 100 年,原稿「TimeCrontab 同款上界」说法核实为假(它无界);③after 逼近 DateTime.MaxValue 时抛 ArgumentOutOfRangeException → 收口返回 null;④GetNextOccurrences 大 count 预分配 OOM → count 上限 1000。另:CRLF 计入空白分隔、归一化统一大写。
- 验证同时钉死 4 处对照语义并回写 §4.1(步长锚点=段最小值明示豁免、7≡0 全位置、孤立 L=SAT、L-n 仲裁不适用),§16 增两行(枚举混用 L/W/#、全名月/周),§6 参数名 `ct`→`cancellationToken`、§9.1 种子注册 Singleton→Transient 两处笔误订正。

### 第 2 轮 — G2 实体四张 + 种子 + Options 接线(2026-07-26,提交即本条对应的 feat(services) 提交)

- 落码:`Entities/{JobEnums,SysJob,SysJobLog,SysJobLock,SysJobNode}.cs`(§3 逐列;job 枚举六件合一文件)、`Jobs/JobConfigKeys.cs`(sys_config 键常量,照 FileService.KEY_* 成法)、`Seed/DefaultJobSeed.cs`(Id=1、SyncOnUpgrade=false 理由留档)、ConfigSeed 追加 Id 27/28(GroupCode="job")、`TenonAdminOptions.Jobs` + `TenonAdminSetup` AddSingleton 与 LeaseSeconds>2×HeartbeatSeconds 绑定校验、ServicesSetup 种子注册。
- 实现期敲定两处细节:①种子行 **NextRunTime 留空**——种子编写期没有时钟;G3 的 ReloadJobs 须对「Ready 且 NextRunTime 为空」的行按触发配置补算(顺带覆盖 enable 复活路径),此约定已写进种子注释;②种子 HandlerName 暂为字面量 `"TenonAdmin.Services.JobLogCleanupJob"`,G3 落类后改 `typeof(...).FullName!`。
- 全量 417 绿(WebApplicationFactory 即 sqlite 全新建库);变异判据实测:种子 Id 改 1001 → SeedIdRange 红,还原。

### 第 3 轮 — G3 调度引擎 + G4 端点(2026-07-26)

- **G3 落码**:`Jobs/{JobTime,JobTrigger,DefaultJobHandlerResolver,JobHttpFence,JobHttpClient,HttpAdminJob,SqlAdminJob,JobLogCleanupJob,JobExecutor,JobSchedulerService}.cs` + `Events/JobEvents.cs` + ServicesSetup 十行注册。测试 `JobEngineHost`(裸容器 + 可拨时钟 + 手动推拍)、`JobElectionTests`/`JobClaimTests`/`JobSchedulerTests`/`JobExecutorTests` 共 21 例。**CAS 变异判据实测**:删 `NextRunTime == expected` → 3 例红,还原。
- **G4 落码**:`Jobs/{JobModels,IJobService,JobService}.cs`(含 `JobLogService`)+ `Controllers/JobController.cs` 13 端点 + 菜单种子 132–146 + `RecycleBinController` 的 `job` 类型(恢复走专用分支强制 Paused)。`JobApiTests` 24 例 + `JobSecurityTests` 40 例。
- **实现期新增的三处纪律**(台账原稿没有,已落码并有测试):①`ExecutionContext.SuppressFlow` —— SqlSugarScope 按 AsyncLocal 隔离连接,fire-and-forget 与 kill 轮询不掐断上下文就会与调度循环共用连接、并发查询直接炸 reader;②处理器不响应取消令牌时(SqlSugar 的 Ado 执行就是),`await` 返回后必须复查 `linked.IsCancellationRequested`,否则跑过头的 SQL 会被记成 Success;③`JobExecutor.IsBusyLocally` 内存在飞表 —— SerialSkip 的库查询是 check-then-act,同节点窗口靠它闭死。
- **三视角对抗验证**(并发正确性 / 规格核对 / SSRF 实跑探针)抓出 1 blocker + 5 major,全部已修并有回归:
  - **blocker**:节点 `kill -9` 遗留的未闭合 Running 行永不闭合,而它正是 SerialSkip 的调度输入 → 任务永久停摆且无 API 恢复路径(kill 写旗标没人轮询、清空与狗粮都刻意保留未闭合行)。修:主节点每拍 `ReapOrphanRunsAsync`,把「执行节点心跳陈死(> 2×LeaseSeconds)」的未闭合行判死闭合为 Cancelled。§2.2「无需启动期修复扫描」的说法只对 Status 列成立,已按此订正理解。
  - **major**:重试 `Task.Delay` 不挂令牌 → 停机硬停后还会开跑全新一次尝试并把 `StopAsync` 无限卡死;修为挂 drain 令牌 + 每轮复查。
  - **major**:`SocketsHttpHandler` 默认走系统/环境代理 → 有 `HTTP_PROXY` 时 ConnectCallback 只看得见代理 IP,**IP 围栏整个归零**(实测能经代理取回云元数据)。修:`UseProxy=false`。
  - **major**:请求头属性包用 `TryAddWithoutValidation`,CRLF 原样上线路 → 内部人可在同一连接走私第二个请求(方法/路径/Host 全自选,围栏与执行记录都看不见)。修:`JobHttpFence.ValidateHeader` 在入库与执行两处校验 token 名与控制字符。
  - **major**:默认 `BlockedCidrs` 只有 IPv4 → AWS IMDS 的 IPv6 端点 `fd00:ec2::254` 裸奔。修:默认值加 `fd00:ec2::/32` 与 `fe80::/10`(169.254/16 的 IPv6 孪生;RFC1918 与 ULA 照旧不封)。
  - **major**:`HandlerKind=Compiled` + 填内置处理器全名可整体跳过入库侧围栏与 SQL 总闸(执行侧还拦得住,但纵深掉一层、47008/47009 保存时不报)。修:编译类拒绝内置处理器全名。
  - 另修 4 条 minor:判死 UPDATE 补 `NextRunTime IS NULL` 谓词(否则并发编辑会把刚救活的任务判死并留下 Completed+非空 NextRunTime,破 §2.2 不变量);`JobService.UpdateAsync` 从整行盲写改为定向更新 + NextRunTime CAS(整行写回会复活已领取的 occurrence → 双发,顺带覆盖计数器);丢失唤醒窗口补 `_dirty` 复查;畸形 CIDR 静默失效改为绑定期抛。
  - **密钥面**(§13-1 的三条旁路):属性包 headers 值在列表接口按 `********` 掩码、保存时掩码原样回传即取回原值;操作日志脱敏词表加 `header/authorization/apikey/cookie`;执行记录只落 `scheme+host+path`(原 url 的 userinfo 与查询串常含凭据)。
  - **PG 专属坑**:响应体含 `\0` 时 PostgreSQL text 列拒收(22021)→ 记录闭合失败 → 永久 Running → 该任务再也不触发。修:摘要入库前净化控制字符。
- 全量 504 绿。

### 第 4 轮 — G5 六件套 + 消费者证明 + CI 子集(2026-07-26)

- `TestHost/SampleJob.cs`(消费者程序集里的 `IAdminJob`,一行 `TryAddEnumerable` 注册)+ `ReplaceabilityTests` 追加两条:消费者处理器可被默认解析器按 Name 找到且出现在 `/handlers` 清单;`IJobService`/`IJobHandlerResolver`/`JobSchedulerService` 前置注册即胜出。
- **写六件套测试时发现的坑,值得记住**:本文件既有用例的 `Overrides = s => s.Replace(...)` 写法**测不出 TryAdd**——它是 `ConfigureTestServices`,跑在 `AddTenonAdmin` 之后,把 TryAdd 改成 Add 照样绿(它证明的是"可替换",不是"TryAdd 注册")。新用例改为裸容器**前置**注册再调 `AddTenonAdminServices()`,变异实测:TryAdd→Add 即红。
- `backend-ci.yml` 的 SqlServer 推送腿子集追加 `JobElectionTests|JobClaimTests` —— DateTime 等值 CAS 正是该方言的精度/舍入敏感面,不进子集就只剩 nightly 兜底。
- 全量 506 绿。

### 第 5 轮 — G6 Worker 配方 + 部署纪律 + 冒烟断言(2026-07-26)

- `Services/WorkerSetup.cs`(`AddTenonAdminWorker`:绑 `TenonAdmin` 节 → 注册各选项 POCO → SqlSugar + Services;WorkerId 未显式配置直接抛,租约参数同 API 侧校验)+ `samples/WorkerHost`(Program.cs 三行 + 带注释的 appsettings)+ 进 `.slnx`。`WorkerSetupTests` 3 例:Generic Host 装配得出调度器、双注册同实例、两条守卫。
- **施工时钉死的 §10.2 待定项**:worker 的「建表与种子关掉」= `Database:EnableCodeFirst=false` + `Database:EnableSeed=false`(schema 归 API 侧所有);`Services` 加了 `Microsoft.Extensions.Configuration.Binder`(Worker 宿主没有 AspNetCore 层可依赖,Microsoft.* 合规),`Microsoft.Extensions.Hosting` 只进样例项目、不进内核包。
- compose 两份都加 `TZ`(默认 `Asia/Shanghai`,可 `TENON_TZ` 覆盖)——容器默认 UTC、宿主机常是 +8,全副本同时区是调度的部署前提(§10.4)。
- `scripts/smoke-multi-replica.sh` 第 6 断言:前置断言 nodes==2(不足即 fail,不静默跳过)→ 建 5s 间隔 HTTP 任务打自身 `/health` → 25s 后行数 ≥3 **且 ScheduledTime 两两互异**(重复即双发,断言消息写明"修前长这样")→ 从 dashboard 取 leader、按 nodeName 的 `#WorkerId` 后缀映射回 compose 服务 → `docker compose stop` 杀主 → 50s 后断言有新行且 leader 已易主 → 复原并删任务。
- **本机彩排**(无 Docker,故用 MinimalHost 真跑同一套断言):5s 任务 25 秒内触发 5 次、5 个互异时刻、全 Success(HTTP 200 打到自身 /health);dashboard 的 `nodes[].nodeName/isLeader/workerId`、log 分页的 `items[].scheduledTime`、`/handlers` 三项形状与脚本读法逐字对上。剩下只有杀主接管那半段要真容器,交给 CI 的 multi 腿。
- 全量 509 绿。
