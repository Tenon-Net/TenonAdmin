# 部署指南

面向**内核消费者**:你已经用 `dotnet new tenon-app`(或三行 `Program.cs`)跑通了本地开发,现在要把它发到服务器上。

`npm run dev` 之所以一切正常,是因为 Vite dev server 把 `/api`、`/openapi` 反代到了后端(`web/vite.config.ts`)——**这层代理只在开发期存在**。构建产物 `web/dist` 是一堆静态文件,谁来托管它、它怎么找到后端,是部署时必须回答的两个问题。本文给三条路线,选一条即可。

> 想直接上容器的看 **路线 D**(本文最后)——仓库根已有 `Dockerfile` + `docker-compose.yml`,一条命令起全栈。

---

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

> ⚠️ 空库 + Production + 没开这一项 = **启动失败**:表还没建,种子无处可写。内核会在启动时探表并抛出一条点名到表的错误(`...但种子要写的表在库中不存在:sys_schema_version, ...`),照它说的二选一即可;日志里同时有一条 `已跳过 CodeFirst 自动建表` 的警告。

首次启动会写种子,并在控制台**打印一次随机超管密码**——注意留存。想自己指定用 `TenonAdmin:Seed:AdminPassword`。

### 升级内核版本:补列

内核新版本可能给自己的表**加列**(加字段是常态,删列/改窄不会做)。而生产的建表闸门默认是关的,**没人替你的库补这一列**。

升级时二选一,和首次部署是同一个开关:

- **让它自己补**:本次启动把 `EnableCodeFirstInProduction` 打开,启动成功后关掉。CodeFirst **只加列,不删列、不改窄**,对存量数据是安全的。
- **DBA 手工补**:按启动错误里点名的表和列执行 `ALTER TABLE ... ADD COLUMN`,再启动。

> ⚠️ **不补会怎样**:启动时会**直接失败**,错误里点名到具体的表和列(`库表结构落后于当前实体,以下表缺少列:sys_user(Avatar)`)。这是故意的 —— 放行的话进程会**正常启动**,直到第一次查到那张表才炸在驱动层的"列不存在"上,那种错误没有表名、没有列名,谁也不知道该 ALTER 什么。
>
> 守卫只查**缺列**,不查类型/长度/可空性的变化:DBA 有意把 `varchar` 放宽、或加了自己的列,都不会被判死。

### 升级内核版本:种子数据

种子默认**只插不改**(按主键判存),所以:

- 内核**新增**的种子行(新菜单、新配置项)——升级后自动流到你的库,不用管。
- 内核**改动已有行**(把某个权限按钮挪到别的页面下、给内置模块补图标)——由 `sys_schema_version` 的版本闸门驱动:内核 bump 了种子版本,下次启动就把**菜单树和模块**这两张结构表的内置行刷回新结构,然后写回版本号。

> ⚠️ **代价**:你在菜单管理页对**内置菜单**改的标题/排序/图标,会在内核升级时被刷回内核的值(内核拥有这些行)。**你自己新增的菜单不受影响。**
>
> ✅ **不会被动的**:配置中心(`sys_config`)、字典、用户、角色授权——这些是你的数据,升级**一行都不碰**。

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

---

## 路线 A:单体部署(后端顺带托管前端)

一个进程、一个端口、同源 —— 内部系统首选。

1. 把 `web/dist/*` 拷进 host 项目的 `wwwroot/`。
2. 在你自己的 `Program.cs` 里加两行**原生 ASP.NET Core** 代码(内核不代管前端托管,因此不提供 `MapTenonAdminSpa` 之类的封装):

```csharp
var app = builder.Build();

app.UseStaticFiles();               // 托管 wwwroot 下的前端产物
app.MapTenonAdmin();                // API(必须在 fallback 之前)
app.MapFallbackToFile("index.html"); // 前端 history 路由回退:未匹配到 API 的路径交给 SPA

app.Run();
```

3. **必须同时把上传目录挪出 `wwwroot`**:

```json
{ "TenonAdmin": { "Upload": { "RootPath": "./storage/upload" } } }
```

> ⚠️ **不挪就是一个鉴权绕过**。上传根目录默认是 `./wwwroot/upload`,而上传的文件平时是通过**要鉴权**的 `GET /api/v1/sys/file/{id}/download` 取的。一旦开了 `UseStaticFiles()`,`wwwroot/upload/**` 会被静态中间件**匿名**直出——任何人猜到/拿到路径就能下载,鉴权形同虚设。
>
> 如果你原本是为了"让图片能显示"才想托管这个目录:**不需要**。内核有签名直链 `GET /api/v1/sys/file/{id}/view?sig=…`(上传接口在 `viewUrl` 字段里直接给你),匿名可取但签名不可伪造——`<img src>` 能用,而整个上传目录仍然锁着。

跑起来后:`/` 是前端,`/api/v1/**` 是后端,`/health` 是探针,同源、无 CORS。

## 路线 B:nginx 反代(前后端分离,但仍是同源)

nginx 托管静态产物,把 `/api` 转给后端。浏览器只看到一个源,所以**同样不需要 CORS**。

```nginx
server {
    listen 80;
    server_name admin.example.com;

    # 上传大小上限要 ≥ TenonAdmin:Upload:MaxSizeMb(默认 20MB);
    # nginx 默认只有 1m,不改的话上传大文件会得到 413 而不是内核的错误码。
    client_max_body_size 32m;

    root /var/www/tenon;          # web/dist 的内容
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;   # 前端 history 路由回退
    }

    location /api/ {
        proxy_pass http://127.0.0.1:5000;   # 后端监听地址
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

> ⚠️ **光在代理里写 `X-Forwarded-For` 是没用的,还必须让内核采信它** —— 见下面的「反向代理之后:让内核取到真实客户端 IP」。不配的话,后端看到的永远是代理那一个 IP:**全体用户共享同一个限流桶**(一个人狂点登录就能把所有人的登录限死),按 IP 的爆破防护归零,登录日志里的 IP 列也全是代理地址。

### 反向代理之后:让内核取到真实客户端 IP

任何反代(nginx / Caddy / Traefik / k8s ingress)之后都必须配这一段:

```json
{
  "TenonAdmin": {
    "Api": {
      "ForwardedHeaders": {
        "Enabled": true,
        "KnownProxies": [ "10.0.0.8" ],
        "KnownNetworks": [ "172.16.0.0/12" ]
      }
    }
  }
}
```

- **默认关**。不在代理后面却打开它 = 允许任何人伪造自己的 IP。
- **打开了就必须声明受信来源**(`KnownProxies` 给具体 IP,`KnownNetworks` 给 CIDR 网段——容器编排下代理 IP 不固定,用网段更实际)。一个都不给 → **启动直接报错**。这不是挑剔:无条件采信 `X-Forwarded-For` 比不解析**更糟** —— 攻击者每个请求伪造一个不同 IP,就能无限开新的限流分区(限流被**完全绕过**),还能把爆破失败记到别人头上。
- 受信的是**来源地址**,所以:**任何能直连后端端口的人都能伪造 IP**。反代之后就别把后端端口暴露出去(compose 里那个调试端口因此只绑 `127.0.0.1`)。

## 路线 C:真跨源(前端在 CDN / 独立域名)

只有这种情况才需要动 CORS,而且**两端都要配**:

前端 —— 构建期给出 API 源(不必改代码):

```bash
VITE_API_BASE=https://api.example.com npm run build
```

后端 —— 放行该源(默认 deny-all,不配就是浏览器全部被拦):

```json
{
  "TenonAdmin": {
    "Api": {
      "Cors": {
        "AllowedOrigins": [ "https://admin.example.com" ],
        "AllowCredentials": true
      }
    }
  }
}
```

`AllowedOrigins` 为空 = 不放行任何跨源;`AllowCredentials` 只在 origins 非空时才生效(不存在 `AllowAnyOrigin + 凭证` 这种组合)。CORS 策略由内核的 `IStartupFilter` 自动挂载在管道前段,**不需要你手写 `UseCors`**。

## 路线 D:Docker(容器化交付)

仓库根的 `docker-compose.yml` 起四个服务:**MySQL + Redis + 后端 + Caddy(托管 SPA 并反代 `/api`)**。

```bash
docker compose up -d --build
# 前端 http://localhost:8080   后端调试口 http://127.0.0.1:8081/health/ready(只绑回环)
docker compose logs app        # 首启的随机超管密码(没显式配 Seed:AdminPassword 时)在这里
```

**为什么默认是 Caddy 而不是 nginx**:把 `web/Caddyfile` 里的站点标签 `:80` 换成你的域名、删掉 `auto_https off`,Caddy 就会**自动申请并续期 Let's Encrypt 证书** —— 自托管时省掉整套 TLS 手工活。仍想用 nginx 的:`web/nginx.conf` 还在仓库里(路线 B 那份的容器版),把 `web/Dockerfile` 的运行阶段换回 `nginx:alpine` 即可。

它跑的是 **`ASPNETCORE_ENVIRONMENT=Production`** —— 这是刻意的:compose 因此顺带成了「生产首启路径」的活体测试,上面 §0 那三条硬要求(显式 JWT 密钥、空库要显式允许建表、上传根挪出 `wwwroot`)必须**同时**满足才起得来,少一条就是一条读得懂的启动错误。这三条在 compose 里都写成了环境变量,照着改成你自己的值即可。

几个不写出来就会踩的点:

| 点 | 为什么 |
|---|---|
| **具名卷,不要 bind mount** | 镜像里跑的是非 root 用户。具名卷首次挂载会从镜像目录带走属主,容器写得进去;bind mount 会用宿主属主覆盖,应用直接写不了 SQLite / 上传目录。 |
| **镜像里没有 `HEALTHCHECK`** | `aspnet` 运行时镜像既没有 `curl` 也没有 `wget`,写了只会恒失败。健康检查交给编排层探 `/health`(存活)与 `/health/ready`(DB + 缓存)。 |
| **`.dockerignore` 是安全项** | 开发机的 `data/` 里躺着真实的 `admin.db` 和 `dev-jwt.key`。没有它,一个 `COPY . .` 就把**签名密钥**烤进镜像层——镜像一推,谁都能伪造超管令牌。 |
| **多副本改 `WorkerId`** | 每个实例 0–63 必须各不相同,否则同毫秒发号撞主键。见下一节。 |

**你自己的 host**:`dotnet new tenon-app` 生成的目录里已经带了一份 `Dockerfile`(从 NuGet 装内核,构建你的 host);仓库根那份是从源码构建样例宿主 `MinimalHost`,给内核 CI 用的,你不需要它。

---

## 多副本部署(水平扩容)

起第二个副本之前,下面**四条一条都不能少**。少任何一条,系统不会报错,只会开始悄悄做错事。

```bash
# 仓库里有现成的双副本叠加层,也是 CI 里真跑的那套
docker compose -f docker-compose.yml -f docker-compose.scale.yml up -d --build
bash scripts/smoke-multi-replica.sh http://localhost:8080   # 逐条验证下面这些保证
```

### ① Redis 是**前置条件**,不是可选优化

进程内缓存意味着副本 A 的失效**永远传不到**副本 B。后果不是"慢一点",是安全功能直接失灵:

| 表现 | 细节 |
|---|---|
| **强制下线失灵(最严重)** | 会话缓存的 TTL 是**刷新令牌寿命**(天级)。A 上强退 → DB 写了吊销、A 清了自己的内存;**B 的那份还在**,继续判定"活跃",于是经负载均衡时约一半请求照常放行,而且一放就是**好几天**。 |
| **撤权后仍有权限** | 权限/数据范围缓存默认 20 分钟。被撤权的人在另一副本上照旧有权限;数据范围还喂着 SqlSugar 全局过滤器 —— 他**继续看得见别的机构的数据**。 |
| **锁定/限流阈值翻 N 倍** | 登录失败计数、限流计数各副本各数各的:`MaxFailCount=5` 两副本就成了 10,认证桶 20/min 成了 40/min。 |
| **验证码必失败** | 一次性票据发在 A、验在 B,B 上没有这个键。 |

配上 `Cache:Provider=Redis` + `Cache:RedisConnectionString`,以上**全部自动修好**(失效走的是缓存键空间,不是事件总线),业务代码零改动。

### ② 每个副本必须有**不同的 `WorkerId`**

同号 = 同毫秒发号撞主键(数据损坏级)。内核对此**不再沉默**:配了 Redis(= 明显的多实例意图)却没显式给 `TenonAdmin:Id:WorkerId` → **启动直接报错**。

- **compose**:`--scale app=2` 给不了各副本不同的环境变量,所以要写**多个显式的 app 服务**(见 `docker-compose.scale.yml`,各配各的 WorkerId)。
- **k8s**:用 **StatefulSet**,从 Pod 名字的序号(`app-0`/`app-1`)注入 `WorkerId`。Deployment 的随机 Pod 名给不了稳定序号。

### ③ 反向代理必须配 `ForwardedHeaders`

见上面那一节。不配的话,两个副本都只看得见负载均衡器那一个 IP —— 按 IP 限流形同虚设,审计日志里的 IP 全是代理地址。

### ④ 冷启动**先起一个副本**

CodeFirst 建表 + 写种子是"检查后插入",**不是原子的**:两个副本同时首启,会有一个撞唯一键崩掉。

- **compose**:第二个副本 `depends_on: app: condition: service_healthy`(`docker-compose.scale.yml` 就是这么写的),零代码解决。
- **k8s**:用 init job / migration job 先把库建好,再放开副本。

### 还没解决的:上传目录必须是**共享可写卷**

`LocalFileStorage` / `ChunkStorage` 写的是**本地盘**。compose 用具名卷,两个副本天然共享;但 **k8s 上如果每个 Pod 一个独立 PVC,A 传的文件在 B 上就是 404**,分片上传更是直接 `ChunkMissing`(分片散落在不同 Pod 上,合并必然缺片)。多副本必须给上传根挂 **RWX(ReadWriteMany)** 的共享卷,或前置替换 `IFileStorage` 成对象存储(S3/OSS)。

---

## 部署后自检

```bash
curl https://<你的域名>/health         # Healthy(存活)
curl https://<你的域名>/health/ready   # Healthy(DB + 缓存都通)
curl -i https://<你的域名>/api/v1/ping # 401 = API 路由通了(该端点需要登录)
```

再打开前端登录一次,确认能拿到菜单(说明 JWT 密钥、数据库、种子都对)。

注意 `/openapi/v1.json` **只在 Development 环境挂载**——它是给前端 `npm run gen:api` 用的契约源,不是生产端点;生产下 404 是预期行为。
