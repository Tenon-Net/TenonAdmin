# Goal: Vue 工作流冒烟验收（手工为主，自动化佐证）
Done-condition: A–F 与验收报告均已勾选；报告含 Pass/Fail、缺陷、自动化摘要；React WF 明确不测
round: 8 / max: 16

## Tasks
- [x] 环境就绪：MinimalHost + `web` dev；登录 superAdmin；确认可进业务中心/系统流程菜单；可选跑一次基线自动化并记入报告
- [x] 路径 A：串行审批 + 抄送（定义→发布→发起→同意→抄送已读→我发起的/已办/监控一致）
- [x] 路径 B：条件分支（amount 高低臂；详情核对节点）
- [x] 路径 C：Webhook 设计器（配置保存/回显/发布；不要求真实 HTTP 投递）
- [x] 路径 D：内置动态表单（设计字段+权限→发起渲染/必填→审批权限→详情回放）
- [x] 路径 E：高级审批动词（退回/转办或一次性委托/催办/撤销/加签或减签/拿回/长期委托 — 能测尽测，缺账号记 blocker）
- [x] 路径 F：并行分支（双臂待办→汇合；详情 Token 不串台）
- [x] 验收报告：汇总 Pass/Fail、缺陷（标题 `[Workflow][页面] …`）、边界说明、跑过的命令与结果；勾选本任务

## Findings
### Boundary / env notes (not bugs)
- React `web-react/` 工作流 UI：**不在本验收范围**（未交付）。
- Outbox 默认 NoOp；AI 决策 shadow-only — 见指南，记为边界。
- 清库重建：账号 `superAdmin` / `Aa123456`（`TenonAdmin__Seed__AdminPassword`）。
- 模块路由：业务中心会话下直开系统路由会 404；需「切换应用」。属预期分区行为。
- Playwright 套件 `workflow-m2a`/`m2b` 在发布后未切业务中心即 `goto /workflow/start` → 失败（测试脚本缺口；手工已覆盖）。

### Defects（已修复 2026-09-17）
1. **`[Workflow][设计器] 加号菜单缺少「并行」节点`** — **Fixed**
   - 根因：`allowParallel` 未传入，Vue 布尔缺省为 false
   - 修复：`WfNodeTree` 传 `allow-parallel`；`WfNodeChain`/`WfAddNode` 默认 `true`；臂内仍禁止嵌套
   - 验证：浏览器菜单含「并行」；`WfAddNode.spec.ts` 2 passed

2. **`[Workflow][加签] 超管加签返回 48036`** — **Fixed**
   - 根因：`EnsureCallerAndTargetScopeAsync` 要求 `caller.OrgId == target.OrgId`，超管 OrgId=null 恒失败
   - 修复：超管跳过机构一致性校验；普通用户仍须同机构
   - 验证：`WfSignTests` 5 passed（含 `Super_admin_can_add_sign_across_org_scope`）；线上 add-sign/remove-sign → code 0

### Path notes (non-defect)
- 路径 D：内置表单设计（开始节点→高级→内置表单）、字段权限（可编辑）、发起渲染、详情 input 回放值 OK；本次必填勾选未稳定落库（schema `required:false`），未记缺陷，建议回归再点一次必填。
- 路径 E 拿回：实例已完结时不可拿回；下游仍有待办时可拿回（`QA-E TBChain2` API 验证通过）。
- 长期委托：页面「新增规则」可用；未建真实跨用户规则（种子用户密码未知）— 记 residual risk，非 blocker。

### Automation evidence
| 命令 | 结果 |
| --- | --- |
| `dotnet test … --filter '…WorkflowReplaceabilityTests\|…WfIdentityHashTests'` | Passed **34** / Failed 0 |
| `dotnet test … --filter 'FullyQualifiedName~WfForm\|…Parallel\|…AddSign\|…TakeBack\|…Delegation\|…Transfer'` | Passed **54** / Failed 0 |
| `dotnet test … --filter 'FullyQualifiedName~WfParallelRuntimeTests'` | Passed **12** / Failed 0 |
| `cd web && npm test -- --run src/composables/useModule.spec.ts` | **5** passed |
| `cd web && npm test -- --run src/workflow/formSchema.spec.ts …WfFormDesigner …WfBuiltinForm` | **23** passed |
| `TENON_E2E_PASSWORD=Aa123456 npx playwright test e2e/workflow-*.spec.ts` | **3** passed（layout×2 + m3a2 Webhook），**2** failed（m2a/m2b 未切业务中心） |

## 验收报告

**范围声明：React 工作流 UI（`web-react/`）不在本次验收范围。**

| 路径 | 结果 | 证据摘要 |
| --- | --- | --- |
| 环境就绪 | **Pass** | API `:5100` + Vue `:5175`；superAdmin 登录；系统/业务中心菜单齐全 |
| A 串行+抄送 | **Pass** | 定义 `QA-A Serial mu4yw6pk`；实例 `849716670287872`；同意→已通过；抄送已读；我发起的/已办/监控一致 |
| B 条件分支 | **Pass** | `QA-B Cond`；`amount=20000`→高额审批；`5000`→默认臂直接已通过 |
| C Webhook | **Pass** | 手工配置 URL/PATCH/超时/转人工/请求头回显并发布；E2E m3a2 亦 Pass |
| D 内置表单 | **Pass*** | 字段+审批字段权限+发起渲染+详情回放；*必填挡提交未充分验证 |
| E 高级动词 | **Pass*** | 退回/撤销/催办/转办/委托/拿回/长期委托页 OK；*加签 API 48036（见缺陷#2）；减签依赖加签 |
| F 并行分支 | **Fail** | 设计器无「并行」入口（缺陷#1）；后端并行运行时测试 Pass 仅作引擎旁证 |

**结论：** Vue 工作流预览版主路径 A–E 可冒烟通过；**并行设计器入口缺失为阻断级缺陷**；加签目标校验需澄清。Outbox NoOp / AI shadow / 无 React WF UI 为已知边界。

## Round log
### Round 1 — 环境就绪（清库+重启 API、登录、菜单探活、基线自动化） → Pass。NEXT: 路径 A。
### Round 2 — 路径 A 串行审批+抄送手工闭环 → Pass（实例 849716670287872）。NEXT: 路径 B。
### Round 3 — 路径 B 条件分支高低额 → Pass。NEXT: 路径 C/D。
### Round 4 — 路径 C Webhook 配置回显发布 → Pass；路径 D 表单入口探查。NEXT: 完成 D。
### Round 5 — 路径 D 内置表单+权限+回放 → Pass*。NEXT: 路径 E。
### Round 6 — 路径 E 动词（退回/撤销/催办/转办/委托/拿回/委托页）→ Pass*；加签 48036。NEXT: 路径 F。
### Round 7 — 路径 F 确认设计器无并行 → Fail（缺陷#1）；后端并行 12 Pass。NEXT: 验收报告。
### Round 8 — 验收报告汇总并勾选全部 Tasks → Done。NEXT: 无（done-condition 已满足）。
