# Syncing Your Fork with Upstream

The frontend ships no npm package, so forking the whole repository and making `web/` your own starting point is the supported path. That trades a version bump for a git merge: upstream fixes land in the same files that hold months of your own changes.

The procedure below is written for that collision, and it is built to keep both sides.

::: tip Check which consumption model you're actually in first
- **Backend-only consumer** (you run `dotnet add package TenonAdmin` or `dotnet new tenon-app` in your own separate repo) → you don't need any of this. Updates arrive by bumping the NuGet package version; see [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md) for what changed and any breaking changes before bumping.
- **You forked the repo to build on `web/`** (the common case) → the procedure below was written for you.
- **One-off snapshot consumer** (`npx degit Tenon-Net/TenonAdmin/web` for a copy you own and maintain yourself, the soybean / vite scaffold model) → you've opted out of the upgrade channel, so this page's merge flow doesn't apply; upstream fixes are yours to read off the diff and reapply by hand, and the frontend drifts from the NuGet-versioned backend contract. To keep pulling upstream fixes, don't take this path — use the fork model above.
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

Most merge friction comes from editing the same files upstream also touches. Three habits keep it manageable:

- **Put your own code in new files**, not inside existing ones. New files never conflict; only shared ones do. Every step of [Add a Frontend Page](/guide/frontend-page) is built around this, and there is a dedicated home for each kind of thing:

  | Your code | Goes in | Not in |
  |---|---|---|
  | Domain types | a new `web/src/types/<module>.ts` | `types/api.ts` |
  | API wrappers | a new `web/src/api/<domain>.ts` (import `unwrap` / `pageParams` / `toPage` from `./index`) | `api/index.ts` |
  | i18n text | a new `web/src/locales/ext/<locale>/<module>.ts` (globbed in automatically — see the [README](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/src/locales/ext/README.md) there) | `locales/zh-CN.ts` / `en-US.ts` |
  | Pages | a new `web/src/views/<module>/` directory | any existing view |

  Those four upstream files are the highest-churn files in `web/src` — they change on nearly every release. That's exactly why your code shouldn't live in them. Conversely, files you own and upstream rarely touches (`styles/tokens.css`, your own views) are safe to edit freely: a conflict needs *both* sides to change the same file.

- **If you must customize a shared file** (a layout, a store, a built-in page), expect to resolve a conflict there each time upstream also changes it — that's normal, not a sign something's wrong. Keep such edits few and small.

- **`web/src/api/schema.d.ts` is a special case: never merge it, regenerate it.** It's a 6000-line generated artifact, and since your backend has your own controllers, your copy diverges from upstream's from day one — so upstream changes to it always land as a whole-file conflict. Don't try to resolve it by hand:

  ```bash
  git checkout --ours web/src/api/schema.d.ts   # keep yours, discard upstream's
  npm run gen:api                               # then regenerate against YOUR running backend
  git add web/src/api/schema.d.ts
  ```

  The conflict is doing you a favour: it's the signal that the backend contract moved and your types need regenerating. (This is why the repo deliberately does *not* ship a `merge=ours` gitattribute — silently keeping your copy would hide that signal, and leave you on a stale contract.)

## 5. Track what changed

- [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md) — Keep a Changelog format, one entry per release, covers both halves and calls out breaking changes explicitly (the project is pre-1.0, so the API can still shift).
- The login page footer shows `web/package.json`'s `version` — after merging, bump it to match the tag you merged so what your users see matches what's actually running.

Everything above is about pulling upstream changes into your fork. The reverse — contributing your own changes back to TenonAdmin — is the domain of the [Contributing Guide](/community/contributing).
