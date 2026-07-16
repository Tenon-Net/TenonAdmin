# Route C: True Cross-Origin (CDN)

Frontend on a CDN / separate domain. Only this scenario needs CORS, and **both sides need configuring**:

Frontend — supply the API origin at build time (no code changes needed):

```bash
VITE_API_BASE=https://api.example.com npm run build
```

Backend — allow that origin (deny-all by default; leave it unconfigured and every browser request is blocked):

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

`AllowedOrigins` empty = no cross-origin requests are allowed; `AllowCredentials` only takes effect when origins is non-empty (there's no such thing as `AllowAnyOrigin` combined with credentials). The CORS policy is mounted automatically at the front of the pipeline by the kernel's `IStartupFilter` — **you don't need to write `UseCors` yourself**.

**Previous:** [Route B: Reverse Proxy (nginx or Caddy)](/guide/deployment/route-b)
**Next:** [Route D: Docker](/guide/deployment/route-d)
