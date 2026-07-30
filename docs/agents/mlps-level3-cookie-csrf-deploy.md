# Level3 Cookie / CSRF 部署模型（一期）

Level3 强制 `HttpOnly` refresh Cookie + 可读 `tenon_csrf` 双提交头。浏览器只能读取**当前文档源**的 Cookie，因此：

## 模型 A：同源反代（推荐）

- SPA 与 API 同一 origin（例如 `https://admin.example.com` 反代 `/` → 前端、`/api` → 后端）。
- `TenonAdmin:Api:Cors:AllowedOrigins` **保持为空**。
- `TenonAdmin:Security:Level3:CookieDomain` **保持为空**（host-only Cookie）。
- 预检 `cookie_csrf_topology` = Pass。

## 模型 B：跨源 + 共享 Cookie Domain

仅在必须分域时使用（`https://admin.example.com` + `https://api.example.com`）：

1. `TenonAdmin:Api:Cors:AllowedOrigins` = `["https://admin.example.com"]`（显式列表，禁止 `*`）
2. `TenonAdmin:Api:Cors:AllowCredentials` = **true**（必配；false 时浏览器不带 Cookie）
3. `TenonAdmin:Security:Level3:CookieDomain` = `.example.com`（SPA 与 API 公共父域）
4. 入口必须 HTTPS（Cookie `Secure` + 跨源时 `SameSite=None`）
5. 前端 `VITE_API_BASE=https://api.example.com`，请求 `credentials: 'include'`

预检 `cookie_csrf_topology` 在以下任一情况 **critical fail**：

- 配置了 CORS origins 却未设置 CookieDomain
- 跨源拓扑下 `AllowCredentials=false`

## 明确不支持

- 跨源 + host-only Cookie（无 CookieDomain）
- 跨源 + CookieDomain 但 `AllowCredentials=false`
- 通配 `AllowAnyOrigin` + credentials
