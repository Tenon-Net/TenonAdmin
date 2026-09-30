# Add a Frontend Page

Once the document endpoints work, add a Vue page to list titles and create, edit, and delete records. First complete [Add a Business Module](/guide/business-module), confirm its endpoints appear in OpenAPI, and make sure `web/` starts successfully.

The paths and components below are for Vue and Naive UI. React projects should follow the [React structure and components](/frontend-react/structure), rather than copy `.vue` files.

## Regenerate types from the OpenAPI contract

The frontend doesn't hand-write API types — it generates them from the OpenAPI contract a running backend exposes. First get the backend running (with the freshly added `sample/doc` endpoints), then in `web/`:

```bash
npm run gen:api
```

The script invokes `openapi-typescript` through `scripts/gen-api.mjs` and generates `src/api/schema.d.ts` from `http://localhost:5100/openapi/v1.json` by default. Set `TENON_API_TARGET` for a different backend. Search the generated file for `/api/v1/sample/doc` to confirm the new endpoints are included; generation fails if the backend is unavailable.

::: warning Don't hand-edit `schema.d.ts`
It's a generated artifact — whenever the backend's endpoints change, rerun `gen:api`, and any hand edits are overwritten wholesale by the next generation.
:::

How the contract flows from backend to frontend, and how `client.ts` builds typed requests from it, are the principles covered in [Frontend API Contract](/frontend/api-contract); this chapter just uses it.

## Add a domain type, wrap a layer of API

::: tip Your code goes in new files
Keep domain types, API wrappers, and translations in your module’s own files to reduce overlap with upstream edits. Import shared entry points without appending business code to them. Separate files do not guarantee conflict-free merges; check API compatibility after upgrades as described in [Syncing with Upstream](/guide/sync-fork).
:::

Create `web/src/types/sample.ts` with the domain type (aligned with the backend DTO fields; the backend serializes as camelCase):

```ts
/** Sample org-isolated document (aligned with backend SampleDoc). */
export interface SampleDoc {
  id: number
  title: string
}
```

Create `web/src/api/sample.ts` — one group per domain, built on the typed client from `client.ts` and unwrapping the standard envelope with `unwrap<T>`. `api/index.ts` exports the shared primitives for exactly this — `unwrap`, `ApiError`, and for paged lists `pageParams` / `toPage` — so your module never has to reach into that file:

```ts
import { client } from './client'
import { unwrap } from './index'
import type { SampleDoc } from '@/types/sample'

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

`unwrap` already handles both failure shapes (a business envelope with a `code`, and a `ProblemDetails` without one), so the view layer just `catch`es and hands the error to `translateError` — no need to duplicate that check here. For the details of envelope unwrapping and the two error shapes, see [Request & Error Handling](/frontend/request).

The example’s `List` endpoint returns an array, so `unwrap` is sufficient. For pagination, return `PagedList<T>` from the backend and use `pageParams` and `toPage` to adapt parameters and results. See `userApi.page` when needed; this example does not require pagination.

## Write the list page

Start with `web/COMPONENTS.md` — the index of the frontend's shared components, a must-read before writing a page: the `FormContainer` (a combined modal/drawer form container) and `useConfirm` (confirmation + result toast) this page uses are both documented there, with conventions and pointers to example pages.

`sample/doc` is an unpaged flat list, so it doesn't need `ProTable` — a bare `NDataTable` is enough, following the dictionary-item panel on the right side of `web/src/views/system/dict/index.vue` (also a bare `n-data-table` plus create/edit/delete). Create `web/src/views/sample/doc/index.vue`:

```vue
<script setup lang="ts">
import { h, reactive, ref, onMounted } from 'vue'
import { NButton, NCard, NSpace, NInput, NPopconfirm, NForm, NFormItem, NDataTable, useMessage, type DataTableColumns, type FormInst, type FormRules } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import FormContainer from '@/components/FormContainer/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import { sampleDocApi } from '@/api/sample'
import type { SampleDoc } from '@/types/sample'

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
                  run(async () => {
                    if (!await sampleDocApi.remove(r.id)) throw new Error(t('sampleDoc.unavailable'))
                  }, t('sampleDoc.deleted')).then((ok) => { if (ok) load() }),
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
    else if (!await sampleDocApi.rename(editingId.value, form.title)) {
      message.error(t('sampleDoc.unavailable'))
      return false
    }
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

A few conventions, all from `web/COMPONENTS.md`, not invented here:

- **`FormContainer` takes over loading/close via the `onConfirm` protocol** — put `validate()` as the first line of `save()`, and a validation failure rejects and blocks the close; on an API failure, `return false` (or throw) keeps the modal open so the user can fix and retry. Business code doesn't manage `saving` or the footer bar itself.
- **`useConfirm().run` paired with `n-popconfirm`**: the popconfirm is the trigger, the post-confirm action and its success/failure toast go to `run`, and `run` returns an `ok` boolean — only reload with `load()` once something was actually deleted.
- **Button-level permissions, both converging on the same permission code**: buttons in the template use the `v-auth` directive (its value is the route permission code `POST:/api/v1/sample/doc`; a non-match hides the button and visibility updates when permissions change); the edit/delete buttons in the operation column go through an `h()` render function where the directive can't be used, so they switch to `authStore.hasPerm(...)` to decide whether to render. Both paths share one ruling: the super admin is let through everything, buttons hide when the permission code isn't present, and a regular user is matched exactly by code. **This is only UI decluttering — the server is always the authority**: the real interception is the backend's `[RolePermission]`, and an over-privileged request still gets a 403. For the rule details, see [Frontend Permission Model](/frontend/permission).
- **Error handling stays in the view layer**: `catch (e) { message.error(translateError(e)) }` — no UI popups in the API layer. `translateError` looks up text in the locale by the error's `msgKey` alone; it never reads the numeric `code`.

## Fill in i18n text

The page above uses a set of `sampleDoc.*` keys (the `common.*` keys are shared site-wide and already exist — no need to add them). i18n keys all have to end up in one object per locale, so there's a dedicated extension seam for it: drop a file under `web/src/locales/ext/<locale>/`, default-export the keys, and `locales/index.ts` globs it in automatically. **The filename is the top-level namespace** (`sampleDoc.ts` → `t('sampleDoc.*')`), and you register nothing:

```ts
// web/src/locales/ext/en-US/sampleDoc.ts
export default {
  title: 'Title',
  titleRequired: 'Please enter a title',
  addTitle: 'Add Document',
  editTitle: 'Edit Document',
  deleteConfirm: 'Confirm deleting "{title}"?',
  deleted: 'Deleted',
  unavailable: 'Record unavailable or access denied. Refresh the list and try again.',
}
```

```ts
// web/src/locales/ext/zh-CN/sampleDoc.ts
export default {
  title: '标题',
  titleRequired: '请输入标题',
  addTitle: '新增文档',
  editTitle: '编辑文档',
  deleteConfirm: '确认删除「{title}」?',
  deleted: '已删除',
  unavailable: '记录不存在或无权操作，请刷新列表后重试',
}
```

This example uses boolean results and adds no error code. When maintaining the kernel, a new `ErrorCode` can use `[MsgKey]` to select its translation key. An independent business project cannot extend the kernel enum directly; its own error-response handling must provide the agreed key. Add the corresponding nested translation in `ext/<locale>/error.ts`. The following illustrates translation mapping only; it does not add an error code to this example:

```ts
// backend: [MsgKey("error.doc.titleDuplicated")] → mirror it, nested, minus the `error.` prefix
// web/src/locales/ext/zh-CN/error.ts
export default { doc: { titleDuplicated: '文档标题重复' } }
```

The ext merge is a **deep** merge, so your keys join the built-in `error` namespace rather than replacing it — and overriding one built-in string (`{ auth: { passwordWrong: '...' } }`) leaves its siblings intact. If a code has no `[MsgKey]` annotation the backend emits `error.code.<number>`, and an entry missing from the locale falls back to the backend's own `message`, then to `error._fallback`. Write column titles in the function form `title: () => t('...')` so switching language takes effect instantly.

## Attach it to the menu so the page becomes visible

No need to hand-edit `router/`. Dynamic routes are registered automatically after login from the menu tree — `composables/useAuthMenu.ts` uses `import.meta.glob('/src/views/**/*.vue')` to map a menu node's `Component` string to a `.vue` file, registering it as a route named `menu-${id}` mounted under `layout`. So all you do is create the `.vue`, then create a node on the **Menu Management** page:

| Field | Value | Notes |
|---|---|---|
| Type | Menu | Directories are parent nodes only; buttons only carry permission codes |
| Path | `/sample/doc` | Route address |
| Component | `sample/doc/index` | → `/src/views/sample/doc/index.vue` (no prefix/suffix) |
| App | Pick an app | Only meaningful on top-level directories |

After saving, log back in (or refresh to trigger a route rebuild) and the page shows up in the menu. If the console reports `[menu] missing view component`, the `Component` string doesn't line up with the file path. `useAuthMenu` keeps the original menu path and renders `MissingRoute`, so the page names the missing component directly. For how the guard rebuilds these in-memory dynamic routes on a refresh or deep link, see [Dynamic Routing & Portal Guards](/frontend/routing).

The example’s update and delete endpoints return `false` when a record is unavailable or access is denied. That can still be a successful HTTP response, so check the result instead of relying on `catch`. Retry an operation on a deleted record to verify that the page does not display a success message.

## Before committing

```bash
npm run lint       # oxlint, lint:fix to autofix
npm run typecheck  # vue-tsc --noEmit
```

## End-to-end self-check

As a regular user, create a record, edit its title, and delete it, checking that the list updates each time. Revoke delete permission and confirm both that the button disappears and that a direct delete request is rejected. Switch language and check titles and validation messages.

**Frontend**
- [ ] `npm run gen:api` regenerates types (backend running)
- [ ] Domain type in a new `types/<module>.ts` + an API group in a new `api/<domain>.ts`
- [ ] `views/<module>/<entity>/index.vue` (table + `FormContainer` form + permission gating)
- [ ] Bilingual i18n under `locales/ext/zh-CN/` + `ext/en-US/` (add error-code translations too if there are new codes)
- [ ] `npm run lint` + `npm run typecheck` pass
- [ ] Review `git diff`: business types, APIs, and translations live in module files; shared-file edits have a clear reason. Changes to generated `schema.d.ts` are expected

**Configure permissions (runtime)**
- [ ] Create a node in Menu Management (`Path` / `Component` matching the file)
- [ ] Check the grant in Role Management — only then can a regular user see and click it

With the page working and the self-check passed, all that's left is shipping the whole system to a server — see the [Deployment Overview](/guide/deployment/) to pick a go-live route.
