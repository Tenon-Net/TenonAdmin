# 路线 C：真跨源（CDN）

前端与 API 使用不同源时，例如 `https://admin.example.com` 和 `https://api.example.com`，浏览器会执行跨源访问检查。需要同时告诉前端 API 地址，并让后端允许该前端源访问。源由协议、域名和端口共同决定，配置时应与实际访问地址一致。

**前端**：构建期给出 API 源，API 调用不必改代码。

```bash
VITE_API_BASE=https://api.example.com npm run build
```

**后端**：放行该源。默认 deny-all，不配就是跨源请求全被拦下。

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

`AllowedOrigins` 为空，就是不放行任何跨源。`AllowCredentials` 只在 origins 非空时才生效，不存在 `AllowAnyOrigin + 凭证` 这种组合。CORS 策略由内核的 `IStartupFilter` 自动挂在管道前段，**不需要你手写 `UseCors`**。

还需检查图片地址。签名直链 `viewUrl` 默认是相对路径，由 `FileUrlSigner.BuildUrl` 生成；直接作为 `<img src>` 使用时，会相对当前前端源解析。若该源没有转发 `/api/*`，图片请求会失败。可在 CDN 侧将 `/api/*` 回源到 API 域名，或[替换 `IFileUrlSigner`](/zh/guide/replace-service)生成绝对 URL。

部署后分别验证登录、预检请求和上传图片预览。在浏览器网络面板确认请求发往预期的 API 地址；API 可用而图片失败时，先检查 `viewUrl` 最终解析出的地址。

