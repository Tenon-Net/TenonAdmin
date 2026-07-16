# 部署(概览)

面向**内核消费者**:你已经用 `dotnet new tenon-app`(或三行 `Program.cs`)跑通了本地开发,现在要把它发到服务器上。

`npm run dev` 之所以一切正常,是因为 Vite dev server 把 `/api`、`/openapi` 反代到了后端(`web/vite.config.ts`)——**这层代理只在开发期存在**。构建产物 `web/dist` 是一堆静态文件,谁来托管它、它怎么找到后端,是部署时必须回答的两个问题。本文给三条路线,选一条即可。

::: tip 想直接上容器
直接看 **路线 D**(本文最后)——仓库根已有 `Dockerfile` + `docker-compose.yml`,一条命令起全栈。
:::

## 0. 上线前必做(安全基线)

| 配置项 | 为什么必须改 |
|---|---|
| `TenonAdmin:Jwt:SecretKey` | 不配 = **开发密钥模式**:自动生成一把密钥落到 `./data/dev-jwt.key` 并打印警告。生产必须显式配置(≥32 字节随机串),且**不要进版本库**——用环境变量或密钥管理服务。 |
| `TenonAdmin:Database` | 默认 SQLite `./data/admin.db`。多实例 / 有并发写就换 MySQL / SqlServer / PostgreSQL。 |
| `TenonAdmin:Id:WorkerId` | **水平扩展时每个实例必须不同**(0–63),否则不同实例同毫秒发号会撞主键。单实例不配即可(回落 0);但**配了 Redis 却不显式给它 → 启动直接报错**(见「多副本部署」)。 |
| `TenonAdmin:Upload:RootPath` | 默认 `./wwwroot/upload`。声明为数据卷(否则重部署丢文件);**若走路线 A 还必须挪出 `wwwroot`**,见下方警告。 |
| `TenonAdmin:Api:ForwardedHeaders` | **在任何反向代理/负载均衡之后都必须配**。不配的话后端看到的永远是代理那一个 IP:全体用户共享一个限流桶、按 IP 的爆破防护归零、审计日志的 IP 列作废。见「反向代理之后」。 |
| `TenonAdmin:Cache:Provider` | 单实例可留 `Memory`。**多副本必须换 `Redis`** —— 否则强制下线、撤权、登录锁定全部会在副本之间失效(见「多副本部署」)。 |
| `TenonAdmin:Database:SlowSqlMillis` | 慢 SQL 告警阈值,默认 `1000`(毫秒)。失败的 SQL **总是**打 `Error`(带语句与参数),不受本项控制。日志类别是 `TenonAdmin.Sql` —— 想单独调级别就调它。 |

### 首次部署到生产:必须显式允许建表

生产环境有一道**建表安全闸门**:`ASPNETCORE_ENVIRONMENT=Production` 时,即便 `EnableCodeFirst=true` 也**不会**自动建表(生产库通常 DBA 手工维护,应用不该擅自 ALTER)。因此空库首次上生产,二选一:

```json
{ "TenonAdmin": { "Database": { "EnableCodeFirstInProduction": true } } }
```

- **让它自己建**:首启时打开上面这项(建表 + 写种子),启动成功后可以再关掉。
- **DBA 手工建**:自行建好表结构后再启动,保持该项为 false。

::: warning 空库 + Production + 没开这一项 = 启动失败
表还没建,种子无处可写。内核会在启动时探表并抛出一条点名到表的错误(`...但种子要写的表在库中不存在:sys_schema_version, ...`),照它说的二选一即可;日志里同时有一条 `已跳过 CodeFirst 自动建表` 的警告。
:::

首次启动会写种子,并在控制台**打印一次随机超管密码**——注意留存。想自己指定用 `TenonAdmin:Seed:AdminPassword`。

### 升级内核版本:补列

内核新版本可能给自己的表**加列**(加字段是常态,删列/改窄不会做)。而生产的建表闸门默认是关的,**没人替你的库补这一列**。

升级时二选一,和首次部署是同一个开关:

- **让它自己补**:本次启动把 `EnableCodeFirstInProduction` 打开,启动成功后关掉。CodeFirst **只加列,不删列、不改窄**,对存量数据是安全的。
- **DBA 手工补**:按启动错误里点名的表和列执行 `ALTER TABLE ... ADD COLUMN`,再启动。

::: warning 不补会怎样
启动时会**直接失败**,错误里点名到具体的表和列(`库表结构落后于当前实体,以下表缺少列:sys_user(Avatar)`)。这是故意的 —— 放行的话进程会**正常启动**,直到第一次查到那张表才炸在驱动层的"列不存在"上,那种错误没有表名、没有列名,谁也不知道该 ALTER 什么。

守卫只查**缺列**,不查类型/长度/可空性的变化:DBA 有意把 `varchar` 放宽、或加了自己的列,都不会被判死。
:::

### 升级内核版本:种子数据

种子默认**只插不改**(按主键判存),所以:

- 内核**新增**的种子行(新菜单、新配置项)——升级后自动流到你的库,不用管。
- 内核**改动已有行**(把某个权限按钮挪到别的页面下、给内置模块补图标)——由 `sys_schema_version` 的版本闸门驱动:内核 bump 了种子版本,下次启动就把**菜单树和模块**这两张结构表的内置行刷回新结构,然后写回版本号。

::: warning 代价
你在菜单管理页对**内置菜单**改的标题/排序/图标,会在内核升级时被刷回内核的值(内核拥有这些行)。**你自己新增的菜单不受影响。**

✅ **不会被动的**:配置中心(`sys_config`)、字典、用户、角色授权——这些是你的数据,升级**一行都不碰**。
:::

配置可以全部走环境变量,层级用双下划线:

```bash
TenonAdmin__Jwt__SecretKey='...'
TenonAdmin__Database__DbType='MySql'
TenonAdmin__Database__ConnectionString='Server=db;Port=3306;Database=tenon;User ID=...;Password=...'
TenonAdmin__Upload__RootPath='/data/upload'
```

## 1. 构建前端

```bash
cd web
npm ci
npm run build     # 产物在 web/dist/
```

`web/dist` 默认按**同源**方式请求后端(`src/api/client.ts` 的 `baseUrl` 为空,路径已含 `/api/v1`)。路线 A、B 都满足同源,因此**不需要配 CORS**;只有路线 C 才需要。

## 本节内容

- [路线 A:单体部署](/zh/guide/deployment/route-a)
- [路线 B:反向代理(nginx 或 Caddy)](/zh/guide/deployment/route-b)
- [路线 C:真跨源(CDN)](/zh/guide/deployment/route-c)
- [路线 D:Docker](/zh/guide/deployment/route-d)
- [多副本部署](/zh/guide/deployment/multi-replica)
- [部署后自检](/zh/guide/deployment/post-deploy-check)

**下一节:** [路线 A:单体部署](/zh/guide/deployment/route-a)
