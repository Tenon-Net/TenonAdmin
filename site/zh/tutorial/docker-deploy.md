# 容器化部署一条龙

本篇用仓库根自带的 `docker-compose.yml` 把 TenonAdmin 完整跑成容器:MySQL + Redis + 后端 + 前端,一条命令起全栈。它同时也是「生产首次启动」这条路径的活体测试——四条安全基线缺一条都起不来,报错会点名到底缺什么。

::: tip 这套 compose 是给谁用的
仓库根的 `Dockerfile` 是从**源码**构建示例宿主 `MinimalHost`,给内核自己的 CI 用的。如果你是 NuGet 消费方,`dotnet new tenon-app` 生成的目录里已经带了一份从 NuGet 装内核构建你自己 host 的 `Dockerfile`,直接用那份即可,下面的步骤同样适用。
:::

## 1. 起全栈

```bash
docker compose up -d --build
```

这一条命令拉起四个服务(定义在仓库根 `docker-compose.yml`):

- `db`——MySQL 8.0
- `redis`——Redis 7
- `app`——后端,`ASPNETCORE_ENVIRONMENT=Production`
- `web`——Caddy,托管前端静态产物并反代 `/api`

起来后:

```bash
# 前端
open http://localhost:8080

# 后端调试口(只绑回环,见下面「为什么只绑 127.0.0.1」)
curl http://127.0.0.1:8081/health/ready

# 首启的随机超管密码(没显式配 TENON_ADMIN_PASSWORD 时)在这里
docker compose logs app
```

## 2. 为什么默认能直接起来:四条安全基线都写成了环境变量

生产环境有几道硬性门槛,不满足会直接拒绝启动而不是带着隐患跑起来。`docker-compose.yml` 里的 `app` 服务把它们都预先配好了,照抄改成你自己的值即可:

| 环境变量 | 对应配置项 | 为什么必须显式给 |
|---|---|---|
| `TenonAdmin__Jwt__SecretKey` | `TenonAdmin:Jwt:SecretKey` | 不配 = 开发密钥模式(自动生成并落盘、打印警告)。生产必须显式给一个 ≥32 字节的随机串,且不能进版本库。 |
| `TenonAdmin__Database__EnableCodeFirstInProduction` | 同名 | `ASPNETCORE_ENVIRONMENT=Production` 时,即便建表开关开着也**不会**自动建表——这是防止应用擅自 `ALTER` 生产库的安全闸门。空库首次上生产,这里必须显式 `true`(建完可以再关掉),或者 DBA 手工建表。 |
| `TenonAdmin__Upload__RootPath` | 同名 | 必须挪出 `wwwroot`。compose 里的路线是"后端+Caddy分离",没有 `UseStaticFiles()` 托管前端的问题,但仍要声明成数据卷,否则重建容器就丢文件。 |
| `TenonAdmin__Id__WorkerId` | 同名 | 水平扩展时每实例必须不同(0–63),否则同毫秒发号撞主键。配了 Redis(=明显的多实例意图)却没显式给这项,内核会**直接拒绝启动**,而不是悄悄埋一个将来才炸的坑。 |

少配一条,`docker compose logs app` 里会看到一条读得懂的启动错误,点名到底缺哪个配置项——这是刻意设计成这样的,比"进程正常启动、直到第一次写库才炸在驱动层"好排查得多。

::: warning 上面这些值只是演示密钥
`docker-compose.yml` 里 `TENON_JWT_SECRET`、`TENON_DB_PASSWORD`、`TENON_ADMIN_PASSWORD` 都带了 `:-` 默认值方便直接跑起来体验。**正式部署前**通过同目录下的 `.env` 文件或部署平台的密钥管理注入真实值,不要沿用默认串。
:::

## 3. 缓存必须是 Redis,不是可选优化

`docker-compose.yml` 里 `app` 服务的 `TenonAdmin__Cache__Provider=Redis` 不是性能调优,是**前提条件**。单副本时留 `Memory`(内存缓存)也能跑,但只要你打算横向扩容,进程内缓存意味着:副本 A 上的强制下线、撤权、登录锁定,永远传不到副本 B——而会话缓存的 TTL 是刷新令牌的寿命(天级),后果不是"慢一点",是这些安全功能在另一副本上**失效好几天**。换成 Redis 之后,失效走的是共享缓存键空间,业务代码零改动。

## 4. 反向代理转发头:让内核看到真实客户端 IP

`app` 服务同时配了:

```yaml
TenonAdmin__Api__ForwardedHeaders__Enabled: "true"
TenonAdmin__Api__ForwardedHeaders__KnownNetworks__0: 172.16.0.0/12
```

Caddy(`web` 服务)在前面挡着,不解析 `X-Forwarded-For` 的话,后端看到的永远是 Caddy 那一个 IP——全体用户共享一个限流桶(一人爆破登录能锁死所有人),登录日志里的 IP 全是代理地址。**打开这项必须同时声明受信来源**(这里是 Docker 桥接网段);受信的是"来源地址",所以任何能直连后端端口的人都能伪造 IP。

这正是为什么 `app` 服务的端口映射写成:

```yaml
ports:
  - "127.0.0.1:${TENON_API_PORT:-8081}:8080"
```

**只绑 `127.0.0.1`**——绑 `0.0.0.0` 会把"伪造 IP、绕过限流"的能力暴露给整个局域网;正常访问都走前面的 Caddy(已反代 `/api` 和 `/health`)。生产环境更进一步,建议直接去掉这个端口映射,只留反代入口。

## 5. 容易漏掉的三个点

| 点 | 为什么 |
|---|---|
| **具名卷,不要 bind mount** | 镜像里跑的是非 root 用户。具名卷首次挂载会从镜像目录继承属主,容器写得进去;bind mount 会用宿主机属主覆盖,应用直接写不了 SQLite / 上传目录。`docker-compose.yml` 里 `app-data`、`upload-data` 都是具名卷。 |
| **镜像里没有 `HEALTHCHECK`** | `aspnet` 运行时镜像既没有 `curl` 也没有 `wget`,写了健康检查指令只会恒失败。健康检查交给编排层探 `/health`(存活)与 `/health/ready`(DB + 缓存)。 |
| **`.dockerignore` 是安全项** | 仓库根有一份 `.dockerignore`(排除 `data/` 等本地目录)。开发机的 `data/` 里可能躺着真实的 `admin.db` 和开发期自动生成的 JWT 签名密钥——没有它,一次 `COPY . .` 就能把签名密钥烤进镜像层,镜像一推,谁都能伪造超管令牌。 |

## 6. 多副本部署(水平扩容)

起第二个副本之前,下面四条一条都不能少——少了不会报错,只会开始悄悄做错事。仓库里有现成的双副本叠加层,也是 CI 里真跑的那一套:

```bash
docker compose -f docker-compose.yml -f docker-compose.scale.yml up -d --build
bash scripts/smoke-multi-replica.sh http://localhost:8080   # 逐条验证下面这些保证
```

`docker-compose.scale.yml` 加了一个显式的 `app2` 服务(而不是用 `docker compose --scale`),原因是 `--scale` 给不了每个副本各自独立的环境变量,而下面第②条恰恰要求每个副本不同:

1. **Redis 是前置条件**——见上面第 3 节,多副本下这不是可选优化。
2. **每个副本必须有不同的 `WorkerId`**——`docker-compose.scale.yml` 里 `app2` 显式给了 `TenonAdmin__Id__WorkerId: "1"`,与 `app` 的 `0` 不同。k8s 上用 StatefulSet,从 Pod 名字的序号(`app-0`/`app-1`)注入。
3. **反向代理必须配 `ForwardedHeaders`**——同上面第 4 节,两个副本都要配,否则都只看得见负载均衡器那一个 IP。
4. **冷启动先起一个副本**——`app2` 的 `depends_on: app: condition: service_healthy`,等第一个副本把表和种子都写完再启动第二个,避免两个副本同时首建表时因唯一键冲突崩溃。k8s 上用 init job / migration job 替代。

::: warning 上传目录必须是共享可写卷
`LocalFileStorage` 写的是本地盘。compose 里两个副本共享同一个 `upload-data` 具名卷,天然没问题;但 k8s 上如果每个 Pod 用独立 PVC,A 传的文件在 B 上会直接 404,分片上传更是必然 `ChunkMissing`。多副本要么给上传根挂 RWX(ReadWriteMany)共享卷,要么前置替换 `IFileStorage` 成对象存储(S3/OSS)。
:::

## 7. 部署后自检

```bash
curl https://<你的域名>/health         # Healthy(存活)
curl https://<你的域名>/health/ready   # Healthy(DB + 缓存都通)
curl -i https://<你的域名>/api/v1/ping # 401 = API 路由通了(该端点需要登录)
```

再打开前端登录一次,确认能拿到菜单——说明 JWT 密钥、数据库、种子数据都对上了。

::: tip `/openapi/v1.json` 生产下 404 是预期行为
它只在 Development 环境挂载,是给前端 `npm run gen:api` 用的契约源,不是生产端点。
:::

## 不用 Docker 的路线

不想上容器的话,`docs/deployment.md` 还给了三条托管路线:单体部署(后端顺带托管前端)、nginx 反代(前后端分离但同源)、真跨源(前端在 CDN,需要处理 CORS)。完整的 nginx / Caddy 配置、`ForwardedHeaders` 配置细节见[路线 B:反向代理](/zh/guide/deployment/route-b);内核版本升级时的建表/补列/种子处理,见[部署指南概览](/zh/guide/deployment/)。
