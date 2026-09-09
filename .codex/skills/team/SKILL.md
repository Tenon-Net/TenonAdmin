---
name: team
description: "[OMX] N coordinated agents on shared task list using tmux-based orchestration"
---

# Team Skill

## When to use

Use only for an explicit `$team` request or `omx team ...` launch that benefits from durable tmux workers, shared task state, mailbox coordination, or worktree isolation. Read `AGENTS.md#durable-runtime-invariants-canonical-ssot`; do not copy its durable rules into this card.

## Inputs and preconditions

- Launch shape: `omx team [N:agent-type] "<task>"` (for example `omx team 3:executor "implement X"`).
- Require `tmux -V`, a leader session with `$TMUX`, and the intended `omx` executable. In Codex App/plain non-tmux sessions, explain the runtime boundary instead of pretending Team is available.
- Before launch, ground the task in a recent `.omx/context/{slug}-*.md`; create a concise snapshot when none exists. Include target, evidence, constraints, unknowns, and likely touchpoints.
- Do not launch nested Team runs. Pick worker roles deliberately; use `OMX_TEAM_WORKER_CLI=codex|claude|auto` or `OMX_TEAM_WORKER_CLI_MAP=...` only when needed.
- Model precedence: explicit `OMX_TEAM_WORKER_LAUNCH_ARGS`, inherited leader `--model`, then `OMX_DEFAULT_SPARK_MODEL` (legacy alias `OMX_SPARK_MODEL`) for low-complexity defaults. Normalize to one `--model <value>` flag; read configured `OMX_DEFAULT_FRONTIER_MODEL` / `OMX_DEFAULT_SPARK_MODEL` instead of guessing model names.
- Per-agent reasoning is set by `agentReasoning`; accepted values are `low`, `medium`, `high`, `xhigh`, and `max`. `max` is passed unchanged and remains capability-dependent. `ultra` is unsupported and is not an alias for `max`.
- Invalid configured values use the built-in role-default fallback. An explicit raw `-c model_reasoning_effort=...` is opaque and wins. When both sources are present, explicit raw reasoning wins over inherited Team reasoning and environment reasoning. Do not downgrade or retry `max` as `xhigh`; built-in role defaults remain unchanged.

## Operational steps

1. Start the runtime and capture startup evidence: `Team started: <name>`, tmux target, worker panes, and the leader ACK mailbox.
2. **Current Runtime Behavior:** runtime-owned task files and APIs are the operational interface; the durable ownership and safety rules remain in [AGENTS.md](../../../AGENTS.md).
3. Runtime creates `.omx/state/team/<name>/config.json`, `manifest.v2.json`, `tasks/task-<id>.json`, worker identities/inboxes, mailbox files, and a dispatch queue. Workers receive `OMX_TEAM_WORKER`, `OMX_TEAM_STATE_ROOT`, and `OMX_TEAM_LEADER_CWD`.
4. Deliver assignments through durable inbox/task state and the CLI API. The worker card defines ACK, claim, transition, mailbox, and idle-status commands.
5. Prefer these machine-readable operations:
   ```bash
   omx team api send-message --input '{"team_name":"<name>","from_worker":"leader-fixed","to_worker":"worker-1","body":"<short trigger>"}' --json
   omx team api read-task --input '{"team_name":"<name>","task_id":"<id>"}' --json
   omx team api transition-task-status --input '{"team_name":"<name>","task_id":"<id>","from":"in_progress","to":"completed","claim_token":"<token>"}' --json
   ```
6. Monitor with `omx team status <name> --json` or `omx team await <name> --timeout-ms 30000 --json`; inspect mailbox/state files when a worker is blocked or stale.
7. Keep the team running until `pending=0`, `in_progress=0`, and `failed=0` (or an explicitly acknowledged failure path). Run `omx team shutdown <name>` only then, unless the user explicitly aborts.
8. Verify shutdown evidence and state cleanup. Do not claim completion while workers are still writing.

## Team and Ultragoal

When a leader-owned `.omx/ultragoal/goals.json` exists, workers return task evidence only. The leader checkpoints with a fresh Codex `get_goal` snapshot:
```bash
omx ultragoal checkpoint --goal-id <id> --status complete --evidence "<Team evidence>" --codex-goal-json <fresh-get-goal-json-or-path>
```
Team launch remains an explicit separate action; it does not create hidden Codex goals.

## Dispatch and recovery

- If dispatch reports `worker_notify_failed:<worker>`, inspect `tmux list-panes`, capture the pane, then send one concise trigger and re-check mailbox/state.
- For Claude panes, do not spam Enter while work is active. Confirm runtime status first.
- If a worker reports `omx team api ... ENOENT`, check whether shutdown or state deletion happened too early; preserve state until all transitions finish.
- For a clean retry, kill only known stale worker panes, remove only the exact stale Team root, and relaunch; never kill the leader/HUD pane accidentally.

## Exit and evidence

Report team name, launch command, pane/ACK evidence, task counts, worker verification, failures or blockers, shutdown result, and cleaned state paths. Include any worktree or CLI-map choices and keep final integration/verification in the leader lane.
