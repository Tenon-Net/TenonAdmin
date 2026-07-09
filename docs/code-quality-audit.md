# 代码质量审查报告

> 审查时间：2026-07-08　范围：`backend/`（.NET 10 内核）+ `web/`（Vue 3 模板）当前 `dev` 分支。
> 结论先行：**整体质量高**，缓存与性能设计成熟、后端注释优秀、无数据库轮询。存在若干**缓存失效遗漏**与**面向未来业务的安全提示**需要关注；前端注释密度偏低。
> 规范与新建业务见 [`coding-standards.md`](./coding-standards.md) / [`new-business-guide.md`](./new-business-guide.md)。

---

## 一、直接回答你的四个问题

### 1. 缓存使用率如何？是不是每次查询都去操作数据库？

**不是。** 系统采用成熟的 **读穿透（cache-aside）+ 显式失效** 模型，和你 Simple 项目“把不常变的数据缓存起来”的思路一致，甚至更严谨（变更即精确失效，而非只靠 TTL）。

**每请求热路径（受保护接口）在缓存预热后 = 0 次数据库往返：**

| 热点数据 | 是否缓存 | 逻辑键 | 失效时机 |
|---|---|---|---|
| 会话活跃态 | ✅ | `session:{sid}` | 登出/强退即移除 |
| 用户权限码 | ✅ | `perm:{userId}` | 角色/授权变更即失效 |
| 用户数据范围 | ✅ | `scope:{userId}` | 角色/范围变更即失效 |
| 系统配置值 | ✅ | `config:{key}` | 配置增删改即失效 |
| 字典项列表 | ✅ | `dict:{typeCode}` | 字典增删改即失效 + 广播事件 |
| 验证码 / 登录失败计数 | ✅ | `captcha:*`/`loginfail:*` | 一次性消费 / 窗口过期 |
| 门户模块列表 / 菜单树 | ✅ | `portal:mod:{uid}:{gen}`/`portal:menu:{uid}:{mid}:{gen}` | 菜单/模块/角色-菜单/用户-角色变更即自增 `portal:gen` 整体失效（2026-07-09) |

- 一次受保护请求的鉴权（会话校验 + 权限码 + 数据范围）**全部走缓存**；超管数据范围直接取 JWT claim，**零查库**。冷缓存首次约 6–8 条查询播种，之后为 0。
- 默认 `MemoryCacheProvider`（进程内 `IMemoryCache`）；多实例共享已提供 **`TenonAdmin.Caching.Redis`** 可选包（StackExchange.Redis 实现），`AddTenonAdminRedisCache` 前置注册即整体替换，业务代码零改动（详见 [`coding-standards.md`](./coding-standards.md) §1.8）。
- 后台管理的**分页/列表查询不缓存**（直接查库），这是对的——它们低频且数据要实时。

### 2. 有没有轮询数据库的行为？

**没有，一处都没有。** 全代码库搜索 `Timer`/`PeriodicTimer`/`while(true)`/`Task.Delay`/`Thread.Sleep` **零命中**。

- 仅有的两个 `IHostedService`：`DatabaseInitializer`（启动时建表+播种，跑一次）、`CacheChangeLogSubscriber`（事件订阅者）。
- 事件总线 `ChannelEventBus` 是 `await foreach` 读通道（有消息才醒），是事件驱动，不是轮询。
- 前端同样零轮询：无 `setInterval`/`setTimeout` 循环/`useIntervalFn`/WebSocket/EventSource。

### 3. 前后端注释是否详细？

**后端：优秀（保持即可）。前端：偏低（建议补 `.vue` 视图层）。**

| 指标 | 后端 `.cs` | 前端 `.ts` / `.vue` |
|---|---|---|
| 整行注释占比 | **~25–29%**（非空行约 31%） | `.ts` ~8% / `.vue` ~2% |
| 公共成员 XML 文档 | 服务/接口/选项/控制器接近 100% | — |
| 设计文档/工单引用（§N、P#-##） | 245 处（少见的好习惯） | 少量 |
| 注释质量 | 讲 WHY（并发、事务顺序、跨方言坑），无复述填充 | 有则质量高，但覆盖面窄 |

- 后端不是“每行一注”，但**每个公共类型都有讲清意图与边界的 `<summary>`**，关键取舍引设计节号——比典型企业级 C# 高一个档次。未注释的多是自解释 DTO 属性（已有 `ColumnDescription`）与单方法框架接口实现，属刻意省略。
- 前端 store/composable/router 的头部块注释很好（`stores/auth.ts`、`router/index.ts` 是范例），但 **`.vue` 视图脚本块偏薄**：`views/system/menu/index.vue`（287 行仅 2 条注释）、`views/system/user/index.vue`、`layouts/AppHeader.vue` 的登出流程等复杂逻辑缺 WHY 注释；6/53 文件零注释（其中 `views/module/index.vue`、`views/personal/profile.vue` 含真实分支逻辑，值得补）。

### 4. 系统性能怎么样？

**好。** 鉴权热路径 0 查库、无轮询、无 N+1（菜单/模块树是小表整载内存计算，非逐条查库；唯一的按项循环是登录时踢并发会话，N 有界）。缓存命中即返回对象引用（进程内不序列化）。多实例时注意配置不同的雪花 `WorkerId` 并切 Redis 缓存。

---

## 二、需要关注的问题（按严重度）

> 均已核对源码确认。多数是“为后续开发打基础”应先补齐的项，而非线上事故。

### ✅ 已修复（原 🔴 高）—— `DataEntity` 写路径越权（IDOR）

- **原现象**：数据范围全局过滤器只作用于查询（SELECT），不作用于按主键的 `Update`/`Delete`，继承 `DataEntity` 的机构隔离业务表可被越权改删他机构行。
- **修复（2026-07-09）**：`SqlSugarRepository` 对 `IOrgScoped` 实体的 `UpdateAsync`/`DeleteAsync` 内置**写路径范围守卫**——写前经带范围过滤器的查询确认目标行在当前数据范围内，越权写返回 `0` 拒绝（`BaseEntity` 编译期静态短路，零开销）。**默认安全**，开发者无需记范式。
- **配套**：消费方 DataEntity CRUD 范本 `backend/tests/TenonAdmin.TestHost/SampleDoc*`；数据层越权测试 `DataScopeTests.Write_path_blocks_cross_org_update_and_delete` + HTTP 端到端 `SampleDocScopeTests`（经真实授权管道的范围解析）。详见[新建业务指南](./new-business-guide.md) A1。

### ✅ 已修复（原 🟠 中）—— 三处缓存/会话失效遗漏（现"授权变更即时生效"）

- **原现象**：设计宣称"授权变更即时生效"，但以下三条变更路径**没有失效对应缓存**，陈旧最长持续到 TTL（默认 20 分钟；若把 `PermissionMinutes` 配成 0 则**永不自动过期**）：

| 变更操作 | 原遗漏 | 证据 |
|---|---|---|
| **停用/删除用户** | 不吊销会话；`RolePermissionAttribute` 也不复查 `Enabled` → 持有的当前 access token 到期前仍可用（刷新已被 `RefreshAsync` 挡） | `UserService.cs:SetEnabledAsync` |
| **改/删菜单** | `MenuService` 未注入缓存；改菜单的 `Permission`/`Enabled` 不失效 `perm:{userId}` → 在线用户最长 20 分钟仍按旧权限 | `MenuService.cs` |
| **改/删机构树** | `OrgService` 未失效 `scope:{userId}` → 重挂机构后"本机构及以下"范围最长 20 分钟陈旧 | `OrgService.cs` |

- **修复（2026-07-09）**，均复用现有基建、机制级、低频过量失效无害：
  - **停用/删除用户** → `ISessionService.RevokeAllForUserAsync(userId)`（仿 `EnforceConcurrencyAsync` 按 userId 查活跃会话逐个吊销），`UserService` 的 `SetEnabledAsync(false)`/`UpdateAsync(Enabled=false)`/`DeleteAsync` 调用之 → 原令牌下次请求即 401。
  - **改/删菜单** → `IRbacService.InvalidatePermissionsByMenuAsync(menuId)`（菜单→角色→用户扇出，复用私有 `InvalidatePermissionsAsync`），`MenuService.UpdateAsync`/`DeleteAsync` 调用之。
  - **改/删机构树** → `IRbacService.InvalidateAllScopesAsync()`（受影响集难精确圈定，机构变更极低频 → 直接失效全体用户 `scope`；ponytail 注明"用户量巨大 + Redis 时收窄或改 scope 代际键"的升级路径），`OrgService.AddAsync`/`UpdateAsync`/`DeleteAsync` 调用之。
- **配套**：HTTP/服务级回归 `CacheInvalidationTests`（停用→原令牌 401、改菜单→同会话即 403、动机构→scope 键即清）。

### 🟠 中 —— 新建业务缺脚手架 + 权限码易漂移

- ~~**无代码生成**：一个最小 CRUD 模块要手改 ~8 个文件跨 4 个工程~~ → **✅ 已加脚手架（2026-07-09）**：`dotnet new tenon-app -n Shop` 一键生成**可运行**的消费方 host（预接线 + 一个机构隔离示例业务模块 `Modules/SampleDoc*` 四件套=复制范本），`dotnet run` 即起。模板包 `TenonAdmin.Templates`（仓库根 `templates/`，刻意置于 `backend/` 外以不继承 net10/中央版本管理；随 release 单独 `dotnet pack` 一并推）；冒烟 `templates/smoke-test.ps1`（pack→install→scaffold→build 全绿）。注:内核内「加 Sys* 跨 4 工程」仍手抄（路线 A 本质如此），模板面向消费方（路线 B,4 文件 + 2 行接线)。
- **权限码手工双写**：控制器路由（真源）与 `DefaultMenuSeed.Permission` 字符串手工同步。~~`PermissionCodeConsistencyTests` 只校验“种子→端点”，不校验反向~~ → **✅ 已加反向锁（2026-07-09）**：`Every_permission_endpoint_is_seeded_or_explicitly_known_unseeded` 断言每个 `[RolePermission]` 端点要么有种子菜单节点、要么显式登记在 `KnownUnseededEndpoints`（当前 28 个仅超管可用的端点，随前端 M2 菜单树落地而缩小）。新增受权端点若既无菜单又不登记 → 测试红，杜绝“静默 403”无声漂移。（`dotnet new tenon-module` 脚手架/源生成器仍为独立待办。）
- ~~**`ScanApplicationAssemblies` 是误导性空开关**：默认 `true` 但未实现，只有 `ApplicationAssemblies.Add(...)` 真正生效~~ → **✅ 已处理（2026-07-09）**：标 `[Obsolete]` 退役（承认从未实现、代码无一处读取；引导改用 `ApplicationAssemblies.Add`）。选退役而非实现——守内核"显式、可预测、无魔法"取向,`ApplicationAssemblies` 已是文档化且被测的唯一正道。同步更新设计文档 §5.7 与新建业务指南。

### 🟡 低

- ~~**门户菜单树/模块列表未缓存**~~ → **✅ 已加缓存（2026-07-09）**：`MenuService.GetMyModulesAsync`/`GetMyMenuTreeAsync` 按 `(userId[,moduleId])` 读缓存(cache-aside + `PermissionMinutes` TTL 兜底)。失效用**门户代际计数** `portal:gen`——因 `ICacheProvider` 无前缀删除、菜单树键又是二维,菜单/模块 CRUD、角色-菜单、用户-角色变更各自增一次 `portal:gen` 令门户缓存整体惰性失效(O(1),旧代际键由 TTL 回收)。**全局代际=任一门户写失效全体门户缓存**;门户写低频、重算仅几条小表查询,过量无害(ponytail 注明可升级到 per-user 代际)。回归 `ModulePortalTests` 三例(模块增/授权/菜单增各测即时反映)。
- **前端 `v-auth` 尚未接线**：指令已实现并全局注册，但**视图里零调用**，且后端暂无“按钮权限码”接口 → 当前 **fail-open**（不隐藏），强制全靠服务端 403。补 `/personal/permissions` 后生效。写按钮鉴权前需知道这点。
- **前端表格列 i18n 不一致**：`module`/`menu` 页列标题用响应式 `() => t(key)`，`user` 页用一次性 `t(key)`——切换语言时 user 页列头不重译。统一成工厂形式。
- **前端注释密度**：见问题 3，重点补 `views/` 脚本块。
- **种子 Id 手工分配无登记表**：`DefaultMenuSeed` 手编 1–49，新增易撞主键。建议约定每模块 Id 段或加启动去重检查。

---

## 三、结论

- **可以放心在此基础上继续开发。** 架构分层、可替换性、缓存/性能、后端注释都达到了产品级内核的水准。
- **原“动业务代码前优先处理”的高/中危项已全部于 2026-07-09 清零**：🔴 `DataEntity` 写路径 IDOR、🟠 三处缓存/会话失效遗漏、🟠 权限码反向一致性校验均已修复。剩余为 🟡 低（前端注释/`v-auth` 接线/列 i18n）；独立增强（脚手架模板 `dotnet new tenon-app`、`ScanApplicationAssemblies` 退役、门户读缓存）均已于 2026-07-09 完成。
- **持续改进**：前端 `.vue` 注释、`v-auth` 接线、前端列 i18n 一致性。

### 建议的后续任务清单

- [x] 停用/删除用户时吊销其会话（`ISessionService.RevokeAllForUserAsync`，2026-07-09）
- [x] 菜单变更时失效受影响用户的 `perm:{userId}`（`IRbacService.InvalidatePermissionsByMenuAsync`，2026-07-09）
- [x] 机构树变更时使 `scope` 缓存失效（`IRbacService.InvalidateAllScopesAsync`，全体失效；epoch 方案留作大规模升级路径，2026-07-09）
- [x] 提供 `DataEntity` 机构隔离 CRUD 参考模块 + 写路径范围守卫（机制级默认安全，2026-07-09）
- [x] `PermissionCodeConsistencyTests` 加反向锁：端点须有菜单节点或显式登记 `KnownUnseededEndpoints`（2026-07-09）
- [x] 处理 `ScanApplicationAssemblies` 空开关（标 `[Obsolete]` 退役，引导用 `ApplicationAssemblies.Add`，2026-07-09）
- [x] 门户菜单/模块读加缓存（代际计数失效，2026-07-09）
- [x] 新建业务脚手架 `dotnet new tenon-app`（生成可运行 host + 机构隔离示例模块;模板包 `TenonAdmin.Templates`,冒烟 `templates/smoke-test.ps1`,2026-07-09）
- [ ] 前端 `.vue` 视图补 WHY 注释；统一列标题 i18n 工厂形式
- [ ] （规划中）后端 `/personal/permissions` 接口 → 前端 `v-auth` 真正生效
