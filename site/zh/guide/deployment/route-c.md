# 路线 C：真跨源（CDN）

浏览器控制台里那行 `blocked by CORS policy`，只有这条路线会真的碰上。为什么？前两条前后端同源，轮不到 CORS 出场。放行要前后端各改一处，只改一边，照样是红的。

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

CORS 配通了，图片仍会全挂。原因是签名直链 `viewUrl` 是个**相对**路径，跨源下 `<img src>` 打到的是 CDN 源，头像和正文里的图就全成了坏链。这段拼 URL 的逻辑在 `FileUrlSigner.BuildUrl` 里。两个办法：要么 CDN 侧把 `/api/*` 回源到 API 域名，要么[替换 `IFileUrlSigner`](/zh/guide/replace-service)，改成拼绝对 URL。

