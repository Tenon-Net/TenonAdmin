# Agent Skills and AI-Assisted Development

An AI assistant needs the repository contracts, domain vocabulary, and matching development workflow before its code will fit the existing system. First decide whether the task changes the kernel or builds a consumer business module, then point the assistant at the matching entry point:

- **Contributing to TenonAdmin itself** — a set of docs under `docs/agents/` specifying how agents should read issues, apply triage labels, and read domain background.
- **Building business modules on top of TenonAdmin** — a set of development-standard docs under `skills/` that teach an agent to create entities, build CRUD, and replace services following the project's established patterns, whether you're a kernel maintainer adding a system module or a consumer building on top of it in your own project.

Neither set is a code generator. Both are rules plus reference templates: the agent reads them, then writes the code your requirement calls for, and the conventions only govern what that code looks like.

## Issues / PRDs: via GitHub Issues

The repo's issues and PRDs are all GitHub issues, managed uniformly via the `gh` CLI (see [`docs/agents/issue-tracker.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/agents/issue-tracker.md) for conventions):

```bash
gh issue create --title "..." --body "..."          # create an issue; use a heredoc for multi-line body
gh issue view <number> --comments                     # read an issue (including comments)
gh issue comment <number> --body "..."                 # comment
gh issue edit <number> --add-label "..."               # add a label
gh issue close <number> --comment "..."                # close
```

`gh` run inside a cloned copy of the repo auto-detects the repo from `git remote -v`, so there's no need to pass `--repo` explicitly.

::: details PRs are not currently treated as a request entry point
The `issue-tracker.md` switch for this is currently "no": external PRs don't go through the same labeling flow as issues. If it's ever flipped to "yes," the `gh pr` command family (`gh pr view`, `gh pr diff`, `gh pr comment`, `gh pr edit --add-label`) will be enabled, and only external PRs with `authorAssociation` of `CONTRIBUTOR` / `FIRST_TIME_CONTRIBUTOR` / `NONE` will participate in triage.
:::

## Triage labels

Issue triage uses five normalized labels, where the label string is the role name itself (see [`docs/agents/triage-labels.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/agents/triage-labels.md) for details):

| Label | Meaning |
|---|---|
| `needs-triage` | Not yet evaluated by a maintainer |
| `needs-info` | Waiting on the reporter for more information |
| `ready-for-agent` | Requirements, boundaries, and acceptance criteria are clear enough for automated execution |
| `ready-for-human` | Needs a human to implement |
| `wontfix` | Won't be addressed |

Automated agents should start with `ready-for-agent`; the other labels still require maintainer judgment or more information from the reporter.

## Domain docs: CONTEXT.md + docs/adr

Before exploring the code, an agent should first read (see [`docs/agents/domain.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/agents/domain.md) for details):

- `CONTEXT.md` at the repo root (or `CONTEXT-MAP.md` in multi-context scenarios, pointing to each context's own `CONTEXT.md`);
- ADRs under `docs/adr/` relevant to the area being changed.

::: tip Read only what the change needs
The repository already has a root `CONTEXT.md`, and `docs/adr/` records decisions for realtime, external login, scheduling, and optional security, among others. Read `CONTEXT.md` first, then select only the ADRs directly related to the change. If no ADR applies, continue without creating one merely to satisfy a process.
:::

If your output uses domain terminology (issue titles, refactor proposals, test names), keep it consistent with the terms in `CONTEXT.md` rather than swapping in near-synonyms where a term is already clearly defined; if your output conflicts with an existing ADR, call out the conflict explicitly rather than silently overriding the prior decision with a new approach.

## Business-development skills (`skills/`)

This set of docs targets "building business features on top of TenonAdmin" — whether you're a kernel maintainer adding a system module or a consumer building on top of it in your own project, both follow the same pattern (see [`skills/README.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/skills/README.md) for details):

| Skill | Purpose | Applicable scenario |
|---|---|---|
| `new-module` | End-to-end orchestration for adding a complete business module | Building a module from scratch (entity → backend → frontend → menu/permission) |
| `create-entity` | Create a SqlSugar entity class | New table, new entity |
| `create-crud-backend` | Create a full backend CRUD set | Models + Interface + Service + ErrorCode + DI + Controller |
| `create-crud-frontend` | Create a frontend CRUD page | Types + API + Vue page (ProTable + FormContainer) |
| `create-crud-frontend-react` | Create a React CRUD page | Types + API + React page (DataTable + FormContainer + `<Can>`) |
| `replace-service` | Replace/extend a built-in service | Customize login flow, swap password hashing, override service steps |
| `wire-import-export` | Wire import/export on your entity | Install `TenonAdmin.Excel`, profiles, six endpoints, menu Ids |
| `wire-integration` | Connect your business to third-party systems | Install `TenonAdmin.Integration`, open API, plain calls, transactional reliable delivery |
| `create-job` | Add a scheduled job to a business module | `IAdminJob`, HTTP/SQL jobs, admin configuration, and verification |
| `create-page-variant` | Non-standard page templates | Tree tables, master-detail split, sidebar filters |

Claude Code discovers slash commands under `.claude/skills/`; Codex discovers `$new-module`, `$create-entity`, and the other project skills under `.agents/skills/`. Both entry-point sets refer to the same workflows under `skills/`. For an AI tool without skill discovery, provide the file path directly, for example: "Use `skills/create-entity.md` to create a BizProduct entity." Treat [`skills/README.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/skills/README.md) as the current command list.

The standard order for a complete CRUD module is below. `new-module` chains the steps in one run; invoke them individually when you want to work step by step:

1. `/create-entity` — create the entity
2. `/create-crud-backend` — build the backend (including menu seed data)
3. Choose `create-crud-frontend` or `create-crud-frontend-react` for the selected template, including i18n

Those three steps and `new-module` all distinguish between **system module** (kernel maintainer) and **business module** (consumer extension) modes, with different generated code locations and naming rules, so be clear about which scenario applies before using them. `replace-service` targets consumers only, and `create-page-variant` splits by page shape, so neither has that fork.

## Reference

- Root [`AGENTS.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/AGENTS.md) is the single source of repository instructions; `CLAUDE.md` only imports it.
- To walk through replacing/extending a built-in service by hand (rather than having an agent generate it via the `replace-service` skill), see [Replacing Built-in Services](/guide/replace-service).
- For how to run tests and submit PRs, see the [Contributing Guide](./contributing).
