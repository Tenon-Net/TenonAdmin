# 加一个业务模块(后端)

在 TenonAdmin 之上加一张业务表、一套接口,不改内核一行代码。这页从选实体基类一路走到接口能被授权、能被前端调通。

::: tip 两条路线,只差"代码放哪"
- **路线 A**——直接改本仓库,在内核内加,新代码进 `TenonAdmin.Services` / `TenonAdmin.AspNetCore`。
- **路线 B**——你的项目装了 `TenonAdmin` NuGet 包,在自己的业务程序集里加,靠 `options.ApplicationAssemblies` 挂载,内核源码一行不碰。

除了"放哪"之外两条路完全一样。下面走路线 B——这也是使用方的推荐路线。
:::

## 用真实代码打底

这页不编"商品"示例,而是带你读仓库里两处真实存在、CI 里跑着的代码:

- `backend/src/TenonAdmin.Services/Dict/`——内核内置字典模块,普通表(不按机构隔离)的范本。
- `backend/tests/TenonAdmin.TestHost/`——集成测试用的消费方宿主,里面的 `SampleDoc` 是一个货真价实的机构隔离业务模块,走的正是路线 B。

照着这两处的结构走,你加的模块会自然长成内核期望的样子。

## 选实体基类:`BaseEntity` 还是 `DataEntity`

先问自己一个问题:这张表需不需要按机构做数据隔离?

- **不需要**(全局共享,比如字典、配置)→ 继承 `BaseEntity`。字典类型实体 `SysDictType`(`backend/src/TenonAdmin.SqlSugar/Entities/`)就是这样。
- **需要**(不同机构的用户只看得到、改得动自己机构的数据)→ 继承 `DataEntity`。它自带 `CreateOrgId` 锚点,查询会被全局过滤器按当前用户的数据范围自动裁剪。

`backend/tests/TenonAdmin.TestHost/SampleDoc.cs` 是后者的真实范本:

```csharp
[SugarTable("sample_doc", TableDescription = "示例机构隔离业务实体(集成测试)")]
public class SampleDoc : DataEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "标题")]
    public string Title { get; set; } = "";
}
```

审计字段(`Id` / `CreateTime` / `CreateUserId` / `CreateOrgId` / `UpdateTime` / `UpdateUserId`)由 AOP 自动填,业务代码不要手写——尤其是 `CreateOrgId`,它是数据范围能生效的锚点,漏填的话按机构查询会查出 0 行。有唯一列时在实体上补一个唯一索引,比如 `[SugarIndex("idx_sample_doc_title", nameof(Title), OrderByType.Asc, IsUnique = true)]`。

把 `SampleDoc` 换成你自己的实体名和字段,放进消费方项目自己的程序集就行(路线 A 才放进 `TenonAdmin.Services`)。

## 服务:读走过滤器,写先校验可见性

契约和实现的完整范本是 `backend/tests/TenonAdmin.TestHost/SampleDocService.cs`。三个读写要点决定了它是否"按机构隔离得住":

```csharp
public class SampleDocService(IRepository<SampleDoc> repo) : ISampleDocService
{
    public virtual async Task<long> CreateAsync(string title)
    {
        var doc = new SampleDoc { Title = title };
        await repo.InsertAsync(doc);   // CreateOrgId 由审计 AOP 从当前用户机构回填
        return doc.Id;
    }

    public virtual async Task<IReadOnlyList<SampleDoc>> ListAsync() =>
        await repo.AsQueryable().OrderBy(d => d.Id).ToListAsync();  // 全局过滤器按数据范围裁剪

    public virtual async Task<bool> RenameAsync(long id, string title)
    {
        var doc = await repo.GetByIdAsync(id);   // 越权/不存在 → null(同样经范围过滤)
        if (doc is null) return false;
        doc.Title = title;
        return await repo.UpdateAsync(doc) > 0;
    }

    public virtual async Task<bool> DeleteAsync(long id)
    {
        if (await repo.GetByIdAsync(id) is null) return false;
        return await repo.DeleteAsync(id) > 0;
    }
}
```

- **读**走 `AsQueryable()`,全局过滤器按当前请求的数据范围裁剪,业务代码不写 `WHERE`。
- **改/删先 `GetByIdAsync` 校验可见性**:看不到就当"不存在/无权"返回 `false`。这不只是礼貌——数据范围全局过滤器只作用于查询(SELECT);写路径靠的是 `SqlSugarRepository` 对 `DataEntity` 的 `Update`/`Delete` 内置的范围守卫,越权改删他机构的行返回 0。两层叠起来才严丝合缝。要注意的是,绕过仓储、直接走 `Db.Updateable`/`Db.Deleteable` 逃生舱口的写**不受**这层守卫,得自己校验归属。
- 方法都是 `virtual`。消费方想重写某一步,继承后 override 单个方法即可,不必整份复制。

真实的管理列表通常要分页,那就把 `ListAsync` 换成 `PageAsync`,入参继承 `PageInputBase`(自带 `Current`/`Size`/`SortField`/`SortOrder`——没有叫 `PageInput` 的基类,别记混),用 `WhereIF` 拼条件、`ToPagedListAsync` 出分页。内核里 `UserService.PageAsync`、`DictService.PageTypesAsync` 是现成蓝本。

有唯一列时,新增前的查重要带上软删行:`repo.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(x => x.Code == input.Code)`。不清软删过滤器的话,一条已软删的同码行会绕过应用层查重、在数据库唯一索引上撞出一个原生 500;查到重复就 `AdminException.ThrowIf(dup, ErrorCode.XxxExists)` 抛业务码。

缓存不是每个查询都要加。列表、分页这类冷路径直接查库就行(内核的 `Dict`/`Config` 分页都没缓存);只有"高频读 + 低频变"的热点(比如某类下拉数据源)才值得加,写法参考 `DictService.GetItemsByTypeAsync` 的读穿透缓存(`ICacheProvider` + 增删改后显式 `RemoveAsync` 失效),规范细则见[后端代码规范](/zh/standard/backend)。

## 控制器:权限码就是路由

`backend/tests/TenonAdmin.TestHost/SampleDocController.cs`——每个动作挂 `[RolePermission]`,权限码就是规范化后的路由本身,代码里不写任何权限字符串:

```csharp
[ApiController]
[Route("api/v1/sample/doc")]
public class SampleDocController(ISampleDocService svc) : ControllerBase
{
    [HttpGet]
    [RolePermission]
    public async Task<Result<IReadOnlyList<SampleDoc>>> List() =>
        Result<IReadOnlyList<SampleDoc>>.Ok(await svc.ListAsync());

    [HttpPost]
    [RolePermission]
    public async Task<Result<long>> Create([FromBody] SampleDocInput input) =>
        Result<long>.Ok(await svc.CreateAsync(input.Title));

    [HttpPut("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Rename(long id, [FromBody] SampleDocInput input) =>
        Result<bool>.Ok(await svc.RenameAsync(id, input.Title));

    [HttpDelete("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Delete(long id) =>
        Result<bool>.Ok(await svc.DeleteAsync(id));
}

public record SampleDocInput(string Title);
```

`GET /api/v1/sample/doc` 这个动作的权限码就是 `GET:/api/v1/sample/doc`。授权时管理员把这条路由挂到某个菜单/按钮上、再勾给某个角色,该角色的用户就有了权限;超管(`sadm` 声明)自动绕过。控制器返回 `Result<T>` 或直接 `return dto` 都行,信封由 `ResultEnvelopeFilter` 统一包。

两个可选特性按需加:需要审计的写操作挂 `[OperationLog("新增文档")]`,入参里的敏感字段(如密码)会被自动脱敏后写进操作日志(蓝本 `UserController`);想让整块模块能被消费方一键关掉,给控制器挂 `[Module("SampleDoc")]`,之后配 `Api:DisabledModules=["SampleDoc"]` 就整体不注册路由(蓝本 `SysLogController`)。

## 把服务和程序集交给内核

消费方不改内核,在自己的 `Program.cs` 里做两件事——完整范本 `backend/tests/TenonAdmin.TestHost/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    // 把自己的程序集交给内核:里面的实体加入 CodeFirst 建表,控制器 AddApplicationPart 挂载
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
});

// 自己的服务用 TryAdd 注册(未被内核占用的接口,AddTenonAdmin 之前/之后都行)
builder.Services.TryAddScoped<ISampleDocService, SampleDocService>();

var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

必须用 `TryAdd`,不是 `Add`。这样消费方才能在 `AddTenonAdmin()` **之前**注册同接口的自定义实现来覆盖默认行为——这是整个内核"可替换"设计的根规则,内置服务(比如 `Dict`)在 `ServicesSetup.cs` 里也是这么注册的。你自己的服务如果没人跟你抢接口,`TryAdd` 和 `Add` 效果一样,统一写 `TryAdd` 省得日后被覆盖时踩坑。

::: warning 忘了 `ApplicationAssemblies.Add(...)` 就静默 404
内核**不会自动扫描发现**你的模块。漏了这一行,`SampleDoc` 不会建表、`SampleDocController` 不会注册路由,直接 404,没有任何兜底开关或提示。(曾有个 `ScanApplicationAssemblies` 自动扫描开关从未实现,已于发包前删除,别去找它。)
:::

## 错误码(可选)

要精确区分"不存在"和其他失败,内核内置模块的做法是往 `Core/ErrorCode.cs` 枚举里加数字码,**只加码,不写文案**——字典模块的 `DictTypeNotFound = 43001`、`DictTypeCodeExists = 43002` 就是例子,文案由前端按码翻译。消费方受限于 `ErrorCode` 是内核枚举、不可扩展,可以像 `SampleDocService` 那样直接用返回值(`false` 表示不存在/无权)表达结果,或者自定义异常经自己的异常过滤器兜底。

## 种子数据(可选)

需要出厂默认数据时,实现**泛型** `ISeedData<T>`(非泛型 `ISeedData` 只是 DI 收集用的空标记,直接实现它能编译,但启动时反推不出实体类型会崩),给每行一个固定 `Id` 保幂等。蓝本 `Seed/DictSeed.cs`,消费方范例 `backend/tests/TenonAdmin.TestHost/SampleWidgetSeed.cs`:

```csharp
public sealed class SampleWidgetSeed : ISeedData<SampleWidget>
{
    public IEnumerable<SampleWidget> HasData() =>
    [
        new() { Id = TenonSeedIds.ConsumerMin,     Name = "widget-a" },
        new() { Id = TenonSeedIds.ConsumerMin + 1, Name = "widget-b" },
    ];
}
```

种子要注册在**你自己的 `Program.cs`** 里——内核不扫描程序集找种子(`ApplicationAssemblies` 只管实体建表和控制器挂载),忘了注册就静默不执行:

```csharp
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SampleWidgetSeed>());
```

固定 `Id` 必须落在消费者保留区间 `[1000, 4095]`(常量 `TenonSeedIds.ConsumerMin` ~ `ConsumerMax`)。别沿用"随手挑个小整数"的老习惯——你和内核会往同一批表(`sys_menu` / `sys_config` …)里播种,今天不撞不代表升级内核包后不撞,而那时你库里已经有那行、退不回去了:

| 区间 | 归谁 | 为什么 |
|---|---|---|
| `[1, 999]` | 内核内置种子 | 内核每加一个鉴权端点就多一行菜单,号段只会往上涨 |
| `[1000, 4095]` | **你的种子** | 从 `ConsumerMin` 起取,避开内核未来会用到的低号段 |
| `[4096, …]` | 雪花运行时发号区 | `id = 毫秒 × 4096 + 低位`,种子占了它,将来某次插入必然主键冲突 |

同一批种子里你可能连播好几行(尤其复制粘贴时),给每行取号沿用内核菜单种子的登记法:记住当前用到的最大号,**新行一律取"最大号 + 1",永不回填空洞**。空洞往往是历史上被挪走或删掉的号,复用会撞上老库里的存量行。

::: warning 种子 Id 撞号或越界:现在启动就拒,不再静默
一个撞了号的固定 Id 过去是**悄无声息**地坏:幂等判存把后来那行当"已存在"跳过(菜单树无声缺一块),开了 `SyncOnUpgrade` 的种子升级时还会把别人那行覆盖掉。现在 `DatabaseInitializer` 会在启动时逐实体登记所有种子(内核 + 消费者)声领的固定 Id,一旦发现越界或同实体重复,**当场抛异常、应用起不来**,并告诉你该换哪段号;`SeedIdRangeTests` 里有对应契约测试,CI 会在任何宿主启动前先变红。既盖住"复制行忘改 Id"的自撞,也盖住跨种子撞号。
:::

## 挂菜单、授权

权限码等于路由,授权靠在菜单树上勾路由,所以新接口要能被普通用户调通,得先有对应的菜单节点。运行时在后台配:

1. 进**菜单管理**,建菜单节点:`Type=菜单`、`Path` 填前端路由地址(如 `/sample/doc`)、`Component` 填对应 `.vue` 文件相对路径(如 `sample/doc/index`)、`所属应用`选一个顶级目录。
2. 需要按钮级权限就建 `Type=按钮` 节点,`Permission` 填对应路由码(如 `POST:/api/v1/sample/doc`)——前端 `v-auth` 按它显隐按钮。
3. 进**角色管理**,给角色勾选该菜单/按钮,该角色下的用户即获得对应路由权限,授权变更即时生效(内核会失效对应缓存)。
4. 超管开发期不用配权,自动放行全部路由。

想让菜单出厂就预置(而不是每套环境手点),用种子 `DefaultMenuSeed` 那套写法批量播 `SysMenu` 行(菜单节点、按钮节点都是 `SysMenu`),取号照上一节的登记法。给内置模块**改**已有种子行(比如同 Id 补一个字段)时要记得 bump `SysSchemaVersion.Current`——老库只有版本号变了才会经 `SyncOnUpgrade` 回填(见 `SqlSugar/Entities/SysSchemaVersion.cs` 注释);纯新增行不需要 bump。

在**模块管理**里给业务应用填"路由前缀 `apiPrefix`"(= 控制器的路由段,如 `sample`,对应 `/api/v1/sample/...`),能让菜单页"配置权限"的路由下拉默认只列本应用的路由,降噪而已,不是权限边界——留空则不过滤。注意填的是路由段,不是模块编码,二者可以不一致。

## 测试

用 `WebApplicationFactory` 写 HTTP 级回归(蓝本 `backend/tests/TenonAdmin.Tests/ModulePortalTests.cs`):造用户、授菜单 → 带 token 调端点 → 断言信封。SQLite/MySQL 两条腿都要绿(`TestDb.cs` 按环境变量派生隔离库):

```bash
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~SampleDoc"
```

后端这套跑通、`/openapi/v1.json` 里能看到新接口后,给这张表做管理页面是下一篇的事:[前端加一个页面](/zh/guide/frontend-page)。

## 提交前自查

- [ ] 实体基类选对(要机构隔离 → `DataEntity`);唯一列补了唯一索引;审计字段没手写
- [ ] 服务方法 `virtual`;改/删先 `GetByIdAsync` 校验可见性;唯一列查重带 `ClearFilter<ISoftDelete>`
- [ ] 控制器每个动作挂 `[RolePermission]`;需审计的写挂 `[OperationLog(...)]`
- [ ] `Program.cs` 里 `ApplicationAssemblies.Add(...)` + 服务 `TryAdd` 都到位(漏程序集 = 静默 404)
- [ ] 种子实现的是泛型 `ISeedData<T>`、注册在自己的 `Program.cs`、固定 Id 落在 `[1000, 4095]` 且不撞号
- [ ] 改了内置种子的已有行 → bump 了 `SysSchemaVersion.Current`
- [ ] 测试 SQLite/MySQL 双绿
- [ ] 运行时:菜单管理建了节点、角色管理勾了授权
