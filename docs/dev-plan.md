# TenonAdmin 开发计划(滚动更新)

> 设计单源:同目录 `rebuild-design.md`(§ 引用均指向它)。
> 本文件回答三个问题:**做到哪了、怎么干活、下一个任务是什么**。每完成一个任务更新一次。
> 最后更新:2026-07-07(T3 多机构数据范围完成)

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

### T4 会话与 Token 完整模型(§15)← **下一个**
`sys_session` / `sys_refresh_token`(存 hash);refresh 轮换+复用检测;登出;在线用户列表/强退;`[RolePermission]` 管道加 session 状态校验(强退即 401);单端/限并发模式;CreateUserId/UpdateUserId AOP 填充接当前用户上下文。
**验收**:刷新换发新对且旧 refresh 失效;强退后原 token 立即 401;重放旧 refresh 触发风险吊销。

### T5 字典 + 系统配置(§4)
`SysDictType`/`SysDictItem`/`SysConfig`;带缓存,变更失效(顺手落 Channels 事件总线,§2.2 替代表)。
**验收**:CRUD + 改字典后再查走新值(缓存确实失效)。

### T6 日志(§4)
`[OperationLog]` 特性 + 过滤器自动记录(入参/耗时/结果码,敏感字段脱敏);登录日志(IP/UA 原文)挂进 AuthService 的 `OnLoginSucceededAsync`/失败路径;查询/清空接口。
**验收**:调用带特性接口后库里有操作日志且密码字段已脱敏;登录成功/失败都有登录日志。

### T7 本地文件上传(§4/§14)
`sys_file`;`IFileStorage` + 本地实现;后缀白名单/大小限制/文件名重写/路径穿越防护;下载/列表。
**验收**:传允许后缀成功、禁止后缀被拒、`../` 路径攻击被拒。

### T8 横切收尾(§6/§12/§14)
统一返回 `IResultFilter`(控制器裸返回 DTO 自动包信封,替代现在手动 `Result.Ok`);内置 OpenAPI(`AddOpenApi`,产出 openapi.json);SVG 验证码(`ICaptchaProvider`,接进 `ValidateCaptchaAsync`);登录失败锁定(LoginLock);`RateLimiter` 限流;标准 HealthChecks 替换极简 /health;`Api:RoutePrefix`/`DisabledModules` 配置化;个人中心(改密码/改资料)。

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
- [ ] 事件总线(Channels)未建——T5 顺手落
- [ ] `OrgService.UpdateAsync` 拒绝"父指向自己"时复用了 `OrgNotFound`(语义略偏,应为专用码或"非法父级");阶段二审查处理
- [ ] `UserService` 默认初始密码为固定常量 `Tenon@123456`——T8 接密码策略时改可配置默认 + 首次登录强制改密
- [ ] `.slnx` 是 .NET 10 新方案格式(非 .sln),IDE 兼容性关注一下
- [ ] docker-compose / Dockerfile 未建(M3,§11)
