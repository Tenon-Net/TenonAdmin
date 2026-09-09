---
name: skill
description: "[OMX] Inspect, add, edit, validate, or remove local agent skills."
argument-hint: "<list|add|edit|remove|search|info|sync|setup|scan|validate> [args]"
---

# Skill management

Follow [AGENTS.md](../../../AGENTS.md) for scope and authority. Use the available skill-creator guidance for authoring; this card manages existing discovery locations.

## Scope and commands

Standard Codex skills live in `.agents/skills/<name>/SKILL.md` (repository) or `~/.agents/skills/` (user). This OMX installation also exposes `.codex/skills/` and the configured Codex home skills directory. Inspect the active catalog and preserve a requested path; do not duplicate names across roots visible to the same host.

- `list` / `scan`: inventory names, descriptions, paths, and duplicate entries.
- `search <query>` / `info <name>`: find metadata/body matches or read the selected skill.
- `add` / `edit`: infer routine metadata from the requested purpose, load the existing file/references, then make the scoped change.
- `sync`: compare requested roots and copy only the selected skills, preserving unrelated content.
- `remove`: resolve the exact skill and its references before removal; confirm destructive targets when authorization is missing.
- `validate`: inspect without writing; report invalid metadata, unresolved local references, duplicate discovery, and incomplete templates.
- `setup`: inspect current discovery and create only requested missing entries.

Use the supplied command directly. If no operation can be inferred, ask what the user wants to manage.

## Authoring and validation

Require YAML `name` and `description`; use a concise scope-specific description. Keep optional UI/invocation metadata in `agents/openai.yaml` and preserve existing policy. OMX-specific metadata is an extension, not a requirement for portable skills.

Each body describes the outcome, necessary constraints, relevant reference branches, and observable completion. Put large conditional procedures in linked references; reuse the project's existing recipe instead of copying it.

Validate YAML, name/description, referenced paths, and any changed scripts. Use the installed skill-creator validator when available; distinguish unsupported extension warnings from invalid YAML. Review consequential routing with realistic tasks. A static check cannot prove runtime behavior.

Report affected paths and validation evidence. Preserve files on cancellation or read-only commands.
