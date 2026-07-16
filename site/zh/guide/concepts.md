# 核心概念

TenonAdmin 是一个**可分发的后台内核**,不是一个应用。它以 NuGet 包的形式交付,让消费方用三行 `Program.cs` 就得到一整套企业级后台(认证、RBAC、多组织数据权限、字典/配置、日志、上传)。这套底座还不止这些:通知公告的定向投递与富通知铃铛、只读演示模式(写请求一律拒)、密码过期策略等成套能力也一并内建,细节各自在后端深读篇展开。贯穿始终的设计约束是**可替换性**。

## 为什么不是又一个后台模板

直接复制一套后台模板上手很快,但随着业务代码增长,项目会和模板深度耦合——之后想升级基础能力、同步上游改动,或只替换其中一部分,都会变得很麻烦。

TenonAdmin 把这些通用功能从具体业务里拆出来:既能直接用默认实现,也能比较自然地接入已有项目,还能在不 fork 的前提下替换任意一环。

## 可替换性模型

这是整个内核的重点,体现为三条约束(由 `ReplaceabilityTests`「六件套」测试锁定):

1. **接口注册 + `TryAdd`**——内置服务都用 `TryAdd*` 注册,消费方在 `AddTenonAdmin()` 之前注册同一个接口即可胜出,覆盖默认实现。
2. **模板方法拆分**——长服务方法被拆成若干 `virtual` 小步骤,消费方通过继承重写**其中一步**,而不是整段复制。
3. **业务程序集挂载**——消费方的实体经 `options.ApplicationAssemblies` 加入 CodeFirst 建表,控制器自动 `AddApplicationPart`,不改内核就能扩展。

## 包分层

依赖只能自上而下,这个次序本身是承重约束:

```text
TenonAdmin.Core        纯契约:接口、Options、Result<T>、ErrorCode。无 SqlSugar、无 ASP.NET。
   ↑
TenonAdmin.SqlSugar    数据层:ISqlSugarClient 单例、IRepository<>、实体基类、CodeFirst、种子。
   ↑
TenonAdmin.Services    领域层:实体(Sys*)、服务实现、RBAC / 数据范围、事件总线。
   ↑
TenonAdmin.AspNetCore  宿主集成:AddTenonAdmin / MapTenonAdmin、JWT、权限/会话过滤器、内置控制器。

TenonAdmin             元包:只引用 AspNetCore,消费方装它一个即可拉起整条栈。
```

## 请求流水线

一个已认证请求依次流经:

1. **认证**——Microsoft JWT Bearer,框架 401 被重塑成标准信封(code 40006)。
2. **`[RolePermission]`**——权限码就是规范化路由(`{METHOD}:/{route}`),**代码里没有权限字符串**,授权靠在角色菜单 UI 里勾路由。超管(`sadm`)直接放行,同时校验会话是否仍有效(强制下线立即生效)。
3. **数据范围**——授权阶段解析出当前用户的有效机构数据范围,注入 `IDataScopeContext`。
4. **结果信封**——控制器可直接 `return dto`,过滤器统一包成 `Result<T>`;业务错误抛 `AdminException` / 返回 `ErrorCode`,转成信封。**错误是数字 `ErrorCode`,从不下发本地化文案**,i18n 由前端按码翻译。

## 数据层约定

- 一个 `SqlSugarScope` 单例;全局查询过滤器自动做**软删除**(`ISoftDelete`)和**数据范围**(`IOrgScoped` / `DataEntity` 按当前请求解析的机构集过滤)——数据范围过滤是招牌特性。
- AOP 在插入/更新时自动填审计字段:雪花 `Id`、`CreateTime`、`CreateUserId`、`CreateOrgId`(数据范围锚点)、`UpdateTime`、`UpdateUserId`。业务代码只管业务字段。
- 雪花 `WorkerId` 来自 `TenonAdmin:Id:WorkerId`(默认 0),**水平扩展时每实例必须不同**,否则同毫秒发号会撞主键。

---

> 更完整的架构与设计背景见仓库的 [架构与设计文档](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/rebuild-design.md)。
