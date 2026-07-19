# 部署：先选路线，再过安全基线

你已经用 `dotnet new tenon-app`（或者三行 `Program.cs`）在本地跑通了，现在要发到服务器上。开发期一切正常，其实靠的是一层你可能没留意的代理。`npm run dev` 底下，Vite dev server 把 `/api`、`/openapi` 反代给了后端，这段代理配置在 `web/vite.config.ts` 里。它只在开发期存在，上线就没有了。构建产物 `web/dist` 只是一堆静态文件。谁来托管它？它又怎么找到后端？这两个问题决定了你怎么部署。下面先给四条托管路线，挑一条走。挑完还有一道安全基线，哪条路线都绕不过。

## 选一条托管路线

四条去处的区别只在两点：前端产物谁托管、前后端是不是同源。

| 路线 | 谁托管前端 | 同源 | 什么时候选它 |
|---|---|---|---|
| [路线 A：单体部署](/zh/guide/deployment/route-a) | 后端进程自己（`UseStaticFiles`） | 是 | 一个进程一个端口，内部系统最省心 |
| [路线 B：反向代理](/zh/guide/deployment/route-b) | nginx / Caddy | 是 | 已有网关，或想让 Caddy 自动签发 TLS 证书 |
| [路线 C：真跨源（CDN）](/zh/guide/deployment/route-c) | CDN / 独立域名 | 否 | 前端上 CDN，唯一要配 CORS 的路线 |
| [容器化与多副本](/zh/guide/deployment/docker) | 容器里的 Caddy | 是 | 上 Docker / K8s，或要横向扩容 |

同源省事，路线 A、B 都算。`web/dist` 默认就按同源请求后端，不用配 CORS。背后是 `src/api/client.ts` 里 `baseUrl` 留空，路径本身已经含 `/api/v1`。只有路线 C 前后端不同源，才要两端都配 CORS。

::: tip 旧「路线 D」去哪了
Docker 那条路线连同多副本部署已经并进[容器化与多副本](/zh/guide/deployment/docker)一篇，不再单列「路线 D」。上容器直接看那页。
:::

四条路线的第一步都一样，先把前端构建出来：

```bash
cd web
npm ci
npm run build     # 产物在 web/dist/
```

## 上线前必过的安全基线

无论选哪条路线，下面这些在生产都躲不过。有几条内核认死理，不满足就拒绝启动，绝不带着隐患把进程跑起来。

| 配置项 | 为什么必须处理 |
|---|---|
| `TenonAdmin:Jwt:SecretKey` | 生产（任何非 Development 环境）不配就**拒绝启动**，直接抛异常。只有 Development 下才会自动生成一把密钥落到 `./data/dev-jwt.key` 并打印警告。生产必须显式配置（≥32 字节随机串），且不要进版本库，改用环境变量或密钥管理服务。 |
| `TenonAdmin:Database` | 默认 SQLite `./data/admin.db`（相对 ContentRoot）。多实例、或有并发写，就换 MySQL / SqlServer / PostgreSQL（改 `DbType` + `ConnectionString` 两项）。 |
| `TenonAdmin:Id:WorkerId` | 雪花发号器的机器位。单实例不配即可（回落 0）；水平扩展时每个副本必须各不相同（0–63），否则同毫秒发号会撞主键。配了 Redis 却不显式给它会直接拒绝启动。详解见[容器化与多副本](/zh/guide/deployment/docker)。 |
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
- **升级内核版本补列**：新版内核可能给自己的表加列。加字段是常态，删列或改窄它不会做。这时也是二选一。要么本次启动打开这项，让它自己补列，CodeFirst 只加列、不删列、不改窄，对存量数据是安全的。要么让 DBA 照错误里点名的表和列，手工 `ALTER TABLE ... ADD COLUMN`。

::: warning 没放行会在启动时点名报错，这是故意的
两种场景都不会带病启动，报的错都点到了名。空库缺表，抛的错点名到表：`...种子要写的表在库中不存在:sys_schema_version, ...`。升级缺列，抛的错点名到列：`库表结构落后于当前实体,以下表缺少列:sys_user(Avatar)`。照它说的二选一就行。为什么宁可启动就炸？因为一旦放行，进程会正常起来，直到第一次查到那张表，才炸在驱动层的「列不存在」上。那种错误没有表名，也没有列名，谁都不知道该 ALTER 什么。守卫只查缺列，不查类型、长度、可空性的变化。DBA 有意把 `varchar` 放宽，或者加了自己的列，都不会被判死。
:::

首次写种子时，如果没有显式配 `TenonAdmin:Seed:AdminPassword`，控制台会打印一次随机超管密码，16 位，仅这一次显示，记得留存。想固定账号密码，把它配上就行。

### 升级时种子数据怎么处理

种子默认只插不改，判存靠主键。所以内核**新增**的种子行，比如新菜单、新配置项，升级后会自动流进你的库，不用管。内核**改动已有行**是另一回事，比如把某个权限按钮挪到别的页面下、给内置模块补图标。这类改动由 `sys_schema_version` 的版本闸门驱动。内核 bump 了种子版本，下次启动就把菜单树和模块这两张结构表的内置行刷回新结构，再写回版本号。

::: warning 你对内置菜单的改动会被升级刷回
你在菜单管理页对**内置菜单**改过的标题、排序、图标，会在内核升级时被刷回内核的值，因为这些行归内核所有。你自己新增的菜单不受影响。配置中心（`sys_config`）、字典、用户、角色授权都是你的数据，升级一行都不碰。
:::

## 上线后自检

三条 curl 就能确认全链路通了：

```bash
curl https://<你的域名>/health         # Healthy:进程存活
curl https://<你的域名>/health/ready   # Healthy:数据库 + 缓存都连得上
curl -i https://<你的域名>/api/v1/ping # 401:API 路由通了(该端点需要登录)
```

`/health` 和 `/health/ready` 语义不同，别探错。`/health` 只看进程本身还在不在响应，对应 k8s 的 livenessProbe、进程级重启。`/health/ready` 会真去连数据库和缓存，对应 readinessProbe、负载均衡摘节点。要判断能不能接流量，探后者。

再打开前端登录一次，能拿到菜单就说明 JWT 密钥、数据库、种子数据全对上了。

最后提一个容易误报的点。`/openapi/v1.json` 在生产返回 404 是预期行为，不是部署漏了什么。它只在 Development 环境挂载，是给前端 `npm run gen:api` 用的契约源，不是生产端点。
