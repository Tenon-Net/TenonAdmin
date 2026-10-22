# AGENTS.md

本文件是 Codex 与 Claude Code 共用的仓库指令唯一来源，包含产品契约、任务参考和工作规则。`CLAUDE.md` 仅作为 Claude Code 的导入入口；修改共同规则时只编辑本文件。

## Product contracts

TenonAdmin (榫卯) is a distributable .NET 10 admin kernel shipped as NuGet packages. Consumers replace built-in services without forking.

- Preserve replaceability: interface-backed services, overridable `virtual` steps, and `TryAdd*` registration. Consumer registrations before `AddTenonAdmin()` win. The `ReplaceabilityTests` contracts also protect `ApplicationAssemblies` entity/controller discovery.
- Runtime dependencies in core packages are SqlSugarCore and Microsoft.* only. Manage package versions in `backend/Directory.Packages.props`.
- Layering: Core contracts ← SqlSugar data access ← Services domain/entities ← AspNetCore host integration; the TenonAdmin meta-package references AspNetCore. Core has no SqlSugar/ASP.NET dependency.
- `web/` (Vue/Naive UI) and `web-react/` (React/Ant Design) are independent, self-contained templates against the same API. Their duplication is intentional; no cross-template imports, shared layer, or requirement to bundle both. Work on the requested template; backend contract changes require checking both consumers.
- Permissions are normalized routes (`VERB:/api/...`); business errors use numeric `ErrorCode`, localized by the frontend. Preserve session revocation, data-scope filters, and automatic audit fields.
- Generate each frontend's `src/api/schema.d.ts` from OpenAPI; do not hand-edit it.
- Comments and documentation are Chinese; commits use English conventional commits (`type(scope): subject`).

## Task context

按当前任务需要的契约、模式或验证方式查阅下表中的相关章节；可复用本轮已读且未变化的上下文。局部文案或格式修改只需目标文件及其适用约束。

| Task | 按需参考 |
| --- | --- |
| Backend services, DI, auth, database, bootstrap | [Repository reference: backend](docs/agents/repository-reference.md#backend-architecture) and relevant tests/callers |
| Vue 开发、调试、测试与审查 | 先按下方「Vue 开发规范」加载技能，再查阅 [web/COMPONENTS.md](web/COMPONENTS.md)、[web/DESIGN.md](web/DESIGN.md) 和 [Vue architecture](docs/agents/repository-reference.md#frontend-architecture-web) |
| React pages/components | [web-react/COMPONENTS.md](web-react/COMPONENTS.md), [React architecture](docs/agents/repository-reference.md#frontend-architecture-web-react); use stable zustand selectors and Ant Design 6 APIs |
| CI, database test performance, template packaging | [Repository reference: CI](docs/agents/repository-reference.md#ci); preserve multi-replica smoke coverage and nightly full SQL Server coverage |
| New module, entity, CRUD, jobs, import/export, service replacement | Select the recipe in [skills/README.md](skills/README.md); complete modules start at [new-module](skills/new-module.md) |
| `site/` pages or root `README*.md`: writing, editing, translation | [skills/write-docs.md](skills/write-docs.md): voice, scope, README presentation, translation and validation |
| Release/version/tag/NuGet | [skills/tenon-release.md](skills/tenon-release.md); human runbook: [docs/releasing.md](docs/releasing.md) |
| Domain terminology or architecture decisions | [docs/agents/domain.md](docs/agents/domain.md); existing `CONTEXT.md` and relevant ADRs |
| GitHub issues/PRDs or triage | [issue tracker](docs/agents/issue-tracker.md), [label mapping](docs/agents/triage-labels.md) |

## Vue 开发规范

`web/` 采用已安装的 [vuejs-ai/skills](https://github.com/vuejs-ai/skills) 作为 Vue 技术规范。所有 Vue 开发、调试、测试与代码审查任务，先读取并遵循 [vue-best-practices](.agents/skills/vue-best-practices/SKILL.md) 及其要求的核心参考文档，再按任务加载专项技能：

| 涉及内容 | 专项技能 |
| --- | --- |
| Vue Router、导航守卫、路由生命周期 | [vue-router-best-practices](.agents/skills/vue-router-best-practices/SKILL.md) |
| Pinia、Store、共享状态 | [vue-pinia-best-practices](.agents/skills/vue-pinia-best-practices/SKILL.md) |
| Vue 组件测试、E2E 测试 | [vue-testing-best-practices](.agents/skills/vue-testing-best-practices/SKILL.md) |
| Vue 运行时错误、警告、异步异常 | [vue-debug-guides](.agents/skills/vue-debug-guides/SKILL.md) |
| 可复用、支持响应式输入的 composable | [create-adaptable-composable](.agents/skills/create-adaptable-composable/SKILL.md) |
| Vue JSX | [vue-jsx-best-practices](.agents/skills/vue-jsx-best-practices/SKILL.md) |
| 明确要求使用 Options API | [vue-options-api-best-practices](.agents/skills/vue-options-api-best-practices/SKILL.md) |

Vue 通用写法、响应式、组件边界和组合函数设计以这些技能为准；项目现有示例与其不一致时，按技能调整当前任务的实现。TenonAdmin 的产品契约、API/权限/i18n、Naive UI 组件接入与设计系统仍按本仓库约定执行。规范适用于新增和修改的 Vue 代码；全量迁移现有代码作为独立任务。`web-react/` 继续使用 React 规范。

技能正文与参考文档以 `.agents/skills/` 中已安装的版本为准，直接引用，避免复制维护；Claude Code 通过 `.claude/skills/` 对应入口使用同一份内容。

## Validation commands

Use the checks relevant to changed behavior. The solution is `.slnx`, not `.sln`.

```bash
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~<ChangedTestClass>"
dotnet run --project backend/samples/MinimalHost
```

Each frontend owns its scripts: run `npm run lint`, `npm run typecheck`, `npm run build`, or `npm run gen:api` in the affected template. Check its `package.json` for targeted test commands. Run `node scripts/check-contract-drift.mjs` for OpenAPI changes; it starts its own host and regenerates both schemas, so preserve pre-existing generated-file edits.

For local database/environment setup and the full test matrix, read [reference commands](docs/agents/repository-reference.md#commands). Required CI checks come from the actual workflows/PR, not a fixed check count.

## 工具与运行时适用范围

产品契约、任务参考和通用执行规则适用于所有代理。下方 OMX 技能路由、`.codex/` 配置、`agent_type` 和持久状态协议仅在支持相应能力的 Codex/OMX 宿主中适用。Claude Code 使用自身可用工具与 `.claude/skills/` 项目技能入口；OMX 的宿主支持和权限检查仍须满足，不能以其他工具替代权限证明。

<!-- CODEGRAPH_START -->
## Code lookup

When `.codegraph/` exists, use `codegraph_explore` or `codegraph explore "<symbol or question>"` before text search to locate or understand code. If it cannot cover the requested material or is unavailable, use `rg` and targeted reads. Do not create an index unless requested.
<!-- CODEGRAPH_END -->

<!-- OMX:AGENTS:START -->
<!-- AUTONOMY DIRECTIVE — DO NOT REMOVE -->
Complete authorized work autonomously through implementation and verification. Resolve routine details from context; ask only when missing information materially changes the result or an action needs authority the user has not provided.
<!-- END AUTONOMY DIRECTIVE -->
<!-- omx:generated:agents-md -->

## Execution

<!-- OMX:GUIDANCE:OPERATING:START -->
- Default workflow: understand → execute → verify → report. A request to build or fix includes doing the work; an analysis or review request stays read-only unless changes are also requested.
- User instructions take precedence over skill guidelines, within system/developer and runtime permission boundaries. Preserve existing authorization and unrelated worktree changes.
- Give a short initial update and meaningful progress updates. Lead the final answer with the result, relevant validation, and remaining gaps; use concise prose and formatting only where useful.
- Prepare all authorized, reversible work before a required approval. If a skill blocks progress, link the exact instruction, explain why it applies, and finish independent work first.
- Incorporate newer user messages into the active task, preserving completed work and earlier non-conflicting requirements.
- Prefer existing code, native features, and installed dependencies. Add dependencies only when requested. For cleanup, identify scope and behavior first; preserve regression coverage for changed logic.
<!-- OMX:GUIDANCE:OPERATING:END -->

## Skills and workflows

Load the selected `SKILL.md` before applying it, then only the references needed for the current branch. Project recipes are indexed in [skills/README.md](skills/README.md); OMX skills live in `.codex/skills/`. Role TOMLs are not skills.

Explicit `$name` invocations run left-to-right and override implicit routing. Hook-injected routing is authoritative for the turn; otherwise match the user's intent to skill descriptions. Mentioning a workflow while discussing or editing it is not an instruction to execute that workflow.

| Request | Workflow |
| --- | --- |
| Clear implementation or focused fix | Execute directly |
| Lightweight plan or plan review | `$plan` |
| 单个会改变结果的缺失信息 | 提出一个聚焦问题，继续不依赖答案的工作 |
| 需要系统梳理目标、范围和取舍，或明确要求深度访谈 | `$deep-interview` |
| Architecture consensus | `$ralplan` |
| Explicit hands-off orchestration | `$autopilot`: `$deep-interview → $ralplan → $ultragoal` |
| Durable multi-goal execution | `$ultragoal` |
| Approved coordinated parallel implementation | `$team` |
| Read-only investigation | `$analyze` |

Standalone stages may reuse inputs that meet their contract. Ordinary work does not silently become a persistent mode. `$ralph`, `$ultrawork`, `$pipeline`, `ecomode`, and `swarm` are retired entrypoints; retain legacy state handling only for recovery/cancellation.

In Codex App outside attached tmux, `omx team`, `omx hud`, and `omx question` require the OMX CLI runtime or a supported bridge. Use native tools for ordinary work and structured questions when supported; otherwise ask one concise question. For an explicitly requested runtime workflow, load its skill and check its documented support before adapting; an unsupported authority check is not permission to bypass its gates.

## Delegation

<!-- OMX:GUIDANCE:SPECIALIST-ROUTING:START -->
Delegate bounded, independent work when it improves throughput or confidence; continue useful local work in parallel. Use the installed `agent_type`: `explore` for repository facts, `researcher` for official references, `dependency-expert` for package decisions, `executor` for implementation, and the relevant installed review/debug role for specialist checks.
- Give each child a concrete deliverable and scope; assign disjoint files for writes. Children preserve other edits and report evidence, blockers, and scope changes to the leader.
- The leader owns mode selection, integration, final verification, and stop decisions. Children recommend handoffs rather than recursively orchestrating.
- Use the host's actual concurrency limit. Reserve `worker` for a Team runtime assignment.
- In Conductor workflows, native children are read-only/advice-only, and reporting requires separate host-authenticated caller, parent, and target proof. Without that proof, reporting and product writes remain denied; implementation requires Team's separate authority checks.
- If native role routing reports `role_routing_unavailable` during adapted Ralplan, run `omx ralplan preflight --json`; stop on `unsupported_documented_leader_proof`. Prompt labels, local state, and session metadata cannot substitute for host authority.
<!-- OMX:GUIDANCE:SPECIALIST-ROUTING:END -->

<!-- OMX:MODELS:START -->
Model and reasoning settings belong in `.codex/config.toml` and `.codex/agents/*.toml`, not a duplicated capability table. Preserve the selected model and specialist cost/quality roles; increase reasoning only when task complexity or failed validation justifies it.
<!-- OMX:MODELS:END -->

## Verification

<!-- OMX:GUIDANCE:VERIFYSEQ:START -->
Define what proves the requested result, run the smallest relevant checks, and read their output. For changed logic, use targeted regression tests plus applicable lint/typecheck/build checks. For instructions, validate discovery, links, metadata, and consequential workflow decisions; do not add tests that only mirror wording. Run mandated checks, but broaden or repeat only for new changes, failures, or unresolved risks. Report unavailable validation explicitly and stop when the requested work is verified.
<!-- OMX:GUIDANCE:VERIFYSEQ:END -->

## Durable Runtime Invariants (canonical SSOT)

This section is the single source of truth for durable state ownership, hook boundaries, cancellation, and Team coordination. Skills and role prompts reference it; they must not restate or weaken these rules.

### State and hook ownership

- Durable state is authoritative only in the current, proven session or Team scope. Compatibility discovery is read-only and never grants write authority.
- Hooks own normal skill activation and workflow-state persistence under `.omx/state/`; skills do not duplicate or mutate hook-owned state except through documented recovery paths.
- Native hook payloads, prompt labels, task text, cwd, environment, pointers, transcripts, markers, and local trackers are routing or diagnostic data, not ownership or write authority.
- The Team state files and `omx team api ... --json` are the source of truth for task lifecycle and mailbox coordination.

### Cancellation boundary

- Cancellation parses and validates arguments before mutation, resolves one exact writable scope, freezes and revalidates target identity, mutates only proven targets, and leaves unrelated sessions, legacy roots, Team artifacts, and tmux sessions untouched.
- Ralph cancellation must satisfy its documented terminal post-conditions in the same scope; linked modes are handled only when the link is proven.
- `--force` does not widen cancellation scope; it only removes the selected exact-session native-stop entry after the same authority checks. `--all` is unsupported.
- Team cancellation requires exact frozen Team root, internal name, session, leader pane, and runtime identity. It fails closed when that proof is unavailable or changes; it must not enumerate or broadly kill Team sessions or recursively delete unrelated Team state.

### Team protocol

- Team runtime is explicit and outside the default workflow. Ultragoal does not auto-launch Team, and ordinary workflows do not silently become Team runs.
- Workers ACK startup, claim before work, transition task status through the lifecycle API, use release only for rollback, and report verification evidence. Leaders own integration, final verification, and shutdown decisions.
- Prefer durable state writes and `omx team api ... --json` dispatch. Direct `tmux send-keys` is fallback-only, never primary dispatch; manual pane actions require prior state/evidence checks.
- Team shutdown waits for terminal task state and uses exact Team authority. It does not shut down active work unless explicitly aborting.

### Ultragoal ownership

- `.omx/ultragoal/goals.json` is the leader-owned plan and `.omx/ultragoal/ledger.jsonl` is its durable audit trail. Workers report task evidence only; they do not create worker ledgers, mutate Ultragoal artifacts, or checkpoint goals.
- Shell commands and hooks do not mutate hidden Codex goal state. The active agent uses `get_goal`, `create_goal`, and `update_goal` only at the documented gates, then checkpoints with a fresh `get_goal` snapshot.


## Runtime maintenance

Load `$cancel` when a proven active mode completes, is cancelled, or reaches a hard blocker; never clear unrelated state. Use normal repository tools for ordinary work; `omx sparkshell --tmux-pane` is an explicit operator aid.

For manual Team pane handoff, freeze and verify the target; load a fresh named tmux buffer, verify its contents, clear the composer with `C-u`, paste with bracketed paste, then recapture to verify acceptance. A failed buffer or authority check blocks the handoff.

Preserve runtime overlay markers (`<!-- OMX:RUNTIME:START -->` / `<!-- OMX:RUNTIME:END -->`, `<!-- OMX:TEAM:WORKER:START -->` / `<!-- OMX:TEAM:WORKER:END -->`). `omx setup` can regenerate managed blocks and installed skills/prompts; run it for installation work, then review its diff against this repository's customizations. Use `omx doctor` for installation diagnostics.
<!-- OMX:AGENTS:END -->
