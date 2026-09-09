---
name: visual-ralph
description: "[OMX] Implement or restyle frontend UI against a generated image, static reference, or live URL through measured capture-and-compare iteration."
---

# Visual Ralph

The name is retained for compatibility. Use this skill for frontend work with a visual target; perform the work with ordinary repository editing, capture, comparison, and verification tools.

Repository-wide authority, state, and verification rules live in [`AGENTS.md`](../../../AGENTS.md).

## Workflow

1. Inspect the frontend stack, route, tokens, reusable components, screenshot tooling, and relevant commands.
2. Establish the reference, viewport, route and state, visible interactions, and exclusions. For a live URL, record its source and authorized scope. For a generated concept, use `$imagegen` with the requested dimensions, text, visual constraints, and feasible UI details, then copy the selected artifact into `.omx/artifacts/visual-ralph/<slug>/reference.png`.
3. Treat a user-specified reference, or a reference already authorized for the current task, as approved. Ask only when an unresolved design choice would materially change the result; do not repeat an approval already given.
4. Implement with existing components and tokens. Capture the same viewport and state, compare it with the reference, turn observed differences into the next edit, and repeat until the requested match is reached or a concrete blocker remains.
5. Use image inspection and pixel-diff overlays when they improve comparison. Preserve the reference, final screenshot, reproduction command, and useful diff artifacts.
6. Run the repository's applicable build, lint, tests, and accessibility checks for the changed surface.

## Quality and stopping

Honor an explicit pixel-diff or similarity threshold from the user. Otherwise judge completion from visible layout, typography, color, spacing, states, interactions, and the user's stated fidelity; a model-generated score is supporting judgment, not an objective quality fact.

Finish when the result reproduces at the recorded viewport and state, relevant checks pass, and remaining visible differences are documented. Encode reusable values in the repository's existing token or component system when the implementation actually introduces them.

Task: {{ARGUMENTS}}
