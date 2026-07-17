# 精致化升级台账

> 来源:对标 XiHan.BasicApp(.NET 功能面参考)+ soybeanAdmin(Naive UI 前端标杆)的三方盘点(2026-07-16)。
> 驱动方式:仿 `docs/review/patrol-ledger.md` ——逐条执行、每条独立英文 conventional commit、可断点续跑。
> 执行协议:**每次只做一条;开工前有设计取舍/命名/行为边界疑问先向维护者确认**;做完跑验证、勾选本文件、单独提交。
> 验证纪律:`dotnet test backend/TenonAdmin.slnx`(SQLite)+ `npm run typecheck` + `npm run lint`,**两个重进程不并发**(本机内存紧张);涉端点跑 MinimalHost 实打 + `npm run gen:api`;体验件 `npm run dev` 实点。
> 横切纪律(进内核条目必做):TryAdd 注册进 ServicesSetup;Controller 挂 `[Module]` + `[RolePermission]`;菜单种子 Id 落内核区 `[1,999]`;ErrorCode 按段取号 + `[MsgKey]`;新增可替换接口补 ReplaceabilityTests;zh/en 双语键零缺口。

## 批次 A · 前端速赢

- [ ] **A1 路由加载进度条** — `App.vue` 包 `n-loading-bar-provider` + 桥单例 `src/lib/loadingBar.ts`(内嵌 ~10 行桥组件存实例,不用 createDiscreteApi);`router/index.ts` 守卫 start/finish/error(enterInitial 重建菜单与懒加载 chunk 是最有价值时刻)。零依赖。验收:F5 深链/切页可见进度条,typecheck+lint 绿。
- [ ] **A2 Tab 中键关闭 + 用户固定** — `stores/tabs.ts` 加 `pinned?: boolean`,close 系列守卫统一 `affix || pinned`;`TabsBar.vue` 中键关闭(`@auxclick`)+ 右键菜单「固定/取消固定」(`ph:push-pin`),pinned 藏关闭 X;持久化白捡(persist.pick 已含 tabs)。**不做拖拽重排**(soybean 也没有)。⚠ 工作区有在途 `tabs.ts` 未提交改动(用户详情页特性),开工前与维护者确认处置。
- [ ] **A3 设置抽屉「复制配置」** — `SettingsDrawer.vue` footer 按钮:app store 中 DEFAULTS 同名键当前值 JSON 进剪贴板(VueUse `useClipboard`)+ message 提示「粘贴到 stores/app.ts DEFAULTS 即为新默认」。正中消费者 fork 改默认的模式。**不做主题预设 JSON**(6 色色板+取色器+布局卡片已覆盖其实用面)。
- [ ] **A4 版本更新通知** — 新建 `composables/useVersionCheck.ts`:`fetch('/index.html', {cache:'no-store'})` 抓 entry `assets/index-*.js` 哈希与当前 document 比对,不一致 → `useDialog` 提示刷新;`useIntervalFn`(5min)+ `useDocumentVisibility` 回前台触发;仅 PROD;「稍后」本轮不再弹;`layouts/default.vue` 挂载(登录页不查)。版本号仍是 Vite define 构建期常量,不能当更新信号——用产物哈希。
- [ ] **A5 外链 / iframe 菜单**(唯一 M 级)— 零后端改动约定式:**外链** = Menu 节点 `path` 为 URL、component 空(`buildRoutesForModule` 现有 `!component` 分支天然跳过;`useLayoutMenu.onSelect`/`onSelectL1` + `MenuSearch` 回车各加 `window.open` 分支;兜底图标 `ph:arrow-square-out`);**iframe** = `path` 为内部路径、`component` 为 URL(`buildRoutesForModule` 检测 URL 时注册 `namedPage(() => import('@/views/embed/iframe.vue'))`,URL 进 `meta.iframeSrc`,keep-alive 顺带保住 iframe 状态);菜单表单加「链接类型」radio 展示糖。完成后约定写进 `COMPONENTS.md`。⚠ `useAuthMenu.ts`/`COMPONENTS.md` 有在途未提交改动,开工前确认。

## 批次 B · 后端 S 级速赢

- [ ] **B1 异常日志** — `ExceptionLogFilter : IExceptionFilter`(注册在 AdminExceptionFilter 之后):非 `AdminException` 落 `sys_exception_log`(Path/HttpMethod/TraceId/ExceptionType/Message 截 2000/StackTrace 截 8000,审计 AOP 补操作人时间),**不置 ExceptionHandled**,500 照旧大声(ErrorCode.cs 既定纪律不动);写入尽力而为。`LogService` + `SysLogController` 加 `exception/page`、`DELETE exception`;前端日志页第三个 tab。纯复制 op/login 三件套。
- [ ] **B2 IEmailSender 邮件通道** — 严格镜像 ISmsSender:`Core/Security/IEmailSender`(`SendAsync(to, subject, htmlBody, ct)`);Services 层 `LoggingEmailSender`(默认)+ `SmtpEmailSender`(BCL SmtpClient,`TenonAdmin:Email` Options:Host/Port/From/User/Pass/Ssl;配了 Host 用 SMTP 否则日志)。零表零页面;要现代协议的消费者前置注册 MailKit 实现即接管。
- [ ] **B3 密码历史防重用** — 表 `sys_password_history`(UserId, PasswordHash);`IPasswordHistoryService`(TryAdd):`EnsureNotReusedAsync`(取最近 N 条逐条 `IPasswordHasher.Verify`——盐不同只能验明文)+ `AppendAsync`(写入裁剪到 N)。开关 `sys.security.password.historyCount`(默认 0=关)走 SecurityPolicyProvider「DB 优先、Options 兜底」通道;挂点:`PersonalService.ChangePasswordAsync`、`UserService.ResetPasswordAsync`,建号初始密码只记录不校验。ErrorCode 42025 PasswordReused。
- [ ] **B4 T-D7 文件引用根治(零 DDL)** — 秒传改「一引用一行」:`FileService.ChunkInitAsync/ChunkCompleteAsync` 命中同 hash 时不复用既有行,新插 `SysFile`(拷 StoragePath/Hash/Size),各引用方独立记录互删不影响;`FileGcService.ReclaimDeletedFilesAsync` 删盘前查同 StoragePath 是否仍有他行(ClearFilter 含未删),有则只硬删记录跳过 `storage.DeleteAsync`。幂等,与逐行 try/catch 兼容。
- [ ] **B5 服务器监控页** — `IMonitorService`/`MonitorService`(TryAddScoped):Environment / Process / GC.GetGCMemoryInfo / ThreadPool / DriveInfo,进程 CPU% 两次 `TotalProcessorTime` 采样(500ms);`MonitorController` + `[Module("Monitor")]`,`GET /api/v1/sys/monitor/server`;前端新页 + 菜单种子。只报进程与主机基础面,全量指标留给消费者观测栈(OTel 是既定可选包方向)。

## 批次 C · DiffLog + 缓存管理

- [ ] **C1 实体变更差异日志** — 已核验 SqlSugar 5.1.4.198:有 `EnableDiffLogEvent`/`OnDiffLogEvent`、**无**全局自动开关 → 只能在 `SqlSugarRepository<>` 咽喉逐命令启用。`IDiffLogged` 空接口(放 SqlSugar 层实体标记处,与 ISoftDelete 同处)按实体 opt-in(update/delete 需反查前像有性能成本,不能全量默认开);内核给 SysUser/SysRole/SysMenu/SysOrg/SysConfig 打标;`SqlSugarSetup` 挂 `Aop.OnDiffLogEvent` 落 `sys_diff_log`(TableName/DiffType/BeforeJson/AfterJson,写入 try/catch 吞——同 op log 纪律;落库 insert 不启用 DiffLog,无递归);页面挂日志页新 tab(`SysLogController` diff/page + DELETE diff)。
- [ ] **C2 缓存管理页** — `ICacheProvider` 加默认接口方法 `SearchKeysAsync(prefix, limit)` 默认空(DIM 先例:IncrementAsync,不破第三方实现);Memory 用 `MemoryCache.Keys` 覆写、Redis 用 SCAN 覆写;`CacheController` + `[Module("Cache")]`:按前缀列键 + 逐键清除。**明确不做取值查看**——缓存里有 SMS OTP/MFA 挑战,看值端点 = 给管理员开旁路读 OTP 的口子(XiHan 的展示值做法在这里是安全洞)。

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
| 缓存取值查看 | 安全洞(见 C2) |
| 演示模式写保护 | **已存在**(DemoModeFilter + 41002),勿重复造 |

## 未排期备忘

- 定时任务调度中心:自写 5 段 cron(~100 行,不做秒级/L/W/#)+ `IAdminJob` TryAddEnumerable + `JobSchedulerService : BackgroundService`(复用 FileGcService 骨架与 ICacheProvider.IncrementAsync 多副本租约);表 sys_job / sys_job_log;ErrorCode 46xxx 段。设计已成稿,待排期。
- SignalR 实时通知:共享框架零依赖;Core `IRealtimePublisher` + Services no-op 默认(照 LoggingSmsSender),AspNetCore 层 `options.Realtime.Enabled` 时 MapHub + JwtBearer OnMessageReceived 读 query token;notice-changed / force-logout 推送,30s 轮询保留兜底;进程内事件总线跨副本推不到 → 轮询兜底退化为最终一致,backplane 留消费者。
- `TenonAdmin.Excel` 卫星包(Magicodes.IE,`rebuild-design.md:165` 已定稿方向):用户导入 + 同步导出,经 ApplicationAssemblies 通路挂入,内核零改动。
- 多租户消费者侧 skill 文档(replace-service 姊妹篇):字段级 = 前置替换 IDataScopeProvider + DataEntity 子类基座;库级 = 多 ConfigId。

## 轮次日志

### 第 0 轮 — 建账。三方盘点收敛为 A(5)/B(5)/C(2)/D(2)/E(1) 共 15 条;SMS/MFA 特性已落 cd88d78,非本台账范围。工作区另有在途未提交改动(用户详情页 DetailPage/useTabTitle/detailRoutes,涉 tabs.ts/useAuthMenu.ts/COMPONENTS.md)——与 A2/A5 重叠,开工前须确认处置。NEXT: A1 路由加载进度条。
