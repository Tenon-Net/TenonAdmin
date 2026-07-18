# 请求管线

一个已认证请求从进入到返回，依次流经四道关卡：认证、`[RolePermission]` 授权、数据范围解析、结果信封。每一道都由内核内置，业务代码无需感知——下面按顺序拆开每一步做了什么、由哪个类型承担。

## 全景

```text
HTTP 请求
  │
  ├─①  认证   Microsoft JWT Bearer
  │          claim 不映射(sub / sid / sadm / unique_name)
  │          框架 401 challenge → 统一信封(40006)
  │
  ├─②  [RolePermission]   授权过滤器
  │          未认证 → 401;超管 sadm → 放行
  │          校验会话 sid 是否仍活跃(强退即时生效)
  │          权限码 = {METHOD}:/{路由模板},比对用户权限码集合
  │
  ├─③  数据范围   解析生效机构集,写入 IDataScopeContext
  │
  └─④  结果信封   裸 return dto → Result<T>
             AdminException / ErrorCode → 信封(数字码,不下发文案)
```

## ① 认证：Microsoft JWT Bearer

内核直接用 `Microsoft.AspNetCore.Authentication.JwtBearer`，不自造认证栈。装配在 `TenonAdminSetup.cs`:

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<SymmetricSecurityKey>((o, signingKey) =>
    {
        o.MapInboundClaims = false;   // 保留原始 claim 名,不做遗留映射
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = options.Jwt.Issuer,
            IssuerSigningKey = signingKey,
            ValidateAudience = false,          // 单体后台,不启用 audience
            ValidateLifetime = true,           // 校验 exp / nbf
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
        };
        o.Events = new JwtBearerEvents { OnChallenge = /* 见下 */ };
    });
```

**Claim 不映射**。`MapInboundClaims = false` 关掉了 .NET 默认那套把 `sub` 改写成一长串 XML namespace URI 的遗留映射，令牌里的 claim 名原样保留。内核约定的自定义 claim 名集中在 `TokenClaimNames`(`Core/Security/ITokenProvider.cs`):

| Claim | 常量 | 含义 |
| --- | --- | --- |
| `sub` | `JwtRegisteredClaimNames.Sub` | 用户主键 |
| `sid` | `TokenClaimNames.SESSION_ID` | 会话标识（强退锚点） |
| `sadm` | `TokenClaimNames.SUPER_ADMIN` | 超管标志（值为 `"true"` 时授权直接放行） |
| `org` | `TokenClaimNames.ORG_ID` | 归属机构 Id（数据范围锚点） |
| `unique_name` | `JwtRegisteredClaimNames.UniqueName` | 登录账号，映射为 `User.Identity.Name` |

**框架 401 被重塑为统一信封**。默认情况下，令牌缺失或过期时 JwtBearer 会返回一个空的 401，响应体不是内核的信封格式。`OnChallenge` 把它接管，改写成与业务出口一致的 `Result<T>`:

```csharp
OnChallenge = async ctx =>
{
    ctx.HandleResponse();
    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(Result<object>.Fail(ErrorCode.TokenInvalid));
};
```

`ErrorCode.TokenInvalid` 对应数字码 **40006**。这样前端无论碰到「令牌过期」还是「无权限」，拿到的都是同构的信封，可按码统一处理。

::: tip 默认拒绝
控制器端点经 `MapControllers().RequireAuthorization()` 默认要求认证，尊重 `[AllowAnonymous]`。登录、验证码等匿名端点显式放行，其余一律先过认证。
:::

## ② `[RolePermission]`：权限码就是路由

授权由 `RolePermissionAttribute`（实现 `IAsyncAuthorizationFilter`）承担。它**无参数、无权限字符串**——这是内核刻意的设计：代码里永远不出现 `"sys:user:add"` 之类的魔法串，授权靠在角色-菜单界面上勾选路由完成。

过滤器内部按固定顺序执行：

```csharp
// 1. 必须已通过 JWT 认证
if (user.Identity?.IsAuthenticated != true)
    → 401 + 40006

// 2. 会话活性校验:sid 对应会话被吊销/过期 → 401(超管同样受此约束)
var sessionId = user.FindFirstValue(TokenClaimNames.SESSION_ID);
if (!await sessions.IsActiveAsync(sessionId))
    → 401 + 40006

// 3. 超管直接放行 + 数据范围不受限
if (user.HasClaim(TokenClaimNames.SUPER_ADMIN, "true"))
{
    scopeContext.Current = DataScopeResult.Unrestricted;
    return;
}

// 4. 普通用户:解析数据范围写入上下文(见 ③)
// 5. 权限码比对
var code = PermissionCode.Build(method, routeTemplate);
if (!codes.Contains(code)) → 403 + 41001
```

**权限码 = 规范化路由**。`PermissionCode.Build` 是唯一真源：

```csharp
public static string Build(string httpMethod, string? routeTemplate) =>
    $"{httpMethod.ToUpperInvariant()}:/{(routeTemplate ?? "").TrimStart('/').ToLowerInvariant()}";
// 例:GET:/api/v1/ping
```

用**路由模板**而非实际路径，带参数的路由（`user/{id}`）权限码稳定，不随参数值变化。同一个 `Build` 函数被三处共用——授权比对、`MenuController.Routes` 路由清单（喂菜单表单的权限码下拉）、操作日志的缺省操作名——防止「授权时算的码」与「菜单里存的码」因大小写、斜杠差一个字符而静默对不上。

**会话活性校验让强制下线立即生效**。第 2 步每请求都调 `ISessionService.IsActiveAsync(sid)`。管理员在「在线用户」里踢人后，该会话的缓存被移除、库里标记吊销，被踢用户手里的 access token 哪怕未到期，下一个请求就会 401。超管也不例外。

::: tip `[ActiveSession]`：任意登录用户端点
个人中心、登出这类「任何已登录用户可用、但不需要具体权限码」的端点，挂 `ActiveSessionAttribute`。它只做上面的第 1、2 步（认证 + 会话活性），跳过权限码比对。若只挂 `[Authorize]` 而不挂它，会话被强退后未过期的令牌仍能继续调用——所以需要强退即时生效的端点必须挂 `[ActiveSession]`。
:::

## ③ 数据范围：解析并注入 `IDataScopeContext`

授权阶段（第 3、4 步）顺带把当前用户的**生效数据范围**解析出来，写进 `IDataScopeContext`:

```csharp
// 超管
scopeContext.Current = DataScopeResult.Unrestricted;

// 普通用户(走缓存)
var userId = long.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
scopeContext.Current = await dataScopeProvider.ResolveAsync(userId, abort);
```

之所以在授权阶段（动作执行前）就写入，是因为动作里的 DB 查询要用到它。写进去之后，同请求后续对 `DataEntity` 的查询会被 SqlSugar 全局过滤器自动按机构集过滤，业务代码不写一行过滤条件。数据范围的完整机制见 [多组织数据权限](./data-scope.md)。

## ④ 结果信封：裸返回也被包好

到了返回阶段，内核统一把出参包成信封 `Result<T>`，把业务错误转成信封。

**成功：裸 `return dto` 自动包壳**。`ResultEnvelopeFilter`（`IAsyncResultFilter`）让业务控制器直接 `return dto;` 也得到统一信封，不必每处手写 `Result.Ok(...)`:

```csharp
public static bool TryWrap(IActionResult result, out ObjectResult wrapped)
{
    wrapped = null!;
    if (result is not ObjectResult obj) return false;        // File/StatusCode 等不动
    if (obj.Value is IResultEnvelope) return false;          // 已是信封,放行
    if (obj.StatusCode is int sc && (sc < 200 || sc >= 300)) return false; // 非 2xx 不包
    wrapped = new ObjectResult(Result<object?>.Ok(obj.Value)) { StatusCode = obj.StatusCode };
    return true;
}
```

只包**成功（2xx）的裸 `ObjectResult`**;File、StatusCode、错误结果一律不动。内置控制器仍显式返回 `Result<T>`（OpenAPI 契约保真），对它们本过滤器是空操作。

::: warning 契约提示
过滤器在结果执行阶段才包信封，对 ApiExplorer 不可见。裸返回 `dto` 的端点，其 OpenAPI 200 schema 记的是 `dto`（无信封外壳），而运行时实为 `Result<dto>`。若该端点要经 `npm run gen:api` 生成前端类型，请**显式声明返回 `Result<T>`**（与内置控制器一致），否则前端会把 `data` 误当成顶层 dto。
:::

**失败：`AdminException` → 信封**。可预期的业务失败（账密错、验证码错、无权限……）抛 `AdminException`，由 `AdminExceptionFilter` 转成 HTTP 200 + 业务码信封：

```csharp
public void OnException(ExceptionContext context)
{
    if (context.Exception is not AdminException ex) return;
    logger.LogInformation("业务失败 {Code}({MsgKey}):{Path}", (int)ex.Code, ex.MsgKey, ...);
    context.Result = new ObjectResult(Result<object>.From(ex));
    context.ExceptionHandled = true;
}
```

业务失败记 **Information** 级日志（不是错误，不打扰告警）。其他异常不在这里拦——让框架默认 500 流程处理，保留完整堆栈，程序缺陷该大声失败。

**错误是数字码，从不下发本地化文案**。信封携带 `{ code, msgKey, args, message }`,`code` 是 `ErrorCode` 枚举的数字值。i18n 由前端按码翻译，后端不返回中文/英文错误文案。

## 一次调用回顾

以「删除某角色」为例：

1. **认证**——校验 JWT 签名与有效期，读出 `sub` / `sid` / `sadm`。
2. **`[RolePermission]`**——会话 `sid` 仍活跃;非超管;权限码 `DELETE:/api/v1/sys/role/{id}` 在用户权限码集合里 → 放行。同时把该用户的数据范围写入 `IDataScopeContext`。
3. **数据范围**——仓储的写路径守卫先经带范围过滤器的查询确认目标行在范围内，越权改删他机构行返回 0 行被拒。
4. **结果信封**——控制器 `return` 的结果被包成 `Result<T>`;若中途抛 `AdminException`，转成业务码信封返回。
