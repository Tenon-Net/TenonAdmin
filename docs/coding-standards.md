# TenonAdmin 代码规范

> 面向后续开发的落地规范。规则均从现有代码提炼，每条尽量给出参照文件，照抄即合规。
> 配套文档：新建业务见 [`new-business-guide.md`](./new-business-guide.md)。

---

## 0. 总则

1. **可替换性优先**：内核以 NuGet 分发，消费方不改源码即可替换任一部件。凡新增可替换服务，一律 `TryAdd*` 注册、接口背书、方法拆 `virtual` 步。这是硬约束，不是建议。
2. **注释讲“为什么”**：解释边界、权衡、坑，而非复述代码。公共类型/成员必须有 XML 文档注释。设计取舍标注设计文档节号（`§6`、`T3`）。
3. **中文注释**：代码、注释、文档统一中文，与既有代码一致。
4. **错误是数字码**：后端永不返回本地化文案，只返回 `ErrorCode`；文案在前端按码翻译。
5. **刻意简化留痕**：临时/有上限的简化用 `// ponytail:` 注释标注上限与升级路径（例：`SessionService.cs:24` 的进程内锁）。

---

## 1. 后端规范（.NET 10 内核）

### 1.1 分层与依赖方向

依赖只能向下，越层禁止。新增代码先想清楚落在哪层：

```
Core        契约层：接口(I*Provider/I*Service 面)、Options、Result<T>、ErrorCode、AdminException。无 SqlSugar、无 ASP.NET。
  ↑
SqlSugar    数据层：ISqlSugarClient 单例、IRepository<>、实体基类、CodeFirst 初始化、种子运行器。
  ↑
Services    领域层：实体(Sys*)、*Service 实现、RBAC/数据范围 Provider、事件总线。★实体放这里，不放 SqlSugar 层。
  ↑
AspNetCore  宿主集成：AddTenonAdmin/MapTenonAdmin、JWT、过滤器、内置控制器。
  ↑
TenonAdmin  元包：只引用 AspNetCore，消费方装它即拉全栈。
```

- 运行时依赖**仅** SqlSugarCore + Microsoft.\*，核心包不得引入其它第三方框架。
- 每层装配是一个 `*Setup.cs` 扩展方法：`SqlSugarSetup` → `ServicesSetup` → `TenonAdminSetup`（组合根，`AddTenonAdmin` 逐层向下调）。

### 1.2 可替换性三件套（`ReplaceabilityTests` 锁定的契约）

| 手段 | 做法 | 参照 |
|---|---|---|
| **TryAdd 注册** | 内置服务全部 `TryAdd*`；消费方在 `AddTenonAdmin()` 之前注册同接口即胜出。**严禁**对可替换服务用裸 `Add*`。 | `ServicesSetup.cs`、`SqlSugarSetup.cs` |
| **virtual 模板方法** | 长方法拆成小 `virtual` 步，消费方覆写一步而非抄整方法。 | `SessionService.EnforceConcurrencyAsync`、`RbacPermissionProvider.LoadFromDatabaseAsync` |
| **接口背书** | 每个服务先有 `I*Service`，实现类 `virtual`。 | 全部 `Services/*/I*.cs` |
| **消费方装配** | 业务程序集经 `options.ApplicationAssemblies` 并入：实体参与建表、控制器 `AddApplicationPart`。改实体扫描/控制器注册时**务必保留此路径**。 | `TenonAdminSetup.cs:60-62,120-121` |

### 1.3 实体规范

- **位置**：实体定义在 `Services/Entities/`，不在 SqlSugar 层。命名 `Sys*`（系统内核表）。
- **基类**：
  - `BaseEntity`（`SqlSugar/Entities/BaseEntity.cs`）：主键 + 审计四件套（CreateTime/CreateUserId/UpdateTime/UpdateUserId）+ 软删 `IsDelete`。这些字段由 AOP 自动填，业务代码零感知。
  - `DataEntity`（带机构数据范围的业务表继承它，含 `CreateOrgId` 锚点）——需要按机构做数据隔离时用它。
- **SqlSugar 特性**：`[SugarTable("表名", TableDescription=…)]`、唯一索引 `[SugarIndex(..., IsUnique=true)]`、列 `[SugarColumn(Length=…, ColumnDescription=…, IsNullable=…)]`。参照 `Entities/SysDictType.cs`。
- **不可变约定写进注释**：如“Code 创建后不可变”，并在 Service 的 Update 里落实（不改该字段）。

### 1.4 服务规范

- 一服务一目录：`I{X}Service.cs` + `{X}Service.cs` + `{X}Models.cs`（DTO：`{X}Input`/`{X}PageInput`/`{X}Output`，用 `record`）。
- 实现类构造函数注入依赖（主构造函数语法），方法 `virtual`，`async` 后缀 `Async`。
- 分页统一 `PagedList<T>` + `.ToPagedListAsync(current, size)`（`SqlSugar/Paging/`）。
- 校验用 `AdminException.ThrowIf(条件, ErrorCode.X)`。

### 1.5 错误处理

- 业务错误抛 `AdminException(ErrorCode)` 或返回 `ErrorCode`；由 `AdminExceptionFilter` 统一转信封。
- **`ErrorCode` 是数字枚举，永不带本地化文案**（`Core/ErrorCode.cs`）。新增错误码往枚举里加。
- 控制器可直接 `return dto`，`ResultEnvelopeFilter` 兜底包 `Result<T>`；内置控制器为了 OpenAPI 契约清晰，显式返回 `Result<T>.Ok(...)`。

### 1.6 控制器规范

参照 `Controllers/DictController.cs`：

```csharp
[ApiController]
[Route("api/v1/sys/dict")]
[Module("Dict")]                       // 可经 Api:DisabledModules 关停整模块
public class DictController(IDictService svc) : ControllerBase
{
    [HttpGet("type/page")]
    [RolePermission]                   // ★权限码 = 规范化路由，无字符串
    public async Task<Result<PagedList<SysDictType>>> PageTypes([FromQuery] DictTypePageInput input) =>
        Result<PagedList<SysDictType>>.Ok(await svc.PageTypesAsync(input));
}
```

- **`[RolePermission]` 无参**：权限码就是 `{METHOD}:/{路由模板}`（如 `GET:/api/v1/sys/dict/type/page`），在角色-菜单界面勾路由即配权。**代码里永远不写 `"sys:user:add"` 之类魔法串**（`Security/RolePermissionAttribute.cs`）。超管 `sadm` claim 直接放行。
- **`[ActiveSession]`**：任意已登录用户可访问、但无需特定权限的端点用它。
- **`[OperationLog(...)]`**：需要审计的写操作挂它，`OperationLogFilter` 记录。
- **`[Module("X")]`**：模块化开关，可被配置摘除。
- 匿名端点显式 `[AllowAnonymous]`（登录/刷新/验证码）。默认拒绝：`MapControllers().RequireAuthorization()` 全局兜底，漏挂 `[RolePermission]` 也不会静默公开。

### 1.7 数据访问

- 注入 `IRepository<T>`；复杂查询走 `.AsQueryable()`，需逃生时走 `.Db`（如 `Db.Deleteable<>()` 物理删关联行、`Db.Ado.UseTranAsync`）。
- **全局过滤器**（`SqlSugarSetup.cs:61-72`，业务代码无需重复写）：
  - 软删：`ISoftDelete` 实体自动 `IsDelete == false`。查已删数据显式 `.ClearFilter<ISoftDelete>()`。
  - **数据范围**（招牌能力 §6）：`IOrgScoped`/`DataEntity` 按当前请求生效机构集过滤。
- **唯一性查重要带上软删行**：`.ClearFilter<ISoftDelete>().AnyAsync(...)`，否则撞库唯一索引抛原生 500（见 `DictService.AddTypeAsync`、`ConfigService.AddAsync`）。
- **多写操作包事务**：`Db.Ado.UseTranAsync`，失败整体回滚；**缓存失效放在事务提交之后**（`RbacService.ReplaceAsync`、`SessionService.OpenAsync`）。
- 审计字段（Id 雪花、CreateTime/User/Org、UpdateTime/User）由 AOP 自动填（`SqlSugarSetup.cs:75-104`），业务只设业务字段。`CreateOrgId` 不填则机构维度数据范围对业务表恒 0 行——不要手动绕过 AOP。
- 雪花 `WorkerId` 来自 `TenonAdmin:Id:WorkerId`，**多实例必须各配不同值**。

### 1.8 缓存规范（性能核心，务必遵守）

系统采用 **读穿透 / cache-aside + 显式失效** 模型，不是每次查库。新增热读路径按此模板：

```csharp
public virtual async Task<T> GetHotAsync(string k)
{
    var key = CacheKeys.Xxx(k);               // ①逻辑键集中定义
    var cached = await cache.GetAsync<T>(key); // ②命中即返回
    if (cached is not null) return cached;
    var v = await LoadFromDb(k);               // ③未命中查库
    var ttl = cacheOptions.PermissionMinutes > 0 ? TimeSpan.FromMinutes(cacheOptions.PermissionMinutes) : (TimeSpan?)null;
    await cache.SetAsync(key, v, ttl);         // ④回填（TTL 仅兜底，主靠显式失效）
    return v;
}
// 任何增删改后：await cache.RemoveAsync(CacheKeys.Xxx(k));  ⑤显式失效
```

- **键集中在 `Core/CacheKeys.cs`，禁散落魔法串**。前缀 `Cache:KeyPrefix`（默认 `tenon:`）由 provider 统一追加。
- **缓存值的空集合 ≠ 未缓存**（`ICacheProvider.GetAsync` 返回 `default`），无权限用户也只查一次库。
- 变更时**既失效缓存也广播事件**（`DictService.InvalidateAsync` → `DictChangedEvent`），供跨节点失效/审计/推送订阅。
- 一次性票据用 `GetAndRemoveAsync`（验证码），并发计数用 `IncrementAsync`（登录失败）——`MemoryCacheProvider` 用进程内锁保原子，`RedisCacheProvider` 用原生 `GETDEL`/`INCR`+`EXPIRE`。
- 默认 `MemoryCacheProvider`（进程内）；多实例共享装 **`TenonAdmin.Caching.Redis`** 可选包（基于 StackExchange.Redis），**业务代码零改动**：

  ```csharp
  builder.Services.AddTenonAdminRedisCache(builder.Configuration); // ★须在 AddTenonAdmin 之前,赢 TryAdd
  builder.Services.AddTenonAdmin(builder.Configuration);
  ```
  ```jsonc
  "TenonAdmin": { "Cache": { "Provider": "Redis", "RedisConnectionString": "127.0.0.1:6379", "KeyPrefix": "tenon:" } }
  ```
  `Provider≠Redis` 时 `AddTenonAdminRedisCache` 空操作(留 Memory 默认)。缓存落 Redis 后，现有“变更即 `RemoveAsync` 失效”天然跨实例生效。值走 System.Text.Json 序列化——新增缓存的类型须可序列化(record/POCO，参照 `DataScopeResult`)。

已缓存的热读：用户权限码、用户数据范围、会话活跃态、字典项、系统配置。参照 `RbacPermissionProvider`、`DataScopeProvider`、`SessionService`、`DictService`、`ConfigService`。

### 1.9 DI 装配

- 装配写进 `*Setup.cs` 扩展方法。内置服务**显式** `TryAdd`（不靠扫描，可预测、可替换）；种子用 `TryAddEnumerable`（按实现类型防重）。
- 无状态服务 `Singleton`（哈希、验证码生成器、文件存储、缓存 provider、事件总线）；按请求 `Scoped`（多数业务服务，与仓储一致）。
- 消费方业务程序集经 `options.ApplicationAssemblies` 并入（实体建表 + 控制器挂载）。

### 1.10 种子数据

- 实现 `ISeedData<TEntity>`，`HasData()` 返回默认行（`Seed/DictSeed.cs`）。
- **固定小整数 Id 保幂等**：种子只在缺失时补，不回改已存在行——界面上的改动不会被重启覆盖。
- 在 `ServicesSetup` 用 `TryAddEnumerable` 注册。

### 1.11 命名 / 组织 / 其它

- 命名空间随目录；一类型一文件；`Sys*` 实体、`I*` 接口、`*Service`/`*Provider`/`*Filter`/`*Attribute` 后缀。
- 启用可空引用类型；`async` 方法带 `Async` 后缀并接受 `CancellationToken`（热路径）。
- 时间统一走注入的 `TimeProvider`（可测试），不用 `DateTime.Now` 裸调。

### 1.12 包管理

- 版本**集中管理**：增/改依赖在 `backend/Directory.Packages.props` 的 `<PackageVersion>`，**不在各 `.csproj` 写版本号**。
- 共享构建/NuGet 元数据在 `backend/Directory.Build.props`。

### 1.13 注释规范（后端注释率现 ~29%，保持）

- 公共类型/成员：`/// <summary>` 说清职责与边界；关键取舍引 `§N`/`TN`。
- 行内注释解释 WHY（并发、事务顺序、边界、跨方言坑），不复述 WHAT。
- 简化/有上限处用 `// ponytail:` 注明上限与升级路径。

---

## 2. 前端规范（Vue 3 + Naive UI）

### 2.1 技术栈与目录

`<script setup>` + Naive UI + Pinia(持久化) + vue-router + vue-i18n + VueUse。路径别名 `@` → `src`。

| 目录 | 职责 |
|---|---|
| `views/` | 页面（按模块/实体分子目录，`views/<模块>/<实体>/index.vue`） |
| `composables/` | 与 UI 库无关的逻辑单源（`use*`），Naive 消息留在视图层 |
| `stores/` | Pinia 状态 |
| `layouts/` | 布局壳（顶栏/侧栏/标签/设置） |
| `components/` | 可复用组件 |
| `api/` | `client.ts`(openapi-fetch) + `index.ts`(按域分组) + 生成的 `schema.d.ts` |
| `router/` | 静态路由 + 动态路由重建 |
| `theme/`、`styles/` | 主题令牌 |
| `locales/` | i18n |
| `directives/` | `v-auth` 等 |
| `types/` | 手写类型（`menu.ts`）与再导出 |

### 2.2 API 契约流

- **`schema.d.ts` 由后端 OpenAPI 生成**（`npm run gen:api`，后端需运行），**禁止手改**，改了重新生成。
- `api/client.ts` 是 `openapi-fetch` 针对 schema 的类型化封装。
- `api/index.ts` 按域分组导出（`authApi`/`personalApi`/`userApi`/`moduleApi`/`menuApi`…），每个方法 `client.X(...).then(r => unwrap<T>(r))`。
- **`unwrap`** 统一解信封：2xx 的 `Result<T>`（code≠0 抛 `ApiError`）、非 2xx 的信封/ProblemDetails 都归一到 `ApiError`（带 `code`/`msgKey`）。视图 `catch` 后 `translateError(e)` 展示。参照 `api/index.ts`。
- 分页返回在 api 层归一为 `{ items, total }` 以适配 `useTable`（后端是 `PagedList<T>{current,size,total,items}`）。查询参数名用 PascalCase（ASP.NET 绑定要求）。

### 2.3 路由（静态 + 动态菜单注入）

- `router/routes.ts` 只放静态路由（login、error、shell/layout）。真实菜单树登录后从后端拉取，注入为**动态路由**（只活在内存）。
- **组件解析**（`composables/useAuthMenu.ts`）：`import.meta.glob('/src/views/**/*.vue')` 收集全部页面；菜单节点的 `component` 串（如 `system/user/index`）→ `/src/views/system/user/index.vue`。路由 `path` 取菜单 `path`，`name = menu-${id}`，挂在 `layout` 下。
- `namedPage`（`router/namedPage.ts`）包一层使“组件名===路由名”，供 keep-alive 的 `:include` 匹配。
- **F5/深链**：动态路由丢失，守卫（`router/index.ts`）在 `routesReady=false` 时调 `useModule().enterInitial()` 重建后重解析当前 URL。**不要持久化 `routesReady`/`menuTree`**（会跳过重建导致 404）。
- 登出/切应用用 `registerDynamic`/`resetRouter` 精确增删动态路由。

### 2.4 状态（Pinia）

- `defineStore` + `actions`；**按需持久化** `persist: { pick: [...] }`（`auth` 只存 `currentModuleId`，见 `stores/auth.ts` 顶部长注释解释为何其余不持久化）。
- 登出走 `reset()` 清授权态并清标签。
- 现有 store：`auth`(模块/菜单/权限码/routesReady)、`user`(令牌/登录态)、`app`(主题/偏好)、`tabs`(标签)。

### 2.5 组合式函数

- `use*` 命名，返回响应式引用与方法；**与 Naive 无关**（错误/消息回调由视图注入，见 `useTable` 的 `onError`）。
- 列表页统一用 `composables/useTable.ts`：传 `fetcher(({page,pageSize,...params})=>Promise<{items,total}>)`，得 `loading/rows/pagination/load/search/onPage/onPageSize`。

### 2.6 按钮级权限

- `v-auth`（`directives/auth.ts`）：`v-auth="'POST:/api/v1/sys/user'"`（单码）/ 数组（默认 OR）/ `.and`（AND）；不命中移除 DOM。
- ⚠ 现状 **fail-open**：后端暂无“返回按钮权限码”接口，`permissionCodes` 恒空 → 指令不隐藏，强制靠服务端 403。补 `/personal/permissions` 后自动生效（详见审查报告的待办项）。

### 2.7 i18n

- 文案按 code 翻译：后端只给 `code`/`msgKey`，前端 `translateError` + `locales/zh-CN.ts`/`en-US.ts` 出文案。
- 视图内所有可见文本走 `t('...')`，禁硬编码。

### 2.8 主题

- 令牌在 `styles/tokens.css` + `theme/`；Naive 主题 `theme/naive-theme.ts`。
- 首访跟随系统深浅（VueUse `usePreferredDark`），手动切换后由持久化接管。

### 2.9 组件 / 视图

- `<script setup lang="ts">`；表格列用 `h()` 渲染函数（`views/system/menu/index.vue` 是完整 CRUD 范例：`NDataTable` + `NModal` 表单 + `NPopconfirm`）。
- 样式 `scoped`，用 CSS 变量（`var(--gap-card)` 等），不写死颜色/间距。

### 2.10 提交前检查

```bash
npm run lint        # oxlint（lint:fix 自动修）
npm run typecheck   # vue-tsc --noEmit
npm run build       # 类型检查 + 构建
```

### 2.11 注释规范（前端现状偏低，建议补齐）

- 导出的 store/composable/指令加块注释说明用途与边界（现有 `auth.ts`/`useModule.ts`/`namedPage.ts` 是好范例）。
- `.vue` `<script setup>` 里复杂逻辑（树运算、成环校验、分页归一）加行内注释讲 WHY。
- 现前端注释率明显低于后端（`.ts` ~8%、`.vue` ~2%），若要对齐后端密度，重点补 `views/` 下的脚本块。

---

## 附：目录速查

| 你要改… | 后端 | 前端 |
|---|---|---|
| 加实体/表 | `Services/Entities/` | — |
| 加接口/业务 | `Services/<域>/` + `ServicesSetup.cs` 注册 | `api/index.ts` |
| 加端点 | `AspNetCore/Controllers/` | — |
| 加页面 | — | `views/<模块>/<实体>/index.vue` + 菜单管理挂载 |
| 加缓存 | `Core/CacheKeys.cs` + 服务内 cache-aside | — |
| 加错误码 | `Core/ErrorCode.cs` | `locales/*` 加文案 |
| 加依赖版本 | `Directory.Packages.props` | `package.json` |
