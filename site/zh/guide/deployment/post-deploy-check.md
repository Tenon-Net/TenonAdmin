# 部署后自检

```bash
curl https://<你的域名>/health         # Healthy(存活)
curl https://<你的域名>/health/ready   # Healthy(DB + 缓存都通)
curl -i https://<你的域名>/api/v1/ping # 401 = API 路由通了(该端点需要登录)
```

再打开前端登录一次,确认能拿到菜单(说明 JWT 密钥、数据库、种子都对)。

注意 `/openapi/v1.json` **只在 Development 环境挂载**——它是给前端 `npm run gen:api` 用的契约源,不是生产端点;生产下 404 是预期行为。

**上一节:** [多副本部署](/zh/guide/deployment/multi-replica)
