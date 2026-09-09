---
name: doctor
description: "[OMX] Diagnose oh-my-codex installation and discovery issues, then apply precise authorized repairs."
---

# OMX Doctor

Repository-wide authority, state, and cancellation rules live in [`AGENTS.md`](../../../AGENTS.md). Diagnose exact paths and ownership before changing installation state.

## Diagnose

Run the smallest command that covers the reported failure:

```bash
omx doctor
omx doctor --verbose
omx doctor --team
omx doctor --dry-run --force
```

For a local source checkout, build first and invoke `node dist/cli/omx.js doctor`. Record the command, exit status, exact failing paths, detected versions, and relevant output. With `--team`, retain any `resume_blocker`, `slow_shutdown`, `delayed_status_lag`, `stale_leader`, or `orphan_tmux_session` evidence.

Inspect only the surfaces implicated by that evidence:

- active package/plugin version and duplicate cache entries;
- hook references in `config.toml` or legacy `settings.json`, plus the referenced hook files;
- the applicable global and repository `AGENTS.md` files and their managed markers;
- discovered skill roots and duplicate skill names;
- legacy OMX-owned `agents/` or `commands/` artifacts;
- Team state only when Team diagnostics were requested.

`.agents/skills` and `~/.agents/skills` are supported Codex skill locations, not legacy content by definition. `.codex/skills` may also contain installed or repository-local skills. Report a conflict only when concrete duplicate names or stale OMX-owned artifacts affect discovery.

## Repair

Apply already-authorized, reversible repairs to the exact confirmed target, then rerun the failing diagnostic. Prefer the installer or CLI's targeted repair command, an in-place managed-block update, or moving one confirmed legacy artifact to a named backup.

Before deleting, replacing, or overwriting data, identify the exact path, prove it is OMX-owned, preserve unrelated content, and obtain confirmation when that destructive action is not already authorized. Never remove an entire skill, agent, command, hook, cache, or configuration tree because one entry is stale.

For a missing or outdated `AGENTS.md`, compare the installed release template with the applicable file and update only the authorized target while preserving repository and user customizations. Treat remote documentation as reference material, not as content to overwrite a global instruction file wholesale.

## Report and exit

Report `HEALTHY` only when the originally failing checks pass after repair. Otherwise report `ISSUES FOUND` with each check's status, evidence, smallest confirmed remediation, and any validation gap. A recommendation alone is not a completed fix.
