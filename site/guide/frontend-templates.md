# Choosing a Frontend Template

Both Vue and React templates connect to the same TenonAdmin backend. Choose the stack your team knows: `web/` uses Vue 3 and Naive UI, while `web-react/` uses React 19 and Ant Design. Each can be installed, developed, and deployed independently.

## One Backend, Two Frontends

Each template runs its own `npm run gen:api` to generate API types from `/openapi/v1.json`. They use the same backend permissions and error contract, while components, state management, and page implementations are maintained separately. Development ports `5175` and `5174` allow side-by-side comparison.

| | Vue template | React template |
|---|---|---|
| Directory | `web/` | `web-react/` |
| Stack | Vue 3 + Naive UI | React 19 + Ant Design |
| State management | Pinia | Zustand |
| Table wrapper | ProTable | DataTable |
| Dev port | `5175` | `5174` |

After choosing a template, follow the [Vue tutorial](/frontend/getting-started) or [React tutorial](/frontend-react/getting-started): create a static page, connect its menu, load API data, and grant permissions. Then consult the routing, request, localization, and theme references as needed.

After signing in, user management looks like this. Choose a feature on the left, filter and inspect records in the center, and add, import, or export above the table. The templates use different components while sharing backend permissions. These screenshots show the Chinese interface; click to enlarge.

[![Vue · Naive UI](/screenshots/vue-admin.png)](/screenshots/vue-admin.png)

[![React · Ant Design](/screenshots/react-admin.png)](/screenshots/react-admin.png)

## Which One

Base the choice on team experience and existing components. Vue teams can use the Vue template; teams using React and Ant Design can choose React. Check the pages and components you need in the selected release before adopting it.

If you have no preference, run the Vue sample first, then compare component usage with the live demo or local React template. Choosing a frontend does not change the backend permission model.

The templates do not import each other. Install dependencies, build, and deploy only the one you choose. Use that template’s own components and conventions when adding pages.

## degit One, Make It Yours

To run one straight from the repo first, head to [Quick Start](/guide/getting-started) — the start commands for both are there. To use one as the starting point for your own project, degit a snapshot with no `.git` history, whichever template you chose:

::: code-group

```bash [Vue (web/)]
npx degit Tenon-Net/TenonAdmin/web my-web
```

```bash [React (web-react/)]
npx degit Tenon-Net/TenonAdmin/web-react my-web
```

:::

A snapshot suits projects that maintain their frontend independently, but it does not retain upstream Git history, so later fixes need manual migration. Use [Syncing Your Fork](/guide/sync-fork) to merge updates through Git instead. In either case, check frontend and backend version compatibility.
