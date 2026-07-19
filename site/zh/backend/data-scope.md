# 多组织数据权限

数据范围替业务代码扛下了全部机构过滤，内核的招牌就在这里。它回答一个问题：同样的接口、同样的 SQL，不同用户看到的行为什么不同？答案在全局查询过滤器。内核会按当前请求解析出的**生效机构集**，自动裁剪查询结果。

## 五种数据范围

这五种类型说的是角色能看到哪些机构的数据，存库时是个 `int`。定义在 `Core/Security/DataScopeType.cs`。

| 值 | 类型 | 含义 |
| --- | --- | --- |
| 1 | `All` | 全部数据，不受机构约束 |
| 2 | `Org` | 本机构，仅用户主属机构 |
| 3 | `OrgAndChildren` | 本机构及以下，主属机构 + 其所有子孙机构 |
| 4 | `Self` | 仅本人，只看自己创建的数据 |
| 5 | `Custom` | 自定义机构，显式指定的机构集合 |

范围是挂在**角色**上的，表是 `sys_role_data_scope`。一个用户可以有多个角色。多个角色的范围怎么合并成一个？规则是**取最宽**：

- 任一角色是 `All`，整体就不受限，看全部。
- 否则把各角色的机构集合并起来。本机构取主属机构，本机构及以下取主属机构加子孙，自定义取指定集合。
- 任一角色是 `Self`，再附加一个「仅本人」维度，和机构集合取并集，自己创建的数据也能看。

比如某用户身兼两个角色：一个是 `Org`（主属机构是「华东分部」），另一个是 `Custom`（机构集是「华南分部」和「华北分部」）。两个角色都不是 `All`，合并结果就是这三家机构的并集，查询只按这三家机构过滤。这个用户没有 `Self` 角色，看不到「仅自己创建」这层补充数据。

## 解析：多角色合并成一个结果

`IDataScopeProvider.ResolveAsync(userId)` 把一个用户名下多个角色的范围合并成一个 `DataScopeResult`，这个结果是不可变的。默认实现是 `DataScopeProvider`，它按用户缓存这个结果，授权或机构一变就失效。只有缓存没命中，才会真的聚合查库。

```csharp
protected virtual async Task<DataScopeResult> ComputeAsync(long userId)
{
    // 取用户启用角色
    var roleIds = ...;
    if (roleIds.Count == 0) return DataScopeResult.Restricted([], includeSelf: true, userId); // 无角色 → 仅本人

    var scopes = ...;   // 各角色的数据范围配置
    if (scopes.Any(s => s.ScopeType == DataScopeType.All)) return DataScopeResult.Unrestricted; // 最宽
    if (scopes.Count == 0) return DataScopeResult.Restricted([], includeSelf: true, userId);    // 有角色未配 → 仅本人

    // 逐角色累加机构集:Org 加主属机构;OrgAndChildren 展开子孙;Self 置 includeSelf;Custom 并入指定集合
    return DataScopeResult.Restricted(orgSet, includeSelf, userId);
}
```

::: tip 安全默认
没有角色，或者有角色但一个范围都没配，都收敛成「仅本人」，为的是不放大可见面。要是两样都空，机构集是空的、也不含本人，那就是「看不到任何数据」。这是默认拒绝，不是默认放行。
:::

`DataScopeResult`（`Core/Security/DataScopeResult.cs`）是不可变值对象，可安全放进缓存跨请求复用：

```csharp
public sealed record DataScopeResult
{
    public bool IsUnrestricted { get; init; }                       // 不受限:看全部
    public IReadOnlyCollection<long> OrgIds { get; init; } = [];    // 允许可见的机构 Id 集(按 CreateOrgId 匹配)
    public bool IncludeSelf { get; init; }                          // 是否附加「仅本人」
    public long UserId { get; init; }                              // IncludeSelf 为真时比对 CreateUserId

    public static readonly DataScopeResult Unrestricted = new() { IsUnrestricted = true };
    public static DataScopeResult Restricted(IReadOnlyCollection<long> orgIds, bool includeSelf, long userId) => ...;
}
```

它为什么用 `record` 加 `init` 属性，还让参数名和属性名对齐？为了能被 `System.Text.Json` 正常往返。换成 Redis 缓存做多实例部署的时候，这个结果得先序列化进缓存，再反序列化回来。

## 锚点字段：`CreateOrgId`

数据范围过滤是挂在实体的 `CreateOrgId` 字段上的，这个字段记的是**创建者当时所属的机构**。业务表只要继承 `DataEntity` 就有这个字段。类在 `SqlSugar/Entities/DataEntity.cs`。

```csharp
public abstract class DataEntity : BaseEntity, IOrgScoped
{
    [SugarColumn(IsNullable = true, ColumnDescription = "归属机构 Id(数据范围锚点)")]
    public long? CreateOrgId { get; set; }
}
```

有些表不需要机构隔离，比如全局字典、机构树自身。它们继续用 `BaseEntity`，不带这个字段，也就不被数据范围过滤。

`CreateOrgId` **不用业务代码赋值**。插入时，审计 AOP 会从当前用户的 `org` claim 自动把它填上。这段在 `SqlSugarSetup.cs` 里。

```csharp
// CreateOrgId 未指定 → 填当前用户归属机构(数据范围锚点)
else if (info is { PropertyName: nameof(DataEntity.CreateOrgId),
                   EntityValue: DataEntity { CreateOrgId: null } }
         && currentUser.OrgId is { } insOrgId)
    info.SetValue(insOrgId);
```

::: warning 锚点不填 = 查不到自己刚插的行
`CreateOrgId` 要是没被填上，比如绕过了内核的令牌流程、或者用户没有 org claim，这行数据的机构维度可见性就是空的。机构范围的用户于是查不到它。所以审计 AOP 的自动填充，是数据范围能工作的前提。
:::

## 全局过滤器：业务代码零过滤条件

过滤器对所有实现 `IOrgScoped` 接口的实体一次性挂上，也就是 `DataEntity` 及其子类，对所有查询都生效。挂载的位置在 `SqlSugarSetup.cs`。

```csharp
client.QueryFilter.AddTableFilter<IOrgScoped>(e =>
    scope.Current.IsUnrestricted == true
    || (e.CreateOrgId != null && scope.Current.OrgIds.Contains(e.CreateOrgId.Value))
    || (scope.Current.IncludeSelf == true && e.CreateUserId == scope.Current.UserId));
```

三个分支对应三种可见性。不受限时整体恒真，等于不过滤。否则就看 `CreateOrgId` 在不在机构集里。要是启用了「仅本人」，再放行 `CreateUserId == 当前用户` 的行。

::: details 两个实现细节
**按接口匹配，不按基类**：SqlSugar 的 `AddTableFilter<T>` 只认接口或精确类型，不认基类。所以锚点字段是通过 `IOrgScoped` 接口暴露的。软删过滤器 `AddTableFilter<ISoftDelete>` 也是一个道理。

**布尔标记写成 `== true`**：`scope.Current` 的三个属性都和实体参数无关。SqlSugar 会先把它们在本地求值成常量，机构集渲染成 SQL 的 `IN`，再拼进 `WHERE`。两个布尔为什么写成 `== true`，不写裸布尔？因为 SqlServer 的谓词上下文不接受裸标量，必须是个比较式。
:::

有了它，业务代码写最朴素的查询就已经受机构隔离约束：

```csharp
// 业务服务里:不写任何机构过滤条件
var orders = await orderRepo.AsQueryable()
    .Where(o => o.Status == OrderStatus.Pending)
    .ToListAsync();
// 实际执行的 SQL 已自动追加:AND (CreateOrgId IN (...) OR CreateUserId = ...)
```

还是这段代码。`All` 范围的用户看到全部待处理订单，`Org` 范围的用户只看到本机构的，`Self` 用户只看到自己建的。差异全都来自请求早期解析出的那个 `DataScopeResult`，业务逻辑一个字都不用改。

::: warning 写路径守卫
全局过滤器只管**查询（SELECT）**，不管按主键的 `Updateable` / `Deleteable`。所以仓储 `SqlSugarRepository` 给 `IOrgScoped` 实体的 `UpdateAsync` / `DeleteAsync` 内置了一道写路径守卫。写之前，它先用带范围过滤器的查询确认目标行在当前范围内，越权改删别的机构的行会被拒，返回 0 行。要是绕过仓储、直接走 `Db.Updateable/Deleteable` 这个逃生舱口，这道守卫就管不着了，得自己校验归属。
:::

## 上下文载体：为什么不是 `AsyncLocal`

生效范围通过 `IDataScopeContext` 在请求内传递。`Current` 永远不是 null，**没有显式设置就是不受限**。这个口子是留给系统和可信上下文的，比如启动、种子、自检。认证请求不一样，它必须在查询之前显式解析、写入，这一步由授权管道保证。

内核为它提供了两个实现，取决于是否有 HTTP 上下文：

- **`HttpContextDataScopeContext`**（AspNetCore 层，HTTP 路径）：用 `HttpContext.Items` 存当前请求的范围。
- **`DataScopeContext`**（SqlSugar 层，非 HTTP 回退）：基于 `AsyncLocal`，供后台/自检等无 HTTP 的场景使用。

HTTP 路径**刻意不用 `AsyncLocal`**，原因写在 `HttpContextDataScopeContext.cs`：

> 授权过滤器是 MVC 管道的被调用方，其内部 `await` 之后设置的 `AsyncLocal` 不会回流到管道上游（经典陷阱），动作里的查询将读不到。`HttpContext.Items` 挂在请求对象上、全管道稳定可见，无此问题。

也就是说，范围是在**授权过滤器**里写入的，查询却发生在**更上层的动作**里。`AsyncLocal` 的值只会顺着执行流往下走。写在下游的过滤器里，上游的动作就读不到。`HttpContext.Items` 不一样，它挂在请求对象上，整条管道都能稳定读到同一份。所以这里必须选 `Items`，不能用 `AsyncLocal`。

## 扩展点

`IDataScopeProvider` 是替换点。默认实现聚合的是 `sys_role_data_scope`。典型的替换是换一个隔离维度，比如按租户隔离。`ResolveAsync` 和内部的 `ComputeAsync` 都是 `virtual`，只覆写其中一步也行。按内核的可替换性约定，消费方在 `AddTenonAdmin()` 之前注册自己的 `IDataScopeProvider` 就能接管，不用 fork。
