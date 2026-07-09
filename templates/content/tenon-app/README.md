# TenonApp

基于 **TenonAdmin** 内核的后台 host,由 `dotnet new tenon-app` 生成。已接线一个机构隔离示例业务模块(`Modules/SampleDoc*`)。

## 运行

```bash
dotnet run
```

- 零配置默认用 SQLite(相对路径落在 ContentRoot),自动建表 + 种子。
- **首次启动控制台会打印随机超管密码**,用它登录。
- 换数据库:在 `appsettings.json` 加 `TenonAdmin:Database` 节(DbType + 连接串),支持 SQLite / MySQL / SqlServer / PostgreSQL。
- 健康检查 `/health`、`/health/ready`;开发期 OpenAPI 契约 `/openapi/v1.json`。

## 加一个业务模块

复制 `Modules/` 下的四件套改名即可(示例 `SampleDoc` 是机构隔离表,继承 `DataEntity`;不需机构隔离的表改继承 `BaseEntity`):

1. `{实体}.cs` —— `[SugarTable]` 实体。
2. `I{实体}Service.cs` + `{实体}Service.cs` —— 业务服务(方法 `virtual`,构造注入 `IRepository<{实体}>`)。
3. `{实体}Controller.cs` —— `[ApiController]` + `[Route]`,每个 action 挂 `[RolePermission]`,返回 `Result<T>`。
4. 在 `Program.cs` 追加一行 `builder.Services.TryAddScoped<I{实体}Service, {实体}Service>();`。

实体在本程序集内,`AddTenonAdmin(..., o => o.ApplicationAssemblies.Add(...))` 已登记 → 自动建表、控制器自动挂路由。**权限码 = 规范化路由**(如 `GET:/api/v1/sample/doc`),普通用户经角色-菜单授权;超管放行。
