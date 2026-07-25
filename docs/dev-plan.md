# TenonAdmin 开发计划(滚动更新)

> 设计单源:同目录 `rebuild-design.md`(§ 引用均指向它)。
> 本文件只回答三个问题:**做到哪了、怎么干活、下一个是什么**。
> 逐提交的历史在 `git log`,不在这里 —— 这份文件不是变更日志。
> 最后更新:2026-07-25(发版后刷新:§1 全部重写,§4 转为历史,§5 重排)。

---

## 1. 现在在哪

**已发版,并且有真实消费者在吃它。** `0.3.3`(`TenonAdmin` / `TenonAdmin.Templates`)已推到 nuget.org;
公开参考应用 [`tenon-example`](https://github.com/Tenon-Net/tenon-example) 吃**已发布的包**,部署在 `tenonadmin.52moyu.net`。
§4 那两批(发版链路 / 升级路径)早已收口,自此是历史记录。

**瓶颈不在功能面了** —— 精致化台账 A–E 全部收口,两个前端模板各自到位,剩下的候选见 §5。

今日实测(2026-07-25,现跑现记,不是抄旧数):

| 面 | 证据 |
|---|---|
| 后端 | `dotnet test backend/TenonAdmin.slnx` —— **320 通过 / 0 失败 / 0 跳过** |
| `web/`(Vue 3 + Naive UI) | vitest **60/60**(15 文件)+ typecheck + lint + 生产构建全绿 |
| `web-react/`(React 19 + antd 6) | vitest **723/723**(100 文件)。与 `web/` **零共享、各自自包含**(理由见 `react-template-ledger.md`) |
| 消费者第一条命令 | `template-smoke` 逐字跑 `dotnet new tenon-app` → build → 裸 `dotnet run` 必须出 `/health`,且用**带连字符**的项目名(那类替换坑真发生过) |

已在 CI 里被真实执行验证的能力(不是"代码写完了"):

| 能力 | 证据 |
|---|---|
| 三行启动 / 零配置 SQLite / CodeFirst 建表 / 幂等种子 / 首启随机超管密码 | `backend-ci`,4 库矩阵(sqlite / mysql / sqlserver / postgres)。**注**:push/PR 上 sqlserver 腿只跑方言敏感子集(全量太慢,见 `CLAUDE.md`),全量走 nightly |
| 认证 · RBAC · 多机构数据范围 · 字典配置 · 日志 · 上传 · 多应用门户 | 同上 |
| **可替换性六件套**(§8 契约) | `ReplaceabilityTests` —— **当契约看,不是普通测试** |
| 容器化交付(Caddy + compose) | `docker-smoke` 的 `single` 腿 |
| **多副本正确性**(强退跨副本即时生效 / 锁定阈值不翻倍 / 每副本独立雪花机器号 / 反代后取真实客户端 IP / 限流阈值是集群级的) | `docker-smoke` 的 `multi` 腿,5 条断言各钉一个真 bug;反面对照(缓存换回进程内)如期变红 |

前端:后端**每一个** Controller 都有对应页面(两个模板各一套),i18n zh-CN / en-US 零缺口。

## 2. 工作约定

1. **分支**:开发提交到 `dev`;`main` 只接 PR。
2. **提交**:Conventional Commits,**英文**,一个任务一个提交。
3. **验证 = 跑出来的证据,不是"代码写完了"**。改动涉及启动/接口 → 实跑 + curl;非平凡逻辑 → 留一个能跑的检查。**测试静默跳过 = 没测**(前科:`RedisCacheTests` 因门控环境变量从没被设过而早 return 报绿,整个多副本方案压在那个包上却一个断言都没跑过)。
4. **架构纪律**(违反即打回):
   - 核心四包运行时依赖只允许 `SqlSugarCore` + `Microsoft.*`(§2.3);
   - 框架服务显式 `TryAdd`(消费者前置注册即替换),服务 public、方法 virtual、长流程拆小步(§5);
   - 禁硬编码字符串:错误走 `ErrorCode` 枚举,claim 名走 `TokenClaimNames`,**后端不写死中文业务文案**(i18n 在前端按码翻译,§13);
   - 种子必须固定 Id,且落在 `TenonSeedIds` 的号段内(内核 `[1,999]` / 消费者 `>=1000`,上限为启动时动态雪花地板);
   - 安全默认拒绝:新接口默认挂 `[RolePermission]`,放行是显式例外(§14)。
5. **公开 API 一旦发包就锁死**。可订阅类型(`FileGcService`、`SqlSugarRepository<>` 等)**不得新增必填构造参数**,新参数一律给默认值。

## 3. 走过的路(压缩版;细节见 git log)

- **M0–M1 · T1–T10**:五包分层 → 认证闭环 → RBAC → 多机构数据范围 → 会话/令牌 → 字典配置 → 日志 → 上传 → 横切收尾 → 测试工程 → NuGet 打包。
- **Phase 2**:7 维多代理自审,34 条发现全处置(12 P1 + 22 P2);补 RateLimiter + MySQL CI 矩阵。**报告已消费**(结论落进代码注释与回归测试),原文见 git 历史。
- **M1.5 + M2 + M3**:多应用门户 → Vue3 前端脚手架 → 系统管理全量页面 + 配置中心(「改配置不改代码」4 类:基础 / 安全 / 上传 / 限流)。
- **M4 清债(五批)**:操作日志 opt-out、首次登录强制改密、分片续传+秒传、种子主键保留区间、树表可用性、**磁盘回收**(`FileGcService`)、**签名直链**、**容器化交付**、**多副本就绪**(T-D3,见 §1 表)。

## 4. ~~发 v1 之前必须做的~~ ✅ 已收口(历史记录,不是待办)

功能没缺口,缺的是"发得出去"和"升得上去"。**两批已于 2026-07-14 完成,`0.1.0` 起持续发版,当前 `0.3.3`。**
本节保留是因为下面那几条"只有真跑才抓得到的坑"仍然值钱 —— 别当待办读。

### ~~第七批 · 发版链路~~ ✅ 已完成(2026-07-14)

R1–R6 全部处置。闸门先红后绿的证据在下面这张表里 —— **这一批本身就是"没有闸门"的代价的教科书**:三个洞(R2 的真实行为、被污染的本地缓存、少一层的 `PackagePath`)都是在闸门真跑起来之后才现形的,靠读代码一个也发现不了。

| # | 做了什么 | 提交 |
|---|---|---|
| **R1** | 发版流水线加闸门:构建 + 测试 + 模板冒烟全绿才推 nuget.org(推包不可撤销,只能 unlist) | `test(release)` |
| **R3** | `smoke-test.ps1` 接进 `backend-ci`(`template-smoke` 腿)与发版闸门;`templates/**` 的改动现在也会触发 CI(此前不会 —— 模板腐烂全程隐形) | `test(release)` |
| **R2** | 打包时把 `-p:Version` 盖进模板默认值(`StampTemplateVersion`),生成物精确钉住随同发布的内核版本 | `fix(template)` |
| **R4** | 包署名组织名 `DotNet-MoYu` → `Tenon-Net` | `fix(template)` |
| **R5** | SourceLink + snupkg + 包图标 —— 消费者能步进内核源码(否则"继承并重写某一步"这个卖点根本没法用) | `chore(release)` |
| **R6** | 删掉 `ScanApplicationAssemblies` 空开关(发包后再删就是破坏性变更) | `chore(core)!` |

**R2 的真实严重性(修正)**:此前记的是"消费者 `dotnet new` + `restore` **必失败**"—— **这是错的**。`PackageReference` 的 `Version` 是**最低版本**,找不到就**向上漂移**到 feed 上最近的可用版本(NU1603 警告)。实测:模板默认 `0.0.1-preview`、feed 上只有 `9.9.9-smoke` → restore **成功**,只报一条警告。所以真实后果是:**发出去的模板引用一个从未发布的版本,靠漂移勉强活着**;真正被咬的是开了 `TreatWarningsAsErrors` / 用锁文件(`--locked-mode`)/ 走精确版本策略的消费者 —— 他们直接失败。

**两个只有真跑才抓得到的坑**(记下来,别再踩):
- **冒烟测试必须隔离 `NUGET_PACKAGES`**。第一次跑它是绿的 —— 因为本机全局缓存里躺着以前跑剩的 `TenonAdmin 0.0.1-preview`,restore 根本没出门。**不隔离,它测的就是机器脏不脏,不是发版产物**。
- **断言必须钉"精确版本",不能只看 build 成不成功**。因为漂移的存在,"编译通过"完全不代表模板引对了版本 —— 加 `-warnaserror:NU1603` + 直接断言生成的 csproj 里是刚打的那个版本号,闸门才第一次真的红。

### ~~第八批 · 上线之后活不活得下去~~ ✅ 已完成(2026-07-14)

- **O1 · 升级即炸** —— 守卫从「表」扩到「列」(`EnsureExistingTablesHaveEntityColumns`)。缺列即启动失败,点名到 `sys_user(Avatar)` 并给出两条出路;`deployment.md` 补了「升级内核版本:补列」一节。
  **测试先红得很诚实**:缺一列的库,宿主**根本没抛异常,正常启动了**(`No exception was thrown`)。这条 bug 的本体不是"报错难看",是"**根本不报**"。
  **范围取舍**:只查**缺列**,不查类型/长度/可空性的漂移 —— DBA 把 `varchar` 放宽、加自己的列都**不算坏**,判死它们等于凭空造新的失败面;而缺列是**确定会崩**的(实体映射了它,查询必然 SELECT 它)。
- **O2 · 线上一条 SQL 都打不出来** —— 挂上 `OnError`(失败 SQL + 参数 → `LogError`,不给关的开关)与 `OnLogExecuted`(≥ `Database:SlowSqlMillis`,默认 1000ms → `LogWarning`)。输出**只走 `ILogger`**;绝不能写进 `SysOpLog`(那条 INSERT 自己会再触发一次 `OnLogExecuted`,直接递归)。
  **被测试套件抓到的一条**:`ILoggerFactory` 起初用了 `GetRequiredService` —— 而 `AddTenonAdminSqlSugar` 是**公开装配入口**,允许在裸容器上单独调用。公开入口不得凭空新增必需依赖,改回 `GetService` + `NullLoggerFactory`。

### 顺带(不单独占一批)

- ~~**`CHANGELOG.md` 缺**~~ ✅ 已建(2026-07-14),`0.0.1-preview` 的能力清单 + 未发布段。
- ~~**升级指南缺**~~ ✅ 已补,和 O1 绑定:内核升级补列/种子写进 `deployment.md`(见其目录第 7 行)。

## 5. 现在的候选池(下一个做什么)

**先读这条**:功能面已经没有"下一批"了,下面全是自选动作。台账 `refinement-ledger.md` 的
批次 A–E 已收口,`react-template-ledger.md` 的 R/B/C/D/E/F/G 已收口。真正的候选只有三个:

| 候选 | 状态 |
|---|---|
| **定时任务调度中心** | 设计已成稿(自写 5 段 cron + `IAdminJob` + `JobSchedulerService`),前置阻塞(实体基类重构 `c2a963e`)已解除。**但归属两处打架、开工前必须先定**:`rebuild-design.md:305/320` 写的是卫星包 `TenonAdmin.Scheduling`,台账按进内核设计 —— 这条决定了是自写 cron(留内核,依赖纪律只允许 SqlSugarCore + Microsoft.\*)还是直接吃 Quartz/Hangfire(卫星包)。 |
| **`TenonAdmin.Excel` 卫星包** | Magicodes.IE,`rebuild-design.md:165` 已定稿方向;用户导入 + 同步导出,经 `ApplicationAssemblies` 挂入,内核零改动。 |
| **E6 pro-components 转正** | `web-react/` 等上游 beta → stable,**不由我们控制**,长期挂着。 |

已裁定**不做**的(别再排):多租户与其消费者侧 skill 文档(2026-07-25,证据在 `refinement-ledger.md` 不做清单)。

**功能之外的那条**:仓龄 19 天、21 star / 4 fork,但 10 个外部 issue 全来自同一个人 ——
按 `crm-reference-app-ledger.md` §4 的去重规则,真实评估动作 = 1,阈值是 3;而 P5 的社区文已撤,
时间盒起点至今没有重新定义。**"下一个做什么"的答案未必是代码**,这一点写在这里免得下次又只在功能里挑。

### v1 之后的老条目(仍有效)

- ~~**密码过期策略**~~ —— **已实现(2026-07-16)**:`SysUser.LastPasswordChangeTime` + 运行时配置 `sys.security.password.expireDays`(默认 0=关闭);null 回填 = 存量用户首次登录时回填当时时间、过期窗口从那一刻起算(不会上线当天全员被判过期);过期仅置 `MustChangePassword`(现成通道,不拦登录),自助改密清标志并重置窗口。测试 `PasswordExpiryTests`。
- **T-D5 RoutePrefix / Version 配置化**。深耦合鉴权路径(权限码 = 路由),需引入 Core 的 `PermissionCode` 规范化 helper 供过滤器与种子共用。低频低价值,明确后置。
- **T-D6 验证码更多类型**。`ICaptchaProvider` 已有 SVG / 算术(`MathCaptchaProvider`)/ 笔画拼图(`PathCaptchaProvider`)三种实现。滑块 / 行为码仍后置 —— **YAGNI 未解除,先确认真有人要**,否则不做。
- ~~**T-D7 文件引用关系**~~ —— **已根治(2026-07-17,`3f4dd58`,零 DDL)**:秒传命中改「一引用一行」,各引用方独立记录互删不影响;`FileGcService` 删盘前查同 `StoragePath` 是否仍有他行,末个引用回收时才真删盘。
- `BaseEntity` 是否 POCO 化进 Core(§5.6)—— Phase 2a 结论:收益低,维持现状。

## 6. 这次审计确认「没问题」的(别再查了)

- **i18n**:zh-CN / en-US **各 497 键,零缺口**。
- **假配置**:逐个 Options 属性 grep 使用点 —— 除了 R6 那个 `[Obsolete]` 空开关,**没有第二个**"改了不生效"的配置。
- **前端覆盖**:后端 17 个 Controller,**每一个**都有对应页面。
- **假绿测试**:测试套件里**没有**静默跳过 / 吞断言 / 空 `[Fact]`(Redis 那个洞已在多副本批堵死 —— 现在 CI 里缺 Redis 直接失败)。
- **包不会误推**:`Directory.Build.props:13` `IsPackable=false` 是默认,src 各包显式开 —— 测试项目和示例宿主**不会被打包推上 nuget.org**。
