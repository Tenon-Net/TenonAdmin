# Syncing Your Fork with Upstream

After building business features on the Vue or React template, use Git to merge upstream fixes while preserving your changes. This procedure applies to forks that retain repository history. Projects using only the backend through NuGet can update their packages directly.

A degit snapshot has no upstream history, so fixes need manual migration. Both approaches require checking frontend/backend contracts and release notes: a successful Git merge does not prove runtime compatibility.

## 1. Fork and clone

Fork [Tenon-Net/TenonAdmin](https://github.com/Tenon-Net/TenonAdmin) on GitHub, then clone your repository:

```bash
git clone https://github.com/<your-username>/TenonAdmin.git
cd TenonAdmin
git remote add upstream https://github.com/Tenon-Net/TenonAdmin.git
git remote -v
```

The output should list your `origin` and the official `upstream`. Run subsequent commands from the repository root.

## 2. Pick a branch to track

`main` is the release branch and `dev` is for active development. Choose a matching tag for a published release. To track the release branch, create your business branch from `main`:

```bash
git checkout -b my-product main
```

Commit business changes on your own branch so they remain distinguishable from upstream updates. Use `dev` only if you accept unreleased features and behavior still being adjusted.

## 3. Pull upstream changes

Commit or stash your current work, read the target version's [changelog](/changelog), then fetch and merge upstream:

```bash
git fetch upstream --tags
git merge upstream/main
```

To upgrade to a particular release, replace `upstream/main` with its verified tag. Shared branches usually use merge; rebase rewrites commit history and should follow your team's agreement.

Resolve conflicts by preserving both business requirements and upstream fixes. Resolve source files first, regenerate API types, then run the checks at the end of this page.

## 4. Keep conflicts small

Separate business files reduce the chance of editing the same files as upstream, but cannot guarantee conflict-free updates. Common Vue locations follow; use the React template's own directories and conventions for React:

| Business content | Recommended location |
|---|---|
| Domain types | `web/src/types/<module>.ts` |
| API wrappers | `web/src/api/<module>.ts` |
| Business translations | `web/src/locales/ext/<locale>/<module>.ts` |
| Pages | `web/src/views/<module>/` |

Keep necessary layout, store, and built-in page changes focused. Record the business reason so future merges can determine whether the change is still needed. Extension locations may differ by version; check the selected release's source.

`schema.d.ts` is generated and should be regenerated from the merged backend. The following commands are only for an in-progress merge conflict. Start the backend containing your business endpoints, then run from the repository root:

```bash
git checkout --ours web/src/api/schema.d.ts
npm --prefix web run gen:api
git add web/src/api/schema.d.ts
```

For React, replace `web` with `web-react`. `--ours` only selects temporary file content; it does not replace regeneration. Its meaning differs during rebase, so do not use it blindly to preserve local business changes.

## 5. Track what changed

Run `npm ci`, `npm run lint`, `npm run typecheck`, and `npm run build` in your chosen frontend directory. Rebuild backend extensions and run their relevant tests too.

Sign in and verify menus, regular-user permissions, core business operations, and uploads. Confirm that generated API types contain your endpoints before committing the merge. The login page version comes from the frontend's `package.json` and should reflect what you deliver; your product can use its own version policy.

See [Contributing](/community/contributing) to send changes upstream.
