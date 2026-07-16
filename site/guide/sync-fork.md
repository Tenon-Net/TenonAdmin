# Syncing Your Fork with Upstream

This page is for one specific situation: you forked or cloned the whole `Tenon-Net/TenonAdmin` repository to use `web/` as the starting point for your own frontend, made your own changes on top of it, and now want to pull in TenonAdmin's upstream fixes and improvements without losing your work.

::: tip Check which consumption model you're actually in first
- **Backend-only consumer** (you run `dotnet add package TenonAdmin` or `dotnet new tenon-app` in your own separate repo) → you don't need any of this. Updates arrive by bumping the NuGet package version; see [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md) for what changed and any breaking changes before bumping.
- **You forked the repo to build on `web/`** (the common case — there's no npm-installable frontend package, so building on `web/` directly is the supported path) → this page is for you.
:::

## 1. Fork and clone

Fork [`Tenon-Net/TenonAdmin`](https://github.com/Tenon-Net/TenonAdmin) on GitHub, then clone **your fork**:

```bash
git clone https://github.com/<your-username>/TenonAdmin.git
cd TenonAdmin
```

Add the original repo as a second remote, conventionally named `upstream`:

```bash
git remote add upstream https://github.com/Tenon-Net/TenonAdmin.git
git remote -v
```

## 2. Pick a branch to track

The repo has two long-lived branches with different purposes:

- **`main`** — release-only. Every commit on `main` corresponds to a tagged, published release (`v0.1.0`, `v0.1.1`, ...). Stable, good default to track.
- **`dev`** — active development, the target branch for incoming PRs. Newer, but may contain work in progress between releases.

Unless you specifically want to track unreleased work, base your fork on `main`:

```bash
git checkout -b my-product main
```

Do your customization work on `my-product` (or further branches off it) — **don't build directly on a branch named `main`/`dev` that you also use to pull upstream into**, so a bad merge never has to be untangled from your own commit history.

## 3. Pull upstream changes

Periodically (e.g. before starting new work, or when you see a new tag in the [CHANGELOG](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md)):

```bash
git fetch upstream
git merge upstream/main        # or: git rebase upstream/main
```

Both work; merge is simpler to reason about once you've already published work built on this branch elsewhere, rebase keeps history linear if you haven't. Resolve any conflicts, then push to your fork as usual.

To pull a specific release instead of "whatever's newest on `main`":

```bash
git fetch upstream --tags
git merge v0.1.1
```

## 4. Keep conflicts small

Most merge friction comes from editing the same files upstream also touches. Two habits keep it manageable:

- **Put your own pages/components/API modules in new files**, not inside existing ones — e.g. a new `web/src/views/your-module/` directory rather than adding routes into an existing view. New files never conflict; only shared ones do.
- **If you must customize a shared file** (a layout, a store, `styles/tokens.css`), expect to resolve a conflict there each time upstream also changes it — that's normal, not a sign something's wrong.

## 5. Track what changed

- [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md) — Keep a Changelog format, one entry per release, covers both halves and calls out breaking changes explicitly (the project is pre-1.0, so the API can still shift).
- The login page footer shows `web/package.json`'s `version` — after merging, bump it to match the tag you merged so what your users see matches what's actually running.

## Where to next

- [Quick Start](/guide/getting-started) — if you haven't run the project yet.
- [Adding a New Business Module](/guide/new-business/) — building your own features on top of the kernel.
- [Contributing](/community/contributing) — the *other* fork workflow: sending changes back to TenonAdmin instead of pulling changes in.
