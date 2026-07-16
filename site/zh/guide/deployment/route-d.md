# 路线 D:Docker

容器化交付。仓库根的 `docker-compose.yml` 起四个服务:**MySQL + Redis + 后端 + Caddy(托管 SPA 并反代 `/api`)**。

```bash
docker compose up -d --build
# 前端 http://localhost:8080   后端调试口 http://127.0.0.1:8081/health/ready(只绑回环)
docker compose logs app        # 首启的随机超管密码(没显式配 Seed:AdminPassword 时)在这里
```

**为什么默认是 Caddy 而不是 nginx**:把 `web/Caddyfile` 里的站点标签 `:80` 换成你的域名、删掉 `auto_https off`,Caddy 就会**自动申请并续期 Let's Encrypt 证书** —— 自托管时省掉整套 TLS 手工活。仍想用 nginx 的:`web/nginx.conf` 还在仓库里(路线 B 那份的容器版),把 `web/Dockerfile` 的运行阶段换回 `nginx:alpine` 即可。

> 路线 B 的 Caddy 是**主机直装**(自己在服务器上装 Caddy),路线 D 的 Caddy 是**容器内**(`web/Caddyfile` 打进镜像),两者是同一套反代思路,按你是否上容器二选一。

它跑的是 **`ASPNETCORE_ENVIRONMENT=Production`** —— 这是刻意的:compose 因此顺带成了「生产首启路径」的活体测试,上面 §0 那三条硬要求(显式 JWT 密钥、空库要显式允许建表、上传根挪出 `wwwroot`)必须**同时**满足才起得来,少一条就是一条读得懂的启动错误。这三条在 compose 里都写成了环境变量,照着改成你自己的值即可。

几个不写出来就会踩的点:

| 点 | 为什么 |
|---|---|
| **具名卷,不要 bind mount** | 镜像里跑的是非 root 用户。具名卷首次挂载会从镜像目录带走属主,容器写得进去;bind mount 会用宿主属主覆盖,应用直接写不了 SQLite / 上传目录。 |
| **镜像里没有 `HEALTHCHECK`** | `aspnet` 运行时镜像既没有 `curl` 也没有 `wget`,写了只会恒失败。健康检查交给编排层探 `/health`(存活)与 `/health/ready`(DB + 缓存)。 |
| **`.dockerignore` 是安全项** | 开发机的 `data/` 里躺着真实的 `admin.db` 和 `dev-jwt.key`。没有它,一个 `COPY . .` 就把**签名密钥**烤进镜像层——镜像一推,谁都能伪造超管令牌。 |
| **多副本改 `WorkerId`** | 每个实例 0–63 必须各不相同,否则同毫秒发号撞主键。见下一节。 |

**你自己的 host**:`dotnet new tenon-app` 生成的目录里已经带了一份 `Dockerfile`(从 NuGet 装内核,构建你的 host);仓库根那份是从源码构建样例宿主 `MinimalHost`,给内核 CI 用的,你不需要它。

**上一节:** [路线 C:真跨源(CDN)](/zh/guide/deployment/route-c)
**下一节:** [多副本部署](/zh/guide/deployment/multi-replica)
