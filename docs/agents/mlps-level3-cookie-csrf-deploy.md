# Cookie / CSRF 部署模型（可选会话）

> **ADR 0006：** 这是可选能力，由 `TenonAdmin:Security:Session:CookieMode=true` 启用；**不是**等保 Level3 强制档。  
> 历史键 `Security:Level3:CookieDomain` 仍可读作回退；新部署请用 `Session:CookieDomain`。  
> 完整键名见 [security-optional-config.md](security-optional-config.md)。

`CookieMode` 下：refresh → `HttpOnly` Cookie + 可读 `tenon_csrf` 双提交头。浏览器只能读取**当前文档源**的 Cookie，因此：

## 模型 A：同源反代（推荐）

- SPA 与 API 同一 origin（例如 `https://admin.example.com` 反代 `/` → 前端、`/api` → 后端）。
- `TenonAdmin:Api:Cors:AllowedOrigins` **保持为空**。
- `TenonAdmin:Security:Session:CookieDomain` **保持为空**（host-only Cookie）。

## 模型 B：跨源 + 共享 Cookie Domain

仅在必须分域时使用（`https://admin.example.com` + `https://api.example.com`）：

1. `TenonAdmin:Api:Cors:AllowedOrigins` = `["https://admin.example.com"]`（显式列表，禁止 `*`）
2. `TenonAdmin:Api:Cors:AllowCredentials` = **true**（必配；false 时浏览器不带 Cookie）
3. `TenonAdmin:Security:Session:CookieDomain` = `.example.com`（SPA 与 API 公共父域）
4. 入口必须 HTTPS（Cookie `Secure` + 跨源时 `SameSite=None`）
5. 前端 `VITE_API_BASE=https://api.example.com`，请求 `credentials: 'include'`

## 明确不支持

- 跨源 + host-only Cookie（无 CookieDomain）
- 跨源 + CookieDomain 但 `AllowCredentials=false`
- 通配 `AllowAnyOrigin` + credentials

## 前端注意

合入后若仅后端打开 `CookieMode`，前端须使用内存 access token、Cookie 静默刷新与写请求 `X-Tenon-CSRF`。未对齐前请保持 `CookieMode=false`（默认 body/localStorage）。
