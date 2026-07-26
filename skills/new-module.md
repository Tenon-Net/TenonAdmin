# 新增业务模块全流程 (New Module)

端到端串起一个完整模块:实体 → 后端 CRUD → 测试 → API 契约 → 前端页面 → i18n → 菜单/权限 → 验证。
每一步的细节在对应的专项 skill 里,本文只管**顺序、模式分叉和步骤之间的交接点**。

> 前端有两套官方模板(`web/` Vue 与 `web-react/` React),零共享、各自维护,**按消费者选的那套走,不用两边都做**。下文前端路径以 `web/` 为例;选 React 模板时,前端步骤看 `create-crud-frontend-react.md`,路径与命令换到 `web-react/`。

## 第零步:确定模式(决定后面每一步怎么走)

| | 系统模块(内核维护者) | 业务模块(消费者二开) |
|---|---|---|
| 代码位置 | `backend/src/TenonAdmin.*` 分层 | 你自己的 Assembly(`dotnet new tenon-app` 的 `Modules/`) |
| 表名 / 路由 | `sys_*` / `api/v1/sys/*` | `biz_*` / `api/v1/biz/*`(或自定前缀) |
| DI 注册 | `ServicesSetup.cs` 里 `TryAddScoped`(可替换性契约) | 自己 `Program.cs` 里普通 `AddScoped` |
| ErrorCode | `ErrorCode.cs` 42xxx 段追加(看枚举头部分段表取下一个号) | 自建枚举,从 60000 起步 |
| 菜单/权限 | `DefaultMenuSeed.cs` 追加种子,Id 按登记取号(勿回填空洞) | 后台「菜单管理」UI 添加;要预置则自注册 `ISeedData<T>`,Id ≥ `TenonSeedIds.ConsumerMin`(1000) |
| 程序集挂载 | 内置 | `options.ApplicationAssemblies.Add(typeof(Program).Assembly)`(缺这行:表不建、Controller 404) |
| 前端类型 / API | 追加进 `web/src/types/api.ts` / `api/index.ts` | **新建** `types/<模块>.ts` / `api/<域>.ts`(从 `api/index.ts` 导入 `unwrap`/`pageParams`/`toPage`) |
| 前端 i18n | 追加进 `locales/zh-CN.ts` + `en-US.ts` | **新建** `locales/ext/zh-CN/<模块>.ts` + `ext/en-US/<模块>.ts`(glob 自动并入,无需注册) |

> 前端那两行的理由和后端的可替换性同源:消费者 fork 整仓(见 `guide/sync-fork`),而 `locales/zh-CN.ts`(52 次改动)/`api/index.ts`(34)/`types/api.ts`(28) 是 `web/src` 里 churn 最高的文件。冲突只在双方都改同一文件时发生 —— 消费者的东西一律新建文件,就永远不冲突。

## 步骤

1. **建实体** → `create-entity.md`(BaseEntity 还是 DataEntity 的选型判据在那里;机构数据隔离选 DataEntity)。
2. **后端 CRUD 六件产出** → `create-crud-backend.md`(Models / Interface / Service / ErrorCode / DI / Controller,菜单种子取号规则也在那)。
3. **后端测试**:xUnit + `AdminAppFactory`(见 `backend/tests/TenonAdmin.Tests/` 现成写法);跑 `dotnet test backend/TenonAdmin.slnx`。
4. **刷新 API 契约**:先把后端跑起来(`dotnet run --project backend/samples/MinimalHost` 或你的 host),再 `cd web && npm run gen:api`(React 模板:`cd web-react`,脚本同名)。**绝不手改 `schema.d.ts`**。
5. **前端页面** → `create-crud-frontend.md`(Vue)或 `create-crud-frontend-react.md`(React),都是平铺 CRUD;树表/主从分栏/侧栏筛选(Vue)→ `create-page-variant.md`;组件契约总索引 → `web/COMPONENTS.md` / `web-react/COMPONENTS.md`,设计规范 → `web/DESIGN.md`。
6. **i18n**:双语两处都要加(模块 key + `error.*` key),文件按第零步的模式选——系统模块进 `locales/zh-CN.ts`/`en-US.ts`,业务模块新建 `locales/ext/<locale>/<模块>.ts`。键结构见 `create-crud-frontend.md` 的「i18n」节。**`error.*` 的键必须和后端 `[MsgKey]` 字符串逐字对上(嵌套形态,去掉 `error.` 前缀)**——`translateError` 只按 `msgKey` 取字、从不读数字 `code`,写成 `{ 50001: '...' }` 是永远没人读的死文案。
7. **菜单/权限接线**:
   - 系统模块:`DefaultMenuSeed` 加页面节点 + 权限按钮。
   - 消费者:菜单管理 UI 建节点,`component` 填 `views/` 相对路径(如 `biz/product/index`),动态路由自动注册,**不写任何路由代码**。
8. **可选加挂**:这个模块要定时跑点什么(对账、清理、推送)→ `create-job.md`;要 xlsx 导入导出 → `wire-import-export.md`。两者都不改动上面任何一步的产出,是纯加法。
9. **验证**(顺序跑,两个重进程不要并发):
   - `dotnet build backend/TenonAdmin.slnx -c Release` → `dotnet test backend/TenonAdmin.slnx`
   - `cd web && npm run typecheck && npm run lint`(React 模板:`cd web-react`,命令同名)
   - `npm run dev` 手工走查:列表/搜索/新增/编辑/删除/StatusSwitch 不回弹/无权限按钮被隐藏/错误提示走 i18n。

## 交接点清单(步骤之间最容易断的地方)

- **一个权限码,四处一致**:Controller 路由模板 = 菜单按钮 `Permission` = 前端 `v-auth`(Vue)/`<Can code>`(React) 值,格式统一 `METHOD:/api/v1/...`(路径参数保留 `{id}` 占位)。错一个字符 = 静默 403。
- **ErrorCode 的 `[MsgKey]` = 前端 `locales` 的 `error.*` 键**,zh/en 两个语言包都要有,漏了显示兜底文案。
- **`gen:api` 依赖后端在跑**;新端点没出现在 `/openapi/v1.json` 里就去查 Controller 是否注册(消费者:`ApplicationAssemblies` 挂了没)。
- **种子 Id 有保留区间**:内核 [1, 999]、消费者 [1000, 4095];越界/撞号启动即拒(`DatabaseInitializer` 强制)。
- 编码规范总纲(命名/注释/事务/缓存失效等)→ `docs/coding-standards.md`。
