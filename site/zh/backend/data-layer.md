# 数据层与审计

数据层的约定全部在 `SqlSugarSetup` 里全局收口：一个 `SqlSugarScope` 单例、两条全局查询过滤器、一套审计字段自动填充。业务代码只写业务字段，软删除、机构隔离、审计四件套都由框架兜底。

## 一个 `SqlSugarScope` 单例

`ISqlSugarClient` 以 `SqlSugarScope` 形态注册为单例——这是 SqlSugar 官方推荐的线程安全形态（内部按线程建 Client）。构造时一次性挂上全局过滤器、审计 AOP、SQL 诊断日志三组钩子。

```csharp
// backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs
services.TryAddSingleton<ISqlSugarClient>(sp =>
{
    var config = new ConnectionConfig
    {
        ConfigId = "TenonAdmin",
        DbType = Enum.Parse<DbType>(db.DbType, ignoreCase: true),
        ConnectionString = db.ConnectionString,
        IsAutoCloseConnection = true,
        // SqlServer CodeFirst 默认把 string 建成 varchar,存中文丢成 "??";打开后统一建 nvarchar
        MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
    };
    return new SqlSugarScope(config, client => { /* 挂过滤器 + AOP + 日志 */ });
});
```

## 全局查询过滤器

两条过滤器按**接口**匹配实体注册。SqlSugar 的 `AddTableFilter<T>` 只认接口或精确类型，不匹配基类，所以标记走接口 `ISoftDelete` / `IOrgScoped`，而不是基类 `BaseEntity` / `DataEntity`。

### 软删除

实现 `ISoftDelete` 的实体，查询自动排除已删行：

```csharp
client.QueryFilter.AddTableFilter<ISoftDelete>(e => e.IsDelete == false);
```

已删数据对所有查询天然不可见。确需查已删数据时用 `.ClearFilter<ISoftDelete>()` 显式解除。

### 数据范围

这是内核的招牌能力。对 `IOrgScoped` 实体（即 `DataEntity` 及其子类），按当前请求解析出的有效机构集过滤：

```csharp
client.QueryFilter.AddTableFilter<IOrgScoped>(e =>
    scope.Current.IsUnrestricted == true
    || (e.CreateOrgId != null && scope.Current.OrgIds.Contains(e.CreateOrgId.Value))
    || (scope.Current.IncludeSelf == true && e.CreateUserId == scope.Current.UserId));
```

`scope.Current` 的三个属性都与实体参数无关，SqlSugar 先本地求值成常量（机构集合渲染成 SQL `IN`），再拼进 WHERE。`IsUnrestricted` 时整体恒真、不过滤。

::: warning 数据范围只作用于查询
全局数据范围过滤器只作用于 SELECT，不作用于按主键的 `Updateable` / `Deleteable`。为此 `SqlSugarRepository<TEntity>` 对 `IOrgScoped` 实体的 `UpdateAsync` / `DeleteAsync` 内置了写路径范围守卫：写前经带范围过滤器的查询确认目标行在当前数据范围内，越权改删他机构行返回 0 行，默认安全。绕过仓储直接走 `Db.Updateable` / `Deleteable` 逃生舱口的写不受此守卫，需自行校验归属。
:::

## AOP 自动填审计字段

`SqlSugarScope` 的 `Aop.DataExecuting` 钩子在插入/更新时兜底填充基建字段。业务代码不碰这些字段。

| 字段 | 时机 | 填充规则 |
| --- | --- | --- |
| `Id` | 插入 | `Id == 0` 时填雪花号;显式给定（如种子数据）原样保留 |
| `CreateTime` | 插入 | 未设置时填当前时间 |
| `CreateUserId` | 插入 | 从当前登录用户填;系统上下文为 null 则留空 |
| `CreateOrgId` | 插入 | 从当前用户归属机构填（仅 `DataEntity` 有此列） |
| `UpdateTime` | 更新 | 每次整行更新都刷新 |
| `UpdateUserId` | 更新 | 有登录上下文时记录操作人 |

```csharp
client.Aop.DataExecuting = (_, info) =>
{
    switch (info.OperationType)
    {
        case DataFilterType.InsertByObject:
            if (info is { PropertyName: nameof(BaseEntity.Id), EntityValue: BaseEntity { Id: 0 } })
                info.SetValue(idGen.NextId());
            // …CreateTime / CreateUserId / CreateOrgId 同理兜底
            break;
        case DataFilterType.UpdateByObject:
            // …UpdateTime 每次刷新、UpdateUserId 记录操作人
            break;
    }
};
```

::: warning `CreateOrgId` 是数据范围锚点
`CreateOrgId` = 创建者当时所属机构，插入时由 AOP 从 `ICurrentUser.OrgId`（令牌 org claim）自动填充。数据范围过滤器正是按它决定行可见性。**若这个字段没被填上，按机构维度的数据范围查询会对业务表恒返回 0 行**——数据在库里，却查不出来。为 null 表示不受机构范围约束（系统内建数据、或创建者无归属机构）。
:::

## 实体基类

业务实体二选一继承：

- `BaseEntity`——主键 `Id` + 审计四件套（`CreateTime` / `CreateUserId` / `UpdateTime` / `UpdateUserId`）+ 软删除 `IsDelete`。实现 `ISoftDelete`。不需要机构隔离的表（全局字典、机构树自身）用它。
- `DataEntity`——继承 `BaseEntity`，额外带数据范围锚点 `CreateOrgId`，实现 `IOrgScoped`。需要「本机构/本机构及以下/仅本人/自定义」隔离的业务表用它。

## 泛型仓储 `IRepository<>`

数据访问的标准入口，开放泛型一次注册，任意实体即注即用。构造注入即可：

```csharp
public class DeviceService(IRepository<Device> repo) : IDeviceService
{
    public Task<Device?> Get(long id) => repo.GetByIdAsync(id);  // 自动带软删 + 数据范围过滤器
}
```

所有查询自动带全局过滤器。仓储覆盖不了的复杂操作（联表、事务、批量）直接用 `repo.Db` 上的 SqlSugar 原生能力——仓储是便捷层，不是把 ORM 关进笼子的抽象层。常用方法：`AsQueryable` / `GetByIdAsync` / `GetFirstAsync` / `AnyAsync` / `InsertAsync` / `InsertRangeAsync` / `UpdateAsync` / `DeleteAsync`（软删）/ `HardDeleteAsync`（物删）/ `RestoreAsync`（恢复）。

## 雪花 `WorkerId`

主键 `Id` 由 `IIdGenerator` 产生，默认实现是自写单文件雪花算法 `SnowflakeIdGenerator`（零第三方依赖）。64 位布局：

```text
┌─1 bit─┬────────41 bit────────┬───6 bit───┬───6 bit───┐
│ 符号 0 │ 相对纪元的毫秒时间戳    │  机器号    │  毫秒内序列  │
└───────┴──────────────────────┴───────────┴───────────┘
```

低位固定 12 bit（机器 6 + 序列 6）不是随手选的：41+12=53,ID 恒小于 2^53，落在 JS `Number.MAX_SAFE_INTEGER` 内，前端按数字解析 long 主键不丢精度。容量为 64 台机器、单机单毫秒 64 个 ID。

这 12 bit 也划出了种子的地盘：`id = 相对纪元毫秒数 × 4096 + 低位`，雪花永远发不出小于 4096 的号，`[1, 4095]` 便留给种子的固定 Id（内核用 `[1, 999]`，消费方从 `1000` 起取号）。`DatabaseInitializer` 启动时校验每个种子 Id 都在这个区间内、且同一实体上不重复：越界（迟早被雪花号追上撞主键）或撞号（幂等判存把后来那行当"已存在"静默跳过）一律启动即抛，CI 也有对应用例把这类错误拦在宿主启动之前。

机器号从配置 `TenonAdmin:Id:WorkerId` 注入（默认 0，范围 0–63）:

```json
{
  "TenonAdmin": {
    "Id": { "WorkerId": 3 }
  }
}
```

::: danger 水平扩展每实例必须不同
单机部署不配即可（回落 0）。**多实例水平扩展时必须为每个实例配置不同的 `WorkerId`**，否则不同实例同毫秒发号会撞主键——这是数据损坏级的问题，且默认静默发生。

内核给了一道防线：选了 Redis 缓存（明显的多实例意图）却没显式设置 `WorkerId` 时，启动即抛，把一个静默的主键冲突换成一条可读的启动错误。单实例请显式配 `0` 以示知情;k8s 可用 StatefulSet 的 Pod 序号注入。
:::

时钟安全上，`SnowflakeIdGenerator` 注入 `TimeProvider`（可测试）。检测到时钟回拨时，小幅（≤5ms,NTP 微调级）自旋等待追平，大幅回拨直接抛异常拒绝发号——绝不发出可能重复的 ID。
