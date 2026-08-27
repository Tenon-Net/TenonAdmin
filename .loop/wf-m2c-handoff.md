# TenonAdmin Workflow M2c 交接摘要

> 给接续 agent 的入口。权威台账是 [wf-m2c.md](./wf-m2c.md);本文件含可复制提示词与纪律摘要。

## 当前结论

- **M2a / M2b 已完成**。M2b 在 `origin/dev` 顶 `bffec77`(指定过滤器 **190/190**)。
- **M2c 未开始**:Tasks 1–10 全未勾;台账轮次 0;下一步 **Round 1 — Task 1 plan only**。
- **严格执行 loop**:每个 Task 必须 plan → exec → review → 修 Findings → 勾选;**禁止跳过 review、禁止同轮勾选、禁止未跑闸门就勾选**。见台账 `## Loop 纪律`。

## 复制给新 agent 的提示词

```text
在 TenonAdmin 仓库继续 TenonAdmin.Workflow 的 M2c 可靠性收口。

纪律：严格执行 .loop/wf-m2c.md 的 plan→exec→review，不要跳过 review；每个 Task 未独立 review 并 0×P1/0×未修 P2 前不得勾选。

必读顺序：
1. .loop/wf-m2c-handoff.md（本文件）
2. .loop/wf-m2c.md（Status、Loop 纪律、Tasks、DONE-CONDITION）
3. docs/workflow/workflow-design-plan-2026-08-17.md §14.2、§15.1
4. docs/workflow/workflow-database-design-review-2026-08-24.md §五、§九、§十(M2c)
5. CLAUDE.md / backend/CLAUDE.md / web/CLAUDE.md

分支：dev（与 origin/dev 对齐后开工）。工作流包：backend/src/TenonAdmin.Workflow/。
本轮只动 web/；web-react/ 仅最后一 Task 可 gen:api 刷 schema.d.ts。

M2c 范围（10 Tasks）：operation receipt、IdentityHash（一次定死）、RequestId 贯穿 8 个写命令（默认不含 Urge）、CompletedTime、wf_history.RequestId、通知失败日志、四库 WfPersistenceContractTests、Vue request key、双 gen:api。

禁区：不做 M3a/M3b；不重写 M2b Instance/Token CAS；不新增动词/页面；不 port React 工作流页；不抽 web/web-react 共享层；不提交 TestResults/。

闸门：dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow" ≥190 全绿；四库契约套件 CI 四腿绿；web typecheck/lint/vitest 绿；双 schema SHA256 一致。

第一轮动作（plan only，不写产品代码）：
1. git checkout dev && git pull
2. 精读数据库评审 §五 + grep 写命令 DTO/Controller
3. 在 .loop/wf-m2c.md 写 Task 1 的 ## Plan（实体、IdentityHash 规则、快照用例、决策点、陷阱）
4. 更新 Status：轮次=1、当前任务=1、当前阶段=plan、下一步=Round 2 exec
5. 默认不 commit/push，除非用户要求
```

## M2b 已交付(勿重做)

- 动词:退回/撤销/委托/催办/超时 Job/去重/实例 Token Version CAS
- 页面:抄送、我发起、我已办、监控、回放、btnInfo、详情动词
- 验收:e2e `web/e2e/workflow-m2b.spec.ts` + `.loop/wf-ui-shots/m2b-01`…`07`
- 台账:`.loop/wf-m2b.md` Tasks 1–14 全勾

## M2c 起点(零文件,别去找)

- `wf_operation_receipt` / `IdentityHashBuilder` — 不存在
- 命令 DTO 的 `RequestId` — 不存在
- `WfInstance.CompletedTime`、`wf_history.RequestId` — 未加
- `WfPersistenceContractTests` — 不存在

## M2b 已为 M2c 提前落地(勿重写)

- `WfInstance.Version` / `WfToken.Version` + `WfVersionCasTests`
- `WfTimeoutJob` task 级 CAS 领取;提醒路径零 CAS

## 验证命令

```bash
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"
cd web && npm run typecheck && npm run lint && npx vitest run src/workflow/
```

## Git 与接续规则

1. 先 `git status` / `git log -3`,确认在 `dev` 且读过台账 `## Status`。
2. 每轮只推进**一个 Task 的一个阶段**;不要跨 Task 大包提交。
3. Commit message 英文 conventional commits;默认不 push。
4. 不要把 M3、React port、新动词混入 M2c。
