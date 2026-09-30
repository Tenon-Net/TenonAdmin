# 系统集成技术参考

实现开放端点、自定义数据范围、出站客户端或可靠投递适配器时，可以在这里查阅完整开发约定。第一次配置接入应用、凭据和授权时，请先完成[系统集成教程](./integration.md)。

装上 `TenonAdmin.Integration`、接好两行之后，业务代码只剩三样：对外的 DTO、对方的协议、入队那一句。凭据、授权、数据范围、调用记录、目标地址安全和投递恢复都归框架。别的系统调你，写开放接口。你调别的系统时，结果要和本地业务一起成败就入队走可靠投递，否则直接普通调用。不装这个包，就没有 `itg_*` 表、接口、菜单和后台任务，已有功能一点不变。

Agent 施工用的逐步清单在仓库 `skills/wire-integration.md`。能直接跑的例子有两份：`dotnet new tenon-app --integration` 生成的 `Integrations/` 目录，以及内核仓库的 `backend/samples/IntegrationSample`，后者配本地可控第三方 `backend/samples/IntegrationMockPartner`，不用任何商业账号就能演示去重、查询确认、响应丢失和异步受理。

## 启用

```bash
dotnet add package TenonAdmin.Integration
```

接线和工作流包一样是两步，缺一步都不行：

```csharp
using TenonAdmin.Integration;

builder.Services.AddTenonAdminIntegration(builder.Configuration); // 选项与服务，全部 TryAdd
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
    o.UseIntegration();                                            // itg_* 建表、管理与开放端点挂载
});
```

只调了 `AddTenonAdminIntegration` 而漏掉 `UseIntegration` 时，启动直接报错，免得后台任务对着不存在的表空转。只引用包而两步都没调用时，模块控制器上的 `[NonController]` 会让默认发现跳过它们，因此不会暴露端点。只调用 `UseIntegration` 时会让 `itg_*` 实体参与建表，但不会注册控制器特性提供者、菜单和后台任务。不要采用这两种不完整接线；启用模块时应同时调用两个入口。

要替换其中任何一个内置服务，在 `AddTenonAdminIntegration` 之前注册同一接口。启用后多出这些东西：

- 后台菜单「第三方接入」：接入应用、开放接口调用记录、第三方接口调用记录、发送任务。`web/` 和 `web-react/` 两套模板都自带这四个页面，菜单与按钮权限随种子下发，模块没启用时菜单不出现。
- 两个编译型任务：`itg-delivery` 每 5 秒扫描一次投递，`itg-retention` 每天 03:30 清理过期记录，都受「任务调度」的开关、运行日志与告警管理。
- 独立的认证方案 `TenonOpenApp`，只认开放端点。

配置都在 `TenonAdmin:Integration` 节，缺省即默认值；非法值在启动时直接报错，消息里带出配置键。

## 开放接口：让第三方调你

### 写一个端点

开放端点就是一个普通控制器，多挂两个特性：

```csharp
public sealed record SampleDocDto(long Id, string Title, DateTimeOffset CreatedAt);

[ApiController]
[Route("api/open/v1/sample-docs")]
[OpenApi]
[OpenApiDataScope(OpenApiDataScopes.Org)]
public class SampleDocOpenController(IRepository<SampleDoc> docs) : ControllerBase
{
    [HttpGet]
    public async Task<Result<PagedList<SampleDocDto>>> Page([FromQuery] OpenApiPageInput input) =>
        Result<PagedList<SampleDocDto>>.Ok(await docs.AsQueryable().OrderBy(d => d.Id)
            .ToOpenPagedListAsync(input, d => new SampleDocDto(d.Id, d.Title,
                new DateTimeOffset(d.CreateTime, TimeZoneInfo.Local.GetUtcOffset(d.CreateTime)))));
}
```

启动时会逐个检查开放端点，下面任何一条不满足都拒绝启动：

- 路由以 `api/open/v{n}/` 开头；`api/open/` 下的动作都必须挂 `[OpenApi]`。
- 显式声明 HTTP 方法，并声明 `[OpenApiDataScope]`，键对应一个已注册的范围策略。
- 返回 `Result<T>`，`T` 里直接或经属性都不能含持久化实体。
- 不挂 `[RolePermission]`、`[ActiveSession]`、`[RequireReauth]` 或 `[AllowAnonymous]`。开放端点只用 `TenonOpenApp` 认证，用户令牌进不了开放端点，接入凭据也进不了后台接口。

第三条逼着你为对方单写 DTO：实体上的审计列、机构 Id 和内部字段一旦出现在开放文档里，就成了必须长期兼容的契约。分页入参用 `OpenApiPageInput`，沿用内核 `current`/`size` 约定，单页最多 100 条。开放端点的写操作不进用户操作日志，改记开放调用记录。

### 数据范围

每个开放端点都要说清它按什么隔离数据。应用没绑定端点声明的范围时，调用直接 403，不会退化成看全部数据。

| 键 | 含义 | 业务代码要做的 |
|---|---|---|
| `none` | 端点不读写受范围约束的数据 | 无；内核机构维度按拒绝处理 |
| `org` | 内置机构范围，绑定机构 Id，含全部下级 | 无；`DataEntity` 的查询与按主键改删由内核过滤器和仓储写守卫约束 |
| 自定义键 | 你的业务分区，如对接方编码 | 写一个策略，查询时 `WhereInScope`，写入前 `EnsureAllowed` |

自定义策略继承 `OpenApiDataScopePolicyBase`，给出键、名称和候选值；如果这个分区本身就是完整的隔离维度，再显式放开内核机构维度：

```csharp
public sealed class PartnerScopePolicy(ISqlSugarClient db) : OpenApiDataScopePolicyBase
{
    public override string Key => "partner";
    public override string Name => "工单对接方（示例）";

    public override async Task<IReadOnlyList<OpenApiScopeOption>> ListOptionsAsync(string? keyword, CancellationToken cancellationToken = default) =>
        (await db.Queryable<PartnerTicket>().Select(t => t.PartnerCode).Distinct().Take(200).ToListAsync(cancellationToken))
            .Select(c => new OpenApiScopeOption(c, c)).ToList();

    // 默认按拒绝处理内核机构维度;放开必须写出来
    protected override DataScopeResult ResolveKernelScope(OpenAppDataScope scope) => DataScopeResult.Unrestricted;
}

// 注册:集合注册,同一个键重复注册会在启动时报错
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IOpenApiDataScopePolicy, PartnerScopePolicy>());
```

控制器里注入 `IOpenAppContext`，列表用 `query.WhereInScope(app.DataScope, t => t.PartnerCode)` 过滤，写入前用 `app.DataScope.EnsureAllowed(input.PartnerCode)` 校验，越界返回业务码 `49004`。完整例子是 `IntegrationSample` 的 `PartnerTicketOpenController`。

`WhereInScope` 按数据库的比较规则匹配，`EnsureAllowed` 按原值精确比较。MySQL 和 SQL Server 的默认排序规则不区分大小写，`ab` 与 `AB` 在查询里会被当成同一个值，写入时却不是。范围编码要统一大小写，或者给这类列用区分大小写的排序规则。

### 应用、凭据与授权

这些都在「第三方接入 → 接入应用」里操作：

1. 新增应用，填编码、名称，可选归属机构与每分钟调用上限。归属机构是应用写入数据时的机构锚点；不设置时，应用写入的行只对不受限范围可见。
2. 发放凭据。凭据形如 `tna_<keyId>.<secret>`，完整值只在发放和轮换的响应里出现一次，库里只存摘要，后台和记录只显示 `keyId`。默认有效期 365 天，每个应用最多 5 把有效凭据。
3. 在「接口与数据权限」里逐个勾选开放端点，没勾的一律拒绝；再给端点用到的范围绑定取值。权限码与后台一样是规范化路由，如 `GET:/api/open/v1/sample-docs`。

轮换会发一把新凭据，旧凭据在并存窗口结束时过期，对方换好新凭据之前两把都能用。窗口留空按系统默认（`Credentials:DefaultRotationOverlapHours`，默认 24 小时），最长 `Credentials:MaxRotationOverlapHours`（默认 720 小时）；填 0 即旧凭据立刻失效。撤销和停用对所有副本在下一次请求时生效：每个开放请求都按 `keyId` 读库核对凭据与应用状态，没有进程内凭据缓存。

扩大或恢复应用能力的操作只有超级管理员能做：授权端点、绑定数据范围、发放和轮换凭据、调整凭据到期、启用应用、修改归属机构。其他管理员即使有这些按钮权限，也会收到 `41003`。这与内核「角色授权和数据范围仅限超级管理员」同源：能决定一把凭据看到多少数据的人，就等同于超级管理员。停用、撤销、删除这类止损操作，以及修改名称、说明和调用上限，可以按按钮权限交给其他管理员。

### 调用约定

对方把凭据放在请求头 `X-Api-Key`，凭据不从 URL 读取。可以带 `X-Request-Id`，合法时原样回显；每个响应都带 `X-Trace-Id`，排障时按它在「开放接口调用记录」和异常日志里都能找到。响应沿用内核的 `{ code, data, ... }` 信封，失败时 HTTP 状态与业务码对应：

| HTTP | 业务码 | 含义 |
|---|---|---|
| 401 | `49001` | 凭据缺失、错误、过期、已撤销或应用已停用，对外不区分原因 |
| 403 | `49002` | 应用没有被授予这个端点 |
| 403 | `49003` | 应用没有绑定端点声明的数据范围 |
| 429 | `49005` | 超过应用的每分钟调用上限 |
| 429 | `49006` | 同一来源认证失败过多，暂时拒绝 |
| 400 | `49007` | 请求参数不合法，`args.errors` 是字段错误 |

每个应用每分钟默认 600 次（`OpenApi:DefaultRateLimitPerMinute`，应用上可单独设置，0 表示不限）。认证失败按来源计数：格式正确的凭据按「IP + `keyId`」分别计数，格式错误的按 IP 计数，每分钟超过 30 次（`OpenApi:AuthFailuresPerMinutePerIp`）后在查库之前就返回 429。一个来源反复用错凭据，不会连累同一出口后面的其他应用。部署在反向代理后面时要打开内核的转发头配置，否则所有来源都算作代理的 IP。没装 Redis 时这些计数按副本各算各的，N 个副本相当于 N 倍阈值。

版本放在路由里。同一版本内只做兼容变更（加可选字段、加端点）；不兼容的改动放到新版本，在 `OpenApi:Versions` 里追加 `v2`，旧版本继续服务到对方迁移完。每个版本单独一份 OpenAPI 文档，只含开放端点，在「接入应用」页下载，对应管理接口 `GET /api/v1/integration/catalog/open-api/{version}`，受后台权限保护，生产环境不匿名公开。

「开放接口调用记录」记下每次调用的应用、凭据 `keyId`、端点、状态码、业务码、结果、失败原因、耗时、追踪标识和来源 IP。请求体、响应体和请求头一概不记。

## 普通调用：你调第三方

### 配置目标

每个第三方是一个命名目标：

```json
"TenonAdmin": {
  "Integration": {
    "Outbound": {
      "Targets": {
        "partner": {
          "BaseUrl": "https://partner.example.com/api/",
          "TimeoutSeconds": 10,
          "TrustedCidrs": [],
          "Auth": { "Type": "Bearer" }
        }
      }
    }
  }
}
```

`Auth.Type` 可选 `None`、`Bearer`、`Header`（配 `HeaderName`）和 `Basic`（配 `Username`）。秘密不写在这里，用环境变量 `TenonAdmin__Integration__Outbound__Targets__partner__Secret`、`dotnet user-secrets` 或密钥库配置源提供；每次调用时现取，改了配置下一次调用就生效。凭据来源另有要求时，在 `AddTenonAdminIntegration` 之前注册自己的 `IOutboundCredentialProvider`。

目标地址按解析结果逐个判定：

- 回环、私网和 CGNAT 地址默认拒绝，解析结果落在 `TrustedCidrs` 列出的网段内才放行。调内部系统时显式列出它的网段，不会因为「支持内网」就放开任意目标。
- 链路本地（含云元数据地址）、未指定、组播与保留地址恒拒绝，列进 `TrustedCidrs` 也无效。
- 每次建连都按当时的 DNS 结果复检；不跟随重定向，3xx 按拒绝处理；不走系统代理。基础地址写成 IP 的，启动时就判定。

### 写客户端

继承 `OutboundAdapterBase`，只写路径和结果含义：

```csharp
public class PartnerClient(OutboundAdapterServices services) : OutboundAdapterBase(services)
{
    protected override string TargetName => "partner";

    public virtual async Task<PartnerInfo?> FindAsync(string code, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync($"partners/{Uri.EscapeDataString(code)}",
            new OutboundCallOptions { Operation = "partner.get" }, cancellationToken);
        if (response.StatusCode == 404) return null;
        response.EnsureSucceeded(allowAccepted: false); // 未完成即抛 4903x,前端按码提示
        return ReadJson<PartnerInfo>(response);
    }
}

builder.Services.TryAddScoped<PartnerClient>();
```

`GetAsync`、`PostJsonAsync`、`PutJsonAsync`、`DeleteAsync` 的路径相对 `BaseUrl`，写成绝对地址或用 `../` 跳出都会被拒绝。每次调用都带 `X-Request-Id`，值就是这次的调用 Id。对方要签名或先换令牌时覆写 `AuthenticateAsync`，秘密用 `GetCredentialAsync` 取；对方用响应体表达受理或业务失败时覆写 `Classify`。

框架不做任何自动重试。写请求每次新建连接并带 `Connection: close`，传输层也不会替你把它再发一遍。结果分成下面几类，默认按 HTTP 状态判定：

| 结果 | 默认判定 | 对方执行了吗 |
|---|---|---|
| `Succeeded` 成功 | 2xx（202 除外） | 执行了 |
| `Accepted` 已受理 | 202 | 接下了，最终结果待确认 |
| `AuthenticationFailed` 认证失败 | 401、403 | 没有 |
| `Rejected` 拒绝 | 其他 4xx、3xx | 没有 |
| `NotSent` 未发出 | DNS、建连、TLS 失败，目标被拦截，凭据缺失，429 | 没有 |
| `Unknown` 结果未知 | 超时、连接中断、408、所有 5xx（包括带 `Retry-After` 的 503） | 可能执行了 |
| `Cancelled` 已取消 | 调用方取消 | 可能执行了 |

未发出的结果另带 `Transient` 标记：网络失败和限流属于暂时性问题，稍后重发可能成功；目标被拦截、凭据缺失或不合法是配置问题，重发也没用。网关在上游已经执行请求之后也可能回 503。`Retry-After` 只说明建议的重试时间，不能证明未执行，因此 503 一律按结果未知处理；有效的重试时间仍会保留。

`EnsureSucceeded` 把未完成的结果翻成业务码：目标未配置 `49030`，未发出 `49031`，被拒 `49032`，结果未知 `49033`，已受理 `49034`（仅在 `allowAccepted: false` 时）。「第三方接口调用记录」记下每次调用的目标、操作、方法、地址（不含查询串）、状态码、结果、原因、耗时和调用 Id，请求头、请求体和响应体一概不记。

## 可靠投递：与业务同一事务的外呼

普通调用发生在事务外，本地提交和对方执行各管各的。需要「本地写成了，对方就一定会收到」时，在业务事务里入队，由后台投递器送达。

### 入队

```csharp
var result = await docs.Db.Ado.UseTranAsync(async () =>
{
    var doc = new SampleDoc { Title = title };
    await docs.InsertAsync(doc);
    await outbox.EnqueueAsync(new DeliveryRequest
    {
        Adapter = SampleDocSyncAdapter.AdapterName,
        Operation = "doc.create",
        DeliveryKey = $"sample-doc:{doc.Id}",     // 稳定的业务键,所有重试与人工操作都用它作幂等标识
        BusinessKey = doc.Id.ToString(CultureInfo.InvariantCulture),
        Payload = new SampleDocPayload(doc.Title), // 只放业务数据
    }, cancellationToken);
});
if (!result.IsSuccess) throw result.ErrorException;
```

投递记录和业务行写在同一个库、同一个事务里：提交则两者都在，回滚则两者都没有，进程重启后照样被处理。几条规矩：

- 入队必须在活动事务里，否则返回 `49041`。确实没有业务写入要一起提交时，设 `Standalone = true`。
- `DeliveryKey` 用稳定的业务键。同一个键、同样的内容重复入队返回同一条记录；同一个键配了不同的适配器、操作或载荷则返回 `49042`。
- 载荷里不放秘密，凭据在每次投递时由框架现取。
- 事务里不做网络调用。业务主库之外的副库不和主库同事务，入队必须和业务写在同一个库。

### 适配器

适配器继承 `DeliveryAdapterBase`，按名字注册：

```csharp
public class SampleDocSyncAdapter(OutboundAdapterServices services) : DeliveryAdapterBase(services)
{
    public const string AdapterName = "sample-doc-sync";
    public override string Name => AdapterName;
    protected override string TargetName => "partner";

    public override bool SupportsIdempotency => true; // 对方确实按幂等标识去重才能写 true
    public override bool SupportsQuery => true;       // 对方能按幂等标识查询结果时写 true

    public override async Task<DeliverySendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken)
    {
        var response = await PostJsonAsync("tickets", context.ReadPayload<SampleDocPayload>(), context.SendOptions, cancellationToken);
        return DeliverySendResult.From(response, ReadJson<PartnerTicketStatus>(response)?.TicketNo);
    }

    // SupportsQuery 为 true 时实现 QueryAsync:把对方状态翻成 Succeeded、Accepted、Failed 或 NotFound
}

builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeliveryAdapter, SampleDocSyncAdapter>());
```

`context.SendOptions` 已经带上幂等标识（按目标的 `IdempotencyHeader`，默认 `Idempotency-Key`）和投递记录的关联。两个能力声明决定了结果未知时框架怎么做，所以必须与对方真实行为一致：对方并不去重，你却声明了 `SupportsIdempotency`，响应丢失时框架会用同一标识重发，对方就会再执行一次。拿不准就保持 `false`。

适配器自己构造 `NotSent` 结果时（例如对方用 200 响应体表示「稍后再来」），要让框架按退避重发，就把 `Transient` 设为 `true`；不设按配置类问题处理，直接转「重试耗尽」。

适配器名写进了投递记录，改名前先处理完存量记录。名字须为 1 到 64 位字母、数字及 `._-`，不区分大小写唯一，否则拒绝启动。

### 投递器怎么处理

后台任务领取到期的记录，在事务外调用适配器，再按领取时的栅栏回写。领取之后记录若被别人改过，这次回写不生效，迟到的结果只追加尝试记录。结果未知时按对方能力分流，所有重发都沿用同一个幂等标识：

| 对方能力 | 结果未知时 |
|---|---|
| 按幂等标识去重 | 按退避重发 |
| 只能查询 | 先查询，按对方的真实结果推进 |
| 两者都没有 | 转「待核对」并告警，不自动重发 |

进程在调用途中崩溃、租约过期后，恢复逻辑同样按这张表处理，不会只因租约到期就认定可以安全重发。未发出的结果中，网络和限流类按退避重发：从 30 秒起翻倍，最长 1 小时，对方给了 `Retry-After` 时取更长的那个；发满 8 次或入队满 24 小时转「重试耗尽」。目标被拦截、凭据缺失这类配置问题不自动重发，直接转「重试耗尽」并告警，改好配置后人工重试。已受理的记录可查询时每 60 秒轮询一次，否则等对方回调，72 小时仍未确认就转「待核对」。

| 状态 | 含义 |
|---|---|
| 待处理 | 等待发送或重发 |
| 处理中 | 投递器正在处理 |
| 已受理待确认 | 对方接下了，还没有最终结果，这不是成功 |
| 成功 | 对方已完成 |
| 待核对 | 结果未知且不能安全重发，要人核实 |
| 重试耗尽 | 次数或时间用完 |
| 失败 | 对方明确拒绝或认证失败 |
| 已取消 | 人工关闭 |

对方用回调告知最终结果时，写一个开放端点，把对方的状态翻成成功或失败，交给 `IDeliveryConfirmationService.ConfirmAsync(new DeliveryConfirmation { ... })`。回调端点和其他开放端点一样要授权给具体应用；为了不让一个对方确认另一个对方的投递，还有三条约束：

- `Adapters` 必填，列出这个端点负责的适配器，别的适配器的投递一律当作不存在。
- 端点声明业务范围（例如 `partner`），先按调用应用的范围核实投递对应的业务行，再把它的业务键填进 `BusinessKey`。
- 还没发出过的投递不接受回调。

```csharp
[HttpPost("ticket-status")]
public async Task<Result<PartnerTicketCallbackResult>> TicketStatus(PartnerTicketCallback input, CancellationToken cancellationToken)
{
    // 标识来自对方,不能直接信任:先反解出工单,按本应用的工单对接方范围核实,看不到就当不存在
    var ticket = PartnerTicketService.TryParseTicketId(input.DeliveryKey, out var ticketId)
        ? await tickets.Query().Where(t => t.Id == ticketId).WhereInScope(app.DataScope, t => t.PartnerCode).FirstAsync(cancellationToken)
        : null;
    var outcome = ticket is null
        ? DeliveryConfirmOutcome.NotFound
        : await confirmations.ConfirmAsync(new DeliveryConfirmation
        {
            DeliveryKey = input.DeliveryKey,
            Succeeded = input.Status == "done",   // 按对方协议翻译;完整示例对未知状态直接拒绝
            Adapters = [PartnerTicketSyncAdapter.AdapterName, LegacyTicketSyncAdapter.AdapterName],
            BusinessKey = ticket.Id.ToString(CultureInfo.InvariantCulture),
            RemoteReference = input.TicketNo,
        }, cancellationToken);
    return Result<PartnerTicketCallbackResult>.Ok(new(outcome.ToString()));
}
```

不满足任何一条都返回 `NotFound`，不透露记录是否存在。重复回调返回 `AlreadyApplied`，与已确认结果相反的回调不自动改写。完整例子是 `IntegrationSample` 的 `PartnerCallbackOpenController`。

记录进入待核对、耗尽或失败时，每个 `IDeliveryAlertSink` 都会收到一条告警，默认实现写一条 Warning 日志；要接邮件或即时通讯，用 `TryAddEnumerable` 追加实现。

### 人工处理

「发送任务」页按状态筛选，详情里有载荷、每次尝试的时间线和能跳到对应出站调用的调用 Id。可执行的操作由服务端按状态、对方能力和尝试历史计算，页面只负责显示：

- 重试：失败，或本轮发送里没有「对方不去重、结果未知」的耗尽记录。沿用原幂等标识。
- 查询对方结果：适配器可查询时。
- 确认已成功、确认未执行：必须写处理说明；确认未执行之后按常规规则重新发送。

重试、确认未执行，以及查询得知对方没有这条记录时，都会开启新一轮发送：次数预算、截止时刻、确认时限和对方引用都重新开始，上一轮已经人工处理过的结果不再影响本轮。
- 取消：关闭记录，不再自动处理。

操作时回传页面看到的栅栏值，记录已被投递器或他人改动就返回 `49046`，刷新后再决定；状态不允许的操作返回 `49045`，直接调接口也绕不过去。所有操作都记操作人和说明，并进入操作日志。

## 部署与保留

- 多副本：凭据与应用状态每次读库，撤销和停用即时生效；投递按栅栏领取，多个副本同时扫描也只会有一个成功；两个任务由调度器选主执行。开放接口的两个限流计数要跨副本共享时装 Redis。
- 秘密：出站秘密只来自配置源或你的凭据提供器，不进库、不进载荷；调用记录、管理接口和日志都不回显。开放凭据的原文只在发放和轮换时返回一次。
- 代理：出站调用不走系统代理，否则地址判定会失效。必须经代理出网时，在代理一侧限制可达目标。
- `Delivery:LeaseSeconds`（默认 120）必须大于最长的出站超时加安全余量 `Delivery:LeaseSafetyMarginSeconds`（默认 30 秒），启动时检查，你在 `AddTenonAdminIntegration` 之前自己注册的选项实例也一样检查。投递里的单次调用指定了更长的超时时，会被截断到「租约减安全余量」并记一条警告，调用不会拖过租约。

`itg-retention` 每天按下表清理，天数填 0 表示永久保留：

| 配置 | 默认 | 清理什么 |
|---|---|---|
| `Retention:InboundLogDays` | 30 | 开放调用记录 |
| `Retention:OutboundLogDays` | 30 | 出站调用记录 |
| `Retention:DeliveryDays` | 90 | 成功和已取消的投递及其尝试记录，按完成时刻算 |

待处理、处理中、待确认、待核对、耗尽和失败的投递永不自动删除。

## 故障排查

| 现象 | 原因与处理 |
|---|---|
| 启动报 `TenonAdmin:Integration:...` | 配置非法，消息指明了键 |
| 启动报「开放接口声明不合法」 | 消息逐条列出违规的动作，按「写一个端点」的四条规则改 |
| 启动报「可靠投递适配器声明不合法」 | 适配器名格式不对或重名 |
| 开放调用 401 `49001` | 凭据错、过期、已撤销或应用已停用。对外不区分原因，「开放接口调用记录」里有具体原因 |
| 开放调用 403 `49002`、`49003` | 端点没勾选，或端点声明的范围没绑定 |
| 管理操作返回 `41003` | 扩大应用能力的操作只有超级管理员能做，见「应用、凭据与授权」 |
| 启动报缺少 `UseIntegration` | 调了 `AddTenonAdminIntegration`，却没在 `AddTenonAdmin` 的选项里调 `UseIntegration` |
| 回调确认返回 `NotFound` | 投递不属于端点声明的适配器、业务行不在调用应用的范围内，或这条投递还没发出过 |
| 出站 `target_unknown`，业务码 `49030` | 目标名没配置 |
| 出站 `target_blocked` | 地址被拒：内部地址要列进 `TrustedCidrs`，链路本地与元数据地址永远不放行 |
| 出站 `credential_missing` | 没提供秘密，请求没有发出 |
| `dns_failed`、`connection_failed`、`tls_failed`、`connect_timeout` | 未发出，可以稍后重发 |
| `timeout`、`transport_error`、`response_ended` | 结果未知，对方可能已执行，先核实再处理 |
| 入队 `49041`、`49042`、`49043` | 不在事务里、同一标识配了不同内容、适配器没注册 |
| 投递停在「待核对」 | 结果未知且对方既不去重也不能查询。在对方系统核实后确认已成功或确认未执行 |
| 长时间「已受理待确认」 | 对方还没完成。检查轮询间隔 `Delivery:ConfirmPollSeconds` 或对方回调是否授权到位 |
