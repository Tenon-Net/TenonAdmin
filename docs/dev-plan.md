# TenonAdmin 开发计划(滚动更新)

> 设计单源:同目录 `rebuild-design.md`(§ 引用均指向它)。
> 本文件只回答三个问题:**做到哪了、怎么干活、下一个是什么**。
> 逐提交的历史在 `git log`,不在这里 —— 这份文件不是变更日志。
> 最后更新:2026-07-14(v1 发版前审计:文档清理 + 阻塞项定位)。

---

## 1. 现在在哪

**功能上已经够发第一个正式版了**,但**还不能发** —— 卡在发版链路和升级路径上,不是卡在功能上(见 §4)。

已在 CI 里被真实执行验证的能力(不是"代码写完了"):

| 能力 | 证据 |
|---|---|
| 三行启动 / 零配置 SQLite / CodeFirst 建表 / 幂等种子 / 首启随机超管密码 | `backend-ci`,4 库矩阵(sqlite / mysql / sqlserver / postgres)各 **184 用例全绿** |
| 认证 · RBAC · 多机构数据范围 · 字典配置 · 日志 · 上传 · 多应用门户 | 同上 |
| **可替换性六件套**(§8 契约) | `ReplaceabilityTests` —— **当契约看,不是普通测试** |
| 容器化交付(Caddy + compose) | `docker-smoke` 的 `single` 腿 |
| **多副本正确性**(强退跨副本即时生效 / 锁定阈值不翻倍 / 每副本独立雪花机器号 / 反代后取真实客户端 IP / 限流阈值是集群级的) | `docker-smoke` 的 `multi` 腿,5 条断言各钉一个真 bug;反面对照(缓存换回进程内)如期变红 |

前端:后端**每一个** Controller 都有对应页面,i18n **zh-CN / en-US 各 497 键、零缺口**。

## 2. 工作约定

1. **分支**:开发提交到 `dev`;`main` 只接 PR。
2. **提交**:Conventional Commits,**英文**,一个任务一个提交。
3. **验证 = 跑出来的证据,不是"代码写完了"**。改动涉及启动/接口 → 实跑 + curl;非平凡逻辑 → 留一个能跑的检查。**测试静默跳过 = 没测**(前科:`RedisCacheTests` 因门控环境变量从没被设过而早 return 报绿,整个多副本方案压在那个包上却一个断言都没跑过)。
4. **架构纪律**(违反即打回):
   - 核心四包运行时依赖只允许 `SqlSugarCore` + `Microsoft.*`(§2.3);
   - 框架服务显式 `TryAdd`(消费者前置注册即替换),服务 public、方法 virtual、长流程拆小步(§5);
   - 禁硬编码字符串:错误走 `ErrorCode` 枚举,claim 名走 `TokenClaimNames`,**后端不写死中文业务文案**(i18n 在前端按码翻译,§13);
   - 种子必须固定 Id,且落在 `TenonSeedIds` 的号段内(内核 `[1,999]` / 消费者 `[1000,4095]`);
   - 安全默认拒绝:新接口默认挂 `[RolePermission]`,放行是显式例外(§14)。
5. **公开 API 一旦发包就锁死**。可订阅类型(`FileGcService`、`SqlSugarRepository<>` 等)**不得新增必填构造参数**,新参数一律给默认值。

## 3. 走过的路(压缩版;细节见 git log)

- **M0–M1 · T1–T10**:五包分层 → 认证闭环 → RBAC → 多机构数据范围 → 会话/令牌 → 字典配置 → 日志 → 上传 → 横切收尾 → 测试工程 → NuGet 打包。
- **Phase 2**:7 维多代理自审,34 条发现全处置(12 P1 + 22 P2);补 RateLimiter + MySQL CI 矩阵。**报告已消费**(结论落进代码注释与回归测试),原文见 git 历史。
- **M1.5 + M2 + M3**:多应用门户 → Vue3 前端脚手架 → 系统管理全量页面 + 配置中心(「改配置不改代码」4 类:基础 / 安全 / 上传 / 限流)。
- **M4 清债(五批)**:操作日志 opt-out、首次登录强制改密、分片续传+秒传、种子主键保留区间、树表可用性、**磁盘回收**(`FileGcService`)、**签名直链**、**容器化交付**、**多副本就绪**(T-D3,见 §1 表)。

## 4. 发 v1 之前必须做的(唯一优先级)

功能没缺口。**缺的是"发得出去"和"升得上去"。** 下面两批做完即可发版。

### 第七批 · 发版链路(`chore(release)` / `fix(template)`)

今天打一个 `v0.1.0` 的 tag,会发生什么:

| # | 问题 | 证据 | 后果 |
|---|---|---|---|
| **R1** | **发版流水线没有任何 build / test 闸门** | `backend-release.yml:24-32` —— checkout → pack → push,中间什么都没有 | **测试是红的也照发**。发出去的包没人验证过 |
| **R2** | **模板的包版本写死,不随 tag 替换** | `templates/content/tenon-app/.template.config/template.json:19` `"defaultValue": "0.0.1-preview"`,而 `TenonApp.csproj:11` 是 `Version="TENON_PKG_VERSION"` | 打 `v0.1.0` → 包以 0.1.0 发布,模板却仍默认引用 **0.0.1-preview(一个从未发布过的版本)** → 消费者 `dotnet new tenon-app && dotnet restore` **必失败**。发出去的模板是坏的 |
| **R3** | **`smoke-test.ps1` 从没接进 CI** | 全仓 grep `smoke-test` 在 `.github/` 下**零命中** | 「消费者的第一步」零自动化覆盖 —— R2 正是这么漏出去的 |
| **R4** | 组织名漂移 | `template.json:3` `"author": "DotNet-MoYu"`,而 `git remote` 是 **Tenon-Net** | 发出去的包署名是错的组织 |
| **R5** | 包元数据缺 SourceLink / 符号包 / 图标 | `Directory.Build.props` 无 `PublishRepositoryUrl` / `IncludeSymbols` / `PackageIcon`(第 26 行自己标了 TODO) | 消费者**调试时步不进内核源码**;包在 nuget.org 上像半成品 |
| **R6** | `ScanApplicationAssemblies` 是个 `[Obsolete]` 空开关 | `TenonAdminOptions.cs:33-37` —— 注释自己写着「全代码库无一处读取它」 | **现在删是清理,发包之后再删就是破坏性变更**。窗口就这一次 |

**先写什么测试**:把 R3 补成真闸门 —— `pack → 推本地 feed → dotnet new tenon-app → restore → build` 全绿才允许 push。这条流水线**必须先红**在 R2 上(restore 找不到 0.0.1-preview),修完 R2 才转绿。R1/R4/R5/R6 是配置改动,由这条闸门顺带守住。

### 第八批 · 上线之后活不活得下去(`fix(sqlsugar)` / `feat(sqlsugar)`)

这两条不是"少了个功能",是**发了 v1 之后会持续咬人**。

- **O1 · 升级即炸(最要命的一条)**
  启动守卫 `DatabaseInitializer.EnsureSeedTablesExist`(`DatabaseInitializer.cs:74-94`)只检查**表在不在**,**不检查列**。而生产环境按 `deployment.md` 是**关掉建表闸门**的。于是:消费者从 v1.0 升到 v1.1(内核加了一列)→ 表存在 → 守卫放行 → **启动成功** → 第一次查询炸在驱动层的 `no such column`。
  这跟已经修过的「生产首启崩溃」是同一个病,只是发生在**升级**路径而非**首启**路径。**发了 v1 之后,内核每加一列都会咬到所有关闸门的用户。**
  **修法**(复用现成机械):守卫从「表」扩到「列」—— 比对实体列 vs `DbMaintenance.GetColumnInfosByTableName`,缺列即 fail-fast,**并把要跑的 `ALTER TABLE` 直接打在异常里**。把一个驱动层的天书换成一条可执行的启动错误。
  **先写测试**:建一张缺列的老表 + 关闸门 → 启动**必须抛**,且异常文本里**含缺失的列名**。它必须先红在「启动成功了,查询时才炸」。

- **O2 · 线上一条 SQL 都打不出来**
  `SqlSugarSetup.cs:78` 只挂了 `Aop.DataExecuting`(审计字段填充)。**`OnLogExecuted` / `OnError` 一个都没挂** —— 查询失败时只有驱动层异常,**没有 SQL、没有参数、没有耗时**。线上出问题,DBA 和开发都无从下手。慢查询同理:不知道慢在哪。
  **修法**:挂 `OnError`(错误 SQL + 参数 → `ILogger.LogError`)+ `OnLogExecuted`(超过 `AdminDatabaseOptions` 里的阈值 → `LogWarning`)。
  **天花板**:**绝不能写进 `SysOpLog`** —— 那条 INSERT 自己会再触发一次 `OnLogExecuted`,直接递归。输出只走 `ILogger`。
  **先写测试**:故意跑一条会失败的 SQL → 断言 `ILogger` 收到的日志里**含 SQL 原文**。

### 顺带(不单独占一批)

- **`CHANGELOG.md` 缺**——发版前建,`0.1.0` 从 M0 到今天的能力做首条。
- **升级指南缺**——和 O1 绑定:内核加列时消费者该做什么,写进 `deployment.md`。

## 5. v1 之后(不阻塞发版)

- **密码过期策略**。`SysUser` 缺 `LastPasswordChangeTime`(不能用 `BaseEntity.UpdateTime` —— 改个昵称都会刷新它)。真正的设计决策是 **null 回填策略**:天真实现会在上线当天把**全部存量用户**判成"已过期",整个用户群被钉在改密页。信号复用 `MustChangePassword`(现成通道),前端零改动。
- **T-D5 RoutePrefix / Version 配置化**。深耦合鉴权路径(权限码 = 路由),需引入 Core 的 `PermissionCode` 规范化 helper 供过滤器与种子共用。低频低价值,明确后置。
- **T-D6 验证码第二种类型**。`ICaptchaProvider` 扩展点已在,只有 SVG 一种实现。**YAGNI 未解除 —— 先确认真有人要**行为/算术/滑块,否则不做。
- **T-D7 文件引用关系**。「秒传」按内容哈希复用**同一条** `sys_file` 记录 → 同一文件 Id 被多方引用,甲删掉"他的"文件,乙的引用就悬空。现靠 GC 保留期(默认 7 天)兜底,不是真解。**先确认真有消费者被咬到**再动。
- `BaseEntity` 是否 POCO 化进 Core(§5.6)—— Phase 2a 结论:收益低,维持现状。

## 6. 这次审计确认「没问题」的(别再查了)

- **i18n**:zh-CN / en-US **各 497 键,零缺口**。
- **假配置**:逐个 Options 属性 grep 使用点 —— 除了 R6 那个 `[Obsolete]` 空开关,**没有第二个**"改了不生效"的配置。
- **前端覆盖**:后端 17 个 Controller,**每一个**都有对应页面。
- **假绿测试**:测试套件里**没有**静默跳过 / 吞断言 / 空 `[Fact]`(Redis 那个洞已在多副本批堵死 —— 现在 CI 里缺 Redis 直接失败)。
- **包不会误推**:`Directory.Build.props:13` `IsPackable=false` 是默认,src 各包显式开 —— 测试项目和示例宿主**不会被打包推上 nuget.org**。
