# Commit Standards

::: tip TL;DR
Code, comments, and docs are all in Chinese; **Git commit messages are always in English**, following the conventional-commit format: `type(scope): subject`.
:::

## Why commit messages are English while code comments are Chinese

The two have different audiences. Code and docs are for people maintaining this repo — internal team communication is more efficient and precise in Chinese. But commit history (`git log`) is part of the repo's external face — TenonAdmin distributes as a NuGet package to all .NET users, and commit history gets read by contributors, downstream consumers, and automated tooling (release-note generation, semantic-version detection). Standardizing on English keeps that history readable by everyone and parseable by tools.

## Format

```
type(scope): subject

[optional body explaining motivation/impact, not restating the diff]
```

- `type`: a fixed vocabulary, see below.
- `scope`: optional, marks the affected area (module name, package name, directory name). Omit it for cross-cutting changes with no single clear scope.
- `subject`: imperative mood, lowercase first letter, no trailing period, one sentence stating what was done.

## `type` values

Distilled from the repo's actual commit history — pick the closest semantic match for new commits, don't invent new words:

| type | used for |
|---|---|
| `feat` | New features |
| `fix` | Bug fixes |
| `docs` | Documentation-only changes (README, design docs, comment translation, etc.) |
| `refactor` | Internal refactoring with no external behavior change |
| `test` | Adding or adjusting tests |
| `build` | Build artifacts, packaging metadata (e.g. template package icon/README) |
| `ci` | CI/CD pipeline, release process |

## Real examples (from this repo's history)

```
fix(web,backend): gate every action button by button-level permission
fix(web): hide permission-gated buttons for users without access
feat: demo mode — block write operations for showcase deployments
feat(notice): targeted delivery + rich notification bell
feat(menu): app-scoped permission-route picker + menu UX cleanup
refactor(web): fold the repeated paged-list mapping into two helpers
test: backfill CRUD/endpoint coverage for untested service areas
build(templates): add package icon and readme to the template package
ci(release): fix OIDC username typo (hu531035580 -> hu53135580)
docs: sync READMEs to zh-CN baseline, log 0.1.0 features
```

Key points:

- `scope` can be a single module (`web`, `notice`, `menu`), or several comma-separated (`web,backend`) — when a change genuinely spans both sides, say so honestly rather than forcing it into one.
- When there's no single clear scope, or the change is inherently global (e.g. a repo-wide doc translation, a new cross-cutting capability), omit `scope` and write `type: subject` directly.
- `subject` may use `—`/`:`/parentheses for brief elaboration, but it should still read as one sentence — don't write it as multiple compound sentences.

## When to write a body

Add a body only when a one-line `subject` can't convey the change's **motivation** or **impact**, for example:

- The fix addresses a subtle security issue and needs to explain the trigger condition and blast radius.
- A change touches multiple coordinated pieces (frontend/backend field renames, config migration) and future readers need a "why we did it this way" explanation.

The body isn't for restating the diff (the diff itself already shows what code changed) — it's for background that isn't visible in the diff. Routine small fixes (style tweaks, a single bug fix) don't need a body; a one-line `subject` is enough.

## Common mistakes

::: warning Don't do this
- Writing the commit message in Chinese (even though code comments are Chinese, commits must be English).
- Capitalizing the first letter of `subject` or ending it with a period (follow conventional-commit convention: lowercase start, no period).
- Cramming multiple unrelated changes into one commit and summarizing them with one vague `type` (e.g. mixing a `feat` and a `fix` in the same commit) — split them into separate commits by semantics.
- Using a `type` that's never appeared in the repo's history (like `update` or `change`) — that semantics is already covered by `feat`/`fix`/`refactor`, and inventing new words only makes history inconsistent.
:::
