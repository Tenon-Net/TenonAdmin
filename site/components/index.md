# Component Ecosystem

A few business-agnostic, general-purpose components have been **split out of the TenonAdmin frontend into standalone npm packages**: they don't depend on TenonAdmin, and any Vue 3 + Naive UI project can install them individually — this admin console uses the same packages.

| Package | Purpose | Docs |
|---|---|---|
| [`tenon-naive-pro-table`](/components/pro-table) | Column-driven Pro Table: a single `columns` array drives the search form, dict rendering, and column settings; a single `fetcher` adapts to any backend | [View →](/components/pro-table) |
| [`tenon-naive-iconify-picker`](/components/icon-picker) | Offline-first icon picker built on Iconify: multiple icon libraries, zero network requests, single-string value | [View →](/components/icon-picker) |

> Both packages are published to npm and each repo's README is the authoritative documentation; this site's pages are a curated overview.
