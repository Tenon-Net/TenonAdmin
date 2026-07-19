# 核心概念

三行 `Program.cs` 换来一整套企业级后台：认证、RBAC、多组织数据权限、字典与配置、日志、上传、通知公告、只读演示模式、密码过期策略。它以 NuGet 包交付，是**内核**不是应用，业务代码住在你自己的仓库里。这个形态逼出一条约束：里面任何一环，你都得能换掉。

## 为什么不是又一个后台模板

复制一套后台模板，上手很快。但业务代码一多，项目就和模板深度耦合了。以后想升级底层能力、同步上游改动、或者只替换其中一块，每件都很麻烦。

TenonAdmin 把这些通用功能从业务里拆了出来。你可以直接用默认实现，也可以把它接进已有项目，还可以不 fork 就替换掉任意一环。

## 可替换性模型

它落成三条约束，由 `ReplaceabilityTests`「六件套」测试锁定：

1. **接口注册 + `TryAdd`**：内置服务都用 `TryAdd*` 注册。消费方只要在 `AddTenonAdmin()` 之前注册同一个接口，就能胜出，覆盖掉默认实现。
2. **模板方法拆分**：长方法被拆成若干 `virtual` 小步骤。消费方继承之后只重写**其中一步**，不用整段复制。
3. **业务程序集挂载**：消费方的实体经 `options.ApplicationAssemblies` 加入 CodeFirst 建表，控制器也自动 `AddApplicationPart`。不改内核就能扩展。

## 包分层

依赖只能自上而下，这个次序在整套设计里承重：

```text
TenonAdmin.Core        纯契约:接口、Options、Result<T>、ErrorCode。无 SqlSugar、无 ASP.NET。
   ↑
TenonAdmin.SqlSugar    数据层:ISqlSugarClient 单例、IRepository<>、实体基类、CodeFirst、种子。
   ↑
TenonAdmin.Services    领域层:实体(Sys*)、服务实现、RBAC / 数据范围。
   ↑
TenonAdmin.AspNetCore  宿主集成:AddTenonAdmin / MapTenonAdmin、JWT、权限/会话过滤器、内置控制器。

TenonAdmin             元包:只引用 AspNetCore,消费方装它一个即可拉起整条栈。
```

## 请求流水线

一个已认证请求依次流经：

1. **认证**：Microsoft JWT Bearer，框架 401 被重塑成标准信封（code 40006）。
2. **`[RolePermission]`**：权限码就是规范化路由（`{METHOD}:/{route}`）。**代码里没有权限字符串**，授权靠在角色菜单 UI 里勾路由。超管（`sadm`）直接放行。它还会校验会话是否仍有效，所以强制下线立即生效。
3. **数据范围**：授权阶段解析出当前用户的有效机构数据范围，注入 `IDataScopeContext`。
4. **结果信封**：控制器可以直接 `return dto`，过滤器统一包成 `Result<T>`。业务错误则抛 `AdminException` 或返回 `ErrorCode`，同样转成信封。**错误是数字 `ErrorCode`，从不下发本地化文案**，i18n 交给前端按码翻译。

## 数据层约定

- 一个 `SqlSugarScope` 单例。全局查询过滤器自动做**软删除**（`ISoftDelete`）和**数据范围**（`IOrgScoped` / `DataEntity` 按当前请求解析的机构集过滤）。
- AOP 在插入/更新时自动填审计字段：雪花 `Id`、`CreateTime`、`CreateUserId`、`CreateOrgId`、`UpdateTime`、`UpdateUserId`。其中 `CreateOrgId` 是数据范围的锚点。业务代码只管业务字段。
- 雪花 `WorkerId` 来自 `TenonAdmin:Id:WorkerId`（默认 0），**水平扩展时每实例必须不同**，否则同毫秒发号会撞主键。

---

> 更完整的架构与设计背景见仓库的 [架构与设计文档](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/rebuild-design.md)。
