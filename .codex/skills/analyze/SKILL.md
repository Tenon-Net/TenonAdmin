---
name: analyze
description: "[OMX] 只读调查跨文件的行为、原因或架构问题，提供源码证据与不确定性。单文件查找无需此流程。"
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

1. Identify the question and the smallest evidence-backed scope.
2. Identify the smallest files, tests, configs, and docs likely to answer it.
3. Read direct code paths and contracts first; trace boundaries only as far as needed.
4. Compare competing explanations, rank them by support, and mark unresolved points.
5. Stop when the question is answered with sufficient evidence, or name the smallest read-only probe that would resolve the remaining uncertainty.

## Evidence discipline

清楚区分以下三类信息；可在相关句子中说明，无需为每条结论添加固定标签：

- **Evidence** — directly shown by code, tests, generated artifacts, configuration, or docs.
- **Inference** — a reasoned conclusion drawn from cited evidence.
- **Unknown** — not settled by the repository evidence.

Prefer direct paths and independent corroboration over contextual clues. Never present guesses as evidence or inference, and never overclaim certainty.

## Output contract

先回答问题，随后提供支持关键结论的文件与行号、推断依据和未解决的不确定性。仅有多个竞争解释时按证据强弱排序；表格和分节按内容需要使用。证据不足时指出最小的下一步调查，不为填充模板而扩大范围。

## Stop conditions

- Do not edit files, run an implementation lane, or make recommendations the evidence cannot support.
- Do not continue searching after the answer and confidence boundary are grounded.
- If evidence is insufficient, report the limit explicitly rather than manufacturing certainty.

Task: {{ARGUMENTS}}
