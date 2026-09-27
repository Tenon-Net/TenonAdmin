---
name: ai-slop-cleaner
description: "[OMX] Run an anti-slop cleanup/refactor/deslop workflow"
---

# AI Slop Cleaner Task Card

Use this bounded helper for cleanup/refactor/deslop work, not as a competing top-level
workflow. Shared operating invariants live in [AGENTS.md](../../../AGENTS.md); this card defines
scope, smell taxonomy, passes, and evidence.

## When to use and inputs

Use when working code is bloated, noisy, repetitive, over-abstracted, or AI-generated;
the user requests cleanup/refactor/deslop; or a follow-up left duplicate/dead code,
weak boundaries, missing tests, fallback-like paths, or wrappers. Inputs are the requested
feature/files and behavior to preserve. A file list scope is valid; keep the pass bounded
to it. Inside an implementation workflow, limit cleanup to its changed files unless the user requests a broader scope.

## Before editing

1. **Lock behavior first**: run existing targeted checks; add regression coverage for changed logic when missing, including compatibility/fail-safe paths. For instruction-only edits, check discovery, links, metadata, and workflow decisions.
2. **Create a cleanup plan before code**: list scope and smells, include fallback findings/classifications/escalation, and order safest/highest-signal fixes first.
3. **Inventory fallback-like code** in scope: quick hacks, temporary workaround, temporary fallback, just bypass, just skip, fallback if it fails, swallowed errors, silent defaults, broad compatibility shims, and duplicate alternate execution paths.
4. Classify each fallback: **Masking fallback slop** hides evidence, bypasses the contract, suppresses validation, swallows failures, silently defaults, or adds untested paths; **Grounded compatibility/fail-safe fallback** is narrow at an external/version/fail-safe boundary, documents rationale, preserves failure evidence, and tests primary plus fallback.
5. Prefer root-cause repair, deletion, boundary repair, or explicit failure behavior. For unresolved architectural decisions, report the finding to the caller; use `$ralplan` when consensus planning is requested or required by the active workflow.

## Smell taxonomy and passes

Classify before changing:

- **Fallback-like code**: masking fallbacks, workaround branches, bypasses, swallowed errors, silent defaults, broad shims, alternate paths.
- **Duplication**: repeated logic, copy-paste branches, redundant helpers.
- **Dead code**: unused/unreachable code, stale flags, debug leftovers.
- **Needless abstraction**: pass-through wrappers, speculative indirection, single-use layers.
- **Boundary violations**: hidden coupling, leaky responsibilities, wrong-layer imports/side effects.
- **Missing tests**: behavior not locked or edge cases uncovered.
- **UI/design slop**: context-sensitive signals, not absolute bans; preserve intentional brand, design-system, accessibility, or product-context exceptions. Challenge Korean body text at 11-12px (generally 14px or larger); gratuitous box shadows; repetitive eyebrow + title + description + paragraph stacks and generic emoji badges; default AI blue/purple such as `#3B82F6`; reflexive 3-column or 4-column grids; and extreme gradients unless justified by context.

Resolve relevant fallback findings first, then select the applicable passes, one smell at a time:
**Pass 1: Dead code deletion**; **Pass 2: Duplicate removal**; **Pass 3: Naming/error
handling cleanup**; **Pass 4: Test reinforcement**. Re-run targeted verification after
each pass and avoid unrelated refactors. Prefer deletion/existing utilities; no new
abstractions or dependencies unless explicitly required.

## Evidence/output contract

Report:

```text
AI SLOP CLEANUP REPORT
Scope: [files/feature]
Behavior Lock: [targeted tests added/run]
Cleanup Plan: [bounded smells/order]
Fallback Findings: [finding -> masking fallback slop | grounded compatibility/fail-safe fallback -> escalation]
UI/Design Findings: [none/N/A or signal -> action/defer -> intentional rationale]
Passes Completed: [resolution gate; Passes 1-4]
Quality Gates: Relevant regression, lint/typecheck/build, or instruction checks (PASS/FAIL/N/A)
Changed Files: [path -> simplification]
Remaining Risks: [none or deferred item]
```

Include changed files, simplifications, fallback classifications/escalation status,
tests/diagnostics/build checks run, UI findings when relevant, and deferred risks. Keep
writer/reviewer separation for cleanup plans and approvals.

## Exit condition

Stop when the requested scope has behavior-lock evidence, each selected smell pass is
complete or explicitly deferred with rationale, verification is reported, and no
unrelated files or temporary artifacts remain. Never present an unverified cleanup as
complete; escalate a real architectural blocker rather than masking it.
