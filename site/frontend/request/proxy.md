# Dev Proxy & CORS

The typed client (`src/api/client.ts`) and `gen:api` both assume the browser is talking same-origin to `/api` and `/openapi`. Neither makes any cross-origin allowance — `client`'s `baseUrl` defaults to empty, and `gen:api` fetches `/openapi/v1.json` as a plain relative-looking URL. Locally, the backend runs on a different port (`:5100`) than the dev server (`:5173`), so something has to bridge that gap before either can work.

That something is `vite.config.ts`'s dev proxy:

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
},
```

It forwards `/api/*` and `/openapi/*` requests from `:5173` to the backend, so the browser only ever sees one origin. The target defaults to `http://localhost:5100`; set `TENON_API_TARGET` before starting Vite to point at a backend running elsewhere.

Without this proxy, both the typed client's requests and `gen:api`'s schema fetch would go directly to the backend's origin — and the backend's CORS policy defaults to deny-all, so the browser (or `gen:api`'s fetch) would reject the response before it reached `unwrap` or `openapi-typescript`. The proxy is what makes the request layer's same-origin assumption true in dev; in production a reverse proxy plays the same role.

See [Project Structure & Startup](/frontend/structure) for the full dev-proxy config and sibling-package aliases.

**Previous:** [The Typed Client](/frontend/request/client)
**Next:** [Adapting to the Backend Response](/frontend/request/backend)
