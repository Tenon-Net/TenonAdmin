# 部署：先选路线，再过安全基线

你已经用 `dotnet new tenon-app`（或三行 `Program.cs`）在本地跑通了，现在要发到服务器上。`npm run dev` 一切正常，是因为 Vite dev server 把 `/api`、`/openapi` 反代到了后端（`web/vite.config.ts`）——这层代理只在开发期存在。构建产物 `web/dist` 是一堆静态文件，谁来托管它、它怎么找到后端，是上线时必须回答的两个问题。这页先帮你按这两个问题挑一条托管路线，再把无论哪条路线都躲不过的安全基线过一遍。

## 选一条托管路线

四条去处的区别只在两点：前端产物谁托管、前后端是不是同源。

| 路线 | 谁托管前端 | 同源 | 什么时候选它 |
|---|---|---|---|
| [路线 A：单体部署](/zh/guide/deployment/route-a) | 后端进程自己（`UseStaticFiles`） | 是 | 一个进程一个端口，内部系统最省心 |
| [路线 B：反向代理](/zh/guide/deployment/route-b) | nginx / Caddy | 是 | 已有网关，或想让 Caddy 自动签发 TLS 证书 |
| [路线 C：真跨源（CDN）](/zh/guide/deployment/route-c) | CDN / 独立域名 | 否 | 前端上 CDN，唯一要配 CORS 的路线 |
| [容器化与多副本](/zh/guide/deployment/docker) | 容器里的 Caddy | 是 | 上 Docker / K8s，或要横向扩容 |

同源（A、B）省事：`web/dist` 默认按同源请求后端（`src/api/client.ts` 的 `baseUrl` 为空，路径已含 `/api/v1`），不用配 CORS。只有路线 C 前后端不同源，才需要两端都配 CORS。

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

无论选哪条路线，下面这些在生产都躲不过。其中几条内核是「不满足就拒绝启动」，不会带着隐患把进程跑起来。

| 配置项 | 为什么必须处理 |
|---|---|
| `TenonAdmin:Jwt:SecretKey` | 不配 = 开发密钥模式：内核自动生成一把密钥落到 `./data/dev-jwt.key` 并打印警告。生产必须显式配置（≥32 字节随机串），且不要进版本库——用环境变量或密钥管理服务。 |
| `TenonAdmin:Database` | 默认 SQLite `./data/admin.db`（相对 ContentRoot）。多实例、或有并发写，就换 MySQL / SqlServer / PostgreSQL（改 `DbType` + `ConnectionString` 两项）。 |
| `TenonAdmin:Id:WorkerId` | 雪花发号器的机器位。单实例不配即可（回落 0）;水平扩展时每个副本必须各不相同（0–63），否则同毫秒发号会撞主键。配了 Redis 却不显式给它会直接拒绝启动——详解见[容器化与多副本](/zh/guide/deployment/docker)。 |
| `TenonAdmin:Upload:RootPath` | 默认 `./wwwroot/upload`。声明成数据卷，否则重部署丢文件;走路线 A（后端顺带托管前端）还必须把它挪出 `wwwroot`，否则上传文件会被静态中间件匿名直出——见[路线 A 的鉴权绕过警告](/zh/guide/deployment/route-a)。 |
| `TenonAdmin:Api:ForwardedHeaders` | 在任何反向代理 / 负载均衡之后都必须配。不配的话后端看到的永远是代理那一个 IP：全体用户共享一个限流桶、按 IP 的爆破防护归零、审计日志的 IP 列作废。配置细节见[路线 B](/zh/guide/deployment/route-b)。 |
| `TenonAdmin:Cache:Provider` | 单实例可留 `Memory`。多副本必须换 `Redis`，否则强制下线、撤权、登录锁定会在副本之间失效，而且一失效就是好几天。详解见[容器化与多副本](/zh/guide/deployment/docker)。 |

上面这些都能走环境变量，层级用双下划线（容器化部署常用）:

```bash
TenonAdmin__Jwt__SecretKey='...'
TenonAdmin__Database__DbType='MySql'
TenonAdmin__Database__ConnectionString='Server=db;Port=3306;Database=tenon;User ID=...;Password=...'
TenonAdmin__Upload__RootPath='/data/upload'
```

表外还有一项 `TenonAdmin:Database:SlowSqlMillis`（慢 SQL 告警阈值，默认 `1000` 毫秒）：执行耗时超过它的语句连同 SQL 与参数打一条 `Warning`;失败的 SQL 总是打 `Error`（带语句与参数），不受这项控制，也没有关掉它的开关。想观察全部语句可以调小（如 `1`），但生产上会把日志淹掉。日志类别是 `TenonAdmin.Sql`，想单独调级别就调它。

## 生产建表闸门：首次建表与升级补列

生产环境有一道建表安全闸门：`ASPNETCORE_ENVIRONMENT=Production` 时，即便 `EnableCodeFirst=true`（默认就是 true）也不会自动建表 / 改表——生产库通常 DBA 手工维护，应用不该擅自 `ALTER`。要放行，显式打开这一项：

```json
{ "TenonAdmin": { "Database": { "EnableCodeFirstInProduction": true } } }
```

它默认 false，管两件事：

- **空库首次上生产**：表还没建，种子无处可写。要么临时打开这项让它自己建表 + 写种子（建成之后可以再关掉），要么 DBA 按启动错误里点名的表先建好再启动。
- **升级内核版本补列**：新版内核可能给自己的表加列（加字段是常态，删列 / 改窄不会做）。要么本次启动打开这项让它补（CodeFirst 只加列、不删列、不改窄，对存量数据是安全的），要么 DBA 按错误里点名的表和列手工 `ALTER TABLE ... ADD COLUMN`。

::: warning 没放行会在启动时点名报错——这是故意的
两种场景都不会带病启动：空库缺表抛点名到表的错误（`...种子要写的表在库中不存在:sys_schema_version, ...`），升级缺列抛点名到列的错误（`库表结构落后于当前实体,以下表缺少列:sys_user(Avatar)`），照它说的二选一即可。之所以宁可启动就炸，是因为放行的话进程会正常起来，直到第一次查到那张表才炸在驱动层的「列不存在」上——那种错误没有表名、没有列名，谁也不知道该 ALTER 什么。守卫只查缺列，不查类型 / 长度 / 可空性的变化：DBA 有意把 `varchar` 放宽、或加了自己的列，都不会被判死。
:::

首次写种子时，如果没有显式配 `TenonAdmin:Seed:AdminPassword`，控制台会打印一次随机超管密码（16 位，仅这一次显示），注意留存。想固定账号密码，就把它配上。

### 升级时种子数据怎么处理

种子默认只插不改（按主键判存），所以内核**新增**的种子行（新菜单、新配置项）升级后自动流进你的库，不用管。内核**改动已有行**（把某个权限按钮挪到别的页面下、给内置模块补图标）由 `sys_schema_version` 的版本闸门驱动：内核 bump 了种子版本，下次启动就把菜单树和模块这两张结构表的内置行刷回新结构，再写回版本号。

::: warning 你对内置菜单的改动会被升级刷回
你在菜单管理页对**内置菜单**改的标题 / 排序 / 图标，会在内核升级时被刷回内核的值（这些行归内核所有）。你自己新增的菜单不受影响。配置中心（`sys_config`）、字典、用户、角色授权是你的数据，升级一行都不碰。
:::

## 上线后自检

三条 curl 就能确认全链路通了：

```bash
curl https://<你的域名>/health         # Healthy:进程存活
curl https://<你的域名>/health/ready   # Healthy:数据库 + 缓存都连得上
curl -i https://<你的域名>/api/v1/ping # 401:API 路由通了(该端点需要登录)
```

`/health` 和 `/health/ready` 语义不同，别探错：`/health` 只看进程本身还在不在响应（对应 k8s 的 livenessProbe、进程级重启）;`/health/ready` 会真去连数据库和缓存（对应 readinessProbe、负载均衡摘节点）。要判断「能不能接流量」，探后者。

再打开前端登录一次，能拿到菜单就说明 JWT 密钥、数据库、种子数据全对上了。

最后一个容易误报的点：`/openapi/v1.json` 在生产返回 404 是预期行为，不是部署漏了什么。它只在 Development 环境挂载，是给前端 `npm run gen:api` 用的契约源，不是生产端点。
