# 路线 B：反向代理（nginx 或 Caddy）

反向代理托管静态产物，把 `/api` 转给后端。浏览器只看到一个源，所以**同样不需要 CORS**。nginx 和 Caddy 两份配置等价，选一份抄走就行。

::: tip 该选哪个
已有 nginx 网关的照抄 nginx 那份。**新拉一台机器、想省掉 TLS 手工活的选 Caddy**：站点标签只要写真实域名（不是 `:80`），Caddy 就会自动申请并续期 Let's Encrypt 证书。这也是仓库的[容器化与多副本](/zh/guide/deployment/docker)交付默认用 Caddy 的原因。
:::

## nginx

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

    # 探针:编排层/负载均衡直接探这里
    # 漏掉这段,/health 会落进上面的 try_files 拿到一张 index.html,后端挂了探针照样返回 200。
    location /health {
        proxy_pass http://127.0.0.1:5000;
        access_log off;
    }
}
```

## Caddy

用真实域名当站点标签，Caddy 会自动签发和续期证书。本地或没域名时才用 `:80`。这份是主机直装版，和[容器化与多副本](/zh/guide/deployment/docker)容器里那份 `web/Caddyfile` 是同一套思路。

```
admin.example.com {
    # 上传大小上限要 ≥ TenonAdmin:Upload:MaxSizeMb(默认 20MB)
    request_body {
        max_size 32MB
    }

    # 必须用 handle 块,别把 reverse_proxy 和 try_files 平铺在一起:
    # Caddy 按内置指令顺序执行(不按书写顺序),try_files 属 rewrite 阶段、早于 reverse_proxy。
    # 平铺时 /api/... 这种磁盘上找不到文件的路径会先被 try_files 改写成 /index.html,
    # 于是 API 请求拿到的是一张 HTML —— 前端整个连不上后端(实测:登录直接返回空)。
    handle /api/* {
        reverse_proxy 127.0.0.1:5000
    }

    # 探针:编排层/负载均衡直接探这里
    handle /health* {
        reverse_proxy 127.0.0.1:5000
    }

    # 兜底:前端 history 路由,未命中静态文件就交给 SPA(深链刷新不 404)
    handle {
        root * /var/www/tenon
        try_files {path} /index.html
        file_server
    }
}
```

::: warning 两份配置都还差一步
代理里写了 `X-Forwarded-For`（nginx）或让 `reverse_proxy` 自动带（Caddy），都还不够。内核那边也得采信它，做法见下面的「反向代理之后：让内核取到真实客户端 IP」。不配的话，后端看到的永远是代理那一个 IP。于是**全体用户共享同一个限流桶**，一个人狂点登录就能把所有人的登录限死。按 IP 的爆破防护也归零，登录日志里的 IP 列全是代理地址。
:::

## 反向代理之后：让内核取到真实客户端 IP

任何反代（nginx / Caddy / Traefik / k8s ingress）之后都必须配这一段：

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
- **打开了就必须声明受信来源**：`KnownProxies` 给具体 IP，`KnownNetworks` 给 CIDR 网段。容器编排下代理 IP 不固定，用网段更实际。一个都不给 → 启动直接报错。这不是挑剔，因为无条件采信 `X-Forwarded-For` 比不解析更糟。攻击者每个请求伪造一个不同 IP，就能无限开新的限流分区，限流被完全绕过。他还能把爆破失败记到别人头上。
- 受信的是来源地址，所以**任何能直连后端端口的人都能伪造 IP**。反代之后就别把后端端口暴露出去（compose 里那个调试端口因此只绑 `127.0.0.1`）。

