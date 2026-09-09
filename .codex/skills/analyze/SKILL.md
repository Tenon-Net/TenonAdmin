---
name: analyze
description: "[OMX] Run read-only deep repository analysis and return a ranked synthesis with explicit confidence, concrete file references, and clear evidence-vs-inference boundaries. Use when a user says 'analyze', 'investigate', 'why does', 'what's causing', or needs grounded cross-file explanation before any changes are proposed."
---

# Analyze — Read-Only Deep Analysis

Use `$analyze` to answer a repository question with grounded, read-only evidence. Explain what the code most likely says; do not turn analysis into implementation or generic fix planning.

Shared operating, delegation, state, hook, team, cancellation, and verification invariants live in [AGENTS.md](../../../AGENTS.md). Follow that source instead of duplicating its rules here.

## Use when

- The user needs a causal, architectural, behavioral, impact, or tradeoff explanation.
- The answer requires tracing multiple files or boundaries, or ranking plausible explanations.
- The user needs confidence and concrete evidence before changing anything.

Do not use it for edits, implementation, a new product plan, a simple one-file lookup, or OMX team-runtime operation.

## Inputs and method

1. Restate the question and define the evidence-backed scope.
2. Identify the smallest files, tests, configs, and docs likely to answer it.
3. Read direct code paths and contracts first; trace boundaries only as far as needed.
4. Compare competing explanations, rank them by support, and mark unresolved points.
5. Stop when the question is answered with sufficient evidence, or name the smallest read-only probe that would resolve the remaining uncertainty.

## Evidence discipline

Label every material claim as one of:

- **Evidence** — directly shown by code, tests, generated artifacts, configuration, or docs.
- **Inference** — a reasoned conclusion drawn from cited evidence.
- **Unknown** — not settled by the repository evidence.

Prefer direct paths and independent corroboration over contextual clues. Never present guesses as evidence or inference, and never overclaim certainty.

## Output contract

Answer the asked question first and use this shape:

### Question
Restated question, briefly.

### Ranked synthesis
| Rank | Explanation | Confidence | Basis |
|------|-------------|------------|-------|
| 1 | ... | High / Medium / Low | strongest supporting evidence |
| 2 | ... | High / Medium / Low | why it trails |
| 3 | ... | High / Medium / Low | why it remains possible |

### Evidence
- `path/to/file:line-line` — direct observation.
- `path/to/file:line-line` — corroborating observation.

### Inference
- What the evidence most strongly implies.
- Why weaker alternatives were down-ranked.

### Unknowns / limits
- What the repository does not establish.
- The next discriminating read-only probe, when useful.

## Stop conditions

- Do not edit files, run an implementation lane, or make recommendations the evidence cannot support.
- Do not continue searching after the answer and confidence boundary are grounded.
- If evidence is insufficient, report the limit explicitly rather than manufacturing certainty.

Task: {{ARGUMENTS}}
