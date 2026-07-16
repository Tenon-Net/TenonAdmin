# 前端规范(Vue 3 + Naive UI)

> 本页是可执行清单,规则均从现有代码提炼。前端整体架构见 [核心概念](/zh/guide/concepts),组件用法见仓库 [`web/COMPONENTS.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/COMPONENTS.md),设计系统见 [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md)。

## 技术栈与目录

`<script setup>` + Naive UI + Pinia(持久化)+ vue-router + vue-i18n + VueUse。路径别名 `@` → `src`。

| 目录 | 职责 |
|---|---|
| `views/` | 页面(按模块/实体分子目录,`views/<模块>/<实体>/index.vue`) |
| `composables/` | 与 UI 库无关的逻辑单源(`use*`),Naive 消息留在视图层 |
| `stores/` | Pinia 状态 |
| `layouts/` | 布局壳(顶栏/侧栏/标签/设置) |
| `components/` | 可复用组件 |
| `api/` | `client.ts`(openapi-fetch)+ `index.ts`(按域分组)+ 生成的 `schema.d.ts` |
| `router/` | 静态路由 + 动态路由重建 |
| `theme/`、`styles/` | 主题令牌 |
| `locales/` | i18n |
| `directives/` | `v-auth` 等 |
| `types/` | 手写类型(`menu.ts`)与再导出 |

## API 契约流

::: warning schema.d.ts 是生成产物
`src/api/schema.d.ts` 由后端 OpenAPI 生成(`npm run gen:api`,**后端需正在运行**才能拉到 `/openapi/v1.json`),**禁止手改**——改了下次一生成就会被覆盖。要调整类型只能改后端接口/DTO,再重新生成。生产环境该端点不挂载,详见 [常见问题](/zh/faq)。
:::

`src/api/client.ts` 是 `openapi-fetch` 针对 schema 的类型化封装,承担三件事:

```ts
// 默认空 baseUrl = 同源:schema 的 path 键已含 /api/v1
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
export const client = createClient<paths>({ baseUrl })
```

- **鉴权中间件**:请求前从 `useUserStore()` 读最新 token 注入 `Authorization: Bearer`。
- **401 刷新中间件**:并发 401 合流到同一次刷新(`refreshOnce`),刷新成功后重放原请求(写请求提前 `clone()` 保留一份可重放的 body),刷新失败清会话并跳转登录。
- 只有前端与 API 真的不同源(CDN / 独立域名)才需要构建期给 `VITE_API_BASE`,此时后端还必须显式配置 `TenonAdmin:Api:Cors:AllowedOrigins`(默认 deny-all)。

`api/index.ts` 按域分组导出(`authApi`/`userApi`/`moduleApi`/`menuApi` …),每个方法形如 `client.X(...).then(r => unwrap<T>(r))`:

- **`unwrap`** 统一解信封:2xx 的 `Result<T>`(`code≠0` 抛 `ApiError`)、非 2xx 的信封/ProblemDetails 都归一到 `ApiError`(带 `code`/`msgKey`)。视图层 `catch` 后用 `translateError(e)` 展示文案。
- 分页返回在 api 层归一为 `{ items, total }` 以适配 `useTable`(后端是 `PagedList<T>{current,size,total,items}`)。
- 查询参数名用 PascalCase(ASP.NET 模型绑定要求)。

## 路由(静态 + 动态菜单注入)

- `router/routes.ts` 只放静态路由(login、error、shell/layout)。真实菜单树在登录后从后端拉取,注入为**动态路由**(只活在内存,不落盘)。
- 组件解析(`composables/useAuthMenu.ts`):`import.meta.glob('/src/views/**/*.vue')` 收集全部页面;菜单节点的 `component` 串(如 `system/user/index`)映射到 `/src/views/system/user/index.vue`。路由 `path` 取菜单 `path`,`name = menu-${id}`,挂在 `layout` 下。
- **F5 / 深链**:动态路由会因刷新丢失。路由守卫在 `routesReady=false` 时调用 `useModule().enterInitial()` 重建后再重新解析当前 URL。

::: danger 不要持久化 routesReady / menuTree
持久化会跳过重建流程,刷新后直接导向 404。这两个状态必须保持只活在内存里。
:::

- 登出 / 切应用用 `registerDynamic` / `resetRouter` 精确增删动态路由,不整体重置整棵路由树。

## 状态(Pinia)

`defineStore` + `actions`;**按需持久化** `persist: { pick: [...] }`,不是整个 store 全量持久化(比如 `auth` 只存 `currentModuleId`)。现有 store:

| store | 职责 |
|---|---|
| `auth` | 模块/菜单/权限码/`routesReady` |
| `user` | 令牌/登录态 |
| `app` | 主题/偏好 |
| `tabs` | 标签页 |

登出走 `reset()` 清授权态并清标签。

## 组合式函数

- `use*` 命名,返回响应式引用与方法;**与 Naive 无关**(错误/消息回调由视图注入,参照 `useTable` 的 `onError`)。
- 列表页统一用 `composables/useTable.ts`:传 `fetcher(({page,pageSize,...params})=>Promise<{items,total}>)`,得到 `loading/rows/pagination/load/search/onPage/onPageSize`。

## 按钮级权限(`v-auth`)

```vue
<n-button v-auth="'POST:/api/v1/sys/user'">新增</n-button>
```

- 单权限码传字符串;数组默认 OR;`.and` 修饰符做 AND。不命中直接移除 DOM(不是仅隐藏)。
- 权限码的取值就是后端的规范化路由(与 `[RolePermission]` 同源),不是自造的权限字符串。

## 共享组件

::: tip 写页面前先看这份索引
后台**不设组件演示菜单**,组件用法统一沉淀在 [`web/COMPONENTS.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/COMPONENTS.md)。新增页面前先看一遍,避免重复造轮子。加了新的通用组件也要同步更新它。
:::

已有的组件覆盖列表页(ProTable)、表单容器(FormContainer)、二次确认(`useConfirm`)、行内启停(StatusSwitch)、字典套件(DictSelect/DictRadio/DictTag/DictCheckbox)、机构树选择(OrgTreeSelect)、文件上传(FileUpload,支持分片续传)、远程分页下拉基座(ApiSelect,派生出 UserSelect/RoleSelect)等。每个组件的详细 API 见其目录下的 `README.md`。

## i18n

- 文案按 code 翻译:后端只给 `code`/`msgKey`,前端 `translateError` + `locales/zh-CN.ts`/`en-US.ts` 出文案。
- 视图内所有可见文本走 `t('...')`,禁止硬编码中文/英文字面量。

## 设计系统

- Tokens 单源是 [`src/styles/tokens.css`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/src/styles/tokens.css),颜色/字号/间距/圆角/阴影都从这里的 CSS 变量取,业务代码**只消费角色令牌层**(如 `--color-text-primary`),不直接引原语层(如 `--color-gray-500`)。
- 明暗切换靠 `<html data-theme="dark">`,不打即亮色;角色令牌/主色/语义色/阴影在 `[data-theme="dark"]` 下整体翻转。
- 组件样式用 `scoped` + CSS 变量(`var(--gap-card)` 等),不写死颜色/间距。
- 完整规范(设计基调、布局尺寸、组件层级、token → Naive `GlobalThemeOverrides` 映射)见 [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md)。

## 组件 / 视图

- `<script setup lang="ts">`;表格列用 `h()` 渲染函数。`views/system/menu/index.vue` 是完整 CRUD 范例(`NDataTable` + `NModal` 表单 + `NPopconfirm`)。

## 提交前检查

```bash
npm run lint        # oxlint(lint:fix 自动修)
npm run typecheck   # vue-tsc --noEmit
npm run build        # vue-tsc --noEmit && vite build
```

三者都通过才算完成,不要只跑其中一个就认为没问题。

---

> 更完整的说明见 [`docs/coding-standards.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/coding-standards.md) 第 2 节。
