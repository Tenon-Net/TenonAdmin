# Agent Skills and AI-Assisted Development

The gap is never correctness. Ask an agent for a CRUD module and you will usually get something that runs; what you won't get is something shaped like the rest of this repo. Two sets of conventions close that gap, and which one applies depends on whose code the agent is touching:

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
| `ready-for-agent` | Requirement is clearly described and can be handed straight to an AFK agent |
| `ready-for-human` | Needs a human to implement |
| `wontfix` | Won't be addressed |

For tasks that can be automated, issues labeled `ready-for-agent` are the easiest pick.

## Domain docs: CONTEXT.md + docs/adr

Before exploring the code, an agent should first read (see [`docs/agents/domain.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/agents/domain.md) for details):

- `CONTEXT.md` at the repo root (or `CONTEXT-MAP.md` in multi-context scenarios, pointing to each context's own `CONTEXT.md`);
- ADRs under `docs/adr/` relevant to the area being changed.

::: tip These two don't exist yet — that's expected
TenonAdmin doesn't have a `CONTEXT.md` or `docs/adr/` yet — by convention they're created "lazily," only when a skill like `/domain-modeling` actually needs to record a term or a decision. Their absence doesn't mean the convention doesn't exist, and it's not a reason to demand the docs be backfilled first.
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
| `replace-service` | Replace/extend a built-in service | Customize login flow, swap password hashing, override service steps |
| `create-page-variant` | Non-standard page templates | Tree tables, master-detail split, sidebar filters |

Under **Claude Code**, these six skills are already wrapped as slash commands under `.claude/skills/` — just type `/new-module`, `/create-entity`, `/create-crud-backend`, `/create-crud-frontend`, `/replace-service`, or `/create-page-variant`. They also support natural-language auto-triggering, e.g. just saying "help me create a Product entity." Other AI tools don't have a slash-command mechanism, so reference the file path directly in the conversation instead — e.g. "refer to skills/create-entity.md and help me create a BizProduct entity."

Standard order for adding a complete new CRUD module (`/new-module` chains these three into a single run; call them individually if you want to go step by step):

1. `/create-entity` — create the entity
2. `/create-crud-backend` — build the backend (including menu seed data)
3. `/create-crud-frontend` — build the frontend (including i18n)

Those three steps and `new-module` all distinguish between **system module** (kernel maintainer) and **business module** (consumer extension) modes, with different generated code locations and naming rules, so be clear about which scenario applies before using them. `replace-service` targets consumers only, and `create-page-variant` splits by page shape, so neither has that fork.

## Reference

- The "Agent skills" section of the root [`CLAUDE.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/CLAUDE.md) is the index entry point for these conventions.
- To walk through replacing/extending a built-in service by hand (rather than having an agent generate it via the `replace-service` skill), see [Replacing Built-in Services](/guide/replace-service).
- For how to run tests and submit PRs, see the [Contributing Guide](./contributing).
