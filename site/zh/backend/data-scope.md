# 多组织数据权限

数据范围是内核的招牌特性。它回答一个问题:同样的接口、同样的 SQL,不同用户看到的行为什么不同?答案是——业务代码不写任何机构过滤条件,内核在全局查询过滤器里按当前请求的**生效机构集**自动裁剪结果。本页拆解五种数据范围、过滤器如何工作、锚点字段是什么,以及上下文载体为何不用 `AsyncLocal`。

## 五种数据范围

数据范围类型定义在 `Core/Security/DataScopeType.cs`,是角色可见数据的机构维度,存库为 `int`:

| 值 | 类型 | 含义 |
| --- | --- | --- |
| 1 | `All` | 全部数据,不受机构约束 |
| 2 | `Org` | 本机构,仅用户主属机构 |
| 3 | `OrgAndChildren` | 本机构及以下,主属机构 + 其所有子孙机构 |
| 4 | `Self` | 仅本人,只看自己创建的数据 |
| 5 | `Custom` | 自定义机构,显式指定的机构集合 |

范围挂在**角色**上(`sys_role_data_scope`),一个用户可有多个角色。合并规则是**取最宽**:

- 任一角色为 `All` → 整体不受限,看全部;
- 否则并集各角色的机构集合(本机构 = 主属机构;本机构及以下 = 主属机构 + 子孙;自定义 = 指定集合);
- 任一角色为 `Self` → 附加「仅本人」维度,与机构集合取并集(也能看自己创建的)。

## 解析:多角色合并成一个结果

`IDataScopeProvider.ResolveAsync(userId)` 把某用户的多角色范围合并成一个不可变的 `DataScopeResult`。默认实现 `DataScopeProvider` 结果按用户缓存(授权/机构变更时失效),缓存未命中才聚合查库:

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
无角色、或有角色但没配任何范围,都收敛为「仅本人」——不放大可见面。两者皆空(机构集空 + 不含本人)即「看不到任何数据」,是默认拒绝而非默认放行。
:::

`DataScopeResult`(`Core/Security/DataScopeResult.cs`)是不可变值对象,可安全放进缓存跨请求复用:

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

它用 `record` + `init` 属性且参数名与属性名对齐,是为了能被 `System.Text.Json` 正常往返——换 Redis 缓存做多实例部署时,这个结果要能序列化进缓存再反序列化回来。

## 锚点字段:`CreateOrgId`

数据范围过滤的锚点是实体上的 `CreateOrgId`——**创建者当时所属的机构**。业务表继承 `DataEntity`(`SqlSugar/Entities/DataEntity.cs`)即获得这个字段:

```csharp
public abstract class DataEntity : BaseEntity, IOrgScoped
{
    [SugarColumn(IsNullable = true, ColumnDescription = "归属机构 Id(数据范围锚点)")]
    public long? CreateOrgId { get; set; }
}
```

不需要机构隔离的表(全局字典、机构树自身)继续用 `BaseEntity`,不带这个字段、不被数据范围过滤。

`CreateOrgId` **不由业务代码赋值**,由审计 AOP 在插入时从当前用户的 `org` claim 自动填充(`SqlSugarSetup.cs`):

```csharp
// CreateOrgId 未指定 → 填当前用户归属机构(数据范围锚点)
else if (info is { PropertyName: nameof(DataEntity.CreateOrgId),
                   EntityValue: DataEntity { CreateOrgId: null } }
         && currentUser.OrgId is { } insOrgId)
    info.SetValue(insOrgId);
```

::: warning 锚点不填 = 查不到自己刚插的行
若 `CreateOrgId` 没被填(例如绕过内核令牌流程、用户无 org claim),这行数据的机构维度可见性就空了——机构范围的用户查不到它。这也是为什么审计 AOP 的自动填充是数据范围能工作的前提。
:::

## 全局过滤器:业务代码零过滤条件

过滤器在 `SqlSugarSetup.cs` 里对实现 `IOrgScoped` 接口的实体(`DataEntity` 及其子类)一次性挂上,对所有查询生效:

```csharp
client.QueryFilter.AddTableFilter<IOrgScoped>(e =>
    scope.Current.IsUnrestricted == true
    || (e.CreateOrgId != null && scope.Current.OrgIds.Contains(e.CreateOrgId.Value))
    || (scope.Current.IncludeSelf == true && e.CreateUserId == scope.Current.UserId));
```

三个分支对应三种可见性:不受限则整体恒真(不过滤);否则 `CreateOrgId ∈ 机构集`,或(启用「仅本人」时)`CreateUserId == 当前用户`。

::: details 两个实现细节
**按接口匹配,不按基类**:SqlSugar 的 `AddTableFilter<T>` 匹配接口或精确类型,不匹配基类,所以锚点字段经 `IOrgScoped` 接口暴露。软删过滤器 `AddTableFilter<ISoftDelete>` 同理。

**布尔标记写成 `== true`**:表达式里 `scope.Current` 的三个属性都与实体参数无关,SqlSugar 先本地求值成常量(机构集 → SQL 的 `IN`),再拼进 `WHERE`。两个布尔写成 `== true` 而非裸布尔,是因为 SqlServer 的谓词上下文不接受裸标量,必须是比较式。
:::

有了它,业务代码写最朴素的查询就已经受机构隔离约束:

```csharp
// 业务服务里:不写任何机构过滤条件
var orders = await orderRepo.AsQueryable()
    .Where(o => o.Status == OrderStatus.Pending)
    .ToListAsync();
// 实际执行的 SQL 已自动追加:AND (CreateOrgId IN (...) OR CreateUserId = ...)
```

同一段代码,`All` 范围的用户看到全部待处理订单,`Org` 范围的用户只看到本机构的,`Self` 用户只看到自己建的——差异全部来自请求早期解析出的 `DataScopeResult`,业务逻辑一个字都不改。

::: warning 写路径守卫
全局过滤器只作用于**查询(SELECT)**,不作用于按主键的 `Updateable` / `Deleteable`。为此仓储 `SqlSugarRepository` 对 `IOrgScoped` 实体的 `UpdateAsync` / `DeleteAsync` 内置了写路径守卫:写前经带范围过滤器的查询确认目标行在当前范围内,越权改删他机构行会被拒(返回 0 行)。绕过仓储直接走 `Db.Updateable/Deleteable` 逃生舱口的写不受此守卫,需自行校验归属。
:::

## 上下文载体:为什么不是 `AsyncLocal`

生效范围通过 `IDataScopeContext` 在请求内传递。`Current` 恒非 null——**未显式设置即不受限**(系统/可信上下文,如启动、种子、自检),因此认证请求必须在查询前显式解析写入,这一步由授权管道保证。

内核为它提供了两个实现,取决于是否有 HTTP 上下文:

- **`HttpContextDataScopeContext`**(AspNetCore 层,HTTP 路径):用 `HttpContext.Items` 存当前请求的范围。
- **`DataScopeContext`**(SqlSugar 层,非 HTTP 回退):基于 `AsyncLocal`,供后台/自检等无 HTTP 的场景使用。

HTTP 路径**刻意不用 `AsyncLocal`**,原因写在 `HttpContextDataScopeContext.cs`:

> 授权过滤器是 MVC 管道的被调用方,其内部 `await` 之后设置的 `AsyncLocal` 不会回流到管道上游(经典陷阱),动作里的查询将读不到。`HttpContext.Items` 挂在请求对象上、全管道稳定可见,无此问题。

也就是说,范围是在**授权过滤器**里写入的,而查询发生在**更上层的动作**里。`AsyncLocal` 的值只会随执行流向下流,写在下游的过滤器里、上游的动作读不到;而 `HttpContext.Items` 挂在请求对象上,整条管道都能稳定读到同一份。这正是必须选 `Items` 而非 `AsyncLocal` 的原因。

## 扩展点

`IDataScopeProvider` 是替换点。默认实现聚合 `sys_role_data_scope`;典型替换是换一个隔离维度(如按租户)。`ResolveAsync` 与内部的 `ComputeAsync` 都是 `virtual`,可只覆写其中一步。按内核的可替换性约定,消费方在 `AddTenonAdmin()` 之前注册自己的 `IDataScopeProvider` 即接管,不必 fork。
