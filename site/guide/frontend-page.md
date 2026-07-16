# Add a Frontend Page

Continuing from the previous guide, [Add a Business Module End-to-End](/guide/business-module) — the backend already has the `GET/POST/PUT/DELETE /api/v1/sample/doc` set of endpoints. This guide turns them into a working admin page.

## 1. Regenerate the API types

The frontend doesn't hand-write API types — they're generated from the OpenAPI contract of a running backend. Start the backend (with the newly added `sample/doc` endpoints), then in `web/`:

```bash
npm run gen:api
```

Under the hood this runs `openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts` (see `web/package.json`) — regenerating `src/api/schema.d.ts` from `/openapi/v1.json`.

::: warning Don't hand-edit `schema.d.ts`
It's a generated artifact — whenever the backend's endpoints change, you must rerun `gen:api`, and any hand edits will be overwritten by the next generation.
:::

## 2. Add types + wrap the API

Add a domain type in `web/src/types/api.ts` (aligned with the backend DTO fields; the backend serializes as camelCase):

```ts
/** Sample org-isolated document (aligned with backend SampleDoc). */
export interface SampleDoc {
  id: number
  title: string
}
```

Add a group in `web/src/api/index.ts` for this domain, built on the typed client exported from `client.ts`, unwrapping the standard envelope with `unwrap<T>`:

```ts
import type { SampleDoc } from '@/types/api'

export const sampleDocApi = {
  list: () => client.GET('/api/v1/sample/doc', {}).then((r) => unwrap<SampleDoc[]>(r)),
  create: (title: string) =>
    client.POST('/api/v1/sample/doc', { body: { title } }).then((r) => unwrap<number>(r)),
  rename: (id: number, title: string) =>
    client.PUT('/api/v1/sample/doc/{id}', { params: { path: { id } }, body: { title } }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sample/doc/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}
```

`unwrap` already handles both failure shapes (a business envelope with a `code`, and a `ProblemDetails` without one) — the view layer just needs to `catch` and pass the error to `translateError`, without duplicating that logic here.

The `sample/doc` `List` endpoint isn't paged — it returns an array directly, so there's no need for the `toPage`/pagination-params trio here (that's for `PagedList<T>` endpoints — see `dictAdminApi.typePage` for that pattern).

## 3. Write the list page

Start by reading `web/COMPONENTS.md` — it's the index of the frontend's shared components, a must-read before writing any page. The `FormContainer` (modal form container) and `useConfirm` (confirmation) used on this page are documented there, with conventions and example pages to reference.

`sample/doc` is a flat, unpaged list, so it doesn't need `ProTable` — a plain `NDataTable` is enough (see the same pattern used for the dictionary-item panel on the right side of `web/src/views/system/dict/index.vue`). Create `web/src/views/sample/doc/index.vue`:

```vue
<script setup lang="ts">
import { h, reactive, ref, onMounted } from 'vue'
import { NButton, NCard, NSpace, NInput, NPopconfirm, NForm, NFormItem, NDataTable, useMessage, type DataTableColumns, type FormInst, type FormRules } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import FormContainer from '@/components/FormContainer/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import { sampleDocApi } from '@/api'
import type { SampleDoc } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()

const rows = ref<SampleDoc[]>([])
const loading = ref(false)
async function load() {
  loading.value = true
  try {
    rows.value = await sampleDocApi.list()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
onMounted(load)

const columns: DataTableColumns<SampleDoc> = [
  { title: () => t('sampleDoc.title'), key: 'title' },
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 130,
    render: (r) =>
      h(NSpace, { size: 4 }, () => [
        authStore.hasPerm('PUT:/api/v1/sample/doc/{id}')
          ? h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit'))
          : null,
        authStore.hasPerm('DELETE:/api/v1/sample/doc/{id}')
          ? h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => sampleDocApi.remove(r.id), t('common.deleted')).then((ok) => { if (ok) load() }),
              },
              {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
                default: () => t('sampleDoc.deleteConfirm', { title: r.title }),
              },
            )
          : null,
      ]),
  },
]

// ── Add/edit modal ──
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const form = reactive({ title: '' })
const rules: FormRules = {
  title: { required: true, whitespace: true, message: () => t('sampleDoc.titleRequired'), trigger: ['input', 'blur'] },
}
function openAdd() {
  editingId.value = null
  form.title = ''
  show.value = true
}
function openEdit(r: SampleDoc) {
  editingId.value = r.id
  form.title = r.title
  show.value = true
}
async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) await sampleDocApi.create(form.title)
    else await sampleDocApi.rename(editingId.value, form.title)
    message.success(t('common.success'))
    await load()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
</script>

<template>
  <n-card :bordered="true">
    <template #header-extra>
      <n-button v-auth="'POST:/api/v1/sample/doc'" type="primary" size="small" @click="openAdd">
        {{ t('common.add') }}
      </n-button>
    </template>
    <n-data-table :columns="columns" :data="rows" :loading="loading" :row-key="(r: SampleDoc) => r.id" />
  </n-card>

  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('sampleDoc.addTitle') : t('sampleDoc.editTitle')"
    :width="420"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="60">
      <n-form-item :label="t('sampleDoc.title')" path="title">
        <n-input v-model:value="form.title" :placeholder="t('sampleDoc.title')" />
      </n-form-item>
    </n-form>
  </FormContainer>
</template>
```

Key points (all from existing conventions in `web/COMPONENTS.md`, nothing invented here):

- **`FormContainer`** takes over loading/close via the `onConfirm` protocol — when `save()` returns `false` (or throws), the modal stays open, so validation errors can be shown.
- **`useConfirm().run`** pairs with `n-popconfirm`: the popconfirm acts as the trigger, and the confirmed action plus success/failure toasts are handled by `run`.
- **`v-auth`**: button-level permission, whose value is the route permission code itself (`POST:/api/v1/sample/doc`) — if it doesn't match, the DOM node is removed outright. The edit/delete buttons in the operation column additionally use `authStore.hasPerm(...)` to decide whether to render, both converging on the same permission code.
- **Error handling stays in the view layer**: `catch (e) { message.error(translateError(e)) }` — no UI popups in the API layer.

## 4. Attach it to the menu (making the page visible)

No need to hand-edit `router/`. Dynamic routes are registered automatically after login based on the menu tree — `composables/useAuthMenu.ts` uses `import.meta.glob('/src/views/**/*.vue')` to map the menu's `Component` string to a `.vue` file. All you need to do is create a node in **Menu Management**:

| Field | Value | Notes |
|---|---|---|
| Type | Menu | |
| Path | `/sample/doc` | Route address |
| Component | `sample/doc/index` | → `/src/views/sample/doc/index.vue` (without prefix/suffix) |
| App | Pick a module | Only top-level directories are valid |

After saving, log back in (or refresh to trigger route rebuilding), and the page will show up in the menu. If the console reports `[menu] missing view component`, it means the `Component` string doesn't match the file path.

## 5. Add i18n text

The page above uses a set of `sampleDoc.*` keys — add them to `web/src/locales/zh-CN.ts` / `en-US.ts` following the pattern of existing pages:

```ts
sampleDoc: {
  title: 'Title',
  titleRequired: 'Please enter a title',
  addTitle: 'Add Document',
  editTitle: 'Edit Document',
  deleteConfirm: 'Confirm deleting "{title}"?',
},
```

## 6. Before committing

```bash
npm run lint       # oxlint (lint:fix to autofix)
npm run typecheck  # vue-tsc --noEmit
```

## Next steps

Once the page is working, if you want to ship the whole system, see the next guide: [Full Container Deployment Walkthrough](/guide/deployment/docker).

> For the fuller frontend specification (naming conventions, component directory structure, `ProTable` patterns), see `web/COMPONENTS.md` and [Frontend Standard](/standard/frontend).


---

<!-- TODO(rewrite): merged from frontend.md -->

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



---

<!-- TODO(rewrite): merged from checklist.md -->

# C. End-to-End Checklist

**Backend**
- [ ] Entity (choose `BaseEntity`/`DataEntity`) + Sugar attributes + unique index
- [ ] `*Models.cs` DTOs (records)
- [ ] `I*Service` + `*Service` (virtual, transactional, duplicate check includes soft-deleted rows)
- [ ] `TryAddScoped` registration in `ServicesSetup`
- [ ] Controller (`[ApiController]`/`[Route]`/`[Module]`, `[RolePermission]` on every action)
- [ ] Add codes to `ErrorCode`
- [ ] Cache only hot reads (`CacheKeys` + cache-aside + invalidation)
- [ ] Seed data (optional, fixed Id)
- [ ] Tests (`WebApplicationFactory`, both SQLite/MySQL legs green)

**Frontend**
- [ ] `npm run gen:api` to regenerate types
- [ ] Add a group to `api/index.ts`
- [ ] `views/<module>/<entity>/index.vue` (`useTable` + Naive table/form)
- [ ] i18n text + error-code translations
- [ ] `lint` + `typecheck` pass

**Configure permissions (at runtime)**
- [ ] Create the node in menu management (Path/Component matched up)
- [ ] Check off the grant in role management

