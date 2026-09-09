# execution contract

### Optional execution contract foundation

When an Autopilot/deep-interview handoff explicitly requires a stride contract, emit it as structured data rather than prose. This is a validation foundation, not a broadness-inference feature: do not infer stride from task length, phase labels, snapshots, or freeform wording.

Canonical location under Autopilot state:

```json
{
  "handoff_artifacts": {
    "deep_interview": {
      "execution_contract_required": true,
      "execution_contract": {
        "version": 1,
        "execution_stride": "task",
        "source": "deep-interview",
        "selected_by": "user",
        "allow_task_shrink": true,
        "completion_unit": "One focused task",
        "stop_condition": "Stop after that task is implemented and verified",
        "acceptance_coverage_scope": "task",
        "shrink_policy": "allowed"
      }
    }
  }
}
```

Stride meanings:
- `task`: conservative, small-step execution; `allow_task_shrink:true`, `acceptance_coverage_scope:"task"`, `shrink_policy:"allowed"`.
- `deliverable`: finish the named deliverable before stopping; `allow_task_shrink:false`, `acceptance_coverage_scope:"deliverable"`, `shrink_policy:"ask_before_shrink"`.
- `milestone`: finish the larger approved milestone unless blocked; `allow_task_shrink:false`, `acceptance_coverage_scope:"milestone"`, `shrink_policy:"deny_unless_blocked"`.

Only set `execution_contract_required:true` when the selected downstream workflow needs this explicit stride/stop-condition guard. New artifacts must write the canonical snake_case schema shown above under `handoff_artifacts.deep_interview`; runtime readers may accept legacy camelCase field/marker aliases and direct/nested `execution_contract` locations only as compatibility input. If `execution_contract_required` is absent or false, downstream Autopilot compatibility behavior is unchanged.
