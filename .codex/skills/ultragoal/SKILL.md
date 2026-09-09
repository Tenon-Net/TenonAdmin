---
name: ultragoal
description: "[OMX] Create and execute durable repo-native multi-goal plans over Codex goal mode artifacts."
---

# Ultragoal Workflow

## When to use

Use for `ultragoal`, `create-goals`, `complete-goals`, durable multi-goal planning, or sequential execution over Codex `/goal`. Read `AGENTS.md#durable-runtime-invariants-canonical-ssot` for state ownership and goal-tool boundaries.

## Inputs and preconditions

- Durable artifacts are `.omx/ultragoal/brief.md`, `.omx/ultragoal/goals.json`, and `.omx/ultragoal/ledger.jsonl`.
- New plans use aggregate Codex goal mode by default: one stable pointer objective for the plan while OMX tracks individual stories. Use `--codex-goal-mode per-story` only when explicitly requested.
- Do not call `/goal clear` from shell or this skill. After a completed run, the operator may clear the interactive Codex goal in the Codex UI before another same-thread run.

## Operational steps: create

Run one command, then inspect the generated plan:
```bash
omx ultragoal create-goals --brief "<brief>"
omx ultragoal create-goals --brief-file <path>
cat <brief> | omx ultragoal create-goals --from-stdin
omx ultragoal create-goals --codex-goal-mode per-story --brief "<brief>"
```
If refinement is needed, use explicit supported steering rather than editing durable artifacts by hand.

## Operational steps: execute

Repeat until `omx ultragoal status` reports all goals complete:

1. Run `omx ultragoal complete-goals` and read its handoff.
2. Call `get_goal`. Call `create_goal` with the printed payload only when no active Codex goal exists; otherwise continue the matching aggregate objective.
3. Complete one OMX story and audit its objective against real artifacts and verification evidence.
4. For intermediate aggregate stories, keep the Codex goal active and checkpoint:
   ```bash
   omx ultragoal checkpoint --goal-id <id> --status complete --evidence "<evidence>" --codex-goal-json <fresh-get-goal-json-or-path>
   ```
5. For a blocked/failed story, use `--status blocked|failed` with evidence; resume failed work with `omx ultragoal complete-goals --retry-failed`.
6. On the final story, complete the final gate below before `update_goal({status: "complete"})`; then call `get_goal` again and checkpoint the fresh complete snapshot.

## Explicit steering

Use evidence-backed directives only when the decomposition must change:
```bash
omx ultragoal steer --kind add_subgoal --title "<title>" --objective "<objective>" --evidence "<evidence>" --rationale "<reason>" --json
omx ultragoal steer --directive-json ./steering.json --json
```
Supported kinds: `add_subgoal`, `split_subgoal`, `reorder_pending`, `revise_pending_wording`, `annotate_ledger`, and `mark_blocked_superseded`. Ordinary prose does not mutate the plan; repeated structured directives dedupe.

## Phase/HUD handoff

At standalone activation, declare the smallest accurate phase (`planning`, `executing`, `verifying`, `reviewing`, `checkpointing`, or `blocked`):
```bash
omx state write --input '{"mode":"ultragoal","active":true,"current_phase":"planning"}' --json
```
Inside Autopilot, keep the parent mode active and set `current_phase:"ultragoal"`; persist Ultragoal evidence under `handoff_artifacts.ultragoal` before a code-review handoff. Mark standalone Ultragoal complete only when every durable goal is complete.

## Optional Team bridge

For parallel story execution, use the separate Team command. The leader records the Ultragoal checkpoint with a fresh `get_goal` snapshot; see the Team skill for the bridge details.

## Final gate and exit evidence

Before final completion:

1. Run targeted story verification.
2. Run `ai-slop-cleaner` on changed files. Rerun affected verification if cleanup changes them; otherwise retain the fresh step-1 evidence in the required quality gate.
3. Audit every architecture/domain invariant from the brief/spec/accepted steering against implementation, test, and review evidence.
4. Run `$code-review` through independent `code-reviewer` and `architect` lanes. If review or invariant proof is not clean, do not update the Codex goal; record durable blockers:
   ```bash
   omx ultragoal record-review-blockers --goal-id <id> --title "Resolve final code-review blockers" --objective "<objective>" --evidence "<findings>" --codex-goal-json <active-get-goal-json-or-path>
   ```
5. If clean, call `update_goal({status: "complete"})`, call `get_goal`, and checkpoint with `--quality-gate-json` containing cleaner, verification, review, and architecture-invariant evidence.

Report goal ids/statuses, ledger/checkpoint paths, fresh goal snapshot evidence, review verdicts, and any blocker. Never claim completion from OMX state alone.
