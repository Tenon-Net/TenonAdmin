# TenonAdmin 开发计划(滚动更新)

> 设计单源:同目录 `rebuild-design.md`(§ 引用均指向它)。
> 本文件回答三个问题:**做到哪了、怎么干活、下一个任务是什么**。每完成一个任务更新一次。
> 最后更新:2026-07-12(**M3 前端管理页全量 + 配置中心上线**——`web/` 系统管理各页(配置/日志/字典/岗位/会话/文件/机构/用户/角色 RBAC 闭环)+ 通用组件套件 + 前端 CI 落地;「改配置不改代码」配置中心四类(基础/安全/上传/限流)+ 密码策略端点 + 密码强度共享组件全部 push origin/dev。详见 §4 M3。下一步候选见 §6 TDD 待开发清单)
> 早期基线:2026-07-07 Phase 2(2a 自审 34 发现全处置 + 2b RateLimiter/MySQL CI)+ M1.5 多应用门户后端,详见 §4。

---

## 1. 当前状态

**M0 完成、M1 进行中**。仓库 https://github.com/DotNet-MoYu/TenonAdmin ,开发在 **`dev` 分支**,稳定后 PR 合 `main`。

已端到端验证(MinimalHost 实跑,非推断):

- 三行 `Program.cs` 启动 → 零配置 SQLite 自建库 → CodeFirst 建表(2 实体)→ 种子幂等(二次启动 0 插入)
- 首启打印随机超管密码(仅建号那次)→ `POST /api/v1/auth/login` 拿 JWT
- 无 token 访问 `[RolePermission]` 接口 401;带 token 200;伪造 token 401;错密码返回 `40001` 统一信封
- 重启后旧 token 仍有效(`./data/dev-jwt.key` 持久化)

## 2. 工作约定(每个任务都遵守)

1. **分支**:一切开发提交到 `dev` 并推送;`main` 只接 PR。
2. **提交**:Conventional Commits(`feat(core):` / `fix(auth):` …),**一个任务一个提交**,提交信息里写清"做了什么 + 验证了什么"。
3. **验证**:任务完成的标准是**跑出来的证据**,不是"代码写完了"——
   - 改动涉及启动/接口:MinimalHost 实跑 + curl 验证;
   - 非平凡逻辑(分支/循环/安全路径):留一个 .NET 10 file-based app 自检脚本(`#:project` 引项目;
     **引用 SqlSugar 的脚本必须加 `#:property PublishAot=false`**,否则 Reflection.Emit 报错)。
4. **代码风格**:注释详细(XML doc 写清"为什么"与设计出处 §x);刻意简化处标 `// ponytail: 现状 + 升级路径`。
5. **架构纪律**(违反即打回):
   - 核心四包运行时依赖只允许 `SqlSugarCore` + `Microsoft.*`(§2.3);
   - 框架服务显式 `TryAdd`(用户前置注册即替换),服务类 public、方法 virtual、长流程拆小步(§5);
   - 禁硬编码字符串:错误码走 `ErrorCode` 枚举 + `[MsgKey]`,claim 名走 `TokenClaimNames`,后端不写死中文文案(§6/§13);
   - 种子数据必须固定 Id(幂等锚点);
   - 安全默认拒绝:新接口默认挂 `[RolePermission]`,放行是显式例外(§14)。

## 3. 已完成(dev 分支提交序)

| 提交 | 内容 |
|---|---|
| `373d0f6` | walking skeleton:5 包分层 + 三行启动 + SQLite CodeFirst + /health |
| `1076e24` | 目录重构:后端独立 `backend/` 一层 |
| `5a84be7` | P0:NuGet.config 锁 nuget.org(消 NU1507);SQLitePCLRaw→3.0.3(消 NU1903 高危) |
| `35b2ba4` | Core:ErrorCode+[MsgKey] / AdminException / Result 信封 / 雪花 ID / 安全扩展点接口 |
| `5d9f605` | SqlSugar:IRepository 开放泛型仓储 / ISeedData 幂等种子 / AOP 审计填充 / 软删过滤器 |
| `0352a58` | Services:SysUser / PBKDF2 哈希(自描述格式) / 超管种子(随机密码打印) |
| `c46fa67` | M1 认证闭环:AuthService 模板方法 / JWT 签发+验证 / [RolePermission] / 统一异常信封 |
| `de53a72` | T1 RBAC 纵切:SysRole/SysMenu/关联表 + ICacheProvider/MemoryCacheProvider + RbacPermissionProvider 权限码聚合 + RbacService 授权与缓存失效 + 种子(默认角色/基础菜单)。自检 7/7 通过 + MinimalHost 冒烟 |
| `cd68d43` | T2 用户/机构/职位 CRUD:PagedList+ToPagedListAsync + DataEntity 基类 + SysOrg(树)/SysPosition + 用户全套(增删改查分页/重置密码/启停用/角色分配,守住不出哈希/不提权/超管保护)。自检 19/19 + MinimalHost 全套 CRUD 冒烟 + 清偿 T1 的 HTTP 403→200 |
| `e3a9f7e` | T3 多机构数据范围:DataScopeType 五种范围 + SysRoleDataScope + IDataScopeProvider(多角色合并/机构树展开/按用户缓存)+ SqlSugar 全局过滤器(IOrgScoped 接口匹配)+ IDataScopeContext(HttpContext.Items/AsyncLocal)+ ICurrentUser + 授权管道解析写入。自检 12/12(真跑 ORM 过滤器)+ MinimalHost 回归 |
| `ac7ae3a` | T4 会话/令牌模型:sys_session/sys_refresh_token(存哈希)+ SessionService(轮换/复用检测/强退/在线列表/单端限并发)+ /auth/refresh /auth/logout + SysSessionController + [RolePermission] 会话校验 + 审计字段 AOP 填充。自检 14/14 + 2/2 + MinimalHost 三条验收全绿 |
| `48d8cfb` | T5 字典/配置:SysDictType/SysDictItem/SysConfig + DictService/ConfigService(读穿透缓存 + 变更即失效 + 发事件)+ Channels 进程内事件总线(IEventBus/ChannelEventBus + 变更日志订阅者)+ 字典/配置菜单与种子。自检 16/16 + MinimalHost HTTP 冒烟 12/12(含经 HTTP 缓存失效验收) |
| `7c885ec` | T6 日志:SysOpLog/SysLoginLog + [OperationLog] 特性 + 全局 OperationLogFilter(入参脱敏/耗时/结果码/操作人/IP/UA,opt-in)+ SensitiveDataMasker(按字段名递归打码)+ AuthService 加 OnLoginFailedAsync 钩子记成功/失败登录日志 + LogService(写入尽力而为、清空硬删)+ IResultEnvelope 免反射读码 + ICurrentUser 补 IP/UA + SysLogController + 日志菜单种子。masker 自检 10/10 + MinimalHost 冒烟 10/10(操作日志脱敏且明文不泄漏、登录成功/失败均留痕、401 回归) |
| `c7216d1` | T7 本地上传:sys_file + IFileStorage 扩展点(LocalFileStorage,路径穿越围栏)+ FileService 三道关(非空/后缀白名单不信 CT/大小上限 + 文件名重写 {日期}/{GUIDv7})+ AdminUploadOptions + 44xxx 错误码 + SysFileController(上传/下载/列表/删)+ 上传挂 [OperationLog] + 文件菜单种子;附带修 .gitignore(`**/wwwroot/upload/`)。storage 自检 10/10 + MinimalHost 冒烟 8/8(png 上传→下载往返无损、exe 拒 44003、列表含原名、操作日志含『上传文件』、401 回归) |
| `0635a1f` | T8a 统一返回兜底过滤器 + 个人中心:ResultEnvelopeFilter(裸 DTO 自动包信封,内置仍显式 Result<T> 保 OpenAPI 契约,TryWrap 纯函数)+ PersonalController([Authorize] 看/改资料/验旧改密,限当前用户)。envelope 自检 8/8 + MinimalHost 冒烟 14/14 |
| `a54c8d7` | T8b 登录失败锁定:ILoginLockService/LoginLockService(账号失败计数进缓存,达阈值锁定窗口内拒登,TTL 滑动过期)+ 接进 AuthService(CheckLoginLockAsync 最前置、成功重置、仅密码错计入)+ AdminSecurityOptions.LoginLock + CacheKeys.LoginFail。自检 6/6 + MinimalHost 冒烟 7/7。RateLimiter 暂缓(见 T8b 注) |
| `849af1d` | T8c SVG 验证码:ICaptchaProvider 扩展点 + SvgCaptchaProvider(纯字符串 SVG 零依赖)+ CaptchaService(签发缓存 + 一次性校验)+ 接进 AuthService.ValidateCaptchaAsync + GET /auth/captcha + LoginInput 加验证码字段 + AdminSecurityOptions.Captcha(默认关)。自检 12/12 + MinimalHost 冒烟 6/6 |
| `622f19a` | T8d-i OpenAPI + HealthChecks:AddOpenApi/MapOpenApi(/openapi/v1.json,契约含 Result 信封)+ 标准 MapHealthChecks(/health)替代极简 MapGet;修高危 CVE(Microsoft.OpenApi 2.0.0→2.7.5,NU1903)。MinimalHost 冒烟 7/7,build 0 警告 |
| `dfe32ab` | T8d-ii 模块禁用:[Module] 标记 + DisabledModuleConvention(命中 Api:DisabledModules 的控制器摘除→404)+ AdminApiOptions;Dict/Config/Upload/Log 可禁,核心不可禁。MinimalHost 冒烟 7/7 |
| `4d67377` | T9a 单元测试转正:`backend/tests/TenonAdmin.Tests`(xunit)+ 5 个纯逻辑 scratchpad 自检转正式测试(SensitiveDataMasker/ResultEnvelopeFilter/LoginLockService/CaptchaService/LocalFileStorage)。`dotnet test` 27/27 通过 |
| `0e3fdd2` | T9b 集成 + 六件套 + CI + **修用户程序集断点**:AddTenonAdmin 之前写死只扫内置程序集,`options.ApplicationAssemblies`(§5.7 用户扩展点)未接进→用户实体不建表、控制器不注册;现接进实体扫描 + AddApplicationPart。新增 `TenonAdmin.TestHost`(示例用户 App)+ AdminAppFactory;§8 六件套(ReplaceService/OverrideAuthStep/DisabledModule/CustomController/CustomSeedData/DataScope)+ 认证全流程(登录/错密/刷新/锁定);CI `backend-ci.yml`(build+test,SQLite)。`dotnet test` 37/37 通过 |

## 4. 任务队列(按依赖序;每个任务 = 一次会话可完成的纵切)

### T1 RBAC 纵切 ✅ 完成(`de53a72`)
实体 `SysRole` / `SysMenu`(目录/页面/按钮三级,§16)/ `SysUserRole` / `SysRoleMenu`;
角色-菜单授权服务;`IPermissionProvider` 真实现(按钮权限码 = 菜单表里的路由码,聚合用户全部角色,进 `ICacheProvider` 缓存——缓存抽象也在本任务落:`ICacheProvider` + `MemoryCacheProvider` 默认实现,§5.5);种子:默认角色 + 基础菜单。
**验收**:自检脚本 `backend/scratchpad/t1-rbac-check.cs` 直跑 provider+service 7/7 通过(默认拒绝/授权命中/加授后缓存即时失效/收回归零/双侧失效);MinimalHost 冒烟回归通过(超管绕过、无 token 401)。
> 遗留:非超管用户经 **HTTP** 的 200/403 完整走查依赖用户/角色 CRUD(T2 建),届时纳入累计冒烟;正式 HTTP 集成用例进 T9(WebApplicationFactory)。[RolePermission] 过滤器本身(`codes.Contains`)M1 已验、T1 未改动。

### T2 用户 / 机构 / 职位 CRUD ✅ 完成(`cd68d43`)
`SysOrg`(树)/ `SysPosition`;用户 CRUD(增删改查分页、重置密码、启停用);`DataEntity` 基类(+CreateOrgId,§5.6)落地;分页模型 `PagedList`。
**验收**:自检 19/19(含账号唯一/密码重置/超管护栏/软删);MinimalHost curl 走完全套 CRUD + 用户挂机构/职位/多角色 + 软删归零;并清偿 T1 的 HTTP 403→200(非超管授权前 403、授权后 200、按码隔离)。

### T3 多机构数据范围(招牌能力,§4/§6)✅ 完成(`e3a9f7e`)
`SysRoleDataScope`;`IDataScopeProvider` + SqlSugar 全局过滤器(按 `IOrgScoped` 接口匹配);五种范围(全部/本机构/本机构及以下/仅本人/自定义)。附带落地 `ICurrentUser` + `IDataScopeContext`(T4/T6 复用)。
**验收**:自检 12/12 直压 SqlSugar 全局过滤器——切换范围可见行集随之变化(§8 测试点 2:两机构用户查同一列表得不同数据集),五种范围解析正确 + 改范围即缓存失效;MinimalHost 回归通过。
> 设计修正见 §6:v1 数据范围**按角色**全局过滤(非旧版 role×api 接口级);`sys_user` 本身非 DataEntity 不走通用过滤(其机构维度是 OrgId 特例)。

### T4 会话与 Token 完整模型(§15)✅ 完成(`ac7ae3a`)
`sys_session` / `sys_refresh_token`(存 hash);refresh 轮换+复用检测;登出;在线用户列表/强退;`[RolePermission]` 管道加 session 状态校验(强退即 401);单端/限并发模式;CreateUserId/UpdateUserId AOP 填充接当前用户上下文。
**验收**:自检 14/14(轮换/旧串失效/重放整会话吊销/强退失活/单端/限并发)+ 2/2(审计填充);MinimalHost 三条验收全绿(刷新换发新对且旧 refresh 失效;强退后原 token 立即 401;重放旧 refresh 触发风险吊销)+ 登出 + 非超管累计回归。

### T5 字典 + 系统配置(§4)✅ 完成(`48d8cfb`)
`SysDictType`/`SysDictItem`/`SysConfig`;DictService/ConfigService 读穿透缓存 + 变更即失效 + 发事件;
顺手落 Channels 进程内事件总线(`IEventBus`+`ChannelEventBus`,Core;单例后台派发、订阅退订)+ 变更日志订阅者;
种子:通用状态字典(启用/停用)+ 站点标题配置。
**验收**:自检 16/16(总线投递/退订、字典&配置读穿透缓存证明生效并变更失效走新值、级联软删、唯一码守护)
+ MinimalHost HTTP 冒烟 12/12(认证回归 + 字典/配置 CRUD + 经 HTTP 缓存失效 + 种子可读 + 事件订阅者日志)。

### T6 日志(§4)✅ 完成(`7c885ec`)
`[OperationLog]` 特性 + 过滤器自动记录(入参/耗时/结果码,敏感字段脱敏);登录日志(IP/UA 原文)挂进 AuthService 的 `OnLoginSucceededAsync`/失败路径;查询/清空接口。
**验收**:masker 自检 10/10;MinimalHost 冒烟 10/10——带 `[OperationLog]` 的新增用户接口写入操作日志且入参密码脱敏为 `***`、明文不泄漏;登录成功(code 0)与失败(40001)均有登录日志;无 token 401 回归。
> 设计说明:登录失败在 `AuthService.LoginAsync` 用 try/catch(AdminException) 统一走 `OnLoginFailedAsync` 钩子(默认钩子写登录日志),覆写登录流程的用户如需保留日志记得 `base.` 调用。日志表 `SysOpLog`/`SysLoginLog` 继承 `BaseEntity` 只为复用雪花 Id + CreateTime 自动填充,无软删语义,"清空"走 `Db.Deleteable` 硬删。写入路径尽力而为(吞异常只告警),不因日志失败破坏业务/登录。

### T7 本地文件上传(§4/§14)✅ 完成(`c7216d1`)
`sys_file`;`IFileStorage` + 本地实现;后缀白名单/大小限制/文件名重写/路径穿越防护;下载/列表。
**验收**:storage 自检 10/10(存读删往返 + `../`/多级`../`/绝对路径/读逃逸全拒);MinimalHost 冒烟 8/8——允许后缀(.png)上传成功且下载往返无损、禁止后缀(.exe)拒 44003、列表含原始名、操作日志含『上传文件』(联动 T6)、无 token 401 回归。
> 设计说明:后缀按原始名判定、不信 Content-Type(§14);文件名重写为 `{日期}/{GUIDv7}{后缀}`,原始名绝不进物理路径(天然免穿越),`LocalFileStorage.Resolve` 再断言落在存储根内做纵深防御。软删只隐藏记录、物理回收留 v1.x 清理任务。`**/wwwroot/upload/` 才能忽略 samples 宿主下的上传物(中间斜杠模式会被锚定到根)。

### T8 横切收尾(§6/§12/§14)—— 8 子特性,拆 4 小轮
一个队列条目实为 8 个横切特性;按 loop「每轮聚焦一件、独立可验证」拆成 4 小轮依次做:
- **T8a ✅ 完成(`0635a1f`)**:统一返回兜底过滤器(`ResultEnvelopeFilter`,裸 DTO 自动包信封,内置仍显式 `Result<T>` 保 OpenAPI 契约)+ 个人中心(改密码/改资料)。
  > 设计修正:原设计「全面裸返回替代手动 Result.Ok」与 OpenAPI 子项打架(契约会丢信封结构),经确认改为「兜底过滤器 + 内置显式」两全。
- **T8b ✅ 完成(`a54c8d7`)**:登录失败锁定 `LoginLock`(账号级,接 `AuthService` 前置步 + 成功重置 + 仅密码错计入)。
  > RateLimiter 暂缓:ASP.NET `RateLimiter` 需 `app.UseRateLimiter()` 中间件,而三行零配置宿主的 `MapTenonAdmin` 只拿到 `IEndpointRouteBuilder`、无处插中间件;强塞要么破坏零配置、要么自造节流(违 ponytail)。账号级 LoginLock 已挡住爆破主向。IP 级限流留到:①宿主提供中间件挂载点(如 `IStartupFilter` 注入,待验证不与 auth 中间件次序冲突),或 ②文档化为用户显式 `app.UseRateLimiter()` 的 opt-in。归入后续处理,不阻塞 M1。
- **T8c ✅ 完成(`849af1d`)**:SVG 验证码 `ICaptchaProvider` 接进 `AuthService.ValidateCaptchaAsync` + `GET /auth/captcha` + 登录入参验证码票据。
  > 设计修正:`Captcha.Enabled` 默认 true→false(保零配置 API 直登;Web/生产 opt-in)。默认 SVG 机器可读(已注天花板),抗识别走扩展点。
- **T8d-i ✅ 完成(`622f19a`)**:内置 OpenAPI(`/openapi/v1.json`)+ 标准 HealthChecks(`/health`);附带修 Microsoft.OpenApi 高危 CVE。
- **T8d-ii ✅ 部分完成(`dfe32ab`)**:`DisabledModules` 已落(`[Module]` + 约定摘控制器)。
  > `Api:RoutePrefix`/`Version` 配置化**明确后置到 v1.x**:要去掉全部控制器硬编码前缀 + 全局前缀约定,深度耦合权限码与菜单种子(改动鉴权路径),而"重命名 /api/v1"是低频低价值需求。若确需,专门一轮:引入 Core `PermissionCode` 共享规范化 helper 供过滤器 + 种子共用(默认 `api/v1` 不错位),逐控制器改相对路由 + 非超管授权全量回归。

**T8 收尾结论**:8 子项中 6 项已落(统一返回信封 / 个人中心 / LoginLock / SVG 验证码 / OpenAPI / HealthChecks / DisabledModules),**RoutePrefix-Version** 与 **RateLimiter** 两项明确后置(理由分别见上与 T8b),均不阻塞 M1。进入 **T9 测试工程**。

### T9 测试工程(§8——产品承诺,发布前必须)✅ 完成(`4d67377` + `0e3fdd2`)
- **T9a**:`backend/tests/TenonAdmin.Tests`(xunit),5 个纯逻辑 scratchpad 自检转正式测试。
- **T9b**:`TenonAdmin.TestHost`(示例用户 App:实体 + 种子 + 自定义控制器)+ AdminAppFactory(WebApplicationFactory);
  §8 可替换性六件套用例名照写死:`ReplaceService_ShouldUseUserImplementation` / `OverrideAuthStep_ShouldAffectLoginFlow` /
  `DisabledModule_ShouldRemoveBuiltInController` / `CustomController_ShouldOwnSameRouteAfterModuleDisabled` /
  `CustomSeedData_ShouldRunOnceAndBeIdempotent` / `DataScope_ShouldFilterByCurrentUserOrg`,+ 认证全流程;`dotnet test` 37/37。
- **顺带修真实缺口**:`AddTenonAdmin` 之前忽略 `options.ApplicationAssemblies`,用户业务模块不生效;已接进实体扫描 + AddApplicationPart。
- CI:`.github/workflows/backend-ci.yml`(build+test,SQLite)。
> 后置(follow-up,已在 workflow 注明):**MySQL 矩阵**——集成测试连接串现固定 SQLite,跑 MySQL 需加 MySqlConnector + 测试从环境变量读 DbType/连接串 + CI 挂 mysql 服务容器;独立小改动,不阻塞 T10。

### T10 NuGet 打包 ✅ 完成(`21fd4fe`)
5 包元数据入 `Directory.Build.props`(0.0.1-preview / Apache-2.0 / README 作包说明 / XML 注释随包 / Authors 等);
元包依赖链坐实"装一个包引全部";`backend-release.yml`(tag v*→按 tag 推导版本→pack→nuget push)。
验证:`dotnet pack` → 5 个 nupkg 0 警告;**洁净消费者工程从本地 feed 装 `TenonAdmin` → 三行 AddTenonAdmin/MapTenonAdmin 还原+编译通过**。
> 后置(follow-up):① `PackageIcon`(预览版暂缺,发稳定版前补);② `TenonAdmin.*` ID 前缀保留——首次发布后由 owner 在 nuget.org 手动申请(无法自动化);③ 首次真推需在仓库 Secrets 配 `NUGET_API_KEY`。

——以上 = **T1–T10 全部完成**。

### Phase 2a 自审 ✅ 完成(`9dd4b8c`…`325b471`)
7 维多代理全量审查(code-reviewer + security-reviewer,每条 3 反驳者对抗验证)产出 **34 条确认发现(0 P0 / 12 P1 / 22 P2)**,报告见 `docs/phase2-review.md`。**全部处置**:12 P1 全修(含并发变红、超管越权护栏、防爆破绕过、软删唯一键 500、JWT 生产 fail-fast、生产建表闸门、CreateOrgId 机构范围落地、CORS 缺失等);22 P2 中 18 修 + 3 文档化 + 1 记录为已知行为。测试 **37→62**,`dotnet build` 0 警告 / `dotnet test` 62/62,偶发变红消除。
> **RoutePrefix/Version 明确维持 v1.x 后置**(纠正:Phase 2 原计划曾把它列入本阶段,与 T8d-ii『低频低价值、深耦合鉴权路径』的结论冲突,以 T8d-ii 为准)。

### Phase 2b ✅ 完成(`4c26e07` + `8c80320`)
- **RateLimiter**(`4c26e07`):`AdminSecurityOptions.RateLimit` 按客户端 IP 固定窗口——全局宽松档(默认 300/60s)+ 认证端点(`/api/v1/auth/*`)更严档(默认 20/60s),默认启用。经 Phase 2a 落地的 `TenonAdminMiddlewareStartupFilter` 挂 `UseRateLimiter`(CORS→RateLimiter→路由,均全局策略不依赖端点元数据,与自动插入的认证中间件次序不冲突);命中出 429 + 统一信封(`TooManyRequests=40008`)+ `Retry-After`。测试 62→65;MinimalHost 实跑验证(正常登录放行、认证端点洪泛 429 信封、`/health` 走宽松档不误伤)。
- **MySQL CI 矩阵**(`8c80320`):`MySqlConnector` 仅测试项目直接引用(版本对齐 SqlSugarCore 传递的 2.2.5,核心四包零第三方运行时依赖不变);`TestDb` 助手按环境变量选库(默认 SQLite,本地不变;MySQL 按 identity 派生独立库、自动建/删);`AdminAppFactory`/`DataScopeTests` 改走 `TestDb`;`backend-ci.yml` matrix `db=[sqlite,mysql]` + `mysql:8.0` service。**CI 双腿均 success**(run `455f885`:`build-test (sqlite)` + `build-test (mysql)` 皆 green)——MySQL 腿首跑即过,ORM 层在 MySQL 上验证通过。
- **顺带修 CI 长期变红**(`455f885`):CI(ubuntu)自 T9b 起一直红而本地(Windows)全绿——`LocalFileStorageTests` 的 `C:/Windows/evil.txt` 只在 Windows 上是穿越;Linux 无盘符,`C:` 是普通目录名、落存储根内不构成穿越,`SaveAsync` 不抛致断言失败。改用两平台都 rooted 的 `/evil.txt`。**此前 dev-plan 各处"CI/测试通过"实为 Windows 本地跑,现 CI 首次真绿。**
> RoutePrefix/Version 仍维持 v1.x 后置(理由见 Phase 2a 注)。

——**Phase 2(2a 自审 + 2b 补做)全部完成,CI 双腿绿**。下一阶段:**M2 Vue 前端**(先 `DESIGN.md` + tokens 定稿,§7.1)。

### M1.5 多应用门户 · 后端增量 ✅ 完成(待提交)
需求补充:一个系统下多个各自独立的子系统,登录选/切应用、每应用独立菜单树、每用户默认应用(设计 §0 决策表 / §4 / §6 / §16 已同步)。
- **数据模型**(纯增量,CodeFirst 只增不改):新增 `sys_module`(`SysModule`,内置 `system` 不可删);`sys_menu` 增 `ModuleId`(仅顶级目录)+ 前端展示列 `Path/Component/Icon/Visible`;`sys_user` 增 `DefaultModuleId`。拒绝 SimpleAdmin 的 `MenuType.Module` 判别式(TenonAdmin 已刻意去判别式)。
- **模块访问权按菜单授权实时反推**:不建 `sys_role_module/sys_user_module` 派生表、不加缓存键(门户/登录时算,非热路径);复用 `RbacPermissionProvider` 同款授权链 + 短路;超管见全部启用模块。`MenuService.GetMyModulesAsync/GetMyMenuTreeAsync`(整表进内存上溯根目录取 ModuleId + 授权叶子祖先脚手架 + 按钮不入导航)。
- **权限码保持模块无关**(回归锁死的不变量):`RbacPermissionProvider` 未改,切应用只改侧边栏/路由,不改用户持有的 API 权限码。
- **端点**:`ModuleController`(`api/v1/sys/module` CRUD,`[RolePermission]`,默认不授默认角色);`PersonalController` 增 `GET /personal/modules`、`GET /personal/menu?moduleId=`、`PUT /personal/default-module`(设默认前校验访问权,否则 `ModuleAccessDenied` 42014)。错误码 42011–42014。
- **种子**:`DefaultModuleSeed`(内置 `system` 模块)+ `DefaultMenuSeed` 四顶级目录挂 `ModuleId=1`。
- **验证**:`dotnet build` 0 警告 0 错误;`dotnet test` **65→73 全绿**(新增 `ModuleCrudTests` 3 + `ModulePortalTests` 5:访问权反推/无授权空/超管见全部/菜单树按模块/默认应用设与拒 + 不变量)。集成测试经 `AdminAppFactory` 走真实 HTTP+CodeFirst+种子,等价冒烟。
> M2 前端补:app-switcher(登录选/切应用、拉 `/personal/menu` 重建动态路由)、模块管理页、菜单表单「所属应用」选择器(设计 §7.3/§7.4)。菜单授权 CRUD(`MenuController` create/update 带 `ModuleId`)属 M2 新建。旧 dev 库已有目录行不会被种子回填 `ModuleId`(种子只插不更)——dev 阶段重置库或手动 `UPDATE sys_menu SET ModuleId=1 WHERE Id IN (1,10,20,30)`。

### M2 · 设计首刀(DESIGN.md + tokens 单源)✅ 完成(2026-07-07)
视觉出自 **Claude Design** 工程「设计系统 Design Tokens」;因本地 CLI 无法 `/design-login`(DesignSync/WebFetch 均不通),改导出交接:导出稿留档 `web/design-mockups/design-tokens.dc.html`(JS-bundled),解码内嵌 `manifest`/`template` 拿到权威 token 定义后落地。
- **`web/src/styles/tokens.css`**:tokens 唯一色源。中性 10 级灰阶 + 主色四档(`#646CFF` 系)+ 语义色 base+浅底;角色令牌层(`--color-bg/text/border/fill/mask`)亮色在 `:root`、暗色在 `[data-theme="dark"]` 整体翻转;字号 12/13/14/16/20/24(正文 14/22)、间距 4px 网格、圆角 4/6/8、阴影三级。
- **`web/DESIGN.md`**:六节规范 + **token→Naive UI `GlobalThemeOverrides` 映射表**。
- **验证**:CSS 结构良好(括号平衡、亮暗块齐全);WCAG 对比度实测——主文字 13–16:1、次文字 ~7:1 过 AA;占位文字/白字-主色按钮 ~3.2–4.1 属 AA-large(品牌靛蓝天花板,已在 DESIGN.md §6 标注)。
> 下一刀 = **web/ 工程脚手架**:Vite+Vue3.5+Naive+Pinia+router+openapi-typescript,tokens 接进 `n-config-provider` 验证换肤,再按 §7.3 逐页 + M1.5 门户前端(app-switcher/模块管理页/菜单「所属应用」选择器)。

### M2 · 工程脚手架首版 ✅ 完成(2026-07-07,待提交)
计划见 `.claude/plans/docs-rebuild-design-md-...md`。**技术栈**:Vite 6 + Vue 3.5 + TS + **Naive UI 单套** + Pinia(+persist) + vue-router + **openapi-fetch/openapi-typescript**(弃 axios,依赖最轻)+ vue-i18n;显式 import(弃 unplugin,typecheck 洁净不依赖生成 dts);运行时图标统一 `@iconify/vue`。
- **设计单源对齐(Phase 0)**:按新原型 `web/design-mockups/design_handoff_rbac_admin/` 改 `tokens.css`(圆角 6/10/12/16、shadow-2 更柔、`--color-header-bg`、border-strong #D3D6DB)+ `DESIGN.md`(6 主色候选、236↔76 侧栏、密度 58/48、§7.1 派生规则权威化)。
- **请求层**:`api/client.ts`(openapi-fetch + Bearer 中间件 + 401 共享 Promise 刷新重放)+ `api/index.ts`(`unwrap()` 容忍 `Result<T>` 信封与 ProblemDetails 两形状 + `ApiError` 带 msgKey)+ `gen:api` 从 `/openapi/v1.json` 生成 `schema.d.ts`。CORS deny-all → **Vite dev proxy** `/api`+`/openapi`→:5000。
- **菜单驱动动态路由**:`import.meta.glob` + 菜单树叶子(type=2)映射 `views/**` → `addRoute('layout')`;刷新白屏守卫(auth store 易失、F5 重建)。**修真 bug**:守卫原按 `to.meta.public` 短路,深链未注册路由先命中 public 404 → 错显 404;改为按登录态+`routesReady` 判定。
- **门户(M1.5)**:`useModule` 单应用自动进/默认进/多应用选择器;顶栏切应用。
- **运行时主题**:`theme/mix.ts`(派生规则纯函数 + 自检)+ `naive-theme.ts`(tokens→GlobalThemeOverrides)+ `useTheme`;**主色 6 候选运行时切换 + 舒适/紧凑密度 + 明暗**,均 `app` store 持久化。渐变/发光仅登录页/英雄区。
- **示范页**:登录(英雄渐变按钮)、工作台、用户管理(`n-data-table`+`useTable`,真连 `/sys/user/page`)、个人资料/改密、模块选择器、404;i18n zh-CN/en-US(error.* 键=后端 msgKey)。
- **后端小改(前端必需)**:`DefaultMenuSeed` 加 1 行页面菜单(Id 15 用户管理,`Path=/system/user`/`Component=system/user/index`)——原种子只有目录+按钮、无页面节点,动态路由无处可去。
- **验证**:`vue-tsc --noEmit` 0 错 + `vite build` 通过;后端 `MinimalHost` 起真库(超管 `superAdmin`/`Tenon@2026`),浏览器实跑闭环:登录→单应用自动进→动态路由→工作台;点/深链 `/system/user` 表格真拉数据;**F5 深链无白屏**(守卫重建);明暗切换实测 `data-theme` 翻转 + `--color-primary` 按 accent 派生(#10B981→暗 #3bc698);accent/密度/语言持久化生效;stale token→401→刷新失败→自动登出回登录。
> 后续刀:后端补 `GET /personal/permissions`(暴露已有 `GetPermissionCodesAsync`)→ `v-auth` 真生效;fx 动画档 + 完整双布局 Dashboard(纯 SVG/CSS);§7.3 其余页(角色授权面板/菜单管理/字典/日志)+ 模块管理页 + 菜单「所属应用」选择器;前端 CI(lint+typecheck+build)。

### M3 前端管理页全量 + 配置中心 ✅ 完成(2026-07-09~11,均 push origin/dev)

M2 脚手架之后一次性把系统管理各页与「改配置不改代码」配置中心补齐,约 50 提交,按域归纳(逐提交见 `git log --since=2026-07-09`):

- **前端管理页**:配置管理(`327de66`,确立 ProTable CRUD 范式)/登录日志/操作日志/字典主从/岗位/在线会话/文件/机构树表/用户写侧 CRUD/**角色 RBAC 闭环**(`582bffe`:角色 CRUD + 授权菜单/数据范围 + `v-auth` 真生效)+ 批量删除。图标选择器/pro-table 抽成 npm 包消费(`tenon-naive-iconify-picker` / `tenon-naive-pro-table`)。
- **通用组件套件**(`61fa406` 起):FormContainer / useConfirm / StatusSwitch / 字典三件套(DictSelect/Radio/Tag)/ OrgTreeSelect / FileUpload / **PasswordStrength**(改密页 + 建用户页共用,`dd371b7`)。索引见 `web/COMPONENTS.md`。
- **前端 CI**(`b8cccf8`):lint + build(内含 `vue-tsc` 类型检查)。
- **配置中心**(`b932b3c` 分类 Tab 结构化表单起,GroupCode 组织,统一范式:参数存 `SysConfig`,后端强制点先读 DB 穿透缓存、`Options` 兜底,存值经 `saveBatch` 逐键即时失效):① 系统基础(站点标题)② **安全策略**(登录失败锁定 / 密码复杂度 / 会话令牌时长 / 登录验证码开关 `28a2db9`,经 `ISecurityPolicyProvider`)③ 上传限制(单文件大小 + 后缀白名单 `9a9136a`)④ **请求限流**(运行时可配 `1d12400`:单例快照 `RuntimeRateLimit` + 订阅 `ConfigChangedEvent` 刷新 + 阈值/窗口编进分区键即时生效)。
- **密码策略贯通**:`GET /api/v1/sys/config/password-policy`(`2747084`,`[ActiveSession]` 免权限码,复用 `ISecurityPolicyProvider.GetPasswordPolicyAsync`)→ 前端 PasswordStrength 组件 `onMounted` 拉真实策略、规则清单按策略动态构建(超管改 minLength/require* 后精确同步不漂移)。
- **安全加固**(`bf78b73`~`a02d771`):Redis 可选缓存包 / 门户菜单读缓存(代际计数失效)/ 权限码反向一致性锁(受权端点须有菜单节点)/ 三处缓存·会话失效兜底 / DataEntity 写路径数据范围守卫(默认安全)/ `ScanApplicationAssemblies` 退役 / 雪花 ID 低位 22→12bit(主键恒 < 2^53 JS-safe)。
- **其他**:业务脚手架模板 `dotnet new tenon-app`(`761c2e7`)/ org 子树克隆 / 机构分类字段 / 登录日志解析用户名·设备 / 会话 IP·UA / op-log 异常消息 / **PostgreSQL CI 腿**(`7fd4d0b`)/ SqlServer 中文存 nvarchar 修复(`da015e2`)。

> 提交语言约定改为**英文**(`9c2d320` 起,保留 conventional-commit)。配置中心进度另见项目记忆 `config-center-progress`。

## 5. 遗留小事(不阻塞,顺手处理)

- [ ] `BaseEntity` 暂在 SqlSugar 层(带 Sugar 特性保 Core 零依赖)——待定是否 Core POCO 化(§5.6),代码已标 ponytail(Phase 2a 审查结论:不阻塞,POCO 化需拆特性映射层,收益低,维持现状)
- [x] 雪花 `WorkerId` 固定 0——Phase 2a 已接 `TenonAdmin:Id:WorkerId` 配置(`AdminIdOptions`)
- [x] `EnableCodeFirstInProduction` 生产建表闸门——Phase 2a 已落(接 `IHostEnvironment`,生产需显式开启)
- [x] `./data`(SQLite/dev-jwt.key)相对路径——Phase 2a 已改 ContentRoot 相对
- [x] 事件总线(Channels)——T5 已落:`IEventBus`+`ChannelEventBus`(Core)+ 变更日志订阅者(Services)
- [x] `OrgService.UpdateAsync` 父指向自己复用 `OrgNotFound`——Phase 2a 已改专用码 `OrgInvalidParent`(42008)
- [x] `UserService` 默认初始密码固定常量——Phase 2a 已改可配置默认(`Security:DefaultInitialPassword`,默认 null→随机);首次登录强制改密仍留 v1.x
- [ ] `.slnx` 是 .NET 10 新方案格式(非 .sln),IDE 兼容性关注一下
- [ ] docker-compose / Dockerfile 未建(见 §6 T-D6)

## 6. TDD 待开发清单(下一批候选;红→绿→重构,每项先落失败测试再实现)

按「价值 ÷ 成本」排序。每项标注**先写什么测试**——测试红了、且红得对(为正确的原因失败),再写最小实现。

- [ ] **T-D1 首次登录强制改密**(安全,已确认未做)。`SysUser` 加 `MustChangePassword` 标志(重置密码/建号默认 true);登录成功返回需改密信号;改密后清标志。
  - 先写:集成测试 `Login_WhenMustChangePassword_ReturnsMustChangeSignal` + `ChangePassword_ClearsMustChangeFlag_ThenLoginNormal`。前端改密页拦截跳转。
- [ ] **T-D2 软删文件物理回收任务**(T7 遗留)。后台 hosted service 周期清理 `IsDelete=true` 的物理文件;存储根围栏不误删。
  - 先写:`FileGc_RemovesSoftDeletedPhysicalFile_AndLeavesLiveFiles`;越界路径断言不触碰。
- [ ] **T-D3 多副本分布式限流**(现 `RuntimeRateLimit` 为单实例进程内 `PartitionedRateLimiter`)。抽限流计数存储为扩展点,Redis 实现放 `TenonAdmin.Caching.Redis`。
  - 先写:`TwoInstances_ShareRateCounter_HitSameThreshold`(用共享计数后端)。单实例默认行为不变的回归。
- [ ] **T-D4 部署产物 docker-compose / Dockerfile**(M3,§11)。多阶段构建 + compose(app + mysql/pg)。
  - 先写:烟测脚本 `compose up` → 轮询 `/health/ready` 绿 + CodeFirst 建表·种子跑通(非单元测试,CI job)。
- [ ] **T-D5 RoutePrefix / Version 配置化**(v1.x 明确后置,深耦合鉴权路径)。引入 Core `PermissionCode` 规范化 helper 供过滤器 + 种子共用,逐控制器改相对路由。
  - 先写:`PermissionCode_Normalization_MatchesFilterAndSeed`(改前缀后权限码两侧一致)+ 非超管授权全量回归。
- [ ] **T-D6 验证码第二种类型**(YAGNI 未解除——`ICaptchaProvider` 扩展点已在,仅 SVG 一种实现)。**先确认真需要**行为/算术/滑块验证码再动;否则不做。
  - 先写(动了才写):新 provider `Register_SecondCaptchaType_ReplacesSvg` 走扩展点。

> 非 TDD 收尾项(打包/申请,无逻辑分支不留测试):`PackageIcon` 补图 + 稳定版发布;`TenonAdmin.*` NuGet ID 前缀 owner 手动申请;首次真推配 `NUGET_API_KEY` secret。
