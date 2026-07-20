# Component Ecosystem

Some of the frontend's business-agnostic components now live outside it, as **standalone npm packages**. Neither depends on TenonAdmin. Any Vue 3 + Naive UI project can install them, and this admin console runs on those same packages rather than on a private copy.

| Package | Role | Docs |
|---|---|---|
| [`tenon-naive-pro-table`](/components/pro-table) | Column-driven Pro Table: one `columns` array drives the search form, dict rendering, and column settings; one `fetcher` adapts to any backend | [View →](/components/pro-table) |
| [`tenon-naive-iconify-picker`](/components/icon-picker) | Offline-first icon picker built on Iconify: multiple icon libraries, registered sets render without touching the network, single-string value | [View →](/components/icon-picker) |

> Both packages are published to npm. Looking up what a prop is called or what it defaults to? That lives with the package, in its own README. These two pages answer two questions only: whether to use it, and how to wire it into tenon.

For how to wire them into this admin template and keep theming and icons aligned, see [Theming & Icons](/frontend/appearance) and each package's own doc page above.
