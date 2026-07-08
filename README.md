# TenonAdmin(榫卯 Admin)

**一个 NuGet 包、三行代码,启动一套企业级后台管理系统内核。**

基于 ASP.NET Core + SqlSugar,开箱即用地提供登录认证、RBAC 权限、多机构数据权限和 Vue 管理端模板 ——
而且每一项能力都能被你替换或覆写。

> 核心运行时除 SqlSugar 外**不绑定任何第三方框架**;零配置即可跑,全配置皆可换。

## 为什么用它

- **三行接入** —— 不用搭脚手架、不用抄样板,`AddTenonAdmin()` + `MapTenonAdmin()` 即得完整后台。
- **零配置起步** —— 默认 SQLite、自动建表、首启生成随机超管密码,`dotnet run` 直接能登录。
- **想换就换** —— 服务面向接口、方法 `virtual`、长流程拆小步。你可以只重写其中一步,而不必复制整个方法;配置、服务实现、端点都可覆写。
- **不绑架技术栈** —— 除 SqlSugar 外只用 .NET/BCL 内置件;Redis、MQTT、国密等都隔离在可选包,按需引入。
- **多数据库** —— 官方支持 SQLite / MySQL / SqlServer / PostgreSQL。

## 快速开始

```bash
dotnet run --project backend/samples/MinimalHost
```

启动后会自动建库、建表、写入种子数据(**首次启动会在控制台打印随机超管密码,请留意**),然后:

```bash
curl http://localhost:5xxx/health
# {"status":"ok","app":"TenonAdmin"}
```

在你自己的项目里,`Program.cs` 只需三行:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration);   // 1. 注册
var app = builder.Build();
app.MapTenonAdmin();                                     // 2. 挂载端点
app.Run();
```

## 功能一览

| 模块 | 能力 |
|---|---|
| 认证 | 账密登录 + 图形验证码、JWT + Refresh Token、登录锁定、在线用户列表 / 强退 |
| RBAC | 角色、菜单(目录 / 页面 / 按钮三级)、按钮级权限码、角色-菜单授权 |
| 多应用门户 | 模块管理、菜单按应用分区、登录后选 / 切应用、每用户默认应用 |
| 数据权限 | 多机构数据范围(全部 / 本机构 / 本机构及以下 / 仅本人 / 自定义),接口级可配 |
| 组织 | 用户、机构(树)、职位;用户多角色、主属机构 |
| 字典 / 配置 | 字典类型与字典项、键值系统配置(带缓存) |
| 日志 | 操作日志(自动记录)、登录日志(IP / UA / 结果) |
| 文件 | 本地上传 / 下载 / 列表,大小限制、后缀白名单、路径穿越防护 |
| 个人中心 | 改密码、改资料、头像 |

前端为 Vue 3 + Naive UI 管理端模板(逻辑与视图分离)。

## 想深入定制?

四层覆写能力,按侵入程度递增,总有一层适合你:

1. **配置覆写** —— 改 `appsettings.json` 的 `TenonAdmin` 节。
2. **服务替换** —— 用你的实现替换任意内置服务接口。
3. **继承覆写** —— 继承默认实现,只重写模板方法里的某一步。
4. **端点覆写** —— 替换或扩展默认路由。

同时支持实体扩展与自定义业务模块。详见设计文档 §5。

## 支持的数据库

SQLite(默认,零配置)· MySQL · SqlServer · PostgreSQL。切换只需改连接字符串与数据库类型配置。

## 项目状态

当前处于 **M1 开发阶段**,认证最小闭环已跑通(三行启动 + 零配置 SQLite + CodeFirst 建表 + JWT 登录 + `[RolePermission]` 授权管道)。RBAC / 组织 / 数据权限等按计划推进中,开发在 `dev` 分支。

尚未发布到 nuget.org —— 现阶段请从源码运行样例体验。

## 仓库布局

仓库分两半:`backend/`(.NET 内核,即产品本体)与 `web/`(Vue 管理端模板)。`backend/` 下采用 .NET 标准约定:

| 路径 | 是什么 |
|---|---|
| `backend/src/` | **产品本体** —— 待发布为 NuGet 的 5 个包(`Core → SqlSugar → Services → AspNetCore` 分层 + `TenonAdmin` 元包) |
| `backend/samples/` | 示例消费方(`MinimalHost`,演示三行启动),供抄用,不发布 |
| `backend/tests/` | 测试项目,不发布 |
| `backend/artifacts/` | `dotnet pack` 的产物输出,构建生成、已被 gitignore,不进版本库 |
| `backend/Directory.*.props` | 全仓共享构建配置 + 集中版本管理(增删依赖改这里,不动各 `.csproj`) |

> 内核消费者只 `dotnet add package TenonAdmin` 拿编译产物,无需关心以上布局;此表面向会 clone 本仓库的开发者。

## 文档

- 设计方案:[`docs/rebuild-design.md`](docs/rebuild-design.md)
- 开发进度与任务队列:[`docs/dev-plan.md`](docs/dev-plan.md)

## 许可证

Apache-2.0。
