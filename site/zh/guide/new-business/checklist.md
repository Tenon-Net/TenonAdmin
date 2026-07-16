# C. 端到端清单

**后端**
- [ ] 实体(选 `BaseEntity`/`DataEntity`)+ Sugar 特性 + 唯一索引
- [ ] `*Models.cs` DTO(record)
- [ ] `I*Service` + `*Service`(virtual、事务、查重带软删)
- [ ] `ServicesSetup` 里 `TryAddScoped` 注册
- [ ] 控制器(`[ApiController]`/`[Route]`/`[Module]`,每动作 `[RolePermission]`)
- [ ] `ErrorCode` 加码
- [ ] 热读才加缓存(`CacheKeys` + cache-aside + 失效)
- [ ] 种子(可选,固定 Id)
- [ ] 测试(`WebApplicationFactory`,SQLite/MySQL 双绿)

**前端**
- [ ] `npm run gen:api` 重生成类型
- [ ] `api/index.ts` 加一组
- [ ] `views/<模块>/<实体>/index.vue`(`useTable` + Naive 表格/表单)
- [ ] i18n 文案 + 错误码翻译
- [ ] `lint` + `typecheck` 通过

**配置权限(运行时)**
- [ ] 菜单管理建节点(Path/Component 对应)
- [ ] 角色管理勾选授权

**上一节:** [B. 前端](/zh/guide/new-business/frontend)
