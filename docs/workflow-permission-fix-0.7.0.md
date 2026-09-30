# 0.7.0 工作流路由权限一致性修复

## 基线与根因

复现基线为 `v0.7.0`，源码 `63c773ffe2725a708390407466d6b696a49914d9`。本次从 `dev` 的 `f0aa399` 建立独立分支 `fix/workflow-permission-consistency`；两条后续提交只涉及 README 和 CI，权限实现未修复。既有未提交图片及其他用户文件保留，不提交、推送或发布。

`RolePermissionAttribute` 用 `PermissionCode.Build` 将 HTTP 方法大写、模板小写并补前导 `/`，与 `IPermissionProvider` 返回的字符串精确比较。控制器详情、版本、删除使用 `{id:long}`，outbox 重放使用 `{id:long}/replay`；种子却写成 `{id}`。普通账号已有角色菜单授权仍返回 `403 / 41001`，超管旁路掩盖了漂移。

全部调用链已核对：授权过滤器、`MenuController.Routes` 路由下拉、`OperationLogFilter` 操作名、Integration 的 `OpenApiCatalog`（及调用同规则的测试）。种子通过 RBAC 聚合后供后端和 `/personal/permissions` 使用，Vue `hasPerm` / `v-auth` 与 React `has` / `Can` 精确检查这些码。

## 方案与边界

选择修正工作流种子和两套模板的详情、删除按钮权限码，保持共享权限码的精确模板契约。没有剥离路由约束：剥离会把相同方法、路径、参数名但不同约束的接口合并授权，同时需要迁移消费者已通过路由下拉保存的带约束权限、MFA 高敏权限和开放接口目录。这个全局行为变化不适合作为本次修复。

修改后的四条码：

| 菜单 ID | 权限码 |
| --- | --- |
| 48003 | `GET:/api/v1/workflow/definition/{id:long}` |
| 48004 | `GET:/api/v1/workflow/definition/versions/{id:long}` |
| 48009 | `DELETE:/api/v1/workflow/definition/{id:long}` |
| 48042 | `POST:/api/v1/workflow/outbox/{id:long}/replay` |

参数名、复合约束、可选参数、默认值、单星和双星 catch-all 均继续保留在授权模板中；无约束权限码不变。`long` 与 `guid` 模板仍是不同权限。实际控制器路由、会话检查、数据范围和办理人校验不变，无通配授权。

## 存量数据库与缓存

`SysSchemaVersion.Current` 从 `8` 升到 `9`，复用 `WorkflowMenuSeed.SyncOnUpgrade`。启动时按原菜单 ID 更新结构种子，保留 `sys_role_menu`、`sys_user_role`、角色数据范围、定义和版本；不删除或重授角色关联。审计字段、软删除标记继续受现有升级保护。第二次启动版本相同，不再覆盖内置菜单的用户修改。

注意现有结构种子机制会在升级时同步所有声明 `SyncOnUpgrade=true` 的内置结构行，其标题、排序等自定义字段会恢复种子值；用户配置、字典、用户和角色不参与覆盖。生产升级前应照常备份并核对内置菜单定制。

权限缓存逻辑键使用新格式 `perm:{userId}:v2`，所有读取、授权变更和菜单变更的失效操作仍共用 `CacheKeys.UserPermissions`。旧 Redis 键 `perm:{userId}` 不再读取，不必清空 Redis、会话或用户授权；默认 TTL 后自然回收，若旧权限缓存配置为永不过期，可按既有应用前缀定向移除旧权限键以回收空间。门户树不包含按钮权限，菜单管理树实时读取，无需更改会话或门户缓存格式。

必须先停止全部旧版本副本，再启动使用同版本包的新副本，不能滚动混跑。旧二进制使用 `stored != Current` 判断升级，会将版本 `9` 当作需同步并回刷为 `8`，从而再次写入旧菜单码。启用种子时自动完成迁移。`EnableSeed=false` 或消费者自行维护菜单时，需通过既有菜单管理服务将上述四条菜单权限改为真实路由下拉给出的码（服务会精确失效缓存），保留菜单 ID 和角色授权；无需开启生产 CodeFirst 或改表结构。已登录浏览器需要重新加载权限集合（刷新页面或重新登录），并同步更新两套前端的对应按钮检查。

## 验证记录

修改文件：

| 文件 | 用途 |
| --- | --- |
| `backend/src/TenonAdmin.Workflow/WorkflowMenuSeed.cs` | 四个按钮种子采用完整约束模板 |
| `backend/src/TenonAdmin.SqlSugar/Entities/SysSchemaVersion.cs` | 结构种子版本 8 → 9 |
| `backend/src/TenonAdmin.Core/CacheKeys.cs` | 隔离旧权限缓存 |
| `backend/src/TenonAdmin.Core/Security/ICacheProvider.cs`、`backend/src/TenonAdmin.Caching.Redis/RedisCacheProvider.cs` | 同步缓存键示例文档 |
| `web/src/views/workflow/definition/index.vue`、`web-react/src/views/workflow/definition/index.tsx` | 详情、删除按钮与后端权限一致 |
| `web-react/src/views/workflow/definition/designer.spec.tsx` | 现有权限 fixture 同步，原断言保留 |
| `backend/tests/TenonAdmin.Tests/PermissionCodeConsistencyTests.cs` | 保留参数及约束、不同约束不合并的单元回归 |
| `backend/tests/TenonAdmin.Tests/WorkflowPermissionRegressionTests.cs` | 真实宿主生命周期、安全边界及旧库升级回归 |
| `backend/tests/TenonAdmin.Tests/WorkflowAppFactory.cs` | 升级用例保留测试库 |
| `web/e2e/workflow-permission.spec.ts` | 普通账号真实浏览器设计器回归 |
| `CHANGELOG.md`、本记录 | 修复、升级和验证说明 |

修复前，真实 MinimalHost、真实工作流菜单、普通账号的 Playwright 检查在创建草稿后的详情读取失败：`Expected: 200 / Received: 403`。没有替换权限提供器或关闭安全检查。

2026-09-30 本地验证，重任务逐项执行，无并行前后端构建：

| 命令（从仓库根执行，另注明目录） | 实际结果 |
| --- | --- |
| `dotnet restore backend/TenonAdmin.slnx` | 成功，无 restore 错误 |
| `dotnet build backend/TenonAdmin.slnx -c Release --no-restore` | 成功，0 warning / 0 error，28.50s |
| `dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter FullyQualifiedName~WorkflowPermissionRegressionTests` | 新增真实宿主回归 4/4，0 失败、0 跳过，13s |
| 下方后端相关回归命令 | 142/142，0 失败、0 跳过，34s；TRX 核对 executed=142、passed=142、notExecuted=0 |
| `cd web && npm run lint` | 成功 |
| `cd web && npm run typecheck` | 成功 |
| `cd web && npm run test -- src/views/workflow src/stores/auth.spec.ts` | 9 个文件，38/38 测试通过 |
| `cd web && npm run build` | 成功 |
| `cd web && npm run test:e2e -- workflow-permission.spec.ts` | 修复前详情 HTTP 403 导致失败；修复后 Chromium 1/1 通过，45.4s |
| `cd web-react && npm run lint` | 成功 |
| `cd web-react && npm run typecheck` | 成功 |
| `cd web-react && npm run test -- src/views/workflow src/stores/auth.spec.ts src/components/Can.spec.tsx` | 15 个文件，99/99 测试通过 |
| `cd web-react && npm run build` | 成功 |
| `git diff --check` | 成功 |

后端相关回归的完整命令：

```bash
dotnet test backend/TenonAdmin.slnx -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Workflow|FullyQualifiedName~PermissionCodeConsistencyTests|FullyQualifiedName~PermissionRoutesEndpointTests|FullyQualifiedName~AuthorizationTests|FullyQualifiedName~SeedUpgradeTests|FullyQualifiedName~CacheInvalidationTests|FullyQualifiedName~CacheEndpointTests|FullyQualifiedName~DataScopeTests|FullyQualifiedName~ReplaceabilityTests' \
  --logger 'console;verbosity=minimal' \
  --logger 'trx;LogFileName=workflow-security.trx' \
  --results-directory /tmp/tenon-workflow-test-results
```

新增后端回归首次执行时发现两个测试 fixture 问题：截取 GUID v7 的时间前缀导致同库角色编码重复，以及 outbox 重放请求未传必需的 `requestId`。分别改用 GUID v4 随机后缀、补合法 requestId 后重跑；未改产品业务规则或删除断言，原先宽松的“不是 41001”进一步收紧为精确的业务不存在错误码。最终相关回归无跳过。

脱敏证据（ID 仅用占位符，不记录令牌或口令）：

| 场景 | 回归确认的结果 |
| --- | --- |
| 普通、启用、非超管；仅定义管理导航与 page/detail/versions/add/update/publish 菜单 | page 成功；创建草稿 → GET `<definitionId>` → update → publish → GET versions 成功，版本列表含 1 个快照 |
| 相同最小角色去掉详情、版本两个菜单 | 两个请求均 HTTP 403 / code 41001 |
| 普通用户登出后复用原 bearer | HTTP 401 / code 40006 |
| 有效会话的非办理人尝试 approve `<taskId>` | code 48007（TaskConflict）；指定办理人随后成功，code 0 |
| 详情 URL 非 long / 超出 long 上限 | HTTP 404 |
| 仅授删除及重放菜单 | 到达各自 action，分别返回 DefinitionNotFound / OutboxNotFound，未触发 403 |
| 路由下拉与种子 | 真实宿主返回的 RolePermission 清单包含全部 6 条带参数的工作流种子权限（含 4 条修复项） |
| schema 8 / 旧四码 / 原角色授权 / 已发布定义 / 旧权限缓存 | 同一数据库升级到 9，四码修正，原菜单 ID 授权、非超管身份、定义和快照保留；新缓存包含 `:long`，旧缓存仍含旧码且未读取 |
| 升级后的再次启动 | 授权继续有效，升级后自定义的菜单标题保留 |
| 真实 Chromium 普通管理员设计器 | 新建草稿后详情 200/code 0，画布加载、刷新重载、发布成功，列表“设计”按钮可重新打开该定义 |

回归使用真实工作流宿主、真实菜单种子和 RBAC/Session/DataScope 服务。存量升级 fixture 在同一保留数据库中恢复 0.7.0 的四个原码与版本 `8`，保留账户、角色、授权、定义及已发布快照，再启动两次验证；共享 MemoryCacheProvider 模拟跨宿主保留的旧权限缓存，不把删库当作升级。只替换缓存存储，不替换权限提供器或安全检查。

## 验证范围与剩余风险

本次数据库集成实测为 SQLite；未实测 MySQL、PostgreSQL、SQL Server 的存量升级，也未连接真实 Redis。缓存逻辑键隔离已用跨宿主保留的真实 MemoryCacheProvider 验证，Redis 适配器共用这些逻辑键。未直接访问用户生产库或 tenon-example 的数据库，未使用发布后的正式 NuGet 包复验。浏览器实测覆盖 Vue 普通管理员设计器；React 通过相关单元、类型检查和构建，未做浏览器复验。

两套 Vite 构建仍提示部分 chunk 过大，Vue 另提示已有 WfFormMount 动静态导入并存；构建均成功，此次未调整无关打包结构。OpenAPI 与控制器路由未改，生成 schema 未手改。

## 正式包复验

代码涉及 `TenonAdmin.Workflow`（种子）、`TenonAdmin.SqlSugar`（种子版本）及 `TenonAdmin.Core`（权限缓存键）。正式发布需使用统一新版本发布这三个包，并更新其依赖链 `TenonAdmin.Services`、`TenonAdmin.AspNetCore` 和元包 `TenonAdmin`，避免消费者只更新 Workflow 却仍使用旧种子版本或缓存键。仓库发布工作流通常统一发布所有包；本次未执行发布或修改版本号。

`TenonAdmin.Caching.Redis` 本次仅修改键示例注释，缓存运行行为由 Core 的新逻辑键生效，不需要修改 Redis 适配器。

tenon-example 复验时更新正式 `TenonAdmin` / `TenonAdmin.Workflow` 引用并核对传递依赖版本，保留原库和原角色授权，按迁移说明启动，再以普通管理员完整执行设计器闭环。只需要同步其采用的前端模板的两处权限字符串，不复制控制器、替换权限提供器或修改业务代码。
