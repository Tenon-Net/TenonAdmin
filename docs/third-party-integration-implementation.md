# 第三方接入模块：实现契约

状态：G01–G13 全部落地并通过完整验收（2026-09-29）。本文是[已确认设计](third-party-integration-design.md)与 [ADR 0008](adr/0008-system-integration-boundary.md) 的唯一实现契约说明，各目标按本文落点实现（见[实施计划](third-party-integration-goals.md)），实际落点变化已同步回本文。验收证据记录在[证据记录](third-party-integration-evidence.md)。

本文只确定包、类型归属、数据契约与状态语义，不代表对应代码已经存在；每节末尾的「落点」列出由哪个目标实现。

## 1. 现有能力核对（复用点与限制）

| 能力 | 依据（文件 / 符号） | 可复用 | 限制与处理 |
|---|---|---|---|
| 认证注册 | `TenonAdminSetup.AddTenonAdmin`：`AddAuthentication(JwtBearer)`，`MapControllers().RequireAuthorization()` 默认拒绝 | ASP.NET 多认证方案；端点级 `[Authorize(AuthenticationSchemes=...)]` 会让授权中间件只用该方案重建 `HttpContext.User` | JWT 是默认方案。开放接口必须显式声明独立方案，否则会落到 JWT；靠启动校验兜底（见 §4.5） |
| 用户授权 | `RolePermissionAttribute`、`ActiveSessionAttribute`、`PermissionCode.Build` | 规范化路由权限码 `VERB:/route` 的唯一真源 `PermissionCode.Build` | 两个特性都假定存在 `sub`/`sid`（`long.Parse(sub!)`），应用主体绝不能进入这两条通道 |
| Controller 发现 | `TenonAdminOptions.ApplicationAssemblies` → CodeFirst 实体扫描 + `AddApplicationPart` | 卫星包沿用 `UseWorkflow()` 两步接线 | 未登记程序集的实体不建表、控制器 404 |
| 数据范围 | `IDataScopeContext`（HTTP 实现 `HttpContextDataScopeContext`，默认 `Unrestricted`）、`SqlSugarSetup` 的 `AddTableFilter<IOrgScoped>`、`SqlSugarRepository.InScopeAsync` 写路径守卫 | 应用请求在授权阶段写入 `IDataScopeContext.Current`，即可复用机构维度的读过滤与按主键改删守卫 | **未设置即不受限**：开放请求必须在进入动作前显式写入范围，默认写成拒绝（空机构集合） |
| 审计填充 | `SqlSugarSetup` 的 `Aop.DataExecuting`：`CreateUserId`←`ICurrentUser.UserId`，`CreateOrgId`←`ICurrentUser.OrgId`；`HttpContextCurrentUser` 读 `sub` / `org` claim | 应用主体不带 `sub` → `UserId=null`，不伪造用户；带 `org` claim → 插入行锚定到应用归属机构 | 内核审计列只有用户维度；应用身份记入开放调用记录（§5） |
| 操作审计 | `OperationLogFilter`（全局，写操作一律记 `sys_op_log`） | 管理员对接入应用的操作自动进入用户操作审计 | 开放接口的写调用会被误记为「无操作人的用户操作」。需内核新增一个豁免标记（§10） |
| 统一返回 | `Result<T>`、`AdminExceptionFilter`（业务异常 → HTTP 200 + 业务码）、`ResultEnvelopeFilter`、`PagedList<T>`、`PagedListExtensions.MAX_SIZE=200` | 开放接口沿用同一信封与分页模型 | 开放接口页大小另设更低上限（默认 100） |
| 限流 | `RateLimitMiddleware`（按 IP 固定窗口，计数走 `ICacheProvider.IncrementAsync`） | 未认证入口天然受内核 IP 限流保护；按应用计数复用同一原子自增 | 未装 Redis 时计数是进程内的：N 副本 = N × 阈值（与内核一致，文档明示） |
| OpenAPI | `services.AddOpenApi()` 产出 `/openapi/v1.json`，仅开发环境 `MapOpenApi()`；`Microsoft.AspNetCore.OpenApi` 10.0.9 提供按文档名的 `IOpenApiDocumentProvider` | 默认 `ShouldInclude` 为 `GroupName == null \|\| GroupName == 文档名`：给开放端点设 `GroupName="open-v1"` 即自动离开后台文档、进入独立文档 | 生产不匿名映射；独立文档经受权限保护的管理端点下载（§4.6） |
| HTTP 目标安全 | `JobHttpFence`（静态 URL 校验、`ConnectCallback` 解析后 IP 复检、禁代理、禁自动重定向）、`JobHttpClient` | `IsBlocked`/`TryParseCidr`/`ValidateHeader` 为 public static，可直接复用 | 其默认**不封内网**（调度器主用途），不符合本模块「内部地址须显式受信」要求；出站另建按目标隔离的客户端与更严默认（§6.2） |
| 可靠派发参考 | `WfOutbox`、`WfOutboxStore.EnqueueAsync`（调用方事务内 ensure-insert）、`WfOutboxConsumerStore`（条件 UPDATE 领取、CAS 回写）、`WfOutboxDispatcher`（领取 / 事务外调用 / 回写三段）、`WfOutboxJob` + `WfOutboxJobSeed` | 事务边界三段式、条件 UPDATE 领取、MySQL 秒边界处理、`IAdminJob` + `sys_job` 种子 | 工作流 outbox 是 at-least-once：租约到期即重投。本模块**不能**因租约到期就重发（结果未知），恢复规则见 §8.4；属于工作流包，不能据此声称通用投递已存在 |
| 后台任务 | `IAdminJob`、`ISeedData<SysJob>`、`JobSchedulerService`（DB 选主）、`AdminJobsOptions.SchedulerEnabled` | 投递扫描与保留清理沿用编译类任务 + 种子，受调度开关、运行日志与告警机制管理 | 只在注册处理器与种子时存在；模块未启用即无任务 |
| 卫星包模式 | `WorkflowSetup.AddTenonAdminWorkflow` / `UseWorkflow`、`WorkflowMenuSeed`（`ConsumerMin + 47_000` 段）、`WorkflowErrorCode`（48xxx）、`ExcelSetup` | 同样的两步接线、种子号段、错误码自管常量 | — |
| 事务 | `ISqlSugarClient`（`SqlSugarScope` 单例）、`db.Ado.UseTranAsync`、`db.Ado.IsAnyTran()`（仓内已用） | 入队在调用方同一 scope 客户端上执行，天然加入其事务 | 副库（`AdditionalDatabases`）与主库不是同一事务，不承诺原子性 |

## 2. 包、启用方式与依赖

- **一个卫星包** `TenonAdmin.Integration`（`backend/src/TenonAdmin.Integration`），引用 `TenonAdmin.AspNetCore` 与 ASP.NET 共享框架，不新增任何第三方依赖；元包 `TenonAdmin` 不引用它（装了才有）。入站、出站与可靠投递共用实体、权限、菜单和任务，拆包只会增加接线步骤而无依赖收益，因此首版不拆。
- **启用**（与工作流同构的两步，缺一不可）：

```csharp
builder.Services.AddTenonAdminIntegration(builder.Configuration);            // TryAdd 注册选项与服务
builder.Services.AddTenonAdmin(builder.Configuration, o => o.UseIntegration()); // 实体建表 + 控制器挂载
```

- **未启用**（未安装或未调用上面两行）：没有 `itg_*` 表、没有菜单种子、没有后台任务；认证方案不注册，现有登录、数据范围和双前端启动不受影响。未安装时也没有管理与开放端点；安装了包却两行都没调时，Web SDK 仍会把包里的控制器作为应用部件发现，这些地址因认证方案未注册而报错（工作流包同样如此），因此不用本模块就不要引用它。只调了 `AddTenonAdminIntegration` 而没调 `UseIntegration` 时，`IntegrationStartupValidator` 在任何托管服务启动前（含建表与种子）直接失败，避免后台任务对着不存在的表空转。模块内不再设第二个总开关，也不设单独的投递开关（避免出现「菜单存在但功能被关」的不可用菜单）；「显式启用的可靠投递」指调用方在需要可靠送达的操作上显式入队，框架不对普通调用自动重试。
- 命名空间统一为 `TenonAdmin.Integration`（与工作流包一致，消费者只需一个 using）。目录：`Abstractions/`（SPI、契约、错误码、选项）、`Entities/`、`OpenApi/`、`Apps/`、`Outbound/`、`Delivery/`、`Controllers/`、`Jobs/`、`Seed/`。
- 配置节 `TenonAdmin:Integration`，绑定 `IntegrationOptions`，启动时校验（非法值 fail-fast）。前置注册的 `IntegrationOptions` 实例在配置绑定和回调之前优先，用同一实例校验、注册服务与版本化文档。工厂注册可替换运行时选项，但文档版本须在注册期通过配置、`configure` 或前置实例声明；工厂额外声明未注册的文档版本时，启动即报接线错误，不能留到下载时返回 500。

## 3. 数据库实体与索引

全部表前缀 `itg_`。均**不**继承 `DataEntity`：后台任务没有请求级数据范围，`IOrgScoped` 过滤会让扫描静默返回 0 行（与 `WfOutbox` 同理）；管理访问由路由权限控制。业务时间列一律 UTC 且列名带 `Utc` 后缀；基类 `CreateTime/UpdateTime` 仍是 AOP 填的本地时间，二者不得比较或相减。超长文本用 `StaticConfig.CodeFirst_BigString`，外部错误文本 C# 侧截断到 512。

| 表 / 实体 | 基类 | 关键列 | 索引 |
|---|---|---|---|
| `itg_app` `IntegrationApp` | `BaseEntity`（软删） | `Code`(输入 ≤64，列宽 96 以容纳软删后缀)、`Name`(64)、`Description`(256)、`Enabled`、`OwnerOrgId?`、`RateLimitPerMinute?`、`StateVersion`(long) | 唯一 `Code`（软删由仓储 `_del_{id}` 释放） |
| `itg_app_credential` `IntegrationAppCredential` | `BaseEntity`（仅已撤销后可软删） | `AppId`、`KeyId`(32，非秘密)、`SecretHash`(64，SHA-256 hex)、`Name`(64)、`ExpiresAtUtc?`、`RevokedAtUtc?`、`RevokedBy?`、`RotatedFromId?`、`LastUsedAtUtc?` | 唯一 `KeyId`；`AppId` |
| `itg_app_grant` `IntegrationAppGrant` | `AuditEntity` | `AppId`、`Permission`(256，`VERB:/api/open/v1/...`) | 唯一 (`AppId`,`Permission`) |
| `itg_app_scope` `IntegrationAppScope` | `AuditEntity` | `AppId`、`ScopeKey`(64)、`AllValues`、`ValuesJson`(BigString) | 唯一 (`AppId`,`ScopeKey`) |
| `itg_inbound_log` `IntegrationInboundLog` | `AuditEntity` | `AppId?`、`AppCode?`、`CredentialId?`、`KeyId?`、`HttpMethod`、`Route`(权限码)、`Path`(不含查询串)、`StatusCode`、`ResultCode`、`Outcome`、`FailureReason?`、`ElapsedMs`、`TraceId`、`ClientRequestId?`、`ClientIp?` | `CreateTime`；(`AppId`,`CreateTime`)；`TraceId` |
| `itg_outbound_log` `IntegrationOutboundLog` | `AuditEntity` | `Target`、`Operation?`、`HttpMethod`、`Url`(scheme+host+path)、`StatusCode?`、`Outcome`、`Reason?`(64)、`ErrorSummary?`(512)、`ElapsedMs`、`CallId`、`TraceId?`、`DeliveryId?`、`AttemptNo?` | `CreateTime`；(`Target`,`CreateTime`)；`DeliveryId`；`CallId` |
| `itg_delivery` `IntegrationDelivery` | `AuditEntity` | `DeliveryKey`(128，稳定幂等标识)、`Adapter`(64)、`Operation`(64)、`BusinessKey?`(128)、`PayloadJson`(BigString)、`PayloadHash`(64)、`Status`、`Fence`、`AttemptCount`、`BudgetStartAttempt`、`MaxAttempts`、`NextAttemptAtUtc`(非空)、`LeaseUntilUtc?`、`LeaseOwner?`、`DeadlineAtUtc`、`ConfirmDeadlineAtUtc?`、`VerifyBeforeSend`、`LastOutcome?`、`LastError?`(512)、`RemoteReference?`(128)、`CompletedAtUtc?`、`ResolvedBy?`、`ResolutionNote?`(256) | 唯一 `DeliveryKey`；(`Status`,`NextAttemptAtUtc`)；(`Adapter`,`Status`)；`CompletedAtUtc`；`BusinessKey` |
| `itg_delivery_attempt` `IntegrationDeliveryAttempt` | `AuditEntity` | `DeliveryId`、`AttemptNo`、`Kind`(Send/Query/Recovery/Manual/Confirm/Expired)、`Trigger`(Worker/Manual/Callback)、`OperatorId?`、`StartedAtUtc`、`FinishedAtUtc?`、`Outcome`(32)、`HttpStatus?`、`CallId?`(64，关联出站调用记录)、`ErrorSummary?`(512)、`Note?`(256)、`NodeName?` | (`DeliveryId`,`Id`) |

落点：G02（app/credential）、G03（grant/scope）、G04（inbound log）、G07（outbound log）、G08–G09（delivery/attempt）。

## 4. 开放接口：身份、认证与授权

### 4.1 凭据格式与校验

- 形如 `tna_{keyId}.{secret}`：`keyId` 为 16 位小写 hex（8 字节随机数，唯一索引，非秘密，可展示）；`secret` 为 32 字节 `RandomNumberGenerator` 随机数的 base64url（43 字符）。格式不合法直接判无效，不查库。
- 服务端只存 `SHA-256(secret)` 的 hex（不存任何秘密片段）；比较用 `CryptographicOperations.FixedTimeEquals`。秘密只在创建/轮换响应中返回一次，查询接口永不回显。高熵随机秘密不需要慢哈希。
- 校验顺序：格式 → 按 `KeyId` 唯一索引取凭据及其应用（一次查询）→ 摘要比较 → 撤销 → 过期（`ExpiresAtUtc <= now`）→ 应用存在、未删、已启用。缺失、错误、过期、撤销、应用停用统一对外返回 `49001`（不暴露具体原因），具体原因只写入调用记录（凭据可识别时）。
- 默认服务 `IOpenAppCredentialValidator`（`OpenAppCredentialValidator`，TryAdd，方法 virtual），前置注册即替换（例如接企业身份平台）。

### 4.2 系统身份载体

`OpenAppIdentity`（`AppId`、`AppCode`、`CredentialId`、`KeyId`、`OwnerOrgId`、`StateVersion`）转为认证方案 `TenonOpenApp` 的 `ClaimsPrincipal`：`tenon_app_id`、`tenon_app_code`、`tenon_app_credential`，以及应用设置了归属机构时的内核 `org` claim。**不写 `sub`、`sid`、`sadm`**：

- `ICurrentUser.UserId == null`、`IsSuperAdmin == false`：审计 AOP 不伪造 `CreateUserId/UpdateUserId`；复用的业务服务按「已认证非超管」处理，不会走超管放行路径。
- `ICurrentUser.OrgId == 应用归属机构`：未显式赋值的插入行 `CreateOrgId` 落到归属机构（数据范围锚点）。未设置归属机构时留空，行只对不受限范围可见，文档提示管理员设置。
- 请求内经 `IOpenAppContext`（`HttpContext.Items` 载体）读取当前应用与其数据范围。

### 4.3 多副本一致性（停用 / 撤销 / 授权变更）

- 每个开放请求都按 `KeyId` 读库校验凭据与应用状态，不做进程内凭据缓存：撤销、过期、停用在所有副本上**下一次请求即生效**，与缓存提供者无关。
- 授权与范围绑定按 (`AppId`,`StateVersion`) 做缓存键；任何授权、范围、启停、归属机构变更都在同一事务里递增 `StateVersion`。由于版本号每次随凭据查询一起从库里读出，旧缓存天然失效，内存缓存与 Redis 下行为一致。
- 应用与凭据的变更只写目标列，不整行回写：`StateVersion` 只由递增语句写入，不会回退到用过的版本号；启停以「未删除」为条件；凭据到期调整与轮换对旧凭据的截止以「未撤销」为条件，与撤销并发时撤销优先（影响 0 行即 `49016`）。并发的停用、撤销、删除不会被另一个管理员的保存覆盖。
- 已撤销凭据可单独软删（`DELETE /api/v1/integration/app/{id}/credentials/{credentialId}`）；删除以应用、凭据、已撤销且未删除为条件，列表与校验立即排除该行，审计行及轮换来源关系仍保留。未撤销返回 `49023`。
- `LastUsedAtUtc` 节流回写（默认 5 分钟一次，条件 UPDATE），尽力而为，不影响请求结果。

### 4.4 授权管线

1. `MapControllers().RequireAuthorization()` 的默认策略与开放特性 `[OpenApi]`（实现 `IAuthorizeData`，`AuthenticationSchemes="TenonOpenApp"`）合并：授权中间件**只**用 `TenonOpenApp` 方案认证并替换 `HttpContext.User`，JWT 用户令牌进不了开放端点；后台端点只用默认 JWT 方案，`X-Api-Key` 头被忽略 → 401。两条通道互不替代。
2. 认证失败的挑战由 `TenonOpenApp` 处理器写统一信封（401 + `49001`，附 `WWW-Authenticate: ApiKey header="X-Api-Key"`）；多值凭据头按格式错误处理。凭据可识别（标识存在）的失败写入调用记录，不可识别的只写不含凭据的信息级日志，避免未认证洪泛放大写库。认证失败按来源计数：格式正确的凭据按 (IP, `KeyId`) 计数，格式错误的按 IP 计数（默认 30 次/分钟），超阈值在查库前直接 429（`49006`）；一个来源反复出错不会连累同一出口的其他应用。
3. 开放授权过滤器（`[OpenApi]` 附带的 `IAsyncAuthorizationFilter`）依次：
   - 取权限码 `PermissionCode.Build(method, routeTemplate)`，不在应用授权集合 → 403 `49002`（默认拒绝）。
   - 解析端点声明的数据范围（§4.5），应用未绑定该范围 → 403 `49003`。
   - 写入 `IOpenAppContext` 与内核 `IDataScopeContext.Current`。
   - 按应用限流（默认 600 次/分钟，应用可单独设置，计数键 `itg:rl:{appId}:{window}`）→ 超限 429 + `Retry-After`（`49005`）。
4. 管理员不能通过开放通道管理应用自身授权：管理端点全部是 `[RolePermission]` 后台接口，应用主体无法进入（见第 1 点）。
5. 扩大或恢复应用能力的管理操作仅限超级管理员：授权端点、绑定数据范围、发放与轮换凭据、调整凭据到期、启用应用（包括新建时显式或默认启用）、修改归属机构。普通管理员新建必须明确传 `enabled: false`。已认证的非超管即使持有路由权限也返回 `41003`（`SuperAdminRequired`），系统与未认证上下文视为可信。依据与内核 QA09/QA36 相同：能决定一把凭据看到多少数据的人等价于超管。停用、撤销、删除等止损操作与名称、说明、限流等元数据修改仍按路由权限授予。守卫是各服务的 `protected virtual EnsureSuperAdmin()`；内置 `org` 范围的候选机构对非超管只列其数据范围内的机构及祖先（与内核机构列表一致，`OrgOpenApiDataScopePolicy.VisibleOrgIdsAsync`）。

### 4.5 开放声明与启动校验

消费者只写业务 DTO、服务调用与声明：

```csharp
[ApiController]
[Route("api/open/v1/tickets")]
[OpenApi]                                   // 独立认证 + 授权 + 文档分组 + 调用记录
public class TicketOpenController(ITicketService tickets, IOpenAppContext app) : ControllerBase
{
    [HttpGet("{id}")]
    [OpenApiDataScope("partner")]           // 必须：数据范围声明
    public async Task<Result<TicketOpenDto>> Get(long id) => ...;
}
```

启动时（托管服务，失败即拒绝启动）校验全部动作：带 `[OpenApi]` 的路由必须以 `api/open/v{n}/` 开头、必须有生效的 `[OpenApiDataScope]` 且键已注册、不得同时挂 `[RolePermission]`/`[ActiveSession]`/`[RequireReauth]`/`[AllowAnonymous]`；路由位于 `api/open/` 下却没有 `[OpenApi]` 的动作同样拒绝；没有 `[OpenApi]`、却在授权元数据（`IAuthorizeData.AuthenticationSchemes` 或授权策略）里指定 `TenonOpenApp` 方案的动作也拒绝——它会以接入应用身份运行却绕过授权与范围检查。

返回字段白名单即输出 DTO：启动校验遍历 DTO 属性与泛型参数（以已访问类型集合终止循环），拒绝声明返回类型（展开 `Task<>`、`Result<>`、`PagedList<>`、集合后）直接或间接包含持久化实体（`PrimaryId` 派生类型）的开放动作，避免直接序列化实体。`object`、非泛型集合、无法静态验证的接口或抽象输出也在启动时拒绝，消费者应声明具体 DTO。

### 4.6 数据范围模型

- **范围策略**：`IOpenApiDataScopePolicy`（`Key`、`Name`、候选值 `ListOptionsAsync`、绑定校验 `ValidateBindingAsync`、内核机构维度映射 `ResolveKernelScopeAsync`），`TryAddEnumerable` 注册。内置：
  - `org`：值为机构 Id，绑定某机构即含其下级（与 `DataScopeProvider` 的子孙计算一致）；映射为 `DataScopeResult.Restricted(机构集合)`，`AllValues` 映射为 `Unrestricted`。列表、详情经全局过滤器约束；按主键更新/删除经 `SqlSugarRepository.InScopeAsync` 守卫；插入的 `CreateOrgId` 未赋值时取应用归属机构，显式赋值时须调用 `OpenAppDataScope.EnsureAllowed(long?)`。
  - `none`（`OpenApiDataScopes.None`）：端点声明不读写受范围约束的数据，无需绑定；内核机构维度写成拒绝（空集合）。
- **自定义策略**（消费者按业务定义，如合作方、仓库）：基类 `OpenApiDataScopePolicyBase` 的 `ResolveKernelScopeAsync` 默认返回**拒绝**（空机构集合），业务维度已构成分区时须在子类里显式返回 `Unrestricted`（全量须显式声明）。消费者的查询与写入使用 `IOpenAppContext.DataScope`：`query.WhereInScope(scope, x => x.PartnerCode)` 过滤列表与详情（范围外按不存在处理，取值以参数传入 SQL），`EnsureAllowed(value)` 校验写入目标（范围外 `49004`，按原值精确比较；查询则按数据库排序规则比较，MySQL、SQL Server 默认不区分大小写，范围编码须统一大小写或用区分大小写的排序规则）。框架保证策略必被解析与强制绑定，不承诺自动识别任意 SQL 的业务归属。
- **绑定**：`itg_app_scope` 每应用每范围一行，`AllValues=true` 为管理员显式授予全量；未绑定即拒绝，从不退化为全量。

### 4.7 开放接口约定

- 路由 `/api/open/v{n}/...`；独立 OpenAPI 文档 `open-v{n}`（`TenonAdmin:Integration:OpenApi:Versions`，默认只有 `v1`），模块约定给开放控制器设置 ApiExplorer `GroupName`，开放文档的 `ShouldInclude` 只收本分组；后台文档 `v1` 因分组不同自动排除开放端点，前端生成类型也不含外部契约。文档含 `X-Api-Key` 安全方案。开发环境由内核 `MapOpenApi()` 匿名提供 `/openapi/open-v1.json`；生产环境只能经 `GET /api/v1/integration/catalog/open-api/{version}`（`[RolePermission]`）由管理员下载，内部用 `IOpenApiDocumentProvider` 生成。
- 返回沿用 `Result<T>` 信封与数字业务码：认证失败 401、授权/范围拒绝 403、限流 429，业务失败沿用内核「HTTP 200 + 业务码」。开放端点必须声明返回 `Result<T>`（启动校验），保证文档与运行时信封一致。框架级 4xx（模型校验等 ProblemDetails）改写为同状态码 + `49007` 信封（`args.errors` 为字段错误）；未处理异常由排序最后执行的开放异常出口转成 500 + `50000` 信封（`args.traceId`），内核异常留痕照常先执行。分页沿用 `PagedList<T>`（`current/size/total/pages/items`），输入 `OpenApiPageInput`（`current`/`size`，页码 ≤0 归一为 1，页大小 ≤0 归一为 20，上限为常量 `OpenApiPageInput.MaxSize = 100`），`ToOpenPagedListAsync` 完成分页与 DTO 映射。
- 时间：开放 DTO 使用 `DateTimeOffset`，序列化为带偏移的 ISO 8601（模板与示例示范）；内部本地时间列不直接暴露。
- 追踪：每个开放响应（含 401/403/429/500）带 `X-Trace-Id`，取请求标识 `HttpContext.TraceIdentifier`，与调用记录的 `TraceId`、500 信封的 `args.traceId` 以及内核异常留痕 `sys_exception_log.TraceId` 是同一个值，对方报来的标识能直接查到异常记录；客户端可传 `X-Request-Id`（≤64，字符集 `[A-Za-z0-9._:-]`），合法则原样回显并写入调用记录，不合法则丢弃。
- 凭据只从请求头读取；查询串里出现形似凭据（`tna_` 前缀）的值时直接 401 拒绝，不校验、不落库原文。
- 版本：同一主版本内兼容演进，破坏性变更进入新版本路由与新文档分组。

落点：G02（4.1–4.3 服务与载体、管理后端）、G03（4.4–4.6）、G04（4.7、限流、调用记录）。

## 5. 审计表达

- 实体审计列语义不变：应用新增的行 `CreateUserId` 为空（不伪造），`CreateOrgId` 为应用归属机构或显式范围内机构；应用更新行时审计 AOP 只刷新 `UpdateTime`，`UpdateUserId` 保持原值（应用新增的行即为空），「最后由哪个应用改的」以开放调用记录为准。
- 应用身份记入 `itg_inbound_log`（应用、凭据 `KeyId`、路由权限码、结果、耗时、追踪标识），与 `sys_op_log`（用户操作审计）分开；开放端点带内核豁免标记，不再进入 `sys_op_log`（§10）。
- 管理员创建应用、授权、改范围、发放/轮换/撤销凭据、人工核对投递均走后台 `[RolePermission]` 接口，自动进入 `sys_op_log`；凭据类与授权类接口另挂 `[RequireReauth]`（仅在启用 TOTP 能力时生效）。
- 调用记录默认不记录请求/响应正文与任何认证头；`Path` 不含查询串；凭据只以 `KeyId` 出现。

## 6. 普通出站调用

### 6.1 凭据来源与目标配置

- 目标在 `TenonAdmin:Integration:Outbound:Targets:{name}` 配置（目标名 1–64 位字母数字及 `._-`，不区分大小写）：`BaseUrl`（http/https，不含 userinfo、查询串与片段；统一补齐结尾 `/`）、`TimeoutSeconds`（缺省取 `Outbound:DefaultTimeoutSeconds`=30）、`TrustedCidrs`、`Auth`（`Type`=`None|Bearer|Header|Basic`、`HeaderName`、`Username`）、`IdempotencyHeader`（默认 `Idempotency-Key`）。`Outbound:MaxResponseBytes`（默认 1 MB）限制读入内存的响应体，超出截断并标记。配置非法启动即失败，消息只指明配置键、不带秘密。
- 秘密由 `IOutboundCredentialProvider` 在**每次调用时**取得。默认 `ConfigurationOutboundCredentialProvider` 先读 `IConfiguration`（`...:Targets:{name}:Secret`，可由环境变量、用户机密或支持热更新的配置提供方供给，换秘密无需重启），缺失时回落到注册模块时代码设置的值；消费者前置注册即可换成密钥管理服务。秘密不入库、不进投递载荷、不回显、不写日志；`OutboundCredential.ToString()` 恒为掩码。首版不建设通用密钥托管。
- `IOutboundAuthenticator` 按配置把凭据施加到请求（默认 `ConfiguredOutboundAuthenticator` 覆盖上面四类；Bearer/自定义头的秘密只允许可见 ASCII，否则按 `credential_invalid` 不发送）；缺秘密抛 `OutboundCredentialUnavailableException`，请求不发出（`NotSent`，`credential_missing`，不可自动重试）。适配器可按调用覆写认证步骤（签名、换取令牌）。

### 6.2 目标地址安全

- `OutboundHttpClientFactory`（单例，`TryAddSingleton`）按目标隔离 `HttpClient` + `SocketsHttpHandler`：禁用代理、禁用自动重定向（3xx 按拒绝处理）、`PooledConnectionLifetime` 5 分钟、建连超时 = 调用超时的一半（1–15 秒）。每个目标两个客户端：读（GET/HEAD/OPTIONS）走连接池；写（其余方法）每次新建连接并带 `Connection: close`，不依赖 `SocketsHttpHandler`「复用连接失败后重试」这类未公开的内部判定，并且每次写都按当下的解析结果复检地址。
- 静态校验：仅 http/https、不含 userinfo；字面 IP 的 `BaseUrl` 在启动校验时即按地址策略判定。`ConnectCallback` 对**解析后的每个 IP** 复检（覆盖 DNS 变化与 rebinding；解析步骤 `ResolveAsync` 可覆写，覆写结果同样逐个经过策略）。恒拒绝：未指定（`0.0.0.0/8`、`::`）、链路本地（`169.254.0.0/16` 含云元数据、`fe80::/10`）、组播与保留/广播（`224.0.0.0/4`、`240.0.0.0/4`、`ff00::/8`），列入 `TrustedCidrs` 也无效；回环、私网、CGNAT、`198.18.0.0/15`、`::1`、`fc00::/7`、`fec0::/10` 仅当落在该目标 `TrustedCidrs` 内才放行；IPv4 映射地址与 NAT64 前缀 `64:ff9b::/96` 先取出内嵌 IPv4 再判定。复用 `JobHttpFence.IsBlocked/TryParseCidr`。
- 请求路径必须相对 `BaseUrl` 解析且不得越出（开头的 `/` 视为相对基础地址；绝对地址、`../` 越界抛 `ArgumentException`）。附加请求头名须为 RFC 9110 token、值只允许可见 ASCII（拦 CRLF 注入，也避免非 ASCII 在发送时才失败）；`X-Request-Id`、幂等标识头、`Host`/`Connection`/`Content-Length` 等由框架管理，调用方不能自行设置。这里不直接调用 `JobHttpFence.ValidateHeader`：它抛任务模块的 47xxx 业务码且允许非 ASCII，不适合作为适配器编程错误的检查。

### 6.3 结果分类

`OutboundOutcome`（默认 `DefaultOutboundResultClassifier`；可前置注册替换，适配器也可按调用覆写）：

| 分类 | 默认判定 | 含义 |
|---|---|---|
| `Succeeded` | 2xx（202 除外） | 远端确认完成 |
| `Accepted` | 202，或适配器识别的「已受理」 | 远端已受理、最终结果待确认，**不等于成功** |
| `AuthenticationFailed` | 401、403 | 认证/授权失败，远端未执行 |
| `Rejected` | 其他 4xx（408/429 除外）、3xx、适配器识别的业务拒绝 | 远端明确拒绝，未执行 |
| `NotSent` | DNS 失败、连接失败、TLS 失败、建连超时、目标被拦截、凭据缺失、429 | 请求未被远端处理；`Transient` 标明稍后重发是否可能成功（网络与限流类为真，拦截与凭据类为假），尊重 `Retry-After` |
| `Unknown` | 调用超时、连接中断、响应不完整、408、所有 5xx（包括带 `Retry-After` 的 503）、分类器异常 | 远端可能已执行也可能未执行 |

| `Cancelled` | 调用方取消 | 请求可能已发出，按结果未知对待 |

`Retry-After` 只说明建议的重试时间，不能证明远端未执行。503 默认一律按 `Unknown`，仍保留有效的重试时间供具备去重能力的恢复使用；只有具体适配器能依据对方协议证明请求在副作用前被拒绝，才可覆写为 `NotSent`。

- 传输异常按 `HttpRequestException.HttpRequestError` 映射：`NameResolutionError`→`dns_failed`、`ConnectionError`→`connection_failed`、`SecureConnectionError`→`tls_failed`（均 `NotSent`）；建连超时（`OperationCanceledException` 内含 `TimeoutException`，调用双方令牌均未触发）→`connect_timeout`（`NotSent`）；调用超时→`timeout`、`ResponseEnded`→`response_ended`、其余→`transport_error`（均 `Unknown`）。已收到状态行但响应体没读完同样按 `Unknown`（`response_incomplete`）。
- 基础调用层**不做任何自动重试**，异常一律转成分类结果返回；只有调用方编程错误（目标名为空、路径越界、请求头非法）抛 `ArgumentException`。取消与超时分开记录。
- 每次调用写 `itg_outbound_log`（不含查询串、请求头与正文；原因短码 `Reason`、错误摘要截断到 512，传输异常使用固定安全摘要，认证、分类、适配器及凭据来源的异常只记类型、不向应用日志传入异常对象），请求带 `X-Request-Id`（调用 Id，32 位十六进制），带幂等标识时按目标 `IdempotencyHeader` 发送。记录走注入的同一个 `ISqlSugarClient`：在调用方事务内发起的外呼，其记录随该事务提交或回滚——需要与业务原子的外呼用可靠投递（§7）。
- 消费者写第三方适配器时继承 `OutboundAdapterBase`（构造只收 `OutboundAdapterServices` 聚合），只实现目标名、协议路径与载荷，按需覆写 `AuthenticateAsync`、`Classify`、`JsonOptions`；地址安全、超时、追踪、分类与记录全部复用。
- 后台接口需要把第三方失败提示给界面时调用 `OutboundResponse.EnsureSucceeded()`：`49030` 目标未配置、`49031` 未发出、`49032` 被拒绝（含认证失败）、`49033` 结果未知、`49034` 已受理待确认（`allowAccepted=false` 时），args 含 `target`、`reason`、`callId`。
- 管理接口 `api/v1/integration/outbound-log`：`page`（按目标、操作名、结果、投递、调用 Id、追踪标识、时间筛选）与 `targets`（目标清单，只回答能否取到秘密）；菜单「第三方接口调用记录」（`ConsumerMin + 49_050` 起）。出站记录按 `Retention:OutboundLogDays`（默认 30 天）清理。

落点：G07。

## 7. 事务内投递记录

- 入口 `IDeliveryOutbox.EnqueueAsync(DeliveryRequest)`（默认 `DeliveryOutbox`，`TryAddScoped`）：在注入的同一个 `ISqlSugarClient`（`SqlSugarScope`）上执行，**不另开连接或事务**，因此与调用方 `db.Ado.UseTranAsync(...)` 内的业务写入同属一个事务。
- **无活动事务**（`db.Ado.IsAnyTran()==false`）默认拒绝（`49041`），防止误以为有原子性；确需单独入队时设置 `DeliveryRequest.Standalone=true`，语义为「允许在无事务时独立提交，不与任何业务写入原子」；调用时若恰在事务内，记录仍随该事务提交或回滚。
- **跨库**：投递表在主库；业务数据在副库（`AdditionalDatabases`）时不属于同一事务，框架不承诺原子性（文档明示，建议投递与业务同库）。
- **幂等标识**：`DeliveryKey` 由调用方给出稳定业务键（如 `ticket-created:{id}`，1–128 位可见 ASCII——它会作为请求头发给对方），全生命周期不变；未给出时生成 `dlv_{雪花}`。重复入队：同键且适配器（不区分大小写）、操作、载荷摘要一致 → 返回已有记录（`Created=false`）；不一致 → `49042`。并发同键插入由唯一索引兜底，异常原样抛给调用方事务（与 `WfOutboxStore` 同理，不在事务内吞唯一冲突）。
- **载荷**：`Payload`（对象，Web 命名序列化）与 `PayloadJson`（原始 JSON）二选一，统一规范化为紧凑 JSON（同一内容不论写法摘要相同），上限 `Delivery:MaxPayloadBytes`（默认 256 KB）。载荷为业务 JSON，禁止放入认证秘密；凭据在投递时由 §6.1 取得。
- **校验**（写入前完成，失败不写任何行）：适配器须已注册（`49043`）；`Operation` 1–64 位字母数字及 `._:-`、`BusinessKey` ≤128、`MaxAttempts` 1–100、`MaxAge` 1 分钟–30 天、载荷合法且不超限，否则 `49044`（args.field 指明字段）。`NotBefore` 可延迟首次处理。
- **适配器契约**：`IDeliveryAdapter`（`Name`、`SupportsIdempotency`、`SupportsQuery`、`SendAsync`、`QueryAsync`），消费者通常继承 `DeliveryAdapterBase`（基于 §6.3 的 `OutboundAdapterBase`），以 `TryAddEnumerable` 注册为 scoped；启动时校验名称合法且不区分大小写唯一。`DeliveryContext.SendOptions` 已把幂等标识、投递 Id 与尝试序号带进出站调用记录。

落点：G08。

## 8. 可靠投递：状态、领取与恢复

### 8.1 状态

| 状态 | 值 | 含义 | 终态 |
|---|---|---|---|
| `Pending` | 0 | 待处理（含退避等待、待校验后发送） | 否 |
| `Dispatching` | 1 | 已领取、调用进行中（租约内） | 否 |
| `AwaitingConfirmation` | 2 | 远端已受理，等待最终确认 | 否 |
| `Succeeded` | 3 | 远端确认成功 | 是 |
| `NeedsReconciliation` | 4 | 结果未知且不可安全重试，待人工核对 | 否（需人工） |
| `Exhausted` | 5 | 次数或时间上限耗尽 | 否（需人工） |
| `Failed` | 6 | 确定性失败（业务拒绝、认证失败） | 否（需人工） |
| `Cancelled` | 7 | 人工关闭 | 是 |

### 8.2 领取与回写

- 扫描候选：`Pending|AwaitingConfirmation` 且 `NextAttemptAtUtc <= now`，以及租约已过期的 `Dispatching`（最早到期在前，每轮 `Delivery:BatchSize`=20）。
- 领取（`DeliveryStore.ClaimAsync`，单条条件 UPDATE 自动提交）：`WHERE Id=@id AND Fence=@seen AND Status=@seenStatus`（栅栏未变即说明读到的到期条件仍成立），成功则 `Status=Dispatching`、`Fence+1`、`LeaseUntilUtc=now+租约`、`LeaseOwner=节点`（`Jobs:NodeName`，缺省 `{机器名}#{进程号}`，仅供排障），发送类动作 `AttemptCount+1`。
- 调用在事务外进行；回写（短事务：CAS 迁移 + 追加尝试记录）`WHERE Id=@id AND Fence=@myFence AND Status=Dispatching`，同时 `Fence+1`、清空租约。影响 0 行说明已被恢复或他人重新领取：迟到结果只追加尝试记录（备注「迟到的结果」），不覆盖新领取结果；唯一例外是迟到的成功/受理证据可以把仍处于 `NeedsReconciliation` 的记录推进（同样 CAS 当前 Fence）。回写不跟随调用方取消：停机时也尽量落库，落不了由租约过期后的恢复接手。
- `Fence` 是唯一的并发栅栏（每次领取、回写、恢复、人工操作、回调确认都递增），`AttemptCount` 只统计真实发送次数。租约默认 120 秒，须大于最长目标超时加安全余量 `Delivery:LeaseSafetyMarginSeconds`（默认 30 秒）；启动时校验的是实际解析到的选项实例（含消费者在 `AddTenonAdminIntegration` 之前自己注册的）。投递中的发送与查询若按调用指定了更长的超时，由 `OutboundHttpInvoker.ResolveTimeout` 截断到「租约减安全余量」并记 Warning，调用不会拖过租约而与恢复重叠；普通调用不受影响。
- 不发生调用的状态变更（截止时刻已过、受理确认超时、次数上限、适配器已移除）按看到的栅栏直接迁移，并追加 `Kind=Expired` 的尝试记录。适配器抛出的异常按结果未知处理（摘要只记异常类型）。

### 8.3 结果处理

| 调用结果 | 迁移 |
|---|---|
| `Succeeded` | → `Succeeded` |
| `Accepted` | → `AwaitingConfirmation`，记录 `RemoteReference`，确认时限从本次受理起重新计算，按确认间隔轮询 |
| `NotSent`（`Transient`） | 预算内 → `Pending`（指数退避，上限 1 小时，尊重 `Retry-After`）；否则 → `Exhausted` |
| `NotSent`（非 `Transient`：目标被拦截、凭据缺失或不合法） | → `Exhausted`（不自动重发，告警；改好配置后人工重试，未发出过的记录重试总是安全的） |
| `Unknown` | 适配器声明去重 → 同上按可重试处理；声明可查询 → `Pending` + `VerifyBeforeSend`（先查询再决定）；两者皆无 → `NeedsReconciliation` |
| `AuthenticationFailed` / `Rejected` | → `Failed` |

`DeliverySendResult.From(response)` 沿用出站结果的 `Transient`（暂时性：建连超时、DNS/连接/TLS 失败、429；非暂时性：`target_unknown`、`target_blocked`、`credential_missing`、`credential_invalid`）。适配器自己构造 `NotSent` 结果时须显式设 `Transient: true` 才会自动重发，缺省视为配置类问题直接耗尽。

查询结果：远端已成功 → `Succeeded`；已受理 → `AwaitingConfirmation`；远端明确失败 → `Failed`；远端无记录 → 可安全发送（受理后的确认阶段查不到则与已知事实矛盾 → `NeedsReconciliation`）；查询本身失败 → 截止/确认时限内稍后再查，否则 `NeedsReconciliation`。`Cancelled`（调用方取消）按 `Unknown` 处理。迁移规则集中在纯函数 `DeliveryStateMachine`（`AfterSend`、`AfterQuery`、`AllowedActions`、`Backoff`），投递器、人工操作与回调共用。

### 8.4 中断恢复（租约过期的 `Dispatching`）

租约到期**不**视为可安全重发：恢复动作先以新 `Fence` 夺回记录并追加一条 `Kind=Recovery` 的尝试记录（注明原领取节点），然后按适配器能力决定——已受理过（有确认时限）且可查询：继续确认轮询；声明去重：以同一幂等标识重新发送（预算已尽则 `Exhausted`）；声明可查询：先查询；两者皆无：进入 `NeedsReconciliation`（`interrupted_unknown`）。

### 8.5 去重责任

框架保证：同一逻辑投递的幂等标识在所有自动与人工尝试中不变，并随每次调用交给适配器；不对结果未知的调用盲目重发。适配器负责：把幂等标识按对方协议传出，只有对方确实按该标识去重时才声明 `SupportsIdempotency`；能按标识或业务键查询对方结果时才声明 `SupportsQuery`；区分「已受理」与「已完成」。框架不承诺任意第三方恰好执行一次。

### 8.6 上限、异步受理确认与人工操作

- 默认上限：`MaxAttempts=8`（自最近一次预算重置起计）、`MaxAgeHours=24`（`DeadlineAtUtc`）；退避 `30s × 2^(n-1)`，封顶 1 小时。
- 异步受理的最终确认两种触发：**轮询**（适配器实现查询，`AwaitingConfirmation` 按 `ConfirmPollSeconds`=60 查询，直到 `ConfirmDeadlineAtUtc`，默认 72 小时，超时转 `NeedsReconciliation`；不可查询的受理记录把下次处理时刻设为确认时限，只等回调）；**回调**（消费者在自己的回调开放接口里调用 `IDeliveryConfirmationService.ConfirmAsync(DeliveryConfirmation)`，同样按栅栏 CAS、追加 `Kind=Confirm, Trigger=Callback` 记录；可从任何未完结状态确认，含处理中——进行中的调用回写时即成迟到结果；重复回调返回 `AlreadyApplied`，已取消或已确认为相反结果返回 `Rejected` 交人工）。回调确认必须限定在调用方身上：`Adapters` 必填，投递不属于这些适配器、`BusinessKey` 给出却不一致、或记录从未发出（`AttemptCount=0`）时一律返回 `NotFound`，不透露记录是否存在；回调端点声明业务范围，先按调用应用的范围核实投递对应的业务行，再把业务键传入（示例 `PartnerCallbackOpenController`）。
- 新一轮发送：`retry`、`confirm-not-executed` 与查询得知远端无记录时重置预算（`BudgetStartAttempt=AttemptCount`、截止时刻顺延默认时限），同时清空 `ConfirmDeadlineAtUtc` 与 `RemoteReference`；新的受理总是从受理时刻重新计算确认时限，人工查询得知「已受理」也重新计时，只有自动确认轮询沿用既有时限。
- 人工操作（`IDeliveryAdminService`，后台接口，服务端计算 `AllowedActions` 并在每次执行前复检，全部记录操作者与说明、保持幂等标识不变；可回传详情里的 `Fence`，不符即 `49046`）：
  - `retry`：`Failed`；`Exhausted`（对方去重，或本轮——自最近一次预算重置起——没有「对方不去重、结果未知」的发送，且不在待核实状态）。重置预算（`BudgetStartAttempt=AttemptCount`、截止时刻顺延默认时限）后回到 `Pending`。`NeedsReconciliation` 不允许直接重试。
  - `query`：适配器声明可查询时，对 `NeedsReconciliation`/`Exhausted`/`AwaitingConfirmation`/`Failed` 在事务外立即查询，再以查询前的栅栏回写（远端无记录 → 重置预算回到 `Pending`；查询失败只记尝试不改状态）。
  - `confirm-succeeded`：人工确认远端已成功（`NeedsReconciliation`/`AwaitingConfirmation`/`Exhausted`）→ `Succeeded`，须填写说明（`49047`）。
  - `confirm-not-executed`：人工确认远端未执行（`NeedsReconciliation`）→ 重置预算回到 `Pending`，之后按正常规则发送，须填写说明。
  - `cancel`：非 `Dispatching`、非终态 → `Cancelled`。
  - 不可用 → `49045`（args.action、args.status）；适配器已移除的记录只能确认或关闭。重新发送一律由投递器经同一出站基础设施完成，地址策略、凭据与分类不因人工操作放宽。
- 告警：进入 `NeedsReconciliation`、`Exhausted`、`Failed` 时由 `DeliveryAlertPublisher` 调用全部 `IDeliveryAlertSink`（出口异常只记错误日志，不影响状态与其他出口）；默认 `LoggingDeliveryAlertSink` 输出结构化 Warning 日志（默认可观察输出），消费者可追加邮件/IM 等实现。后台任务沿用 `sys_job` 的运行日志与告警。
- 管理接口 `api/v1/integration/delivery`：`page`、`summary`（按状态计数）、`{id}`（详情：载荷、栅栏、截止与确认时限、适配器能力、`AllowedActions`、最近 200 条尝试含操作人姓名与出站调用 Id）、`{id}/retry|query|confirm-succeeded|confirm-not-executed|cancel`（逐项授权、进入 `sys_op_log`）；菜单「发送任务」（`ConsumerMin + 49_060` 起）。
- 外部依赖故障只影响投递记录，不回写或回滚已提交的本地业务。

### 8.7 后台任务

- `IntegrationDeliveryJob`（`IAdminJob`，种子 `itg-delivery`，`ConsumerMin + 49_000`，`sys_job` 间隔 5 秒、串行跳过、错过即跳过）：调用一轮 `IDeliveryDispatcher.RunOnceAsync`，有候选时把统计写进运行日志；多副本下由 DB 选主调度，手动执行或切主重叠时由 `Fence` CAS 保证单赢家。
- 已完结投递的保留清理并入 `IntegrationRetentionJob`：只删 `Succeeded`/`Cancelled` 且 `CompletedAtUtc` 早于 `Retention:DeliveryDays`（默认 90 天）的记录，连同尝试记录（每批一个短事务）；其他状态永不自动删除。
- `IntegrationRetentionJob`（种子 `itg-retention`，`ConsumerMin + 49_001`，每天 03:30）：见 §9；分批按 Id 删除（`Retention:BatchSize`，默认 1000），不持有长事务。
- 种子号段 `TenonSeedIds.ConsumerMin + 49_000` 起（菜单、任务各自表内独立编号）。`SchedulerEnabled=false` 的副本不执行投递。

落点：G09。

## 9. 限流与保留默认值

| 项目 | 默认 | 说明 |
|---|---|---|
| 按应用限流 | 600 次/分钟 | 应用可单独设置，0 表示不限；装 Redis 为全集群计数，否则每副本独立计数 |
| 认证失败（格式正确的凭据按 IP 与 `KeyId`，格式错误的按 IP） | 30 次/分钟 | 超过后查库前拒绝；内核 IP 全局限流仍然生效 |
| 开放接口页大小上限 | 100 | 常量 `OpenApiPageInput.MaxSize`，低于内核 200 |
| 凭据默认有效期 | 365 天 | 0 表示不过期；创建时可指定 |
| 每应用有效凭据上限 | 5 | 轮换时新旧并存计入 |
| 轮换并存窗口 | 24 小时 | 可指定 0 到 `MaxRotationOverlapHours`（默认 720）小时，留空按默认；旧凭据到期后失效 |
| 入站调用记录保留 | 30 天 | 按 `CreateTime` 清理 |
| 出站调用记录保留 | 30 天 | 同上 |
| 投递记录保留 | 90 天 | 只清理 `Succeeded`/`Cancelled`（按 `CompletedAtUtc`），连同尝试记录；其他状态永不自动删除 |

## 10. 内核改动（最小、通用）

- `OperationLogFilter` 识别新增的公开标记接口 `ISkipOperationLogMetadata`：端点已有等价或更强的专用审计时可豁免用户操作日志，且优先于 `[OperationLog]`。`[OpenApi]` 实现该接口。内核测试 `OperationLogCoverageTests.Endpoints_with_their_own_audit_stay_out_even_when_marked` 锁定。
- 其余能力均通过公开扩展点完成：`IDataScopeContext`、`TokenClaimNames.ORG_ID`、`PermissionCode.Build`、ApiExplorer `GroupName`、`ICacheProvider.IncrementAsync`、`JobHttpFence` 公共方法、`IAdminJob`/`ISeedData`。

## 11. 可替换性

所有服务以接口 + `TryAdd*` 注册、方法 `virtual`：`IOpenAppCredentialValidator`、`IOpenAppKeyGenerator`、`IIntegrationAppService`、`IOpenAppCredentialService`、`IOpenAppAuthorizationService`、`IOpenApiCatalog`、`IOpenApiDataScopePolicy`（集合）、`IOpenAppRateLimiter`、`IInboundLogService`、`IOutboundHttpInvoker`、`IOutboundTargetRegistry`、`OutboundHttpClientFactory`（可继承覆写解析与处理器）、`IOutboundCredentialProvider`、`IOutboundAuthenticator`、`IOutboundResultClassifier`、`IOutboundLogService`、`IDeliveryOutbox`、`IDeliveryDispatcher`、`IDeliveryAdminService`、`IDeliveryConfirmationService`、`IDeliveryAlertSink`（集合）、`IIntegrationRetentionService`；适配器 `IDeliveryAdapter`（集合，按 `Name` 分发）。在 `AddTenonAdminIntegration` 之前注册同接口即胜出，由模块自己的替换性测试锁定。

## 12. 管理后端与界面

- 管理接口前缀 `/api/v1/integration/`，`[Module("Integration")]`：`app`（应用 CRUD、启停、凭据创建/到期/撤销/轮换及已撤销凭据删除、授权、范围绑定）、`catalog`（开放端点清单、范围策略与候选值、开放文档下载）、`inbound-log`、`outbound-log`、`delivery`（列表、详情含尝试与 `AllowedActions`、人工操作、状态汇总）。
- 菜单：「系统」应用下的目录「第三方接入」，页面「接入应用」「开放接口调用记录」「第三方接口调用记录」「发送任务」，按钮即各接口权限码。前端组件路径 `integration/{app,inbound-log,outbound-log,delivery}/index`；Vue 与 React 各自实现（`web/src/api/integration.ts`、`web-react/src/api/integration.ts`），互不导入。
- 错误码 49xxx 由 `IntegrationErrorCode` 自管常量，前端按 `error.code.<数字>` 翻译，双前端各自维护中英文。

## 13. 验证与示例落点

- 测试宿主 `backend/tests/TenonAdmin.IntegrationTestHost`：启用模块并提供最小消费者开放端点、自定义范围策略与测试适配器；`IntegrationAppFactory`（测试工程 alias `integrationhost`）与 `TestDb` 模板类型 `integration`。
- 本地可控第三方 `backend/samples/IntegrationMockPartner`：认证、按幂等键去重、查询、模拟响应丢失、异步受理，测试以 Kestrel 随机端口宿主它。
- 消费者示例 `backend/samples/IntegrationSample`（含 README）：示例工单与对接方的开放接口、普通调用、事务内投递与安全恢复；双前端的 `test:e2e:integration` 以它和本地可控第三方为后端。
- `MinimalHost` 启用模块，使双前端开发与 `gen:api` 覆盖管理接口。
- 开发模板：`dotnet new tenon-app --integration`（`template.json` 布尔参数 `integration`，默认关）多引用 `TenonAdmin.Integration`、接好两步、生成 `Integrations/`（机构范围开放接口、普通调用客户端、事务内入队服务与投递适配器、说明哪些代码按对方协议填写的 README）；默认生成物不含该包与目录。`templates/smoke-test.ps1` 对两种生成物都做「精确版本还原 → 构建 → `dotnet run` 到 `/health`」，`--integration` 还探测开放端点无凭据返回 401；`-Port` 仅供本地端口被占时使用。
- 施工清单 `skills/wire-integration.md`（`.claude/skills/`、`.agents/skills/` 薄壳入口）；站点页 `site/zh/guide/integration.md` 与英文 `site/guide/integration.md`。

## 14. 目标映射

| 目标 | 落点 |
|---|---|
| G02 | `Entities/IntegrationApp*`、`Apps/*Service`、`OpenApi/OpenAppCredentialValidator`、`OpenAppIdentity`、`Controllers/IntegrationAppController`（应用与凭据）、`IntegrationSetup`、测试宿主与工厂 |
| G03 | `OpenApi/OpenApiAttribute`、认证处理器、授权过滤器、`IOpenAppContext`、范围策略、`IntegrationAppGrant/Scope`、授权/范围管理接口、启动校验、内核 `ISkipOperationLogMetadata`、消费者测试端点 |
| G04 | 文档分组与下载、`OpenApiPageInput`、追踪头、按应用限流、认证失败限流、`itg_inbound_log` 与查询、保留清理、双前端 `schema.d.ts` |
| G05 / G06 | `web/src/views/integration/app`、`inbound-log` / React 对应页面、菜单种子 |
| G07 | `Outbound/*`、`itg_outbound_log` 与查询 |
| G08 | `Delivery/DeliveryOutbox`、`itg_delivery` |
| G09 | `Delivery/DeliveryDispatcher`、恢复、确认、管理服务与接口、告警、`Jobs/*` |
| G10 / G11 | `outbound-log`、`delivery` 双前端页面 |
| G12 | Mock 第三方、消费者示例、`tenon-app` 模板集成选项、`skills/` 配方、站点文档、打包验证 |
| G13 | 验收矩阵、方言矩阵、联合回归、独立评审 |
