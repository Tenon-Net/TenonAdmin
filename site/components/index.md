# Component Ecosystem

The list-page and icon-picker components are published as standalone npm packages. A business project can use the versions already connected in the TenonAdmin template or install them in any Vue 3 + Naive UI application; both paths run the same components.

## Choose by task

| Package | Role | Docs |
|---|---|---|
| [`tenon-naive-pro-table`](/components/pro-table) | Column-driven Pro Table: one `columns` array drives the search form, dict rendering, and column settings; one `fetcher` adapts to any backend | [View →](/components/pro-table) |
| [`tenon-naive-iconify-picker`](/components/icon-picker) | Offline-first icon picker built on Iconify: multiple icon libraries, registered sets render without touching the network, single-string value | [View →](/components/icon-picker) |

Start with ProTable when a page needs search, pagination, column settings, or a tree table. Start with IconPicker when menus, buttons, and local SVGs need to share one icon value. Both packages are published to npm. Their repository READMEs remain authoritative for individual props and defaults; these site pages explain how each package fits into the TenonAdmin template and where its boundary lies.

For how to wire them into this admin template and keep theming and icons aligned, see [Theming & Icons](/frontend/appearance) and each package's own doc page above.
