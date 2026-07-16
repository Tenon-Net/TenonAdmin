# B. 前端

### B1. 重新生成 API 类型

后端跑起来后:

```bash
cd web && npm run gen:api     # 从 /openapi/v1.json 重生成 src/api/schema.d.ts(勿手改)
```

新端点即出现在类型里。

### B2. 封装　`web/src/api/index.ts`

按域加一组:

```ts
export const productApi = {
  page: (params: { page: number; pageSize: number; name?: string }) =>
    client.GET('/api/v1/biz/product/page', {
      params: { query: { Current: params.page, Size: params.pageSize, Name: params.name } }, // PascalCase
    }).then((r) => unwrap<PagedList<Product>>(r)).then((p) => ({ items: p.items, total: p.total })),
  add: (body: ProductInput) => client.POST('/api/v1/biz/product', { body }).then((r) => unwrap<number>(r)),
  // update/remove 同 menuApi 风格
}
```

### B3. CRUD 视图　`web/src/views/biz/product/index.vue`

蓝本:`views/system/menu/index.vue`(含 `NDataTable` + `NModal` 表单 + `NPopconfirm`)。列表逻辑用 `useTable`:

```ts
const { loading, rows, pagination, search, onPage } = useTable(productApi.page, {
  initParams: { name: '' },
  onError: (e) => message.error(translateError(e)),
})
```

- 列用 `h()` 渲染,操作列放编辑/删除按钮。
- 所有可见文本走 `t('...')`,i18n key 见 B6。
- 危险按钮可挂 `v-auth="'POST:/api/v1/biz/product'"`(当前 fail-open,见[前端规范](/zh/standard/frontend))。

### B4. 挂载菜单(页面才可见)

到**菜单管理**页建/确认菜单节点,关键是 `Component` 必须与文件路径对应:

| 字段 | 值 | 说明 |
|---|---|---|
| Type | 菜单 | 目录只作父节点,按钮只承载权限码 |
| Path | `/biz/product` | 即路由地址 |
| Component | `biz/product/index` | → `/src/views/biz/product/index.vue`(**不带前后缀**) |
| 所属应用 | 选模块 | 仅顶级目录有效 |

### B5. 路由如何解析(原理,无需手动加路由)

`composables/useAuthMenu.ts` 用 `import.meta.glob('/src/views/**/*.vue')` 把 `Component` 串映射到 `.vue` 文件,登录/刷新后自动注册为动态路由(名 `menu-${id}`,挂 `layout` 下)。**所以你不用动 `router/`**——建好 `.vue` + 配好菜单即可。若控制台报 `[menu] 缺少视图组件`,是 `Component` 串与文件路径没对上。

### B6. i18n　`web/src/locales/zh-CN.ts` / `en-US.ts`

加该页文案 key,以及 A6 里新错误码对应的翻译(`translateError` 按 code/msgKey 取)。

### B7. 提交前

```bash
npm run lint && npm run typecheck
```

**上一节:** [A. 后端](/zh/guide/new-business/backend)
**下一节:** [C. 端到端清单](/zh/guide/new-business/checklist)
