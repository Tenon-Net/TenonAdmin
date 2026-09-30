# 容器化与多副本

仓库提供 Docker Compose 示例，将数据库、Redis、后端和前端一起启动，适合在本地验证完整部署链路。后端使用 Production 环境，因此需要显式配置密钥、数据库初始化和持久化存储。示例中的默认密码仅用于本地体验，正式部署前必须替换。

::: tip 这套 compose 是给谁用的
仓库根的 `Dockerfile` 是从**源码**构建示例宿主 `MinimalHost`，给内核自己的 CI 用。你要是 NuGet 消费方，那用另一份。`dotnet new tenon-app` 生成的目录里已经带了一份 `Dockerfile`，它从 NuGet 装内核、构建你自己的 host，直接用就行，下面的步骤照样适用。
:::

## 起全栈

```bash
docker compose up -d --build
```

这一条命令拉起四个服务，定义在仓库根 `docker-compose.yml` 里：`db`（MySQL 8.0）、`redis`（Redis 7）、`app`（后端）、`web`（Caddy，托管前端静态产物并反代 `/api`）。起来后：

```bash
open http://localhost:8080                 # 前端
curl http://127.0.0.1:8081/health/ready    # 后端调试口,只绑回环,原因见下文
docker compose logs app                    # 首启信息
```

`app` 使用 `ASPNETCORE_ENVIRONMENT=Production`。未配置 JWT 密钥会拒绝启动；空库需要先建表或显式允许生产建表；上传目录要持久化，并在启用静态托管时放到 `wwwroot` 外。仓库的 `docker-compose.yml` 已提供演示配置，实际部署请按[部署概览](/zh/guide/deployment/)核对。

首次登录的超管账号是 `superAdmin`。密码呢？compose 里这一行给了答案：

```yaml
TenonAdmin__Seed__AdminPassword: ${TENON_ADMIN_PASSWORD:-Tenon@123456}
```

`:-` 是 shell 的默认值写法：环境变量 `TENON_ADMIN_PASSWORD` 没设，就取冒号后面的 `Tenon@123456`。所以这次登录用的是它，不是随机密码。想回到零配置那条路径，把这个默认删掉，让 `Seed:AdminPassword` 真的不配。那时内核会随机生成一个 16 位密码，在建号那一次启动打印到控制台，仅此一次。日志在 `docker compose logs app`。

::: warning 演示密钥只是让你先跑起来
`docker-compose.yml` 里 `TENON_JWT_SECRET`、`TENON_DB_PASSWORD`、`TENON_ADMIN_PASSWORD` 都带了 `:-` 默认值，方便你直接上手。正式部署前得换成真实值，通过同目录下的 `.env` 文件注入，或者用部署平台的密钥管理。别沿用默认串，也别让它进版本库。
:::

前端那个 `web` 服务跑的是 Caddy。把 `web/Caddyfile` 的站点标签从 `:80` 换成你的域名，再删掉 `auto_https off`，它就会自动申请并续期 Let's Encrypt 证书，自托管省掉整套 TLS 手工活。想继续用 nginx 也行，`web/nginx.conf` 还在仓库里，把 `web/Dockerfile` 的运行阶段换回 `nginx:alpine` 就可以。完整的 nginx、Caddy 反代配置在[路线 B：反向代理](/zh/guide/deployment/route-b)。

## 容器化里几个不写出来就会踩的点

| 点 | 为什么 |
|---|---|
| **确认数据卷可写** | 镜像里跑的是非 root 用户。具名卷首次挂载会从镜像目录继承属主，容器写得进去；bind mount 使用宿主目录权限，需要确保容器用户有权写入 SQLite 和上传目录。`docker-compose.yml` 里 `app-data`、`upload-data` 都是具名卷。 |
| **镜像里没有 `HEALTHCHECK`** | `aspnet` 运行时镜像既没有 `curl` 也没有 `wget`，写了健康检查指令只会恒失败。健康检查交给编排层探 `/health`（存活）与 `/health/ready`（DB + 缓存）。 |
| **`.dockerignore` 是安全项** | 开发机的 `data/` 里可能躺着真实的 `admin.db` 和开发期自动生成的 JWT 签名密钥（`dev-jwt.key`）。仓库根的 `.dockerignore` 把它排除掉。没有它，一次 `COPY . .` 就能把签名密钥烤进镜像层，镜像一推，谁都能伪造超管令牌。 |
| **多副本改 `WorkerId`** | 每实例 0–63 必须各不相同，否则同毫秒发号撞主键。不配时共享库会领空闲槽；写成同一个号，后到的起不来。详见下面「多副本与 WorkerId」。 |

## 多副本与 WorkerId

准备多副本部署时，需要同时处理共享缓存、实例编号、代理信任和共享文件存储。先在测试环境使用仓库的双副本配置验证，再应用到生产：

```bash
docker compose -f docker-compose.yml -f docker-compose.scale.yml up -d --build
bash scripts/smoke-multi-replica.sh http://localhost:8080   # 逐条验证下面这些保证
```

`docker-compose.scale.yml` 加了一个显式的 `app2` 服务，没用 `docker compose --scale`。为什么？`--scale` 给不了每个副本各自独立的环境变量。而下面「每个副本一个不同的 WorkerId」这条，恰恰要求每个副本都不同。

### 缓存换成 Redis，这是前提不是优化

`Memory` 只在当前进程中保存状态。多个副本使用各自内存时，一个副本撤销会话或权限，另一个副本仍可能使用旧缓存，直到过期。下面列出需要验证的跨副本行为：

| 表现 | 细节 |
|---|---|
| **强制下线失灵（最严重）** | 会话缓存的 TTL 是刷新令牌寿命（天级）。A 上强退，DB 随即写了吊销、A 也清了自己的内存，可**B 的那份还在**，继续判定「活跃」，于是经负载均衡时约一半请求照常放行，一放就是好几天。 |
| **撤权后仍有权限** | 权限 / 数据范围缓存默认 20 分钟。被撤权的人在另一副本上照旧有权限；数据范围缓存还喂着 SqlSugar 全局过滤器，于是他**继续看得见别的机构的数据**。 |
| **锁定 / 限流阈值翻倍** | 登录失败计数、限流计数各副本各数各的：`MaxFailCount=5` 两副本就成了 10，认证桶 20/min 成了 40/min。 |
| **验证码必失败** | 一次性票据发在 A、验在 B,B 上没有这个键。 |

自有宿主需安装 `TenonAdmin.Caching.Redis`，在 `AddTenonAdmin()` 之前调用 `AddTenonAdminRedisCache(builder.Configuration)`，再配置 `TenonAdmin:Cache:Provider=Redis` 和 `Cache:RedisConnectionString`。仓库示例已注册该扩展。各副本使用同一共享缓存后，才能共享会话、权限失效和计数状态；上线前仍需运行多副本验证。

### 每个副本一个不同的 `WorkerId`

雪花发号器的机器位来自 `TenonAdmin:Id:WorkerId`，取值 0–63。同机不配时文件锁换号；**容器里文件锁帮不上忙**，改在共享库 `sys_worker_lease` 上领空闲槽（插 0 失败就插 1），不随机。显式写成同一个号，后到的副本起不来。compose 里仍建议把号写死，重启才稳。

- **compose**：`--scale app=2` 给不了各副本不同的环境变量，所以拆成多个显式的 `app` 服务各配各的。`docker-compose.scale.yml` 里 `app2` 就显式给了 `TenonAdmin__Id__WorkerId: "1"`，与 `app` 的 `0` 不同。
- **k8s**：用 StatefulSet，从 Pod 名字的序号注入，比如 `app-0`、`app-1`。Deployment 的随机 Pod 名给不了稳定序号。

### 反代之后必须配 `ForwardedHeaders`

`app` 服务已经配了：

```yaml
TenonAdmin__Api__ForwardedHeaders__Enabled: "true"
TenonAdmin__Api__ForwardedHeaders__KnownNetworks__0: 172.16.0.0/12
```

这套 `ForwardedHeaders` 配置和[路线 B：反向代理](/zh/guide/deployment/route-b)是同一件事，只是受信来源换成了 Docker 桥接网段。为什么要配、不配会有什么后果，那页已经讲透，这里不重复。多副本要每个副本都配，否则大家看到的都只是负载均衡器那一个 IP。`app` 的端口映射因此只绑 `127.0.0.1`，理由同样在那页：

```yaml
ports:
  - "127.0.0.1:${TENON_API_PORT:-8081}:8080"
```

绑 `0.0.0.0` 会把伪造 IP、绕过限流的能力暴露给整个局域网。正常访问都走前面的 Caddy，它已经反代了 `/api` 和 `/health`。生产环境更进一步，建议直接去掉这个端口映射，只留反代入口。

### 冷启动先起一个副本

CodeFirst 建表加写种子是「检查后插入」，不是原子操作。两个副本同时首启，会有一个撞唯一键崩掉。compose 里给 `app2` 加了 `depends_on: app: condition: service_healthy`，等第一个副本把表和种子都写完，再启动第二个，零代码解决。k8s 上换个做法，用 init job 或 migration job 先把库建好，再放开副本。

::: warning 上传目录必须是共享可写卷
`LocalFileStorage` 与 `ChunkStorage` 使用本地磁盘。多副本需要共享完整文件和临时分片目录，否则请求切换到另一副本时可能遇到文件不存在或 `ChunkMissing`。仓库 Compose 使用共享的 `upload-data` 具名卷；Kubernetes 可采用 RWX（ReadWriteMany）共享卷。替换 `IFileStorage` 为对象存储时，仍需单独解决 `ChunkStorage` 的分片共享，不能只替换最终文件存储。
:::

不想上容器的话，[部署概览](/zh/guide/deployment/)还给了单体、反向代理、真跨源三条托管路线，上线后的健康检查与自检清单也在那里。
