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
| 门户模块列表 / 菜单树 | ❌ | — | 每次登录/切应用/刷新查库（见问题遗漏项） |

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

### 🔴 高 —— `DataEntity` 写路径越权（IDOR），面向即将开发的业务表

- **现象**：数据范围全局过滤器**只作用于查询（SELECT）**，不作用于按主键的 `Update`/`Delete`。对继承 `DataEntity` 的**机构隔离业务表**，直接 `repo.DeleteAsync(id)`/`UpdateAsync(dto)` 不带范围谓词，**能改删他机构的行**。
- **现状**：当前内核 CRUD 模块（Position/Org/Menu/Module/User）都用 `BaseEntity`，**尚未触发**；代码作者已在 `DataEntity.cs:25-29`（P2-21）明确记录此坑。属**潜在**风险，但你接下来写的第一张机构隔离业务表就会踩到。
- **建议**：① 业务服务的改/删**先经 `AsQueryable()`（带范围过滤）读到再写**（看不到即拒），这是内置服务的范式，已写入[新建业务指南 A1]；② 补一个 `DataEntity` 的 CRUD 参考模块作抄写样板；③ 评估“写路径自动补范围谓词”的机制级兜底（作者已把它列为待办）。

### 🟠 中 —— 三处缓存/会话失效遗漏（授权变更并非全都“即时生效”）

设计宣称“授权变更即时生效”，但以下三条变更路径**没有失效对应缓存**，陈旧最长持续到 TTL（默认 20 分钟；若把 `PermissionMinutes` 配成 0 则**永不自动过期**）：

| 变更操作 | 遗漏 | 证据 | 影响 |
|---|---|---|---|
| **停用用户** `SetEnabledAsync(id,false)` | 不吊销会话、不清权限/范围缓存；`RolePermissionAttribute` 也不复查 `Enabled` | `UserService.cs:150-158` | 被停用者持有的当前 access token 到期前仍可正常调接口（刷新已被挡） |
| **改/删/停用菜单** | `MenuService` 根本没注入 `ICacheProvider`；改菜单的 `Permission`/`Enabled` 不失效 `perm:{userId}` | `MenuService.cs:91-140` | 改路由权限码/停用菜单后，已在线用户最长 20 分钟仍按旧权限 |
| **改/删机构树** | `OrgService` 未失效 `scope:{userId}`；且 `ICacheProvider` 只有按键 `RemoveAsync`，无批量/前缀清除 | `OrgService.cs:47-72` | 重挂机构后“本机构及以下”范围最长 20 分钟陈旧 |

- **建议**：停用用户→复用会话吊销路径（给 `ISessionService` 加 `按 userId 吊销`）；菜单变更→复用 `RbacService` 的“菜单→受影响用户→失效 `perm`”扇出；机构树→引入 `scope` 版本号/epoch 键，变更时自增使缓存惰性重算。（若短期不改，至少把“20 分钟有界陈旧”写进文档，并**不要**把 `PermissionMinutes` 设为 0。）

### 🟠 中 —— 新建业务缺脚手架 + 权限码易漂移

- **无代码生成**：一个最小 CRUD 模块要手改 ~8 个文件跨 4 个工程，每个域都是手抄兄弟域。建议做 `dotnet new tenon-module` 模板或源生成器。
- **权限码手工双写**：控制器路由（真源）与 `DefaultMenuSeed.Permission` 字符串手工同步。`PermissionCodeConsistencyTests` 只校验“种子→端点”，**不校验反向**——挂了 `[RolePermission]` 却没有菜单节点的端点会对普通用户静默 403，无测试无告警。建议加反向断言或启动时列出“无菜单节点的受权端点”。
- **`ScanApplicationAssemblies` 是误导性空开关**：默认 `true` 但未实现（`TenonAdminOptions.cs:31`），只有 `ApplicationAssemblies.Add(...)` 真正生效。建议实现或标 `[Obsolete]`。

### 🟡 低

- **门户菜单树/模块列表未缓存**（`MenuService.GetMyModulesAsync`/`GetMyMenuTreeAsync`）：每次登录/切应用/刷新查 3–4 条。频率是“导航级”非“请求级”，小表整载，影响有限——是整体缓存覆盖里唯一的刻意例外。要极致可按 `(userId,moduleId)` 缓存并在角色-菜单变更时失效。
- **前端 `v-auth` 尚未接线**：指令已实现并全局注册，但**视图里零调用**，且后端暂无“按钮权限码”接口 → 当前 **fail-open**（不隐藏），强制全靠服务端 403。补 `/personal/permissions` 后生效。写按钮鉴权前需知道这点。
- **前端表格列 i18n 不一致**：`module`/`menu` 页列标题用响应式 `() => t(key)`，`user` 页用一次性 `t(key)`——切换语言时 user 页列头不重译。统一成工厂形式。
- **前端注释密度**：见问题 3，重点补 `views/` 脚本块。
- **种子 Id 手工分配无登记表**：`DefaultMenuSeed` 手编 1–49，新增易撞主键。建议约定每模块 Id 段或加启动去重检查。

---

## 三、结论

- **可以放心在此基础上继续开发。** 架构分层、可替换性、缓存/性能、后端注释都达到了产品级内核的水准。
- **动业务代码前，优先处理**：🔴 `DataEntity` 写路径校验范式（写进你的第一张业务表）、🟠 三处失效遗漏、🟠 权限码反向一致性校验。
- **持续改进**：前端 `.vue` 注释、`v-auth` 接线、脚手架模板。

### 建议的后续任务清单

- [ ] 停用用户时吊销其会话（`ISessionService` 加按 userId 吊销）
- [ ] 菜单变更时失效受影响用户的 `perm:{userId}`
- [ ] 机构树变更时使 `scope` 缓存失效（版本号/epoch 方案）
- [ ] 提供 `DataEntity` 机构隔离 CRUD 参考模块（含写前范围校验）
- [ ] `PermissionCodeConsistencyTests` 加“受权端点必须有菜单节点”反向断言
- [ ] 处理 `ScanApplicationAssemblies` 空开关（实现或标记 `[Obsolete]`）
- [ ] 门户菜单/模块读酌情加缓存
- [ ] 前端 `.vue` 视图补 WHY 注释；统一列标题 i18n 工厂形式
- [ ] （规划中）后端 `/personal/permissions` 接口 → 前端 `v-auth` 真正生效
