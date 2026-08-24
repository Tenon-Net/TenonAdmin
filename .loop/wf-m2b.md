# Loop: TenonAdmin.Workflow M2b 动词与时效

## GOAL

在 M2a(已收口,四项 CI 全绿,commit `b0d796e`/`a2bd9c6`/`dce7e75`)基础上做 **M2b**:退回/撤销/委托/催办/超时 Job/同一人相邻节点去重/`IWorkflowNotifier` 落地(接 `IRealtimePublisher`)/`btnInfo`;前端(仅 `web/`)加抄送列表、我发起的/我已办的、流程图回放、实例列表按参与筛选。范围与定案见 `docs/review/workflow-design-plan-2026-08-17.md` **§十三 13.3**「M2b 动词与时效」行,语义默认值见 `CONTEXT.md` 「工作流」节「行为语义默认值」条。

**禁止做 M3**(动态表单/`formPerms`/并行/webhook/加减签/比例票签/长期委托/React port)。**不改 `web-react/`**,除了最后一个任务的 `gen:api` 刷 `schema.d.ts`。不抽 `web/` 与 `web-react/` 共享层。**长期委托规则(定时/条件触发)不做**——本 loop 的「委托」只做单次、发起人手动指定的一次性委托,对齐 §十三 13.3 明确写的「委托」而非 CONTEXT.md 未提及的规则委托。

## DONE-CONDITION

- 本账本 `## Tasks` 全部打勾
- `dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` 绿(基线 92,M2b 只增不减)
- `cd web && npm run typecheck && npm run lint` 绿,`npx vitest run src/workflow/` 绿
- 真实浏览器至少走通:退回一单、撤销一单、催办一次、抄送列表已读、我发起的/我已办的列表可见——留截图证据
- 双模板 `gen:api` 后 schema SHA256 一致

> ⚠️ 沿用 M2a 修正过的过滤器写法,**不要**回退成 `~Workflow`(漏掉 `WfXxxTests`)或 `~Wf|~Workflow`(误拉 `Snowflake` 系列)。

## Status

- 轮次: 19
- max: 60
- 当前任务: 5
- 当前阶段: exec
- 上一轮: Round 18 — plan。读了 `CompleteTaskOp.cs`(全文)、`Schema/WfNode.cs`/`WfSchemaEnums.cs`(`OnReject`/`RejectToNodeId`/`ReturnPolicy`/`ReturnToNodeId` 精确形状)、`WfHisTask.cs`/`WfHistory.cs`/`WfToken.cs`、`TransferTaskOp.cs`(CAS 模板)、`WorkflowEngine.cs`(全文)、`WfInstanceService.StartAsync`、设计草案 §六原表(锁定"退回后重提默认从头重走,不管退回目标是哪个节点"这条关键语义)。核心判断(厘清共用与否):**拒绝路由**与**主动退回**是两套不同机制——拒绝路由直接复用 `EnterNodeOp` 自动继续;主动退回关闭当前任务后**不**自动继续,需要发起人显式调用**新增第三套引擎命令"重提"**(`ResubmitInstanceCmd`,`BeginStartAsync` 的翻版,作用在已有实例行上)。按 A(拒绝路由)→B(主动退回)→C(重提)拆了 3 大块、25 个步骤(2+3+4=9 条新测试,5 个区分力变异点),写进 `## Plan`,附陷阱记录 5 条。**未写任何产品代码**。
- 下一步: Task 5(拒绝路由 + 主动退回)exec——把 `## Plan` 全文(A/B/C 三块 + 陷阱记录)+ 硬约束喂给 `Agent(executor, model=sonnet)`,提醒它这是大任务、可能需要跨轮完成,按 A→B→C 顺序推进,做到哪算哪、诚实汇报

## 已知起点(2026-08-20 实测,免得重查)

- **M1/M2a 已经把大量 schema 空壳焊好,M2b 主要是把插头插上,不是新建**:
  - `WfNode.Props`(`Schema/WfNode.cs`)已有 `OnReject`(`WfRejectAction: Terminate|ToNode`)+ `RejectToNodeId`、`ReturnPolicy`(`WfReturnPolicy: Prev|Any|Node`)+ `ReturnToNodeId`、`Timeout`(`WfTimeout{Hours,Action:Remind|AutoPass|AutoReject|Transfer,TransferUserId}`)——**全部字段就位,零消费方**。
  - `WfTaskAction` 枚举已有 `Return=4`/`Cancel=5`/`Delegate=6`/`Urge=7`,`WfHistoryEventType` 已有 `TimeoutFired=6`——**枚举值预留,引擎从未产出过**。
  - `WfCc` 实体(`Entities/WfCc.cs`)已有 `IsRead`/`ReadTime` 列,`EnterCcAsync`(`EnterNodeOp.cs`)已在写 `wf_cc` 行——**缺的是查询/已读的 Service+Controller+前端页**,不是表。
  - `IRealtimePublisher`(`TenonAdmin.Core/Realtime/IRealtimePublisher.cs`)已是成品:`NotifyUserAsync`/`NotifyAllAsync`/`NotifySessionAsync`;`NoopRealtimePublisher`(默认)+ `SignalRRealtimePublisher`(`TenonAdmin:Realtime:Enabled=true` 时,`AspNetCore` 层)都已注册。**`IWorkflowNotifier` 只是编排层,不新建长连接**。
  - `IWorkflowNotifier`(`Abstractions/IWorkflowNotifier.cs`)现在是**零方法空接口**,`WorkflowSetup.cs` 甚至没注册它——Task 1 要先补方法再 `TryAdd`。
  - `WfInstanceService.PageMineAsync`(已存在,`Where(i => i.StarterUserId == starterUserId)`)= 「我发起的」的后端契约**已经好使**;`WfTaskService.PageDoneAsync`(已存在)= 「我已办的」**同样已经好使**。前端目前只有 `views/workflow/{definition,instance/detail.vue,todo}` 三块,**没有对应列表页/路由/菜单**——这两项是纯前端任务。
  - `TransferTaskOp.cs` 是「CAS 校验 Pending → 写 `WfHisTask` → 挂新 actor / 推进」这套模式的现成模板,`委托` 可以照此写一个新 Op,不要另起炉灶。
- **`CompleteTaskOp.RejectInstanceAsync`(`Engine/Operations/CompleteTaskOp.cs:158`)是本 loop 最核心的改点**:当前硬编码 `// M1:忽略 toNode,统一 terminate`,`node` 参数传进来却完全没用(`_ = node;`)。M2b 要按 `node.Props.OnReject` 分流。
- **`EnterNodeOp.CreateTaskAsync`(`Engine/Operations/EnterNodeOp.cs:225`)`DueTime = null` 是硬编码**——超时 Job 落地时要从 `Node.Props?.Timeout?.Hours` 算出真实 `DueTime`。
- `WfTaskController`/`WfInstanceController` 目前分别是 3 端点起步(`Todo/Done/Approve/Reject/Transfer` 与 `Startable/Start/Page/History/Get`)——新增动词各自需要一条 Controller action + `[RolePermission]` 天然生效(路由即权限码,不用手写权限字符串)。
- **CONTEXT.md「行为语义默认值」是本 loop 的跨任务契约**(已核实仍与代码现状一致,未被 M2a 动过):拒绝→默认终止(节点可配退回)、退回重提→默认从头重走、撤销→仅限无人已审、抄送≠待办(独立列表+已读)、同一人相邻节点→默认自动通过一次。
- **不存在的东西,别去找**:`WfCcService`/`WfCcController`、`WfTimeoutJob`、`WfDelegate*`、`WfUrge*` 全部零文件,本 loop 从零建。
- **务必 grep 一遍再动手,别学 Round 9 的教训(M2a 台账 P1 史)**:`CompleteTaskOp`/`TransferTaskOp`/`WfTaskService` 里凡是 CAS 校验 Pending actor 的写法,新增动词(委托/退回)如果结构相似要按**代码形状** grep 现有三份,不要只 grep 符号名。

## 语义契约(跨任务长期有效;`## Plan` 被重写也不得丢)

| 场景 | 定案(源:CONTEXT.md + §十三 13.3,本轮未翻转) |
|---|---|
| 拒绝(Reject) | `node.Props.OnReject` 缺省或 `Terminate` → 现状不变(终止);`ToNode` → 不终止,token 回到 `RejectToNodeId` 节点重建任务(视为该节点重新进入,走 `EnterNodeOp` 现有逻辑,不新增专门的"拒绝重建"代码路径) |
| 退回(Return,新动词,区别于拒绝) | 办理人主动选择"退回"(非拒绝/非同意);目标节点按 `node.Props.ReturnPolicy`:`Prev`=上一个已走过的审批节点、`Any`=办理人从历史节点里选、`Node`=固定 `ReturnToNodeId`;退回后原实例保持 `Running`,当前 token 回退到目标节点 |
| 退回重提 | 发起人对被退回的实例重新提交(復用同一 `wf_instance` 行,不建新实例)后,**默认从头重走**(`start` 节点开始),不是从退回点重走——即退回本质是「打回发起人重填」,不是「打回某审批人重批」 |
| 撤销(Cancel) | 仅发起人可撤销;仅当 `wf_his_task` 里**没有任何 `Approve` 记录**(还没人批过)才允许;`WfInstanceStatus.Cancelled`,token `Cancelled`,当前任务全部 `Skipped` |
| 委托(Delegate,一次性) | 发起人/办理人把**当前一个待办**指给别人处理,不是长期规则;与 `Transfer`(转办)的区别是语义标签不同(`WfTaskAction.Delegate` vs `Transfer`),底层机制可复用 `TransferTaskOp` 的 CAS+建新 actor 模式,只是历史动作记 `Delegate` |
| 催办(Urge) | 发起人对当前 Pending 办理人发一次提醒;不改变任何任务/实例状态,只落一条历史事件 + 走 `IWorkflowNotifier` 推送;可重复催办,不做频率限制(YAGNI,超出本轮范围) |
| 超时(Timeout) | `node.Props.Timeout.Hours` 非空且 >0 时,建任务按 `TimeProvider.GetLocalNow() + Hours` 填 `wf_task.DueTime`;`WfTimeoutJob : IAdminJob` 扫 `DueTime < now` 的活跃任务,按 `Timeout.Action`:`Remind`(只推送不改状态,可重复触发)、`AutoPass`(等价于该 actor 自动同意)、`AutoReject`(等价于该 actor 自动拒绝)、`Transfer`(转给 `TransferUserId`) |
| 同一人相邻节点去重 | `EnterApprovalAsync` 解析出的办理人集合,若与**紧邻的上一个已完成审批节点**的办理人集合有交集且该交集用户在上一节点已 `Approve`,对交集用户自动记一次「跳过」(不建 Pending actor,只在办理人 ≥2 人时对其余人正常建任务;若解析结果整体只剩该用户一人,等价于该节点整体自动通过) |
| 抄送 | 独立列表(不是待办),`WfCc.IsRead` 由查看详情页时标记已读(仿 `SysNotice` 已读语义,不新建通道) |

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段重写)

> **Task 5 — 拒绝路由 + 主动退回(本 loop 最复杂的任务)**。读了 `Engine/Operations/CompleteTaskOp.cs`(全文——`RejectInstanceAsync` 当前硬编码终止,`node` 参数传进来 `_ = node;` 完全没用,行为要按 `node.Props?.OnReject` 分流)、`Schema/WfNode.cs`(`WfNodeProps.OnReject`/`RejectToNodeId`/`ReturnPolicy`/`ReturnToNodeId` 四个字段的准确类型与注释)、`Schema/WfSchemaEnums.cs`(`WfRejectAction{Terminate,ToNode}`、`WfReturnPolicy{Prev,Any,Node}`,JSON 走 camelCase)、`Entities/WfHisTask.cs`(无专门"目标节点"列,`TransferToUserId` 是 Transfer 专用列的先例——Return 不新增列,目标节点记进 `WfHistory.PayloadJson`)、`Entities/WfHistory.cs`/`WfToken.cs`、`Engine/Operations/TransferTaskOp.cs`(CAS 认领 Pending actor → 写 `WfHisTask` → 收尾的现成模板,`ReturnTaskOp` 照此写,不套 `CompleteTaskOp` 的三态计票逻辑)、`Engine/WorkflowEngine.cs`(全文——`BeginStartAsync`/`BeginCompleteAsync`/`BeginTransferAsync` 三种"加载+校验在 BeginXxxAsync,写操作在 IWfOperation"的分工;`ResolveLeaderLevels`/`SnapshotLeaderChainsAsync`/`DeserializeSelectedUsers`/`DeserializeLeaderChainsByLevel` 四个可复用的 `protected virtual` 辅助方法)、`Services/WfInstanceService.StartAsync`(重提要抄的原型)、docs 设计草案 §六「行为语义默认值」原表(比台账「语义契约」表更原始的措辞:「退回后重新提交」默认行为=**从头重走**,可配项(不在本任务范围)=「从退回节点继续,已过节点不再审」——这句话锁死了"退回目标节点"只是记录/展示用,**功能上退回后重提永远从 `start` 重新走全链,不管 ReturnPolicy 解析出的节点是哪个**)。
>
> **核心判断(厘清「退回」与「拒绝路由」是否共用同一个 token 回退函数):不共用,是两套完全不同的机制**——
> 1. **拒绝路由**(`OnReject=ToNode`):语义契约原文「视为该节点重新进入,走 `EnterNodeOp` 现有逻辑,不新增专门的'拒绝重建'代码路径」——直接 `ctx.Agenda.Plan(new EnterNodeOp(targetNode))`,**自动继续**,不等发起人。`RejectToNodeId` 是节点级固定配置,没有 Prev/Any/Node 三选一。
> 2. **主动退回**(新动词 `WfTaskAction.Return`):语义契约原文「退回本质是'打回发起人重填',不是'打回某审批人重批'」——`ReturnTaskOp` 关掉当前活跃任务后**不 `EnterNodeOp` 、不自动继续**,`WfToken.NodeId` 只是记录 `ReturnPolicy` 解析出的目标节点(供历史/UI 展示"退回到了哪一步"),真正让流程往下走的是发起人调用**新增的"重提"能力**——这是第三套引擎命令(`ResubmitInstanceCmd`),内部逻辑 = `BeginStartAsync` 的翻版(校验表单→重新快照多级主管链→`EnterNodeOp(model.Root)` 从 `start` 整条链重走),但作用在**已有的** `wf_instance`/`wf_token` 行上,不插新实例行。
>
> 三块互不依赖但共用同一批 helper,建议按 A(拒绝路由,最小)→B(主动退回)→C(重提)顺序做,每块做完能独立跑绿一批测试。

### 关键定案

| 决策点 | 定案 |
|---|---|
| A. 拒绝路由怎么改 | `CompleteTaskOp.RejectInstanceAsync(ctx, node, cancellationToken)` 开头判 `node.Props?.OnReject`:缺省/`Terminate` → 现状代码原样保留(整段不动,只是包进 `else` 分支或提前 `return` 分流);`ToNode` → 目标 `var target = ctx.FindNode(node.Props!.RejectToNodeId!) ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid, new(){["reason"]="rejectTargetInvalid"})`;写 `ctx.AppendHistoryAsync(WfHistoryEventType.NodeLeave, node.Id, ...)`(仿 `TakeTransitionOp` 离开当前节点的写法),`ctx.Agenda.Plan(new EnterNodeOp(target)); return;`(**不**碰 `Instance.Status`/`Token.Status`/`FormBinder`/通知——实例仍在正常审批流程里,`EnterNodeOp` 自己会处理后续一切)。**这部分不需要新 Cmd/新 Op,是 `CompleteTaskOp` 里一个方法的行为分叉**,`CompleteTaskOp.ExecuteAsync` 的调用点 `await RejectInstanceAsync(ctx, node, cancellationToken); return;` 不用改。 |
| B1. Return 的 Cmd/Op 结构 | 新增 `ReturnTaskCmd : IWfCommand { required long TaskId; required long UserId; string? TargetNodeId; string? Comment; }`(仿 `TransferTaskCmd`,`TargetNodeId` 只在 `ReturnPolicy=Any` 时有意义,其余策略忽略这个字段);`WorkflowEngine` 加 `BeginReturnAsync`(结构完全照抄 `BeginTransferAsync`:task 存在→`TaskNotFound`/`TaskConflict`、`instance.Status!=Running`→`InstanceStatusConflict`、加载 `token`/`version`/`model`、建 ctx)+ 新建 `Engine/Operations/ReturnTaskOp.cs`(`IWfOperation`,构造参数 `WfTask task, long userId, string? targetNodeId, string? comment`,结构照抄 `TransferTaskOp`:CAS 认领 `WfTask.Version` → CAS 认领 `WfTaskActor`(`UserId==caller && Status==Pending && ActorType==Approver`,claim 失败→`TaskConflict`))。 |
| B2. 目标节点怎么解析(三选一,在 `ReturnTaskOp.ExecuteAsync` 里做,认领成功之后) | 读当前节点 `var node = ctx.FindNode(Task.NodeId) ?? throw ModelInvalid`;按 `node.Props?.ReturnPolicy`:**`Node`**→`node.Props!.ReturnToNodeId`(为空→`WorkflowErrorCode.ReturnNotAllowed` reason=`"targetNotConfigured"`);**`Prev`**→查 `ctx.Db.Queryable<WfHisTask>().Where(h => h.TokenId == Task.TokenId && h.Action == WfTaskAction.Approve).OrderBy(h => h.Id, OrderByType.Desc).FirstAsync()`,有则取其 `NodeId`,**没有**(本节点是链上第一个审批节点)则退化取 `ctx.Model.Root.Id`(=`start`,语义上"没有更早的节点可退,只能退到最开始");**`Any`**→用调用方传入的 `TargetNodeId`,校验:非空、`ctx.FindNode(...)` 能解析、且必须出现在 `ctx.Db.Queryable<WfHistory>().Where(h => h.InstanceId == ctx.Instance.Id && h.EventType == WfHistoryEventType.NodeEnter).Select(h => h.NodeId).ToListAsync()`(本实例真正走过的节点集合)里,任一条件不满足 → `ReturnNotAllowed` reason=`"invalidTarget"`;**未配置 `ReturnPolicy`**(节点没开退回)→ `ReturnNotAllowed` reason=`"policyNotConfigured"`。最终目标节点 id 统一再过一遍 `ctx.FindNode(targetId) ?? throw ModelInvalid` 兜底。 |
| B3. Return 具体动作序列 | ①`ctx.Db.Insertable(new WfHisTask{InstanceId,NodeId=Task.NodeId,NodeName=node.Name,TaskId=Task.Id,TokenId=Task.TokenId,UserId,Action=WfTaskAction.Return,Comment,DurationMs}).ExecuteCommandAsync()`(**要写**,退回是一次真实办理动作,和 Urge/Cancel 不同);②`ctx.AppendHistoryAsync(WfHistoryEventType.TaskCompleted, Task.NodeId, new{taskId=Task.Id,userId,action="Return",targetNodeId}, ct)`(复用既有枚举值,不新增——`TaskCompleted` 注释本来就写"…"留了口子,`TransferTaskOp` 也是这么记转办的);③关闭当前活跃任务:`WfTaskActor` 全部标 `Skipped`(不限 Pending,顺序会签的 Waiting 后手也要清)→ 物理删 actor 行 → 物理删 `WfTask` 行(和 `CompleteTaskOp.CloseTaskAsync`/`CancelInstanceOp` 同一套三步,直接在 `ReturnTaskOp` 里手写,不抽公共方法——**这是台账已经记过两次的 P3 留痕,不在本任务新增第三处时才去重构,时间不够**);④`ctx.Token.NodeId = targetNodeId`,`ctx.Db.Updateable(ctx.Token).UpdateColumns(t => new{t.NodeId, t.UpdateTime, t.UpdateUserId}).ExecuteCommandAsync()`(`Token.Status` **不变**,仍是 `Active`——`退回后原实例保持 Running`,不是完结);⑤**不** `ctx.Agenda.Plan(...)` 任何东西——Agenda 空,引擎自然停,等发起人重提。 |
| C1. 重提的 Cmd/Op 结构 | 新增 `ResubmitInstanceCmd : IWfCommand { required long InstanceId; required long CallerUserId; string? VariablesJson; IReadOnlyDictionary<string, List<long>>? SelectedUserIdsByNode; }`;`WorkflowEngine` 加 `BeginResubmitAsync`(结构是 `BeginStartAsync` 与 `BeginCancelAsync` 的合体:①加载 `instance`(`ClearFilter<IOrgScoped>()`,不存在→`InstanceNotFound`);②`instance.Status!=Running`→`InstanceStatusConflict`;③`instance.StarterUserId!=cmd.CallerUserId`→新码 `ResubmitNotAllowed` reason=`"notStarter"`;④加载该实例的活跃 `WfToken`(`Status==Active`,不存在→`TokenNotFound`),再查 `ctx.Db.Queryable<WfTask>().AnyAsync(t => t.TokenId == token.Id)`——**为真**说明还有活跃待办、根本不是"被退回等重提"的状态 → `ResubmitNotAllowed` reason=`"hasActiveTask"`;⑤加载 `version`/`model`(和 `BeginStartAsync` 一样反序列化);⑥`await formBinder.ValidateOnStartAsync(new WfFormBindContext{InstanceId=instance.Id, DefinitionVersionId=instance.DefinitionVersionId, BusinessKey=instance.BusinessKey, VariablesJson=cmd.VariablesJson ?? instance.VariablesJson, Status=Running, StarterUserId=instance.StarterUserId}, ct)`;⑦若 `cmd.VariablesJson` 非空,`instance.VariablesJson = cmd.VariablesJson`;⑧重新走 `ResolveLeaderLevels(model)` + `SnapshotLeaderChainsAsync(...)`(**这一步要改签名**,见下一行);⑨`instance.LeaderChainJson` 重写;⑩`ctx.Db.Updateable(instance).UpdateColumns(i => new{i.VariablesJson, i.LeaderChainJson, i.SelectedUserIdsJson, i.UpdateTime, i.UpdateUserId}).ExecuteCommandAsync()`(`SelectedUserIdsJson` 同理若 `cmd.SelectedUserIdsByNode` 非空则覆盖);⑪`token.NodeId = model.Root.Id`,更新 DB;⑫建 ctx(`Instance=instance,Token=token,...`,`SelectedUserIdsByNode` 用 `cmd.SelectedUserIdsByNode ?? DeserializeSelectedUsers(instance.SelectedUserIdsJson)`,`LeaderChainByLevel` 用刚重算的);⑬`ctx.AppendHistoryAsync(WfHistoryEventType.InstanceResubmitted, model.Root.Id, new{starterUserId=cmd.CallerUserId}, ct)`(**新枚举值**,不能複用 `InstanceStarted`——那个专指"实例创建",重提没建新实例,审计语义不同);⑭`agenda.Plan(new EnterNodeOp(model.Root))`;⑮`return ctx;`。 |
| C2. `SnapshotLeaderChainsAsync` 签名要改 | 现在签名是 `SnapshotLeaderChainsAsync(StartInstanceCmd cmd, ...)`,内部只读 `cmd.StarterUserId`/`cmd.StarterOrgId` 两个字段——`BeginResubmitAsync` 没有 `StartInstanceCmd` 可传,与其现造一个假 Cmd 硬凑参数,不如把签名改成 `SnapshotLeaderChainsAsync(long starterUserId, long? starterOrgId, IReadOnlyDictionary<...> leaderLevels, CancellationToken ct)`——`protected virtual`、只有 `BeginStartAsync` 这一个既有调用点,改起来零风险,`BeginStartAsync` 里的调用改成传 `cmd.StarterUserId, cmd.StarterOrgId` 两个值。`BeginResubmitAsync` 里 `starterOrgId` 现查一次 `SysUser`(仿 `BeginCompleteAsync` 里 `starter = await db.Queryable<SysUser>().Where(u=>u.Id==instance.StarterUserId).FirstAsync(); starterOrgId = starter?.OrgId`)。 |
| 新错误码 | `WorkflowErrorCode.cs` 加 `ReturnNotAllowed = 48024`(注释「退回策略未配置 / 目标节点非法」)、`ResubmitNotAllowed = 48025`(注释「非发起人重提 / 无待重提状态」)。**不碰 48022 那个记账留痕的空洞**,继续往后编号,免得和 Task 4 的留痕记录打架。 |
| 新枚举值 | `WfHistoryEventType` 加 `InstanceResubmitted = 12`(注释「发起人重提(退回后重新提交,复用同一实例行)」)。`WfTaskAction.Return` 已经是既有值(=4),不用改枚举。 |
| DTO/Controller | `WfTaskActionInput` 加一个可空字段 `public string? TargetNodeId { get; init; }`(只有 `ReturnPolicy=Any` 时前端会传,Approve/Reject/Transfer 忽略它,和现有 `ToUserId` 只有 Transfer 用的做法一致);`WfTaskController` 加 `[HttpPost("return")] [OperationLog("退回")]`,调 `taskService.ReturnAsync(input.TaskId, CurrentUserId, input.TargetNodeId, input.Comment, ct)`。实例侧:**顺手解决 Task 4 复评记录里记的 P3**——`WfRuntimeModels.cs` 里 `WfInstanceActionInput` 改名成 `WfInstanceCancelInput`(评审当时就预判了这个名字会和退回重提撞,现在真撞上了,免费改),`WfInstanceController.Cancel` 的入参类型同步改;新增 `WfInstanceResubmitInput { long InstanceId; string? VariablesJson; Dictionary<string, List<long>>? SelectedUserIdsByNode; }`,`WfInstanceController` 加 `[HttpPost("resubmit")] [OperationLog("重新提交")]`,调 `instanceService.ResubmitAsync(input.InstanceId, CurrentUserId, input.VariablesJson, input.SelectedUserIdsByNode, ct)`。 |
| Service 层签名 | `IWfTaskService`/`WfTaskService` 加 `Task<WfEngineResult> ReturnAsync(long taskId, long userId, string? targetNodeId, string? comment, CancellationToken ct = default) => engine.ExecuteAsync(new ReturnTaskCmd{TaskId=taskId,UserId=userId,TargetNodeId=targetNodeId,Comment=comment}, ct)`;`IWfInstanceService`/`WfInstanceService` 加 `Task<WfEngineResult> ResubmitAsync(long instanceId, long callerUserId, string? variablesJson, IReadOnlyDictionary<string,List<long>>? selectedUserIdsByNode, CancellationToken ct = default) => engine.ExecuteAsync(new ResubmitInstanceCmd{...}, ct)`。两个都是薄封装转发,和 `StartAsync`/`CancelAsync` 同款。 |
| 通知 | 不加新 `IWorkflowNotifier` 方法(YAGNI,和 Return/Resubmit 都不是"实例完结"语义,套用 `InstanceCompletedAsync` 不合适;语义契约没提这条)。 |
| 可替换性 | 新方法/新类沿用本包"整包 virtual"惯例;`ReturnAsync`/`ResubmitAsync` 扩了 `IWfTaskService`/`IWfInstanceService`,会连带破坏 `WorkflowReplaceabilityTests.cs` 的 `FakeTaskService`/`FakeInstanceService`,机械补 `NotSupportedException` 桩(第三次踩这个坑了,纯体力活)。 |

### 步骤

**A. 拒绝路由(最小,先做,独立可跑绿)**

1. `Engine/Operations/CompleteTaskOp.cs` 的 `RejectInstanceAsync` 按「A. 拒绝路由怎么改」分流。**注意**:`_ = node;` 那行要删掉(现在真的要用 `node` 参数了)。
2. 新测试文件 `backend/tests/TenonAdmin.Tests/WfRejectRoutingTests.cs`(脚手架仿 `WfCancelTests.cs`)。2 条 `[Fact]`:
   - `Reject_with_default_terminate_still_terminates`(回归门):节点不配 `OnReject`(缺省),拒绝 → `instanceStatus==Rejected`,和现状行为完全一致——防止这次改动误伤 M1/M2a 已有的拒绝终止路径。
   - `Reject_with_toNode_routes_back_without_terminating`:三节点链 start→node1(any,[A])→node2(any,[B],`OnReject=ToNode`,`RejectToNodeId="node1"`)→null。A 批 node1,B 在 node2 拒绝 → 断言 `instanceStatus` 仍是 `Running`(不是 `Rejected`);A 的 `todo` 列表重新出现 node1 的任务(`EnterNodeOp` 对 node1 重新解析办理人建的新任务)。
3. 变异(亲手做):去掉分流判断(强制永远走 terminate 分支)→ `Reject_with_toNode_routes_back_without_terminating` 必须红。
4. 验证:`dotnet build` + `dotnet test ... --filter "FullyQualifiedName~WfRejectRoutingTests"`,绿了再进 B。

**B. 主动退回(Return)**

5. `Abstractions/WorkflowErrorCode.cs` 加 `ReturnNotAllowed = 48024`。
6. `Entities/WfEnums.cs` 的 `WfTaskAction` **不用改**(`Return=4` 已存在);`WfHistoryEventType` 本步骤不用改(Return 复用 `TaskCompleted`,新枚举值是 C 步骤的 `InstanceResubmitted`)。
7. `Engine/WfCommands.cs` 加 `ReturnTaskCmd`。
8. `Engine/WorkflowEngine.cs` 加 `command switch` 分支 `ReturnTaskCmd ret => await BeginReturnAsync(db, ret, cancellationToken),` + `BeginReturnAsync` 方法(结构照抄 `BeginTransferAsync`)。
9. 新建 `Engine/Operations/ReturnTaskOp.cs`,按「B2 目标解析」+「B3 动作序列」实现。
10. `Services/IWfTaskService.cs`/`WfTaskService.cs` 加 `ReturnAsync`。
11. `Services/WfRuntimeModels.cs` 的 `WfTaskActionInput` 加 `TargetNodeId`;`Controllers/WfTaskController.cs` 加 `POST task/return`。
12. `WorkflowReplaceabilityTests.cs` 的 `FakeTaskService` 补 `ReturnAsync` 桩。
13. 新测试文件 `backend/tests/TenonAdmin.Tests/WfReturnResubmitTests.cs`(先写 Return 部分,Resubmit 部分见步骤 C)。Return 的 3 条 `[Fact]`:
    - `Return_with_node_policy_closes_current_task_without_auto_continuing`:node1(any,[A])→node2(any,[B],`ReturnPolicy=Node`,`ReturnToNodeId="node1"`)。A 批 node1,B 在 node2 退回 → 断言 `instanceStatus` 仍 `Running`;B 的 `todo` 为空(任务已关);**A 的 todo 也为空**(区分"退回≠拒绝路由"的关键断言——不自动继续,不会立刻给 node1 重建任务);`wf_his_task` 里有一条 B 的 `Action=Return` 记录。
    - `Return_with_prev_policy_falls_back_to_start_when_no_prior_approval`:单节点 start→node1(any,[A],`ReturnPolicy=Prev`)→null,node1 是链上第一个审批节点(没有更早的已批准节点)。A 直接退回(没人批过任何节点)→ 断言 `code==0`(不报错,`Prev` 无先例时优雅退化到 `start`)。
    - `Return_with_any_policy_rejects_unwalked_target`:随便一条正常链,B 退回时传一个从没被这个实例走过的 `targetNodeId`(比如瞎编一个不存在的 id,或者模型里存在但这个实例从没进入过的节点)→ `code == ReturnNotAllowed`(48024)。
14. 变异(亲手做,逐个转红还原):①删掉 B3 第③步"关闭活跃任务"→ `Return_with_node_policy_...` 的"B 的 todo 为空"断言必须红;②删掉 B2 里"Any 策略校验 targetNodeId 属于本实例已走节点"那段 → `Return_with_any_policy_rejects_unwalked_target` 必须红。
15. 验证:build + `--filter "FullyQualifiedName~WfReturnResubmitTests"`,Return 的 3 条绿了再进 C。

**C. 退回重提(Resubmit)**

16. `Abstractions/WorkflowErrorCode.cs` 加 `ResubmitNotAllowed = 48025`。
17. `Entities/WfEnums.cs` 的 `WfHistoryEventType` 加 `InstanceResubmitted = 12`。
18. `Engine/WorkflowEngine.cs`:①把 `SnapshotLeaderChainsAsync` 签名从 `(StartInstanceCmd cmd, ...)` 改成 `(long starterUserId, long? starterOrgId, ...)`,同步改 `BeginStartAsync` 里唯一的调用点;②新增 `ResubmitInstanceCmd`(`Engine/WfCommands.cs`)+ `command switch` 分支 + `BeginResubmitAsync`(按「C1」15 个子步骤实现)。
19. `Services/IWfInstanceService.cs`/`WfInstanceService.cs` 加 `ResubmitAsync`。
20. `Services/WfRuntimeModels.cs`:`WfInstanceActionInput` 改名 `WfInstanceCancelInput`(同步改 `Controllers/WfInstanceController.cs` 的 `Cancel` 入参类型引用);新增 `WfInstanceResubmitInput`。`Controllers/WfInstanceController.cs` 加 `POST instance/resubmit`。
21. `WorkflowReplaceabilityTests.cs` 的 `FakeInstanceService` 补 `ResubmitAsync` 桩。
22. `WfReturnResubmitTests.cs` 追加 Resubmit 的 4 条 `[Fact]`:
    - `Starter_can_resubmit_after_return_and_flow_walks_from_start_again`(核心用例):node1(any,[A])→node2(any,[B],`ReturnPolicy=Node`,`ReturnToNodeId="node1"`)。A 批 node1,B 退回(目标 node1),发起人调用 resubmit → 断言:A 的 `todo` **重新出现 node1 的任务**(证明"从头重走",连已经批过的 node1 都要重新审——不是简单跳回 B 退回时记录的那个"目标节点"就直接续到那);`GET history/{id}` 有 `InstanceResubmitted` 事件。
    - `Non_starter_cannot_resubmit`:B(审批人,非发起人)调用 resubmit → `code == ResubmitNotAllowed`(48025)。
    - `Cannot_resubmit_when_instance_has_active_task`:正常流程走到一半(A 批完 node1,B 在 node2 有活跃待办,没人退回)→ 发起人此时调用 resubmit → `code == ResubmitNotAllowed`(不是"没退回不让重提"这种特殊码,统一走这个,`reason` 区分即可)。
    - `Resubmit_with_new_variables_json_overrides_instance_data`:退回后重提时带一个新的 `variablesJson` → 断言 `GET instance/{id}` 详情接口返回的 `variablesJson` 是重提时传的新值,不是原来发起时的旧值(证明"发起人重填"确实生效,不是摆设参数)。
23. 变异(亲手做):①删掉 `BeginResubmitAsync` 里"无活跃任务"这条校验 → `Cannot_resubmit_when_instance_has_active_task` 必须红;②删掉发起人校验 → `Non_starter_cannot_resubmit` 必须红;③把 `EnterNodeOp(model.Root)` 换成不做任何 Agenda.Plan(模拟"忘记重新入 start")→ `Starter_can_resubmit_after_return_and_flow_walks_from_start_again` 里"A 的 todo 重新出现 node1"这条断言必须红。
24. **范围**:预期改动/新增文件——`Abstractions/WorkflowErrorCode.cs`(+2 常量)、`Entities/WfEnums.cs`(+1 枚举成员)、`Engine/Operations/CompleteTaskOp.cs`(改 `RejectInstanceAsync`)、`Engine/WfCommands.cs`(+2 Cmd)、`Engine/WorkflowEngine.cs`(+2 switch 分支 + 2 `BeginXxxAsync` + 1 处签名重构 `SnapshotLeaderChainsAsync`)、新增 `Engine/Operations/ReturnTaskOp.cs`、`Services/IWfTaskService.cs`/`WfTaskService.cs`(+`ReturnAsync`)、`Services/IWfInstanceService.cs`/`WfInstanceService.cs`(+`ResubmitAsync`)、`Services/WfRuntimeModels.cs`(+`TargetNodeId`字段、DTO 改名、+2 新 DTO)、`Controllers/WfTaskController.cs`(+1 端点)、`Controllers/WfInstanceController.cs`(+1 端点、1 处类型引用更新)、`WorkflowReplaceabilityTests.cs`(2 处机械桩)、新增 `WfRejectRoutingTests.cs`、新增 `WfReturnResubmitTests.cs`。**不碰** `EnterNodeOp.cs`/`TakeTransitionOp.cs`/`TransferTaskOp.cs`/`CancelInstanceOp.cs` 内部逻辑(只读引用/照抄模式,不改这些文件本身)、`WfTaskActor`/`WfHisTask` schema(不新增列)、前端、`web-react/`、Task 6-12 范围。
25. **验证**:每块(A/B/C)各自跑完就用目标 `--filter` 验证一次,最后跑全量:`dotnet build backend/TenonAdmin.slnx -c Release`;`dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow" -nodeReuse:false`(基线 114,预期 +9 条新用例:拒绝路由 2 + Return 3 + Resubmit 4→123);无 TODO/占位/`.skip`。

### 陷阱记录(plan 阶段读码时发现,提醒 exec 别踩)

- `_ = node;` 那行如果忘删,`node` 参数虽然现在真被用了但编译器不会报错(只是原来那行变冗余/矛盾),要显式确认删掉。
- `SnapshotLeaderChainsAsync` 改签名后,`BeginStartAsync` 里的调用点**必须同步改**,漏改会直接编译失败(强类型捕获,但别指望编译器帮你找到语义正确性,只能保证调用点存在)。
- Return 的"Prev"策略退化到 `start` 时,`ctx.Model.Root.Id` 取的是**当前 ctx 里已反序列化的模型**,不要重新查 DB 反序列化一遍(`BeginReturnAsync` 已经把 `model` 放进 ctx 了)。
- `WfInstanceActionInput` 改名成 `WfInstanceCancelInput` 时,`web`/`web-react` 的 `schema.d.ts` **不用现在同步改**——那两个前端模板要等 Task 12 统一跑 `gen:api` 才会重新生成,本任务只改后端 C# 类型名,不碰任何前端文件。
- Resubmit 的"无活跃任务"校验容易和 Cancel 的"无 Approve 历史"校验搞混——这两个校验目的不同(Resubmit 校验的是"当前有没有人在审",不是"有没有人已经审过"),不要抄错条件。
- `ReturnTaskOp` 的 CAS 认领顺序照抄 `TransferTaskOp`(先认领 `WfTask.Version`,再认领 `WfTaskActor.Status==Pending`),两次认领失败都是 `TaskConflict`,不要和新加的 `ReturnNotAllowed` 混着用——`ReturnNotAllowed` 专指"退回目标解析失败",`TaskConflict` 专指"并发/非本人办理"这类既有语义。

## Findings(review 阶段产出;修完划掉)

### 当前任务 Task 5 — (空,尚未 review)

### Task 4 复评记录(Round 16 code-reviewer 结果 2×P2;Round 17 exec 修,已闭合)

- ~~**[P2]** `CancelInstanceOp.cs` 的 `Instance`/`Token` 状态翻转没有 CAS 保护,双击撤销或撤销撞第一次批准会导致 `FormBinder`/通知重复触发或状态自相矛盾。~~ ✅ Round 17 把 `Instance` 状态更新改成条件更新(`Where(... && i.Status == Running)`,`claimed != 1` 抛既有 `InstanceStatusConflict`),`Token` 保持不变(评审确认锚在 `Instance` 上足够覆盖两种竞态)。审计字段(`UpdateTime`/`UpdateUserId`)显式手填,读了 `SqlSugarRepository.SoftDeleteCoreAsync` 的同类先例确认 `SetColumns` 会绕过整对象更新的审计 AOP。Opus 读码确认实现与修复方案一致;此项是防御性并发修复,当前 xUnit 单线程套件无法自然构造并发红测,已改用"读码逐行核对 + 全量回归零破坏"替代变异测试(与台账 Findings 处置意见一致,非漏检)。
- ~~**[P2]** `WfCancelTests.cs` 对 `FormBinder`/事务后通知在撤销路径上零覆盖。~~ ✅ Round 17 在 `WorkflowNotifierTests.cs` 新增 `Cancel_flow_notifies_instance_completed`,与既有 Approve/Reject 完结用例同文件归类。Opus 亲手复跑变异(临时删掉 `CancelInstanceOp.cs` 里的 `ctx.PendingInstanceCompletedNotification = ...` 赋值块)→ 独立确认该用例红(`Assert.Single() Failure: The collection was empty`),复原后独立重跑全量 114/114,`git diff --check` 干净、无残留。

### 可选/留痕(不阻塞推进,时间充裕时顺手做,否则记账留到下次碰这块代码)

- **[P3]** `WorkflowEngine.cs` 的 `BeginCancelAsync` 里,`instance.Status != Running` 判断先于"是否是发起人"判断——非发起人可以靠撤销接口区分"实例不存在(48003)/已完结(48004)/存在且 Running(48023 notStarter)",构成一个可枚举实例状态的信息泄露 oracle(雪花 Id 时间有序,枚举成本低)。评审建议把两个判断顺序对调("你是不是发起人"是关于调用者的事实,不该依赖实例状态)——不完全解决问题(`InstanceNotFound` 本身还是会泄露"存在与否"),但收窄了非发起人能探到的信息。管理内核场景优先级不高,记账留痕。
- **[P3]** `WfRuntimeModels.cs` 的 `WfInstanceActionInput` 命名过泛——台账「语义契约」表已经写明 Task 5(退回重提)是"发起人重填后重新提交,复用同一 `wf_instance` 行",那个接口至少需要 `{instanceId, variablesJson, selectedUserIdsByNode}`,和现在这个单字段 record 不兼容,将来大概率不会复用这个名字。评审建议现在免费改名成 `WfInstanceCancelInput`,省得 Task 12 `gen:api` 之后要跨两个前端模板改名。已记账,留给下次碰这个文件或专门做的时候处理,不阻塞本任务收口。
- **[P3]** `CancelInstanceOp.cs` 是"完结实例"这套动作序列(状态翻转+历史+FormBinder+排队通知)的第三份几乎相同的抄写(`CompleteTaskOp.RejectInstanceAsync`、`TakeTransitionOp.CompleteInstanceAsync` 是前两份),日后 Terminated 落地会有第四份。评审建议抽一个共享的 `CompleteInstanceAsync(ctx, instanceStatus, tokenStatus, ct)` 步骤,但这属于跨文件重构,不在本任务范围内单独做。
- **[P3]** `CancelInstanceOp.cs` 里查活跃任务只用 `Where(t => t.TokenId == ctx.Token.Id).FirstAsync()`,今天单 token 模型下正确,M3 并行网关落地多 token 后会静默漏清其它分支的任务——评审判定这是"以后要重新访问的代码",不是当前缺陷,记账留痕,不在本任务处理。
- **[P3]** `WfCancelTests.cs` 的 `Starter_can_cancel_before_anyone_approves` 只断言 actor+task 两个物理删除操作共同产生的副作用(todo 列表清空),没有分别断言两张表;也没有在撤销前先断言 todo 非空(建立基线)。这条测试仍然有区分力(不是套套逻辑),但可以更精确。留痕,不阻塞。
- **[P3]** `WorkflowErrorCode.cs` 里 Plan 阶段的注释写错了("48022 已被 Task 2 的 `UrgeNotAllowed` 占用")——`UrgeNotAllowed` 实际是 48021,48022 目前是个空洞(不影响功能,`CancelNotAllowed=48023` 依然唯一且不撞车)。纯记账纠错,不影响代码正确性,不阻塞。
- **[P3]** `WfNotifyContext.Status` 的 XML 注释仍然只提 `Approved`/`Rejected` 两种终态,没提 `Cancelled`(现在是第三种)。顺手更新注释即可,不阻塞。

### Task 3 复评记录(Round 12 code-reviewer 结果 1×P1 + 1×P2;Round 13 exec 修,已闭合)

- ~~**[P1]** `ResolveAdjacentApprovedUserIdsAsync` 查询缺 `InstanceId` 过滤,`wf_his_task` 无 `TokenId` 索引,热路径全表扫描风险。~~ ✅ Round 13 加 `h.InstanceId == ctx.Instance.Id` 条件收窄(纯收窄,零行为变化)。Opus 读码确认。
- ~~**[P2]** `rows.Where(h => h.NodeId == latestNodeId)` 收集节点全部历史访问而非最近一次,为未来退回/重入埋隐患。~~ ✅ Round 13 改成 `rows.TakeWhile(h => h.NodeId == rows[0].NodeId)`,取最近一次访问的连续区间。Opus 读码确认与修复方案完全一致。

两处均为纯防御性收紧、当前无环树模型下行为等价,评审与台账都明确"不强求转红"——独立复核用 build+全量套件(109/109,与基线一致,零回归)代替变异测试。6×P3 未修,记账留痕(见上一轮 Findings 存档,已移入台账早前记录,下次碰这块代码时顺手补,优先级最高的是 `WfSignMode.All` 测试覆盖)。

### 可选/留痕(不阻塞推进,时间充裕时顺手做,否则记账留到下次碰这块代码)

- **[P3]** multiLeader 豁免判断(`providerKey == ApproverProviderKeys.MultiLeader`)硬编码在 `EnterApprovalAsync` 调用点,消费者自注册的连锁语义 provider(`IApproverProvider` 文档明确允许)没有豁免入口,只能整段复制 `EnterApprovalAsync`。评审建议抽成 `protected virtual bool ShouldDedup(string providerKey)`。判断正确、范围不会被滥用,纯粹是可替换性风格问题。
- **[P3]** `DuplicateApproverSkipped` 历史事件 payload 里 `nodeId = Node.Id` 与该事件行自身的 `NodeId` 列重复,其它同类调用点(`TaskCreated`/`CcSent`)都不这么写,建议去掉 payload 里的 `nodeId` 字段。
- **[P3]** `WfAdjacentDedupTests.cs` 缺 `WfSignMode.All`(会签)覆盖——评审确认现状代码逻辑正确(`CompleteTaskOp.TryPassAsync` 的 `All` 分支只看剩余 `Pending` 是否为空,没有原始人数假设),纯粹是测试覆盖缺口。建议补 1 条:node1(any,[A])→node2(all,[A,B,C]),断言只跳过 A、B/C 都建了待办、B 单独批准不足以通过、B+C 都批准才通过。
- **[P3]** `WfAdjacentDedupTests.cs` 未覆盖"连续多节点在同一次 approve 请求内连环自动通过"(N/N+1/N+2 都只解析出同一个人 A 时会一次性全部跳过)——这是"比对对象只看 wf_his_task、被跳过的人不写任何行"这个设计的自然推论,值得一条测试钉住,但不是缺陷。
- **[P3]** `MultiLeader_node_is_exempt_from_dedup` 断言的是间接症状(`newAssigneeUserIds` 内容),评审验证过能杀掉"去掉豁免"这个变异,但更直接的锚点是断言"level-3 节点没有 `DuplicateApproverSkipped` 事件"。
- **[P3]** `EnterNodeOp.cs:296-297` — `adjacentApproved.Contains` 对 `users` 遍历了三次(:290 的 `Any` + :296/:297 的两个 `Where`),量级小、纯 cosmetic,顺手碰这段代码时可以合并。

### 已知起点(供 M2c/M3 参考,非本轮阻塞)——评审在回答"WfSignMode.All 交互"时指出:`WfEnums.cs:51` 的枚举注释预留了"比例票签",届时其分母必须是"去重后的实际办理人数",不能沿用去重前的原始解析人数,否则去重会悄悄挪动通过阈值。不在本轮处理,记录以防未来踩坑。

### Task 2 复评记录(Round 7 code-reviewer 结果 3×P2;Round 8 exec 修,已闭合)

- ~~**[P2]** `Services/WfTaskService.cs:177-180` — `FirstAsync()` 结果未判空直接解引用,裸 500。~~ ✅ Round 8 改成 `.FirstAsync() ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound)`,对齐 `WfInstanceService.RequireInstanceAsync` 写法。Opus 读码确认。
- ~~**[P2]** `Services/WfTaskService.cs:207` — `notifier.TaskUrgedAsync` 未 try/catch,与事务后派发的既有容错约定脱节。~~ ✅ Round 8 包 try/catch 吞异常,注释「通知失败不得影响已提交的历史写入」。Opus 读码确认与 `WorkflowEngine.cs:71-93` 风格一致。
- ~~**[P2]** `WfUrgeTests.cs` 缺 `toUserIds` 排除后为空集这条分支的测试。~~ ✅ Round 8 新增 `Urge_with_sole_pending_approver_being_starter_is_silent_noop`。Opus 亲手复跑变异(删掉 `if (toUserIds.Count == 0) return;`)→ 独立确认该用例红(`Assert.Empty() Failure: Collection was not empty`),复原后独立重跑全量 104/104,`git diff --check` 干净、无残留。

5×P3(未修,记账留痕,下次碰这块代码时顺手补):`Urge_on_unknown_task_returns_task_not_found` 只测「从未存在」未测「已完成」;happy-path 用例未解析 `PayloadJson` 校验载荷;`TaskNotFound`/`UrgeNotAllowed` 抛出未带上下文 dict(评审确认这是 service 层既有惯例,非缺陷);`WfTaskService` 主构造函数新增依赖缺 `<remarks>` 破坏性变更说明;`UrgeAsync` 无实例终态检查(M2b 范围内不可达,留给 M3 并行网关)。

### Task 1 复评记录(Round 3 code-reviewer,判定 REQUEST CHANGES;Round 4 exec 修,已闭合)

- ~~**[P1]** 通知在事务内发出,与 `NewAssigneeUserIds` 的「事务提交后读」设计脱节,真实 SignalR 场景下会推送不存在的待办/读到脏数据。~~ ✅ Round 4 改成 `PendingTaskAssignedNotifications`/`PendingInstanceCompletedNotification` 排队,`WorkflowEngine.ExecuteAsync` 在 `tran.IsSuccess` 后统一派发。Opus 亲手复跑变异(注掉 `CompleteTaskOp.cs` 的排队代码)→ `Sequential_promotion_notifies_task_assigned_for_next_approver` 独立确认红(`Expected: 2 / Actual: 1`),复原后 98/98 绿。
- ~~**[P2]** `CompleteTaskOp.TryPassAsync` 的 Sequential 晋级分支晋级下一位办理人时没通知,`multiLeader` 链第 2/3 级主管永远收不到推送。~~ ✅ 与 P1 同一轮修复,同一条独立复核的红测钉死。
- ~~**[P2]** `WfNotifyContext` 缺 `Status`,`InstanceCompletedAsync` 接收方分不清通过/拒绝。~~ ✅ Round 4 加 `required WfInstanceStatus Status`,两个完结出口各传各的值;executor 变异复跑(改成两处都传 `Approved`)→ `Reject_flow_notifies_instance_completed` 红,已复原。
- **[P3,未修,记账留痕]** `WfDefaultNotifier.cs` 的 `catch (Exception)` 仍静默不打日志。评审给了先例(`NoticeService.cs`/`SessionService.cs` 的可选 `ILogger<T>?` 尾参写法)但本轮判定非阻塞,时间优先修 P1/P2。**下次碰这个文件时顺手加**,别再等下一次评审重新点名。

## Tasks

> 顺序有意为之:先把 `IWorkflowNotifier` 这个后续多个任务都要用的 SPI 焊好,再按状态机复杂度从低到高做各动词,最后前端。每轮只做一项。

- [x] **1. `IWorkflowNotifier` 落地**:补方法 + 默认实现接 `IRealtimePublisher` + 接现有 3 个调用点(建任务/实例完结/转办)。见上面 `## Plan`。**Round 4 收口:通知改事务提交后统一派发,98/98 绿。**
- [x] **2. 催办(Urge)**:`WfTaskService.UrgeAsync`——仅发起人可催办,目标为当前 Pending 办理人(排除发起人自己),写 `WfHistory`(不新增 `WfHisTask`),`IWorkflowNotifier.TaskUrgedAsync` 派发;`POST task/urge`。**Round 8 收口:3×P2(空引用防护/notifier try-catch/空集分支测试)修完,104/104 绿。**
- [x] **3. 同一人相邻节点去重**:`CreateTaskDedupedAsync`——与最近一个已审批节点的完整办理人集合求交集,交集内的人跳过(记 `DuplicateApproverSkipped` 历史事件),剩余为 0 整节点自动通过,剩余 >0 只对剩余人建任务;multiLeader 豁免(跨级重现是其自身语义,不算重复)。**Round 13 收口:1×P1(查询缺 InstanceId 过滤,全表扫描风险)+1×P2(TakeWhile 替代 Where,防未来节点重入误并)修完,109/109 绿;过程中意外修复 3 个被本任务波及的 M2a 老测试回归(2 处测试 fixture 巧合触发 + multiLeader 语义真冲突)。**
- [x] **4. 撤销(Cancel)**:`CancelInstanceCmd`→`WorkflowEngine.BeginCancelAsync`(校验)→`CancelInstanceOp`(改状态+清活跃任务+历史+`FormBinder`+排队通知);`WfInstanceService.CancelAsync` 薄封装转发;`POST instance/cancel`。**Round 17 收口:2×P2(Instance 状态更新加 CAS 防并发竞态、补通知契约测试)修完,114/114 绿。**
- [ ] **5. 拒绝路由 + 主动退回**:`CompleteTaskOp.RejectInstanceAsync` 按 `node.Props.OnReject` 分流(`ToNode` 时不终止,token 回退 `RejectToNodeId` 重进);新增 `WfTaskAction.Return` 动词与 `ReturnTaskOp`(按 `ReturnPolicy` 定目标节点),退回后发起人重提=从 `start` 节点重走(复用 `StartAsync` 现有逻辑,不新建实例)。**本 loop 最复杂的一个任务,plan 阶段要单独厘清「退回」与「拒绝路由」是否共用同一个 token 回退底层函数**。
- [ ] **6. 委托(一次性)**:仿 `TransferTaskOp` 写 `DelegateTaskOp`,`WfTaskAction.Delegate`;`WfTaskController` 新增 `POST task/delegate`。
- [ ] **7. 超时 Job**:`EnterNodeOp.CreateTaskAsync` 按 `Node.Props?.Timeout?.Hours` 填真实 `DueTime`;新增 `WfTimeoutJob : IAdminJob`,扫 `DueTime < now` 的活跃 `wf_task`,按 `Timeout.Action` 分流(`Remind`→`IWorkflowNotifier`;`AutoPass`/`AutoReject`→等价调用 `CompleteTaskOp`;`Transfer`→等价调用 `TransferTaskOp`),写 `WfHistoryEventType.TimeoutFired`;`TryAddEnumerable` 注册。
- [ ] **8. 后端测试固化**:补 Task 2-7 每项的公开 HTTP 契约测试(仿 M2a Task 4 模式,独立 factory/账号/定义,每条变异验证区分力);`WorkflowReplaceabilityTests` 若 Task 1 已补第八件套,本任务复核不重复。
- [ ] **9. 抄送列表**:`Abstractions/IWfCcService.cs` + `Services/WfCcService.cs`(`PageMineAsync`/`MarkReadAsync`)+ `Controllers/WfCcController.cs`;前端新增 `views/workflow/cc/index.vue` + 路由 + 菜单种子(取号规则见 `skills/create-crud-backend.md` 的菜单取号约定)。
- [ ] **10. 我发起的 / 我已办的**:前端复用现成 `instance/page`(mine)与`task/done` 接口,新增两个列表页 + 路由 + 菜单;**不改后端**。
- [ ] **11. 流程图回放 + 实例列表按参与筛选**:详情页新增只读模式的树渲染(复用 `WfNodeTree.vue` 只读态),按 `wf_history` 的 `NodeEnter`/`NodeLeave` 序列高亮已走路径;管理员监控列表(新页或扩展现有 `instance` 列表)加发起人/办理人/抄送人筛选——数据范围仍不滤 `WfInstance`(§十三已定案),这里的"参与"筛选是业务过滤条件,不是数据权限。
- [ ] **12. `btnInfo` + 配置抽屉暴露新字段 + 验收**:节点按钮文案自定义(`WfNodeProps` 新增 `ButtonLabels`,JNPF 增量#2);配置抽屉暴露退回策略/委托/超时(守 ≤5 可见+折叠高级纪律);双模板 `gen:api`;真实浏览器走通退回/撤销/催办/抄送已读/我发起的/我已办的,留截图;跑齐 DONE-CONDITION。

## Log

### Round 1 — 任务1/plan — 动作:开新台账 `.loop/wf-m2b.md`,读 `docs/review/workflow-design-plan-2026-08-17.md` §十三 13.3、`CONTEXT.md` 工作流节「行为语义默认值」、`WfNode.cs`/`WfSchemaEnums.cs`/`WfEnums.cs`(确认 M2b 相关 schema 字段与枚举值均已预留、零消费方)、`IWorkflowNotifier.cs`(空接口)、`IRealtimePublisher.cs`(成品)、`CompleteTaskOp.cs`/`TransferTaskOp.cs`(现状与改动模板)、`WfInstanceService.cs`/`WfTaskService.cs`(确认「我发起的」「我已办的」后端契约已存在)。拆 12 个任务(见 `## Tasks`),Task 1(`IWorkflowNotifier` 落地)写了完整 Plan(6 决策点 + 9 步骤)。**未写任何产品代码**。
NEXT: Round 2 进 exec,把本轮 `## Plan` 整段 + 硬约束喂给 `Agent(executor)`。

### Round 2 — 任务1/exec — 动作:executor 实现 `IWorkflowNotifier` 落地(9 改动文件 + 2 新文件),4 个变异(移除三个调用点各一次 + 去掉 try/catch)逐个转红后还原,报 96/96 绿。Opus 独立复核:`git status`/`git diff --stat` 核对改动集与报告一致;亲手复跑最容易漏的一个(`TakeTransitionOp` 的 Approved 完结出口)——注释掉调用 → 独立确认 `Approve_flow_notifies_task_assigned_and_instance_completed:54` 红,复原后独立重跑 96/96,`git diff --check` 干净、无残留。
结果:exec 绿,阶段推进到 review。
NEXT: Round 3 进 review,`code-reviewer` 显式列 14 个改动/新增文件。

### Round 3 — 任务1/review — 动作:`code-reviewer` 复核 14 个文件,判定 **REQUEST CHANGES**(1×P1 通知在事务内发出与本仓「事务提交后读」的既有设计脱节,真实 `SignalRRealtimePublisher` 场景下有推送不存在的待办/读到脏数据的实证后果;2×P2 Sequential 晋级漏通知 + `WfNotifyContext` 缺 `Status`;2×P3 日志与测试收紧)。写了修复方案(改成 `PendingTaskAssignedNotifications`/`PendingInstanceCompletedNotification` 排队,`WorkflowEngine.ExecuteAsync` 事务提交后统一派发)。
结果:有 P1/P2,阶段回 exec。
NEXT: Round 4 把修复方案喂给 executor。

### Round 4 — 任务1/exec(修 Findings) — 动作:executor 按修复方案改 9 个文件,3 个新变异(移除 P1 修复代码 / 移除派发循环 / `Status` 传错值)逐个转红后还原,报 98/98 绿。Opus 独立复核:`git status`/`git diff --stat` 核对;读了 `WorkflowEngine.cs` 派发循环与 `CompleteTaskOp.cs` 的 Sequential 排队代码,确认实现与修复方案一致;亲手复跑 P1 那条变异(注掉 Sequential 晋级的排队代码)→ 独立确认 `Sequential_promotion_notifies_task_assigned_for_next_approver:113` 红(`Expected: 2 / Actual: 1`),复原后独立重跑 98/98,`git diff --check` 干净、无残留。P3(日志)留痕未修,记入 Findings 供下次顺手补。
结果:review 无遗留 P1/P2,Task 1 勾选收口,任务号 +1,阶段回 plan。
NEXT: Round 5 — Task 2(催办 Urge)plan。

### Round 5 — 任务2/plan — 动作:读现有代码(`WfTaskService.cs`/`IWfTaskService.cs`/`WfTaskController.cs`/`IWorkflowNotifier.cs`/`WfDefaultNotifier.cs`/`TransferTaskOp.cs`/`WfHistory.cs`/`WfEnums.cs`/`WorkflowErrorCode.cs`/`WfInstanceService.ListHistoryAsync`/`WorkflowNotifierTests.cs`),确认 `WfHistoryEventType` 无 Urge 值(需新增 `=10`)、`WorkflowErrorCode` 48021 空闲、`WorkflowNotifierTests.cs` 的 `CapturingWorkflowNotifier` 已有 `TaskUrgedCalls` 字段在等这个任务(只需把嵌套类从 `private` 改 `internal` 就能复用,不用新写 fake)。写了 6 决策点(谁能催办=仅发起人、目标排除自己、task 不存在复用 48006、新历史事件类型、`WfNotifyContext` 手填不经 ctx、返回值仿 `NoticeController` 的 `Result<bool>` 惯例)+ 9 步骤(含 5 条新测试 + 4 个区分力变异点)到 `## Plan`。**未写任何产品代码**。
NEXT: Round 6 进 exec,把本轮 `## Plan` 整段 + 硬约束喂给 `Agent(executor)`。

### Round 6 — 任务2/exec — 动作:executor 实现催办(7 个改动文件 + 1 新文件 `WfUrgeTests.cs`;另有计划外必要改动 `WorkflowReplaceabilityTests.cs`——扩 `IWfTaskService` 接口机械性破坏了里面的 `FakeTaskService`,补一个 `NotSupportedException` 桩,和该文件其它方法写法一致),4 个变异(去 starter 校验 / 去自排除过滤 / 去 TaskNotFound 判空 / 去历史写入)逐个转红后还原,报 103/103 绿(基线 98)。Opus 独立复核:`git status`/`git diff --stat` 核对改动集与报告一致,逐文件读 diff(`WfTaskService.cs`/`IWfTaskService.cs`/`WfTaskController.cs`/`WorkflowErrorCode.cs`/`WfEnums.cs`/`WorkflowReplaceabilityTests.cs`/`WfUrgeTests.cs`)确认实现与 Plan 一致;独立重新 build+跑全量 workflow 测试(103/103);亲手复跑区分力最强的一条变异(去掉 `.Where(userId => userId != callerUserId)` 自排除)→ 独立确认 `Urge_excludes_caller_from_target_list_when_caller_is_also_pending_approver` 红(`Expected: [approverId] / Actual: [starterId, approverId]`),复原后独立重跑全量 103/103,无残留。
结果:exec 绿,阶段推进到 review。
NEXT: Round 7 进 review,`code-reviewer` 显式列 8 个改动/新增文件。

### Round 7 — 任务2/review — 动作:`code-reviewer` 复核 8 个文件,判定 3×P2(`FirstAsync()` 结果未判空可裸 500;`notifier.TaskUrgedAsync` 未 try/catch,与 `WorkflowEngine.cs` 既有「通知失败不得影响已提交事务」约定脱节;`toUserIds` 排除后为空集这条分支零测试覆盖)+ 5×P3(均记入台账「可选/留痕」区)。评审自己给的整体标签是 COMMENT/非阻塞,但本 loop 规则明确 P2 一律视为阻塞——不采信该标签,按 P2 处理。Opus 独立复核:逐行重读 `WfTaskService.cs:177-220` 确认 3 条 P2 均属实(无判空、无 try/catch、`WfUrgeTests.cs` 确实没有空集分支用例)。写了「步骤 10」修复方案(判空+抛 `InstanceNotFound`、包 try/catch、补 1 条新测试)到 `## Plan`。
结果:有 P2,阶段回 exec(修 Findings)。
NEXT: Round 8 — 把 `## Plan` 步骤 10 + 硬约束喂给 `Agent(executor)` 修 3×P2。

### Round 8 — 任务2/exec(修 Findings) — 动作:executor 按步骤 10 修 3×P2(`WfTaskService.cs` 加 `.FirstAsync() ?? throw InstanceNotFound`、`notifier.TaskUrgedAsync` 包 try/catch;`WfUrgeTests.cs` 补 `Urge_with_sole_pending_approver_being_starter_is_silent_noop`),报 104/104 绿(基线 103)。Opus 独立复核:`git status`/`git diff --stat` 确认只碰 `WfTaskService.cs`+`WfUrgeTests.cs` 两个文件;读代码确认两处修复与 Findings 处置意见一致;独立 build+跑全量 workflow 测试(104/104);亲手复跑最强变异(删掉 `if (toUserIds.Count == 0) return;` 短路)→ 独立确认新测试红(`Assert.Empty() Failure: Collection was not empty`),复原后独立重跑 104/104,无残留。3×P2 全部闭合。Task 2 勾选收口(参照 Task 1 Round 4→5 先例,未单独再起一轮 code-reviewer re-review,由 Opus 亲自核验修复内容与 Findings 处置意见完全一致后直接收口)。
结果:Task 2 完成,任务号 +1(→3),阶段回 plan。
NEXT: Round 9 — Task 3(同一人相邻节点去重)plan。

### Round 9 — 任务3/plan — 动作:读 `EnterNodeOp.cs`(确认 `EnterApprovalAsync`→`CreateTaskAsync` 的裸调用点、`ApplyNobodyAsync` 的 `AutoPass` 分支是"整节点直接推进"的现成模板)、`CompleteTaskOp.cs`(`WfHisTask` 字段形状,确认永不物理删除、可长期查)、`WfEnums.cs`(`WfHistoryEventType` 11 起空闲)、`CONTEXT.md:39` 与台账「语义契约」表原文。核心判断:比对"最近一次已 Approve 节点"的**完整**办理人集合(同 `TokenId`+同 `NodeId` 的全部 `Approve` 行,覆盖会签多人各自一行的情况),而不是全部历史交集(会误伤 A→B→A 这种"隔了不同人节点"的非相邻重复)。设计 2 个新 `protected virtual` 步骤:`ResolveAdjacentApprovedUserIdsAsync`(查询)+ `CreateTaskDedupedAsync`(去重派发,交集为空直接原逻辑;剩余为 0 整节点自动通过仿 `ApplyNobodyAsync.AutoPass`;剩余 >0 只对剩余人建任务),接线进 `EnterApprovalAsync` 唯一调用点。写了 7 决策点 + 8 步骤(4 条新测试:部分去重+整节点自动通过+非相邻不误伤+顺序会签 Sort 保序,2 个区分力变异点)到 `## Plan`。**未写任何产品代码**。
NEXT: Round 10 进 exec,把本轮 `## Plan` 整段 + 硬约束喂给 `Agent(executor)`。

### Round 10 — 任务3/exec(被打断) — 动作:executor 完成步骤 1-4(`WfHistoryEventType.DuplicateApproverSkipped=11`、`ResolveAdjacentApprovedUserIdsAsync`、`CreateTaskDedupedAsync`、接线),跑全量套件发现 3 个 M2a 老测试红,开始诊断时(`git stash` 隔离自己的改动重跑)因会话额度耗尽被中断,stash 未弹回。Opus 独立处理:`git stash list` 发现残留 stash,`git stash pop` 找回(确认 stash 内容 = `EnterNodeOp.cs`+`WfEnums.cs` 的 Task1+2+3 累计改动,而非只有 Task 3 自己的),`git status`+`dotnet build` 确认零丢失、编译干净。独立重跑全量套件复现同样 3 个红:`WorkflowMultiLeaderSnapshotTests.Cc_node_multi_leader_resolution_uses_snapshot_not_live_director`(`createdTaskId` 变 null)、`WorkflowM2BranchRegressionTests.Arm_with_condition_...`(`instanceStatus` 提前变 `Approved`)、`WorkflowMultiLeaderSnapshotTests.Different_levels_keep_exact_filtered_chains_...`(顺序会签第一位从 level2Id 错变成别的 Id)。逐一读了 3 个失败用例的完整模型定义(`BranchModel`/`GateThenCcThenApprovalModel`/`TwoLevelMultiLeaderModel`),确诊根因分两类(测试 fixture 巧合触发 vs multiLeader 语义真冲突,细节见 `## Plan` Round 10 复盘段)。写了 7 条追加修复步骤(a-g)到 `## Plan`,阶段留在 `exec`(未绿)。
结果:exec 未完全绿(3 个既有测试回归,新测试文件也还没写),阶段留 `exec`,记 blocker 于 `## Plan`(不单独开 Findings,因为还没到 review 阶段)。
NEXT: Round 11 继续 exec,把 `## Plan`(含 Round 10 复盘追加的 a-g)喂给新一轮 `Agent(executor)`,明确步骤 1-4 已完成不用重做。

### Round 11 — 任务3/exec(后台 executor 静默中断,Opus 接手核验) — 背景:压缩前的会话里起了一个后台 `Agent(executor)` 执行 Round 10 追加的 a-g 步骤;本轮续聊时发现它已经把代码写完(multiLeader 豁免、两处测试 fixture 修复、`WfAdjacentDedupTests.cs` 5 条新用例全部就位),但**没有发回完成报告就没了动静**——诊断过程:独立跑全量套件先看到 1 个红(`MultiLeader_node_is_exempt_from_dedup`,`newAssigneeUserIds` 第一位对不上),差点误判为代码有 bug;再次读文件发现 `EnterNodeOp.cs` 带着一行英文注释 `// MUTATION-TEST-3: temporarily remove the multiLeader exemption.`——即那个红是撞上了后台 executor 自己在做变异测试时的中间状态,不是真回归。之后连续观察:文件 4 分钟无变化、`dotnet` 进程列表里没有新起的测试进程、`TaskList`/`state_list_active` 都查不到它的句柄——判定为静默中断(和 Round 10 的 `git stash` 中断是同一类故障,这次卡在"变异测试"环节),但这次拿不到 Agent 引用没法用 `SendMessage` 续上,只能 Opus 直接接手完成 exec 阶段剩余的独立核验。**动作**:确认 `EnterNodeOp.cs` 当时处于"豁免逻辑已恢复"的正常状态(不是卡在半吊子的变异里);`dotnet build -c Release` 干净;全量套件独立重跑 109/109(基线 104+新增 5,与 Plan 步骤 g 的预期完全一致);`git diff --stat` 核对 18 个改动/新增文件、`grep` 确认无 TODO/FIXME/`.Skip(`/`NotImplementedException`;亲手做一次变异复核——移除 `EnterApprovalAsync` 里的 multiLeader 豁免判断(`providerKey == ApproverProviderKeys.MultiLeader` 分支),独立确认 `WfAdjacentDedupTests.MultiLeader_node_is_exempt_from_dedup` 与 M2a 老测试 `WorkflowMultiLeaderSnapshotTests.Different_levels_keep_exact_filtered_chains_without_granting_higher_level_approval` 双双转红(两者的 `newAssigneeUserIds`/`firstLevel3Actors` 断言第一位都从 level2Id 错变成 level3Id,与「去掉豁免会让 level2Id 被误当相邻重复滤掉」的预期完全吻合);复原豁免代码后独立重跑全量套件 109/109,`git diff --check` 干净无残留。
结果:exec 独立核验通过(3 个此前红的 M2a 老测试全部转绿、5 条新用例全部通过、multiLeader 豁免的区分力用亲手变异验证过),阶段推进到 review。
NEXT: Round 12 进 review,`code-reviewer` 显式列出本任务改动的 18 个文件(`Entities/WfEnums.cs`、`Engine/Operations/EnterNodeOp.cs`、`WorkflowM2BranchRegressionTests.cs`、`WorkflowMultiLeaderSnapshotTests.cs`、新增 `WfAdjacentDedupTests.cs`,以及 Task 1/2 遗留在同批 diff 里的其余文件——review 范围只聚焦 Task 3 本轮实际改动的这 5 个)。

### Round 12 — 任务3/review — 动作:先 `git status --short`/`git diff --stat` 确认本任务实际改动范围只有 5 个文件(其余 13 个是 Task 1/2 遗留在同批未提交 diff 里的,不属于本轮)。`code-reviewer` 复核这 5 个文件,判定 1×P1(`ResolveAdjacentApprovedUserIdsAsync` 的 `wf_his_task` 查询缺 `InstanceId` 过滤——该表只有 `InstanceId`/`UserId` 两个索引、无 `TokenId` 索引,查询跑在每次进入审批节点的引擎事务热路径上,永不清理的表上是无索引全表扫描)+ 1×P2(`Where(h => h.NodeId == latestNodeId)` 收集的是该节点全部历史访问而非最近一次访问,当前无环树模型下等价,但未来退回/重入若让同一节点被同一 token 二次进入会误并旧访问的批准人)+ 6×P3(multiLeader 豁免判断建议抽 `protected virtual` 谓词/历史事件 payload 冗余字段/`WfSignMode.All` 测试覆盖缺口/连环自动通过未钉住测试/`MultiLeader` 豁免测试断言可以更直接/三次遍历可合并)。同时给出 7 个聚焦问题的完整独立验证(相邻节点解析正确性、豁免范围不可被滥用、自动通过路径与 `ApplyNobodyAsync` 语义一致且无重复处理、被跳过用户不会因此丢失实例可见性、两个 M2a fixture 改动没有削弱原断言、5 条新测试均有区分力、`WfSignMode.All` 交互无隐藏人数假设),给了 REQUEST CHANGES(仅 P1 阻塞,P2 建议顺带修)。Opus 独立复核:读了 `WfHisTask.cs` 的 `[SugarIndex]` 声明(确认只有 InstanceId/UserId 两个索引)与 `ResolveAdjacentApprovedUserIdsAsync` 的查询代码,确认 P1/P2 均属实。写了「步骤 11」修复方案(2 处收紧查询范围,均为纯防御性改动、当前模型下行为等价)到 `## Plan`,6×P3 记入「可选/留痕」区。
结果:有 P1(+建议顺带的 P2),阶段回 exec(修 Findings)。
NEXT: Round 13 — 把 `## Plan` 步骤 11 + 硬约束喂给 `Agent(executor)` 修 P1+P2。

### Round 13 — 任务3/exec(修 Findings) — 动作:executor 按步骤 11 改 `EnterNodeOp.cs` 的 `ResolveAdjacentApprovedUserIdsAsync`:①查询加 `h.InstanceId == ctx.Instance.Id` 过滤(纯收窄、命中现有 `idx_wf_his_task_instance` 索引);②`rows.Where(h => h.NodeId == latestNodeId)` 改成 `rows.TakeWhile(h => h.NodeId == rows[0].NodeId)`(已按 `Id` 倒序,取连续区间而非全部历史)。报 109/109 绿(与基线一致)。Opus 独立复核:`git status`/`git diff` 确认只改了这一个文件、且改动内容与 Plan 步骤 11 的修复方案逐字一致;独立 `dotnet build`+跑全量 workflow 测试(109/109,零回归)。两处修复都是纯防御性收紧,P1 是性能问题不是正确性问题(当前测试数据量下天然无法构造区分红的场景)、P2 在当前无环树模型下新旧写法结果恒等——评审与台账都已明确不强求转红,故以独立 build+全量套件替代变异测试作为核验依据。P1/P2 全部闭合,Task 3 勾选收口(参照 Task 1/2 先例,未单独再起一轮 code-reviewer re-review,由 Opus 亲自核验修复内容与 Findings 处置意见完全一致后直接收口)。
结果:Task 3 完成,任务号 +1(→4),阶段回 plan。
NEXT: Round 14 — Task 4(撤销 Cancel)plan。

### Round 14 — 任务4/plan — 动作:读 `WfInstanceService.cs`(全文,`StartAsync` 是"薄封装转发给引擎"的现成模板)、`WfCommands.cs`(`StartInstanceCmd`/`CompleteTaskCmd`/`TransferTaskCmd` 三个 Cmd 字段形状)、`WorkflowEngine.cs`(全文,`ExecuteAsync` 的 `command switch`→`BeginXxxAsync` 分工:加载+校验在 `BeginXxxAsync`,真正状态变更交给 `IWfOperation`)、`WfEnums.cs`(确认 `WfInstanceStatus.Cancelled=4`/`WfTokenStatus.Cancelled=3` 早已预留,零消费方)、`CompleteTaskOp.RejectInstanceAsync`(实例终结标准动作序列:`Instance.Status`→`Token.Status`→`AppendHistoryAsync(InstanceCompleted)`→`FormBinder.OnInstanceCompletedAsync`→排队通知,这是 Cancel 该抄的模板)、`CompleteTaskOp.CloseTaskAsync`(活跃待办清理:标 Skipped 后物理删)、`WfInstanceController.cs`(`Start` 用 body DTO 惯例)、`WorkflowErrorCode.cs`(48023 起空闲,注意到 `TransferTargetInvalid`/`NobodyBlocked` 用一码多 `reason` 区分子情形的既有惯例)。核心判断:撤销要动 `Instance.Status`/`Token.Status`/活跃任务,和 Approve/Reject 属同一类"终结实例"操作,应复用引擎既有的 `FormBinder` 回调 + 事务后通知派发机制,不能像 Urge 那样绕开引擎手搓。设计新增 `CancelInstanceCmd`,`WorkflowEngine` 加 `BeginCancelAsync`(加载+5 项校验:实例存在/`Running`/发起人/无 `Approve` 历史/`Token` 存在)+ 新建 `CancelInstanceOp`(改状态+清活跃任务+历史+`FormBinder`+排队通知);校验失败复用一码多 reason(`CancelNotAllowed=48023`)。写了 6 决策点 + 11 步骤(4 条新测试:发起人可撤销/非发起人拒绝/已批准后拒绝/非 Running 拒绝,3 个区分力变异点)到 `## Plan`。**未写任何产品代码**。
NEXT: Round 15 进 exec,把本轮 `## Plan` 整段 + 硬约束喂给 `Agent(executor)`。

### Round 15 — 任务4/exec — 动作:executor 实现撤销:`CancelInstanceCmd`(`WfCommands.cs`)+ `WorkflowEngine.BeginCancelAsync`(实例存在→`Running`校验→发起人校验→无 `Approve` 历史校验→`Token` 加载→`version`/`model` 加载→建 ctx→`agenda.Plan(new CancelInstanceOp())`)+ 新建 `CancelInstanceOp.cs`(改 `Instance.Status`/`Token.Status`→清活跃任务全部 actor(标 Skipped 后物理删)+删 `WfTask`→`AppendHistoryAsync(InstanceCompleted)`→`FormBinder.OnInstanceCompletedAsync`→排队 `PendingInstanceCompletedNotification`)+ `IWfInstanceService`/`WfInstanceService.CancelAsync`(薄封装转发引擎)+ `WfInstanceActionInput` DTO + `WfInstanceController` 的 `POST cancel` 端点 + `WorkflowReplaceabilityTests.FakeInstanceService` 机械桩 + 新增 `WfCancelTests.cs` 4 条用例。3 个变异(去清活跃任务/去发起人校验/去无 Approve 历史校验)逐个转红后还原,报 113/113 绿(基线 109)。Opus 独立复核:`git status`/`git diff --stat` 核对改动集(8 个已跟踪文件改动 + 2 个新文件,209 行新增)与报告一致;读了 `CancelInstanceOp.cs` 全文与 `BeginCancelAsync` 的完整校验序列,确认与 Plan 逐字一致;独立 `dotnet build`+跑全量 workflow 测试(113/113);亲手复跑「无 Approve 历史」这条校验(语义上最容易和"发起人校验"混淆、也是 Plan 里唯一涉及跨表查询的判定)——删掉 `BeginCancelAsync` 里的该校验块 → 独立确认 `Cannot_cancel_after_someone_approved` 红(`Assert.Equal() Failure: Expected: 48023 / Actual: 0`),复原后独立重跑全量 113/113,`git diff --check` 干净、无残留。
结果:exec 独立核验通过,阶段推进到 review。
NEXT: Round 16 进 review,`code-reviewer` 显式列出本任务改动的文件(`WorkflowErrorCode.cs`/`WfCommands.cs`/`WorkflowEngine.cs`/新增 `CancelInstanceOp.cs`/`IWfInstanceService.cs`/`WfInstanceService.cs`/`WfRuntimeModels.cs`/`WfInstanceController.cs`/`WorkflowReplaceabilityTests.cs`/新增 `WfCancelTests.cs`,共 8 改动 + 2 新增)。

### Round 16 — 任务4/review — 动作:先 `git status --short`/`git diff --stat` 确认本任务实际改动范围只有 10 个文件(其余是前序任务遗留在同批未提交 diff 里的)。`code-reviewer` 复核这 10 个文件,判定 2×P2 + 6×P3。P2 之一:`CancelInstanceOp.cs` 的 `Instance`/`Token` 状态翻转没有 CAS——撤销不像 Approve/Reject 那样经任务级 `WfTask.Version` CAS 间接获得保护(撤销压根不认领任务),双击撤销会让 `FormBinder`/通知触发两次,撤销撞第一次批准(快照隔离下)会让 `wf_instance.Status=Cancelled` 与 `wf_his_task` 里的 `Approve` 行自相矛盾——"无 Approve 历史"这条校验只是读,防不住这个竞态。P2 之二:`WfCancelTests.cs` 对 `FormBinder.OnInstanceCompletedAsync`/事务后通知在撤销路径上零覆盖,删掉调用整套件仍然绿。另给了 5、6、7 三个聚焦问题的独立验证(校验顺序无信息泄露之外的正确性问题、活跃任务查询当前模型下无跨实例误命中风险、一码多 reason 与本仓既有惯例一致)。Opus 独立复核:读了 `CancelInstanceOp.cs` 全文确认状态更新确实是无条件 `UpdateColumns`(不带 `Status==Running` 的 `Where`);grep 确认 `OnInstanceCompletedAsync`/`InstanceCompletedCalls` 在 `WfCancelTests.cs`/`WorkflowNotifierTests.cs` 的撤销相关用例里零出现。两条 P2 均属实。写了「步骤 12」修复方案(`Instance` 状态更新改条件更新 CAS,失败抛既有 `InstanceStatusConflict`;补一条通知契约测试到 `WorkflowNotifierTests.cs`,与该文件既有 Approve/Reject 完结用例放一起)到 `## Plan`,6×P3 记入「可选/留痕」区(其中 `WfInstanceActionInput` 改名与 48022 记账纠错两条标记为"免费、值得顺手做")。
结果:有 2×P2,阶段回 exec(修 Findings)。
NEXT: Round 17 — 把 `## Plan` 步骤 12 + 硬约束喂给 `Agent(executor)` 修 2×P2。

### Round 17 — 任务4/exec(修 Findings) — 动作:executor 按步骤 12 改 `CancelInstanceOp.cs`(`Instance` 状态更新从无条件 `Updateable(entity).UpdateColumns(...)` 改成条件更新 `Updateable<WfInstance>().SetColumns(...).Where(i => i.Id == ... && i.Status == Running)`,`claimed != 1` 抛既有 `InstanceStatusConflict`;审计字段 `UpdateTime`/`UpdateUserId` 显式手填——读了 `SqlSugarRepository.SoftDeleteCoreAsync` 的同类先例确认 `SetColumns` 会绕过整对象更新的审计 AOP,`UpdateUserId` 取 `ctx.Instance.StarterUserId`,因为 `BeginCancelAsync` 已校验 caller 必为 starter)+ 在 `WorkflowNotifierTests.cs` 新增 `Cancel_flow_notifies_instance_completed`(与既有 Approve/Reject 完结用例同文件归类,复用现成 `CapturingWorkflowNotifier` 脚手架)。报 114/114 绿(基线 113)。CAS 修复本身无法用当前单线程 xUnit 套件自然构造并发红测,executor 按台账处置意见明确说明改用"读码逐行核对 `CompleteTaskOp.cs` CAS 先例 + 全量回归零破坏"替代,未虚报变异测试结果。Opus 独立复核:`git status` 确认只碰这两个文件(均为本轮新建、尚未提交,状态一致);读了 `CancelInstanceOp.cs` 全文,确认 CAS 实现(`Where` 条件、失败路径、审计字段处理)与「步骤 12」修复方案逐字一致;独立 `dotnet build`+跑全量 workflow 测试(114/114);亲手复跑通知测试的变异——临时删掉 `ctx.PendingInstanceCompletedNotification = ...` 赋值块 → 独立确认 `Cancel_flow_notifies_instance_completed` 红(`Assert.Single() Failure: The collection was empty`),复原后独立重跑全量 114/114,`git diff --check` 干净、无残留。2×P2 全部闭合。Task 4 勾选收口(参照前几个任务先例,未单独再起一轮 code-reviewer re-review,由 Opus 亲自核验修复内容与 Findings 处置意见完全一致后直接收口)。
结果:Task 4 完成,任务号 +1(→5),阶段回 plan。
NEXT: Round 18 — Task 5(拒绝路由 + 主动退回)plan——本 loop 最复杂的一个任务,plan 阶段要单独厘清「退回」与「拒绝路由」是否共用同一个 token 回退底层函数。

### Round 18 — 任务5/plan — 动作:读了 `CompleteTaskOp.cs`(全文,`RejectInstanceAsync` 当前硬编码终止、`node` 参数完全没用)、`Schema/WfNode.cs`+`WfSchemaEnums.cs`(`WfNodeProps.OnReject`/`RejectToNodeId`/`ReturnPolicy`/`ReturnToNodeId` 精确类型,`WfRejectAction{Terminate,ToNode}`/`WfReturnPolicy{Prev,Any,Node}`)、`WfHisTask.cs`/`WfHistory.cs`/`WfToken.cs`(确认无专门"目标节点"列,记进 `WfHistory.PayloadJson`)、`TransferTaskOp.cs`(CAS 认领模板)、`WorkflowEngine.cs`(全文,`BeginStartAsync`/`BeginCompleteAsync`/`BeginTransferAsync` 三种分工 + 4 个可复用 `protected virtual` helper)、`WfInstanceService.StartAsync`、docs 设计草案「行为语义默认值」原表(比台账语义契约表更原始,锁定"退回后重提默认从头重走,不管 ReturnPolicy 解析出哪个节点"这条关键语义,解开了此前"目标节点"与"从头重走"看似矛盾的疑惑)。核心判断(回答台账自己提出的问题——两者是否共用同一个回退函数):**不共用**。拒绝路由(`OnReject=ToNode`)直接复用 `EnterNodeOp` 自动继续,不新增 Cmd/Op,只是 `RejectInstanceAsync` 内部行为分叉;主动退回(`WfTaskAction.Return`)是全新的 `ReturnTaskCmd`+`BeginReturnAsync`+`ReturnTaskOp`,关闭当前任务后**不**自动继续(和拒绝路由的关键区别),需要发起人显式调用**第三套全新引擎命令**——`ResubmitInstanceCmd`+`BeginResubmitAsync`(`BeginStartAsync` 的翻版,作用在已有 `wf_instance`/`wf_token` 行上,不插新实例)。按 A(拒绝路由,最小,独立可跑绿)→B(主动退回)→C(重提)拆分,写了 8 个决策点表格 + 25 个编号步骤(A:2 条新测试;B:3 条新测试;C:4 条新测试,共 9 条;5 个区分力变异点)+ 5 条陷阱记录(`_ = node;` 忘删、`SnapshotLeaderChainsAsync` 改签名后调用点必须同步改、Prev 退化到 start 用 ctx 里已有的 model 不要重查 DB、`WfInstanceActionInput` 改名不影响前端 schema.d.ts、Resubmit"无活跃任务"校验和 Cancel"无 Approve 历史"校验目的不同别抄错)到 `## Plan`。**未写任何产品代码**。
NEXT: Round 19 进 exec,把本轮 `## Plan` 整段(A/B/C 三块)+ 硬约束喂给 `Agent(executor)`,提醒任务规模大、按 A→B→C 顺序推进、允许跨轮完成、诚实汇报进度而非虚报全部完成。
