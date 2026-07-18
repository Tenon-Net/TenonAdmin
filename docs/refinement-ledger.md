# 精致化升级台账

> 来源:对标 XiHan.BasicApp(.NET 功能面参考)+ soybeanAdmin(Naive UI 前端标杆)的三方盘点(2026-07-16)。
> 驱动方式:仿 `docs/review/patrol-ledger.md` ——逐条执行、每条独立英文 conventional commit、可断点续跑。
> 执行协议:**每次只做一条;开工前有设计取舍/命名/行为边界疑问先向维护者确认**;做完跑验证、勾选本文件、单独提交。
> 验证纪律:`dotnet test backend/TenonAdmin.slnx`(SQLite)+ `npm run typecheck` + `npm run lint`,**两个重进程不并发**(本机内存紧张);涉端点跑 MinimalHost 实打 + `npm run gen:api`;体验件 `npm run dev` 实点。
> 横切纪律(进内核条目必做):TryAdd 注册进 ServicesSetup;Controller 挂 `[Module]` + `[RolePermission]`;菜单种子 Id 落内核区 `[1,999]`;ErrorCode 按段取号 + `[MsgKey]`;新增可替换接口补 ReplaceabilityTests;zh/en 双语键零缺口。

## 批次 A · 前端速赢

- [x] **A1 路由加载进度条**(b8dfde6) — `App.vue` 包 `n-loading-bar-provider` + 桥单例 `src/lib/loadingBar.ts`(内嵌 ~10 行桥组件存实例,不用 createDiscreteApi);`router/index.ts` 守卫 start/finish/error(enterInitial 重建菜单与懒加载 chunk 是最有价值时刻)。零依赖。验收:F5 深链/切页可见进度条,typecheck+lint 绿。
- [ ] **A2 Tab 中键关闭 + 用户固定** — `stores/tabs.ts` 加 `pinned?: boolean`,close 系列守卫统一 `affix || pinned`;`TabsBar.vue` 中键关闭(`@auxclick`)+ 右键菜单「固定/取消固定」(`ph:push-pin`),pinned 藏关闭 X;持久化白捡(persist.pick 已含 tabs)。**不做拖拽重排**(soybean 也没有)。⚠ 工作区有在途 `tabs.ts` 未提交改动(用户详情页特性),开工前与维护者确认处置。
- [ ] **A3 设置抽屉「复制配置」** — `SettingsDrawer.vue` footer 按钮:app store 中 DEFAULTS 同名键当前值 JSON 进剪贴板(VueUse `useClipboard`)+ message 提示「粘贴到 stores/app.ts DEFAULTS 即为新默认」。正中消费者 fork 改默认的模式。**不做主题预设 JSON**(6 色色板+取色器+布局卡片已覆盖其实用面)。
- [~] **A4 版本更新通知** — **用户裁定不做**(2026-07-16),从前端 pass 移除。— 新建 `composables/useVersionCheck.ts`:`fetch('/index.html', {cache:'no-store'})` 抓 entry `assets/index-*.js` 哈希与当前 document 比对,不一致 → `useDialog` 提示刷新;`useIntervalFn`(5min)+ `useDocumentVisibility` 回前台触发;仅 PROD;「稍后」本轮不再弹;`layouts/default.vue` 挂载(登录页不查)。版本号仍是 Vite define 构建期常量,不能当更新信号——用产物哈希。
- [x] **A5 外链 / iframe 菜单**(57f8c69;`isHttpUrl` 约定 + iframe 视图 + 点击/搜索外链分支 + 菜单表单 linkHint;**radio 展示糖延后**——用占位提示 + linkHint + COMPONENTS.md 承载约定,radio 属可选 UX,未做)— 零后端改动约定式:**外链** = Menu 节点 `path` 为 URL、component 空(`buildRoutesForModule` 现有 `!component` 分支天然跳过;`useLayoutMenu.onSelect`/`onSelectL1` + `MenuSearch` 回车各加 `window.open` 分支;兜底图标 `ph:arrow-square-out`);**iframe** = `path` 为内部路径、`component` 为 URL(`buildRoutesForModule` 检测 URL 时注册 `namedPage(() => import('@/views/embed/iframe.vue'))`,URL 进 `meta.iframeSrc`,keep-alive 顺带保住 iframe 状态);菜单表单加「链接类型」radio 展示糖。完成后约定写进 `COMPONENTS.md`。⚠ `useAuthMenu.ts`/`COMPONENTS.md` 有在途未提交改动,开工前确认。

## 批次 B · 后端 S 级速赢

- [x] **B1 异常日志**(B1a 后端 75becfc + B1b 前端页/菜单 3f5c56f)— `ExceptionLogFilter : IExceptionFilter`(注册在 AdminExceptionFilter 之后):非 `AdminException` 落 `sys_exception_log`(Path/HttpMethod/TraceId/ExceptionType/Message 截 2000/StackTrace 截 8000,审计 AOP 补操作人时间),**不置 ExceptionHandled**,500 照旧大声(ErrorCode.cs 既定纪律不动);写入尽力而为。`LogService` + `SysLogController` 加 `exception/page`、`DELETE exception`;前端日志页第三个 tab。纯复制 op/login 三件套。
- [x] **B2 IEmailSender 邮件通道**(10ff869)— 严格镜像 ISmsSender:`Core/Security/IEmailSender`(`SendAsync(to, subject, htmlBody, ct)`);Services 层 `LoggingEmailSender`(默认)+ `SmtpEmailSender`(BCL SmtpClient,`TenonAdmin:Email` Options:Host/Port/From/User/Pass/Ssl;配了 Host 用 SMTP 否则日志)。零表零页面;要现代协议的消费者前置注册 MailKit 实现即接管。
- [x] **B3 密码历史防重用**(cd6e534)— 表 `sys_password_history`(UserId, PasswordHash);`IPasswordHistoryService`(TryAdd):`EnsureNotReusedAsync`(取最近 N 条逐条 `IPasswordHasher.Verify`——盐不同只能验明文)+ `AppendAsync`(写入裁剪到 N)。开关 `sys.security.password.historyCount`(默认 0=关)走 SecurityPolicyProvider「DB 优先、Options 兜底」通道;挂点:`PersonalService.ChangePasswordAsync`、`UserService.ResetPasswordAsync`,建号初始密码只记录不校验。ErrorCode 42025 PasswordReused。
- [x] **B4 T-D7 文件引用根治(零 DDL)**(3f4dd58)— 秒传改「一引用一行」:`FileService.ChunkInitAsync/ChunkCompleteAsync` 命中同 hash 时不复用既有行,新插 `SysFile`(拷 StoragePath/Hash/Size),各引用方独立记录互删不影响;`FileGcService.ReclaimDeletedFilesAsync` 删盘前查同 StoragePath 是否仍有他行(ClearFilter 含未删),有则只硬删记录跳过 `storage.DeleteAsync`。幂等,与逐行 try/catch 兼容。
- [x] **B5 服务器监控页**(B5a 后端 9cfd38e + B5b 前端页/菜单 c2d52a9,卡片+手动刷新)— `IMonitorService`/`MonitorService`(TryAddScoped):Environment / Process / GC.GetGCMemoryInfo / ThreadPool / DriveInfo,进程 CPU% 两次 `TotalProcessorTime` 采样(500ms);`MonitorController` + `[Module("Monitor")]`,`GET /api/v1/sys/monitor/server`;前端新页 + 菜单种子。只报进程与主机基础面,全量指标留给消费者观测栈(OTel 是既定可选包方向)。

## 批次 C · 缓存管理(C2-lite);DiffLog 裁定不做(ADR-0001)

- [~] **C1 实体变更差异日志** — **grilling 后裁定不做**(2026-07-18,ADR-0001)。与 op-log 重叠(已记写请求参数 JSON+操作人+时间)、顶架构(仓储无咽喉需动 6 方法、AOP 跨层写表、软删是列更新捕获不干净、update 前像多一次 SELECT),增量仅"字段级前像+非 HTTP 写入"面窄。扩展点保留:消费者子类化 `SqlSugarRepository<>` 或自挂 `client.Aop.OnDiffLogEvent`(SqlSugar 原生 `.EnableDiffLogEvent` 逐命令 + `StaticConfig.CompleteUpdateableFunc` 全局 marker opt-in,后者进程全局静态内核刻意不碰)。→ 见不做清单。
- [x] **C2 缓存管理页 → C2-lite 失效操作页**(907a44e)— **改键浏览器为定向失效**(grilling + 代码核对:默认 `MemoryCacheProvider` 包 `IMemoryCache` 无法枚举键;键内嵌手机号/IP、值含明文 OTP/验证码,列键即泄 PII、读值即旁路读 OTP)。故 `ICacheProvider` **零改动**(不加 `SearchKeysAsync`);`ICacheAdminService`/`CacheAdminService`(TryAddScoped,virtual,镜像 IMonitorService 成法)4 个无参全局动作:flush-auth(遍历 sys_user 逐键清 perm/scope)、flush-dict(遍历 SysDictType.Code)、flush-config(遍历 SysConfig.ConfigKey)、rebuild-portal(自增 PortalGeneration,O(1))——全单键 RemoveAsync/Increment、DB 已知 ID 驱动、零缓存枚举、内存/Redis 皆可用。`CacheController` [Module("Cache")] 每端点 `[RolePermission]`+`[OperationLog]`;菜单 121–125 挂系统运维;前端动作卡片页 + 中英键 + gen:api。用途:补自动失效(RbacService 授权变更已自动 bump 代际+清 per-user 键)盖不到的"直接改库致陈旧"运维逃生舱。验:后端 296/0/0(含 CacheAdminTests 端到端)、typecheck/lint 绿、4 端点真 Kestrel 401 信封。

## 批次 D · OAuth/SSO(L 级,最后)

- [ ] **D1 IExternalAuthProvider 框架 + 内核 OIDC** — Core:`IExternalAuthProvider`(ProviderCode / BuildAuthorizeUrl(state, redirectUri) / ExchangeAsync(code)→ExternalIdentity{Provider,Subject,DisplayName,Phone?,Email?}),TryAddEnumerable 多实现;表 `sys_user_external`(UserId+Provider+Subject 唯一);AuthController:`GET auth/external/providers`(点亮登录页现有占位按钮)/ authorize / callback / 个人中心绑解绑;回调用一次性票据换令牌对(令牌不进重定向 URL,票据走 CacheKeys);AuthService 模板方法 `LoginByExternalAsync`(ResolveIdentity→FindBinding→未绑定按配置自动开户或抛新码→复用现有建会话/发令牌步骤);内核内置 `OidcExternalAuthProvider`(JwtBearer 已传递依赖 Microsoft.IdentityModel.Protocols.OpenIdConnect,发现文档/JWKS/id_token 验签零新包,通吃 Keycloak/Entra/Authing);ErrorCode 40xxx 续号(未绑定/未启用)。开工前疑点(届时确认):OIDC 配置形态、未绑定默认策略(自动开户 vs 拒绝)、前端回跳路由。
- [ ] **D2 卫星包 TenonAdmin.Auth.WeCom / DingTalk** — 裸 HttpClient 实现 IExternalAuthProvider(照 Caching.Redis 可选包成法),独立发包节奏。依赖 D1。

## 批次 E · 杂项修缮(可选随手带)

- [ ] **E1 岗位行拖拽漂移** — `COMPONENTS.md` 记载了 `row-draggable` + `POST /sys/position/reorder` 全套,但当前岗位页无拖拽、后端无端点(双向漂移)。倾向补实现(后端 reorder 按序赋 Sort + 前端 row-draggable,pro-table ^0.3.1 已支持);或退而修正文档。

## 不做清单(有依据,防反复)

| 项 | 理由 |
|---|---|
| 多租户 | `rebuild-design.md:38` 整体不做;官方替换点 IDataScopeProvider(:391) |
| 工作流/审批 | `rebuild-design.md:320` 非目标;属应用域独立产品 |
| 锁屏 | 纯前端是安全表演(F12 即破);真保护 = [ActiveSession] + 强退 + OS 锁屏 |
| 自注册/忘记密码 | 后台账号管理员开;SMS 免密 + 首登强改密已闭环 |
| 色弱模式 | `styles/index.css:49` 注释:整页 invert 是照片负片,刻意删除 |
| 主题预设 JSON | 现有色板 + 取色器 + 布局卡片已覆盖实用面 |
| 异步导出中心 | 同步下载够用;真需要时定时任务是现成载体 |
| 页面动画更多模式 | 过渡脆弱性刚由 .page-view 单元素根根治,到此为止 |
| 面包屑下拉子菜单 | 导航冗余度已够(侧栏+Ctrl+K+页签),撑不起结构改动 |
| 灵动岛全局任务反馈 | 内核无长任务面,为不存在的任务建通道是库存 |
| 缓存取值查看 / 键浏览 | 安全洞:值含明文 OTP、键含 PII;默认内存 provider 也无法枚举键(见 C2 / ADR-0001) |
| 实体变更差异日志(内核内 DiffLog) | 与 op-log 重叠 + 顶架构;字段级审计交消费者按替换点自建(见 C1 / ADR-0001) |
| 演示模式写保护 | **已存在**(DemoModeFilter + 41002),勿重复造 |

## 未排期备忘

- 定时任务调度中心:自写 5 段 cron(~100 行,不做秒级/L/W/#)+ `IAdminJob` TryAddEnumerable + `JobSchedulerService : BackgroundService`(复用 FileGcService 骨架与 ICacheProvider.IncrementAsync 多副本租约);表 sys_job / sys_job_log;ErrorCode 46xxx 段。设计已成稿,待排期。
- SignalR 实时通知:共享框架零依赖;Core `IRealtimePublisher` + Services no-op 默认(照 LoggingSmsSender),AspNetCore 层 `options.Realtime.Enabled` 时 MapHub + JwtBearer OnMessageReceived 读 query token;notice-changed / force-logout 推送,30s 轮询保留兜底;进程内事件总线跨副本推不到 → 轮询兜底退化为最终一致,backplane 留消费者。
- `TenonAdmin.Excel` 卫星包(Magicodes.IE,`rebuild-design.md:165` 已定稿方向):用户导入 + 同步导出,经 ApplicationAssemblies 通路挂入,内核零改动。
- 多租户消费者侧 skill 文档(replace-service 姊妹篇):字段级 = 前置替换 IDataScopeProvider + DataEntity 子类基座;库级 = 多 ConfigId。

## 轮次日志

### 第 10 轮 — 批次 C(/grill-with-docs 收敛)· 一轮设计审问 + 双 Explore 探查 + SqlSugar 官方 DiffLog 文档核对后,两项与原设想差距大:**C1 DiffLog 裁定不做**(与 op-log 重叠 + 顶架构:仓储无咽喉、AOP 跨层写表、软删列更新捕获不干净;扩展点保留给消费者),**C2 改键浏览器为 C2-lite 失效操作页**(默认内存 provider 无法枚举键、键含 PII/值含明文 OTP → 键浏览既不可用又泄密)。落地 C2-lite(907a44e):`ICacheAdminService`/`CacheAdminService`(TryAddScoped)4 无参全局失效(flush auth/dict/config + rebuild-portal,全单键 Remove/Increment、DB 已知 ID 驱动、零缓存枚举、`ICacheProvider` 零改动)+ `CacheController` [Module("Cache")] + 菜单 121–125 + 前端动作卡片页 + 中英键 + gen:api;新 CacheAdminTests 端到端锁 flush-config。两条决策存档 `docs/adr/0001`。验:后端 296/0/0、typecheck/lint 绿、4 端点真 Kestrel 401 信封。NEXT:批次 D(OAuth/SSO,开工前先定"未绑定账号默认策略 + OIDC 配置形态")/ E(岗位拖拽)按需。

### 第 9 轮 — 自查 + 双 code-reviewer 对抗审 + 修复 · 查出 2 真 bug + 2 非必要项。**修**:① B4 秒传探针每次命中都插行→孤儿泄漏 + 物理文件永不删盘(击穿 GC),改为按 (Hash, 当前用户) 幂等(FileService 加可选 ICurrentUser),加"重复探测不泄漏行"回归测试(commit 922c42a);② A5 iframe src 原为 computed(全局路由)→ keep-alive 回访重载/双页串源,改为 setup 一次性快照(f6ed470)。**取舍处理**:A3 复制配置按钮改 `import.meta.env.DEV` 门控(fork 开发动作不给管理员看)、密码历史构造参改可选(对齐 FileGcService 先例,d43c262)、监控页过滤 0 容量盘;IEmailSender 按用户意见保留。**已核验正确未动**:异常过滤器不吞 500、FileGc 单趟多引用判定、密码 trim、监控 CPU 采样。全量后端 295/0/0 + 前端 typecheck/lint/5 spec 绿。NEXT:批次 C(DiffLog+缓存)/ D(OAuth,需先定方案)/ E(岗位拖拽)按需推进。

### 第 8 轮 — 前端 pass 后半 · **A2 Tab 中键关闭 + 用户固定**(tabs.ts 加 pinned + togglePin,close 系列守卫 affix||pinned;TabsBar 中键 auxclick 关闭 + 右键固定/取消项 + pinned 显图钉藏 X;tabs.spec 加 pin 测试,commit 45224e5)· **A3 复制配置**(app.ts exportSettings 用 Object.keys(DEFAULTS);SettingsDrawer footer 复制按钮 useClipboard;中英键,commit 9e629c6)· **A5 外链/iframe**(见上,commit 57f8c69)· **浏览器冒烟**:起 MinimalHost(已知密码+干净库)+ dev 5174,登录→进系统模块→监控页真数据渲染(CPU 7.5%/内存/.NET 10.0.9/磁盘,控制台净)、异常日志页 ProTable+菜单归位(日志审计→异常日志)渲染正常,后端 API 直打 monitor/exception 端点均 code 0、菜单树含两页路径。**前端 pass 完成,精致化 A+B 全部落地**。遗留:E1 岗位拖拽漂移(批次 E)、C/D 批次未开工、A5 菜单表单 radio 展示糖(可选)。

### 第 7 轮 — 前端 pass 前半 · gen:api 重生成 schema(吃 exception+monitor 端点,提交)· **B1b 异常日志前端**(SysExceptionLog 类型 + logApi.exception* + `views/system/log/exception/index.vue` 镜像 op 页 + 中英键 + DefaultMenuSeed 116–118 挂日志审计 + 移除 KnownUnseededEndpoints 两端点 + 修 J2 注释,commit 3f5c56f)· **B5b 监控前端**(ServerInfoOutput/DiskInfo 类型 + monitorApi + `views/system/monitor/index.vue` 卡片+手动刷新 + 中英 monitor 命名空间 + DefaultMenuSeed 119–120 挂系统运维 + 清空 KnownUnseededEndpoints,commit c2d52a9)· 后端 seed/permission/typecheck/lint 全绿。踩坑:MinimalHost 未杀会锁 DLL 致 build 失败;Bash cwd 停在 web/ 要回根。**A4 版本更新通知按用户裁定不做**。NEXT: A2 Tab 中键/固定 → A3 复制配置 → A5 外链/iframe 菜单 → 浏览器冒烟(exception/monitor/新交互一起点)。

### 第 6 轮 — B5a 服务器监控(后端)· `IMonitorService`/`MonitorService`(纯 BCL:Environment/Process/GC/DriveInfo/RuntimeInformation,进程 CPU% 两次采样归一)+ `MonitorController` [Module("Monitor")] `GET /api/v1/sys/monitor/server` + KnownUnseededEndpoints 登记 · MonitorTests 验形状 + 全量 294/0/0(commit 9cfd38e)。**批次 B 后端全部完成**(B1a/B2/B3/B4/B5a)。剩余延后的前端件汇总成一个前端 pass:gen:api(一次吃 exception+monitor 端点)→ B1b 异常日志页+菜单 → B5b 监控页+菜单 →(移除两处 KnownUnseededEndpoints)→ A2 Tab 中键/固定 → A3 复制配置 → A4 版本通知 → A5 外链/iframe 菜单。NEXT: 前端 pass(需起 MinimalHost 跑 gen:api)。

### 第 5 轮 — B4 T-D7 文件引用根治(零 DDL)· 秒传命中改为 `ReferenceExistingAsync` 插独立 sys_file 行(共享 StoragePath),不再返回既有行 Id → 一方删除不牵连另一方;`FileGcService` 删盘前 ClearFilter 查同 StoragePath 是否仍有他行,有则只硬删记录、保留物理文件,末个引用回收时才删盘 · 改 ChunkUploadTests 秒传断言为新契约(不同 Id、下载同字节)+ 新增 GC 共享存储引用计数测试;全量 293/0/0(commit 3f4dd58)。踩坑:`dotnet test --no-build` 会跑旧产物,改测试后必须先 build。NEXT: B5 服务器监控页(纯后端 + 前端新页,前端已解锁)。

### 第 4 轮 — B3 密码历史防重用 · 新 `sys_password_history` 表 + `IPasswordHistoryService`(TryAdd,方法 virtual);开关 `sys.security.password.historyCount`(默认 0=关)走 SecurityPolicyProvider DIM `GetPasswordHistoryCountAsync`;改密挂 EnsureNotReused(查当前口令 + 最近 N 条历史,盐不同验明文)+ Append;重置/建号只 Append(记录不校验);ConfigSeed Id=25 播种;ErrorCode 42025 PasswordReused + 中英 locale · 2 集成测试(启用后拦当前/最近重用、按 N 裁剪后可复用;默认关允许)+ 全量 292/0/0(commit cd6e534)。**并发前端会话已收尾提交(0bb1c9e detail-page 等),前端 locale/tabs/useAuthMenu 碰撞解除**——A2/A3/A5 + B1b 可安全排期;先做完 B4/B5 收尾后端批次。NEXT: B4 T-D7 文件引用根治(纯后端,零 DDL)。

### 第 3 轮 — B2 IEmailSender 邮件通道 · Core `IEmailSender` + `AdminEmailOptions`(顶层 TenonAdmin:Email);Services `LoggingEmailSender`(默认)+ `SmtpEmailSender`(BCL System.Net.Mail);ServicesSetup 工厂按 Host 配置选实现(TryAdd,消费方前置注册可接管);TenonAdminSetup 注册 options 单例 · 镜像 ISmsSender 成法,零表零端点零请求路径改动 · 加 ReplaceEmailSender 替换测试 + EmailSenderTests(默认走日志/配 Host 选 SMTP),9 测试绿(commit 10ff869)。NEXT: B3 密码历史防重用(纯后端,需新表 sys_password_history)。

### 第 2 轮 — B1a 异常日志(后端)· 新 `SysExceptionLog` 表 + `ExceptionLogFilter`(IAsyncExceptionFilter,注册在 AdminExceptionFilter 之后)· 未捕获异常落一行(path/trace/type/message 截2000/stack 截8000 + 操作人回填),显式判 `is AdminException` 跳过业务异常,不设 ExceptionHandled 故 500 照旧 · TestHost 加 DiagController 供集成测 · 3 新测试 + 全量 287/0/0 绿(commit 75becfc)。B1b(菜单种子 + `system/log/exception/index.vue` + locale 键 + 移除 KnownUnseededEndpoints 两端点)待并发前端会话平息。因并发会话正改前端 locale/tabs/useAuthMenu,批次 A 剩余件(A2/A3/A5)与 B1b 一并延后,先推纯后端 B2→B5。NEXT: B2 IEmailSender 邮件通道(纯后端,零前端)。

### 第 1 轮 — A1 路由加载进度条 · `lib/loadingBar.ts` 桥单例(Provider 外守卫可用,未挂载前 no-op)+ App.vue 包 NLoadingBarProvider + router 三守卫 · typecheck/lint 绿,浏览器实测导航后 `.n-loading-bar-container` 入 DOM 带 fade-in(commit b8dfde6)。NEXT: A3 复制配置(A2 与在途 tabs.ts 改动重叠,待确认)。

### 第 0 轮 — 建账。三方盘点收敛为 A(5)/B(5)/C(2)/D(2)/E(1) 共 15 条;SMS/MFA 特性已落 cd88d78,非本台账范围。工作区另有在途未提交改动(用户详情页 DetailPage/useTabTitle/detailRoutes,涉 tabs.ts/useAuthMenu.ts/COMPONENTS.md)——与 A2/A5 重叠,开工前须确认处置。NEXT: A1 路由加载进度条。
