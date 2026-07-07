# TenonAdmin 开发计划(滚动更新)

> 设计单源:同目录 `rebuild-design.md`(§ 引用均指向它)。
> 本文件回答三个问题:**做到哪了、怎么干活、下一个任务是什么**。每完成一个任务更新一次。
> 最后更新:2026-07-07(T8a 统一返回兜底过滤器 + 个人中心完成;T8 拆 4 小轮进行中)

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
- **T8b(下一轮)**:登录失败锁定 `LoginLock`(接 `AuthService.OnLoginFailedAsync`/策略步)+ `RateLimiter` 限流(登录等敏感端点)。安全向,自己把关。
- **T8c**:SVG 验证码 `ICaptchaProvider` 接进 `AuthService.ValidateCaptchaAsync`(默认 SVG,零绘图依赖)+ 登录入参加验证码票据。
- **T8d**:内置 OpenAPI(`AddOpenApi` 产出 openapi.json)+ 标准 HealthChecks 替换极简 /health + `Api:RoutePrefix`/`Version`/`DisabledModules` 配置化。

### T9 测试工程(§8——产品承诺,发布前必须)
`backend/tests/TenonAdmin.Tests`(xunit + WebApplicationFactory);把 scratchpad 自检转正;
可重写三件套用例名照 §8 写死:`ReplaceService_ShouldUseUserImplementation` 等 6 个;CI(GitHub Actions:build+test,SQLite+MySQL 矩阵)。

### T10 NuGet 打包
5 包元数据(版本 0.0.1-preview、License、README、icon)、`dotnet pack` 产物验证、tag CI 推 nuget.org、申请 `TenonAdmin.*` 前缀保留。

——以上 = M1+M3 后端部分打完。之后进 **M2 Vue 前端**(先 `DESIGN.md` + tokens 定稿,§7.1)。

## 5. 遗留小事(不阻塞,顺手处理)

- [ ] `BaseEntity` 暂在 SqlSugar 层(带 Sugar 特性保 Core 零依赖)——待定是否 Core POCO 化(§5.6),代码已标 ponytail
- [ ] 雪花 `WorkerId` 固定 0,多实例部署需配置项(`TenonAdmin:Id:WorkerId`)
- [ ] `EnableCodeFirstInProduction` 生产建表开关未接宿主环境判断(§12)
- [ ] `./data`(SQLite/dev-jwt.key)是进程工作目录相对路径,正式宿主应改 ContentRoot 相对
- [x] 事件总线(Channels)——T5 已落:`IEventBus`+`ChannelEventBus`(Core)+ 变更日志订阅者(Services)
- [ ] `OrgService.UpdateAsync` 拒绝"父指向自己"时复用了 `OrgNotFound`(语义略偏,应为专用码或"非法父级");阶段二审查处理
- [ ] `UserService` 默认初始密码为固定常量 `Tenon@123456`——T8 接密码策略时改可配置默认 + 首次登录强制改密
- [ ] `.slnx` 是 .NET 10 新方案格式(非 .sln),IDE 兼容性关注一下
- [ ] docker-compose / Dockerfile 未建(M3,§11)
