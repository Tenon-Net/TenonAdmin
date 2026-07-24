# Choosing a Frontend Template

One backend, two frontends: `web/` is Vue 3 with Naive UI, `web-react/` is React 19 with Ant Design. Both speak the same OpenAPI contract, and login, menus, permissions, and CRUD are aligned feature for feature. Which one you pick comes down to the stack your team already knows.

## One Backend, Two Frontends

Both templates generate their typed client from the backend's `/openapi/v1.json`, run the same `gen:api`, and share the same error codes, data permissions, and dynamic menus. The only visible difference is the component library — Naive UI on one side, Ant Design on the other. The dev ports are deliberately different too, so `5173` and `5174` can run at once and be compared side by side.

| | Vue template | React template |
|---|---|---|
| Directory | `web/` | `web-react/` |
| Stack | Vue 3 + Naive UI | React 19 + Ant Design |
| State management | Pinia | Zustand |
| Table wrapper | ProTable | DataTable |
| Dev port | `5173` | `5174` |

Implementation details for each — routing, portal guards, the request layer, permissions, i18n, theming — live in their own deep-dive sections: [Vue docs](/frontend/structure) and [React docs](/frontend-react/structure).

## Which One

Start with the team. If you write Vue day to day, take `web/`; if React is your habit, take `web-react/` — that alone settles it for most people. The two are feature-aligned, so there's no "pick this one and lose a capability" trade-off to agonize over.

No strong preference? Take the Vue one. It's the default template shipped with the kernel, it's what `dev.bat` and the Quick Start bring up first, and there are more people around to lend a hand when something breaks.

The two templates are self-contained and never reference each other, so you leave with only the one you picked, and the other doesn't tag along. That's a deliberate product decision: one template is one complete starting point you can run, change, and ship on its own. The cost is the maintainers': the same copy and the same design token get maintained twice, once on each side. But that's the repo's side of things — what lands in your hands is always one clean, complete template.

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

That snapshot is entirely yours to change however you like. The trade-off is no upgrade channel: upstream fixes are yours to read off the diff and reapply by hand. To keep pulling upstream fixes, don't snapshot — follow [Syncing Your Fork](/guide/sync-fork), whose seams exist to keep merge conflicts near zero.
