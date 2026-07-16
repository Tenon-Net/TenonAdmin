# Component Ecosystem

A few business-agnostic, general-purpose components have been **split out of the TenonAdmin frontend into standalone npm packages**: they don't depend on TenonAdmin, any Vue 3 + Naive UI project can install them on its own, and they're the very same set this admin console runs on.

| Package | Role | Docs |
|---|---|---|
| [`tenon-naive-pro-table`](/components/pro-table) | Column-driven Pro Table: one `columns` array drives the search form, dict rendering, and column settings; one `fetcher` adapts to any backend | [View →](/components/pro-table) |
| [`tenon-naive-iconify-picker`](/components/icon-picker) | Offline-first icon picker built on Iconify: multiple icon libraries, zero network requests, single-string value | [View →](/components/icon-picker) |

> Both packages are published to npm, and each repo's README is the authoritative documentation; the pages here are a curated overview.

For how to wire them into this admin template and keep theming and icons aligned, see [Theming & Icons](/frontend/appearance) and each package's own doc page above.
