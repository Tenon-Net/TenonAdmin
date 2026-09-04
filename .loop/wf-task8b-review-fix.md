# Task 8b review repair: token visit fencing on resubmit

## State

- status: `DONE`
- round: `4 / 10`
- baseline: `8b40b2849628cc458d2b87a631b6822dd8e25aed` (`dev`, PostgreSQL scheduler-test correction)
- protected untracked path: `backend/tests/TenonAdmin.Tests/TestResults/`
- current task: complete
- next: none; Task 8b review repair and its four-database CI are closed.

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

## CI follow-up: PostgreSQL scheduler-test boundary

- Commit `f49ce2d` was pushed normally. `contract-drift` and `docker-smoke` passed; backend SQLite, MySQL, SQL Server, and template-smoke passed. PostgreSQL was the only failure.
- PostgreSQL log identified `WfNodeExecutionWorkerTests.Scheduler_tick_fires_the_seeded_worker_through_executor_and_handler_resolver`: expected job-log `Success`, actual `Skipped`.
- Root cause: the test manually set `NextRunTime = DateTime.Now.AddMinutes(-1)`, exactly on `AdminJobsOptions.MisfireThresholdSeconds = 60`. The scheduler intentionally evaluates `now - expected > threshold` and the workflow seed intentionally uses `MisfireStrategy.Skip`; PostgreSQL timestamp precision made the row legitimately late enough to skip, while local SQLite timing often did not.
- The test now uses `DateTime.Now.AddSeconds(-10)`: due enough to be dispatched, safely inside the 60-second non-misfire window. It does not alter scheduler production behavior, seed cadence, or configuration.
- Focused scheduler test repeated **5/5** locally with exit code 0. Docker/PostgreSQL is unavailable in this Windows workspace, so PostgreSQL confirmation requires the fresh GitHub matrix; do not claim local PostgreSQL coverage.

## Final remote CI evidence

- `8b40b28` was pushed normally after the test-only correction. Local and remote `dev` matched that commit before this evidence update.
- Backend run [`33828658172`](https://github.com/Tenon-Net/TenonAdmin/actions/runs/33828658172) completed **success**: SQLite, MySQL, PostgreSQL, SQL Server, and template-smoke all passed. PostgreSQL specifically confirms that the seeded-worker scheduler test no longer crosses the misfire boundary.
- Contract-drift run [`33828658095`](https://github.com/Tenon-Net/TenonAdmin/actions/runs/33828658095) completed **success**.
- Docker smoke run [`33828658106`](https://github.com/Tenon-Net/TenonAdmin/actions/runs/33828658106) completed **success** for both single-replica and multi-replica checks.
- The preceding `f49ce2d` backend run had one PostgreSQL failure and was superseded by `8b40b28`; its remaining SQLite/MySQL/SQL Server/template-smoke checks had passed. No open P1/P2 remains after the deterministic scheduler-fixture correction and the fresh all-green matrix.

## Scope boundaries

- No new dependencies, worker fleet, schema column, or outbox consumer.
- Keep Task 8c transport/consumer work deferred.
- Do not remove, clean, stage, or commit `backend/tests/TenonAdmin.Tests/TestResults/`.
