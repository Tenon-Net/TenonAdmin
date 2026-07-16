# AGENTS.md —— 给 AI 编码助手的项目指引

本项目是基于 **TenonAdmin** 内核(NuGet 包)的后台 host,由 `dotnet new tenon-app` 生成。全部后台基础能力(认证 / RBAC / 多机构数据权限 / 字典 / 配置 / 日志 / 上传)来自内核包,**本项目只写业务模块**。

## 新增业务模块(标准流程)

参照 `Modules/` 下的 SampleDoc 四件套(实体 / IService / Service / Controller),复制改名:

1. **实体**:机构数据隔离继承 `DataEntity`,全局表继承 `BaseEntity`;`[SugarTable("biz_xxx")]`,唯一字段加 `[SugarIndex]`。审计字段(Id/CreateTime/CreateOrgId 等)由 AOP 自动填充,**不要重复定义**。启动时 CodeFirst 自动建表,没有迁移脚本。
2. **Service**:主构造注入 `IRepository<T>`;方法一律 `virtual`;业务错误抛 `AdminException((ErrorCode)自选数字)`——构造只收内核 `ErrorCode` 类型,自选数字**从 60000 起步**强转传入,集中成常量类防散落;前端语言包加 `error.code.<数字>` 键即可翻译。唯一性查重用 `.ClearFilter<ISoftDelete>()` 把软删行纳入,否则撞库上唯一索引。
3. **Controller**:`[ApiController]` + `[Route("api/v1/biz/xxx")]`;每个 action 挂 `[RolePermission]`;写操作加 `[OperationLog("描述")]`;返回 `Result<T>.Ok(...)`(裸返回也会被信封过滤器包上)。
4. **注册**:`Program.cs` 追加一行 `builder.Services.TryAddScoped<IXxxService, XxxService>();`。`ApplicationAssemblies` 已挂本程序集 → 实体自动建表、控制器自动挂路由;**缺这一挂,表不建、接口 404**。

## 铁律(违反即坏)

- **权限码 = 规范化路由**(如 `POST:/api/v1/biz/product/add`,路径参数保留 `{id}` 占位)。代码里没有权限字符串——授权在后台「角色管理 → 授权菜单」按路由勾选;超管绕过。
- **菜单/页面在后台「菜单管理」UI 添加**,不写路由代码;`component` 填前端 `views/` 相对路径。
- **种子数据**(可选预置):实现泛型 `ISeedData<T>`(`HasData()` 返回带固定 Id 的行),固定 Id 必须落在消费者保留区间 **[1000, 4095]**(`TenonSeedIds.ConsumerMin/ConsumerMax`;[1,999] 归内核)。越界或撞号会被启动检查当场拒绝。在 `Program.cs` 用 `TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, XxxSeed>())` 注册——内核不扫描程序集找种子,忘注册＝静默不执行。
- **错误只返回数字码**,不返回文案;前端按 msgKey 做 i18n。
- 想替换内核内置行为:在 `AddTenonAdmin()` **之前**注册同接口实现(内核全部 `TryAdd`,你的注册优先),或继承服务类覆写单个 `virtual` 步骤。不要 fork 内核。
- 配置都在 `appsettings.json` 的 `TenonAdmin` 节;生产必配 `Jwt:SecretKey`;横向扩容时每实例 `Id:WorkerId` 必须不同,否则雪花 Id 冲突。

## 详版指南

- 内核仓库 `skills/`(新模块全流程 / 建实体 / 后端 CRUD / 前端页面 / 替换服务):https://github.com/Tenon-Net/TenonAdmin/tree/main/skills
- 文档站:https://tenon.52moyu.net/
