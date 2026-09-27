# Loop: TenonAdmin.Workflow M1（能走通一单）

## GOAL

实现 TenonAdmin.Workflow 的 M1（能走通一单），严格按 `docs/workflow/workflow-design-plan-2026-08-17.md` §八 M1 行执行，不扩范围。

## DONE-CONDITION

- `dotnet build backend/TenonAdmin.slnx -c Release` 全绿
- `dotnet test backend/TenonAdmin.slnx` 全绿
- `cd web && npm run typecheck && npm run lint` 全绿
- 存在一条「请假流程发起→审批→完结」的端到端集成测试且通过
- 本账本 `## Tasks` 全部打勾

## Status

- 轮次: 15
- 状态: DONE
- 上一轮: 全量验证：Release build + 全量 test + web typecheck/lint 全绿
- 下一步: （无，M1 完成）

## Tasks

> 源自设计方案 §八 M1 行；宁少勿多，不含 M2+（分支/会签/超时/退回/SignalR/动态表单/React port 等）。

### 后端

- [x] 新建 TenonAdmin.Workflow 包骨架并接入 slnx / AddTenonAdmin（TryAdd/virtual/CodeFirst 接线）
- [x] 落地 9 张 `wf_*` 实体 + CodeFirst 建表
- [x] JSON schema v1 模型（含 `formSchema` / `formPerms` 预留）
- [x] `SysOrg` 增加 `LeaderUserId`
- [x] `IApproverProvider` SPI + 内置 8 种 Provider
- [x] 引擎核心：token 串行推进 + 历史事件流
- [x] 审批动词：同意 / 拒绝 / 转办
- [x] 定义 CRUD + 发布 / 版本 API
- [x] 发起 / 待办 / 已办 / 详情 API + `IWorkflowFormBinder` 挂载点
- [x] `WorkflowReplaceabilityTests` 六件套

### 前端（仅 Vue）

- [x] 设计器 MVP（串行链：审批 + 抄送节点）+ 配置抽屉
- [x] 发起页 / 待办列表 / 详情页（含表单挂载点）

### 验收

- [x] 请假流程端到端集成测试（发起→审批→完结）并通过
- [x] 全量验证：Release build + 全量 test + web typecheck/lint 全绿

## Log

### Round 1 — 2026-08-17

- 动作: 账本不存在，按 §八 M1 行推导最小 Tasks（14 项）并创建 `.loop/wf-m1.md`
- 结果: 账本已就绪；尚未写任何实现代码
- 下一轮: 新建 TenonAdmin.Workflow 包骨架并接入 slnx / AddTenonAdmin（TryAdd）

### Round 2 — 2026-08-17

- 动作: 新建 `backend/src/TenonAdmin.Workflow`（csproj 引 AspNetCore、Abstracted SPI/`WorkflowOptions`、`WorkflowSetup.AddTenonAdminWorkflow`+`UseWorkflow`）；纳入 `TenonAdmin.slnx`；MinimalHost 接线；元包不引本包
- 结果: `dotnet build backend/TenonAdmin.slnx -c Release` 绿（含 TenonAdmin.Workflow / MinimalHost）
- 下一轮: 落地 9 张 `wf_*` 实体 + CodeFirst 建表

### Round 3 — 2026-08-17

- 动作: 在 `TenonAdmin.Workflow/Entities/` 落地 9 表（`WfDefinition`/`WfDefinitionVersion`/`WfInstance`/`WfToken`/`WfTask`/`WfTaskActor`/`WfHisTask`/`WfHistory`/`WfCc`）+ `WfEnums`；DataEntity×2 / BaseEntity×7；`FormSchema` 列预留在 version；CodeFirst 仍走既有 `UseWorkflow`→`ApplicationAssemblies` 扫描，未改引擎/API
- 结果: `dotnet build backend/TenonAdmin.slnx -c Release` 绿（0 警告 0 错误）
- 下一轮: JSON schema v1 模型（含 `formSchema` / `formPerms` 预留）

### Round 4 — 2026-08-17

- 动作: 在 `TenonAdmin.Workflow/Schema/` 落地 JSON schema v1：`WfModel`+`WfModelJson`、`WfNode`/`WfNodeProps`/`WfAssignee`/`WfTimeout`/`WfBranchArm`/`WfConditionExpr`、`WfFormSchema`/`WfFormField`/`WfFormFieldPerm`、`WfSchemaEnums`；定义级 `formSchema` + 节点级 `formPerms` 预留；未动引擎/API/前端
- 结果: `dotnet build backend/TenonAdmin.slnx -c Release` 绿（0 警告 0 错误）；本地序列化 smoke 对拍 `type/mode/formPerms` 与草案一致
- 下一轮: `SysOrg` 增加 `LeaderUserId`

### Round 5 — 2026-08-17

- 动作: `SysOrg` 新增可空 `LeaderUserId`（软引用 `sys_user`，对齐 `DirectorId`）；`OrgInput` + `OrgService` Add/Update/Copy 接线；种子未改（可空默认 null）；未动 Provider/引擎
- 结果: `dotnet build backend/TenonAdmin.slnx -c Release` 绿（0 错误；既有警告未新增）
- 下一轮: `IApproverProvider` SPI + 内置 8 种 Provider

### Round 6 — 2026-08-17

- 动作: 补齐 `IApproverProvider`（`ResolveAsync` + `ApproverResolveContext`）+ `IApproverResolver`/`DefaultApproverResolver`；内置 8 键 `user|leader|multiLeader|role|position|selfSelect|initiator|orgLeader`；`WorkflowSetup` `TryAddEnumerable`×8 + `TryAdd` Resolver；未动引擎/API/前端
- 结果: `dotnet build backend/TenonAdmin.slnx -c Release` 绿（0 警告 0 错误）
- 下一轮: 引擎核心：token 串行推进 + 历史事件流

### Round 7 — 2026-08-17

- 动作: 落地 `Engine/`——`WorkflowEngine.ExecuteAsync`(一 Cmd 一事务)+ `WfAgenda` + `EnterNodeOp`/`TakeTransitionOp`/`CompleteTaskOp`；`StartInstanceCmd`/`CompleteTaskCmd`；每步 append `wf_history`；串行 start→approval|cc→完结；空审批人三级策略；`NoOpWorkflowFormBinder` + `WorkflowErrorCode` 48xxx；`TryAdd` 注册；未做 HTTP API / 转办服务 / 前端
- 结果: `dotnet build backend/TenonAdmin.slnx -c Release` 绿（0 警告 0 错误）
- 下一轮: 审批动词：同意 / 拒绝 / 转办

### Round 8 — 2026-08-17

- 动作: 补齐审批动词层——`TransferTaskCmd`+`TransferTaskOp`(CAS 跳过原 actor、写 `wf_his_task` Transfer、挂目标 Pending、不推进 token)；`IWfTaskService`/`WfTaskService` 三动词(`Approve`/`Reject`→`CompleteTaskCmd`，`Transfer`→转办 Cmd)；错误码 `48010`；`TryAdd` 注册；未做 HTTP/前端
- 结果: `dotnet build backend/TenonAdmin.slnx -c Release` 绿（0 警告 0 错误）
- 下一轮: 定义 CRUD + 发布 / 版本 API

### Round 9 — 2026-08-17

- 动作: 落地定义 API——`IWfDefinitionService`/`WfDefinitionService`（草稿 Version=0、发布快照≥1、停用）；`WfDefinitionController`（§七：`add|update|publish|disable` + `page|{id}|versions/{id}`，全 `[RolePermission]`，写操作 `[OperationLog]`）；错误码 `48011–48013`；`TryAdd` 注册；未做运行态实例/待办 API、前端
- 结果: `dotnet build backend/TenonAdmin.slnx -c Release` 绿（0 警告 0 错误）
- 下一轮: 发起 / 待办 / 已办 / 详情 API + `IWorkflowFormBinder` 挂载点

### Round 10 — 2026-08-17

- 动作: 运行态 API——`IWfInstanceService`/`WfInstanceController`（`start|page|{id}|history/{id}`，全 `[ActiveSession]`）；`IWfTaskService` 扩待办/已办 + `WfTaskController`（`todo|done|approve|reject|transfer`）；详情透出 `FormComponent`；引擎既有 `ValidateOnStart`/`OnInstanceCompleted` 保持；`TryAdd` 注册 `IWfInstanceService`；错误码 `48014`；未做前端/种子/E2E/撤销/抄送列表
- 结果: `dotnet build backend/TenonAdmin.slnx -c Release` 绿（0 警告 0 错误）
- 下一轮: `WorkflowReplaceabilityTests` 六件套

### Round 11 — 2026-08-17

- 动作: 新增 `WorkflowReplaceabilityTests` 六件套——锁 `AddTenonAdminWorkflow` 六个 `TryAddScoped`（`IApproverResolver`/`IWorkflowFormBinder`/`IWorkflowEngine`/`IWfDefinitionService`/`IWfTaskService`/`IWfInstanceService`）；前置注册即胜出（裸容器，对齐 QA29）；Tests.csproj 引用 Workflow；未做前端/种子/E2E
- 结果: `WorkflowReplaceabilityTests` 6/6 绿；`dotnet build … -c Release` 绿（0 警告 0 错误）
- 下一轮: 设计器 MVP（串行链：审批 + 抄送节点）+ 配置抽屉

### Round 12 — 2026-08-17

- 动作: Vue 设计器 MVP——框架无关 `web/src/workflow/{schema,model}.ts` + Vitest；`api/workflow.ts` / `types/workflow.ts`；`views/workflow/definition/designer.vue` + `WfNodeTree`/`WfAddNode`/`WfNodeCard`/`WfConfigDrawer`（串行审批+抄送、默认可见≤5、高级折叠）；zh/en i18n；顺带 schema 枚举挂 `CamelCaseEnumConverter` 使 API 往返与 ModelJson 同形；未做定义列表/发起/待办/详情/React
- 结果: `npm run typecheck` 绿；`model.spec` 4/4 绿；`TenonAdmin.Workflow` Release build 绿
- 下一轮: 发起页 / 待办列表 / 详情页（含表单挂载点）

### Round 13 — 2026-08-17

- 动作: Vue 运行态三页——`workflow/start/index`（选已发布定义+业务键/摘要 JSON/自选人+`WfFormMount`）；`workflow/todo/index`（ProTable 待办→详情）；`workflow/instance/detail`（约定式路由、时间线、同意/拒绝/转办、`formComponent` 动态挂载）；扩 `api/workflow`+types；zh/en 运行态键 + `error.code.48xxx`；未做请假种子/E2E/React/定义列表
- 结果: `npm run typecheck` 绿
- 下一轮: 请假流程端到端集成测试（发起→审批→完结）并通过

### Round 14 — 2026-08-17

- 动作: TestHost 挂 `AddTenonAdminWorkflow`+`UseWorkflow`；新增 `LeaveWorkflowE2ETests`——fixture 内建「请假审批」(start→approval/`leader`)，员工发起→主管同意→Approved；断言待办/已办/详情意见/事件流完结；未做菜单种子/全量验收
- 结果: `LeaveWorkflowE2ETests` 1/1 绿
- 下一轮: 全量验证：Release build + 全量 test + web typecheck/lint 全绿

### Round 15 — 2026-08-17

- 动作: 全量验证——`dotnet build … -c Release`；`dotnet test …`；`cd web && npm run typecheck && npm run lint`；lint 卡在 `WfConfigDrawer.vue` 无用 `?? {}` spread，去掉后重跑 web 全绿
- 结果: Release build 0 警告 0 错误；tests 701/701 绿；web typecheck+lint 绿；Tasks 14/14 全勾 → Status DONE
- 下一轮: （无，M1 DONE）
