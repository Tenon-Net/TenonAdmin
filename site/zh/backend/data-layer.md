# 数据层与审计

业务代码里查订单的那一行，既不写 `IsDelete == false`，也不写机构条件，可这两个条件都进了最终的 SQL。填进去的是 `SqlSugarSetup`：整个进程只有一个 `SqlSugarScope`，过滤器和审计 AOP 在它构造时一次挂上，此后无处可漏。

## 一个 `SqlSugarScope` 单例

`ISqlSugarClient` 以 `SqlSugarScope` 形态注册为单例。这是 SqlSugar 官方推荐的线程安全形态，内部按线程建 Client。构造时一次性挂上全局过滤器、审计 AOP、SQL 诊断日志三组钩子。

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

两条过滤器按**接口**匹配实体注册。SqlSugar 的 `AddTableFilter<T>` 只认接口或精确类型，不匹配基类。所以这里的标记走的是接口 `ISoftDelete` / `IOrgScoped`，而不是基类 `BaseEntity` / `DataEntity`。

### 软删除

实现 `ISoftDelete` 的实体，查询自动排除已删行：

```csharp
client.QueryFilter.AddTableFilter<ISoftDelete>(e => e.IsDelete == false);
```

已删数据对所有查询天然不可见。确需查已删数据时用 `.ClearFilter<ISoftDelete>()` 显式解除。

### 数据范围

对 `IOrgScoped` 实体（即 `DataEntity` 及其子类），按当前请求解析出的有效机构集过滤：

```csharp
client.QueryFilter.AddTableFilter<IOrgScoped>(e =>
    scope.Current.IsUnrestricted == true
    || (e.CreateOrgId != null && scope.Current.OrgIds.Contains(e.CreateOrgId.Value))
    || (scope.Current.IncludeSelf == true && e.CreateUserId == scope.Current.UserId));
```

`scope.Current` 的三个属性都与实体参数无关，SqlSugar 会先把它们在本地求值成常量，再拼进 WHERE。机构集合会被渲染成 SQL 的 `IN`。`IsUnrestricted` 为真时整体恒真，不过滤。

::: warning 数据范围只作用于查询
全局数据范围过滤器只作用于 SELECT，不作用于按主键的 `Updateable` / `Deleteable`。为此，`SqlSugarRepository<TEntity>` 对 `IOrgScoped` 实体的 `UpdateAsync` / `DeleteAsync` 内置了写路径范围守卫。写之前，会先经过带范围过滤器的查询，确认目标行在当前数据范围内。越权改删他机构的行，返回 0 行，默认就是安全的。绕过仓储、直接走 `Db.Updateable` / `Deleteable` 这类逃生舱口的写，不受此守卫保护，需要自行校验归属。
:::

## AOP 自动填审计字段

`SqlSugarScope` 的 `Aop.DataExecuting` 钩子在插入/更新时兜底填充基建字段。业务代码不碰这些字段。

| 字段 | 时机 | 填充规则 |
| --- | --- | --- |
| `Id` | 插入 | `Id == 0` 时填雪花号；显式给定（如种子数据）原样保留 |
| `CreateTime` | 插入 | 未设置时填当前时间 |
| `CreateUserId` | 插入 | 从当前登录用户填；系统上下文为 null 则留空 |
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
`CreateOrgId` 就是创建者当时所属的机构。插入时由 AOP 从 `ICurrentUser.OrgId` 自动填充，这个值来自令牌里的 org claim。数据范围过滤器正是按它决定行可见性。**若这个字段没被填上，按机构维度的数据范围查询会对业务表恒返回 0 行**。数据在库里，却查不出来。为 null 表示不受机构范围约束（系统内建数据、或创建者无归属机构）。
:::

## 实体基类

业务实体按**要哪几样能力**挑基类。五个基类叠成一条链，每往下一层多一样：

```text
PrimaryId          只有主键 Id
  └─ AuditEntity   + 审计四件套(CreateTime / CreateUserId / UpdateTime / UpdateUserId)
       ├─ BaseEntity     + 软删除 IsDelete            实现 ISoftDelete
       │    └─ DataEntity     + 机构锚点 CreateOrgId  实现 IOrgScoped
       └─ OrgAuditEntity + 机构锚点 CreateOrgId       实现 IOrgScoped
```

| 基类 | 审计 | 软删除 | 机构隔离 | 用在哪 |
| --- | --- | --- | --- | --- |
| `PrimaryId` | 无 | 无 | 无 | 明细/子表。**用不了内置仓储、不能种子化**（`IRepository<T>` 约束 `where T : BaseEntity`），通常经 `ISqlSugarClient` 随主表在同一事务里读写 |
| `AuditEntity` | 有 | 无 | 无 | 确需真删又要留痕的表：可真删的关联表、只增日志表 |
| `BaseEntity` | 有 | 有 | 无 | 全局共享表：字典、配置、机构树自身 |
| `DataEntity` | 有 | 有 | 有 | 需要「本机构 / 本机构及以下 / 仅本人 / 自定义」隔离的业务表 |
| `OrgAuditEntity` | 有 | 无 | 有 | 要机构隔离、又确需真删的表 |

**没有软删除的那两个，仓储 `DeleteAsync` 是物理删除**，行从库里移除，没有回收站，也没有 `RestoreAsync`。挑基类前先问这张表要不要回收站。

机构隔离的两个基类（`DataEntity` / `OrgAuditEntity`）都吃写路径守卫。仓储的 `UpdateAsync`/`DeleteAsync` 对 `IOrgScoped` 实体内置了范围检查，越权改删他机构的行会被拒，返回 0 行。

## 泛型仓储 `IRepository<>`

数据访问的标准入口，开放泛型一次注册，任意实体即注即用。构造注入即可：

```csharp
public class DeviceService(IRepository<Device> repo) : IDeviceService
{
    public Task<Device?> Get(long id) => repo.GetByIdAsync(id);  // 自动带软删 + 数据范围过滤器
}
```

所有查询自动带全局过滤器。仓储覆盖不了的复杂操作（联表、事务、批量）直接用 `repo.Db` 上的 SqlSugar 原生能力。仓储是便捷层，不是把 ORM 关进笼子的抽象层。常用方法：`AsQueryable` / `GetByIdAsync` / `GetFirstAsync` / `AnyAsync` / `InsertAsync` / `InsertRangeAsync` / `UpdateAsync` / `DeleteAsync`（软删）/ `HardDeleteAsync`（物删）/ `RestoreAsync`（恢复）。

## 雪花 `WorkerId`

主键 `Id` 由 `IIdGenerator` 产生，默认实现是自写单文件雪花算法 `SnowflakeIdGenerator`（零第三方依赖）。64 位布局：

```text
┌─1 bit─┬────────41 bit────────┬───6 bit───┬───6 bit───┐
│ 符号 0 │ 相对纪元的毫秒时间戳    │  机器号    │  毫秒内序列  │
└───────┴──────────────────────┴───────────┴───────────┘
```

低位固定 12 bit（机器 6 位 + 序列 6 位）不是随手选的。41+12=53，ID 恒小于 2^53，落在 JS 的 `Number.MAX_SAFE_INTEGER` 内。前端按数字解析 long 型主键，不会丢精度。容量是 64 台机器、单机单毫秒 64 个 ID。

这 12 bit 也划出了种子的地盘：`id = 相对纪元毫秒数 × 4096 + 低位`，雪花永远发不出小于 4096 的号，`[1, 999]` 因此可以放心留给内核内置种子。消费方从 `1000` 起取号，上限不是写死的数字。`DatabaseInitializer` 启动时会现算一次「此刻起雪花号最小会是多少」（`SnowflakeIdGenerator.CurrentFloor()`），严格小于它的种子 Id，从这一刻起就再也不会被这台实例真实发出的雪花号追上，因为时钟只会往前走。启动时校验两件事：每个种子 Id 都落在这个动态上限之内，同一实体上不重复。越界的号迟早被雪花追上撞主键，撞号的行会被幂等判存当「已存在」静默跳过。两种情况一律启动即抛，CI 也有对应用例把它们拦在宿主启动之前。

机器号从配置 `TenonAdmin:Id:WorkerId` 注入（默认 0，范围 0–63）：

```json
{
  "TenonAdmin": {
    "Id": { "WorkerId": 3 }
  }
}
```

::: danger 水平扩展每实例必须不同
单机部署不配即可（回落 0）。**多实例水平扩展时必须为每个实例配置不同的 `WorkerId`**，否则不同实例同毫秒发号会撞主键。两台机器的 `WorkerId` 一样，ID 里代表机器号的那 6 bit 就完全相同。只要同一毫秒里序列号也凑巧从头对齐，拼出来的 64 位数字就会一模一样。这是数据损坏级的问题，且默认静默发生。

内核给了一道防线：选了 Redis 缓存（明显的多实例意图）却没显式设置 `WorkerId` 时，启动即抛，把一个静默的主键冲突换成一条可读的启动错误。单实例请显式配 `0` 以示知情。k8s 场景可用 StatefulSet 的 Pod 序号注入。
:::

时钟安全上，`SnowflakeIdGenerator` 注入了 `TimeProvider`，这样可以测试。检测到时钟回拨时，小幅回拨（≤5ms，属于 NTP 微调级别）会自旋等待追平；大幅回拨则直接抛异常，拒绝发号。宁可不发，也绝不发出可能重复的 ID。
