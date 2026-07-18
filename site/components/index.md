# Component Ecosystem

Some of the frontend's business-agnostic components now live outside it, as **standalone npm packages**. Neither depends on TenonAdmin. Any Vue 3 + Naive UI project can install them, and this admin console runs on those same packages rather than on a private copy.

| Package | Role | Docs |
|---|---|---|
| [`tenon-naive-pro-table`](/components/pro-table) | Column-driven Pro Table: one `columns` array drives the search form, dict rendering, and column settings; one `fetcher` adapts to any backend | [View →](/components/pro-table) |
| [`tenon-naive-iconify-picker`](/components/icon-picker) | Offline-first icon picker built on Iconify: multiple icon libraries, zero network requests, single-string value | [View →](/components/icon-picker) |

> Both packages are published to npm. The two pages here cover when to reach for each one and where the integration boundary sits; the authoritative per-API reference is in each repo's README.

For how to wire them into this admin template and keep theming and icons aligned, see [Theming & Icons](/frontend/appearance) and each package's own doc page above.
