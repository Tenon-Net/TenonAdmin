# Vue Frontend Tutorial: Connect Your First Page

This tutorial adds one read-only Position Preview page to the existing Vue template and opens it from the sidebar. The backend stays unchanged; the page reuses the existing position paging endpoint. By the end, you will have connected a view file, generated types, an API wrapper, a dynamic menu, and a permission.

## 0. Start the app and identify the workspace

Start the backend, then the Vue template:

```bash
dotnet run --project backend/samples/MinimalHost
```

```bash
npm --prefix web install
npm --prefix web run dev
```

Vite listens on `http://localhost:5175` by default; use the URL printed in the terminal if it differs. Sign in with the administrator credentials printed by the backend on first boot. The main workspace has a sidebar, an application bar, and the active page:

[![Vue admin workspace; the interface in this screenshot is Chinese](/screenshots/vue-admin.png)](/screenshots/vue-admin.png)

Open **Position Management** first. If the list is empty, create one test position and remember its name. Seeing that record later will prove that the new page reaches the backend instead of rendering static copy.

## 1. Locate the four moving parts

| Location | Role | Change in this tutorial |
|---|---|---|
| `web/src/views/` | Page components | Add a page |
| `web/src/api/index.ts` | Backend calls grouped by domain | Reuse `positionApi.page` |
| `web/src/api/schema.d.ts` | Types generated from OpenAPI | Read only; never edit by hand |
| System Management → Menu Management | Turns a file path into an accessible route | Add a menu |

TenonAdmin does not require a hand-written route for every business page. After login, the component path stored on a menu record maps to a file under `views/`.

## 2. Start with a static page

Create `web/src/views/example/position-preview/index.vue`:

```vue
<script setup lang="ts">
import { NCard } from 'naive-ui'
</script>

<template>
  <n-card title="Position Preview">
    Vue has loaded the page file.
  </n-card>
</template>
```

Do not connect an endpoint yet. This step has one observable result: after the route is connected, the browser displays “Vue has loaded the page file.”

## 3. Turn the file into a route with a menu

Open **Menu Management** and add a menu under the current application:

| Field | Value |
|---|---|
| Type | Menu |
| Name | Position Preview |
| Route path | `/example/position-preview` |
| Component path | `example/position-preview/index` |

The component path excludes both the `web/src/views/` prefix and the `.vue` suffix. Save and refresh the browser so the menu tree and dynamic routes reload. The new menu should display the static sentence from the previous step. This verifies the menu-to-route-to-file chain even if the position endpoint is unavailable.

If the missing-component page appears, compare these three values exactly:

```text
Menu component: example/position-preview/index
File:           src/views/example/position-preview/index.vue
Browser route:  /example/position-preview
```

## 4. Add the real endpoint and its types

After the static page opens, replace `index.vue` with the complete component below:

```vue
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { NAlert, NButton, NCard, NList, NListItem, NSpin } from 'naive-ui'
import { positionApi } from '@/api'
import type { SysPosition } from '@/types/api'
import { translateError } from '@/utils/error'

const rows = ref<SysPosition[]>([])
const loading = ref(false)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const page = await positionApi.page({ page: 1, pageSize: 20 })
    rows.value = page.items
  } catch (e) {
    error.value = translateError(e)
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <n-card title="Position Preview">
    <template #header-extra>
      <n-button :loading="loading" @click="load">Refresh</n-button>
    </template>
    <n-alert v-if="error" type="error">{{ error }}</n-alert>
    <n-spin v-else :show="loading">
      <n-list bordered>
        <n-list-item v-for="row in rows" :key="row.id">
          {{ row.name }} · {{ row.code || '—' }}
        </n-list-item>
      </n-list>
    </n-spin>
  </n-card>
</template>
```

Reload the page. The list should contain the position you created or noted in step 0. The view never calls `fetch` directly. It receives a normalized `{ items, total }` from `positionApi.page()`, while existing layers handle the token, refresh, CSRF, response envelope, and error translation.

### Where the types come from

Open `web/src/api/index.ts` and search for `positionApi`. Its paging function ultimately calls:

```ts
client.GET('/api/v1/sys/position/page', {
  params: { query: { Current, Size, Name, SortField, SortOrder } },
})
```

The original types for the path, query, and response DTO come from `src/api/schema.d.ts`. After a backend contract change, run this from the repository root:

```bash
npm --prefix web run gen:api
```

The backend must be running on `http://localhost:5100`. Run type checking afterward to find callers that no longer match. Do not edit `schema.d.ts`; generation overwrites it.

## 5. Grant access to a regular role

The super administrator passes frontend permission checks automatically. A regular role needs both:

1. the new Position Preview menu;
2. the existing position query permission, `GET:/api/v1/sys/position/page`.

[![Role permission editor; the interface in this screenshot is Chinese](/screenshots/role-permissions.png)](/screenshots/role-permissions.png)

In the permission tree shown above, select the new menu and then the endpoint permission for the position query. Selecting only the menu may expose an entry while the backend still rejects its data request; complete access needs both checks.

Sign out and back in as that role. Seeing the menu and loading the list proves that both menu access and endpoint access are present. Remove the query permission and sign in again: menu visibility depends on the menu grant, but the endpoint must return 403. Hiding controls in the frontend improves the interaction; the server remains the security boundary.

## 6. Verify the result

- Open the page from the sidebar and see position names.
- Copy the current URL and reload; the same page should return.
- Confirm that the network request targets `/api/v1/sys/position/page`.
- Confirm that a regular role succeeds with the query permission and receives 403 without it.
- Run the checks below.

Run these from the repository root:

```bash
npm --prefix web run lint
npm --prefix web run typecheck
```

## Continue by question

| Question | Next page |
|---|---|
| Why must the app install its foundations in a fixed order? | [Project Structure & Startup](/frontend/structure) |
| How does a menu locate the new `.vue` file? | [Routing & Dynamic Menus](/frontend/routing) |
| How does the request receive a token and recover from 401? | [The HTTP Request Layer](/frontend/request) |
| How does a response become data or a displayable error? | [Response Contract & Error Codes](/frontend/api-contract) |
| How should operation controls be gated? | [Frontend Permissions](/frontend/permission) |
| How should hard-coded copy move into dictionaries? | [Internationalization](/frontend/i18n) |
| How should custom styles follow the active theme? | [Theme & Icons](/frontend/appearance) |
| Why does a dynamic deep link survive reload? | [Multi-App Portal & Router Guards](/frontend/portal-guards) |
