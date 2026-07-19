# 请求管线

你给自己的控制器加一个新接口，鉴权代码一行都不用写，方法上只放一个 `[RolePermission]`，还不带参数。权限码是它自己从路由算出来的，比如 `GET:/api/v1/ping`。所以全站没有一个硬编码的权限字符串，加权限就是在角色菜单界面上勾一下路由。管线另外三道关也是这个路子：位置固定，调用方零参数。

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

内核直接用 `Microsoft.AspNetCore.Authentication.JwtBearer`，不自己造一套认证栈。装配在 `TenonAdminSetup.cs`：

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

**Claim 不映射**。.NET 默认有一套遗留映射，会把 `sub` 改写成一长串 XML namespace URI。`MapInboundClaims = false` 把它关掉，令牌里的 claim 名就原样保留了。内核约定的那几个自定义 claim 名，集中在 `TokenClaimNames`，代码在 `Core/Security/ITokenProvider.cs`。

| Claim | 常量 | 含义 |
| --- | --- | --- |
| `sub` | `JwtRegisteredClaimNames.Sub` | 用户主键 |
| `sid` | `TokenClaimNames.SESSION_ID` | 会话标识（强退锚点） |
| `sadm` | `TokenClaimNames.SUPER_ADMIN` | 超管标志（值为 `"true"` 时授权直接放行） |
| `org` | `TokenClaimNames.ORG_ID` | 归属机构 Id（数据范围锚点） |
| `unique_name` | `JwtRegisteredClaimNames.UniqueName` | 登录账号，映射为 `User.Identity.Name` |

**框架 401 被重塑成统一信封**。令牌缺失或过期的时候，JwtBearer 默认返回一个空的 401，响应体不是内核的信封格式。`OnChallenge` 把它接管过来，改写成和业务出口一致的 `Result<T>`：

```csharp
OnChallenge = async ctx =>
{
    ctx.HandleResponse();
    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(Result<object>.Fail(ErrorCode.TokenInvalid));
};
```

`ErrorCode.TokenInvalid` 对应的数字码是 **40006**。这么一来，前端不管碰到「令牌过期」还是「无权限」，拿到的都是同构的信封，可以按码统一处理。

::: tip 默认拒绝
控制器端点经 `MapControllers().RequireAuthorization()` 默认都要求认证，但尊重 `[AllowAnonymous]`。登录、验证码这些匿名端点显式放行，其余的一律先过认证这一关。
:::

## ② `[RolePermission]`：权限码就是路由

授权由 `RolePermissionAttribute` 承担，它实现的是 `IAsyncAuthorizationFilter`。这个特性**不带参数，也不带权限字符串**。代码里永远不会出现 `"sys:user:add"` 这种魔法串。授权是靠在角色-菜单界面上勾选路由完成的。

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

这里用的是**路由模板**，不是实际路径。所以带参数的路由，权限码是稳定的，不随参数值变化，比如 `user/{id}`。同一个 `Build` 函数有三处共用：授权比对、`MenuController.Routes` 路由清单（喂给菜单表单的权限码下拉）、操作日志的缺省操作名。为什么要共用一个函数？这样「授权时算的码」和「菜单里存的码」才不会因为大小写、斜杠差了一个字符就静默对不上。

**会话活性校验让强制下线立即生效**。第 2 步每个请求都会调一次 `ISessionService.IsActiveAsync(sid)`。管理员在「在线用户」里踢完人，这个会话的缓存就被移除、库里也标记成吊销。被踢用户手里的 access token 哪怕还没到期，下一个请求照样 401。超管也不例外。

::: tip `[ActiveSession]`：任意登录用户端点
个人中心、登出这类端点，任何已登录用户都能用，但不需要具体的权限码。给它们挂 `ActiveSessionAttribute` 就行。它只做上面的第 1、2 步，也就是认证加会话活性，跳过权限码比对。要是只挂 `[Authorize]` 不挂它，会话被强退之后，没过期的令牌还能接着调。所以想让强退即时生效的端点，必须挂 `[ActiveSession]`。
:::

## ③ 数据范围：解析并注入 `IDataScopeContext`

授权阶段的第 3、4 步，顺带把当前用户的**生效数据范围**解析出来，写进 `IDataScopeContext`：

```csharp
// 超管
scopeContext.Current = DataScopeResult.Unrestricted;

// 普通用户(走缓存)
var userId = long.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
scopeContext.Current = await dataScopeProvider.ResolveAsync(userId, abort);
```

为什么要在授权阶段、也就是动作执行之前就写入？因为动作里的 DB 查询要用到它。写进去之后，这个请求后续对 `DataEntity` 的查询，都会被 SqlSugar 全局过滤器自动按机构集过滤，业务代码一行过滤条件都不用写。数据范围的完整机制见 [多组织数据权限](./data-scope.md)。

## ④ 结果信封：裸返回也被包好

到了返回阶段，内核会统一把出参包成信封 `Result<T>`，把业务错误也转成信封。

**成功：裸 `return dto` 自动包壳**。有了 `ResultEnvelopeFilter`，业务控制器直接 `return dto;` 也能拿到统一信封，不用每个地方都手写 `Result.Ok(...)`。这个过滤器实现的是 `IAsyncResultFilter`：

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

它只包**成功（2xx）的裸 `ObjectResult`**。File、StatusCode、错误结果一律不动。内置控制器仍然显式返回 `Result<T>`，为的是保住 OpenAPI 契约，所以对它们来说这个过滤器是空操作。

::: warning 契约提示
过滤器是在结果执行阶段才包信封的，ApiExplorer 看不到这一步。所以裸返回 `dto` 的端点，它的 OpenAPI 200 schema 记的是 `dto`，没有信封外壳，可运行时实际是 `Result<dto>`。要是这个端点要经 `npm run gen:api` 生成前端类型，请**显式声明返回 `Result<T>`**，和内置控制器保持一致。不然前端会把 `data` 误当成顶层 dto。
:::

**失败：`AdminException` → 信封**。账密错、验证码错、无权限这类可预期的业务失败，都抛 `AdminException`。`AdminExceptionFilter` 把它转成 HTTP 200 加业务码信封：

```csharp
public void OnException(ExceptionContext context)
{
    if (context.Exception is not AdminException ex) return;
    logger.LogInformation("业务失败 {Code}({MsgKey}):{Path}", (int)ex.Code, ex.MsgKey, ...);
    context.Result = new ObjectResult(Result<object>.From(ex));
    context.ExceptionHandled = true;
}
```

业务失败记的是 **Information** 级日志，因为它不是错误，不该去打扰告警。其他异常不在这里拦。框架默认的 500 流程会接手它们，保留完整堆栈。程序缺陷就该大声失败。

**错误是数字码，从不下发本地化文案**。信封里带的是 `{ code, msgKey, args, message }`，其中 `code` 是 `ErrorCode` 枚举的数字值。i18n 交给前端按码翻译，后端不返回中文或英文的错误文案。

## 一次调用回顾

以「删除某角色」为例：

1. **认证**：校验 JWT 签名与有效期，读出 `sub` / `sid` / `sadm`。
2. **`[RolePermission]`**：会话 `sid` 仍然活跃，用户不是超管，权限码 `DELETE:/api/v1/sys/role/{id}` 也在用户的权限码集合里，于是放行。同时把该用户的数据范围写入 `IDataScopeContext`。
3. **数据范围**：仓储的写路径守卫先用带范围过滤器的查询，确认目标行在范围内。越权改删别的机构的行，返回 0 行被拒。
4. **结果信封**：控制器 `return` 的结果被包成 `Result<T>`。中途要是抛了 `AdminException`，就转成业务码信封返回。
