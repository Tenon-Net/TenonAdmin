# Route B: Reverse Proxy (nginx or Caddy)

The reverse proxy hosts the static build and forwards `/api` to the backend. The browser only ever sees one origin, so **CORS still isn't needed**. Below are two equivalent configs, nginx and Caddy — pick either.

::: tip Which one to pick
If you already have an nginx gateway, just copy the nginx config as-is. **If you're spinning up a fresh box and want to skip manual TLS work, go with Caddy** — put your real domain in the site label and Caddy automatically obtains and renews a Let's Encrypt certificate. This is also why the repo's [Containers & Multi-Replica](/guide/deployment/docker) delivery defaults to Caddy.
:::

## nginx

```nginx
server {
    listen 80;
    server_name admin.example.com;

    # Upload size cap must be >= TenonAdmin:Upload:MaxSizeMb (default 20MB);
    # nginx defaults to 1m, so without this large uploads get a 413 instead of the kernel's own error code.
    client_max_body_size 32m;

    root /var/www/tenon;          # contents of web/dist
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;   # SPA history-mode fallback
    }

    location /api/ {
        proxy_pass http://127.0.0.1:5000;   # backend listen address
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

## Caddy

Use a real domain as the site label and Caddy will auto-issue/renew a certificate; only use `:80` locally or when you have no domain. This is the **bare-metal install** version, following the same approach as the `web/Caddyfile` inside the [Containers & Multi-Replica](/guide/deployment/docker) container.

```
admin.example.com {
    # Upload size cap must be >= TenonAdmin:Upload:MaxSizeMb (default 20MB)
    request_body {
        max_size 32MB
    }

    # Must use handle blocks — don't put reverse_proxy and try_files at the same top level:
    # Caddy executes by built-in directive order (not the order you wrote them), and try_files
    # belongs to the rewrite phase, which runs before reverse_proxy.
    # Left flat, a path like /api/... that doesn't exist on disk gets rewritten to /index.html by
    # try_files first, so the API request gets back an HTML page — the frontend can't reach the
    # backend at all (observed in practice: login just returns empty).
    handle /api/* {
        reverse_proxy 127.0.0.1:5000
    }

    # Probe: hit this directly from your orchestration layer / load balancer
    handle /health* {
        reverse_proxy 127.0.0.1:5000
    }

    # Fallback: SPA history-mode routing — anything that doesn't match a static file goes to the SPA
    # (so a refresh on a deep link doesn't 404)
    handle {
        root * /var/www/tenon
        try_files {path} /index.html
        file_server
    }
}
```

::: warning Both configs still need one more step
Writing `X-Forwarded-For` in the proxy (nginx) or having `reverse_proxy` add it automatically (Caddy) **isn't enough by itself** — the kernel also has to be told to trust it. See "Behind a reverse proxy: getting the kernel the real client IP" below. Without it, the backend always sees the proxy's single IP: **every user shares the same rate-limit bucket** (one person hammering the login endpoint can lock everyone else out), per-IP brute-force protection is neutralized, and the IP column in login logs is just the proxy's address.
:::

## Behind a reverse proxy: getting the kernel the real client IP

Behind any reverse proxy (nginx / Caddy / Traefik / a k8s ingress), this block is required:

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

- **Off by default.** Turning it on when you're *not* actually behind a proxy lets anyone forge their own IP.
- **Turning it on requires declaring trusted sources** (`KnownProxies` for specific IPs, `KnownNetworks` for CIDR ranges — under container orchestration the proxy's IP isn't fixed, so a range is more practical). Give neither, and **startup fails outright**. This isn't pedantry: blindly trusting `X-Forwarded-For` is worse than not parsing it at all — an attacker can forge a different IP on every request to open unlimited rate-limit buckets (**completely bypassing rate limiting**), and pin brute-force failures on someone else.
- What's trusted is the **source address**, so: **anyone who can connect directly to the backend port can forge an IP**. Once you're behind a reverse proxy, don't expose the backend port — that's why the debug port in the compose file is bound to `127.0.0.1` only.

