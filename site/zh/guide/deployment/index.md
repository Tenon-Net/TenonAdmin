# 部署：先选路线，再过安全基线

本地运行成功后，部署还需要安排前端静态文件的托管、后端服务和生产配置。开发时的 Vite 代理不会进入构建产物，所以服务器必须明确把 API 请求转发到后端。下面的示例默认使用 Vue 的 `web/`；React 使用 `web-react/`，构建产物同样在其 `dist/` 目录。

## 选一条托管路线

四条去处的区别只在两点：前端产物谁托管、前后端是不是同源。

| 路线 | 谁托管前端 | 同源 | 什么时候选它 |
|---|---|---|---|
| [路线 A：单体部署](/zh/guide/deployment/route-a) | 后端进程自己（`UseStaticFiles`） | 是 | 一个进程一个端口，内部系统最省心 |
| [路线 B：反向代理](/zh/guide/deployment/route-b) | nginx / Caddy | 是 | 已有网关，或想让 Caddy 自动签发 TLS 证书 |
| [路线 C：真跨源（CDN）](/zh/guide/deployment/route-c) | CDN / 独立域名 | 否 | 前端上 CDN，唯一要配 CORS 的路线 |
| [容器化与多副本](/zh/guide/deployment/docker) | 容器里的 Caddy | 是 | 上 Docker / K8s，或要横向扩容 |

同源省事，路线 A、B 都算。`web/dist` 默认就按同源请求后端，不用配 CORS。背后是 `src/api/client.ts` 里 `baseUrl` 留空，路径本身已经含 `/api/v1`。只有路线 C 前后端不同源，才要两端都配 CORS。

四条路线的第一步都一样，先把前端构建出来：

```bash
cd web
npm ci
npm run build     # 产物在 web/dist/
```

## 上线前必过的安全基线

先确定托管方式，再逐项配置下表。JWT 密钥等缺失会阻止启动；上传存储、代理信任与跨副本缓存还需要通过实际访问验证。

| 配置项 | 为什么必须处理 |
|---|---|
| `TenonAdmin:Jwt:SecretKey` | 生产（任何非 Development 环境）不配就**拒绝启动**，直接抛异常。只有 Development 下才会自动生成一把密钥落到 `./data/dev-jwt.key` 并打印警告。生产必须显式配置（≥32 字节随机串），且不要进版本库，改用环境变量或密钥管理服务。 |
| `TenonAdmin:Database` | 默认 SQLite `./data/admin.db`（相对 ContentRoot）。多实例、或有并发写，就换 MySQL / SqlServer / PostgreSQL（改 `DbType` + `ConnectionString` 两项）。 |
| `TenonAdmin:Id:WorkerId` | 雪花发号器的机器位。不配时同机走文件锁、共享库走 `sys_worker_lease` 领空闲槽。显式写成同一个号，后到的实例起不来。详解见[容器化与多副本](/zh/guide/deployment/docker)。 |
| `TenonAdmin:Upload:RootPath` | 默认 `./wwwroot/upload`。声明成数据卷，否则重部署丢文件；走路线 A（后端顺带托管前端）还必须把它挪出 `wwwroot`，否则上传文件会被静态中间件匿名直出。见[路线 A 的鉴权绕过警告](/zh/guide/deployment/route-a)。 |
| `TenonAdmin:Api:ForwardedHeaders` | 在任何反向代理 / 负载均衡之后都必须配。不配的话后端看到的永远是代理那一个 IP：全体用户共享一个限流桶、按 IP 的爆破防护归零、审计日志的 IP 列作废。配置细节见[路线 B](/zh/guide/deployment/route-b)。 |
| `TenonAdmin:Cache:Provider` | 单实例可留 `Memory`。多副本必须换 `Redis`，否则强制下线、撤权、登录锁定会在副本之间失效，而且一失效就是好几天。改这一项还不够：宿主项目要装 `TenonAdmin.Caching.Redis` 包，并在 `AddTenonAdmin()` **之前**调用 `AddTenonAdminRedisCache(builder.Configuration)`，两个条件缺一个就静默退回内存缓存。详解见[容器化与多副本](/zh/guide/deployment/docker)。 |

上面这些都能走环境变量，层级用双下划线（容器化部署常用）：

```bash
TenonAdmin__Jwt__SecretKey='...'
TenonAdmin__Database__DbType='MySql'
TenonAdmin__Database__ConnectionString='Server=db;Port=3306;Database=tenon;User ID=...;Password=...'
TenonAdmin__Upload__RootPath='/data/upload'
```

表外还有一项慢 SQL 告警阈值，配置键是 `TenonAdmin:Database:SlowSqlMillis`，默认 `1000` 毫秒。执行耗时超过它的语句，会连同 SQL 和参数打一条 `Warning`。失败的 SQL 则总是打 `Error`，带语句和参数，不受这项控制，也没有开关能关掉。想看全部语句就把它调小，比如 `1`，但生产上这么干会把日志淹掉。日志类别是 `TenonAdmin.Sql`，想单独调级别就调它。

## 生产建表闸门：首次建表与升级补列

生产环境有一道建表安全闸门。只要 `ASPNETCORE_ENVIRONMENT=Production`，哪怕 `EnableCodeFirst=true`（默认就是 true），也不会自动建表或改表。生产库通常由 DBA 手工维护，应用不该擅自 `ALTER`。想放行，显式打开这一项：

```json
{ "TenonAdmin": { "Database": { "EnableCodeFirstInProduction": true } } }
```

它默认 false，管两件事：

- **空库首次上生产**：表还没建，种子无处可写。这时候两条路二选一。要么临时打开这项，让它自己建表、写种子，建完可以再关掉。要么让 DBA 照启动错误里点名的表先建好，再启动。
- **升级内核版本补列**：新版内核可能给自己的表加列。加字段是常态，删列或改窄它不会做。同样两条路：本次启动打开这项，让它自己补列，CodeFirst 只加列、不删列、不改窄，仍应先备份并验证迁移；或者让 DBA 照错误里点名的表和列，手工 `ALTER TABLE ... ADD COLUMN`。

::: tip 演进列为什么是可空的
内核给**已有表**加的字段一律使用可空数据库列（`IsNullable`）。MSSQL 无法对「表里已有数据」直接 `ADD` 无默认值的 `NOT NULL` 列；可空补列后，旧行是 `NULL`，业务读侧按默认语义处理（例如 MFA 标志读为 false，绝对过期回退到会话 `ExpiresAt`）。新属性可以用 `T?` 表达该语义，但已发布的公共属性保留原 CLR 类型，并用回归测试锁住 ORM 的默认值物化。DBA 手工补列时也请加可空列，不要自作主张加 `NOT NULL` 除非同时写了 `DEFAULT` 并回填旧行。
:::

::: warning 没放行会在启动时点名报错，这是故意的
启动检查会指出缺失的表或列。例如，缺表时包含 `种子要写的表在库中不存在`，缺列时包含 `库表结构落后于当前实体`。根据日志先补齐结构，再重新启动，避免错误延迟到业务请求中出现。

检查范围仅包括缺列，不涵盖类型、长度或可空性变化。数据库迁移仍需单独审查，不能把通过启动检查当作完整的结构兼容性验证。
:::

首次写种子时，如果没有显式配 `TenonAdmin:Seed:AdminPassword`，控制台会打印一次随机超管密码，16 位，仅这一次显示，记得留存。想固定账号密码，把它配上就行。

### 升级时种子数据怎么处理

种子默认只插不改，判存靠主键。所以内核**新增**的种子行，比如新菜单、新配置项，升级后会自动流进你的库，不用管。内核**改动已有行**是另一回事，比如把某个权限按钮挪到别的页面下、给内置模块补图标。这类改动由 `sys_schema_version` 的版本闸门驱动。内核 bump 了种子版本，下次启动就把菜单树和模块这两张结构表的内置行刷回新结构，再写回版本号。

::: warning 你对内置菜单的改动会被升级刷回
升级执行内置结构种子同步时，你对内置菜单标题、排序和图标的修改可能被框架值覆盖。自建菜单不属于这批内置行。配置、字典、用户和角色授权按各自的数据维护规则处理；上线前应查看目标版本的迁移说明并备份。
:::

## 上线后自检

先用以下请求检查进程、依赖和 API 路由，再登录前端验证实际业务访问：

```bash
curl https://<你的域名>/health         # Healthy:进程存活
curl https://<你的域名>/health/ready   # Healthy:数据库 + 缓存都连得上
curl -i https://<你的域名>/api/v1/ping # 401:API 路由通了(该端点需要登录)
```

`/health` 和 `/health/ready` 语义不同，别探错。`/health` 只看进程本身还在不在响应，对应 k8s 的 livenessProbe、进程级重启。`/health/ready` 会真去连数据库和缓存，对应 readinessProbe、负载均衡摘节点。要判断能不能接流量，探后者。

再打开前端登录一次，能拿到菜单就说明 JWT 密钥、数据库、种子数据全对上了。

最后提一个容易误报的点。`/openapi/v1.json` 在生产返回 404 是预期行为，不是部署漏了什么。它只在 Development 环境挂载，是给前端 `npm run gen:api` 用的契约源，不是生产端点。

## 版本回滚

回滚前先确认旧版本能否读取当前数据库结构和数据，并准备数据库与上传文件的备份。NuGet 应用需要重新部署使用旧包的构建产物；容器部署需要切回旧镜像。框架的加列策略有助于兼容旧代码，但不能保证业务迁移、种子变更或外部服务变更都可逆。应在测试环境验证回滚，再用于生产。

真正回不去的是发布这一步本身。tag 一推送就已经触发 `backend-release` 往 nuget.org 发包，包只能 unlist，不能删除。完整节奏写在[更新日志](/zh/changelog)和[发布流程](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/releasing.md)里。回滚退的是你自己部署的那个实例，退不掉已经发出去的包。
