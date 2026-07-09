# SimpleAdmin 重构设计方案 —— 开源企业级小型管理系统

> 状态:设计稿 v3(2026-07,并入 `补充.txt`/`补充2.txt` 及 GPT 评审 `rebuild-design-review.md` 的 MVP 收敛与修正)
> **本文件是设计单源**(已从旧仓迁入,2026-07-06);开发进度与任务队列见同目录 `dev-plan.md`。
> 项目名已定:**TenonAdmin**(中文名:榫卯 Admin —— 榫卯互锁、不假钉铆,喻零第三方依赖、模块可拆换)。
> GitHub 组织沿用既有 **`DotNet-MoYu`**;NuGet ID `TenonAdmin.*` 已验证空闲(2026-07)。
>
> 原先开源地址:https://github.com/DotNet-MoYu/SimpleAdmin

---

## 0. 目标与原则

**一句话目标**:TenonAdmin 是一个基于 ASP.NET Core + SqlSugar 的轻量企业管理系统内核——安装一个 NuGet 包、
三行代码启动,默认提供登录、RBAC、多机构数据权限和 Vue 管理端模板;**核心运行时除 SqlSugar 外不绑定第三方框架,
所有关键能力均可替换或覆写**。

> v1.0 交付 = 后端 NuGet + Vue(Naive UI)一套模板 + Docker demo。React 模板列 v1.x(见 §9)。

**设计原则**(排序即优先级):

1. **零配置可跑,全配置可换** —— 每一项能力都有默认实现和默认配置;每一项也都能被配置、替换或继承覆写。
2. **除 SqlSugar 外不引第三方运行时依赖** —— BCL / ASP.NET Core 内置件优先;确实绕不开的(如 Redis 驱动)隔离到可选包。
3. **面向接口 + 模板方法** —— 所有服务先定义接口;实现类 public、方法 virtual、长流程拆小步,用户重写任意一步而不必复制整个方法。
4. **刻意少分包、少抽象** —— 不为想象中的需求建工厂/建层;一个接口只有一个实现时也保留接口,但仅限"用户可能替换"的位置(这是产品能力,不是过度设计)。
5. **契约单源** —— 前端 API 层由后端 OpenAPI 文档生成,前端不手写接口定义(工具链见 §13.6)。

**决策记录**:

| 决策 | 结论 |
|---|---|
| 项目名 | TenonAdmin;GitHub 组织 `DotNet-MoYu`;发布首包后在 nuget.org 申请 `TenonAdmin.*` 前缀保留 |
| .NET 版本 | 单 TFM `net10.0`,跟随当前 .NET LTS 滚动升级 |
| 包版本 | SemVer,**从 `0.0.1` 预发布起**,稳定后转 1.0.0;不与 .NET 主版本号绑定(见 §17) |
| 前端 | 自研设计系统 + 自建;Vue 版**只做 Naive UI 一套**(逻辑/视图分离,Soybean 皮肤列 v1.x 可选),React 版 shadcn/ui |
| 对象映射 | Riok.Mapperly(编译期源生成器,运行时零依赖);不用 Mapster/AutoMapper |
| 多语言 | 前后端 i18n,前端持有文案单源、后端只给错误码(见 §13) |
| 多租户 | **不做**(不是 v1 推迟,是整体不做);实体不带租户维度,保持模型简单 |
| 多应用门户 | **做**:独立 `sys_module` 表 + 菜单 `ModuleId`(仅顶级目录);登录选/切应用、每应用独立菜单树、一次加载一个、每用户默认应用。访问权由菜单授权**反推**(非独立权限轴);权限码保持**模块无关** |
| 旧版迁移 | **不支持**从旧 SimpleAdmin 迁移;这是面向新系统的全新产品,不背历史数据包袱 |
| 实时通知 | 不用 SignalR;在线用户走缓存令牌,通知用 HTTP 轮询(v1)或 MQTT 可选包(见 §12) |
| 登录会话 | 可配置,默认多端并存(沿用旧版多 token);可切单端(新登录踢旧)或限并发数 |
| 验证码 | 默认 SVG(零绘图依赖,跨平台);图片/滑块走 `ICaptchaProvider` 扩展点 |
| 国密 SM2/3/4 | 不进核心;做成 v1.x 可选包 `TenonAdmin.Security.Gm`(核心只用 BCL AES/RSA/SHA) |
| 支持数据库 | 官方支持 SQLite/MySQL/SqlServer/PostgreSQL;CI 至少测 SQLite + MySQL |
| 自有 Simple* 包 | SimpleTool 常用件**拷进 Core**;SimpleRedis / SimpleMQTT 作**可选包依赖**保留(仅可选层,不进核心) |
| 仓库 | **v1 合一仓**(monorepo:后端 5 包 + `web/` Vue 前端同仓);React 模板 v1.x 再拆独立仓 `tenon-admin-web-react` |
| v1.0 范围 | 核心子集(见 §4),其余以 v1.x 可选包补齐 |
| License | **纯 Apache-2.0**(去除旧版限制性附加条款,标准 OSI 开源) |
| uniapp 移动端 | 不进 v1,旧版继续可用 |

---

## 1. 仓库结构

| 仓库 | 内容 | 发布物 |
|---|---|---|
| `DotNet-MoYu/tenon-admin`(**v1 单仓 monorepo**) | 后端 5 包源码 + `web/`(Vue 前端)+ `samples/` + `docs/` + `tests/` + `docker-compose.yml` | NuGet 包(nuget.org)+ 前端模板(同仓 `web/`) |
| `DotNet-MoYu/tenon-admin-web-react`(**v1.x 再建**) | React 管理端模板 | 独立模板仓库 |

选合一仓的理由:一次 clone 跑全栈、`docker compose up` 即起 demo、openapi 契约本地生成不跨仓、前后端同步演进、早期少维护一堆仓。React 到 v1.x 观感/逻辑分离成熟后再拆独立仓。

v1 monorepo 目录草案(**后端 v1.0 只建这 5 个包**;可选包按需再建,不先建空目录):

```
tenon-admin/                       # v1 单仓
├─ backend/                        # 后端独立一层(前后端边界清晰)
│  ├─ src/
│  │  ├─ TenonAdmin.Core/         # 抽象与契约
│  │  ├─ TenonAdmin.SqlSugar/     # 数据访问
│  │  ├─ TenonAdmin.Services/     # 领域服务
│  │  ├─ TenonAdmin.AspNetCore/   # Web 层
│  │  └─ TenonAdmin/              # 元包(仅 PackageReference)
│  ├─ samples/
│  │  └─ MinimalHost/             # 三行 Program.cs 的验收样例
│  ├─ tests/
│  │  └─ TenonAdmin.Tests/        # xunit
│  ├─ Directory.Build.props       # 统一版本号、TFM、分析器、包元数据
│  ├─ Directory.Packages.props    # 统一依赖版本(中央包管理)
│  └─ TenonAdmin.slnx             # .NET 10 方案格式(等价旧 .sln)
├─ web/                           # Vue 3 + Naive UI 前端模板(openapi 本地生成)
├─ docs/
├─ docker-compose.yml             # 后端 + web(nginx)+ 可选 Redis/MySQL,一键起 demo
├─ .gitattributes                 # 统一行尾(LF)
└─ README.md
# 可选包(TenonAdmin.Caching.Redis / .Mqtt / .Scalar / .Security.Gm)按需在 backend/src 下再建,见 §2.1
# React 模板 v1.x 拆出为独立仓 tenon-admin-web-react
```

---

## 2. NuGet 包矩阵

### 2.1 包与依赖关系

```
TenonAdmin(元包,无代码)
 └─→ TenonAdmin.AspNetCore
      └─→ TenonAdmin.Services
           ├─→ TenonAdmin.SqlSugar ──→ SqlSugarCore(唯一重量级第三方)
           └─→ TenonAdmin.Core(零运行时依赖;Mapperly 仅 analyzer,不进运行时)

v1.0 只建这 5 个包。以下可选包"**已规划、按需再建**"(v1.0 不建空目录,不受"核心零依赖"约束):
TenonAdmin.Caching.Redis ──→ SimpleRedis(你的库:高并发 + 内置 MQ 封装)   [v1.0/v1.1,默认内存能跑则后移]
TenonAdmin.Mqtt          ──→ SimpleMQTT(你的库)                          [v1.x]
TenonAdmin.Scalar        ──→ Scalar.AspNetCore(仅开发期)                  [v1.x]
TenonAdmin.Security.Gm   ──→ BouncyCastle(国密 SM2/3/4)                   [v1.x]
```

> **核心四包(Core / SqlSugar / Services / AspNetCore)运行时依赖只允许 `SqlSugarCore` + `Microsoft.*`。**
> "零运行时依赖"**不代表零 analyzer 依赖**:Mapperly 以源生成器方式引入,不进运行时程序集:
> ```xml
> <PackageReference Include="Riok.Mapperly" Version="..." PrivateAssets="all" OutputItemType="Analyzer" />
> ```
> 其余一切要么自写、要么拷源、要么下沉到可选包 —— 具体逐库处置见 §2.3。

### 2.2 各包职责

| 包 | 内容 | 依赖 |
|---|---|---|
| `TenonAdmin.Core` | 实体基类(`BaseEntity`/`DataEntity`)、`Result<T>` 统一返回模型、业务异常体系(`AdminException`)、全部扩展点接口(§5)、雪花 ID 实现、Channels 事件总线、分页模型、常用扩展方法 | 无(零运行时依赖;Mapperly 仅 analyzer) |
| `TenonAdmin.SqlSugar` | `SugarClient` 单例封装、`IRepository<T>` 仓储、CodeFirst 建表、种子数据机制(`ISeedData`)、多库/读写分离配置解析 | Core + SqlSugarCore |
| `TenonAdmin.Services` | 全部领域服务及其 DTO:认证、RBAC、用户/机构/职位/角色/菜单、字典、系统配置、操作/登录日志、本地上传、在线用户;内置种子数据 | SqlSugar |
| `TenonAdmin.AspNetCore` | 控制器(按模块)、`AddTenonAdmin()`/`MapTenonAdmin()`、JWT 接入、统一返回过滤器、全局异常处理、权限/数据范围过滤器、验证码端点、内置 OpenAPI 文档 | Services + ASP.NET Core 框架引用 |
| `TenonAdmin`(元包) | 仅 PackageReference,一键全装 | AspNetCore |
| `TenonAdmin.Caching.Redis`(可选) | `RedisCacheProvider`,把默认 `MemoryCacheProvider` 换成 Redis(+ MQ) | Core + **SimpleRedis** |
| `TenonAdmin.Mqtt`(可选,v1.x) | MQTT 接入(通知推送等) | Core + **SimpleMQTT** |
| `TenonAdmin.Scalar`(可选,v1.x) | `MapTenonAdminApiDocs()`,开发期 API 调试 UI | AspNetCore |

**替代表**(旧版 MoYu 能力 → 新版实现):

| 旧版(MoYu/第三方) | 新版 |
|---|---|
| `Serve.Run()` 隐式启动 | 标准 `WebApplication` + `AddTenonAdmin()`/`MapTenonAdmin()` |
| 动态 API 控制器 | 标准 `[ApiController]`,可经 ApplicationPart 按模块禁用 |
| DI 自动扫描(全量) | **双层**:框架内置服务各模块扩展方法内显式 `TryAdd`;用户外部模块默认扫描入口程序集及其引用(见 §5.7) |
| 统一返回/异常包装 | 自写 `IResultFilter` + .NET 内置 `IExceptionHandler` |
| JWT(MoYu.Extras) | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| Swashbuckle | 内置 `Microsoft.AspNetCore.OpenApi`;UI 走可选 Scalar 包 |
| 事件总线 | 自写 `System.Threading.Channels` 进程内总线(<200 行) |
| 定时任务插件 | `IHostedService` + 自写轻量 cron 解析 |
| Yitter 雪花 ID | 自写雪花算法(单文件,`IIdGenerator` 可换) |
| NewLife.Redis / 缓存 | 对外抽象 `ICacheProvider`,默认 `MemoryCacheProvider`(基于 `IMemoryCache`);Redis 可选包 `RedisCacheProvider` 用你的 **SimpleRedis**(含高并发 + MQ);HybridCache 仅作内部可选细节,不作为用户核心概念 |
| Mapster 映射 | **Riok.Mapperly**(编译期源生成器,生成纯代码,运行时零依赖零反射) |
| BouncyCastle 国密 | v1 用 BCL AES/RSA/SHA;国密延后为 `TenonAdmin.Security.Gm` 可选包 |
| Masuit.Tools / SimpleTool 等工具库 | 按需自写进 Core.Extension,用到哪个写哪个 |

### 2.3 现有依赖处置清单(**已定稿**)

原则:核心四包只允许 `SqlSugarCore` + `Microsoft.*` 作运行时依赖;其余进"自写 / 拷源 / 可选包"三档。
下表处置已定(2026-07)。

| 现有包 | 用途 | 处置 | 状态 |
|---|---|---|---|
| SqlSugarCore | ORM | **保留**(唯一重量级第三方) | ✅ 定 |
| MoYu.Pure | 整个框架 | **移除** → ASP.NET Core 原生 | ✅ 定 |
| MoYu.Extras.Authentication.JwtBearer | JWT | **替换** → `Microsoft.AspNetCore.Authentication.JwtBearer` | ✅ 定 |
| MoYu.Extras.ObjectMapper.Mapster / Mapster | 映射 | **替换** → Riok.Mapperly(源生成) | ✅ 定 |
| Yitter.IdGenerator | 雪花 ID | **自写** → Core 单文件,`IIdGenerator` 可换 | ✅ 定 |
| SharpZipLib | 压缩 | **替换** → BCL `System.IO.Compression` | ✅ 定 |
| System.Drawing.Common | 验证码绘图 | **移除**(跨平台隐患)→ SVG 验证码 | ✅ 定 |
| Lazy.Captcha.Core | 图形验证码 | **自写 SVG** → 默认 SVG,图片/滑块走 `ICaptchaProvider` | ✅ 定 |
| NewLife.Core | 工具/网络 | **移除** → 用到处换 BCL | ✅ 定 |
| Masuit.Tools.Core | 工具集 | **移除/拷源** → 仅拷用到的 helper 进 Core.Extension | ✅ 定 |
| Magicodes.IE.Excel | Excel 导入导出 | **v1.x 可选包** → `TenonAdmin.Excel` | ✅ 定 |
| Minio | 对象存储 | **v1.x 可选包** → `TenonAdmin.Storage.Minio`,走 `IFileStorage` | ✅ 定 |
| SimpleMQTT | MQTT | **可选包依赖** → `TenonAdmin.Mqtt` 直接用 SimpleMQTT | ✅ 定 |
| SimpleRedis | Redis 缓存 + MQ | **可选包依赖** → `TenonAdmin.Caching.Redis` 直接用 SimpleRedis(高并发 + MQ 封装,StackExchange 无现成 MQ 层) | ✅ 定 |
| SimpleTool | 工具(你自己的) | **拷源进 Core** → 常用 helper 拷进 `Core.Extension`,不作外部依赖 | ✅ 定 |
| Portable.BouncyCastle | 国密/加密 | **v1.x 可选包** → `TenonAdmin.Security.Gm`;v1 核心用 BCL AES/RSA/SHA | ✅ 定 |
| IP2Region.Net (+ ip2region.xdb) | IP 地理(登录日志) | **v1.x 可选包** → v1 登录日志只存 IP 原文;地理定位后置 | ✅ 定 |
| UAParser | UA 解析(登录日志) | **v1.x/移除** → v1 只存 UserAgent 原文;精解后置 | ✅ 定 |
| Microsoft.Extensions.Hosting | 宿主 | **保留**(框架件) | ✅ 定 |
| Microsoft.Extensions.Hosting.WindowsServices | Windows 服务托管 | **移除** → 管理系统后端 v1 不需 Windows 服务托管 | ✅ 定 |

---

## 3. 用户侧体验(验收基准)

### 3.1 最小启动

用户项目完整 `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration);   // 默认扫描入口程序集及其引用,接管用户模块
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

需要控制扫描/程序集时用带 options 的重载(见 §5.7):
```csharp
builder.Services.AddTenonAdmin(builder.Configuration, options =>
{
    // 注:ScanApplicationAssemblies 从未实现,已标 [Obsolete](置任何值都无效);实际只有下面这行生效。
    options.ApplicationAssemblies.Add(typeof(DeviceService).Assembly);
});
```

零配置时的默认行为:

- 数据库:SQLite `./data/admin.db`,首启自动 CodeFirst 建表 + 种子(默认超管账号,启动日志打印);
- 缓存:`MemoryCacheProvider`(进程内);
- JWT:自动生成开发密钥并持久化到 `./data/dev-jwt.key`,日志输出醒目警告"生产环境必须配置密钥";
- 上传:本地 `./wwwroot/upload`;
- 全部内置端点挂载,前端模板可直接登录。

### 3.2 配置结构(`appsettings.json` 的 `TenonAdmin` 节)

所有子节可整体省略;给出即覆盖默认值。

```jsonc
{
  "TenonAdmin": {
    "Database": {
      "DbType": "Sqlite",              // Sqlite | MySql | SqlServer | PostgreSQL(官方支持,CI 测 SQLite+MySQL)
      "ConnectionString": "Data Source=./data/admin.db",
      "EnableCodeFirst": true,          // 首启自动建表(生产默认禁,见下)
      "EnableCodeFirstInProduction": false, // 生产必须显式开启才允许自动改表(§12 建表安全)
      "EnableSeed": true                // 首启自动种子(幂等)
    },
    "Cache": {
      "Provider": "Memory",            // Memory(默认,进程内)| Redis(装 TenonAdmin.Caching.Redis 可选包后可用)
      "RedisConnectionString": null,
      "KeyPrefix": "tenon:",           // 缓存键前缀,与 §15 会话键 tenon:session:{sid} 一致;逻辑键由 ICacheProvider 统一追加
      "PermissionMinutes": 20          // 用户权限码缓存 TTL 兜底(授权变更走显式失效即时生效);0=永不过期
    },
    "Jwt": {
      "SecretKey": null,               // null => 开发密钥 + 警告
      "Issuer": "TenonAdmin",
      "ExpireMinutes": 120,
      "RefreshExpireMinutes": 10080
    },
    "Security": {
      // PasswordPolicy 尚未实现(设计意图,v1.x);当前口令策略仅有下述 LoginLock + DefaultInitialPassword
      "Captcha": { "Enabled": false, "Type": "Svg" },  // 默认关(保三行零配置 API 直登;Web/生产 opt-in);SVG 零绘图依赖,图片/滑块走 ICaptchaProvider
      "LoginLock": { "MaxFailCount": 5, "LockMinutes": 10 },
      "Session": { "Mode": "Multi", "MaxConcurrent": 0 }, // Multi(默认,多端并存)| Single(新登录踢旧);MaxConcurrent>0 时限制并发端数
      "RateLimit": {                    // 请求限流(Phase 2b):按客户端 IP 固定窗口,认证端点更严
        "Enabled": true,
        "WindowSeconds": 60,
        "PermitPerWindow": 300,         // 全局每 IP(宽松,挡洪泛);<=0 不限
        "AuthPermitPerWindow": 20       // /api/v1/auth/* 每 IP(更严,挡在线爆破);<=0 不限
      },
      "DefaultInitialPassword": null    // 新建用户/重置密码未给时的默认口令;null => 密码学随机(安全默认,不落公开常量)
    },
    "Upload": {
      "Provider": "Local",             // Local;OSS 类走 IFileStorage 扩展点
      "RootPath": "./wwwroot/upload",
      "MaxSizeMb": 20,
      "AllowedExtensions": [".jpg", ".png", ".pdf", ".xlsx", ".docx", ".zip"]
    },
    "Api": {
      // RoutePrefix / Version 配置化后置 v1.x(深耦合权限码与菜单种子);v1 内置路由固定 api/v1
      "DisabledModules": [],           // 例:["Dict","Upload"] 关闭对应模块控制器
      "Cors": {                         // 跨源(Phase 2b):默认收紧,无源即不放行;经 IStartupFilter 挂 UseCors
        "AllowedOrigins": [],           // 例:["https://admin.example.com"];空=不放行任何跨源
        "AllowCredentials": true
      }
    },
    "Id": {
      "WorkerId": 0                     // 雪花机器号(0–1023);多实例水平扩展须为每实例配不同值
    },
    "Notify": {
      "Mode": "Polling",               // v1.0 仅 Polling;Mqtt 由 v1.x 可选包 TenonAdmin.Mqtt 提供,v1 不暴露
      "PollingIntervalSeconds": 30
    },
    "Seed": {
      "AdminAccount": "superAdmin",
      "AdminPassword": null            // null => 随机生成并打印
    }
  }
}
```

每个子节对应一个 Options 类:`AdminDatabaseOptions`、`AdminCacheOptions`、`AdminJwtOptions`、
`AdminSecurityOptions`(含 `AdminRateLimitOptions`)、`AdminUploadOptions`、`AdminApiOptions`(含 `AdminCorsOptions`)、
`AdminSeedOptions`、`AdminIdOptions`,全部 public,支持 `builder.Services.Configure<T>(...)` 代码覆写。

---

## 4. v1.0 功能范围

**进 v1.0(核心子集)**:

| 模块 | 说明 |
|---|---|
| 认证 | 账密登录 + 图形验证码、JWT + Refresh Token、登录锁定、登出、在线用户列表/强退 |
| RBAC | 角色、菜单(目录/页面/按钮三级)、按钮级权限码、角色-菜单授权 |
| 模块/应用 | **多应用门户**:模块管理(`sys_module`);菜单按模块分区(顶级目录挂模块);登录后选/切应用,每应用独立菜单树、一次加载一个;每用户默认应用。模块访问权由菜单授权反推,权限码保持模块无关 |
| 数据权限 | **多机构数据范围**(全部/本机构/本机构及以下/仅本人/自定义机构),接口级可配 —— 旧版招牌能力,完整保留 |
| 组织 | 用户、机构(树)、职位;用户多角色、主属机构 |
| 字典 | 字典类型 + 字典项,前端下拉数据源 |
| 系统配置 | 键值配置,分组管理,带缓存 |
| 日志 | 操作日志(过滤器自动记录)+ 登录日志(存 IP + UserAgent 原文 + 时间 + 结果 + 用户 + 失败原因),查询/清空 |
| 文件 | **普通本地上传** + 下载 + 列表 + 大小限制 + 后缀白名单 + 路径穿越防护(分片上传后置 v1.x) |
| 个人中心 | 改密码、改资料、头像 |

> 登录日志的 **IP 地理定位 / UA 精解**(旧版 IP2Region / UAParser)后置 v1.x,v1 只存原文。

**v1.x 以可选包/后续版本补齐**(不阻塞 v1.0 发布):
**React 模板**、**SoybeanUI 皮肤**、**分片上传**、IP 地理/UA 精解、代码生成(`TenonAdmin.CodeGen`)、
导入导出(`TenonAdmin.Excel`)、批量修改、消息中心、MQTT(`TenonAdmin.Mqtt`)、任务调度(`TenonAdmin.Scheduling`)、
国密(`TenonAdmin.Security.Gm`)、OSS/Minio 存储(`TenonAdmin.Storage.*`)、可观测性(`TenonAdmin.Observability`)。

### 4.1 v1.0 非目标(明确不做,防膨胀)

v1.0 **明确不做**——写清楚"不做什么"和"做什么"同样重要:

- 不支持从旧 SimpleAdmin 迁移数据;
- 不做多租户;
- 不提供 React 正式模板(v1.x);
- 不提供 Vue 第二套 UI 皮肤(SoybeanUI,v1.x);
- 不做分片上传(v1.x);
- 不做代码生成 / Excel 导入导出 / 批量修改;
- 不做 Minio / OSS 存储;
- 不做 MQTT 推送、不用 SignalR(v1 只 HTTP 轮询);
- 不做国密、不做任务调度;
- 不做复杂工作流 / 表单引擎;
- 不承诺生产环境自动改表(§12 建表安全)。

---

## 5. 可重写性设计(核心卖点)

四层覆写能力,按侵入程度递增:

### 5.1 配置覆写
Options 全暴露(§3.2),json 或代码均可。

### 5.2 服务替换
`AddTenonAdmin()` 内部所有注册一律 `TryAdd`:

```csharp
// 框架内部
services.TryAddScoped<IUserService, UserService>();

// 用户侧:在 AddTenonAdmin() 之前注册即生效
builder.Services.AddScoped<IUserService, MyUserService>();
builder.Services.AddTenonAdmin(builder.Configuration);
// 或之后 Replace
builder.Services.Replace(ServiceDescriptor.Scoped<IUserService, MyUserService>());
```

### 5.3 继承覆写(模板方法)
所有服务实现类 `public`、方法 `virtual`;长流程拆成可独立覆写的小步。示例签名草案:

```csharp
public class AuthService : IAuthService
{
    public virtual async Task<LoginOutput> LoginAsync(LoginInput input)
    {
        await ValidateCaptchaAsync(input);
        var user = await ValidateUserAsync(input);      // 账密校验,可换成 LDAP/AD
        await CheckLoginPolicyAsync(user);              // 锁定/停用检查
        var token = await CreateTokenAsync(user);       // 签发逻辑
        await OnLoginSucceededAsync(user, token);       // 写日志、发事件
        return BuildLoginOutput(user, token);
    }
    protected virtual Task ValidateCaptchaAsync(LoginInput input) { ... }
    protected virtual Task<SysUser> ValidateUserAsync(LoginInput input) { ... }
    protected virtual Task CheckLoginPolicyAsync(SysUser user) { ... }
    protected virtual Task<TokenPair> CreateTokenAsync(SysUser user) { ... }
    protected virtual Task OnLoginSucceededAsync(SysUser user, TokenPair token) { ... }
    protected virtual LoginOutput BuildLoginOutput(SysUser user, TokenPair token) { ... }
}
```

用户只覆写想改的一步:

```csharp
public class LdapAuthService : AuthService
{
    protected override Task<SysUser> ValidateUserAsync(LoginInput input)
        => _ldap.AuthenticateAsync(input.Account, input.Password);
}
```

### 5.4 端点覆写
`TenonAdmin:Api:DisabledModules` 按模块摘除内置控制器(ApplicationPart `IApplicationFeatureProvider`
过滤),用户自写同路由控制器接管;也可整体不调 `MapTenonAdmin()` 只挑子方法
(`MapTenonAdminAuth()`/`MapTenonAdminSystem()`... 每个模块一个)。

### 5.5 扩展点接口清单(默认实现全部可换)

| 接口 | 默认实现 | 典型替换场景 |
|---|---|---|
| `IPasswordHasher` | PBKDF2(BCL `Rfc2898DeriveBytes`) | 对接已有用户库的哈希算法 |
| `ITokenProvider` | JWT | 自定义 token/对接网关 |
| `IDataScopeProvider` | 机构树数据范围 | 自定义数据隔离维度(如租户) |
| `ICaptchaProvider` | **SVG 验证码**(纯字符串生成,零绘图依赖,跨平台) | 图片/滑块/行为验证码 |
| `IPermissionProvider` | 从缓存/Redis 取用户权限码列表(路由集) | 对接外部鉴权中心 |
| `IFileStorage` | 本地磁盘 | OSS/Minio/S3 |
| `IIdGenerator` | 自写雪花 | 数据库自增/GUID v7(BCL 内置) |
| `IEventBus` | Channels 进程内 | RabbitMQ/Kafka 分布式 |
| `ICacheProvider` | `MemoryCacheProvider`(`IMemoryCache`) | `RedisCacheProvider`(可选包,SimpleRedis) |
| `IOperationLogStore` | 写数据库 | 写 ELK/ClickHouse |
| `ISeedData`(多实现) | 内置种子 | 用户追加自己的种子类 |

> 验证码默认实现定为 **SVG 验证码**(纯文本生成,零绘图依赖);想要图片/滑块的用户走 `ICaptchaProvider` 扩展点。

### 5.6 实体扩展
业务实体基类三层:`BaseEntity`(Id/CreateTime/CreateUser/UpdateTime/IsDelete...)→ `DataEntity`(+ 数据范围/机构字段)。
用户业务表继承基类即自动获得审计字段填充、软删除与数据权限过滤;内置实体(`SysUser` 等)预留
`ExtJson` 扩展字段,避免用户为加一列就得改框架表(读写走强类型包装,不散 `JObject`)。

### 5.7 完整走查:用户如何基于本系统开发自己的业务模块

目标:用户在**自己的外部项目**里新增一个"设备台账"模块,不改框架源码,即可拥有增删改查 +
审计 + 数据权限 + 种子。全流程五步,均在用户项目内:

```csharp
// 1) 实体:继承 DataEntity 即自动获得 Id/审计/软删除/机构数据范围字段
[SugarTable("biz_device")]
public class Device : DataEntity
{
    [SugarColumn(ColumnDescription = "设备名")]
    public string Name { get; set; }
    public string Sn { get; set; }
}
// 首次启动 CodeFirst 自动建表(默认仅 Dev 环境,见 §12 生产建表安全)

// 2) 服务接口 + 实现:继承标记接口即被自动扫描注册(见下"注册模型")
public interface IDeviceService : ITransient
{
    Task<SqlSugarPagedList<Device>> PageAsync(DevicePageInput input);
    Task AddAsync(DeviceAddInput input);
}
public class DeviceService : IDeviceService
{
    private readonly IRepository<Device> _repo;      // 泛型仓储直接注入
    public DeviceService(IRepository<Device> repo) => _repo = repo;

    public virtual Task<SqlSugarPagedList<Device>> PageAsync(DevicePageInput input)
        => _repo.AsQueryable()                       // 数据权限过滤器已自动生效
                .WhereIF(!string.IsNullOrEmpty(input.Name), d => d.Name.Contains(input.Name))
                .ToPagedListAsync(input.Current, input.Size);

    public virtual async Task AddAsync(DeviceAddInput input)
        => await _repo.InsertAsync(DeviceMapper.ToEntity(input));   // Mapperly 源生成的映射,非运行时反射
}

// 2b) 映射:Mapperly 编译期生成实现,零运行时依赖
[Mapper]
public static partial class DeviceMapper
{
    public static partial Device ToEntity(DeviceAddInput input);
}

// 3) 控制器:标准 [ApiController];挂 [RolePermission] 即纳入路由级权限校验
[ApiController, Route("biz/device")]
public class DeviceController : ControllerBase
{
    private readonly IDeviceService _svc;
    public DeviceController(IDeviceService svc) => _svc = svc;

    [HttpGet("page"), RolePermission] public Task<dynamic> Page([FromQuery] DevicePageInput i) => ...;
    [HttpPost("add"), RolePermission, OperationLog("新增设备")] public Task Add(DeviceAddInput i) => _svc.AddAsync(i);
}

// 4) 种子(可选):实现 ISeedData,启动自动执行且幂等
public class DeviceSeedData : ISeedData
{
    public IEnumerable<Device> HasData() => new[] { new Device { Name = "示例设备", Sn = "SN-0001" } };
}

// 5) 用户项目 Program.cs 仍是那三行;框架扫描入口程序集及其引用即接管上述类型。
```

**注册模型(双层,消除"显式 vs 扫描"的歧义)**:
- **框架内置服务**:一律在各模块 `AddXxx()` 里**显式 `TryAdd`** 注册,不靠扫描 —— 可预测、可被用户 `Replace`(§5.2)。
- **用户外部模块**:经 `options.ApplicationAssemblies.Add(...)` **显式登记**业务程序集,其实体参与 CodeFirst 建表、控制器 AddApplicationPart 挂载。
  > 实现说明:原设计的"默认扫描入口程序集及引用"(`ScanApplicationAssemblies`)最终**未实现**——为守内核"显式、可预测、无魔法"的取向,只保留显式登记这一条真源;该开关已于 2026-07-09 标 `[Obsolete]`。

要覆写**内置**服务/实体行为,回到 §5.1–5.4 四层覆写;要新增**自己**的东西,就是上面这套。
两者对称:内置模块本身也是照这套写的(区别只是内置走显式注册、用户走扫描)。

---

## 6. 后端横切机制

- **统一返回**:`Result<T> { Code, Msg, Data }`,由 `IResultFilter` 自动包装;业务抛 `AdminException(errorCode)`,`IExceptionHandler` 统一转换,未知异常记日志返回 500 通用文案。错误码与文案见 §13 i18n/错误码目录,**不在抛出处写死中文串**。
- **权限过滤(沿用你现有模型,不引入硬编码权限串)**:
  - 标记特性 **`[RolePermission]`**(无参数)标注"此接口需授权";**权限码 = 规范化路由**。v1 约定**含 HTTP Method**(`POST:/api/v1/biz/device/add`),以区分同路径不同操作(`GET`/`POST /biz/device`);不再手写 `"sys:user:add"` 这类字符串。
  - 授权管道(对应旧 `JwtHandler.CheckAuthorizationAsync`):超管直接放行 → `[SuperAdmin]` 接口校验超管身份 → `[RolePermission]` 接口取当前用户 `PermissionCodeList`(**从缓存/Redis 读**,`IPermissionProvider` 可换)判断是否包含当前路由;`[IgnoreRolePermission]` 显式豁免。
  - **前端权限常量**:CI 从 OpenAPI / 菜单种子生成前端权限常量,页面优先用 `v-auth="Permission.BizDeviceAdd"` 而非裸串;菜单管理里仍展示实际路由码,便于排错。
  - **数据范围(T3 落地)**:模型为**按角色**配置(`sys_role_data_scope`,每角色一条,五种范围:全部/本机构/本机构及以下/仅本人/自定义),用户生效范围 = 其各角色范围的**并集**(All 最宽优先)。`IDataScopeProvider` 解析并按用户缓存;由 **SqlSugar 全局查询过滤器**对实现 `IOrgScoped` 的实体(`DataEntity` 及子类)自动注入 `CreateOrgId ∈ 范围` / `CreateUserId == 本人` 的 WHERE——业务表继承 `DataEntity` 即自动受控,无需每个接口手写校验。
    - 生效范围经 `IDataScopeContext` 在**授权管道**(动作执行前)按当前用户解析并写入;HTTP 侧用 `HttpContext.Items` 承载(避免授权过滤器内 AsyncLocal 不回流的陷阱),非 HTTP 用 AsyncLocal。
    - 与旧版差异(**设计修正**):旧版是**按接口**(role×api)配置数据范围;v1 简化为**按角色**全局过滤(实现更简、覆盖更全、不易漏挂),更细的"同角色不同接口不同范围"granularity 留 v1.x 扩展。`SqlSugar 全局过滤器只认接口/精确类型、不认基类`,故用标记接口 `IOrgScoped` 匹配(与软删过滤器走 `ISoftDelete` 同理)。
    - **范围边界**:`sys_user` 本身继承 `BaseEntity`(非 `DataEntity`),用户列表**不**走通用机构过滤(用户的机构维度是其 `OrgId` 而非 `CreateOrgId`,属特例,如需按机构筛用户在 `UserService` 显式处理);无角色 / 有角色但未配范围 → 默认"仅本人"(不放大可见面)。会话/强退/在线的模型见 §15。
- **多应用门户(模块分区)**:菜单按 `sys_module` 分区——`ModuleId` 仅顶级目录设置,子节点归属由内存树上溯到根目录解析。"我的模块"由菜单授权**实时反推**(用户被授权某模块下任一菜单即拥有该模块,超管见全部;门户/登录时算,非每请求热路径,不缓存),登录后按选定模块拉菜单树并注册动态路由。**权限码保持模块无关**——切应用只改侧边栏与路由,不改用户持有的 API 权限码(`RbacPermissionProvider` 不按模块过滤;这是回归锁死的不变量)。默认应用为每用户偏好(`sys_user.DefaultModuleId`)。内置 `system` 模块不可删。
- **禁硬编码字符串**(全局约定):缓存 key / 权限码 / 类别码 一律走常量(延续你已有的 `CacheConst` / `SystemConst` / `CateGoryConst`);面向用户的文案走 i18n 资源 + 错误码枚举;魔法数走枚举。代码评审把"裸字面量字符串"当味道。
- **操作日志**:`[OperationLog]` 特性(标题可取常量/资源键)+ 过滤器自动记录(入参/耗时/结果码),敏感字段脱敏配置。
- **缓存策略**:用户权限/菜单/字典/配置进缓存,变更即失效(事件总线广播),保持旧版"登录后接口 10-30ms"的体验目标。
- **OpenAPI**:内置 `AddOpenApi()` 输出 `openapi.json`;CI 产出该文件为前端仓的契约源。

---

## 7. 前端设计(v1.0 = Vue Naive UI;React 版 v1.x)

### 7.1 先行产物:`DESIGN.md` 设计系统规范

前端(v1 在同仓 `web/`,React v1.x 拆出后)共享同一份规范与同一份 design tokens(CSS variables),视觉天然一致。

大纲:

```
DESIGN.md
├─ 1. 设计基调:企业级、精简、留白充分、低饱和主色、明暗双主题
├─ 2. Design Tokens(CSS variables 单源)
│   ├─ 色板:主色/中性色阶/语义色(success/warning/danger/info),暗色映射
│   ├─ 字体:字族、字号阶梯(12/13/14/16/20/24)、行高、字重
│   ├─ 间距:4px 基准网格(4/8/12/16/24/32)
│   └─ 圆角(4/6/8)/阴影(3 级)/边框/过渡时长
├─ 3. 布局:侧边导航(可折叠 240↔64)+ 顶栏(面包屑/搜索/用户区)+ 内容区(页签可选)
├─ 4. 核心页面形态(每种一张规范图)
│   ├─ 列表页:筛选区 + 工具栏 + 表格 + 分页
│   ├─ 表单弹窗 / 抽屉:何时用弹窗何时用抽屉
│   ├─ 树+表联动页(机构-用户、菜单管理)
│   └─ 详情页 / 授权面板
├─ 5. 组件规范:按钮层级、表格密度、空态/加载态/错误态、消息反馈
└─ 6. 可访问性:对比度 ≥ 4.5:1、焦点态、键盘导航
```

风格方向:现代企业感(Stripe/Vercel 一类)。

**设计生产流水线**(可用 AI 设计工具,不必手绘每一屏):
1. **选主题**:用 **typeui MCP**(自带多套主题 skills)挑一套接近目标风格的主题作为 token 起点;
   本仓另有 `pencil` MCP、`awesome-design-md` 技能可作补充参考。
   
   > 注:typeui MCP 目前未接入本会话,需在 Claude Code 的 MCP 配置中添加后使用。
2. **出稿**:关键页(登录、列表页、表单弹窗、树+表、授权面板)用 Figma / Claude / Pencil 生成高保真稿,统一走 §7.1 页面形态。
3. **导出 token**:把主题/稿件的色板、字阶、间距导出成 **一份 CSS variables**(design tokens 单源)。
4. **落地**:Vue/React 两端都消费这份 tokens;组件库只做"渲染载体",视觉由 tokens 决定 → 两端天然一致。

### 7.2 技术栈

| | Vue 版 | React 版 |
|---|---|---|
| 框架 | Vue 3.5+ / Vite / TS | React 19 / Vite / TS |
| 组件库 | **Naive UI 单套**(CSS vars 深度换肤对齐 tokens) | shadcn/ui + Tailwind CSS v4(tokens 直接映射) |
| 状态 | Pinia | Zustand + TanStack Query |
| 路由 | vue-router,菜单驱动动态路由(沿用旧版模式) | react-router(或 TanStack Router),动态注册 |
| API 层 | openapi-ts 从后端 openapi.json 生成 | 同左 |
| 工程 | ESLint + Prettier + commitlint + lint-staged | 同左 |

**Vue = 只做 Naive UI 一套(方案 B),但按"逻辑/视图分离"写**:
- v1.0 只交付 **Naive UI** 一套 Vue 前端,写法完全地道,不做适配层(适配层会抹平组件库个性,已否)。
- 但从第一天就把**逻辑沉进 composables**(请求、分页/搜索状态、权限判断、字典、表单校验规则、路由/菜单数据都与 UI 无关),`.vue` 视图只做 markup + 装配。
- 这样"支持第二种 UI"退化为**将来补一层视图**、而非重写:SoybeanUI 皮肤列为 **v1.x 可选**,有真实需求再做。
- 判据:共享的 composable 不得返回任何 Naive 专有类型(如 `DataTableColumns`),UI 类型只出现在视图层——用 lint 约束这条边界。
> 为什么不双 UI:本项目卖点是后端 NuGet 一行启动;一套打磨到位的 Naive 版对采用度的作用大于两套各半成品,也避免第二套皮肤长期失修(§10 风险)。

**契约单源机制**:后端产出 `openapi.json`(v1 同仓本地生成)→ 前端一条
`npm run gen:api` 脚本拉取并重新生成类型化客户端;接口定义零手写、双版零漂移。

**前端通用约定**:`v-auth` / `<Auth>` 权限指令(按钮级权限码控制显隐)、明暗主题切换、i18n 多语言、
请求层统一错误处理(对齐后端 `Result` 与错误码)、响应式适配(桌面优先,窄屏可用)。

### 7.3 页面范围(对齐后端 v1.0)

登录、工作台(简)、用户管理(机构树+表)、机构、职位、角色(含菜单授权/数据范围授权)、
菜单管理(顶级目录可选所属应用)、模块/应用管理、字典、系统配置、操作日志、登录日志、在线用户、个人中心。

### 7.4 前端架构(以 Vue 版为准,React 版对齐)

沿用旧版 SimpleAdmin(GeeKer-Admin 系)已验证的结构,并按方案 B 的"逻辑/视图分离"补上 `composables`。
参考对象:soybean-admin、naive-ui-admin、旧版 SimpleAdmin `web/`。

**目录结构**(v1 在 monorepo 的 `web/`):
```
web/src/
├─ api/            # 类型化接口层:modules/(按域封装) + interface/(openapi-typescript 生成的类型)
├─ composables/    # ★逻辑单源:useTable/useForm/useAuth/useDict/useRequest —— 与 UI 无关,可复用给第二皮肤
├─ components/     # 通用业务组件(不绑具体页面):SearchForm、ProTable、TreeFilter、Upload…
├─ layouts/        # 布局骨架:多布局(Vertical/Classic/Columns/Transverse)+ Header/Menu/Tabs/Footer
├─ router/         # 静态路由(白名单)+ 动态路由(菜单驱动,登录后按权限注册)
├─ stores/         # Pinia:user(令牌/信息)、auth(权限码/菜单树)、app(布局/主题/折叠)、tabs
├─ directives/     # v-auth(按钮级权限)、v-copy、v-debounce…
├─ locales/        # i18n 资源(见 §13 i18n 设计)
├─ hooks/ utils/ enums/ styles/ config/ typings/
└─ views/          # 页面:按域分组,入口 index.vue + components/ 子组件
```

**关键机制**:
- **布局系统**:**默认竖向侧边栏布局**(企业最常用),保留多布局切换能力(Classic/Columns/Transverse 作为可选)。左侧菜单 + 顶栏(面包屑/搜索/主题/语言/用户)+ 内容区(可选多页签 Tabs)。布局状态存 `stores/app`,支持折叠 240↔64、明暗主题、主题色。
- **菜单与动态路由**:登录后拉取用户菜单树 → 生成动态路由并注册 → 渲染侧边菜单;刷新页面走"路由守卫重建"避免白屏(沿用旧版模式)。菜单的 `component` 字段匹配 `views/**/*.vue`。
- **多应用切换(app-switcher)**:登录后拉 `GET /api/v1/personal/modules`——单应用直接进,有默认且可访问进默认,否则弹「选择应用」;空列表提示未分配应用。顶栏「切换应用」重选。切换即以选定 `moduleId` 拉 `GET /api/v1/personal/menu?moduleId=` 重建动态路由(整棵菜单树按应用替换)。默认应用经 `PUT /api/v1/personal/default-module` 持久化(每用户)。逻辑沉 `composables`(useModule/useAuthMenu),视图不含 Naive 专有类型。
- **权限**:路由级(动态路由本身即权限过滤)+ 按钮级(`v-auth="'/biz/device/add'"`,权限码就是后端路由,呼应 §6 不硬编码)。权限码列表进 `stores/auth`。
- **请求层**:axios 封装,统一注入令牌、统一按后端 `Result` + 错误码处理(§12 i18n 把错误码映射成本地化文案),401 自动刷新/登出。
- **页面范式**:列表页 = `SearchForm` + `ProTable`(封装分页/排序/工具栏/列设置)+ 表单弹窗/抽屉;树+表页 = `TreeFilter` + `ProTable`。这些封装都在 `components/`,逻辑在 `composables/`,页面只做装配。

**React 版对齐**:同样的分层语义(`api`/`hooks`(≈composables)/`components`/`layouts`/`router`/`stores`),
组件范式换成 shadcn/ui + TanStack Table/Query,共享同一份 tokens 与 `openapi.json`。

---

## 8. 工程与开源基建

- **CI(后端)**:GitHub Actions —— push: build + test;tag `v*`: pack + push nuget.org;附带产出 `openapi.json` artifact。
- **CI(前端)**:lint + type-check + build。
- **测试**:`TenonAdmin.Tests`(xunit + `WebApplicationFactory` 集成测试),v1.0 至少覆盖:
  1. 认证全流程(登录/刷新/锁定);
  2. 数据范围(不同角色查同一接口得到不同数据集);
  3. **可重写机制本身**(产品承诺,必须回归锁死),验收用例名直接写死:
     - `ReplaceService_ShouldUseUserImplementation`
     - `OverrideAuthStep_ShouldAffectLoginFlow`
     - `DisabledModule_ShouldRemoveBuiltInController`
     - `CustomController_ShouldOwnSameRouteAfterModuleDisabled`
     - `CustomSeedData_ShouldRunOnceAndBeIdempotent`
     - `DataScope_ShouldFilterByCurrentUserOrg`
  4. 数据库矩阵:CI 至少跑 **SQLite + MySQL**(官方还支持 SqlServer/PostgreSQL,本地/发布前抽测)。
- **提交规范**:统一 Conventional Commits(monorepo 内后端/前端按 scope 区分,如 `feat(web):`)。
- **文档**:后端仓 `docs/`:快速开始、配置全表(§3.2)、覆写指南(§5)、模块 API 说明;v1.0 后再考虑文档站。
- **Demo**:docker-compose 一键起(后端 + Vue 版);发布时附默认账号。
- **License**:纯 Apache-2.0(见 §17)。

---

## 9. 里程碑与任务拆解

### M0 —— 立项(建仓周)✅ 完成(2026-07-06)
- [x] 定名:TenonAdmin(2026-07,NuGet 验证空闲)
- [x] 建仓:https://github.com/DotNet-MoYu/TenonAdmin (Apache-2.0;开发走 dev 分支);基础 CI 与 NuGet 前缀保留待发首包时补
- [x] §2.3 依赖处置清单定稿
- [x] 本文档迁入本仓 `docs/`
- [x] `DESIGN.md` 定稿(含 tokens 文件初版;来源见 §7.1 设计流水线)—— 2026-07-07 落地:`web/DESIGN.md` + `web/src/styles/tokens.css`(单源,亮/暗双主题)+ token→Naive 映射;视觉出自 Claude Design,留档 `web/design-mockups/design-tokens.dc.html`

### M1 —— 后端骨架(核心里程碑)✅ 完成(T1–T10 全绿 + Phase 2 加固;细粒度进度见 `dev-plan.md`)
- [x] `Core`:实体基类 / Result / 异常 / 扩展点接口 / 雪花 ID / Channels 事件总线(`IEventBus`+`ChannelEventBus`,T5)
- [x] `SqlSugar`:单例封装 / 仓储 / CodeFirst / 种子机制
- [x] `Services`:认证 / RBAC / 用户/机构/职位/角色/菜单 / 字典 / 配置 / **日志(T6)/ 上传(T7)** 全套
- [x] `AspNetCore`:AddTenonAdmin/MapTenonAdmin + JWT + 权限过滤器 + **统一返回(T8a)/ 数据范围(T3)/ 日志过滤器(T6)/ OpenAPI(T8d-i)/ 健康检查 / CORS / 限流(Phase 2b)**
- [x] `samples/MinimalHost` 三行启动跑通(§3.1 即验收标准;实跑冒烟含登录/401/健康/限流)
- [x] 测试:xunit 工程(`TenonAdmin.Tests`)+ 认证全流程 / 数据范围 / §8 可替换六件套 / 授权与限流回归;**CI 双腿(SQLite+MySQL)均绿**
> Phase 2:7 维多代理自审 34 发现全处置(12 P1 全修 + 22 P2 收敛,报告 `docs/phase2-review.md`);测试 37→65。

### M2 —— Vue 版(Naive UI 单套)
- [ ] 工程搭建 + tokens 接入 + 布局/菜单/动态路由框架(§7.4)
- [ ] `composables` 逻辑层 + `ProTable`/`SearchForm` 等通用组件;登录 → 动态路由 → `v-auth`
- [ ] §7.3 全部页面(Naive 地道写法);openapi-typescript 生成 API 层(§13.6);i18n(zh-CN/en-US)接入

### M3 —— v1.0 发布准备(NuGet 打包提前落,其余待 M2 后)
- [ ] 文档补全(快速开始/配置/覆写指南/自建模块走查 §5.7/i18n §13/安全 §14)
- [ ] Docker:后端多阶段镜像 + docker-compose demo(§11)
- [ ] `openapi.json` 归档为发布产物
- [x] NuGet 预发布打包(T10):5 包 0.0.1-preview + tag→nuget.org 发布流水线 + 洁净消费者端到端验证(**首次真推待仓库 Secrets 配 `NUGET_API_KEY`**);Vue 前端 tag / README 宣传物料待 M2 后
- [ ] 走一遍 §18 最小验收闭环

### v1.x 路线(发布后按需)
**拆出 `tenon-admin-web-react` 独立仓 + React 版模板** → **SoybeanUI 皮肤**(补视图层)→ **分片上传** → IP 地理/UA 精解 → 代码生成 → Excel 导入导出 →
任务调度 → 消息中心 → OSS 存储 → 国密 → MQTT → 可观测性

### 9.1 优先级(P0/P1/P2)

- **P0(没有它项目不成立)**:三行启动 / 默认 SQLite / 默认超管 / 登录 / RBAC / 多机构数据权限 / Vue 登录到菜单闭环 / OpenAPI / 核心测试(可重写三件套 + 数据范围)/ NuGet 打包。
- **P1(v1.0 应该有)**:字典 / 系统配置 / 操作日志 / 登录日志 / 本地上传 / Docker demo / i18n / 限流 / 健康检查。
- **P2(后置 v1.x)**:React / SoybeanUI 皮肤 / 分片上传 / 非竖向布局 / MQTT / Excel / Minio / 国密 / 代码生成 / 任务调度 / OpenTelemetry / IP 地理 / UA 精解。

---

## 10. 风险与开放问题

| 项 | 说明 | 处理 |
|---|---|---|
| ~~项目命名~~ | 已解决:TenonAdmin | NuGet ID(TenonAdmin / .Core / .Admin 变体)与 GitHub 组织 tenon-admin 已验证空闲(2026-07),建组织时尽快占位 |
| SqlSugar 版本策略 | 唯一第三方核心依赖,其大版本升级可能破坏 CodeFirst 行为 | 锁定次版本,升级走独立 PR + 集成测试 |
| 前端维护成本 | v1 只有 Vue 一套(单仓);React v1.x 才加 | v1 无双前端负担;React 拆分后靠契约单源 + DESIGN.md 把成本压在纯 UI 层,滞后一个里程碑可接受 |
| monorepo 双工具链 CI | 一仓两套构建(dotnet + npm) | CI 按路径触发(`src/**` 跑后端、`web/**` 跑前端)+ 分 job;发版时后端 pack、前端 tag 各走各的 |
| Vue 二皮肤(v1.x) | 将来补 SoybeanUI 皮肤时,若逻辑没沉进 composables 就会变成重写 | v1 起就强制"逻辑/视图分离"+ lint 约束共享层不含 UI 类型(§7.2);补皮肤只写视图 |
| 验证码零依赖 | SVG 验证码安全强度低于图片扭曲 | 默认够用(配合登录锁定);高要求场景走 `ICaptchaProvider` 扩展点,文档明示 |
| 轮询实时性 | HTTP 轮询有延迟、频繁拉增加负载 | 通知/在线用 ETag + 合理间隔(如 30s)够用;要低延迟推送切 MQTT 可选包(§12) |
| typeui MCP 外部可用性 | 设计流水线依赖外部 MCP,可能未接入/变更 | typeui 仅用于"起点主题",产物是自持的 CSS tokens;缺失时退回 pencil MCP / awesome-design-md / 手写 tokens,不阻塞实现 |
| 旧版用户迁移 | 全新产品,数据库结构会变 | **明确不支持**从旧 SimpleAdmin 迁移;不提供迁移脚本,面向全新部署(§0 决策) |

---

## 11. Docker 发布

系统一等公民地支持容器化,用户不装 .NET SDK 也能起。

- **后端多阶段 `Dockerfile`**(放 `backend/samples/MinimalHost`,同时作为用户项目的模板):
  ```dockerfile
  FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
  WORKDIR /src
  COPY backend/ .
  RUN dotnet publish samples/MinimalHost -c Release -o /app
  
  FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
  WORKDIR /app
  COPY --from=build /app .
  RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
  USER appuser                       # 非 root 运行
  EXPOSE 8080
  ENV ASPNETCORE_HTTP_PORTS=8080
  # 不在 Dockerfile 里放 HEALTHCHECK CMD curl —— aspnet 镜像默认不带 curl,会导致健康检查恒失败
  ENTRYPOINT ["dotnet", "MinimalHost.dll"]
  ```
  健康检查交给编排层:`docker-compose.yml` 用 `test: ["CMD","wget","-qO-","http://localhost:8080/health"]`
  或 k8s `livenessProbe/readinessProbe` 直接探 `/health`(§12 已内置端点)。
- **`docker-compose.yml`**:后端 + Vue 前端(nginx 托管静态产物)+ 可选 Redis + 可选 MySQL;
  一条 `docker compose up` 起全栈 demo。数据库默认仍用容器内 SQLite 卷,换 MySQL 只需改 compose + 环境变量。
- **配置走环境变量**:`appsettings` 的 `TenonAdmin__Database__ConnectionString` 等用双下划线映射,
  compose/生产 K8s 直接注入,镜像不打包任何密钥。
- **镜像发布**:GitHub Actions 在 tag 时构建并推 **GHCR**(`ghcr.io/dotnet-moyu/tenon-admin`),
  可选同步 Docker Hub;多架构 `linux/amd64,linux/arm64`。
- **数据卷**:`./data`(SQLite/JWT 开发密钥)、`./wwwroot/upload`(本地上传)声明为卷,避免容器重建丢数据。

---

## 12. 其他考量(补漏)

以下为发布前需要成型、但不改变主架构的横切项,大多用 .NET 内置件即可,不新增第三方运行时依赖。

| 项 | 方案 | 归属 |
|---|---|---|
| **i18n / 多语言** | 独立设计,见 §13 | Core + AspNetCore + 前端,v1 |
| **健康检查** ✅ | 内置 `HealthChecks`,`/health`(存活)+ `/health/ready`(依赖:DB/缓存),匿名供编排层探针 | AspNetCore,已落(Phase 2b) |
| **限流** ✅ | .NET 内置 `RateLimiter`,按客户端 IP 固定窗口,认证端点更严(`Security:RateLimit`);经 `IStartupFilter` 挂 `UseRateLimiter`,命中出 429 信封(40008) | AspNetCore,已落(Phase 2b) |
| **软删除 + 审计** ✅ | `BaseEntity` 带 `IsDelete`/`CreateTime`/`CreateUserId`/`UpdateTime`/`UpdateUserId`,SqlSugar 全局过滤器 + `AOP` 自动填充(含 `DataEntity.CreateOrgId` 从令牌 org claim 回填) | Core + SqlSugar,已落 |
| **API 版本化** | 路由前缀内置版本段(`/api/v1/...`);`RoutePrefix`/`Version` 配置化**后置 v1.x**(深耦合权限码与菜单种子),v1 固定 `api/v1` | AspNetCore,v1 固定 / 配置化 v1.x |
| **CORS / 上传限制 / 请求体大小** ✅ | CORS 走 `Api:Cors`(命名策略 + `IStartupFilter` 挂载,默认收紧);上传走 `Upload:MaxSizeMb` + 后缀白名单 | AspNetCore,CORS 已落(Phase 2b) |
| **在线用户 / 站内通知** | **不用 SignalR**。在线用户 = 令牌信息本就存缓存(参考旧版 `CACHE_USER_TOKEN`),直接列举/强退,无需长连接。实时性:v1 用 **HTTP 轮询**(前端定时拉未读/在线,`If-None-Match`/ETag 省流);需要推送再用 **MQTT**(可选包 `TenonAdmin.Mqtt`,前端走 mqtt.js/WebSocket)。**v1.0 只暴露 Polling**(`Notify:Mode=Polling`);`Mqtt` 模式由 v1.x 可选包 `TenonAdmin.Mqtt` 提供,v1.0 配置不暴露该值 | AspNetCore,v1(轮询)/ 可选包(MQTT,v1.x) |
| **配置热更新** | 服务读配置用 `IOptionsMonitor<T>`,改 json / 环境不必重启 | 全局约定 |
| **生产建表安全 / DB 策略** | Dev 默认允许 CodeFirst 建表+加列;**生产默认禁**,显式 `Database:EnableCodeFirstInProduction` 才开;内置表用 `sys_schema_version` 记版本;**不做破坏性迁移**(不自动删表/删列/改窄字段);发布说明列出每版表结构变更 | SqlSugar,v1 |
| **种子幂等** | `ISeedData` 以主键/唯一键判存,重复启动不重复插入 | SqlSugar,v1 |
| **可观测性** | 默认只用内置 `ILogger`;OpenTelemetry(logs/metrics/traces)做 **可选包** `TenonAdmin.Observability` | 可选包,v1.x |
| **统一时间/ID** | 时间用 `TimeProvider`(.NET 内置,可测试);ID 用自写雪花(`IIdGenerator`) | Core,v1 |

---

## 13. 多语言(i18n)设计

目标:前后端都能多语言,且**语言资源单源、可被用户扩展/覆盖**,不散落硬编码文案(呼应 §6)。

### 13.1 分工:谁负责翻译什么
- **前端负责全部"界面文案"**(菜单、按钮、表单标签、列头、提示语)——这些后端根本不该知道。
- **后端只负责"消息文案"**(校验失败、业务错误、操作结果)——但**后端不返回死中文**,而是返回**错误码 + 参数**,由前端映射成本地化文案。
- 好处:文案**单源在前端**,后端零翻译负担;切语言纯前端行为,不用重发请求。

### 13.2 后端:错误码而非文案

**错误码分段**(数字段位 + 语义 key 双轨,纯数字 key 可维护性差,必须配语义 key):

| 段 | 用途 |
|---|---|
| 0 | 成功 |
| 40000–40999 | 认证与登录 |
| 41000–41999 | 权限与数据范围 |
| 42000–42999 | 用户/组织/角色/菜单 |
| 43000–43999 | 字典/配置 |
| 44000–44999 | 文件上传 |
| 50000–50999 | 系统内部错误 |

```csharp
public enum ErrorCode { PasswordWrong = 40001, CaptchaExpired = 40002, UserNotFound = 42001 }
throw new AdminException(ErrorCode.PasswordWrong);          // 只抛码,不写中文
```
统一返回结构(`code` 给机器、`msgKey` 给前端 i18n、`message` 是后端兜底降级):
```jsonc
{ "code": 40001, "msgKey": "error.auth.passwordWrong", "args": {}, "message": "密码错误", "data": null }
```
- 后端**可选**内置默认多语言资源(`.resx`/json,`IStringLocalizer`),按 `Accept-Language` 兜底填 `message`,给非浏览器调用方(第三方直接调 API)。
- 浏览器端优先用 `msgKey` 走前端翻译,`message` 只作降级。

### 13.3 前端:vue-i18n(React 端 react-i18next)
```
locales/
├─ zh-CN/  { "menu.user": "用户管理", "error.auth.passwordWrong": "密码错误", "error.user.notFound": "用户 {name} 不存在" }
├─ en-US/  { ... }
└─ index.ts   # 按需懒加载语言包
```
- 请求层拦截:拿到 `{msgKey, args}` → `t(msgKey, args)` → 弹本地化提示(**用语义 key,不用 `error.${code}` 纯数字**)。
- 语言持久化到 `stores/app` + localStorage;组件库(Naive UI)的内建语言包随之切换。

### 13.4 动态内容(数据库里的文本)怎么办
菜单名、字典项、系统配置这类**存在库里的文案**,两种策略,按需选:
- **默认(推荐,简单)**:库里存"翻译键",前端用 `t(key)` 渲染;键不存在就回退显示原文。零表结构改动。
- **进阶(可选)**:内置多语言表 `sys_i18n(resourceKey, culture, value)`,后台可维护;适合运营要在线改文案的场景。v1 先做默认策略,进阶列 v1.x。

### 13.5 语言清单
v1 内置 `zh-CN` + `en-US`;新增语言 = 丢一个语言包文件夹,前端自动识别,用户可自行扩展或覆盖内置键。

### 13.6 OpenAPI → 前端代码生成(契约单源工具链)
- 定 **`openapi-typescript`(生成类型)+ `openapi-fetch`(轻量类型化请求客户端)**——依赖最轻,合"少依赖"理念;前端**不手写 DTO/接口类型**,人工只写业务 hooks/composables。
- 前端一条 `npm run gen:api`。
- **契约纪律**:**v1 单仓内前后端同源,`openapi.json` 本地产出即用,无跨仓漂移问题**;跨仓固定 tag 的纪律待 React v1.x 拆分后适用——那时后端 CI 把 `openapi.json` 作为发布产物归档,React 仓**固定拉某个后端 tag**、不追 main。

---

## 14. 安全基线

v1.0 必须成型的安全项(多数为 .NET 内置能力):

- **密码哈希**:PBKDF2(BCL `Rfc2898DeriveBytes`),存储格式含**算法版本 + 迭代次数 + 盐 + hash**,便于将来平滑升级算法。
- **Refresh Token**:数据库/缓存持久化(**存 hash 不存明文**),支持**轮换、吊销、复用检测**(旧 refresh 被重放即判风险、吊销该会话)。
- **JWT**:生产**必须**配置 `SecretKey`;开发密钥只允许 Development 环境自动生成(带醒目警告)。
- **登录防护**:验证码 + 登录失败锁定 + `RateLimiter`(登录接口更严)。
- **文件上传**:后缀白名单 + 大小限制 + **文件名重写** + **路径穿越防护** + **不以 Content-Type 作为唯一依据**。
- **授权默认拒绝**:后台业务接口默认需认证;放行需显式 `[AllowAnonymous]` / `[IgnoreRolePermission]`。
- **敏感字段脱敏**:日志里密码、token、密钥、手机号等脱敏。
- **CORS**:默认仅允许本地开发源;生产必须显式配置。

## 15. 会话与 Token 模型

支撑"在线用户 / 强退 / 权限变更即时生效"的基础(对应 §3.2 `Security:Session`、§6 授权管道):

- **AccessToken**:短期 JWT,**不落库**。
- **RefreshToken**:服务端保存 **hash**,不保存明文。
- **SessionId**:写入 JWT claim,作为**强退与在线用户的稳定标识**。
- **缓存 key**:`tenon:session:{sessionId}` → 保存用户、设备、过期时间、状态。
- **强退**:删除/标记 session;权限过滤器**每次请求校验 session 状态**(失效即 401)。
- **Refresh 轮换**:每次刷新吊销旧 RefreshToken、签发新的。
- **单端模式**(`Session:Mode=Single`):新登录时吊销同用户其他 session。
- **`MaxConcurrent`**:超过并发端数时,按最早登录时间吊销最旧 session。

**实现要点(T4 落地)**:RefreshToken 存 **SHA-256 十六进制**(高熵随机串,非密码,无需 PBKDF2);刷新用**条件更新**(仅当仍 Active 才置 Used)原子轮换,兼作并发双刷保护;**复用检测**:已 Used 的令牌再现即吊销**整个会话**(连坐刷新令牌 + 清缓存),攻击者与真用户一起下线(安全优先)。会话热路径:`ISessionService.IsActiveAsync` 先读 `tenon:session:{sid}` 缓存(TTL=会话过期),未命中查库回填;`[RolePermission]` 管道对**超管与普通用户一律**校验会话状态(强退即 401)。刷新走**滑动续期**(会话过期跟到新刷新令牌过期)。审计字段 `CreateUserId/UpdateUserId` 由 SqlSugar AOP 从 `ICurrentUser` 自动填充(系统上下文留空)。

**部署边界(单节点 vs 多实例,重要)**:强退/权限吊销/登录锁定/验证码一次性的**即时性均依赖共享缓存**。默认 `MemoryCacheProvider` 是**进程内**:多副本负载均衡下,节点 A 的强退/降权只清了 A 的缓存,路由到节点 B 的请求仍读旧缓存(会话 TTL 可达 RefreshExpire 天级、权限/数据范围 TTL 至 `PermissionMinutes`),这些安全动作在 B 上**不即时生效**(`PermissionMinutes=0` 永不过期时 B 上永久保留旧权限)。**故:多实例部署必须配置分布式 `ICacheProvider`(如 `TenonAdmin.Caching.Redis`),否则强退/权限即时性、登录锁定、验证码一次性都只在单节点内成立。** 跨节点失效广播(利用 `IEventBus` 在副本间传播 session/perm/scope 失效)是 Redis 可选包/多实例路线的后续项。

## 16. 内置表清单(草案)

因为"不支持旧版迁移",**新表结构即长期契约,需尽早稳定**。v1.0 内置表(前缀 `sys_`):

`sys_user`、`sys_role`、`sys_menu`、`sys_org`、`sys_position`、`sys_module`、
`sys_user_role`、`sys_role_menu`、`sys_role_data_scope`、
`sys_dict_type`、`sys_dict_item`、`sys_config`、
`sys_login_log`、`sys_operation_log`、`sys_file`、
`sys_session`、`sys_refresh_token`、`sys_schema_version`。

> 多应用门户(§4/§6):`sys_module` 为模块/应用表;`sys_menu` 增 `ModuleId`(仅顶级目录挂模块)+ 前端展示列 `Path`/`Component`/`Icon`/`Visible`;`sys_user` 增 `DefaultModuleId`(每用户默认应用)。模块访问权由菜单授权反推,不建 `sys_role_module`/`sys_user_module` 派生表。

> 均继承 `BaseEntity`/`DataEntity`(§5.6);字段级设计在 M1 前定稿,定稿后视为对外契约,破坏性变更只在主版本。

## 17. 版本与发布策略

- **包版本从 `0.0.1` 起**(预发布/测试版,bug 多,不算正式);功能稳定、API 收敛后再发 `1.0.0`。SemVer。
- 所有 `TenonAdmin.*` 包**同版本发布**,不做独立版本号。
- 后端仓用 `Directory.Build.props` / `Directory.Packages.props` **统一版本与依赖版本**。
- **TFM 跟随当前 .NET LTS**(当前 `net10.0`),但**包版本独立语义化**——不采用"包主版本=.NET 主版本"(否则首发即 10.0.0,误导成熟度)。
- **breaking change 只在主版本**发生;可选包必须声明兼容的核心包版本范围(`[0.1.0,0.2.0)` 之类)。
- tag `v*` 触发 CI:pack + push nuget(预发布走 `-preview` 标签)+ 归档 `openapi.json`。

## 18. v1.0 最小验收闭环

这套端到端场景比模块清单更能指导开发——**跑通它 = v1.0 达标**:

1. 新建空 ASP.NET Core 项目;
2. 安装 `TenonAdmin` NuGet 包;
3. `Program.cs` 写三行(§3.1);
4. `dotnet run`;
5. 控制台打印首次超管账号/密码;
6. 打开 Vue 前端;
7. 登录成功;
8. 进入用户管理;
9. 新增机构、角色、用户;
10. 给角色授权菜单和数据范围;
11. 用新用户登录;
12. 验证菜单权限与数据权限均生效;
13. 修改系统配置/字典;
14. 查看操作日志与登录日志;
15. 强退某在线用户;
16. 刷新页面后动态路由仍正常。
