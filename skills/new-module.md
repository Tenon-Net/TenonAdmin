# 新增业务模块全流程 (New Module)

端到端串起一个完整模块:实体 → 后端 CRUD → 测试 → API 契约 → 前端页面 → i18n → 菜单/权限 → 验证。
每一步的细节在对应的专项 skill 里,本文只管**顺序、模式分叉和步骤之间的交接点**。

从任务和现有代码确定模式、前端和验收范围；只在缺失选择会实质改变结果时询问。已有实体/API/测试可直接复用；仅后端任务跳过前端步骤。先读当前步骤的专项文档，再进入依赖它的下一步。

> 前端有两套官方模板(`web/` Vue 与 `web-react/` React),零共享、各自维护,**按消费者选的那套走,不用两边都做**。下文前端路径以 `web/` 为例;选 React 模板时,前端步骤看 `create-crud-frontend-react.md`,路径与命令换到 `web-react/`。

## 第零步:确定模式(决定后面每一步怎么走)

| | 系统模块(内核维护者) | 业务模块(消费者二开) |
|---|---|---|
| 代码位置 | `backend/src/TenonAdmin.*` 分层 | 你自己的 Assembly(`dotnet new tenon-app` 的 `Modules/`) |
| 表名 / 路由 | `sys_*` / `api/v1/sys/*` | `biz_*` / `api/v1/biz/*`(或自定前缀) |
| DI 注册 | `ServicesSetup.cs` 里 `TryAddScoped`(可替换性契约) | 自己 `Program.cs` 里普通 `AddScoped` |
| ErrorCode | `ErrorCode.cs` 42xxx 段追加(看枚举头部分段表取下一个号) | 自选数字从 60000 起步，集中成常量类；抛错时转成内核 `ErrorCode` |
| 菜单/权限 | `DefaultMenuSeed.cs` 追加种子,Id 按登记取号(勿回填空洞) | 后台「菜单管理」UI 添加;要预置则自注册 `ISeedData<T>`,Id ≥ `TenonSeedIds.ConsumerMin`(1000) |
| 程序集挂载 | 内置 | `options.ApplicationAssemblies.Add(typeof(Program).Assembly)`(缺这行:表不建、Controller 404) |
| 前端类型 / API | 追加进 `web/src/types/api.ts` / `api/index.ts` | **新建** `types/<模块>.ts` / `api/<域>.ts`(从 `api/index.ts` 导入 `unwrap`/`pageParams`/`toPage`) |
| 前端 i18n | 追加进 `locales/zh-CN.ts` + `en-US.ts` | **新建** `locales/ext/zh-CN/<模块>.ts` + `ext/en-US/<模块>.ts`(glob 自动并入,无需注册) |

> 消费者功能放在自己的文件中，减少跟随上游升级时的文本冲突；仍需检查 API 和组件契约变化。

## 步骤

1. **建实体** → `create-entity.md`(BaseEntity 还是 DataEntity 的选型判据在那里;机构数据隔离选 DataEntity)。
2. **后端 CRUD 六件产出** → `create-crud-backend.md`(Models / Interface / Service / ErrorCode / DI / Controller,菜单种子取号规则也在那)。
3. **后端测试**:内核沿用 xUnit + `AdminAppFactory`(见 `backend/tests/TenonAdmin.Tests/`)，先跑新模块的定向测试；消费者使用自己的 host/test project。
4. **刷新 API 契约**:先把后端跑起来(`dotnet run --project backend/samples/MinimalHost` 或你的 host),再 `cd web && npm run gen:api`(React 模板:`cd web-react`,脚本同名)。**绝不手改 `schema.d.ts`**。
5. **前端页面** → `create-crud-frontend.md`(Vue)或 `create-crud-frontend-react.md`(React),都是平铺 CRUD;树表/主从分栏/侧栏筛选(Vue)→ `create-page-variant.md`;组件契约总索引 → `web/COMPONENTS.md` / `web-react/COMPONENTS.md`,设计规范 → `web/DESIGN.md`。
6. **i18n**:双语都添加模块 key 和错误 key，文件按第零步模式选择。系统错误 key 对齐后端 `[MsgKey]`；消费者自选码使用服务端生成的 `error.code.<数字>`，例如 `error: { code: { '60001': '产品不存在' } }`。`translateError` 优先使用服务端 `msgKey`，本地已知数字码映射仅作兜底；不要写顶层 `{ 60001: '...' }`。
7. **菜单/权限接线**:
   - 系统模块:`DefaultMenuSeed` 加页面节点 + 权限按钮。
   - 消费者:菜单管理 UI 建节点,`component` 填 `views/` 相对路径(如 `biz/product/index`),动态路由自动注册,**不写任何路由代码**。
8. **可选加挂**:这个模块要定时跑点什么(对账、清理、推送)→ `create-job.md`;要 xlsx 导入导出 → `wire-import-export.md`。两者都不改动上面任何一步的产出,是纯加法。
9. **验证**(依赖步骤顺序运行；根据资源情况并行独立检查):
   - 内核跑 `dotnet build backend/TenonAdmin.slnx -c Release` 和相关测试；消费者构建/测试自己的项目。步骤 3 已通过且相关代码未变时复用证据，跨模块改动或 CI 要求再扩大范围。
   - `cd web && npm run typecheck && npm run lint`(React 模板:`cd web-react`,命令同名)
   - 启动所选前端，使用可用浏览器工具走查:列表/搜索/新增/编辑/删除/StatusSwitch 不回弹/无权限按钮被隐藏或禁用/错误提示走 i18n。缺少浏览器能力时报告验证缺口。

## 交接点清单(步骤之间最容易断的地方)

- **一个权限码,四处一致**:Controller 路由模板 = 菜单按钮 `Permission` = 前端 `v-auth`(Vue)/`<Can code>`(React) 值,格式统一 `METHOD:/api/v1/...`(路径参数保留 `{id}` 占位)。错一个字符 = 静默 403。
- **服务端 `msgKey` = 前端错误 key**：系统码来自 `[MsgKey]`，消费者自选码为 `error.code.<数字>`；zh/en 两个语言包都要有。
- **`gen:api` 依赖后端在跑**;新端点没出现在 `/openapi/v1.json` 里就去查 Controller 是否注册(消费者:`ApplicationAssemblies` 挂了没)。
- **种子 Id 有保留区间**:内核 [1, 999]；消费者 ≥ `TenonSeedIds.ConsumerMin`(1000) 且小于启动时 `SnowflakeIdGenerator.CurrentFloor()`。`ConsumerMax` 仅为历史常量，越界/撞号由 `DatabaseInitializer` 拒绝。
- 编码规范总纲(命名/注释/事务/缓存失效等)→ `docs/coding-standards.md`。
