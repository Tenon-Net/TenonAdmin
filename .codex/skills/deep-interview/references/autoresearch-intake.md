# autoresearch intake

### Autoresearch specialization

When the clarified task is specifically about `$autoresearch`, or the skill is invoked with `--autoresearch`, keep the interview domain-specific and emit skill-consumable artifacts without skipping clarification.

- **Accepted seed inputs:** `topic`, `evaluator`, `keep-policy`, `slug`, existing mission draft text, and prior evaluator examples/templates
- **Required interview focus:** mission clarity, evaluator readiness, keep policy, slug/session naming, and whether the draft is ready to launch now or should refine further
- **Canonical artifact path:** `.omx/specs/deep-interview-autoresearch-{slug}.md`
- **Launch artifact bundle:** `.omx/specs/autoresearch-{slug}/mission.md`, `.omx/specs/autoresearch-{slug}/sandbox.md`, and `.omx/specs/autoresearch-{slug}/result.json`
- **Launch artifact directory:** `.omx/specs/autoresearch-{slug}/`
- **Required artifact sections:**
  - `Mission Draft`
  - `Evaluator Draft`
  - `Launch Readiness`
  - `Seed Inputs`
  - `Confirmation Bridge`
- **Required launch artifacts under `.omx/specs/autoresearch-{slug}/`:**
  - `mission.md`
  - `sandbox.md`
  - `result.json`
- **Launch-readiness rule:** mark the draft as **not launch-ready** while the evaluator command still contains placeholder markers such as `<...>`, `TODO`, `TBD`, `REPLACE_ME`, `CHANGEME`, or `your-command-here`
- **Structured result contract:** `result.json` should point to the draft + mission/sandbox artifacts and carry the finalized `topic`, `evaluatorCommand`, `keepPolicy`, `slug`, `launchReady`, and `blockedReasons` fields so `$autoresearch` can consume it directly
- **Confirmation bridge:** after artifact generation, offer at least `refine further` and `launch`; do not run direct CLI launch or detached/split tmux launch, and only hand off to `$autoresearch` after explicit confirmation
- **Handoff rule:** downstream execution must preserve the clarified mission intent, evaluator expectations, decision boundaries, and launch-readiness status from this artifact rather than bypassing the draft review step
