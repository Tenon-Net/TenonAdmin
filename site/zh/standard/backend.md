# 后端规范（.NET 10 内核）

改后端代码前后对着这份清单核一遍，每条都是内核里已落地的硬规则。想知道某条为什么这么定，顺着链接去对应深读篇;更完整的正反例见仓库 [`docs/coding-standards.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/coding-standards.md)。

::: tip 第一原则
内核以 NuGet 分发，消费方不改源码就能替换任一部件。凡新增可替换服务，一律 `TryAdd*` 注册、接口背书、方法拆 `virtual` 步。这三条没有例外。机制见 [可替换性模型](/zh/backend/replaceability)。
:::

## 分层落点

- 依赖只能自上而下，越层禁止：`Core`（契约）← `SqlSugar`（数据）← `Services`（领域+实体）← `AspNetCore`（宿主）← `TenonAdmin`（元包）。新增代码先想清楚落哪层;拿不准就回 [架构分层](/zh/backend/architecture) 看全景。
- 运行时依赖只有 SqlSugarCore + Microsoft.\*，核心包不引入其它第三方框架。
- 实体放 `Services` 层，不放 `SqlSugar` 层。
- 每层装配集中在一个 `*Setup.cs`（`SqlSugarSetup` → `ServicesSetup` → `TenonAdminSetup` 组合根），不散落注册。

## 可替换性（`ReplaceabilityTests` 锁死，当契约看）

- 内置可替换服务全部 `TryAdd*`,**严禁**裸 `Add*`;消费方在 `AddTenonAdmin()` 之前注册同接口即胜出。
- 长方法拆成小 `virtual` 步（如 `SessionService.EnforceConcurrencyAsync`），消费方覆写一步而非抄整方法。
- 每个服务先有 `I*Service`，实现类 `virtual`。
- 改实体扫描/控制器注册时，务必保留 `options.ApplicationAssemblies` 挂载路径（业务实体建表、控制器 `AddApplicationPart`），否则消费方模块静默失效。

## 实体

- 定义在 `Services/Entities/`，系统内核表命名 `Sys*`。
- 选基类：普通表继承 `BaseEntity`（主键+审计四件套+软删）;需按机构隔离的继承 `DataEntity`（多带 `CreateOrgId` 锚点）。
- 主键统一 `Id`（雪花，AOP 填，不手赋）;软删统一 `IsDelete`，查已删数据要显式 `.ClearFilter<ISoftDelete>()`。
- 表结构没预留的额外信息塞 `ExtJson`，不新开列。
- 特性照 `Entities/SysDictType.cs`:`[SugarTable]` / 唯一索引 `[SugarIndex(IsUnique=true)]` / `[SugarColumn(Length, ColumnDescription, IsNullable)]`。
- 不可变约定（如「Code 创建后不可变」）写进注释，并在 Service 的 Update 里落实（不改该字段）。字段与审计机制见 [数据层与审计](/zh/backend/data-layer)。

## 服务

- 一服务一目录：`I{X}Service.cs` + `{X}Service.cs` + `{X}Models.cs`（DTO 用 `record`，命名 `{X}Input`/`{X}PageInput`/`{X}Output`）。
- 主构造函数注入依赖，方法 `virtual`，异步方法 `Async` 后缀且热路径收 `CancellationToken`。
- 分页统一 `PagedList<T>` + `.ToPagedListAsync(current, size)`。
- 校验用 `AdminException.ThrowIf(条件, ErrorCode.X)`，不手写 if-throw。

## 控制器

- `[RolePermission]` 无参：权限码就是 `{METHOD}:/{路由模板}`（如 `GET:/api/v1/sys/dict/type/page`）。代码里永不写 `"sys:user:add"` 之类魔法串，权限在角色-菜单界面勾路由即配;超管（`sadm`）放行。
- 无需特定权限的登录态端点用 `[ActiveSession]`;匿名端点显式 `[AllowAnonymous]`（全局 `RequireAuthorization()` 兜底，漏挂不会静默公开）。
- 需审计的写操作挂 `[OperationLog(...)]`;整模块可关停加 `[Module("X")]`（经 `Api:DisabledModules` 摘除，但带菜单的内置模块禁止删）。
- 控制器可直接 `return dto`（`ResultEnvelopeFilter` 兜底包信封）;内置控制器为契约清晰显式 `Result<T>.Ok(...)`。范例照 `Controllers/DictController.cs`;信封在管线哪一步套上，[请求管线](/zh/backend/request-pipeline) 里有全程。

## 错误处理

- 业务错误抛 `AdminException(ErrorCode)` 或返回 `ErrorCode`，由 `AdminExceptionFilter` 统一转信封。
- `ErrorCode` 是数字枚举，永不带本地化文案（`Core/ErrorCode.cs`）,i18n 全在前端按码翻译;新增错误码往枚举里加即可。

## 数据访问

- 注入 `IRepository<T>`;复杂查询走 `.AsQueryable()`，逃生走 `.Db`(`Db.Deleteable<>()` / `Db.Ado.UseTranAsync`)。
- 软删与数据范围是全局过滤器，业务代码不重复写过滤条件。
- 唯一性查重要带上软删行：`.ClearFilter<ISoftDelete>().AnyAsync(...)`，否则会撞库唯一索引抛原生 500。
- 多写操作包事务 `Db.Ado.UseTranAsync`，失败整体回滚;缓存失效放事务提交之后，提前清了而事务回滚，缓存和库就对不上。
- 审计字段（`Id` 雪花、`CreateTime`/`User`/`Org`、`UpdateTime`/`User`）由 AOP 填，只设业务字段。

::: danger CreateOrgId 是数据范围锚点
`DataEntity` 行的 `CreateOrgId` 不填，机构范围查询里恒为 0 行。它由 AOP 自动填，绝不手动绕过赋值。原理见 [多组织数据权限](/zh/backend/data-scope)。
:::

## 缓存

- 模型是 cache-aside（读穿透）+ 显式失效，不是每次查库;增删改后既 `RemoveAsync` 失效缓存，也广播事件（如 `DictService.InvalidateAsync` → `DictChangedEvent`）供跨节点失效/审计/推送订阅。
- 逻辑键集中在 `Core/CacheKeys.cs`，禁散落魔法串;前缀 `Cache:KeyPrefix`（默认 `tenon:`）由 provider 统一追加。
- 默认进程内 `MemoryCacheProvider`;多实例共享装可选包 `TenonAdmin.Caching.Redis`,`AddTenonAdminRedisCache` 须在 `AddTenonAdmin` **之前**注册才能赢过 `TryAdd`（业务代码零改动）。

## DI 装配

- 装配写进 `*Setup.cs`;内置服务显式 `TryAdd`（不靠扫描，可预测、可替换），种子用 `TryAddEnumerable` 按实现类型防重。
- 无状态服务 `Singleton`（哈希、验证码生成器、文件存储、缓存 provider、事件总线）;按请求 `Scoped`（多数业务服务，与仓储一致）。

## 种子数据

- 实现 `ISeedData<TEntity>`,`HasData()` 返默认行（返空集合合法=「库里已有就不播种」）。
- **固定 Id 保幂等**：只在缺失时补，不回改已存在行。所以界面上的改动不会被重启覆盖。
- **Id 必须落保留区间**(`Core/TenonSeedIds.cs`)：内核 `[1, 999]`、消费方 `[1000, 4095]`、`4096+` 归雪花运行时;越界、`Id=0` 或与已有种子 Id 重复，启动即拒。

## 命名 / 组织

- 命名空间随目录;一类型一文件;后缀 `Sys*` 实体、`I*` 接口、`*Service`/`*Provider`/`*Filter`/`*Attribute`。
- 启用可空引用类型;时间统一走注入的 `TimeProvider`（可测试），不用 `DateTime.Now` 裸调。

## 包管理

- 增/改依赖在 `backend/Directory.Packages.props` 的 `<PackageVersion>` 里，不在各 `.csproj` 写版本号;共享构建/NuGet 元数据在 `backend/Directory.Build.props`。

## 注释

- 公共类型/成员用 `/// <summary>` 说清职责与边界;关键取舍引设计文档节号（`§N`/`TN`）。
- 行内注释只解释 WHY（并发、事务顺序、边界、跨方言坑），不复述 WHAT;中文，与既有代码一致。
- 刻意简化 / 有上限的实现用 `// ponytail:` 标注上限与升级路径。
