# React Frontend Tutorial: Connect Your First Page

This tutorial adds one read-only Position Preview page to the existing React template and opens it from the sidebar. The backend stays unchanged; the page reuses the existing position paging endpoint. By the end, you will have connected a component, generated types, an API wrapper, a dynamic menu, and a permission.

## 0. Start the app and identify the workspace

Start the backend, then the React template:

```bash
dotnet run --project backend/samples/MinimalHost
```

```bash
npm --prefix web-react install
npm --prefix web-react run dev
```

The React template listens on `http://localhost:5174`. Sign in with the administrator credentials printed by the backend on first boot:

[![React admin workspace; the interface in this screenshot is Chinese](/screenshots/react-admin.png)](/screenshots/react-admin.png)

Open **Position Management** first. If the list is empty, create one test position and remember its name. Seeing that record later will prove that the new page reaches the backend.

## 1. Locate the four moving parts

| Location | Role | Change in this tutorial |
|---|---|---|
| `web-react/src/views/` | Page components | Add a page |
| `web-react/src/api/index.ts` | Backend calls grouped by domain | Reuse `positionApi.page` |
| `web-react/src/api/schema.d.ts` | Types generated from OpenAPI | Read only; never edit by hand |
| System Management → Menu Management | Turns a file path into an accessible route | Add a menu |

When the menu tree changes, `useRoutes` derives a new route array from it. Business pages do not maintain a second hand-written route table.

## 2. Start with a static page

Create `web-react/src/views/example/position-preview/index.tsx`:

```tsx
import { Card } from 'antd'

export default function PositionPreviewPage() {
  return (
    <Card title="Position Preview">React has loaded the page file.</Card>
  )
}
```

Do not connect an endpoint yet. After the route is connected, the browser should display “React has loaded the page file.”

## 3. Turn the file into a route with a menu

Open **Menu Management** and add a menu under the current application:

| Field | Value |
|---|---|
| Type | Menu |
| Name | Position Preview |
| Route path | `/example/position-preview` |
| Component path | `example/position-preview/index` |

The component path excludes both the `web-react/src/views/` prefix and the `.tsx` suffix. Save and refresh the browser. The new menu should display the static sentence from the previous step. This verifies the menu-to-route-to-file chain.

If the missing-component page appears, compare these values exactly:

```text
Menu component: example/position-preview/index
File:           src/views/example/position-preview/index.tsx
Browser route:  /example/position-preview
```

## 4. Add the real endpoint and its types

After the static page opens, replace `index.tsx` with the complete component below:

```tsx
import { useEffect, useState } from 'react'
import { Alert, Button, Card, List } from 'antd'
import { positionApi } from '@/api'
import type { SysPosition } from '@/types/api'
import { translateError } from '@/utils/error'

export default function PositionPreviewPage() {
  const [rows, setRows] = useState<SysPosition[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const load = async () => {
    setLoading(true)
    setError('')
    try {
      const page = await positionApi.page({ page: 1, pageSize: 20 })
      setRows(page.items)
    } catch (e) {
      setError(translateError(e))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  return (
    <Card title="Position Preview" extra={<Button loading={loading} onClick={() => void load()}>Refresh</Button>}>
      {error && <Alert type="error" title={error} showIcon />}
      <List
        loading={loading}
        dataSource={rows}
        renderItem={(row) => <List.Item>{row.name} · {row.code || '—'}</List.Item>}
      />
    </Card>
  )
}
```

Reload the page. The list should contain the position you created or noted in step 0. The view calls `positionApi.page()` instead of assembling a URL or token. It receives a normalized `{ items, total }`; the existing request layer handles authentication, refresh, CSRF, response envelopes, and error translation.

### Where the types come from

Open `web-react/src/api/index.ts` and search for `positionApi`. Its paging function ultimately calls:

```ts
client.GET('/api/v1/sys/position/page', {
  params: { query: { Current, Size, Name, SortField, SortOrder } },
})
```

The original types for the path, query, and response DTO come from `src/api/schema.d.ts`. After a backend contract change, run this from the repository root:

```bash
npm --prefix web-react run gen:api
```

The backend must be running on `http://localhost:5100`. Run type checking afterward. Do not edit `schema.d.ts`; generation overwrites it.

## 5. Grant access to a regular role

The super administrator passes frontend permission checks automatically. A regular role needs both:

1. the new Position Preview menu;
2. the existing position query permission, `GET:/api/v1/sys/position/page`.

[![Role permission editor; the interface in this screenshot is Chinese](/screenshots/role-permissions.png)](/screenshots/role-permissions.png)

In the permission tree shown above, select the new menu and then the endpoint permission for the position query. Selecting only the menu may expose an entry while the backend still rejects its data request; complete access needs both checks.

Sign out and back in as that role. Seeing the menu and loading the list proves that both menu access and endpoint access are present. Remove the query permission and sign in again; the endpoint must return 403. Frontend gates shape the interaction while the server remains the security boundary.

## 6. Verify the result

- Open the page from the sidebar and see position names.
- Copy the current URL and reload; the same page should return.
- Confirm that the network request targets `/api/v1/sys/position/page`.
- Confirm that a regular role succeeds with the query permission and receives 403 without it.
- Run the checks below.

Run these from the repository root:

```bash
npm --prefix web-react run lint
npm --prefix web-react run typecheck
```

## Continue by question

| Question | Next page |
|---|---|
| Why do bootstrap and theme setup depend on a fixed order? | [Project Structure & Startup](/frontend-react/structure) |
| How does a menu locate the new `.tsx` file? | [Routing & Dynamic Menus](/frontend-react/routing) |
| How does the request receive a token and recover from 401? | [The HTTP Request Layer](/frontend-react/request) |
| How does a response become data or a displayable error? | [Response Contract & Error Codes](/frontend-react/api-contract) |
| How should `<Can>` gate operation controls? | [Frontend Permissions](/frontend-react/permission) |
| How should hard-coded copy move into dictionaries? | [Internationalization](/frontend-react/i18n) |
| How should custom styles follow the active theme? | [Theme & Icons](/frontend-react/appearance) |
| Why does a dynamic deep link survive reload? | [Multi-App Portal & Router Guards](/frontend-react/portal-guards) |
