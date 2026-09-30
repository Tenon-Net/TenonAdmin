# 加一个业务模块（后端）

用一个只有标题字段的「文档管理」模块，学习如何添加自己的实体、服务和受保护接口。完成后，用户可以创建和查看文档，修改与删除受数据范围约束；下一篇将为这些接口添加 Vue 页面。

开始前，请先[运行示例或接入自己的项目](/zh/guide/getting-started)。下面采用独立 ASP.NET Core 项目引用 `TenonAdmin` NuGet 包的方式，业务代码写在你自己的项目中。示例片段省略部分命名空间和服务接口声明，完整文件可对照测试宿主。

::: tip 如果你在维护框架源码
只有直接扩展 TenonAdmin 内核时，才把实体和服务放进 `TenonAdmin.Services`、控制器放进 `TenonAdmin.AspNetCore`。普通业务开发无需修改这些包。
:::

## 用真实代码打底

主示例采用仓库测试宿主中的 `SampleDoc`，便于对照完整代码和已有测试。另外两处是扩展参考，首次操作无需来回切换：

- `backend/src/TenonAdmin.Services/Dict/`：内核内置字典模块，普通表（不按机构隔离）的范本。
- `backend/tests/TenonAdmin.TestHost/`：集成测试用的消费方宿主，里面的 `SampleDoc` 是一个货真价实的机构隔离业务模块，走的正是路线 B。
- [tenon-example](https://github.com/Tenon-Net/tenon-example) 的 `Modules/Crm/`：另一个仓库里的独立参考应用，一整个 CRM 模块从实体、服务、控制器到菜单与权限种子全在里面，也是[在线演示](https://tenonadmin.52moyu.net/login)跑的那份代码。想看路线 B 在一个真项目里从头到尾是什么样，看它最全。

在自己的项目中为实体、服务和控制器分别建立文件。先完成最小接口，再根据业务需要添加分页、唯一性校验和初始数据。

## 选实体基类：`BaseEntity` 还是 `DataEntity`

选哪个基类，就看这张表要不要按机构做数据隔离。

- **不需要**（全局共享，比如字典、配置）→ 继承 `BaseEntity`。字典类型实体 `SysDictType`（`backend/src/TenonAdmin.Services/Entities/`）就是这样。
- **需要**（不同机构的用户只看得到、改得动自己机构的数据）→ 继承 `DataEntity`。它自带 `CreateOrgId` 锚点，查询会被全局过滤器按当前用户的数据范围自动裁剪。

`backend/tests/TenonAdmin.TestHost/SampleDoc.cs` 是后者的真实范本：

```csharp
[SugarTable("sample_doc", TableDescription = "示例机构隔离业务实体(集成测试)")]
public class SampleDoc : DataEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "标题")]
    public string Title { get; set; } = "";
}
```

`Id`、创建时间、创建人、创建机构和更新信息由框架自动填写，业务代码只填写 `Title`。这里的 AOP 指框架在数据库操作前后统一执行的处理。

`CreateOrgId` 记录数据所属机构，是后续数据范围过滤的依据。通过当前用户创建记录后，应检查该字段是否符合用户机构；绕过框架审计处理时，不能假定它仍会自动填写。

业务要求字段唯一时，再添加数据库唯一索引。例如，标题全局唯一可以用 `[SugarIndex("idx_sample_doc_title", nameof(Title), OrderByType.Asc, IsUnique = true)]`。是否全局唯一，还是机构内唯一，应由业务规则决定。

将 `SampleDoc` 换成自己的实体名和字段，放到业务项目中。测试宿主示例的接口声明在 `ISampleDocService.cs`，实现写在 `SampleDocService.cs`。

## 服务：读走过滤器，写先校验可见性

契约和实现的完整范本是 `backend/tests/TenonAdmin.TestHost/SampleDocService.cs`。三个读写要点决定了它是否「按机构隔离得住」：

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

- **读**走 `AsQueryable()`，全局过滤器按当前请求的数据范围裁剪，业务代码不写 `WHERE`。
- **改/删先 `GetByIdAsync` 校验可见性**：看不到就当「不存在/无权」返回 `false`。这不只是礼貌。数据范围全局过滤器只作用于查询（SELECT）。写路径靠的是另一层：`SqlSugarRepository` 对 `DataEntity` 的 `Update`/`Delete` 内置了范围守卫，越权改删他机构的行会返回 0。两层叠起来才严丝合缝。但绕过仓储、直接走 `Db.Updateable`/`Db.Deleteable` 这类逃生舱口的写，不受这层守卫保护，得自己校验归属。
- 方法都是 `virtual`。消费方想重写某一步，继承后 override 单个方法即可，不必整份复制。


## 控制器：权限码就是路由

每个动作挂 `[RolePermission]`，权限码就是规范化后的路由本身，代码里不写任何权限字符串，完整代码在 `backend/tests/TenonAdmin.TestHost/SampleDocController.cs`：

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

示例 DTO 保留测试宿主的最小形状。用于实际业务时，应在服务端校验标题必填且不超过实体声明的 128 字符；前端表单校验不能代替服务端校验。还需按业务规则验证其他输入和关联实体权限。

`GET /api/v1/sample/doc` 这个动作的权限码就是 `GET:/api/v1/sample/doc`。授权时管理员把这条路由挂到某个菜单/按钮上，再勾给某个角色，该角色的用户就有了权限。超管（`sadm` 声明）自动绕过这层检查。控制器返回 `Result<T>` 或直接 `return dto` 都行，信封由 `ResultEnvelopeFilter` 统一包。

两个可选特性按需加。需要审计的写操作挂 `[OperationLog("新增文档")]`。入参里的敏感字段（比如密码）会自动脱敏后再写进操作日志，蓝本是 `UserController`。想让整块模块能被消费方一键关掉，给控制器挂 `[Module("SampleDoc")]`。之后配上 `Api:DisabledModules=["SampleDoc"]`，这个模块就整体不注册路由了，蓝本是 `SysLogController`。

## 把服务和程序集交给内核

消费方不改内核，在自己的 `Program.cs` 里做两件事（完整代码见 `backend/tests/TenonAdmin.TestHost/Program.cs`）：

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

使用 `TryAddScoped` 可以保留调用方提前注册的实现，便于今后替换业务服务。独立业务接口尚未注册时，放在 `AddTenonAdmin()` 前后都能注册；替换内置接口则应在它之前完成。

::: warning 忘了 `ApplicationAssemblies.Add(...)` 就静默 404
框架需要通过 `ApplicationAssemblies` 发现独立业务程序集中的实体和控制器。遗漏登记时，实体不会参加自动建表，控制器也可能无法被发现，表现为接口返回 `404`。请核对登记的程序集确实包含目标类型。
:::

## 挂菜单、授权

权限码等于路由，授权靠在菜单树上勾路由，所以新接口要能被普通用户调通，得先有对应的菜单节点。运行时在后台配：

1. 进**菜单管理**，建菜单节点：`Type=菜单`、`Path` 填前端路由地址（如 `/sample/doc`）、`Component` 填对应 `.vue` 文件相对路径（如 `sample/doc/index`）、`所属应用`选一个顶级目录。
2. 需要按钮级权限就建 `Type=按钮` 节点，`Permission` 填对应路由码（如 `POST:/api/v1/sample/doc`）。前端 `v-auth` 按它显隐按钮。
3. 进**角色管理**，给角色勾选该菜单/按钮，该角色下的用户即获得对应路由权限，授权变更即时生效（内核会失效对应缓存）。
4. 超管开发期不用配权，自动放行全部路由。

想让菜单出厂就预置（而不是每套环境手点），用种子 `DefaultMenuSeed` 那套写法批量播 `SysMenu` 行（菜单节点、按钮节点都是 `SysMenu`），取号参照下方种子数据的登记法。给内置模块**改**已有种子行（比如同 Id 补一个字段）时要记得 bump `SysSchemaVersion.Current`。老库只有版本号变了才会经 `SyncOnUpgrade` 回填（见 `SqlSugar/Entities/SysSchemaVersion.cs` 注释）；纯新增行不需要 bump。

在**模块管理**里给业务应用填「路由前缀 `apiPrefix`」（= 控制器的路由段，如 `sample`，对应 `/api/v1/sample/...`），能让菜单页「配置权限」的路由下拉默认只列本应用的路由，降噪而已，不是权限边界。留空则不过滤。注意填的是路由段，不是模块编码，二者可以不一致。

## 确认最小接口可用

启动自己的宿主，在 Development 环境访问 `/openapi/v1.json`，应能找到 `/api/v1/sample/doc`。未出现时，先检查程序集登记与控制器路由。

用登录令牌调用新增和列表接口，确认能查到刚创建的标题。再按上方「挂菜单、授权」为普通用户授权，用不同机构的用户验证查询和改删范围。只用超级管理员测试，不能证明权限配置正确。


## 测试

用 `WebApplicationFactory` 写 HTTP 级回归（蓝本 `backend/tests/TenonAdmin.Tests/SampleDocScopeTests.cs`）：造用户、授菜单 → 带 token 调端点 → 断言信封。SQLite/MySQL 两条腿都要绿（`TestDb.cs` 按环境变量派生隔离库）：

```bash
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~SampleDoc"
```

后端这套跑通、`/openapi/v1.json` 里能看到新接口后，给这张表做管理页面是下一篇的事：[前端加一个页面](/zh/guide/frontend-page)。

## 按需扩展查询

最小示例返回完整列表，不需要分页或缓存。下面的规则用于扩展到真实业务时，按需采用。

需要分页时，将输入类型继承 `PageInputBase`，使用 `Current`、`Size`、`SortField` 和 `SortOrder` 接收分页与排序参数。按条件筛选可以用 `WhereIF(条件, 表达式)`；`.ToPagedListAsync(input.Current, input.Size)` 返回当前页数据和总数。完整参考是 `UserService.PageAsync` 与 `DictService.PageTypesAsync`。

有唯一列时，新增前的查重要带上软删行：`repo.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(x => x.Code == input.Code)`。不清软删过滤器的话，一条已软删的同码行会绕过应用层查重，在数据库唯一索引上撞出一个原生 500。查到真的重复，就 `AdminException.ThrowIf(dup, ErrorCode.XxxExists)` 抛业务码。

缓存不是每个查询都要加。列表、分页这类冷路径直接查库就行，内核的 `Dict`/`Config` 分页都没缓存。值得加的只有「高频读 + 低频变」的热点，比如某类下拉数据源。写法参考 `DictService.GetItemsByTypeAsync` 的读穿透缓存：先从 `ICacheProvider` 取，增删改之后再显式 `RemoveAsync` 失效。规范细则见[后端代码规范](/zh/standard/backend)。


## 错误码（可选）

要精确区分「不存在」和其他失败，内核内置模块的做法是往 `Core/ErrorCode.cs` 枚举里加数字码，**只加码，不写文案**。字典模块的 `DictTypeNotFound = 43001`、`DictTypeCodeExists = 43002` 就是例子，文案由前端按码翻译。但 `ErrorCode` 是内核枚举，消费方不能扩展它，可以像 `SampleDocService` 那样直接用返回值表达结果（`false` 表示不存在/无权），或者自定义异常，经自己的异常过滤器兜底。

## 种子数据（可选）

需要出厂默认数据时，实现泛型 `ISeedData<T>`，给每行一个固定 `Id` 保幂等。注意别直接实现非泛型 `ISeedData`：它只是 DI 收集用的空标记，直接实现虽然能编译，但启动时反推不出实体类型会崩。内核自己的写法在 `Seed/DictSeed.cs`，消费方范例是 `backend/tests/TenonAdmin.TestHost/SampleWidgetSeed.cs`：

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

种子要注册在**你自己的 `Program.cs`** 里。内核不扫描程序集找种子，`ApplicationAssemblies` 只管实体建表和控制器挂载。忘了注册，种子就静默不执行：

```csharp
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SampleWidgetSeed>());
```

业务种子的固定 `Id` 从 `TenonSeedIds.ConsumerMin`（1000）开始分配，并低于启动时计算的雪花 ID 下界。每个实体的种子编号需要统一登记，避免与内置数据或历史种子复用同一个主键：

| 区间 | 归谁 | 为什么 |
|---|---|---|
| `[1, 999]` | 内核内置种子 | 内核每加一个鉴权端点就多一行菜单，号段只会往上涨 |
| `[1000, 动态地板)` | **你的种子** | 从 `ConsumerMin` 起取，上限是启动时算出的雪花地板（`SnowflakeIdGenerator.CurrentFloor()`），今天已是 15 位数量级 |
| `[动态地板, …)` | 雪花运行时发号区 | 种子占了它，早晚会被这台实例真实发出的雪花号追上撞主键 |

同一批种子里你可能连播好几行，尤其是复制粘贴的时候。给每行取号沿用内核菜单种子的登记法：记住当前用到的最大号，**新行一律取「最大号 + 1」，永不回填空洞**。空洞往往是历史上被挪走或删掉的号，复用会撞上老库里的存量行。

::: warning 种子 Id 撞号或越界：现在启动就拒，不再静默
`DatabaseInitializer` 在启动时按实体检查种子编号。发现同实体重复或越界的 ID 时会拒绝启动，避免把新行误认成已有数据，或在升级同步时覆盖其他种子。遇到这类错误，按日志定位种子类和 ID，再检查该编号是否已在历史版本使用。测试参考 `SeedIdRangeTests`。
:::

## 提交前自查

- [ ] 实体基类选对（要机构隔离 → `DataEntity`）；唯一列补了唯一索引；审计字段没手写
- [ ] 服务方法 `virtual`；改/删先 `GetByIdAsync` 校验可见性；唯一列查重带 `ClearFilter<ISoftDelete>`
- [ ] 控制器每个动作挂 `[RolePermission]`；需审计的写挂 `[OperationLog(...)]`
- [ ] `Program.cs` 里 `ApplicationAssemblies.Add(...)` + 服务 `TryAdd` 都到位（漏程序集 = 静默 404）
- [ ] 种子实现的是泛型 `ISeedData<T>`、注册在自己的 `Program.cs`、固定 Id `≥ 1000` 且不撞号
- [ ] 改了内置种子的已有行 → bump 了 `SysSchemaVersion.Current`
- [ ] 测试 SQLite/MySQL 双绿
- [ ] 运行时：菜单管理建了节点、角色管理勾了授权
