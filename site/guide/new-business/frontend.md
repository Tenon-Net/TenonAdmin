# B. Frontend

### B1. Regenerate API types

Once the backend is running:

```bash
cd web && npm run gen:api     # regenerates src/api/schema.d.ts from /openapi/v1.json (don't hand-edit it)
```

The new endpoint now appears in the generated types.

### B2. Wrap it: `web/src/api/index.ts`

Add a group per domain:

```ts
export const productApi = {
  page: (params: { page: number; pageSize: number; name?: string }) =>
    client.GET('/api/v1/biz/product/page', {
      params: { query: { Current: params.page, Size: params.pageSize, Name: params.name } }, // PascalCase
    }).then((r) => unwrap<PagedList<Product>>(r)).then((p) => ({ items: p.items, total: p.total })),
  add: (body: ProductInput) => client.POST('/api/v1/biz/product', { body }).then((r) => unwrap<number>(r)),
  // update/remove follow the same style as menuApi
}
```

### B3. CRUD view: `web/src/views/biz/product/index.vue`

Template: `views/system/menu/index.vue` (with `NDataTable` + `NModal` form + `NPopconfirm`). Use `useTable` for the list logic:

```ts
const { loading, rows, pagination, search, onPage } = useTable(productApi.page, {
  initParams: { name: '' },
  onError: (e) => message.error(translateError(e)),
})
```

- Columns are rendered with `h()`, with the actions column holding the edit/delete buttons.
- All visible text goes through `t('...')` — see B6 for i18n keys.
- Dangerous buttons can carry `v-auth="'POST:/api/v1/biz/product'"` (currently fail-open — see the [Frontend Standards](/standard/frontend)).

### B4. Mount the menu (so the page becomes visible)

Go to the **menu management** page to create/confirm the menu node. The key part is that `Component` must match the file path:

| Field | Value | Notes |
|---|---|---|
| Type | Menu | Directories are parent nodes only; buttons only carry permission codes |
| Path | `/biz/product` | The route address |
| Component | `biz/product/index` | → `/src/views/biz/product/index.vue` (**no leading/trailing parts**) |
| Application | pick a module | Only meaningful on top-level directories |

### B5. How routing gets resolved (how it works — no manual route needed)

`composables/useAuthMenu.ts` uses `import.meta.glob('/src/views/**/*.vue')` to map a `Component` string to a `.vue` file, and registers it as a dynamic route (named `menu-${id}`, mounted under `layout`) automatically after login/refresh. **So you never touch `router/`** — just create the `.vue` file and configure the menu. If the console reports `[menu] missing view component`, it means the `Component` string doesn't match the file path.

### B6. i18n: `web/src/locales/zh-CN.ts` / `en-US.ts`

Add the page's text keys, plus translations for the new error codes from A6 (`translateError` looks these up by code/msgKey).

### B7. Before submitting

```bash
npm run lint && npm run typecheck
```

**Previous:** [A. Backend](/guide/new-business/backend)
**Next:** [C. End-to-End Checklist](/guide/new-business/checklist)
