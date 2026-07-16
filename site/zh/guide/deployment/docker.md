# 容器化与多副本

这页帮你把 TenonAdmin 完整跑成容器——一条 `docker compose up` 起 MySQL + Redis + 后端 + 前端——再往上安全地扩到多副本。

::: tip 这套 compose 是给谁用的
仓库根的 `Dockerfile` 是从**源码**构建示例宿主 `MinimalHost`,给内核自己的 CI 用。如果你是 NuGet 消费方,`dotnet new tenon-app` 生成的目录里已经带了一份「从 NuGet 装内核、构建你自己 host」的 `Dockerfile`,直接用那份即可,下面的步骤同样适用。
:::

## 起全栈

```bash
docker compose up -d --build
```

这一条命令拉起四个服务(定义在仓库根 `docker-compose.yml`):`db`(MySQL 8.0)、`redis`(Redis 7)、`app`(后端)、`web`(Caddy,托管前端静态产物并反代 `/api`)。起来后:

```bash
open http://localhost:8080                 # 前端
curl http://127.0.0.1:8081/health/ready    # 后端调试口,只绑回环,原因见下文
docker compose logs app                    # 首启信息
```

`app` 跑的是 `ASPNETCORE_ENVIRONMENT=Production`——这是刻意的:这条命令因此顺带成了「生产首启路径」的活体测试。生产有三道硬门槛:JWT 密钥必须显式给(不给就是开发密钥模式,每个副本各签各的会随机 401)、空库首次上生产必须显式允许建表(默认不自动 ALTER 生产库)、上传根必须挪出 `wwwroot`。`docker-compose.yml` 把这三项都写成了环境变量,照抄改成你自己的值即可;少配一条会得到一条点名到配置项的、读得懂的启动错误,比"进程照常起、直到第一次写库才炸在驱动层"好排查得多。这三道门槛连同升级时的建表 / 补列细节,[部署概览](/zh/guide/deployment/)里讲全。

首次登录的超管账号是 `superAdmin`。密码上,compose 通过 `TENON_ADMIN_PASSWORD` 的 `:-` 默认值配了个演示值 `Tenon@123456`,所以这次登录用的是它,不是随机密码。把这个默认删掉(让 `Seed:AdminPassword` 真的不配),才回到零配置那条路径:内核随机生成一个 16 位密码,在建号那一次启动打印到控制台(`docker compose logs app`),仅此一次。

::: warning 演示密钥只是让你先跑起来
`docker-compose.yml` 里 `TENON_JWT_SECRET`、`TENON_DB_PASSWORD`、`TENON_ADMIN_PASSWORD` 都带了 `:-` 默认值,方便直接体验。正式部署前通过同目录下的 `.env` 文件或部署平台的密钥管理注入真实值,不要沿用默认串,也不要让它进版本库。
:::

前端那个 `web` 服务跑的是 Caddy。把 `web/Caddyfile` 的站点标签从 `:80` 换成你的域名、删掉 `auto_https off`,它就自动申请并续期 Let's Encrypt 证书,自托管省掉整套 TLS 手工活。想继续用 nginx,`web/nginx.conf` 还在仓库里,把 `web/Dockerfile` 的运行阶段换回 `nginx:alpine` 即可;完整的 nginx / Caddy 反代配置在[路线 B:反向代理](/zh/guide/deployment/route-b)。

## 容器化里几个不写出来就会踩的点

| 点 | 为什么 |
|---|---|
| **具名卷,不要 bind mount** | 镜像里跑的是非 root 用户。具名卷首次挂载会从镜像目录继承属主,容器写得进去;bind mount 会用宿主属主覆盖,应用直接写不了 SQLite / 上传目录。`docker-compose.yml` 里 `app-data`、`upload-data` 都是具名卷。 |
| **镜像里没有 `HEALTHCHECK`** | `aspnet` 运行时镜像既没有 `curl` 也没有 `wget`,写了健康检查指令只会恒失败。健康检查交给编排层探 `/health`(存活)与 `/health/ready`(DB + 缓存)。 |
| **`.dockerignore` 是安全项** | 开发机的 `data/` 里可能躺着真实的 `admin.db` 和开发期自动生成的 JWT 签名密钥(`dev-jwt.key`)。仓库根的 `.dockerignore` 把它排除掉——没有它,一次 `COPY . .` 就能把签名密钥烤进镜像层,镜像一推,谁都能伪造超管令牌。 |
| **多副本改 `WorkerId`** | 每实例 0–63 必须各不相同,否则同毫秒发号撞主键;配了 Redis 却没显式给还会当场拒绝启动。详见下面「多副本与 WorkerId」。 |

## 多副本与 WorkerId

起第二个副本之前,下面几条一条都不能少——少了大多不会报错,只会开始悄悄做错事(WorkerId 是唯一会当场拦你的例外)。仓库里有现成的双副本叠加层,也是 CI 里真跑的那套:

```bash
docker compose -f docker-compose.yml -f docker-compose.scale.yml up -d --build
bash scripts/smoke-multi-replica.sh http://localhost:8080   # 逐条验证下面这些保证
```

`docker-compose.scale.yml` 加了一个显式的 `app2` 服务,而不是用 `docker compose --scale`,原因是 `--scale` 给不了每个副本各自独立的环境变量,而下面「每个副本一个不同的 WorkerId」这条恰恰要求每个副本不同。

### 缓存换成 Redis,这是前提不是优化

单副本留 `Memory`(进程内缓存)能跑,但进程内缓存意味着副本 A 上的失效永远传不到副本 B。后果不是"慢一点",是安全功能直接失灵,而且失灵窗口是天级:

| 表现 | 细节 |
|---|---|
| **强制下线失灵(最严重)** | 会话缓存的 TTL 是刷新令牌寿命(天级)。A 上强退→DB 写了吊销、A 清了自己的内存,**B 的那份还在**,继续判定"活跃",于是经负载均衡时约一半请求照常放行,一放就是好几天。 |
| **撤权后仍有权限** | 权限 / 数据范围缓存默认 20 分钟。被撤权的人在另一副本上照旧有权限;数据范围还喂着 SqlSugar 全局过滤器——他**继续看得见别的机构的数据**。 |
| **锁定 / 限流阈值翻倍** | 登录失败计数、限流计数各副本各数各的:`MaxFailCount=5` 两副本就成了 10,认证桶 20/min 成了 40/min。 |
| **验证码必失败** | 一次性票据发在 A、验在 B,B 上没有这个键。 |

配上 `TenonAdmin:Cache:Provider=Redis` + `Cache:RedisConnectionString`,以上全部自动修好——失效走的是共享缓存键空间,不是事件总线,业务代码零改动。

### 每个副本一个不同的 `WorkerId`

雪花发号器的机器位来自 `TenonAdmin:Id:WorkerId`(0–63)。单实例不配也没事(回落 0);但两个副本都拿 0,同一毫秒各自发号就会撞主键——数据损坏级,而且悄无声息。内核对这一条不再沉默:一旦配了 `Cache:Provider=Redis`(明显的多实例意图)却没有**显式**给出 `WorkerId`,启动直接抛错,点名到 `TenonAdmin:Id:WorkerId` 和 0–63 范围。显式写 `0` 视为你知情,放行。

- **compose**:`--scale app=2` 给不了各副本不同的环境变量,所以拆成多个显式的 `app` 服务各配各的——`docker-compose.scale.yml` 里 `app2` 就显式给了 `TenonAdmin__Id__WorkerId: "1"`,与 `app` 的 `0` 不同。
- **k8s**:用 StatefulSet,从 Pod 名字的序号(`app-0`/`app-1`)注入;Deployment 的随机 Pod 名给不了稳定序号。

### 反代之后必须配 `ForwardedHeaders`

`app` 服务已经配了:

```yaml
TenonAdmin__Api__ForwardedHeaders__Enabled: "true"
TenonAdmin__Api__ForwardedHeaders__KnownNetworks__0: 172.16.0.0/12
```

Caddy 在前面挡着,不解析 `X-Forwarded-For` 的话,后端看到的永远是 Caddy 那一个 IP——全体用户共享一个限流桶(一人爆破登录能锁死所有人),登录日志里的 IP 全是代理地址。多副本更是每个副本都要配,否则都只看得见负载均衡器那一个 IP。打开这项**必须同时声明受信来源**(这里是 Docker 桥接网段);受信的是"来源地址",所以任何能直连后端端口的人都能伪造 IP——这正是 `app` 的端口映射只绑 `127.0.0.1` 的原因:

```yaml
ports:
  - "127.0.0.1:${TENON_API_PORT:-8081}:8080"
```

绑 `0.0.0.0` 会把"伪造 IP、绕过限流"的能力暴露给整个局域网;正常访问都走前面的 Caddy(已反代 `/api` 和 `/health`)。生产环境更进一步,建议直接去掉这个端口映射,只留反代入口。

### 冷启动先起一个副本

CodeFirst 建表 + 写种子是"检查后插入",不是原子的:两个副本同时首启,会有一个撞唯一键崩掉。compose 里 `app2` 的 `depends_on: app: condition: service_healthy` 等第一个副本把表和种子都写完再启动第二个,零代码解决;k8s 上用 init job / migration job 先把库建好,再放开副本。

::: warning 上传目录必须是共享可写卷
`LocalFileStorage` / `ChunkStorage` 写的是本地盘。compose 里两个副本共享同一个 `upload-data` 具名卷,天然没问题;但 k8s 上如果每个 Pod 用独立 PVC,A 传的文件在 B 上会直接 404,分片上传更是必然 `ChunkMissing`(分片散落在不同 Pod,合并必然缺片)。多副本要么给上传根挂 RWX(ReadWriteMany)共享卷,要么前置替换 `IFileStorage` 成对象存储(S3 / OSS)。
:::

不想上容器的话,[部署概览](/zh/guide/deployment/)还给了单体、反向代理、真跨源三条托管路线,上线后的健康检查与自检清单也在那里。
