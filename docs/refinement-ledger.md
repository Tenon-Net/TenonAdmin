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
- [x] **A6 主色派生只留一份**(`bb2c60d`,第 15 轮) — `theme/naive-theme.ts:derivePrimary` 与 `composables/useTheme.ts:applyPrimaryVars` 各自手写了同一套主色四态派生,**含同样三个魔数**(暗色提亮 `0.18`、hover `+#FFF 0.16`、pressed `+#000 0.18`)。改一处忘另一处 = 裸 CSS 与 Naive 组件主色不同步,而没有任何东西会报错。把派生提进 `theme/mix.ts` 导出一个函数,两处都调它。**注意别把它说成 bug**:两处输出的大小写确实不一致(亮色下 accent 原样透传、暗色下经 `mix` 恒小写),但消费端一个是 CSS 变量一个是 Naive overrides,**都不区分大小写,今天观察不到**——这条的价值在消除重复,不在修 case。发现于 2026-07-20 React 模板分支的 R1 review(`archive/web-shared-extract` 的 `25f4908` 曾用「提到共享层」的方式修过它,共享层方向已推翻,方案不可照搬)。

- [x] **A7 反向 port 一条 i18n 合并用例**(`bf6c661`,第 15 轮) — `web-react/src/locales/index.spec.ts` 有一条「数组不当作对象往下钻」,`web/src/locales/index.spec.ts` 那 8 条里没有等价物。`deepMerge` 两边逐字相同(自包含之后是**有意重复**),所以这个失败模式 Vue 侧同样存在:`isPlainObject` 一旦漏掉 `!Array.isArray(v)`,数组会按下标逐项合并出四不像,而现有 8 条一条都不红。照搬那条用例即可。发现于 2026-07-20 React 模板 R4 的 review。

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

- [x] **D1 IExternalAuthProvider 框架 + 内核 OIDC(完成,25049dd→192017e;第 11 轮)** — Core:`IExternalAuthProvider`(ProviderCode / BuildAuthorizeUrl(state, redirectUri) / ExchangeAsync(code)→ExternalIdentity{Provider,Subject,DisplayName,Phone?,Email?}),TryAddEnumerable 多实现;表 `sys_user_external`(UserId+Provider+Subject 唯一);AuthController:`GET auth/external/providers`(点亮登录页现有占位按钮)/ authorize / callback / 个人中心绑解绑;回调用一次性票据换令牌对(令牌不进重定向 URL,票据走 CacheKeys);AuthService 模板方法 `LoginByExternalAsync`(ResolveIdentity→FindBinding→未绑定按配置自动开户或抛新码→复用现有建会话/发令牌步骤);内核内置 `OidcExternalAuthProvider`(JwtBearer 已传递依赖 Microsoft.IdentityModel.Protocols.OpenIdConnect,发现文档/JWKS/id_token 验签零新包,通吃 Keycloak/Entra/Authing);ErrorCode 40xxx 续号(未绑定/未启用)。开工前疑点(届时确认):OIDC 配置形态、未绑定默认策略(自动开户 vs 拒绝)、前端回跳路由。
- [x] **D2 卫星包 TenonAdmin.Auth.WeCom / DingTalk(完成,3e8f431 / 0c99072;第 11 轮)** — 裸 HttpClient 实现 IExternalAuthProvider(照 Caching.Redis 可选包成法),独立发包节奏。依赖 D1。**待真机联调**(URL 纯字符串已锁;token 交换/取用户需真实厂商应用验)。
- [x] **D-复查 双 reviewer(code + security)对抗审 + 修复(第 12 轮)** — 0 Critical/High;修 2 Medium(删用户遗留孤儿绑定致外部身份永久锁死;login 模式 state 未绑定发起浏览器→登录 CSRF)+ 6 Low(开户并发竞态 500→幂等、bind 漏 IsEnabled 复检、可选依赖 null guard、OIDC http 元数据生产 fail-closed、CallbackBaseUrl 生产必填、企业微信 secret-in-URL 记文档、开户查重注释纠偏)。核心面(PKCE/id_token 验签/一次性票据/绑定唯一/未绑定拒绝)审后判定 SAFE。后端 304/0/0。

## 批次 E · 杂项修缮(可选随手带)

- [x] **E1 岗位行拖拽漂移 → 修文档对齐现实(第 13 轮)** — 核后发现价值很低:岗位排序早已可用(`Sort` 字段可编辑、列表默认 `OrderBy(Sort)`、用户表单岗位下拉继承此序),拖拽只是"改 Sort 值"的顺手糖,对一个种子 6 条、极少改动的小列表不值一个新 `reorder` 端点 + 整表重编号事务。缺陷实为**文档漂移**:`COMPONENTS.md` 吹了不存在的 `row-draggable` + `positionApi.reorder` + `POST /sys/position/reorder`。改文档:三处(行排序说明/^0.3.1 能力注/范例页标注)对齐——岗位排序 = 可编辑 Sort;`row-draggable` 是 pro-table 的纯前端能力但本项目未接线,要拖拽需自行补端点。零代码面。

- [x] **E2 smoke-test 用带连字符的项目名 + Dockerfile 静态断言**(`d5478e1`,第 15 轮)— 补上 `0ad8605` 刻意留下的缺口(它自己的 commit body 写着"`smoke-test.ps1` 用名是 `Probe`,这类坑仍不在 CI 覆盖内")。脚手架用名 `Probe` → `probe-app`。**光改名不够**:该 job 从不 build 镜像,回退修复照样绿;所以另加一条对生成 Dockerfile 的静态断言(不得含被安全化的名字、任何字面 `.csproj` 必须真实存在),无 docker 也能红。**四次真跑**:①`probe-app` + 现行 Dockerfile 全绿(`probe-app.csproj` → `probe-app.dll` → 裸 `dotnet run` 出 `/health`,0 警告)②`probe-app` + 回退 Dockerfile → 新断言红 ③`Probe` + 回退 Dockerfile → **绿**,这才证明了改名是承重的(旧用名根本看不见这个坑,此前只是推断)④第 ③ 次顺带照出断言自身一个缺陷:无连字符时"安全化名"就等于真名,未加守卫会打在合法替换上、报出"contains 'Probe' but files use 'Probe'"的荒唐信息,已加守卫。
- [x] **E3 刷新 `dev-plan.md`**(`76ffe1b`,第 15 轮)— 该文件职责是回答「做到哪了 / 下一个是什么」,却把第一问答错了(还写着"还不能发,卡在发版链路")。§1 全部重写(测试数**现跑现记**:后端 320、web 60、web-react 723),§4 转历史,§5 从"v1 之后"改成真实候选池。顺带订正两条过时项:T-D7 早由 `3f4dd58` 根治、升级指南已在 `deployment.md`。

## 批次 F · 实时通知(SignalR,原第二批之一)

- [x] **F1 SignalR 实时通知(完成,第 14 轮)** — 把两处"实时性"从惰性/轮询提为即时推送:公告发布即时推 `notice-changed`(前端各端立刻自查未读,替代 30s 轮询)、会话吊销即时推 `force-logout`(被踢用户立刻登出,替代惰性 401)。**后端零新包**(SignalR 属共享框架)。Core `IRealtimePublisher`(三推送:按用户/全体/按会话)+ `AdminRealtimeOptions`(Enabled 默认关 + HubPath);Services `NoopRealtimePublisher`(默认空实现,TryAdd)+ 两处触发接线(`SessionService.RevokeAsync` 唯一汇聚点推 force-logout、`NoticeService.PublishAsync` 广播 notice-changed,均可选依赖注入源码兼容);AspNetCore `TenonHub`(纯推送,连接入 `user-{sub}`/`session-{sid}` 两组)+ `SignalRRealtimePublisher`(IHubContext)+ JwtBearer 补 `OnMessageReceived`(query access_token,仅 Hub 路径)+ 条件 `MapHub`/`AddSignalR`(开启时真实现前置压过 Noop)。前端 `@microsoft/signalr` + `useRealtime` composable(default.vue 挂载即连、初次失败静默退回轮询)+ NoticeBell 订阅刷新(保留 30s 兜底)+ 中英 `realtime.forcedLogout`;dev Vite `/hub` 代理(ws)。MinimalHost 样例开启演示。**决策存档 ADR-0003**(默认关纯增强 / 进程内无 backplane / 按会话精确 force-logout)。**验**:后端 **315/0/0**(RealtimeTests 5 例:默认 Noop、RevokeAsync 触发、PublishAsync 触发、开启→真实现+Hub 401、关闭→404;六件套补 `IRealtimePublisher`)+ typecheck/lint 绿 + **推送全链路 Node 冒烟通过**(前端同款客户端直连 MinimalHost:发公告收 notice-changed、踢会话收 force-logout,negotiate 无令牌 401/真令牌 200)。ponytail:未在测试工程引 SignalR.Client 跑真连接(避免测试专用包 + TestServer 传输易碎),接线由单测锁、全链路由 Node 冒烟证。

## 不做清单(有依据,防反复)

| 项 | 理由 |
|---|---|
| 多租户 | `rebuild-design.md:38` 整体不做。**替换点边界(2026-07-25 查证,别再照字面转述)**:替换 `IDataScopeProvider` 只能改「哪些 OrgId 可见」,改不了「按哪一列过滤」——全局过滤器硬打在 `SqlSugarSetup.cs:78` 的 `AddTableFilter<IOrgScoped>`,表达式只认 `CreateOrgId` / `CreateUserId`,而 `DataScopeResult` 的四个字段(`IsUnrestricted`/`OrgIds`/`IncludeSelf`/`UserId`)没有装 `TenantId` 的位置。故 `rebuild-design.md:391`「自定义数据隔离维度(如租户)」只在**租户 ≡ 机构子树**时成立;要独立租户列,消费者须自挂 `AddTableFilter<ITenantScoped>` + 自建租户上下文 + AOP 填列,与"前置替换一个接口"不是一回事。 |
| 多租户消费者侧 skill 文档(replace-service 姊妹篇) | **2026-07-25 退役**(原在「未排期备忘」)。写这篇等于把上一行那两条路当承诺发出去,而两条按原设想都不成立:①字段级见上行;②库级「多 ConfigId」内核没开口——`SqlSugarSetup.cs:46-67` 是**单个** `ConnectionConfig`(`ConfigId = "TenonAdmin"` 写死)交给 `new SqlSugarScope(config, …)`,多库形态要 `SqlSugarScope(List<ConnectionConfig>, …)`。今天真走得通的只有「租户 = 机构树根 + 现成 `OrgAndChildren` 范围」,零代码、已由 `tenon-example` 演示,撑不起一篇 skill。要改这个结论,先拿能跑的证据,别拿设计意图。 |
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

- ~~定时任务调度中心~~ → **2026-07-26 设计定稿,改由 `docs/scheduling-ledger.md` 驱动施工**(决策存档 ADR-0004)。本条旧稿三处被推翻并已在台账 §1 记账:①ICacheProvider.IncrementAsync 桶租约(对非幂等任务不安全)→ DB 选主 + 触发 CAS;②5 段 cron ~100 行 → 6 段秒级 + L/W/# 全套;③两张表 → 四张(加 sys_job_lock/sys_job_node)。`IAdminJob` TryAddEnumerable、复用 FileGcService 骨架、47xxx 段维持原案。
- ~~SignalR 实时通知~~ → **已做(批次 F,第 14 轮)**,见上。
- ~~`TenonAdmin.Excel` 卫星包~~ → 改由 `docs/excel-ledger.md` 驱动施工(库选型推翻 Magicodes,改 MiniExcel + OpenXml;契约落 Core,codec 进卫星包)。
- ~~多租户消费者侧 skill 文档~~ → **2026-07-25 裁定不做**,理由与证据进「不做清单」,见上。
- ~~⚠ 定时任务的归属两处打架,开工前先定~~ → **已定(2026-07-26,ADR-0004):进内核 + 自研零依赖**。推理链条按原句取了"留在内核才需要自写"这一支;`rebuild-design.md` 的卫星包条目与「v1.0 不做任务调度」措辞已同步订正。施工规格 `docs/scheduling-ledger.md`,批次 G1–G9 待排期。

## 轮次日志

### 第 15 轮 — 清边角债(A6 / A7 / E2 / E3)+ 多租户退役 · `/grill-with-docs` 走完设计审问后,用户拍板「继续写代码,使用者的事先放一边」→「多租户 skill 文档 + 清边角债」→「多租户那条砍掉进不做清单」。五条独立提交:`4bccebe`(多租户退役)/ `bf6c661`(A7)/ `bb2c60d`(A6)/ `d5478e1`(E2)/ `76ffe1b`(E3)。

**三处「查了才知道原方案不成立」**,都是本轮最值钱的产出:
1. **多租户备忘的两条路按字面都走不通**。字段级"前置替换 `IDataScopeProvider`"改不了过滤列——全局过滤器硬打在 `SqlSugarSetup.cs:78` 的 `AddTableFilter<IOrgScoped>`,而 `DataScopeResult` 四个字段没有装 `TenantId` 的位置;库级"多 ConfigId"内核没开口(`SqlSugarSetup.cs:46-67` 单个 `ConnectionConfig`)。真走得通的那条(租户 = 机构树根 + 现成 `OrgAndChildren`,零代码)备忘里反而没写。
2. **A6 的形状我一开始定错了**。原打算"按 Vue 自己的需要另设形状,不拷 React 的 `PrimaryRamp`"——读完才发现 `useTheme.applyPrimaryVars` 还派生了第四态 `light`(要 `--color-bg-container`),React 那个签名**恰好就是 Vue 两个调用点的并集**(它本就是从这两半移植过去的)。照搬即可。
3. **E2 的变异判据我一开始也定错了**。原写"回退 `0ad8605` 的 Dockerfile 修复,这条必须红"——但该 job 从不 build 镜像,回退不会红。补了静态断言才成立。

**判据纪律的两次现场兑现**:A7 先跑变异证明"现有 8 条看不见 `!Array.isArray(v)` 消失"(实测全绿,缺口是真的),补的用例在变异下红出的正是那个「四不像」(`['a','b','c']` 与 `['x']` 合成 `{"0":"x","1":"b","2":"c"}`,连数组都不是);E2 用第 ③ 次跑证明"旧用名看不见这个坑"——**此前这句只是推断**。

**未做/已知**:Naive 组件真实渲染新 overrides 未目视确认(无鉴权页面都不挂 Naive 按钮——登录皮肤与 404 页用的是原生 `<button>`),改由一条接线断言 + typecheck + 未动的 `common.primaryColor*` 映射兜住;裸 CSS 那一半已对 6 accent × 明暗真实浏览器实测 12/12。**收口**:五条尚未另起 review lane。

### 第 14 轮 — 批次 F(SignalR 实时通知,原第二批之一)· 从"未排期备忘"排下一步:四项(定时任务/SignalR/Excel/多租户文档)经三路只读探查核准落点后,用户选定先做 **SignalR**(用户可感价值最高、后端零新包、不碰正被另一会话重构的实体基类零冲突)。分 6 个文件原子 commit 落地:①Core `IRealtimePublisher`+`AdminRealtimeOptions`(e3e6ceb)②Services `NoopRealtimePublisher`+`RevokeAsync`/`PublishAsync` 触发接线(18dde1e)③AspNetCore `TenonHub`+`SignalRRealtimePublisher`+JWT query-token+条件 MapHub(625a425)④测试:默认 Noop/触发锁/Hub 401·404/六件套(a737efb)⑤前端 `@microsoft/signalr`+`useRealtime`+NoticeBell 订阅+i18n+vite `/hub` 代理(53e9d48)⑥文档+样例开关(本轮)。**验**:后端 **315/0/0**、typecheck/lint 绿、**推送全链路 Node 冒烟通过**(前端同款客户端直连 MinimalHost:发公告收 notice-changed、踢会话收 force-logout;negotiate 无令牌 401/真令牌 200)。三决策存档 `docs/adr/0003`。全程未触碰另一会话在途的 7 个 SqlSugar 文件(`AuditEntity`/#10)+ `OrgAuditEntityTests.cs`。**未排期余三项**:定时任务调度中心(须等实体基类重构落地)、Excel 卫星包、多租户 skill 文档。

### 第 13 轮 — 批次 E(E1 岗位拖拽)· 核实后裁定**不实现,改文档对齐现实**。岗位排序早已能用(可编辑 `Sort` + 列表 `OrderBy(Sort)` + 用户表单下拉继承此序),拖拽只是改 Sort 值的顺手糖,对种子 6 条的小列表不值一个 `reorder` 端点 + 重编号事务;真缺陷是 `COMPONENTS.md` 吹了不存在的 `row-draggable`/`positionApi.reorder`/后端端点(文档漂移)。修 `web/COMPONENTS.md` 三处(行排序说明改为可编辑 Sort、`^0.3.1` 能力注去掉 position reorder、范例页标注改"可编辑 Sort 排序"),并注明 pro-table 的 `row-draggable` 是未接线的纯前端能力。零代码面,纯文档。**精致化台账 A/B/C/D/E 全部收口。**

### 第 12 轮 — 批次 D 复查 · 双 reviewer(code-reviewer + security-reviewer,均 opus)对 `478e308..d08951f` 对抗审 + 逐条修。**0 Critical / 0 High**;核心安全面(PKCE S256、id_token 全量验签含 nonce、state/票据原子单消费、令牌不进 URL、开户事务原子、分层、替换性契约、`(Provider,Subject)` 唯一非邮箱、未绑定默认拒绝、绑定越权已挡)双方独立判 SAFE。**修 2 Medium**:① 删用户未清 `sys_user_external` → 绑定行悬挂占唯一位,该外部身份既登不进也绑不到新号 = 永久锁死;`UserService.Delete/DeleteBatch` 事务内加 `ISysUserExternalService.UnbindAllAsync`(可选依赖注入,逐行软删走 `_del_{id}` 回收释放唯一位)+ 回归测试。② login 模式 `state` 仅存服务端缓存、未绑发起浏览器 → 登录 CSRF(他人拼 (code,state) 诱受害者登入攻击者账号);`/authorize` 下发 `HttpOnly;SameSite=Lax` binder cookie,回调 `FixedTimeEquals` 比对(bind 模式已由 `[ActiveSession]` UserId 兜住,豁免)+ HTTP 级测试(无 cookie→40014、带 cookie→越门到 40016)。**修 6 Low**:并发首登开户竞态回滚后 re-resolve 复用赢家账号(幂等,不甩裸 500);bind 回调改用 `ResolveEnabledProviderAsync` 补 IsEnabled 复检;`LoginByExternalAsync` 顶部 guard 三可选依赖(未接线给 40013 而非裸 NRE);`OidcExternalAuthProvider` 加 `allowHttpMetadata`(仅 Dev true,生产强制 https 元数据 fail-closed);`CallbackUri` 生产未配 `CallbackBaseUrl` 即抛(不回退请求 Host);企业微信 `corpsecret` 走 query 记 `ponytail` 文档注记(厂商 GET 契约,无代码可改,日志已不落 URL);开户查重 `ClearFilter` 注释纠偏为防御性(软删经回收已释放 Account)。验:后端 **304/0/0**(+2 测试),纯后端零前端改动。ADR-0002 补 L6 注记。

### 第 11 轮 — 批次 D(OAuth/SSO)· `/grill-with-docs` 定三取舍(**未绑定默认拒绝 / 配置折中 / 含卫星包**,按 PC 扫码授权)后全批落地。**后端**:Core 契约 `IExternalAuthProvider`+`ExternalIdentity`+`AdminExternalAuthOptions`+`CacheKeys.OAuthState/Ticket`+ErrorCode 40013–40017(25049dd);内置 `OidcExternalAuthProvider`(AspNetCore,**零新包**——`ConfigurationManager`+`JsonWebTokenHandler` 走 JwtBearer 传递的 IdentityModel,PKCE S256,6e236b6);`SysUserExternal` 表 +`ISysUserExternalService`(绑定 CRUD + `sys.externalauth.{code}.*` 运营配置)+`AuthService.LoginByExternalAsync` 模板方法(解析→找绑定→未绑定 拒绝/开户→复用 `CreateTokenAsync` 尾链,a3b6e95);`ExternalAuthController` [Module] providers/authorize/callback/exchange/bindings(一次性票据、机密客户端、令牌不进 URL,d595eb6);`ExternalAuthTests` 5 例 + 六件套补 `IExternalAuthProvider` 可插(192017e)。**前端**:`externalAuthApi` + LoginForm providers 驱动按钮 + `views/oauth/callback.vue` + 个人中心 bindings 页/路由/下拉 + 中英 `oauth.*` + gen:api(003da00)。**卫星包**:`TenonAdmin.Auth.WeCom`(3e8f431)+`.DingTalk`(0c99072),仅引 Core+Microsoft.*、裸 HttpClient、TryAddEnumerable 按 Code 并存。**样例**:MinimalHost 装两卫星包(config 驱动 no-op)+ appsettings 示例。验:后端 **302/0/0** 全绿;typecheck/lint 绿;起 MinimalHost(demo OIDC+WeCom+DingTalk)浏览器实测——登录页 **3 个 SSO 按钮**(企业微信/钉钉/Demo SSO)+ 超管登录闭环 + 个人中心 **3 条绑定项**(均未绑定 + 绑定按钮)。三取舍 + 回调路由约定存档 `docs/adr/0002`。NEXT:批次 E(岗位拖拽,可选)或按用户指示。

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
