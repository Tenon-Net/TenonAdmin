# HTTP 请求层

前端每一次后端调用都走同一条流水线:后端 OpenAPI 生成的 schema 给客户端上类型,两个中间件分别管认证和令牌刷新,`unwrap` 把后端可能返回的两种响应形状都收拢成统一结果,视图层才拿到手。本页按顺序拆开每一环。

## 全景

```text
后端 OpenAPI (/openapi/v1.json)
  │  npm run gen:api
  ▼
src/api/schema.d.ts        生成的类型(paths,禁止手改)
  │
  ▼
src/api/client.ts          带类型的 openapi-fetch 客户端 + 认证/刷新中间件
  │
  ▼
src/api/index.ts           按领域分组的 API 函数,统一形态:client.X(...).then(r => unwrap<T>(r))
  │
  ▼
views                       catch ApiError,经 translateError(err) 展示
```

## 重新生成契约:`gen:api`

```bash
npm run gen:api   # openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts
```

- 后端必须先跑起来 —— 脚本要向一个真实运行中的服务器拉 `/openapi/v1.json`(默认 `http://localhost:5100`;后端跑在别处时看 `web/vite.config.ts` 的 `TENON_API_TARGET` 对应的 dev 代理目标)。
- `src/api/schema.d.ts` 是**生成产物**,禁止手改 —— 改后端的接口/DTO,重新生成即可;手改的内容下次生成会被无声覆盖。
- `src/api/client.ts` 的 `createClient<paths>()` 就是拿这份文件当类型源,所以每一次 `client.GET/POST/PUT/DELETE` 调用从路径参数、查询参数、请求体到响应形状,全链路都是后端真实契约推出来的类型。

## 本节内容

- [类型化客户端与两个中间件](/zh/frontend/request/client) —— `client.ts` 的两个中间件:认证与 401 刷新
- [开发代理与 CORS](/zh/frontend/request/proxy) —— 为什么请求层离不开 dev 代理
- [对接后端响应](/zh/frontend/request/backend) —— `unwrap`、`ApiError`、分页助手与错误文案

**下一节:** [类型化客户端与两个中间件](/zh/frontend/request/client)
