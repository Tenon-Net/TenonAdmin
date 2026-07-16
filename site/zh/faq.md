# 常见问题

::: tip
以下问题按主题分组,均来自内核实际行为。找不到答案,优先看报错原文(内核的报错信息通常会点名具体的配置项/表/列),再去仓库 [issue](https://github.com/Tenon-Net/TenonAdmin/issues) 搜关键字。
:::

## 前言:遇到问题怎么排查

- 先把报错信息完整看一遍。内核对可预期的配置错误(建表闸门、WorkerId 缺配等)会给出点名到具体配置项的错误,而不是一句笼统的异常。
- 区分是**内核行为**还是**消费方代码**问题:内核相关的现象可以对照本页和 [部署指南](/zh/guide/deployment/) 核实;消费方业务代码的问题(自己的 Controller/Service)不在本页范围。
- 提问时带上:.NET / Node 版本、`TenonAdmin:Database:DbType`、是单实例还是多副本部署、完整报错堆栈。

## 首次启动

### 超级管理员密码在哪看?

**问题背景**:首次启动后不知道用什么账号密码登录。

**原因**:种子只在 `sys_user` 表为空时执行一次。没有显式配置密码时,内核会生成一个 16 位随机密码,并在**这一次**启动的控制台日志里打印,格式类似:

```text
╔══════════════════════════════════════════════════════╗
║  TenonAdmin 首次启动,已创建超级管理员                  ║
║  账号: superAdmin
║  密码: xxxxxxxxxxxxxxxx
║  此密码仅本次显示,请登录后立即修改!                    ║
╚══════════════════════════════════════════════════════╝
```

**解决**:

- 忘了看这次日志 —— 密码已经写进库(哈希后),找不回明文,只能直接改库或删库重新播种。
- 想固定密码(比如 CI/自动化场景):启动前配置

```json
{ "TenonAdmin": { "Seed": { "AdminAccount": "superAdmin", "AdminPassword": "你的密码" } } }
```

`AdminPassword` 留空(默认)才会走随机生成 + 打印这条路径;库里只要已经有任意用户,种子就不会再跑,改配置也不会覆盖已存在的账号。

### `appsettings.Development.json` 去哪了,为什么找不到?

**问题背景**:clone 仓库后本地跑不起来,或者找不到这个文件。

**原因**:`appsettings.Development.json` 已被 `.gitignore` 排除 —— 它是本地开发用的凭证文件(数据库连接串、JWT 密钥等),不应进版本库。

**解决**:从旁边的 `appsettings.Development.json.example` 拷贝一份改名,按需修改。样例宿主在 `backend/samples/MinimalHost/appsettings.Development.json.example`。

## 数据库

### 怎么把默认的 SQLite 换成 MySQL / SqlServer / PostgreSQL?

**问题背景**:零配置默认用 SQLite(`./data/admin.db`),想换成正式数据库。

**原因**:数据库类型和连接串都由 `TenonAdmin:Database` 一段配置驱动,不需要改代码。

**解决**:改 `DbType` + `ConnectionString` 两项(`Sqlite` / `MySql` / `SqlServer` / `PostgreSQL` 均支持):

```json
{
  "TenonAdmin": {
    "Database": {
      "DbType": "MySql",
      "ConnectionString": "Server=127.0.0.1;Port=3306;Database=tenon;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;"
    }
  }
}
```

也可以走环境变量(双下划线分层,适合容器化部署):

```bash
TenonAdmin__Database__DbType='MySql'
TenonAdmin__Database__ConnectionString='Server=db;Port=3306;Database=tenon;User ID=...;Password=...'
```

::: warning 生产环境额外要注意
`ASPNETCORE_ENVIRONMENT=Production` 时,即便 `EnableCodeFirst=true` 也**不会**自动建表 —— 空库首次上生产要么临时打开 `EnableCodeFirstInProduction: true` 让它自己建,要么让 DBA 手工建表。详见 [部署指南 §0](/zh/guide/deployment/)。
:::

## 分布式 ID

### 多副本部署为什么会启动失败,报 `WorkerId` 相关错误?

**问题背景**:单实例跑得好好的,加了 Redis 缓存、起第二个副本后启动直接失败。

**原因**:雪花算法的 `WorkerId`(`TenonAdmin:Id:WorkerId`)决定发号器的机器位。单实例不配也没事(回落 0)。但一旦配置了 `Cache:Provider=Redis`(明显的多实例意图)却没有**显式**给出 `WorkerId`,内核会直接拒绝启动 —— 因为默默放行的话,两个副本大概率都拿 `WorkerId=0`,同一毫秒各自发号就会撞主键,而且是悄无声息地撞。

**解决**:每个副本显式配置各不相同的 `WorkerId`(取值范围 0–63):

```bash
# 副本 0
TenonAdmin__Id__WorkerId=0
# 副本 1
TenonAdmin__Id__WorkerId=1
```

Docker Compose 场景下 `--scale app=2` 给不了各副本不同的环境变量,要拆成多个显式的 `app` 服务分别配置(参照仓库根的 `docker-compose.scale.yml`);Kubernetes 场景建议用 StatefulSet,从 Pod 序号(`app-0`/`app-1`)注入。

## 前端 API 契约

### `npm run gen:api` 跑了但报错/生成的内容不对,是怎么回事?

**问题背景**:执行 `npm run gen:api` 想重新生成 `src/api/schema.d.ts`,但拿不到数据。

**原因**:这个命令是从一个**正在运行的**后端实例的 `/openapi/v1.json` 拉取契约来生成类型,不是离线生成 —— 后端没启动,这个端点就访问不到。

**解决**:先在另一个终端跑起后端(`dotnet run --project backend/samples/MinimalHost`,或者用 `dev.bat` 一键起前后端),确认能访问 `http://localhost:5100/openapi/v1.json`,再执行 `npm run gen:api`。

::: warning
`src/api/schema.d.ts` 是生成产物,**不要手改**——改了下次一 `gen:api` 就会被覆盖。要调整类型只能改后端的接口/DTO,再重新生成。
:::

### 生产环境访问 `/openapi/v1.json` 是 404,是不是部署漏了什么?

**问题背景**:线上环境请求 `/openapi/v1.json` 得到 404,以为契约丢了。

**原因**:这个端点**只在 Development 环境挂载**,它是给前端本地开发时 `npm run gen:api` 用的契约源,不是面向生产的 API,生产下 404 是预期行为,不是 bug。

**解决**:不需要处理。如果是想验证后端本身是否活着,用 `/health`(存活探针)或 `/health/ready`(数据库 + 缓存都通)。

## 跨域与代理

### 本地开发时前端请求 `/api` 为什么能通,是怎么代理到后端的?

**问题背景**:前端跑在 `:5173`,后端跑在 `:5100`,页面里发的却是相对路径 `/api/...`,没配 CORS 也能正常拿到数据。

**原因**:`npm run dev` 用的 Vite dev server 内置了反向代理(`web/vite.config.ts`),把 `/api` 和 `/openapi` 两个前缀原样转发到后端地址(默认 `http://localhost:5100`,可用环境变量 `TENON_API_TARGET` 覆盖)。浏览器眼里从头到尾只有一个源(`:5173`),自然不存在跨域问题。

```ts
// web/vite.config.ts
server: {
  port: 5173,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
}
```

**注意**:这层代理**只在开发期存在**。生产构建出的 `web/dist` 是纯静态文件,谁来托管它、请求怎么到后端,需要在部署时自己解决 —— 常见做法是后端顺带托管前端产物(同源),或 nginx/Caddy 反代(仍是同源),这两种都不需要配 CORS。只有前端和后端部署在**不同源**(比如前端上 CDN、后端在独立域名)才需要动 `TenonAdmin:Api:Cors:AllowedOrigins`。完整方案见 [路线 C:真跨源](/zh/guide/deployment/route-c)。

## 健康检查

### `/health` 和 `/health/ready` 有什么区别,该探哪个?

**问题背景**:配置容器编排的健康检查探针,不确定该打哪个端点。

**原因**:两者语义不同 ——

| 端点 | 语义 | 探测内容 |
|---|---|---|
| `/health` | 存活(liveness) | 进程本身是否还在响应 |
| `/health/ready` | 就绪(readiness) | 数据库 + 缓存连通性是否都正常 |

**解决**:进程级重启策略(比如 k8s 的 livenessProbe)打 `/health`;判断是否可以接流量(readinessProbe、负载均衡摘除节点)打 `/health/ready`。部署后自检可以两个都过一遍:

```bash
curl https://<你的域名>/health         # Healthy
curl https://<你的域名>/health/ready   # Healthy
```
