# 给业务接第三方系统 (Wire Integration)

三件事共用一个可选包 `TenonAdmin.Integration`：第三方调你（开放接口）、你同步调第三方（普通调用）、你调第三方且要与本地业务同成败（可靠投递）。认证、授权、数据范围、调用记录、地址安全、凭据注入、投递领取与恢复都在包里；这份清单只覆盖业务要写的部分。原理与完整说明见文档站「第三方接入」（`site/zh/guide/integration.md`）。

**先决条件**：业务实体与服务已能 CRUD。没有实体先走 `create-entity.md`；没有服务先走 `create-crud-backend.md`。

**活样板（照抄，别自己编）**：

| 样板 | 路径 | 演示什么 |
|---|---|---|
| `tenon-app --integration` | `templates/content/tenon-app/Integrations/` | 最小三件套：机构范围开放接口、普通调用客户端、事务内入队 + 适配器 |
| 合作方工单示例 | `backend/samples/IntegrationSample/` | 自定义范围策略、写入范围校验、回调确认、去重/不去重两种适配器 |
| 本地可控第三方 | `backend/samples/IntegrationMockPartner/` | 按幂等键去重、按键查询、受理、拒绝、限流、执行后断连 |
| 测试宿主 Demo | `backend/tests/TenonAdmin.IntegrationTestHost/Demo/` | 覆写 `AuthenticateAsync` 做 HMAC 签名、识别「200 但业务拒绝」 |

---

## 第 0 步：装包与两步接线（缺一不可）

```bash
dotnet add package TenonAdmin.Integration
```

```csharp
using TenonAdmin.Integration;

builder.Services.AddTenonAdminIntegration(builder.Configuration);   // 要替换内置服务,在这一行之前注册同接口
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
    o.UseIntegration();                                              // 缺它:启动即报错(不建 itg_* 表、不挂模块接口)
});
```

接好后自动有：`itg_*` 表、「系统集成」四个菜单页（双前端都已内置页面，不用写前端）、任务 `itg-delivery`（每 5 秒）与 `itg-retention`（每天 03:30）。配置节 `TenonAdmin:Integration`，非法值启动即报错。

---

## 第 1 步：开放接口（第三方调你）

照抄 `Integrations/SampleDocOpenController.cs`。硬规则由启动校验把关，违反即拒绝启动：

- 控制器挂 `[OpenApi]` 与 `[OpenApiDataScope(...)]`；路由以 `api/open/v{n}/` 开头；每个动作显式 `[HttpGet]`/`[HttpPost]`…。
- 返回 `Result<T>`，`T` 是专门的 DTO，不能（直接或经属性）含实体。
- 不挂 `[RolePermission]`/`[ActiveSession]`/`[RequireReauth]`/`[AllowAnonymous]`。
- 分页入参 `OpenApiPageInput` + `ToOpenPagedListAsync(input, map)`（单页 ≤100）。

数据范围三选一：

| 声明 | 什么时候用 | 你写什么 |
|---|---|---|
| `OpenApiDataScopes.None` | 不读写受范围约束的数据 | 无 |
| `OpenApiDataScopes.Org` | 实体继承 `DataEntity`，按机构隔离 | 无，内核过滤器与写守卫生效 |
| 自定义键 | 按业务分区隔离（合作方、门店…） | 继承 `OpenApiDataScopePolicyBase`；查询 `WhereInScope`，写入前 `EnsureAllowed`；`TryAddEnumerable` 注册 |

自定义策略照抄 `IntegrationSample/OpenApi/PartnerScopePolicy.cs`。分区本身就是完整隔离维度时才覆写 `ResolveKernelScope` 返回 `Unrestricted`，默认是拒绝。

后台操作（不写代码）：接入应用 → 新增 → 发放凭据（原文只显示一次）→ 授权与范围（勾端点、绑范围值）。授权、绑范围、发放与轮换凭据、调整到期、启用和改归属机构只有超级管理员能做（`41003`），和内核角色授权同一条规矩。

---

## 第 2 步：普通调用（同步、事务外）

1. 配目标 `TenonAdmin:Integration:Outbound:Targets:{name}`：`BaseUrl`、`TimeoutSeconds`、`Auth.Type`（`None`/`Bearer`/`Header`+`HeaderName`/`Basic`+`Username`）。内网地址（含 127.0.0.1）要列进 `TrustedCidrs`。
2. 秘密走环境变量 `TenonAdmin__Integration__Outbound__Targets__{name}__Secret` 或 user-secrets，**不进 appsettings、不进代码**。
3. 客户端照抄 `Integrations/PartnerClient.cs`：继承 `OutboundAdapterBase`，`TargetName` 对上配置名；用 `GetAsync`/`PostJsonAsync`/`PutJsonAsync`/`DeleteAsync`（路径相对 `BaseUrl`）；需要把失败报给界面时 `response.EnsureSucceeded()`（抛 4903x）。`TryAddScoped<YourClient>()` 注册。
4. 对方要签名、换令牌：覆写 `AuthenticateAsync`，秘密用 `GetCredentialAsync` 取。对方用 200 响应体表达受理/失败：覆写 `Classify`。

框架不自动重试。调用方拿到 `Unknown` 时不能当失败重调，对方可能已执行。

---

## 第 3 步：可靠投递（要与本地业务同成败）

1. 适配器照抄 `Integrations/SampleDocSyncAdapter.cs`：继承 `DeliveryAdapterBase`，`Name` 全局唯一（1–64 位字母数字 `._-`），`SendAsync` 用 `context.SendOptions`（已带幂等标识）。
2. **能力声明必须属实**：`SupportsIdempotency` 只在对方确实按幂等标识去重时为 true；`SupportsQuery` 为 true 时实现 `QueryAsync`（翻成 `Succeeded`/`Accepted`/`Failed`/`NotFound`/`Unknown`）。拿不准就都 false，结果未知进「待核对」交人工。
3. 注册：`TryAddEnumerable(ServiceDescriptor.Scoped<IDeliveryAdapter, YourAdapter>())`。
4. 入队照抄 `Integrations/SampleDocSyncService.cs`：在业务写入的**同一个** `UseTranAsync` 里 `outbox.EnqueueAsync(new DeliveryRequest { Adapter, Operation, DeliveryKey, BusinessKey, Payload })`。
5. 对方回调告知最终结果：写一个开放端点（照抄 `IntegrationSample/OpenApi/PartnerCallbackOpenController.cs`），调 `IDeliveryConfirmationService.ConfirmAsync(new DeliveryConfirmation { ... })`。三件事一件不能少：端点声明业务范围，先按调用应用的范围查到投递对应的业务行，查不到就当不存在；`Adapters` 只列本端点负责的适配器；把核实过的业务键填进 `BusinessKey`。否则一个对方能确认另一个对方的投递。
6. 告警要进邮件/IM：实现 `IDeliveryAlertSink`，`TryAddEnumerable` 追加（默认只写 Warning 日志）。

---

## 第 4 步：验证

- 编译并启动：启动校验会拦下开放端点声明错误、适配器重名、配置非法。
- 按 `Integrations/README.md` 用本地可控第三方跑三个流程：开放接口授权后调用成功、未授权 403；普通调用查到对方数据；入队后「可靠投递」变成功，排一次 `drop` 后看同一标识只执行一次。
- 内核仓库改了本模块时：`dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Integration"`；两套前端各自 `npm run test:e2e:integration`。

---

## 常见坑

1. **对方不去重却声明 `SupportsIdempotency => true`**：响应丢失时框架用同一标识重发，对方重复执行业务。
2. **在事务里做普通调用**：持锁等网络；事务回滚了对方却已执行。要原子就入队。
3. **入队不在事务里**：`49041`。确实没有业务写入才设 `Standalone = true`。
4. **`DeliveryKey` 用随机值**：每次入队都是新记录，重复提交会重复投递。用稳定业务键（如 `order-sync:{orderId}`）。
5. **秘密写进 appsettings 或载荷**：载荷会入库并显示在投递详情里。
6. **内网目标没列 `TrustedCidrs`**：调用结果 `NotSent`/`target_blocked`。链路本地与云元数据地址列了也不放行。
7. **给已有记录的适配器改名**：存量记录找不到适配器，只能人工处理。先处理完再改。
8. **开放端点直接返回实体**：启动失败。写 DTO。
9. **`Delivery:LeaseSeconds` 不大于最长出站超时 + 30 秒**：启动失败。调大租约或调小超时。
10. **回调端点声明 `none` 范围、只凭投递标识确认**：标识可猜，任何被授权的对方都能把别人的投递确认成成功或失败。按第 3 步第 5 条核实业务行并限定适配器。
11. **只调 `AddTenonAdminIntegration` 忘了 `UseIntegration`**：启动失败。两步都要。
