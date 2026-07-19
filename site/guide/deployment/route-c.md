# Route C: True Cross-Origin (CDN)

That `blocked by CORS policy` line in the browser console is something only this route actually runs into — the other two are same-origin, so CORS never enters the picture. Clearing it takes one change on each side; do only one and the console stays red.

**Frontend**: supply the API origin at build time. No API call has to change.

```bash
VITE_API_BASE=https://api.example.com npm run build
```

**Backend**: allow that origin. It is deny-all by default, so leaving it unconfigured blocks every cross-origin request.

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

Images still break with CORS working. The signed direct link in `viewUrl` is a **relative** path (`FileUrlSigner.BuildUrl`), so cross-origin an `<img src>` resolves against the CDN, not the API domain, and every avatar and inline image is a dead link. Either have the CDN forward `/api/*` to the API origin, or [swap `IFileUrlSigner`](/guide/replace-service) to emit absolute URLs.
