# Task 8b review repair: token visit fencing on resubmit

## State

- status: `LOCAL_VERIFIED`
- round: `2 / 10`
- baseline: `484e802a5f892c9b33b5d8cb4d4b4d3d5cf31477` (`dev`, Task 8b delivery + CI evidence)
- protected untracked path: `backend/tests/TenonAdmin.Tests/TestResults/`
- current task: local repair complete; awaiting a user instruction to commit and/or push
- next: if instructed, create one Lore-protocol commit with the repair, then push normally and wait for the database CI matrix; do not commit or push without a new explicit instruction.

## Review finding and fixed decision

The post-delivery review found that `WfNodeExecution.Fence` fenced only the execution row. A `ResubmitInstanceCmd` can reuse the same active token while an old Webhook handler is outside tx2; without an additional boundary, the old successful result can load the new token visit and advance it by the old node's transition.

The repair preserves current resubmit availability instead of rejecting a resubmit during an active automatic node:

1. To keep the same `execution → token` lock order as completion tx2, cancel all `Pending`/`RetryScheduled`/`Running` executions for that token, clear lease/retry fields, and increment Fence before the resubmit token-version CAS in the same engine transaction. A losing CAS rolls the invalidation back.
2. A late old handler therefore fails the existing `Id + Fence + Running` tx2 CAS, which rolls back its attempt/outbox/token writes.
3. Completion additionally treats a current token whose instance/node/node-visit differs from the execution snapshot as `Cancelled`, so a future token-relocation path cannot advance the wrong visit even if it forgets resubmit invalidation.
4. Superseding an unfinished execution is not a synthetic handler attempt and does not emit the normal completion outbox; its real external effect remains at-least-once and the new visit gets a fresh `ExecutionKey`.

## Tasks

- [x] Reproduce the review finding with a real worker, a blocking Webhook handler, a real resubmit, and a late handler result.
- [x] Confirm the red state: before production repair the old execution remains `Running` after resubmit.
- [x] Implement token-scoped active-execution invalidation, fence increment, completion node-visit defense, and a consistent `execution → token` lock order.
- [x] Add an independent dispatcher test for stale token-visit completion.
- [x] Mutation proof: removing the node-visit defense changes stale completion from `Cancelled` to `Succeeded`.
- [x] Repair post-review lock ordering to `execution → token`, then cover Pending/RetryScheduled invalidation alongside Running.
- [x] Run final targeted regression set, release build, and static diff checks.
- [x] Record final evidence; leave commit/push pending explicit authorization.

## Evidence so far

- Red test: `WfNodeExecutionProductionE2ETests.A_resubmit_invalidates_the_old_webhook_execution_before_its_late_result_can_advance_the_new_visit` failed before the repair with `Expected: Cancelled`, `Actual: Running`.
- Green after repair: the same test passed; full `WfNodeExecutionProductionE2ETests` passed `6/6`.
- Existing resubmit/token-CAS tests passed `24/24` before the lock-order review follow-up; they must be rerun after the final reorder.
- Existing dispatcher/worker/exception/recovery tests passed `38/38` before the lock-order review follow-up; they must be rerun after the final reorder.
- Focused stale-token-visit plus resubmit tests passed `2/2`.
- Mutation: temporarily removing `IsExecutionTokenVisitCurrent` caused the stale-token-visit test to fail with `Expected: Cancelled`, `Actual: Succeeded`; source was immediately restored.

## Final local evidence

- Repaired production E2E plus the independent stale-token-visit test: **7 passed / 0 failed / 0 skipped**. The E2E now proves an old `Running` handler, plus superseded `Pending` and `RetryScheduled` rows, are all cancelled during resubmit; each has lease/retry cleared, Fence advanced, no pseudo attempt/outbox, and cannot move the replacement visit.
- Resubmit and token-CAS regression set after the final lock-order reorder: **24 passed / 0 failed / 0 skipped**.
- Dispatcher, worker, exception, and recovery regression set after the final lock-order reorder: **39 passed / 0 failed / 0 skipped**.
- `dotnet build backend/TenonAdmin.slnx -c Release --no-restore`: **0 warnings / 0 errors**.
- `git diff --check` passed; no temporary mutation marker or debug marker remains in the touched execution/test files.
- The first post-fix reviewer found the lock-order and coverage gaps documented above; both were fixed. A final architecture verifier was requested but produced no usable result before its tool session ended, so it is not counted as independent approval.
- Git boundary: no commit or push was attempted. The only non-task untracked path remains the protected `backend/tests/TenonAdmin.Tests/TestResults/`.

## Scope boundaries

- No new dependencies, worker fleet, schema column, or outbox consumer.
- Keep Task 8c transport/consumer work deferred.
- Do not remove, clean, stage, or commit `backend/tests/TenonAdmin.Tests/TestResults/`.
