# Phase 2a 自审报告

> 生成于 2026-07-07。方法:7 维 finder(code-reviewer + security-reviewer)按维度扇出扫描 backend/src 四个有码项目(~5,900 行)+ 完整性补漏,每条发现 3 个独立反驳者对抗验证(P2 单票),多数票存活。70 个 agent,0 错误。
> 结果:**确认 34 条(0 P0 / 12 P1 / 22 P2)**,0 驳回,0 未验证。基线:`dotnet build` 0 警告、`dotnet test` 37/37(但见 P1-7,套件实为偶发变红)。

**处置图例**:FIX=本轮修复 · TEST=本轮补测试 · DOC=本轮文档化/订正措辞 · NOTE=记录为已知行为(ponytail 后置)。

## 处置结果(Phase 2a 收尾,2026-07-07)

**全部 34 条已处置**:12 条 P1 全修复;22 条 P2 中 18 修复 + 3 文档化(P2-2 框架 400/500 信封口径、P2-17 多节点缓存边界、P2-21 写路径越权约定)+ 1 记录为已知行为(P2-19 LIKE 通配,ponytail)。新增回归测试:37→62 用例(+25)。`dotnet build` 0 警告、`dotnet test` 62/62。基线偶发变红(P1-7)已消除,连跑稳定。

提交序(dev 分支):`fix(events)` 并发 → `fix(auth)` 防爆破 → `fix(user)` 超管/口令/事务 → `fix(data)` 软删唯一键 → `fix(aspnetcore)` JWT/授权基线 → `fix(sqlsugar)` 建表闸门/WorkerId → `fix(scope)` CreateOrgId → `fix(session)` 事务/并发 → `fix(core)` DataScopeResult → `feat(aspnetcore)` CORS/健康就绪 → `test` 回归补齐 → `docs` 契约/边界订正。

**后置(Phase 2b)**:RateLimiter(与 CORS 共用本轮落地的 `IStartupFilter` 挂载点)+ MySQL CI 矩阵;RoutePrefix/Version 配置化维持 v1.x。

## 处置汇总

| # | 级别 | 处置 | 标题 | 位置 |
|---|---|---|---|---|
| P1-1 | P1 | FIX | JWT 开发密钥自动生成未做环境门禁,生产缺配 SecretKey 时静默 fail-open | `JwtKeyResolver.cs:30` |
| P1-2 | P1 | FIX | 未认证/令牌过期返回裸 401,不套统一信封;40006 对其文档所述“令牌过期”场景不可达 | `RolePermissionAttribute.cs:32` |
| P1-3 | P1 | FIX | 裸返回(return dto)端点的 OpenAPI 契约不含 Result<T> 信封,破坏 §13. | `TenonAdminSetup.cs:97` |
| P1-4 | P1 | FIX | CORS 完全未实现,与设计 §12/§14 明列的 v1.0 安全基线项(Api:Cors)缺口 | `AdminApiOptions.cs:15` |
| P1-5 | P1 | FIX | 登录失败锁定可被账号大小写/尾随空白变体绕过(大小写不敏感数据库排序规则下) | `AuthService.cs:62` |
| P1-6 | P1 | FIX | 验证码一次性消费是非原子的先读后删,并发可重放同一张验证码;契约无原子取删故 Redis 也修不了 | `CaptchaService.cs:37` |
| P1-7 | P1 | FIX | Test suite is RED and flaky: unsynchronized List<IDi | `CacheChangeLogSubscriber.cs:34` |
| P1-8 | P1 | FIX | 用户更新接口可停用/篡改超级管理员,绕过 SuperAdminProtected 护栏 | `UserService.cs:96` |
| P1-9 | P1 | FIX | 新建用户/重置密码默认口令为已发布 NuGet 内核里的公开硬编码常量,且无首次登录强制改密 | `UserService.cs:20` |
| P1-10 | P1 | FIX | 软删保留唯一键行:删后重建同 Code/Account 触发原始 UNIQUE 冲突 500,且键永久锁 | `UserService.cs:67` |
| P1-11 | P1 | FIX | 生产建表安全闸门(EnableCodeFirstInProduction + 环境判断)完全缺失,默认在 | `DatabaseInitializer.cs:28` |
| P1-12 | P1 | FIX | 数据范围锚点 CreateOrgId 从不被 AOP 填充,机构维度数据范围对真实业务表恒返回 0 行 | `SqlSugarSetup.cs:68` |
| P2-1 | P2 | FIX | 个人中心端点用 [Authorize] 而非会话活性校验,强退/登出对其非即时生效 | `PersonalController.cs:14` |
| P2-2 | P2 | DOC | 框架生成的非 2xx 响应(400 绑定/校验、500 未捕获)绕过信封;50000 从不作为“统一出口 | `ResultEnvelopeFilter.cs:31` |
| P2-3 | P2 | FIX | ./data 与 ./data/dev-jwt.key 使用进程工作目录相对路径,应相对 Content | `JwtKeyResolver.cs:22` |
| P2-4 | P2 | TEST | 测试缺口:权限码强制匹配(已认证非超管无码 → 403/41001)零集成测试 | `RolePermissionAttribute.cs:63` |
| P2-5 | P2 | FIX | JWT 校验未收紧 ClockSkew,过期访问令牌有 5 分钟宽限 | `TenonAdminSetup.cs:71` |
| P2-6 | P2 | FIX | 默认拒绝仅靠约定,无 FallbackPolicy 兜底——漏挂特性的 action 即匿名公开 | `TenonAdminSetup.cs:79` |
| P2-7 | P2 | FIX | NameClaimType 硬编码字面量 "unique_name",未走 JwtRegisteredC | `TenonAdminSetup.cs:76` |
| P2-8 | P2 | FIX | OpenAPI 文档端点在生产环境无门禁暴露,泄露完整 API 契约 | `TenonAdminSetup.cs:109` |
| P2-9 | P2 | FIX | 健康检查缺 /health/ready 就绪探针与 DB/缓存依赖检查 | `TenonAdminSetup.cs:110` |
| P2-10 | P2 | FIX | DataScopeResult 不可序列化往返,宣传的 Redis 直接替换会破坏数据范围缓存 | `DataScopeResult.cs:23` |
| P2-11 | P2 | FIX | LoginLockService 失败计数读-改-写非原子,并发爆破下丢失更新削弱锁定 | `LoginLockService.cs:31` |
| P2-12 | P2 | FIX | OrgService.UpdateAsync 拒绝『父指向自己』时复用 OrgNotFound,语义错配 | `OrgService.cs:45` |
| P2-13 | P2 | FIX | Pbkdf2PasswordHasher.Verify returns true for a store | `Pbkdf2PasswordHasher.cs:55` |
| P2-14 | P2 | TEST | 测试缺口:缺 DefaultMenuSeed 权限码 与 BuildPermissionCode 输出的 | `DefaultMenuSeed.cs:19` |
| P2-15 | P2 | FIX | EnforceConcurrencyAsync 先查后删非串行化,并发登录可突破单端/限并发上限 | `SessionService.cs:145` |
| P2-16 | P2 | FIX | 会话开立非原子:refresh token 插入失败留下不可刷新的僵尸会话 | `SessionService.cs:34` |
| P2-17 | P2 | DOC | 多实例默认内存缓存下强退/权限吊销不跨节点生效,设计未声明单节点边界 | `SessionService.cs:51` |
| P2-18 | P2 | FIX | 用户+角色关联非原子:半写留下无角色幽灵用户,账号被占且无法干净重建 | `UserService.cs:80` |
| P2-19 | P2 | NOTE | LIKE 通配符 %/_ 未转义:关键字分页过滤返回超出字面子串的结果(功能性,非注入) | `UserService.cs:25` |
| P2-20 | P2 | FIX | 雪花 WorkerId 硬编码为 0,TenonAdmin:Id:WorkerId 配置项从未接入 | `SqlSugarSetup.cs:22` |
| P2-21 | P2 | DOC | 全局数据范围/软删过滤器只作用于查询,不覆盖 Update/Delete:DataEntity 落地后写 | `SqlSugarSetup.cs:62` |
| P2-22 | P2 | TEST | 测试缺口:会话强退→40006 信封、以及 OpenAPI 文档内容(信封结构 + DisabledMo | `AuthFlowTests.cs:52` |

## P1 详情(12)

### P1-1 · [FIX] JWT 开发密钥自动生成未做环境门禁,生产缺配 SecretKey 时静默 fail-open
**位置** `backend/src/TenonAdmin.AspNetCore/Security/JwtKeyResolver.cs:30` · cat=insecure-design / A02-A05 · 反驳票 0/3

**问题** Resolve() 的唯一分支条件只判断 SecretKey 是否配置,方法签名里没有 IHostEnvironment/IWebHostEnvironment,整个 TenonAdmin.AspNetCore 项目 grep 不到任何 IsDevelopment/IsProduction 门禁(已确认零命中)。因此 SecretKey 为空时,无论运行在 Production 还是 Development,都会走 line 48-51 生成 64 字节随机密钥并持久化到 ./data/dev-jwt.key,只在 line 53 打一条 LogWarning。这直接违反设计 §14 line 780『开发密钥只允许 Development 环境自动生成』——设计意图是生产缺 SecretKey 应 fail-fast,而实现是 fail-open。

**失败场景** 运维把应用部署到生产但忘记配置 TenonAdmin:Jwt:SecretKey(环境变量拼写错/漏配):应用不会拒启,而是静默用自动生成的 dev-jwt.key 长期运行,唯一提示是一条没人看的 Warning 日志。更具体的确定性故障:横向扩容部署(k8s/compose 多副本)且未挂共享 ./data 卷时,每个副本各自生成不同的 dev-jwt.key,副本 A 签发的 JWT 到副本 B 校验失败,负载均衡下用户被随机判 401、频繁掉线;若该 data 目录被误备份/打进镜像/暴露,密钥泄露即可伪造任意用户(含超管 sadm)令牌。

**建议** 把宿主环境传入解析逻辑,生产强制 fail-fast:
// 注册处 TenonAdminSetup.cs 改为传入环境
services.TryAddSingleton(sp =>
{
    var env = sp.GetRequiredService<IHostEnvironment>();
    return JwtKeyResolver.Resolve(options.Jwt, env,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JwtKeyResolver)));
});

// JwtKeyResolver.Resolve 内:SecretKey 为空且非 Development → 抛错,不给跑
if (string.IsNullOrEmpty(options.SecretKey))
{
    if (!env.IsDevelopment())
        throw new InvalidOperationException(
            "生产环境必须显式配置 TenonAdmin:Jwt:SecretKey;禁止使用自动生成的开发密钥(设计 §14)。");
    // 仅 Development 才落 ./data/dev-jwt.key + 警告
}

### P1-2 · [FIX] 未认证/令牌过期返回裸 401,不套统一信封;40006 对其文档所述“令牌过期”场景不可达
**位置** `backend/src/TenonAdmin.AspNetCore/Security/RolePermissionAttribute.cs:32` · cat=api-contract · 反驳票 0/3

**问题** 同一个授权过滤器里两条 401 路径信封形状不一致:未认证(令牌缺失/过期/被篡改导致 User 未认证)走第 32 行 `new UnauthorizedResult()`,产出裸 401 空 body(无 code/msgKey);而会话被强退走第 40 行 `Result<object>.Fail(ErrorCode.TokenInvalid)`,产出带信封的 401(40006)。AuthController.Logout 用 [Authorize](第 37 行),未认证时由 JwtBearer 默认 challenge 出裸 401,同样无信封。全程无 OnChallenge/UseStatusCodePages/UseExceptionHandler 兜底(已 grep 确认)。ErrorCode.TokenInvalid=40006 的 XML 注释写明“访问令牌无效或已过期”,但最常见的“访问令牌过期”这条热路径实际返回裸 401、永不产出 40006,该码对其主用途基本死码。违背 §13.2 前端 i18n 靠 msgKey 渲染的统一信封契约。

**失败场景** 前端持过期 access token 请求任一 [RolePermission] 或 [Authorize] 接口 → 收到 HTTP 401、body 为空 → 请求层拦截器 `t(res.data.msgKey)` 因 msgKey 为 undefined 无法本地化,只能退化为按 HTTP 状态码猜测;而会话强退场景却能拿到 40006 信封,两条 401 前端需分别特判。

**建议** 统一 401 出口:第 32 行改为返回与第 40 行一致的信封 `new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid)){ StatusCode = 401 }`;并为 JwtBearer 配置 `Events.OnChallenge`(或加一个 UseStatusCodePages/异常中间件)把框架 challenge 的 401 也写成 40006 信封,使 [Authorize] 接口(Logout)一致。

### P1-3 · [FIX] 裸返回(return dto)端点的 OpenAPI 契约不含 Result<T> 信封,破坏 §13.6 前端代码生成单源
**位置** `backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs:97` · cat=api-contract · 反驳票 0/3

**问题** ResultEnvelopeFilter 在结果执行阶段把裸 ObjectResult 包成 Result<object?>,但该包装对 ApiExplorer 不可见(结果过滤器不参与 ApiDescription 推断)。ApiExplorer 只按动作声明返回类型出 schema。内置控制器都显式声明 Result<T>,契约正确;但框架主打的“用户控制器 return dto 即自动套信封”(§12/§6)便利路径下,声明返回类型是 DTO/object,生成的 openapi.json 200 响应 schema = DTO(无 code/msgKey/data 外壳),而运行时真实返回 Result<DTO>。全仓无 [ProducesResponseType]/ApiConventionType/IApiResponseMetadataProvider 做协调(已 grep 确认)。此注释“契约含统一信封”仅对显式返回成立,对该便利路径不成立。TestHost/CustomDictController.cs:16 `public object TypePage() => new { source = "custom-dict" }` 即真实样例:openapi 会记为自由 object,运行时是信封。

**失败场景** 用户按框架文档写 `public MyDto Get()=>dto;` 并跑 `npm run gen:api`(openapi-typescript+openapi-fetch,§13.6)→ 生成的类型把 200 body 当作 MyDto → 前端 `const {data}=await client.GET(...)` 中 data 被静态断言为 MyDto,TS 编译通过,但运行时 data 实为 {code,msgKey,data:MyDto},字段全在 data.data 下 → 静默运行时错位,取值全 undefined。

**建议** 二选一:(a) 用一个全局 IApiResponseMetadataProvider/OpenAPI transformer,把所有非文件 200 响应 schema 统一改写为 Result<返回类型>,使契约与 filter 的运行时行为对齐;或 (b) 在覆写指南中明确“要正确 OpenAPI 契约的用户控制器必须显式返回 Result<T>”,并把该 AddOpenApi 注释订正为仅显式返回保真。

### P1-4 · [FIX] CORS 完全未实现,与设计 §12/§14 明列的 v1.0 安全基线项(Api:Cors)缺口
**位置** `backend/src/TenonAdmin.Core/Options/AdminApiOptions.cs:15` · cat=security-misconfiguration / missing-control · 反驳票 0/3

**问题** 设计 §12 line 705 明确把 CORS 归口为『全走 Options 配置(Api:Cors)』且标注归属『AspNetCore,v1』;§14 line 785 把 CORS 列为 v1.0 必须成型的安全基线项:『默认仅允许本地开发源;生产必须显式配置』。但 AdminApiOptions 只有 DisabledModules 一个属性,根本没有 Cors 配置节;全仓 src/samples/tests grep 不到任何 AddCors/UseCors/WithOrigins(已确认零命中),TenonAdminSetup 的 Add/Map 两侧也都没有接入 CORS 中间件。这一项既不在你给定的『已知有意后置清单』内(该清单只含 RateLimiter、路由前缀、MySQL CI、PackageIcon、docker、IP/UA、分片上传),文档里也没有任何『CORS 有意留给用户宿主』的说明——属设计承诺了但实现漏做,且无降级说明。

**失败场景** 设计的默认形态是前后端分离(§1:monorepo 含 web/ Vue 前端 + docker-compose 用 nginx 单独起 web 与 backend;§13.6 前端走 openapi 代码生成独立调 API)。任何浏览器端从与 API 不同源的地址发起 XHR/fetch(Vite 开发服务器直连后端、或分离部署时前端域名 ≠ API 域名、或第三方浏览器调用方)都会被浏览器 CORS 预检拦死,而运维没有设计承诺的 Api:Cors 配置项可用来放行源——整个管理端在分离部署下无法访问后端,且无从配置。

**建议** 补齐 Options + 中间件,默认收紧、生产显式配:
// AdminApiOptions.cs 新增
public AdminCorsOptions Cors { get; set; } = new();
public class AdminCorsOptions {
    public string[] AllowedOrigins { get; set; } = []; // 空=不放行;开发可默认 http://localhost:5173
    public bool AllowCredentials { get; set; } = true;
}

// AddTenonAdmin 内(仅当配置了源才注册具名策略)
services.AddCors(o => o.AddPolicy("TenonAdmin", p => {
    if (options.Api.Cors.AllowedOrigins.Length > 0) {
        p.WithOrigins(options.Api.Cors.AllowedOrigins).AllowAnyHeader().AllowAnyMethod();
        if (options.Api.Cors.AllowCredentials) p.AllowCredentials();
    }
}));

// MapTenonAdmin/管道内,置于 UseRouting 之后、认证之前
app.UseCors("TenonAdmin");
// 注意:严禁 AllowAnyOrigin() + AllowCredentials() 组合。

### P1-5 · [FIX] 登录失败锁定可被账号大小写/尾随空白变体绕过(大小写不敏感数据库排序规则下)
**位置** `backend/src/TenonAdmin.Services/Auth/AuthService.cs:62` · cat=authentication-bruteforce · 反驳票 0/3

**问题** 登录锁定是本项目的首要防爆破手段(验证码默认关——见 AdminCaptchaOptions 注释「账号级 LoginLock 已挡爆破主向」)。但账号在整条链路上都未做规范化:ValidateUserAsync 用 `u.Account == input.Account` 查库,失败计数 LoginLockService 以原始 `account` 串为 key(EnsureNotLockedAsync line 19 / RecordFailureAsync line 30,由 OnLoginFailedAsync line 122 传入原始 `input.Account`)。在大小写/尾随空白不敏感的排序规则(MySQL 默认 utf8mb4_0900_ai_ci、PAD SPACE)下,`account = 'Admin'` 命中真实用户行 'admin',但锁定计数 key 'Admin' 与 'admin' 是两个独立计数器。SQLite(零配置默认)大小写敏感不受影响,但 MySQL 是受支持的生产库。

**失败场景** MySQL 部署,存在账号 'admin',MaxFailCount=5。攻击者对 'admin' 发 5 次错误密码触发锁定;随即改用 'Admin'、'ADMIN'、'aDmin'…(2^5 种大小写)以及 'admin '(尾随空格)等变体——每个变体都命中同一真实用户、按其真实哈希校验,但各自独立计数,永远触不到该真实账号的锁定阈值。首要防爆破控制被完全旁路,攻击者获得对高权限账号事实上无限次的在线密码猜测。

**建议** 在信任边界对账号做一次规范化,并让「查库」和「锁定计数」用同一规范形(与唯一索引的排序规则语义对齐)。例如在 LoginInput 规范化或在 AuthService 入口:
```csharp
// 统一规范化(去空白 + 小写),查库与锁定计数共用
var account = input.Account.Trim().ToLowerInvariant();
```
并让 LoginLockService 的三个方法都以规范化账号为 key(或改为登录成功/失败后以命中的 `user.Account` 规范形计数)。关键是:DB 匹配的等价类与锁定计数的 key 等价类必须一致,变体不能拆分计数器。

### P1-6 · [FIX] 验证码一次性消费是非原子的先读后删,并发可重放同一张验证码;契约无原子取删故 Redis 也修不了
**位置** `backend/src/TenonAdmin.Services/Captcha/CaptchaService.cs:37` · cat=A07-auth-failures / CWE-367 TOCTOU · 反驳票 0/3

**问题** 类注释声称『先移除再比对,无论对错该票据都作废,杜绝同一票据重放或多次猜测』,但 GetAsync 与 RemoveAsync 是两次独立 await,不构成原子的 get-and-delete。多个并发 ValidateAsync 携带同一 captchaId 时,可能都在任一 Remove 生效前读到同一个 stored 值,于是全部通过验证码校验,一次性/防重放不变式被打破。ICacheProvider 没有原子 GETDEL,所以 Redis 替换同样修不了——与登录锁定同源的契约缺陷。SVG 验证码强度本就偏低、设计上靠『配合登录锁定』兜底(rebuild-design.md:654),而登录锁定又有 finding 1 的并发缺陷,叠加后每张验证码的猜测节流实际失效。

**失败场景** 攻击者解一张验证码,拿到 captchaId+code 后并发发 N 个登录请求复用同一 captchaId。N 个请求都在 RemoveAsync 落地前执行 GetAsync 读到 stored,于是 N 次登录尝试全部通过验证码这道关,单张验证码被放大成 N 次猜测。

**建议** 在 ICacheProvider 增加原子取删原语(如 Task<T?> GetAndRemoveAsync<T>(string key)),校验改为一次原子消费:
var stored = await cache.GetAndRemoveAsync<string>(key);
AdminException.ThrowIf(stored is null, ErrorCode.CaptchaExpired);
AdminException.ThrowIf(!string.Equals(stored, code, StringComparison.OrdinalIgnoreCase), ErrorCode.CaptchaWrong);
// MemoryCacheProvider 用 per-key lock 实现取删,RedisCacheProvider 映射 GETDEL(或 Lua 脚本 GET+DEL)。

### P1-7 · [FIX] Test suite is RED and flaky: unsynchronized List<IDisposable> in CacheChangeLogSubscriber.StopAsync throws 'Collection was modified' on host shutdown
**位置** `backend/src/TenonAdmin.Services/Events/CacheChangeLogSubscriber.cs:34` · cat=correctness-concurrency · 反驳票 1/3

**问题** The pre-release assumption 'solution compiles + all tests green' is FALSE. I actually ran the suite (twice). The solution builds clean (verified with a full `dotnet build backend/TenonAdmin.slnx -c Release --no-incremental`: 0 warnings, 0 errors, all 8 projects). But `dotnet test backend/tests/TenonAdmin.Tests` FAILS nondeterministically. Run 1: 37 total, 35 passed, 2 failed (AuthFlowTests.Login_succeeds_and_returns_token, ReplaceabilityTests.OverrideAuthStep_ShouldAffectLoginFlow). Run 2: 37 total, 36 passed, 1 failed (AuthFlowTests.Refresh_issues_new_token — a DIFFERENT test). Every failure is the identical stack: `System.InvalidOperationException : Collection was modified; enumeration operation may not execute` at CacheChangeLogSubscriber.StopAsync line 34. Root cause: `_subscriptions` (line 15) is a plain non-thread-safe `List<IDisposable>`. StartAsync mutates it via `.Add()` (lines 19, 24); StopAsync enumerates it (`foreach` line 34) and then `.Clear()`s it (line 36), with zero synchronization. The loop body `s.Dispose()` only calls back into ChannelEventBus.Unsubscribe (touches the bus's `_handlers`, not `_subscriptions`), so the structural modification that invalidates the enumerator comes from another thread during host teardown — i.e. StopAsync racing an overlapping StartAsync.Add or a re-entrant/concurrent StopAsync.Clear driven by WebApplicationFactory's Dispose→DisposeAsync teardown path. The differing failing test per run confirms a data race, not a deterministic bug. This is the single blocker for the 'all green' release gate; the underlying defect lives in shipping library code (TenonAdmin.Services), which users host.

**失败场景** Host shutdown while _subscriptions is being concurrently mutated. In the test harness: WebApplicationFactory teardown drives IHostedService.StopAsync in a way that races the list, so ~1-2 of the 6 integration tests fail per run with 'Collection was modified' at line 34 — the suite is red every run, but on different tests. In production the same throw occurs on the graceful-shutdown path when shutdown (e.g. SIGTERM / orchestrator kill) begins before StartAsync completes (the co-registered DatabaseInitializer hosted service can make startup slow) or when StopAsync is re-entered: the hosted service throws during shutdown, remaining subscriptions are not disposed (leak), and shutdown logs an unhandled exception.

**建议** Make StopAsync operate on a snapshot and synchronize both mutation sites. Minimal root-cause fix: in StartAsync wrap the two `.Add()` calls in `lock (_subscriptions)`; rewrite StopAsync as: `IDisposable[] snapshot; lock (_subscriptions) { snapshot = [.. _subscriptions]; _subscriptions.Clear(); } foreach (var s in snapshot) s.Dispose(); return Task.CompletedTask;`. This makes teardown idempotent and immune to concurrent Add/Clear. After the fix, run `dotnet test backend/tests/TenonAdmin.Tests` several times to confirm the flakiness is gone.

### P1-8 · [FIX] 用户更新接口可停用/篡改超级管理员,绕过 SuperAdminProtected 护栏
**位置** `backend/src/TenonAdmin.Services/User/UserService.cs:96` · cat=broken-access-control · 反驳票 0/3

**问题** 类注释与设计明确的安全不变量是「超管不可删/停(防自锁死、防提权面被破坏)」。DeleteAsync(line 107)与 SetEnabledAsync(line 130 `!enabled && user.IsSuperAdmin → SuperAdminProtected`)都设了护栏,但 UpdateAsync 直接 `user.Enabled = input.Enabled` 并整行 UpdateAsync 落库,完全没有超管护栏。UpdateAsync 还会改超管的 Name/OrgId/PositionId 并 `rbac.SetUserRolesAsync` 全量重设其角色。UserController.Update(`[HttpPut("{id}")]` line 36-41)把任意 id(含超管固定主键 1)直通到此方法。

**失败场景** 一个非超管、但被授予 `PUT:/api/v1/sys/user/{id}` 权限码的普通管理员(或超管本人误操作),调用 `PUT /api/v1/sys/user/1`,body `{"name":"x","orgId":null,"positionId":null,"enabled":false,"roleIds":[]}`。UpdateAsync 无护栏地把超管 Enabled 置 false。之后超管登录被 CheckLoginPolicyAsync 抛 AccountDisabled 拒绝,已存在的超管会话在 RefreshAsync(SessionService line 82 `!user.Enabled → AccountDisabled`)也被拒——最高权限账号被下位者停用锁死,应用内无恢复入口,只能改库。SetEnabledAsync 精心设置的 SuperAdminProtected 护栏被同类的 UpdateAsync 旁路。

**建议** 在 UpdateAsync 里补齐与 SetEnabledAsync 同源的护栏(并按需禁止对超管改资料):
```csharp
public virtual async Task UpdateAsync(long id, UpdateUserInput input)
{
    var user = await users.GetByIdAsync(id);
    AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
    // 停用超管与 SetEnabledAsync 同护栏;超管资料/角色不经普通更新面改动
    AdminException.ThrowIf(user!.IsSuperAdmin && !input.Enabled, ErrorCode.SuperAdminProtected);
    user.Name = input.Name;
    user.OrgId = input.OrgId;
    user.PositionId = input.PositionId;
    user.Enabled = input.Enabled;
    await users.UpdateAsync(user);
    await rbac.SetUserRolesAsync(id, input.RoleIds);
}
```
若要彻底守住「不能改超管」,可在方法入口 `AdminException.ThrowIf(user!.IsSuperAdmin, ErrorCode.SuperAdminProtected)` 直接拒绝对超管的任何更新。

### P1-9 · [FIX] 新建用户/重置密码默认口令为已发布 NuGet 内核里的公开硬编码常量,且无首次登录强制改密
**位置** `backend/src/TenonAdmin.Services/User/UserService.cs:20` · cat=security · 反驳票 0/3

**问题** 确认遗留线索(b)。DEFAULT_PASSWORD 是编译期常量,随公开 NuGet 包分发 → 全网可知。AddAsync(UserService.cs:69)与 ResetPasswordAsync(:119)在未传密码时都落到这个固定口令,且无『首次登录强制改密』机制。代码已标 ponytail 承认后置,但发布前仍是真实弱点。

**失败场景** 管理员在后台批量新建用户时不填密码(接口允许省略),这些账号口令全部是公开已知的 Tenon@123456;若管理员未逐一通知用户改密,攻击者拿任一新建账号的登录名 + 公开默认口令即可直接登入。属 CWE-1392/798 类默认凭据弱点。

**建议** 默认初始密码改为可配置(如 Seed/Security 节),缺省时按账号随机生成并返回给管理员当场转达;并加 SysUser.MustChangePassword 标志,首次登录强制改密后才放行其它接口。

### P1-10 · [FIX] 软删保留唯一键行:删后重建同 Code/Account 触发原始 UNIQUE 冲突 500,且键永久锁死
**位置** `backend/src/TenonAdmin.Services/User/UserService.cs:67` · cat=correctness · 反驳票 0/3

**问题** 软删只把 IsDelete 置 1、物理行保留(SqlSugarRepository.DeleteAsync:49-52),而全部实体的唯一索引(SugarIndex IsUnique=true)在 CodeFirst 下建成覆盖所有物理行的全量 UNIQUE 索引——软删行仍占着 Account/Code。与此同时新增前的查重 AnyAsync 走全局软删过滤器(SqlSugarSetup:56),对软删行天然失明。两者叠加使友好的 *Exists 守卫对已删行静默失效,插入落到数据库唯一约束上抛原生异常;AdminExceptionFilter(:18)只处理 AdminException,DB 异常直穿到框架默认 500。同缺陷面:ConfigService.AddAsync:54(ConfigKey)、DictService.AddTypeAsync:38(Code)。更糟的是 PositionService.AddAsync:28 与 OrgService.AddAsync:25 连查重都没有(ErrorCode 里也无 OrgCodeExists/PositionCodeExists),任何重复 Code(哪怕首次)就 500。全仓无一处 ClearFilter<ISoftDelete>。

**失败场景** 建用户 Account='alice' → 删除(软删,行留存,idx_sys_user_account 仍握 'alice')→ 再建 'alice':AnyAsync 被软删过滤器遮蔽返回 false → 进 InsertAsync → 命中 UNIQUE 约束 → SqliteException 越过 AdminExceptionFilter → HTTP 500 带堆栈。净效果:账号/编码删除后永远无法复用,且 AccountExists/ConfigKeyExists/DictTypeCodeExists 对软删行完全失灵。Position/Org 连首次重复 Code 都直接 500。

**建议** 查重时把软删行纳入:users.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(...),并明确语义(要么以干净 *Exists 拒绝复用,要么复活软删行);或将唯一索引改为按 IsDelete=0 的部分索引(SugarIndex 不直接支持,需原生 DDL/迁移)使软删行不再占键。同时给 Org/Position 补 CodeExists 前置查重。

### P1-11 · [FIX] 生产建表安全闸门(EnableCodeFirstInProduction + 环境判断)完全缺失,默认在任何环境自动 DDL
**位置** `backend/src/TenonAdmin.SqlSugar/DatabaseInitializer.cs:28` · cat=design-safety · 反驳票 0/3

**问题** 设计 §12 与 §4.1 明确承诺『不承诺生产环境自动改表,生产必须显式开启 EnableCodeFirstInProduction 才允许自动改表』。但实际:(1) AdminDatabaseOptions 根本没有 EnableCodeFirstInProduction 字段(backend/src/TenonAdmin.Core/Options/AdminDatabaseOptions.cs 只有 EnableCodeFirst/EnableSeed);(2) DatabaseInitializer 只判 options.EnableCodeFirst(默认 true),不查宿主环境(全仓无 IHostEnvironment/IsProduction 引用,grep 仅命中一句 XML 注释)。安全闸门是空的。

**失败场景** 消费者用默认配置(EnableCodeFirst=true)把内核部署到生产,连的是 DBA 手工维护的库。应用一启动 DatabaseInitializer.StartAsync 就对生产库跑 CodeFirst.InitTables,自动建表/补列——与文档承诺的『生产不自动改表』直接相悖,DBA 不会预期应用会 ALTER 表结构。

**建议** AdminDatabaseOptions 补 EnableCodeFirstInProduction(默认 false);DatabaseInitializer 注入 IHostEnvironment,建表条件改为 options.EnableCodeFirst && (!env.IsProduction() || options.EnableCodeFirstInProduction),生产未显式开启时跳过建表并 LogWarning。

### P1-12 · [FIX] 数据范围锚点 CreateOrgId 从不被 AOP 填充,机构维度数据范围对真实业务表恒返回 0 行
**位置** `backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs:68` · cat=correctness · 反驳票 0/3

**问题** DataExecuting 的 InsertByObject 分支填充了 Id / CreateTime / CreateUserId,但没有任何填充 CreateOrgId 的分支。DataEntity.cs:28-33 的文档明确声明 CreateOrgId『由当前用户上下文 AOP 填充(T4 接入)』,而 ICurrentUser(ICurrentUser.cs)根本不暴露 OrgId、TokenSubject 也不带机构,AOP 无处可取。数据范围全局过滤器(SqlSugarSetup.cs:62-65)以 `e.CreateOrgId != null && scope.Current.OrgIds.Contains(...)` 为机构分支的前置条件。因此任何经仓储插入的 IOrgScoped 行 CreateOrgId 恒为 null,机构分支永远不成立。当前仅测试类 ScopeDoc(DataScopeTests.cs:74)手工赋值 CreateOrgId,生产尚无 : DataEntity 业务表,故问题潜伏未爆。

**失败场景** 新增第一张 `: DataEntity` 业务表,认证用户经 IRepository 插入若干行(CreateOrgId 自动留 null);随后一个数据范围为 本机构 / 本机构及以下 / 自定义(且未含『仅本人』)的用户查询该表 → 机构分支因 CreateOrgId==null 恒 false → 得到 0 行,招牌『数据范围隔离』能力对该表静默失效(欠可见,非越权)。

**建议** 在 DataExecuting 的 InsertByObject 分支补 CreateOrgId 填充,但需先让 ICurrentUser 暴露 OrgId(在 JWT 增 org claim 由 HttpContextCurrentUser 读取,或登录时写入),AOP 据此对 CreateOrgId==null 的 IOrgScoped 实体回填;在交付首张 DataEntity 业务表前补一条覆盖 AOP 自动填充路径(而非手工赋值)的测试。若短期不实现,应修正 DataEntity 文档去掉『AOP 填充』承诺,改为要求业务层显式赋值。

## P2 详情(22)

### P2-1 · [FIX] 个人中心端点用 [Authorize] 而非会话活性校验,强退/登出对其非即时生效
**位置** `backend/src/TenonAdmin.AspNetCore/Controllers/PersonalController.cs:14` · cat=session-management · 反驳票 0/1

**问题** 设计不变量「强退即时 401」由 RolePermissionAttribute 每请求调用 ISessionService.IsActiveAsync 实现(会话被吊销→缓存移除→查库见 RevokedAt→401)。但 PersonalController 用类级 `[Authorize]`,只校验 JWT 签名与有效期,不查会话活性。于是 `/api/v1/personal/*`(看资料/改姓名/改密码)在会话被强退或登出后,仍可被仍在有效期内的 access token 继续调用,直到令牌自然过期(默认 ExpireMinutes=120)。Logout 端点同样是 [Authorize],但登出是幂等自吊销、无害;个人中心是真实操作面。

**失败场景** 管理员在会话管理里对某可疑会话点「强制下线」(SysSessionController.ForceLogout→RevokeAsync)。期望即时切断。但持有该会话 access token 的一方,在其后最长约 120 分钟内仍可 `GET/PUT /api/v1/personal/profile`(读取自身 Account/Name/OrgId/IsSuperAdmin、改自身姓名),而所有 [RolePermission] 端点已即时 401。强退不变量在个人中心面出现缺口。(改密码 PUT /personal/password 需验旧密码,风险受限;资料读改则无此门槛。)

**建议** 给个人中心补上与 [RolePermission] 同源的会话活性校验,但不要求具体权限码。抽一个轻量过滤器复用 IsActiveAsync:
```csharp
public sealed class ActiveSessionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var u = ctx.HttpContext.User;
        if (u.Identity?.IsAuthenticated != true) { ctx.Result = new UnauthorizedResult(); return; }
        var sid = u.FindFirstValue(TokenClaimNames.SESSION_ID);
        var sessions = ctx.HttpContext.RequestServices.GetRequiredService<ISessionService>();
        if (string.IsNullOrEmpty(sid) || !await sessions.IsActiveAsync(sid))
            ctx.Result = new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid)) { StatusCode = StatusCodes.Status401Unauthorized };
    }
}
```
把 PersonalController 的 `[Authorize]` 换成 `[ActiveSessionAttribute]`(或让 RolePermission 在无匹配权限码模板时退化为仅活性校验)。

### P2-2 · [DOC] 框架生成的非 2xx 响应(400 绑定/校验、500 未捕获)绕过信封;50000 从不作为“统一出口”产出
**位置** `backend/src/TenonAdmin.AspNetCore/Filters/ResultEnvelopeFilter.cs:31` · cat=api-contract · 反驳票 0/1

**问题** 两类框架级失败不套信封:(1)[ApiController] 自动 400——请求体畸形/空体/路由参数类型不匹配(如 GET /api/v1/sys/user/{id} 传非数字)→ 返回 RFC7807 ValidationProblemDetails(无 msgKey);ResultEnvelopeFilter 第 31 行显式对非 2xx 放行、不包。(2)未捕获异常——AdminExceptionFilter 第 18 行只处理 AdminException,其余原样抛;全仓无 UseExceptionHandler(已确认),Production 下产出裸 500 空 body。ErrorCode.SystemError=50000 注释称“未捕获异常的统一出口”,但无任何异常处理器把它写成信封响应(OperationLogFilter 仅把 50000 记进操作日志,不影响 HTTP body),该“统一出口”仅为纸面。对非浏览器/第三方直连调用方,响应形状不统一。判断:400/500 不套信封在很多 REST API 属可接受,AdminExceptionFilter 注释也明示“程序缺陷该大声失败”的有意选择——此条更多是契约文档(50000 注释、§13.2 统一信封承诺)与实现的口径不一致,发布前值得做一次显式取舍。

**失败场景** 前端向 /api/v1/auth/login 发畸形 JSON 或空体 → 收到 400 ValidationProblemDetails {type,title,status,errors} 而非 {code,msgKey,data};或某内置动作抛 NullReferenceException → 收到裸 500 空 body。i18n 拦截器两种情况都读不到 msgKey,只能走通用兜底。

**建议** 若要兑现 §13.2 全量信封:重写 ApiBehaviorOptions.InvalidModelStateResponseFactory 把 400 映射为一个 400xx/系统段信封,并加 UseExceptionHandler 把未捕获异常收敛为 SystemError=50000 信封(详情只进日志)。若维持现状,则订正 50000 注释与 §13.2 措辞,明确“框架级 400/500 不进信封,前端须兜底”。

### P2-3 · [FIX] ./data 与 ./data/dev-jwt.key 使用进程工作目录相对路径,应相对 ContentRoot
**位置** `backend/src/TenonAdmin.AspNetCore/Security/JwtKeyResolver.cs:22` · cat=portability · 反驳票 0/1

**问题** 确认遗留线索(d)。开发密钥路径(JwtKeyResolver.cs:22)与 SQLite 目录(DatabaseInitializer.EnsureSqliteDirectory 直接用连接串里的 ./data 相对串)都相对进程当前工作目录(CWD),而非宿主 ContentRoot。

**失败场景** 以 systemd/Windows 服务或从非项目目录启动应用时,CWD 与 ContentRoot 不一致:开发密钥每次可能写到不同目录 → 重启后签名密钥变化 → 旧令牌全部失效(dev-plan 声称的『重启后旧 token 仍有效』不成立);SQLite 库也可能建在意外位置。

**建议** 注入 IHostEnvironment.ContentRootPath,用 Path.Combine(env.ContentRootPath, "data", ...) 组合密钥与默认库路径(仅对相对路径规整,绝对路径原样)。

### P2-4 · [TEST] 测试缺口:权限码强制匹配(已认证非超管无码 → 403/41001)零集成测试
**位置** `backend/src/TenonAdmin.AspNetCore/Security/RolePermissionAttribute.cs:63` · cat=test-gap · 反驳票 0/1

**问题** RBAC 的核心——BuildPermissionCode 算出的规范化码与用户 PermissionCodeList 的匹配(第 60-67 行)——完全没有 HTTP 级用例覆盖。现有集成测试只覆盖:超管成功流(AuthFlow/Replaceability)、未认证 401(ReplaceabilityTests.cs:46)。已认证的普通用户“有码放行 / 无码 403+41001”这条主授权路径,以及数据范围随之写入上下文的行为,均未被回归锁死。grep 全 tests 无 403/Forbidden/41001/NoPermission 断言。

**失败场景** 若日后有人改坏 BuildPermissionCode 的大小写规范化、或改动 IPermissionProvider 的码集来源、或误把 codes.Contains 逻辑反了,现有测试全绿通过,授权漏洞(越权放行或全员 403)要到人工验收或线上才暴露。

**建议** 加一个 WebApplicationFactory 集成用例:造一个普通用户,分别授予/不授予某个已 seed 的按钮码(如 GET:/api/v1/sys/user/page),断言授码→200 信封、未授码→403 且 code=41001。

### P2-5 · [FIX] JWT 校验未收紧 ClockSkew,过期访问令牌有 5 分钟宽限
**位置** `backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs:71` · cat=security-misconfiguration · 反驳票 0/1

**问题** TokenValidationParameters 未显式设置 ClockSkew,Microsoft.IdentityModel 默认 ClockSkew=5 分钟。即一个 exp 已过的 access token 仍会被接受最多 5 分钟。ValidateIssuer/ValidateLifetime 走默认 true 且 ValidIssuer 已设、签名校验生效,这些是对的;但时钟偏移未收紧,与设计「短命令牌」意图不一致。会话强退能兜住多数场景(活性校验独立于令牌有效期),故实际影响有限,但仍是应收紧的基线项。

**失败场景** access token 名义 exp 到点后,持有者在其后最多约 5 分钟内对任一 [RolePermission] 端点发起请求,JwtBearer 因默认 5 分钟 ClockSkew 仍判定令牌有效(只要会话未被强退且未到会话过期)。令牌生命周期被静默延长 5 分钟,与短票据策略相悖。

**建议** 显式收紧时钟偏移并显式声明生命周期校验:
```csharp
o.TokenValidationParameters = new TokenValidationParameters
{
    ValidIssuer = options.Jwt.Issuer,
    IssuerSigningKey = signingKey,
    ValidateAudience = false,
    ValidateLifetime = true,           // 显式
    ClockSkew = TimeSpan.FromSeconds(30), // 收紧,默认 5 分钟
    NameClaimType = "unique_name",
};
```

### P2-6 · [FIX] 默认拒绝仅靠约定,无 FallbackPolicy 兜底——漏挂特性的 action 即匿名公开
**位置** `backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs:79` · cat=broken-access-control · 反驳票 0/1

**问题** 授权服务以无参形式注册,未设置 FallbackPolicy;MapTenonAdmin() 的 MapControllers() 也未 .RequireAuthorization()。[ApiController] 本身不施加任何鉴权。因此"默认拒绝"完全依赖每个 action 手工挂 [RolePermission]/[Authorize],框架层实际是"默认放行":任何未标注鉴权特性的 action 会被 MVC 当作匿名端点直接放行。当前 12 个控制器逐 action 核查均已标注(见 notes),故今天没有实际暴露面,属潜在弱点/加固项。

**失败场景** 某开发者(或后续内置控制器)新增一个 action 时忘记加 [RolePermission],例如 [HttpGet("export")] public Task<...> Export()。由于没有 FallbackPolicy 且 [ApiController] 不强制鉴权,该端点对未认证调用者直接可达,静默破坏默认拒绝,直到人工发现。没有任何结构性机制会让"漏标注"失败(编译/启动/请求)。

**建议** 改为默认拒绝:services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()); FallbackPolicy 只在 action 无任何鉴权特性时生效,与 [AllowAnonymous](captcha/login/refresh)并不冲突,漏标注的 action 会被强制要求认证而非公开。

### P2-7 · [FIX] NameClaimType 硬编码字面量 "unique_name",未走 JwtRegisteredClaimNames 常量
**位置** `backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs:76` · cat=hardcoding · 反驳票 0/1

**问题** 禁硬编码 claim 名纪律的一处漏网:签发端(JwtTokenProvider.cs:48)用 JwtRegisteredClaimNames.UniqueName,验证端却写死字面量 "unique_name"。两处靠人肉保持一致,重构风险。功能当前正确,仅为一致性/防错。

**失败场景** 若日后有人改动账号 claim 名(如为对接外部 IdP 改用 preferred_username),只改了签发端常量而漏掉这个字面量,User.Identity.Name 会静默变空,依赖它的日志/审计取不到账号。

**建议** 改为 NameClaimType = JwtRegisteredClaimNames.UniqueName;(值不变,消除重复字面量)。

### P2-8 · [FIX] OpenAPI 文档端点在生产环境无门禁暴露,泄露完整 API 契约
**位置** `backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs:109` · cat=information-disclosure / A05 · 反驳票 0/1

**问题** AddOpenApi()(line 97)与 MapOpenApi()(line 109)都无条件调用,项目内零 IsDevelopment 门禁。MapOpenApi 默认匿名可访问,于是生产环境下 GET /openapi/v1.json 未认证即可拉到全部端点、路由、参数名与 DTO schema。这偏离了 .NET 官方模板(默认把 app.MapOpenApi() 包在 if (app.Environment.IsDevelopment()) 内)以及设计 §13.6 的定位——openapi 是开发期前端代码生成的契约源,并未要求生产暴露。

**失败场景** 未认证攻击者对生产实例请求 /openapi/v1.json,直接获得整站 API 地图(所有 sys/* 端点、参数结构、枚举错误码),为后续针对性攻击(参数猜测、隐藏管理端点探测)提供完整侦察面。端点本身仍受 [RolePermission] 保护,故为信息泄露/侦察辅助而非直接接管,定 P2。

**建议** 按环境门禁,仅开发暴露:
// Map 侧接收环境判断(把 IHostEnvironment 传入 MapTenonAdmin,或在宿主侧判断)
if (env.IsDevelopment())
{
    endpoints.MapOpenApi();   // 仅开发产出 /openapi/v1.json
}
// AddOpenApi() 可保留(仅注册服务,不产开销);关键是 MapOpenApi 加门禁。
// 若确需生产暴露契约,应改为要求认证/限内网访问,而非匿名开放。

### P2-9 · [FIX] 健康检查缺 /health/ready 就绪探针与 DB/缓存依赖检查
**位置** `backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs:110` · cat=completeness / observability · 反驳票 0/1

**问题** 设计 §12 line 701 要求暴露 /health(存活)与 /health/ready(依赖:DB/缓存)两个端点。实现里 AddHealthChecks()(line 98)未注册任何具体检查项,Map 侧只有 MapHealthChecks("/health"),既没有 /health/ready,也没有对数据库/缓存的就绪探测。于是 /health 只返回进程存活,无法反映底层依赖是否可用。

**失败场景** 在 k8s/compose 用 readiness probe 指向 /health/ready 时探针 404;或当数据库/缓存不可达时 /health 仍返回 200,编排器据此把尚不能服务请求的实例接入流量,导致上线瞬间大量 500。属发布前 host-wiring 完整性缺口。

**建议** 注册依赖检查并映射就绪端点:
services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("db", tags: ["ready"])
    .AddCheck<CacheHealthCheck>("cache", tags: ["ready"]);

endpoints.MapHealthChecks("/health", new() { Predicate = _ => false });      // 仅存活
endpoints.MapHealthChecks("/health/ready",
    new() { Predicate = r => r.Tags.Contains("ready") });                     // 依赖就绪

### P2-10 · [FIX] DataScopeResult 不可序列化往返,宣传的 Redis 直接替换会破坏数据范围缓存
**位置** `backend/src/TenonAdmin.Core/Security/DataScopeResult.cs:23` · cat=A04-insecure-design / serialization-contract · 反驳票 0/1

**问题** DataScopeResult 只有一个私有构造器 + 全 get-only 属性,且构造参数名与属性名不一致(unrestricted↔IsUnrestricted)。它在 DataScopeProvider.cs:30 以 cache.SetAsync 缓存、:25 以 GetAsync<DataScopeResult> 读回。默认 MemoryCacheProvider 存对象引用不序列化(MemoryCacheProvider.cs:10 明说『Redis 实现才涉及序列化』),所以现在无碍;但一旦装上设计主推的 RedisCacheProvider(多实例扩展点的全部意义),用 System.Text.Json 反序列化会抛 NotSupportedException——无公共无参构造器、无 [JsonConstructor]、仅私有构造器,STJ 找不到可用构造器。SessionCacheInfo(record+init)与 string[] 权限缓存能正常往返,唯独 DataScopeResult 不行。该对象被设计成不可变值对象却没做成序列化友好,直接违背『整体替换、多实例共享』承诺。属前瞻性(Redis 包尚未构建、按计划后置),故 P2,但会恰好在有人按文档走多实例路径时触发。

**失败场景** 运维按文档装 TenonAdmin.Caching.Redis 换 RedisCacheProvider 做多实例。任一受限(非超管)用户请求触发 ResolveAsync→GetAsync<DataScopeResult> 时,STJ 反序列化抛 NotSupportedException,该用户每个请求 500(数据范围解析崩)。若 Redis 实现改用 Newtonsoft,则参数名 unrestricted 绑不上 JSON 键 IsUnrestricted→Unrestricted 范围往返成 restricted-empty,用户静默失去全部数据可见性(fail-closed 数据错乱)。

**建议** 把 DataScopeResult 做成序列化友好:加公共构造器或 [JsonConstructor] 且参数名与属性名对齐,或改为 record 用 init 属性。示例:
public sealed record DataScopeResult{
  public bool IsUnrestricted { get; init; }
  public IReadOnlyCollection<long> OrgIds { get; init; } = [];
  public bool IncludeSelf { get; init; }
  public long UserId { get; init; }
  // 保留 Unrestricted/Restricted 工厂方法
}
并在 RedisCacheProvider 落地时对所有缓存 DTO 加一个序列化往返单测,防止此类退化。

### P2-11 · [FIX] LoginLockService 失败计数读-改-写非原子,并发爆破下丢失更新削弱锁定
**位置** `backend/src/TenonAdmin.Services/Auth/LoginLockService.cs:31` · cat=concurrency · 反驳票 0/1

**问题** RecordFailureAsync 是典型的 get-then-set,缓存提供者(MemoryCacheProvider)无原子自增。并发的多次密码错误会各自读到同一个旧值再各自 +1 写回,发生丢失更新,计数增长远小于实际失败次数。

**失败场景** 攻击者对同一账号并发(如 20 路)发起错误密码登录:全部读到 count=k、全部写回 k+1,实际只前进 1 而非 20。于是可在计数触达 MaxFailCount 之前放行远多于阈值的尝试,削弱 §14 防爆破(单实例内存缓存下为有界削弱,请求最终串行化仍会触发锁定)。

**建议** 在 ICacheProvider 增一个原子 IncrementAsync(memory 实现用 lock 或 UpdateValueFactory,Redis 实现用 INCR + EXPIRE),RecordFailureAsync 改用它;或对同账号计数加进程内锁(单实例足够)。

### P2-12 · [FIX] OrgService.UpdateAsync 拒绝『父指向自己』时复用 OrgNotFound,语义错配
**位置** `backend/src/TenonAdmin.Services/Org/OrgService.cs:45` · cat=semantics · 反驳票 0/1

**问题** 确认遗留线索(a)。把机构设为自身父节点这一非法入参,返回的是 42003『机构不存在』(msgKey error.org.notFound)。注:多级环(A→B→A)不被此判拦截,但 DataScopeProvider.CollectDescendants 用 HashSet.Add 访问集守护,不会死循环,故此项仅语义问题,非 DoS。

**失败场景** 管理员在机构编辑页误把某机构的父选成它自己并提交,后端返回 42003/error.org.notFound,前端渲染成『机构不存在』——与真实原因(非法父级)不符,用户因错误提示无法定位问题,白费排障时间。

**建议** 新增专用错误码(如 OrgInvalidParent = 42008 + [MsgKey("error.org.invalidParent")]),UpdateAsync 用它替换 OrgNotFound;可顺带把校验扩展为『父不能是自身或自身子孙』并复用同码。

### P2-13 · [FIX] Pbkdf2PasswordHasher.Verify returns true for a stored hash with an empty hash segment (FixedTimeEquals of two zero-length arrays)
**位置** `backend/src/TenonAdmin.Services/Security/Pbkdf2PasswordHasher.cs:55` · cat=A02-Cryptographic-Failures / auth-parser-robustness · 反驳票 0/1

**问题** The self-describing hash parser validates segment count (==4), the algorithm label, iterations (>0) and base64 decodability, but never checks that the decoded hash segment is non-empty / of the expected length. If parts[3] is an empty string, Convert.FromBase64String("") returns a zero-length array, so expected.Length == 0. Rfc2898DeriveBytes.Pbkdf2(..., outputLength: 0) then returns a zero-length array, and CryptographicOperations.FixedTimeEquals(new byte[0], new byte[0]) returns true. Result: any password verifies against such a hash. This is stricter-than-throwing gone wrong: a malformed string that should mean 'no match' instead means 'always match'. It is NOT reachable through any current write path (Hash() always emits a 32-byte segment; ResetPassword/AddUser/ChangePassword/Seed all route through Hash()), so it is not attacker-exploitable today. It becomes a real auth bypass only if a hash row with an empty terminal segment ever enters the SysUser table via the design's supported 'connect to an existing user store' / migration path (rebuild-design.md §5.3 explicitly supports importing external hashes and overriding Verify). There is also no unit test for Pbkdf2PasswordHasher at all (tests exist for LocalFileStorage, SensitiveDataMasker, Captcha, but not the hasher), so malformed-input behavior is entirely uncovered.

**失败场景** A stored password value of the form 'pbkdf2-sha256.600000.YWJjZGVmZ2hpamtsbW5v.' (valid 16-byte base64 salt, empty 4th segment) is present for an account (e.g. inserted during a migration from a legacy user store, or a truncated/corrupted row). Login with ANY password for that account: parts.Length==4, parts[0] matches, iterations=600000>0, salt decodes fine, parts[3] decodes to byte[0], expected.Length==0, actual==byte[0], FixedTimeEquals(byte[0],byte[0])==true → authentication succeeds with an arbitrary password.

**建议** Reject a zero-length (or wrong-length) hash segment before comparison, e.g. after decoding: `if (expected.Length == 0) return false;` (or assert `expected.Length == HASH_SIZE` for the current format). One line, in the shared Verify parser so every caller is covered. Add a Pbkdf2PasswordHasherTests covering: valid round-trip, empty terminal segment, empty salt segment, wrong segment count, non-base64 salt/hash, unknown algorithm, and non-numeric/negative iterations — all must return false without throwing.

### P2-14 · [TEST] 测试缺口:缺 DefaultMenuSeed 权限码 与 BuildPermissionCode 输出的一致性回归锁
**位置** `backend/src/TenonAdmin.Services/Seed/DefaultMenuSeed.cs:19` · cat=test-gap · 反驳票 0/1

**问题** 我已逐条 byte 级核对 DefaultMenuSeed 全部 18 个按钮码与各控制器路由模板经 BuildPermissionCode(大写 Method + 冒号 + 小写模板,含 {sessionid}/{typecode}/{key} 小写占位符)算出的码——当前全部一字不差、干净。但这份一致性没有任何自动化守护:两处是分离的手写字符串,靠人肉同步。种子码写错一个字符即“授了也匹配不上”,该端点对非超管永久不可授权,且不会有任何编译/测试报错。

**失败场景** 有人把 SysSessionController 的 [HttpDelete("{sessionId}")] 改成 [HttpDelete("{id}")]、或给某控制器 Route 改了段名,忘了同步 seed → 该端点的 seed 码与运行时算出的码不再相等 → 后台勾选授权后普通用户仍 403,静默且难排查。

**建议** 加一个反射/端点枚举单测:遍历所有带 [RolePermission] 的 action,按 BuildPermissionCode 同规则算码,断言每个“真实存在且已 seed”的端点的码都能在 DefaultMenuSeed.HasData() 里找到完全相等项(反之种子里的按钮码也必须对应一个真实端点)。廉价且一劳永逸锁死漂移。

### P2-15 · [FIX] EnforceConcurrencyAsync 先查后删非串行化,并发登录可突破单端/限并发上限
**位置** `backend/src/TenonAdmin.Services/Session/SessionService.cs:145` · cat=concurrency · 反驳票 0/1

**问题** 淘汰逻辑是 check-then-act:先查活跃会话集合,再逐个 Revoke,随后调用方(OpenAsync)插入新会话,全程无锁/无串行化。阈值边界算术本身正确(Take(count+1-max)),但两个并发登录会读到相同的活跃集合、各自只淘汰到 max-1、再各自插入,导致最终活跃数超过上限。

**失败场景** mode=Limited、max=2、用户已有 2 个活跃会话(处于上限)。两个登录并发到达:都读到 active=[s1,s2],都算 Take(1) 只吊销 s1,然后都插入新会话 → 结果 s2+new1+new2=3 个活跃,超过 max=2。Single 模式同理会短暂出现 2 个活跃会话。该越额会持续到相关会话过期或该用户下次登录再触发一次淘汰才被修正。

**建议** 对『淘汰旧会话 + 插入新会话』按 userId 加锁串行化(单实例用 per-user SemaphoreSlim/锁字典),或以数据库层手段(如插入后基于 CreateTime 排序回收超额会话的原子语句/事务)兜底;并发要求高时再考虑分布式锁。

### P2-16 · [FIX] 会话开立非原子:refresh token 插入失败留下不可刷新的僵尸会话
**位置** `backend/src/TenonAdmin.Services/Session/SessionService.cs:34` · cat=transaction-boundary · 反驳票 0/1

**问题** OpenAsync 先插 SysSession 再插 SysRefreshToken,两步不在事务内。若刷新令牌插入在会话行已提交后失败,会残留一个无活跃刷新令牌的会话:它在 ListOnlineAsync(RevokedAt==null 且未过期)里显示为在线,却永远无法刷新;同时该次登录本身 500。影响面小(在线列表污染 + 该会话不可用直到 ExpiresAt 兜底),但属 §15 会话+令牌成对写入的半写口子。

**失败场景** 登录时 SysSession 插入成功、SysRefreshToken 插入因瞬时 DB 错误失败 → 用户收到 500 重登(得到新会话),旧会话行残留并在在线列表长期显示为'在线'直至过期,无法被刷新。

**建议** 把两条 InsertAsync 与缓存写入包进单个 sessions.Db.Ado.UseTranAsync(缓存写放在事务提交后),失败整体回滚。

### P2-17 · [DOC] 多实例默认内存缓存下强退/权限吊销不跨节点生效,设计未声明单节点边界
**位置** `backend/src/TenonAdmin.Services/Session/SessionService.cs:51` · cat=A01-broken-access-control / design-boundary · 反驳票 0/1

**问题** IsActiveAsync 命中缓存即判活跃;RevokeAsync(:124)只对处理该请求的进程调用 cache.RemoveAsync,RbacService.InvalidatePermissionsAsync(:99)/DataScopeProvider 同为进程内失效。RolePermissionAttribute(:38/:57/:61)每个受保护请求都读这些缓存。双节点+默认 MemoryCacheProvider 时,节点 A 强退/改权限只清了 A 的缓存,节点 B 仍命中旧 session:{sid}(TTL=会话过期,RefreshExpireMinutes 默认 7 天),路由到 B 的请求继续判活跃,强退不生效。perm:/scope: 同理:默认 PermissionMinutes=20 分钟才自愈,而配置注释(rebuild-design.md:226)允许 0=永不过期,此时被降权用户在 B 上永久保留权限。设计 §14/§15 承诺『强退即 401 / 权限变更即时生效』却未声明这些语义仅在单节点或分布式缓存下成立;只有 MemoryCacheProvider.cs:8 一句泛化提示,§14/§15 未复述该边界。跨节点失效广播只在 CacheChangeLogSubscriber 注释里作为未来扩展点提及、未实现。注:此项换共享 Redis 可修复(与 finding 1/2 不同),核心缺口是边界未文档化+默认多节点静默失效。

**失败场景** 两节点走负载均衡、用默认内存缓存。管理员强退一个被盗会话(或撤销某用户角色),节点 A 清缓存;凭同一 token/session 的后续请求被 LB 路由到节点 B,B 命中旧缓存,IsActiveAsync 返回 true,被强退会话在 B 上继续通过鉴权直到缓存 TTL(可达 7 天)到期;PermissionMinutes=0 配置下降权在 B 上永不生效。

**建议** 在 docs/rebuild-design.md §14/§15 显式声明:『默认单节点;多实例部署必须配置分布式 ICacheProvider(Redis),否则强退/权限吊销的即时性、登录锁定、验证码一次性均不保证』。并落地跨节点失效:利用已存在的 IEventBus 抽象把 session/perm/scope 失效事件在多副本间广播(替换 Channels 为分布式实现),或在多实例部署校验分支要求共享缓存。

### P2-18 · [FIX] 用户+角色关联非原子:半写留下无角色幽灵用户,账号被占且无法干净重建
**位置** `backend/src/TenonAdmin.Services/User/UserService.cs:80` · cat=transaction-boundary · 反驳票 0/1

**问题** AddAsync 先提交 SysUser 行,再调 SetUserRolesAsync,两步不在同一事务。若第二步抛错(DB 故障/校验),用户已落库而调用方收到失败,留下一个已提交、无角色的用户,其 Account 已被占用。UpdateAsync:97-99(资料 vs 角色)与 DeleteAsync:109-110(清角色 vs 软删)是同一非原子形态。对照 RbacService.ReplaceAsync:88 已用 UseTranAsync 正确包裹删+插,可复用同一手法。

**失败场景** AddAsync 插入用户成功后 SetUserRolesAsync 抛异常(如短暂 DB 错误)→ 用户已提交但无角色,接口对调用方返回错误。重试同账号命中 AccountExists;若管理员随后软删该幽灵用户再重建,又撞上上一条 P1 的 500。用户处于'看似失败实则存在'的错位态。

**建议** 把成对写入包进 users.Db.Ado.UseTranAsync(插用户+设角色一起提交或一起回滚),UpdateAsync/DeleteAsync 同理;缓存失效放到事务提交之后。

### P2-19 · [NOTE] LIKE 通配符 %/_ 未转义:关键字分页过滤返回超出字面子串的结果(功能性,非注入)
**位置** `backend/src/TenonAdmin.Services/User/UserService.cs:25` · cat=like-wildcard-injection-functional · 反驳票 0/1

**问题** 所有关键字分页过滤都用 SqlSugar 表达式 .Contains()/等价 LIKE,SqlSugar 生成的是参数化 SQL(LIKE '%'||@p||'%',@p 为绑定参数),因此不存在 SQL 注入。但 .Contains 未对用户输入里的 LIKE 元字符 % 和 _ 做转义,也未附带 ESCAPE 子句。用户输入中的 % 会被当作任意长度通配、_ 被当作单字符通配,导致过滤命中集大于'字面包含'的预期。这是功能/UX 层面的匹配不精确,不是安全注入面(值始终是绑定参数,不改变 SQL 结构)。同一模式遍布所有 Page 方法:UserService.cs:25-26(Account/Name)、File/FileService.cs:82(OriginalName)、Log/LogService.cs:70(Title)、Log/LogService.cs:78(Account)、Dict/DictService.cs:22-23(Code/Name)、Config/ConfigService.cs:21-22(Name/ConfigKey)、Position/PositionService.cs:15(Name)。

**失败场景** 管理员调用用户分页接口传 input.Account = "_"(或 "%"):SqlSugar 生成 Account LIKE '%'+@p+'%',@p='_'。SQLite/MySQL 把 LIKE 里的 _ 解释为单字符通配,模式 '%_%' 命中任何至少含一个字符的 Account,即几乎全表;搜 "100%" 时 % 同样被当通配。结果分页列表返回了并不真正字面包含所搜关键字的行,过滤形同虚设(且 Count 查询会全表扫描,大表上有额外开销)。无数据泄露/越权(全局数据范围过滤器仍叠加生效),仅是检索不精确。

**建议** 如需精确的'字面子串'语义,在过滤前转义 LIKE 元字符并声明 ESCAPE。最小改法(在各 Page 方法复用一个私有 helper):

// GOOD:转义 % _ \ 后按字面匹配
static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
// 用带 ESCAPE 的原生 LIKE(SqlSugar 表达式 .Contains 不发 ESCAPE 子句,需显式):
.WhereIF(!string.IsNullOrEmpty(input.Account),
         u => SqlFunc.Contains(u.Account, EscapeLike(input.Account!)))  // 若切原生 LIKE,补 ESCAPE '\\'

// 参数化本身已经到位(下面是当前写法,注入层面无需改动):
// u => u.Account.Contains(input.Account!)  // → LIKE '%'||@p||'%',@p 绑定,安全

ponytail:此项属功能精确性打磨,大多数后台框架接受 Contains 的通配行为;若产品认为'搜 % 即模糊匹配'可接受,可仅在 notes 记录为已知行为、不改代码。

### P2-20 · [FIX] 雪花 WorkerId 硬编码为 0,TenonAdmin:Id:WorkerId 配置项从未接入
**位置** `backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs:22` · cat=config-gap · 反驳票 0/1

**问题** 确认遗留线索(e)。SnowflakeIdGenerator 构造签名支持 workerId,但注册处永远传默认 0;SnowflakeIdGenerator.cs:42 的 XML 注释声称『由 AspNetCore 层从 TenonAdmin:Id:WorkerId 配置注入』,而全仓 grep 显示该配置键只在注释里出现,无任何读取代码。

**失败场景** 同一内核多实例水平扩展(常见生产形态)时,所有实例 WorkerId 均为 0,雪花号的机器号段相同;两实例在同一毫秒发号会产生重复 Id,导致主键冲突或数据错插。

**建议** 在 Core Options(如 AdminOptions 增 Id:WorkerId 节)读取配置,注册处改为 new SnowflakeIdGenerator(options.Id.WorkerId, sp.GetService<TimeProvider>());构造已有 0..1023 范围校验兜底。

### P2-21 · [DOC] 全局数据范围/软删过滤器只作用于查询,不覆盖 Update/Delete:DataEntity 落地后写路径可越权(IDOR)
**位置** `backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs:62` · cat=security-design-risk · 反驳票 0/1

**问题** AddTableFilter(软删的 ISoftDelete 与数据范围的 IOrgScoped)只对 SELECT 生效,对 Updateable/Deleteable 不生效——框架自身的软删(SqlSugarRepository.DeleteAsync 用 Updateable.SetColumns(IsDelete=true))正是依赖这一点。而通用仓储对外提供 DeleteAsync(long id)→UPDATE ... SET IsDelete=1 WHERE Id=@id 与 UpdateAsync(entity)→按主键整行更新,均不带范围谓词。设计 §4(rebuild-design.md:473)承诺'业务表继承 DataEntity 即自动受控,无需每个接口手写校验',但该自动受控只覆盖读与'先过滤读后写'路径,不覆盖直接按 id 的写。目前仓库零个 DataEntity 子类,故尚不可利用(潜伏);且现有各服务写前均 GetByIdAsync/GetAsync(带过滤)再写,恰好规避——但这是约定而非机制保证。

**失败场景** 未来业务表 Device:DataEntity,控制器按文档一行式 repo.DeleteAsync(id)/UpdateAsync(dto) 暴露删改。数据范围为'本机构'的用户传入他机构 Device 的 id:读被过滤看不到,但 DeleteAsync 发出的 UPDATE 无范围谓词、QueryFilter 又不作用于 Updateable,于是盲删/盲改成功(越权/IDOR),与设计承诺相悖。

**建议** 二选一:(a)在 UpdateAsync/DeleteAsync 内对 IOrgScoped/ISoftDelete 补范围谓词,或强制先经 AsQueryable 过滤读再写;(b)明确在文档标注 DataEntity 表的写授权非自动、必须每端点 load-then-write 强制。现有服务的'先读后写'即安全范式,建议固化为约定并在扩展点文档点明。

### P2-22 · [TEST] 测试缺口:会话强退→40006 信封、以及 OpenAPI 文档内容(信封结构 + DisabledModules 摘除)无用例
**位置** `backend/tests/TenonAdmin.Tests/AuthFlowTests.cs:52` · cat=test-gap · 反驳票 0/1

**问题** 两处 §设计承诺缺自动化验证:(1)§15“强退即时生效”——RolePermissionAttribute 第 38-42 行 IsActiveAsync 短路 → 401+40006——无 HTTP 级用例(现有会话相关测试未断言吊销后再请求得 40006)。(2)§13.6 承诺 openapi.json 为契约单源,§8 CI 把 openapi.json 作为 artifact,但无用例断言:内置端点响应 schema 含 Result 信封字段、且 Api:DisabledModules 摘除的控制器不出现在 /openapi/v1.json(当前只测了 DisabledModule 后 HTTP 404,未测文档一致性)。我已代码级确认 DisabledModuleConvention 在 ApplicationModel 构建期移除控制器,ApiExplorer 因此不会收录被禁模块,逻辑正确——但无回归锁。

**失败场景** 日后改动会话校验短路顺序、或 OpenAPI 生成管线/DisabledModule 约定执行时机变化,导致强退不再即时(仍放行)或被禁模块泄漏进公开契约文档,现有测试无法发现。

**建议** 补两个用例:(a)登录→取 sid→强制下线→用原 token 再请求,断言 401 且 code=40006;(b)拉 /openapi/v1.json,断言被禁模块路径缺席、且某内置端点的 200 schema 引用 Result 信封(至少含 code/msgKey/data)。
