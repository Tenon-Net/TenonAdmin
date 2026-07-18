# 路线 C：真跨源（CDN）

前端在 CDN / 独立域名。只有这种情况才需要动 CORS，而且**两端都要配**:

前端——构建期给出 API 源（不必改代码）:

```bash
VITE_API_BASE=https://api.example.com npm run build
```

后端——放行该源（默认 deny-all，不配就是浏览器全部被拦）:

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

`AllowedOrigins` 为空 = 不放行任何跨源;`AllowCredentials` 只在 origins 非空时才生效（不存在 `AllowAnyOrigin + 凭证` 这种组合）。CORS 策略由内核的 `IStartupFilter` 自动挂载在管道前段，**不需要你手写 `UseCors`**。

