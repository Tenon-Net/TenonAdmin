# 开发代理与 CORS

类型化客户端(`src/api/client.ts`)和 `gen:api` 都默认浏览器是同源访问 `/api` 和 `/openapi` 的 —— `client` 的 `baseUrl` 默认为空,`gen:api` 拉取 `/openapi/v1.json` 时用的也是一个看起来相对的 URL,两者都没做任何跨域处理。本地开发时后端跑在 `:5100`,dev server 跑在 `:5173`,端口不一样,总得有个东西把这道缝补上,两边才能正常工作。

补这道缝的就是 `vite.config.ts` 里的 dev 代理:

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
},
```

它把 `:5173` 上的 `/api/*`、`/openapi/*` 请求转发给后端,浏览器自始至终只看到一个源。目标地址默认是 `http://localhost:5100`;后端跑在别处时,启动 Vite 前设置 `TENON_API_TARGET` 即可。

没有这层代理,类型化客户端的请求和 `gen:api` 的 schema 拉取都会直接打到后端的源上 —— 而后端 CORS 默认 deny-all,浏览器(或者 `gen:api` 的 fetch)会在响应传到 `unwrap` 或 `openapi-typescript` 之前就把它拒了。是这层代理让请求层"同源"这个前提在本地成立;生产环境下则是反向代理扮演同样的角色。

完整代理配置与联调别名见[项目结构与启动](/zh/frontend/structure)。

**上一节:** [类型化客户端与两个中间件](/zh/frontend/request/client)
**下一节:** [对接后端响应](/zh/frontend/request/backend)
