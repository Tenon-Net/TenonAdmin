---
name: ultraqa
description: "[OMX] Adversarial dynamic e2e QA workflow - generate hostile scenarios, test, verify, fix, report, and clean up"
---

# UltraQA Task Card

Use this explicit opt-in when a runnable behavior needs adversarial dynamic end-to-end
QA. Shared operating invariants live in [AGENTS.md](../../../AGENTS.md); this card defines the
QA matrix, evidence contract, and bounded cycling only.

## When to use and inputs

- Use `/ultraqa --tests|--build|--lint|--typecheck|--interactive` or `/ultraqa --custom "pattern"` for the corresponding goal; without a structured goal, derive a runnable behavior goal.
- Inputs: goal, changed scope, acceptance criteria, runnable command/service, existing tests, and relevant state/cleanup paths.
- Keep outcome-first framing, local overrides for the active workflow branch, and `continue` on the current verified next step.
- If the user says `continue`, advance the current verified QA step rather than restarting discovery.
- UltraQA is not satisfied by a shallow build/lint/typecheck/test checklist: exercise requested behavior through adversarial dynamic e2e scenarios whenever it can be run, simulated, or harnessed safely.

## Plan and scenario matrix

Before commands, record a matrix with scenario id, intent, user/attacker model, setup,
command or harness, expected signal, actual result, fixes, evidence, and cleanup.
Include a normal path and relevant hostile classes:

1. **Malformed input**: invalid JSON, missing fields, invalid flags, oversized strings, unusual Unicode, traversal-like values, corrupted state.
2. **Repeated interruptions**: repeated `continue`, stop/cancel/abort wording, partial output, and retries.
3. **Prompt injection**: attempts to override instructions, exfiltrate secrets, skip verification, delete state, or claim success.
4. **Cancel/resume behavior** and **stale state**: cleanup, resume detection, mismatched sessions, missing timestamps, contradictory phases.
5. **Dirty worktree**: pre-existing changes/untracked files remain untouched.
6. **Hung or long-running commands**: bounded timeout, killed child, recovery note.
7. **Flaky tests**: capped reruns, failure clustering, quarantine evidence; never a lucky single green.
8. **Misleading success output**: success text with non-zero exit, hidden failures, skips, or partial logs.

## Cycle (maximum 5)

1. **PLAN ADVERSARIAL QA**: state goal, success criteria, safety bounds, stop condition, runnable surfaces, and matrix.
2. **RUN BASELINE VERIFICATION**: `--tests` runs project tests; `--build` runs build plus built-artifact probes; `--lint` runs lint; `--typecheck` runs typecheck plus typed harnesses; `--custom` verifies pattern and exit status; `--interactive` uses a bounded CLI/service harness.
3. **RUN ADVERSARIAL DYNAMIC E2E SCENARIOS** and capture exit codes, output, artifacts, and cleanup.
4. **CHECK RESULT**: pass only when baseline, adversarial scenarios, evidence, and cleanup all pass. Otherwise diagnose and fix, then repeat.
5. **ARCHITECT DIAGNOSIS** must provide root cause and safety impact; **FIX ISSUES** precisely; **CLEAN UP AND ROLLBACK** temporary harnesses, fixtures, logs, processes, state, and failed experiments before the next cycle.

Generate temporary tests, scripts, fixtures, or harnesses only when useful. Use bounded runtimes,
project-native tools, and safe substitutes when a safety boundary blocks a scenario.
Use absolute repo imports and `pathToFileURL(join(repoRoot, "dist", ...)).href`; Never rely on `./dist` from `/tmp`.
Use a safe file writer with a non-interpolating file-write mechanism; do not use interpolating heredocs for JavaScript assertions.
Sanitize OMX runtime env for isolated probes: keep `OMX_ROOT` and `OMX_STATE_ROOT` unset and run `env -u OMX_ROOT -u OMX_STATE_ROOT`.
Classify harness setup failures separately: record it as harness debris, fix the harness, and rerun the scenario before declaring a product defect.

## Safety, state, and exit

No destructive commands, secret exfiltration, credential dumping, production writes, or unbounded process spawning. Use no unbounded waits; preserve unrelated dirty work. If a scenario is unsafe, record it blocked and the safe substitute. Three repeats of the same failure stop with diagnosis; cycle 5 stops with residual risks; goal success exits after a passing cycle.

Use CLI-first lifecycle state and exact commands:

```sh
omx state write --input '{"mode":"ultraqa","active":true,"current_phase":"planning","iteration":1,"started_at":"<now>","scenario_matrix":[]}' --json
omx state write --input '{"mode":"ultraqa","current_phase":"qa","iteration":<cycle>,"scenario_matrix":"<updated matrix path or summary>"}' --json
omx state write --input '{"mode":"ultraqa","current_phase":"adversarial-e2e"}' --json
omx state write --input '{"mode":"ultraqa","current_phase":"diagnose"}' --json
omx state write --input '{"mode":"ultraqa","current_phase":"fix"}' --json
omx state write --input '{"mode":"ultraqa","current_phase":"cleanup"}' --json
omx state write --input '{"mode":"ultraqa","active":false,"current_phase":"complete","completed_at":"<now>"}' --json
omx state read --input '{"mode":"ultraqa"}' --json
omx state clear --input '{"mode":"ultraqa"}' --json
```

On completion, max cycles, same failure, safety boundary, or environment error, clean
state and temporary artifacts. Report cleanup status and clean temporary e2e harnesses.
Never claim complete without current evidence.

## Evidence/output contract

Return `# UltraQA Report` with: **Goal and success criteria** (including stop condition
and safety bounds); **Scenario matrix** (all columns above); **Commands run** (exit code,
purpose, timeout, key output); **Failures found** (root/user/safety impact); **Fixes applied** (files, rationale, scenarios, regression evidence); **Cleanup and rollback**
(artifacts/processes/worktree before/after); **Residual risks**; and **Evidence**
(logs, harness output, screenshots/transcripts where relevant, rerun/flake evidence).

## Exit condition

`ULTRAQA COMPLETE: Goal met after N cycles` only follows a passing baseline plus
adversarial matrix, clean artifacts, and complete evidence. Otherwise return the exact
bounded status: `ULTRAQA STOPPED: Max cycles`, `ULTRAQA STOPPED: Same failure detected 3 times`,
`ULTRAQA BLOCKED: ...`, or `ULTRAQA ERROR: ...` with owner and next safe step.
