# 前端规范(Vue 3 + Naive UI)

写页面、调接口前对着这份清单核一遍。栈是 `<script setup>` + Naive UI + Pinia(持久化)+ vue-router + vue-i18n + VueUse,路径别名 `@` → `src`;整体架构见 [核心概念](/zh/guide/concepts),组件用法见仓库 [`web/COMPONENTS.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/COMPONENTS.md)、设计系统见 [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md)。

## 目录落点

- 页面按模块/实体分:`views/<模块>/<实体>/index.vue`;完整 CRUD 范例照 `views/system/menu/index.vue`(`NDataTable` + `NModal` 表单 + `NPopconfirm`)。
- `composables/`(`use*`)放与 UI 库无关的逻辑单源,Naive 消息回调由视图注入,不写进 composable。
- `api/` 三件:`client.ts`(openapi-fetch)+ `index.ts`(按域分组)+ 生成的 `schema.d.ts`。其余目录职责见 [项目结构](/zh/frontend/structure)。

## API 契约

::: warning schema.d.ts 是生成产物,禁手改
`src/api/schema.d.ts` 由后端 OpenAPI 生成(`npm run gen:api`,**后端需正在运行**才能拉 `/openapi/v1.json`),手改下次一生成就被覆盖——要调类型只能改后端接口/DTO 再重新生成。生产不挂该端点,详见 [常见问题](/zh/faq)。
:::

- API 调用集中在 `api/index.ts` 按域分组(`authApi`/`userApi`/`moduleApi`/`menuApi` …),每个方法形如 `client.X(...).then(r => unwrap<T>(r))`,不在视图里裸调 `client`。
- `unwrap` 统一解信封,失败(`code≠0` 或非 2xx)都归一到 `ApiError`(带 `code`/`msgKey`);视图 `catch` 后用 `translateError(e)` 出文案。
- 分页在 api 层归一为 `{ items, total }` 以适配 `useTable`(后端是 `PagedList<T>{current,size,total,items}`)。
- 查询参数名用 PascalCase(ASP.NET 模型绑定要求)。
- 只有前后端真不同源(CDN / 独立域名)才构建期给 `VITE_API_BASE`,且后端要显式配 `TenonAdmin:Api:Cors:AllowedOrigins`(默认 deny-all)。鉴权 / 401 刷新中间件见 [HTTP 请求层](/zh/frontend/request),解信封细节见 [对接后端响应](/zh/frontend/api-contract)。

## 路由

- `router/routes.ts` 只放静态路由(login、error、shell/layout);真实菜单树登录后从后端拉取,注入为动态路由(只活内存,不落盘)。
- 菜单节点的 `component` 串(如 `system/user/index`)映射到 `/src/views/system/user/index.vue`;路由 `name = menu-${id}`,挂在 `layout` 下。
- 登出 / 切应用用 `registerDynamic` / `resetRouter` 精确增删动态路由,不整体重置整棵路由树。

::: danger 不要持久化 routesReady / menuTree
持久化会跳过刷新重建流程,刷新后直接导向 404——这两个状态必须只活在内存里。重建机制见 [路由与动态菜单](/zh/frontend/routing)。
:::

## 状态(Pinia)

- `defineStore` + `actions`;**按需持久化** `persist: { pick: [...] }`,不是整个 store 全量存(如 `auth` 只存 `currentModuleId`)。
- 现有 store:`auth`(模块/菜单/权限码/`routesReady`)、`user`(令牌/登录态)、`app`(主题/偏好)、`tabs`(标签页)。登出走 `reset()` 清授权态并清标签。

## 组合式函数

- `use*` 命名,返回响应式引用与方法,**与 Naive 无关**(错误 / 消息回调由视图注入,参照 `useTable` 的 `onError`)。
- 列表页统一用 `composables/useTable.ts`:传 `fetcher(({page,pageSize,...params})=>Promise<{items,total}>)`,得 `loading/rows/pagination/load/search/onPage/onPageSize`。

## 按钮级权限

```vue
<n-button v-auth="'POST:/api/v1/sys/user'">新增</n-button>
```

- 单权限码传字符串;数组默认 OR,`.and` 修饰符做 AND;不命中直接移除 DOM(不是仅隐藏)。
- 权限码取值就是后端的规范化路由(与 `[RolePermission]` 同源),不自造权限字符串。详见 [前端权限](/zh/frontend/permission)。

## 共享组件

- 后台**不设组件演示菜单**,组件用法统一沉在 [`web/COMPONENTS.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/COMPONENTS.md);写页面前先看一遍避免重复造轮子,加了新的通用组件也同步更新它。
- 已有 ProTable / FormContainer / `useConfirm` / StatusSwitch / 字典套件(DictSelect/DictRadio/DictTag/DictCheckbox)/ OrgTreeSelect / FileUpload(分片续传)/ ApiSelect(派生 UserSelect/RoleSelect)等,每个组件的详细 API 见其目录下 `README.md`。

## i18n

- 视图内所有可见文本走 `t('...')`,禁止硬编码中文 / 英文字面量。
- 错误文案按 code 翻译:后端只给 `code`/`msgKey`,前端 `translateError` + `locales/zh-CN.ts`/`en-US.ts` 出文案。机制见 [国际化与错误码](/zh/frontend/i18n)。

## 设计系统

- 业务代码只消费角色令牌层(如 `--color-text-primary`),不直接引原语层(如 `--color-gray-500`);tokens 单源是 `src/styles/tokens.css`。
- 组件样式用 `scoped` + CSS 变量(`var(--gap-card)` 等),不写死颜色 / 间距。
- 明暗切换靠 `<html data-theme="dark">`,不打即亮色;角色令牌 / 主色 / 语义色 / 阴影在其下整体翻转。完整规范见 [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md) 与 [主题与 Design Tokens](/zh/frontend/appearance)。

## 提交前

```bash
npm run lint        # oxlint(lint:fix 自动修)
npm run typecheck   # vue-tsc --noEmit
npm run build       # vue-tsc --noEmit && vite build
```

三者都通过才算完成,不要只跑其中一个就认为没问题。
