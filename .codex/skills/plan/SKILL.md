---
name: plan
description: "[OMX] Create or review a concise, evidence-backed implementation plan; use interview mode when material requirements are unclear."
---

# Plan

Use `$plan` when the user asks for a plan or plan review. Clear implementation and focused fixes should execute directly. A request to skip planning leaves this skill and continues the requested work; it does not create an Ultragoal or another persistent workflow unless the user explicitly requests one.

## Modes

| Mode | Trigger | Result |
| --- | --- | --- |
| Direct | Detailed planning request or `--direct` | Write the plan from repository evidence |
| Interview | `--interview` or a material unresolved decision | Resolve the decision, then write the plan |
| Review | `--review` | Return `APPROVED`, `REVISE`, or `REJECT` with actionable reasons |

## Workflow

1. Inspect the relevant repository facts before asking about codebase details.
2. In interview mode, ask one focused question at a time and stop when the answer no longer changes the plan. In an attached OMX tmux runtime, use `omx question`; outside tmux, use native structured input when available, otherwise ask one concise plain-text question.
3. Write the smallest plan that covers the requested outcome, affected files or components, testable acceptance criteria, implementation order, material risks, and verification.
4. Cite concrete file paths and lines for consequential repository claims. Check that referenced files exist and that each acceptance criterion has an observable check.
5. Save the result under `.omx/plans/` unless the user requests another location.

For review mode, read the selected plan, test its claims against the repository, and explain every blocking gap. Use a specialist review only when it adds useful independent evidence.

## Stop conditions

- Deliver the plan when remaining uncertainty does not change implementation or acceptance criteria.
- Escalate only a business or product trade-off that cannot be resolved from repository evidence or existing authorization.
- If the user says `continue`, continue the current planning branch without restarting discovery.
