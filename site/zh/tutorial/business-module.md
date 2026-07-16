# 端到端加一个业务模块

在 TenonAdmin 之上加一个业务表、一套接口,不用改内核一行代码。本篇走一遍完整链路:实体 → 服务 → 控制器 → 挂载 → 授权 → 前端能调。

::: tip 两条路线
- **路线 A**——直接改本仓库,在内核内加。
- **路线 B**——消费方(装了 `TenonAdmin` NuGet 包的项目)在自己的业务程序集里加,靠 `options.ApplicationAssemblies` 挂载,**不碰内核源码**。

两条路线除了"代码放哪"之外完全一致。本篇走路线 B——这也是使用方的推荐路线。
:::

## 用真实代码打底

本篇不是编造一个"商品"示例,而是原样带你读仓库里两处**真实存在、CI 里跑着**的代码:

- `backend/src/TenonAdmin.Services/Dict/`——内核内置的字典模块,普通表(不需要按机构隔离数据)的范本。
- `backend/tests/TenonAdmin.TestHost/`——集成测试用的消费方宿主,里面的 `SampleDoc` 就是一个货真价实的"机构隔离业务模块",走的正是路线 B。

跟着这两处代码的结构走,你加的模块自然长成内核期望的样子。

## 1. 选实体基类:`BaseEntity` 还是 `DataEntity`

先问自己一个问题:这张表需不需要按机构做数据隔离?

- **不需要**(全局共享数据,比如字典、配置)→ 继承 `BaseEntity`。字典类型实体 `SysDictType` 就是这样(`backend/src/TenonAdmin.SqlSugar/Entities/`)。
- **需要**(不同机构的用户只能看到/改自己机构的数据)→ 继承 `DataEntity`。它自带一个 `CreateOrgId` 锚点,查询会被全局过滤器按当前用户的数据范围自动裁剪。

`backend/tests/TenonAdmin.TestHost/SampleDoc.cs` 是后者的真实范本:

```csharp
using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.TestHost;

[SugarTable("sample_doc", TableDescription = "示例机构隔离业务实体(集成测试)")]
public class SampleDoc : DataEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "标题")]
    public string Title { get; set; } = "";
}
```

审计字段(`Id`/`CreateTime`/`CreateUserId`/`CreateOrgId`/`UpdateTime`/`UpdateUserId`)由 AOP 自动填,**业务代码不要手写**——尤其是 `CreateOrgId`,这是数据范围能生效的锚点,漏填的话按机构查询会查出 0 行。

把 `SampleDoc` 换成你自己的实体名、字段,放进你消费方项目自己的程序集里就行(路线 A 才放进 `TenonAdmin.Services`)。

## 2. 服务接口 + 实现

契约 `ISampleDocService.cs`:

```csharp
public interface ISampleDocService
{
    Task<long> CreateAsync(string title);
    Task<IReadOnlyList<SampleDoc>> ListAsync();
    Task<bool> RenameAsync(long id, string title);
    Task<bool> DeleteAsync(long id);
}
```

实现 `SampleDocService.cs`——注意读写数据范围的三个要点:

```csharp
public class SampleDocService(IRepository<SampleDoc> repo) : ISampleDocService
{
    public virtual async Task<long> CreateAsync(string title)
    {
        var doc = new SampleDoc { Title = title };
        await repo.InsertAsync(doc);   // CreateOrgId 由审计 AOP 从当前用户机构自动回填
        return doc.Id;
    }

    public virtual async Task<IReadOnlyList<SampleDoc>> ListAsync() =>
        await repo.AsQueryable().OrderBy(d => d.Id).ToListAsync();  // 全局过滤器自动按数据范围裁剪

    public virtual async Task<bool> RenameAsync(long id, string title)
    {
        var doc = await repo.GetByIdAsync(id);   // 越权/不存在 → null(同样经范围过滤)
        if (doc is null) return false;
        doc.Title = title;
        return await repo.UpdateAsync(doc) > 0;  // 仓储写路径守卫二次兜底,越权改删会被拒
    }

    public virtual async Task<bool> DeleteAsync(long id)
    {
        if (await repo.GetByIdAsync(id) is null) return false;
        return await repo.DeleteAsync(id) > 0;
    }
}
```

- **读**走 `AsQueryable()`,全局过滤器自动按当前请求的数据范围裁剪,业务代码不用手写 `WHERE`。
- **改/删先 `GetByIdAsync` 校验可见性**——看不到就当作"不存在/无权"处理;仓储对 `DataEntity` 的 `Update`/`Delete` 本身也内置了写路径范围守卫,双保险。
- 方法都是 `virtual`——消费方要重写某一步流程,继承后override 单个方法即可,不用整份复制。

::: tip 要不要缓存?
不是所有查询都要缓存。只有"高频读 + 低频变"的热点(比如某类下拉数据源)才值得加——参考 `backend/src/TenonAdmin.Services/Dict/DictService.cs` 里 `GetItemsByTypeAsync` 的读穿透缓存写法(`ICacheProvider` + 显式 `RemoveAsync` 失效)。管理端分页这种冷路径直接查库就够,`Dict`/`SampleDoc` 的分页/列表都没缓存。
:::

## 3. 控制器

`SampleDocController.cs`——每个动作挂 `[RolePermission]`,**权限码就是规范化后的路由本身**,不写任何权限字符串:

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

`GET /api/v1/sample/doc` 这个动作的权限码就是 `GET:/api/v1/sample/doc`——授权时管理员到菜单管理页把这条路由挂到某个按钮/菜单上,再到角色管理页把它勾给某个角色,该角色的用户就有权限了。超管(`sadm` 声明)自动绕过。

## 4. 注册服务 + 挂载程序集(路线 B 的关键一步)

消费方不改内核,而是在自己的 `Program.cs` 里做两件事——完整范本见 `backend/tests/TenonAdmin.TestHost/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    // 把自己的程序集交给内核:里面的实体加入 CodeFirst 建表,控制器 AddApplicationPart 挂载
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
});

// 自己的服务在 AddTenonAdmin() 之前/之后 TryAdd 均可(未被内核占用的接口)
builder.Services.TryAddScoped<ISampleDocService, SampleDocService>();

var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

::: warning 只有 `ApplicationAssemblies.Add(...)` 这条路生效
内核**不会自动扫描发现**你的模块。忘了这一行,`SampleDoc` 实体不会建表,`SampleDocController` 也不会注册路由,直接 404——没有兜底开关。
:::

::: tip 必须用 `TryAdd`,不是 `Add`
保持服务用 `TryAddScoped` 注册,消费方才能在 `AddTenonAdmin()` 之前注册同接口的自定义实现来覆盖默认行为——这是整个内核"可替换"设计的基础规则,内置服务(比如 `Dict`)在 `ServicesSetup.cs` 里也是这么注册的。
:::

## 5. 错误码(可选)

如果要精确区分"不存在"和其他失败,内核内置模块的做法是往 `Core/ErrorCode.cs` 枚举里加数字码,只加码不写文案(字典模块的 `DictTypeNotFound = 43001`、`DictTypeCodeExists = 43002` 就是例子),文案在前端按码翻译。消费方受限于 `ErrorCode` 不可扩展,可以像 `SampleDocService` 那样直接用返回值(`false` 表示不存在/无权)表达结果,或自定义异常经自己的异常过滤器兜底。

## 6. 种子数据(可选)

需要出厂默认数据时实现泛型 `ISeedData<T>`,**固定 Id 必须落在消费者保留区间 `[1000, 4095]`**(`TenonSeedIds.ConsumerMin` ~ `ConsumerMax`)。区间外播种,启动直接失败并告诉你该用哪段——内核自己的内置种子占 `[1, 999]`,雪花运行时发号从 `4096` 起,种子占了任一区间将来都会撞主键。

种子要注册在**你自己的 `Program.cs`**(内核不扫描程序集找种子,`ApplicationAssemblies` 只管实体建表和控制器挂载):

```csharp
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, YourSeed>());
```

## 7. 挂菜单、授权(让接口"可被访问")

权限码等于路由,授权靠在菜单树上勾路由,所以新接口要能被普通用户调通,得先有对应的菜单节点:

1. 启动系统,进**菜单管理**页,建菜单节点:`Type=菜单`、`Path` 填前端路由地址(如 `/sample/doc`)、`Component` 填对应 `.vue` 文件路径(不带前后缀)、`所属应用`选一个顶级目录。
2. 需要按钮级权限的话建 `Type=按钮` 节点,`Permission` 填对应路由码(如 `POST:/api/v1/sample/doc`)。
3. 进**角色管理**,给角色勾选该菜单/按钮——该角色下的用户即获得对应路由权限,授权变更即时生效(内核会失效对应缓存)。
4. 超管开发期不用配权,自动放行全部路由。

## 8. 测试

用 `WebApplicationFactory` 写 HTTP 级回归——蓝本 `backend/tests/TenonAdmin.Tests/` 下现有的测试文件:造用户/授菜单 → 带 token 调端点 → 断言信封。SQLite/MySQL 两条腿都要保持绿:

```bash
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~SampleDoc"
```

## 9. 前端接线

后端跑起来后,前端这边的完整步骤(重生成类型、封装 API、写 CRUD 页面、挂菜单)见下一篇:[前端加一个页面](/zh/tutorial/frontend-page)。

## 端到端清单

**后端**
- [ ] 实体(选 `BaseEntity`/`DataEntity`)+ Sugar 特性
- [ ] `I*Service` + `*Service`(方法 `virtual`,改/删先校验可见性)
- [ ] 消费方 `Program.cs`:`ApplicationAssemblies.Add(...)` + `TryAddScoped` 注册服务
- [ ] 控制器(`[ApiController]`/`[Route]`,每个动作 `[RolePermission]`)
- [ ] 错误码(可选)
- [ ] 种子(可选,固定 Id 落在 `[1000, 4095]`)
- [ ] 测试(`WebApplicationFactory`,SQLite/MySQL 双绿)

**配置权限(运行时)**
- [ ] 菜单管理建节点(`Path`/`Component` 对应前端路由与文件)
- [ ] 角色管理勾选授权

> 更完整的规范细则(DTO 分页写法、缓存决策、`SysSchemaVersion` bump 时机等)见[业务模块开发指南:A. 后端](/zh/guide/new-business/backend)。
