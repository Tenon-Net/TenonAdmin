# Loop: TenonAdmin.Workflow M2a 排他分支

## GOAL

在 M1(已收口,四项 CI 全绿)基础上做 **M2a 分支**:排他分支 + 结构化条件 + 条件编辑器 + 会签/或签在设计器暴露。范围与定案见 `docs/workflow/workflow-design-plan-2026-08-17.md` **§十三**(M2a/M2b 切分、三条写死)。

**不做 M2b**(退回/撤销/委托/催办/超时 Job/去重/SignalR/抄送列表/回放)、**不做 M3**(动态表单/并行/webhook/加减签/React port)。不抽 `web/` 与 `web-react/` 共享层——M2a 只动 `web/`。

## DONE-CONDITION

- 本账本 `## Tasks` 全部打勾
- `dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` 绿
- `cd web && npm run typecheck && npm run lint` 绿,`npx vitest run src/workflow/` 绿
- 真实浏览器走通「报销分支流程」两条臂(见 Task 8),留截图证据

> ⚠️ **验证命令已于 Round 10 修正,别用回旧的**。原来写的是 `--filter "FullyQualifiedName~Workflow"`,**它根本不匹配 `WfConditionEvaluatorTests` / `WfModelIndexTests` / `WfBranchPublishValidationTests`** —— `Workflow` 里没有相邻的 `Wf`(W-o-r-k-f-l-o-w)。旧命令只跑 13 条(`WorkflowM1RegressionTests` + `WorkflowReplaceabilityTests` + `LeaveWorkflowE2ETests`),**Task 1 辛苦钉死的 47 条一条都没进 DONE-CONDITION**。
> 也**不要**用 `~Wf|~Workflow`:VSTest 的 `~` 大小写不敏感,`Sno**wf**lake` 会被捞进来,多带 7 条无关内核用例(实测 78)。
> **正确写法是 `~Tests.Wf|~Workflow`,实测恰好 71 条** = 47 求值器 + 8 树索引 + 3 发布校验 + 13 既有工作流。

## Status

- 轮次: 47
- max: 50
- 状态: 已完成
- 当前任务: 8
- 当前阶段: audit
- 上一轮: Round 47 — 最终审计,**8/8 任务完成，DONE-CONDITION 全部有证据**
- 下一步: 无；M2a 已完成，保留未提交 WIP 供用户检查

> 📌 **Claude 交接摘要**:要快速了解本轮交付、证据、已知 P3 与安全偏差，先读 [`wf-m2a-handoff.md`](./wf-m2a-handoff.md)；本台账仍是完整过程记录。

> ✅ **Round 22 已修复的 P1 摘要(历史机理保留供复评)**:`FilterEnabledAsync` 是**压缩式**过滤(`ordered.Where(set.Contains)`,被过滤者不留空位),于是快照下标**不再等于第几级主管**;而旧实现的 `snapshot.Take(level)` 恰恰依赖「下标即级数」。
> `starter→l1→l2→l3` 且 `l1` 停用时:`snapshot(max=3) = [l2,l3]` → **`level=2` 的节点拿到 `[l2,l3]`,把第 3 级主管挂成了审批人**;走实时(旧行为)只得 `[l2]`。
> **触发需 ≥2 个 `multiLeader` 节点且 level 不同 + 链中有人停用** —— 离职在 HR 场景是停用不是删除,**常见**。这不是「组织调整的代价」,是**模型从未授权的人拿到审批权**。
> **是我 Round 17 的定案写错了**:「链有序 1..N,截断即精确」在有过滤时不成立。评审的 M2/M3 双双 84/84 存活也印证**这条定案根本没测试守着**(现有 5 条用例全是单节点 level=2,`maxLevel` 恒等于节点 level,`Take` 恒为 no-op)。
> **处置:不打补丁,直接删掉这个不变式** —— 改成按 level 存快照(`{"2":[...],"3":[...]}`),provider 按自己的 level 取,`Take` 整个不要了。理由见 `## Plan` 步骤 9。

> ✅ **Opus 本轮亲手复验的一条**:`DeserializeLeaderChain` 的 `if (IsNullOrWhiteSpace(json)) return null;` 改成 `return [];` → **84/84 全绿,变异存活**。即「老实例回退实时」在**引擎层**零覆盖,而该变异的现实后果正是定案表点名的事故:老实例 → `[]` → provider 当空快照 → `ApplyNobodyAsync` → 默认 **AutoPass** → **在途单审批节点被静默跳过**。已还原,84/84。

> ✅ **Round 18 的 blocker 已关闭**。Round 20 新增 `Cc_node_multi_leader_resolution_uses_snapshot_not_live_director`,模型用**带 gate 的形状**(`start → approval#gate → cc(multiLeader,level=2) → approval#final`),让 cc 在「改 `DirectorId` 之后」才被进入。
> **Opus 亲手复跑那个曾经存活的变异**:只删 `EnterCcAsync` 的 `LeaderChain = ctx.LeaderChain,`(`grep -c` 从 2 变 1)→ **83/84 变红**,红在该新用例,`Assert.Contains() Failure — Collection: [first, decoy], Not found: second`。已还原(`grep -c` = 2),重跑 **84/84**,`git status` 22 行不变。
> **两个调用点现已各自被独立钉住**:approval 侧 → `Snapshot_survives_director_change_between_start_and_multi_leader_node_entry`;cc 侧 → 本轮新增这条。

> **Task 3 实际改动文件**(review 阶段把这份清单显式喂给评审方;共 **9 个物理文件**):
> - `backend/src/TenonAdmin.Workflow/Entities/WfInstance.cs`(+1 列 `LeaderChainJson`)
> - `backend/src/TenonAdmin.Workflow/Abstractions/IApproverProvider.cs`(`ApproverResolveContext` +1 字段 `LeaderChainByLevel`)
> - `backend/src/TenonAdmin.Workflow/Engine/WfExecutionContext.cs`(+1 字段,**非 `required`**)
> - `backend/src/TenonAdmin.Workflow/Schema/WfModelIndex.cs`(+`Nodes` 枚举)
> - `backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs`(`ResolveLeaderLevels` + `SnapshotLeaderChainsAsync` + `DeserializeLeaderChainsByLevel` + 三处 ctx 填充;**构造函数签名未动**)
> - `backend/src/TenonAdmin.Workflow/Providers/BuiltInApproverProviders.cs`(`MultiLeaderApproverProvider` 优先用快照)
> - `backend/src/TenonAdmin.Workflow/Engine/Operations/EnterNodeOp.cs`(**两处** `ApproverResolveContext` 都传 `LeaderChainByLevel`)
> - `backend/tests/TenonAdmin.Tests/WorkflowMultiLeaderSnapshotTests.cs`(新增,现 9 条)+ `WfModelIndexTests.cs`(+1 条 `Nodes` 用例)
>
> **不在 Task 3 范围(评审别评)**:Task 1 的求值器三件套、Task 2 的分支执行 12 个文件(均已收口)、`docs/review/...`、`.loop/wf-m2a.md`。

> ⚠️ **Round 19 修掉了步骤 8 自己的假绿隐患(写 prompt 前发现)**:原步骤 8 让模型写成 `start → cc(multiLeader) → approval`,但 **cc 节点在发起事务内就解析完了** —— 那一刻快照与实时链完全相同,「发起后改 `DirectorId`」根本影响不到它,**删掉 `LeaderChain` 也会全绿**,等于再造一条假绿用例。
> 已改为**带 gate 的形状**:`start → approval#gate(initiator 自批,用来暂停)→ cc(multiLeader, level=2)→ approval#final`,让 cc 在「改 `DirectorId` 之后」才被进入,走快照 = `[first, second]`、走实时 = `[first, decoy]`,**两者不同才有区分力**。
> 这正是本 loop 三次栽跟头后立的那条检查项(**「我选的这组输入,能让正确实现和错误实现给出不同答案吗?」**)**第一次在下笔前就拦住了问题**,而不是等实测才发现。

> ✅ **Round 19 顺手核实的两条事实**(下轮直接用,别重查):
> - `ResolveMaxLeaderLevel` **没有按节点类型过滤**,只看 `node.Props?.Assignee?.Provider == "multiLeader"` —— 所以 **cc 节点上的 multiLeader 也会被算进 `maxLevel`**,快照会正常生成,用 cc 节点做测试是可行的。
> - `WfEngineResult` **确实有 `NewCcUserIds`**(`Engine/WfCommands.cs`,注释「本次新抄送接收人」),信封路径 `data.newCcUserIds` —— 断言抄送名单用它最省事。

> 🚨 **BLOCKER(Round 18 实测)**:步骤 5 要求「`EnterApprovalAsync` 与 `EnterCcAsync` **两处都**传 `LeaderChain`」,executor **两处都写了、实现是对的**,但只有 approval 那处被钉住:
> - 删 `EnterApprovalAsync` 的 `LeaderChain = ctx.LeaderChain,` → **82/83 变红**(`Snapshot_survives_director_change_between_start_and_multi_leader_node_entry`)✅
> - 删 `EnterCcAsync` 的那一处 → **83/83 全绿** ❌
>
> **这是 Round 13 的同款,而且是在我已经把它写成检查项之后又发生一次** —— 步骤 5 的判据虽然写了「漏任一处,步骤 6 的对应用例要能抓到」,但**步骤 6 的四条用例里没有一条走 cc 节点**,判据与用例对不上。**根因仍是我(Opus):判据点名了两个出口,用例只覆盖了一个。**
> 失败影响面比 approval 那侧轻(cc 只是抄送、不建待办不阻塞),但仍与定案相悖:组织调整后抄送名单会按实时链算,而不是快照。**修法是一条用例,成本低,按 Round 13 的先例回 exec 补。**

> **Task 3 预期改动文件**(review 阶段把这份清单显式喂给评审方):
> - `Entities/WfInstance.cs`(+1 列 `LeaderChainJson`)
> - `Abstractions/IApproverProvider.cs`(`ApproverResolveContext` +1 字段 `LeaderChain`)
> - `Engine/WfExecutionContext.cs`(+1 字段,**非 `required`**)
> - `Schema/WfModelIndex.cs`(暴露 `Nodes` 枚举)
> - `Engine/WorkflowEngine.cs`(`BeginStartAsync` 算快照并落库 + 另两处反序列化;**构造函数签名不动**)
> - `Providers/BuiltInApproverProviders.cs`(`MultiLeaderApproverProvider` 优先用快照)
> - `Engine/Operations/EnterNodeOp.cs`(**两处** `ApproverResolveContext` 都传 `LeaderChain`)
> - 测试:`WfModelIndexTests.cs`(+1)、`WorkflowM2BranchRegressionTests.cs` 或新建 `WorkflowMultiLeaderSnapshotTests.cs`(+4)

> **Task 2 交付物**(已收口,后续任务只读不改;12 个文件):
> - `Schema/WfModelIndex.cs`(新增,纯函数树索引:`Find` / `FindEnclosingBranch` / `ResolveMergeTarget`)
> - `Engine/WfExecutionContext.cs`(注入求值器 + `FindNode` 走索引 + `ResolveMergeTarget`)、`Engine/WorkflowEngine.cs`(构造参数 + 三处 ctx 填充)
> - `Engine/Operations/EnterNodeOp.cs`(`case Branch` + `EnterBranchAsync` + `SelectArm`)、`Engine/Operations/TakeTransitionOp.cs`(汇合)
> - `Services/WfDefinitionService.cs`(发布期校验树化 + 5 类分支规则 + `conditionsOnNonBranch`)
> - `Services/WfTaskService.cs` + `Services/WfInstanceService.cs`(节点名解析改走索引 —— **包里原本有三份主链线性扫描,现已全部收敛**)
> - 测试 5 个文件:`WfModelIndexTests`(8)、`WfBranchPublishValidationTests`(4)、`WfSelectArmTests`(2)、`WorkflowM2BranchRegressionTests`(4)、以及 Task 1 的 `WfConditionEvaluatorTests`(47)
> - **零新增错误码**(分支违规一律 `ModelInvalid` + `reason`,48021+ 仍空);`WorkflowSetup.cs` 零改动
> - **`WorkflowEngine` 构造函数 + `WfExecutionContext` 的 `required` 成员是有意的源码级破坏性变更**,已在 XML 注释写明影响面(继承者需补 `base(...)`;`TryAdd` 整体替换不受影响)

> 📌 **Task 2 全部钉死清单(9 项,每项都实测过变异转红;后续任务改这些地方时它们会拦你)**:
> | 改动 | 钉它的用例 | 变异实测 |
> |---|---|---|
> | `TakeTransitionOp` 汇合 | `Arm_with_condition_...merges_to_branch_next` | 改回 `FromNode.Next` → 73/74 红 |
> | `WfExecutionContext.FindNode` 树查找 | 同上 | 改回线性扫描 → 73/74 红(48002) |
> | `WfTaskService` 节点名接线 | 同上(待办列表 `nodeName`) | 修前实测 `Actual: null` |
> | `WfInstanceService` 节点名接线 | 同上(详情 `myPendingTask.nodeName`) | 改回线性扫描 → 76/77 红 |
> | **待办页缓存按 versionId 键控** | `Todo_page_resolves_node_name_per_row_when_two_definitions_share_arm_node_id` | 键改常量 `0L` → **77/78 红**,`Expected: "A审批节点" / Actual: "B审批节点"` |
> | **`SelectArm` 默认臂不参与求值** | `Matching_condition_arm_wins_even_when_default_arm_is_listed_first` | 删 `continue;` → **77/78 红**,`Expected: Id="high" / Actual: Id="default"` |
> | `SelectArm` 默认臂兜底 | `Default_arm_expr_is_never_evaluated_and_still_wins_as_fallback` | (与上条配对) |
> | `conditionsOnNonBranch` 校验 | `Non_branch_node_with_conditions_is_rejected` | 条件改 `false` → 76/77 红 |
> | branch 节点 NodeEnter/NodeLeave 各一条 | `Gateway_taken_history_..._once_each` | 多写一条 leave → 73/74 红 |

> ⚠️ **本轮最该记住的一条**:`SelectArm` 那条 finding **是 Round 12 P2① 的再开**。第一轮说「零覆盖」→ 第二轮建了文件、写了 XML 注释宣称钉住它 → **实测变异照样全绿**。
> 失效机理:用例把默认臂的 `Expr` 配成**恒真**,而「兜底返回默认臂」与「求值为真返回默认臂」在恒真取值下**答案完全相同** —— 断言取值恰好落在两种语义重合的那个点上。
> **教训:补测试时要问「我选的这组输入,能让正确实现和错误实现给出不同答案吗?」** 覆盖率账面回来了不等于守卫回来了。这与 Round 10/13 那两次 plan defect 是同一个家族:**判据没有真正区分力**。

> 📌 **Round 15 复评已实测/推演确认、下轮别重复查的**:三处节点名接线实现正确(缓存语义、负缓存、同页同版本只建一次索引都核过);`conditionsOnNonBranch` 三种合法形状均不误伤(含前端 `model.ts:148` 同形谓词,设计器不会吐 `conditions: []`);`WfSelectArmTests` 的探针类继承写法**合法可靠**(问题只在断言取值);**全包按代码形状穷举过,没有第四处「同源重复逻辑」** —— 只剩两次树遍历(运行期 `WfModelIndex.WalkChain` + 发布期 `ValidateChain`),已办/历史列表读的是 `WfHisTask.NodeName` 落库字段(由 `ctx.FindNode` 写入),`RejectToNodeId`/`ReturnToNodeId` 全包零消费方。

> ✅ **Round 13 的 blocker 已关闭**。Round 14 在 `WorkflowM2BranchRegressionTests` 用例 ① 里补了实例详情接口的两条断言(`data.myPendingTask.nodeName`),Opus **亲手复跑上一轮那个存活变异**:`WfInstanceService.cs:389` 改回主链线性扫描 → **76/77 变红**(`Expected: "high-approve" / Actual: null`),已还原、无残留、重跑 **77/77**。

> 📌 **Task 2 已钉死清单(复评时告诉评审:这些已实测,不必重复验)**:
> | 改动 | 钉它的用例 | 实测结果 |
> |---|---|---|
> | `TakeTransitionOp` 汇合 | `Arm_with_condition_...merges_to_branch_next` | 改回 `FromNode.Next` → 73/74 红 |
> | `WfExecutionContext.FindNode` 树查找 | 同上 | 改回线性扫描 → 73/74 红(48002) |
> | `WfTaskService` 节点名接线 | 同上(待办列表 `nodeName`) | 修前实测 `Actual: null` |
> | `WfInstanceService` 节点名接线 | 同上(详情 `myPendingTask.nodeName`) | 改回线性扫描 → 76/77 红 |
> | `SelectArm` 默认臂语义 | `Matching_condition_arm_wins_even_when_default_arm_is_listed_first` | 默认臂短路 → 变红 |
> | `conditionsOnNonBranch` 校验 | `Non_branch_node_with_conditions_is_rejected` | 条件改 `false` → 76/77 红 |
> | branch 节点 NodeEnter/NodeLeave 各一条 | `Gateway_taken_history_..._once_each` | 多写一条 leave → 73/74 红(评审做的) |

> ✅ **Round 13 已确认钉死的**(下一轮别重复验):`conditionsOnNonBranch` 校验(Opus 把 `node.Type != Branch` 变异成 `false` → **76/77 变红**,红在 `Non_branch_node_with_conditions_is_rejected`);`SelectArm` 默认臂语义(executor 实测默认臂短路变异 → 红在 `Matching_condition_arm_wins_even_when_default_arm_is_listed_first`)。

> 🚨 **P1 摘要(Opus 已独立复核,不是评审一面之词)**:同一个「主链线性扫描」在包里**有三份**,Task 2 只换掉了 `WfExecutionContext` 那一份。剩下两份(`WfTaskService.cs:316`、`WfInstanceService.cs:390`)负责渲染待办/详情的**节点名**。Task 2 之前 branch 在发布期被拒、主链扫描是对的;**Task 2 让待办第一次能落进臂内,这两处就同步失效** → 臂内待办 `nodeName` 恒 `null`,前端 `||` 兜底后显示设计器内部 Id 而非中文名。
> **这与 Round 9 我翻出的那颗地雷是同一类**(一个逻辑多处复制),只是我当时 grep 的是 `FindNode` 的调用方,**没有 grep 扫描模式本身**,于是找到一处、漏掉两处。**教训:找「同源重复逻辑」要按代码形状 grep,不能按符号名 grep。**

> ✅ **Round 10 的 blocker 已关闭**。Round 11 新增 `WorkflowM2BranchRegressionTests.cs`(3 条 E2E),Opus **亲手复跑两个变异各一次**:
> - `TakeTransitionOp.cs:18` → `FromNode.Next`:**73/74 变红**,`Arm_with_condition_creates_todo_inside_arm_then_merges_to_branch_next`,`Expected: 1 (Running) / Actual: 2 (Approved)` —— 正是「走完第一条臂整单直接通过完结」。
> - `WfExecutionContext.cs:48` → M1 主链线性扫描:**73/74 变红**,同一条用例,`Expected: 0 / Actual: 48002` —— 正是 Round 9 预言的那颗 48002 地雷。
> 两次均已还原,关键行原样在位、无线性扫描残留,重跑 **74/74**。
>
> ⚠️ **留给 review 判断的一个观察(Opus 不自评,只记录)**:两个变异**都只被同一条用例杀死**(`Arm_with_condition_...`)。这是结构决定的 —— `ResolveMergeTarget` 只在「节点 `Next` 为 null **且** 该节点在臂内」时才与 `FromNode.Next` 有差异,而三条用例里只有它构造了这个形状。**executor 主动纠正了 Plan 的一处错误主张**:Plan 说第二条用例(默认臂无子链)「也杀变异①」,**不成立** —— 默认臂那条路里 `TakeTransitionOp` 的 FromNode 是 branch1 本身,它在主链上且 `Next` 非 null,两种写法算出来一样。executor 如实回报而没有硬加一条假断言,判断正确。

> **Task 2 预期改动文件**(review 阶段把这份清单显式喂给评审方;exec 若实际有出入,回报时说明):
> - `backend/src/TenonAdmin.Workflow/Schema/WfModelIndex.cs`(新增,纯函数树索引)
> - `backend/src/TenonAdmin.Workflow/Engine/WfExecutionContext.cs`(注入求值器 + `FindNode` 换实现 + `ResolveMergeTarget`)
> - `backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs`(构造参数 + 三处 ctx 填充)
> - `backend/src/TenonAdmin.Workflow/Engine/Operations/EnterNodeOp.cs`(`case Branch` + `EnterBranchAsync` + `SelectArm`)
> - `backend/src/TenonAdmin.Workflow/Engine/Operations/TakeTransitionOp.cs`(汇合)
> - `backend/src/TenonAdmin.Workflow/Services/WfDefinitionService.cs`(发布期校验树化 + 分支规则)
> - `backend/tests/TenonAdmin.Tests/WfModelIndexTests.cs`(新增)
> - `backend/tests/TenonAdmin.Tests/WfBranchPublishValidationTests.cs`(新增)
> - `backend/tests/TenonAdmin.Tests/WorkflowM2BranchRegressionTests.cs`(**Round 11 新增**,引擎 E2E 回归,3 条)
> - **`WorkflowSetup.cs` 预期零改动**(求值器 Task 1 已 `TryAdd`)

> **Task 1 交付物**(已收口,后续任务只读不改;Task 2 起 `EnterNodeOp` 要注入 `IWfConditionEvaluator` 用它):
> - `Abstractions/IWfConditionEvaluator.cs` —— 单方法 `bool Evaluate(WfConditionExpr?, string?)`,**失败安全:任何不确定一律 false 以落默认臂,永不抛异常**
> - `Engine/WfConditionEvaluator.cs` —— 非 sealed,模板方法全 `public/protected virtual`,11 个 op 全覆盖
> - `WorkflowSetup.cs` —— `TryAddScoped<IWfConditionEvaluator, WfConditionEvaluator>()`
> - `WfConditionEvaluatorTests.cs` —— 35 个测试方法 / **47 条用例**,纯单测不起宿主
> - **语义定案表见下面 `## Plan` 的「语义定案」节 —— Task 2 起该表仍是契约,不要在引擎侧另立一套条件语义。**(Task 2 重写 `## Plan` 时**把该表整体保留/挪到本节**,别弄丢。)

> **Codex 已连续两轮判定不可用,别再试了**:Round 3 我猜是「新文件 untracked 导致它读不到 diff」;Round 5 **专门 `git add -N` 让完整 747 行 diff 可见后重跑,它照样点名 `HttpContextDataScopeContext.cs`/`OrgService.cs`×4/`web-react/UserPicker.tsx`,全不在改动集内** —— 假说被证伪,根因是本机 Codex 压根不读 diff、只 codegraph 漫游全仓。**后续 review 一律直接用 `code-reviewer` 并显式列改动文件路径,不要再花时间在 `git add -N` 上。**(index 已还原,`git status` 与跑之前一致。)

> **Task 1 实际改动文件**(review 阶段把这份清单显式喂给评审方):
> - `backend/src/TenonAdmin.Workflow/Abstractions/IWfConditionEvaluator.cs`(新增)
> - `backend/src/TenonAdmin.Workflow/Engine/WfConditionEvaluator.cs`(新增)
> - `backend/src/TenonAdmin.Workflow/WorkflowSetup.cs`(+3 行)
> - `backend/tests/TenonAdmin.Tests/WfConditionEvaluatorTests.cs`(新增)

> 阶段循环:`plan`(Opus 主上下文拆解)→ `exec`(Sonnet executor 实现)→ `review`(Codex/code-reviewer 评审)→ 无阻断则勾掉本任务、任务号 +1、阶段回 `plan`;有阻断则阶段回 `exec` 修。

## 语义契约(跨任务长期有效;`## Plan` 被重写也不得丢)

> Task 1 定案,已被 `WfConditionEvaluatorTests` 的 **47 条用例**逐行钉死(每行都实测过「变异实现 → 测试变红」)。
> **Task 2 起引擎侧不得另立一套条件语义** —— `EnterNodeOp` 只调 `IWfConditionEvaluator.Evaluate(arm.Expr, instance.VariablesJson)`,**不要在引擎里重复判类型/判空/兜异常**(求值器已保证永不抛、不确定即 false)。

变量源 = `wf_instance.VariablesJson`,**发起时由前端原样提交、后端从不校验**(`WfInstanceService.StartAsync` 直接透传 `input.VariablesJson`)。因此求值器必须对烂 JSON 免疫。

| 场景 | 定案 |
|---|---|
| `variablesJson` 为 null/空白/非法 JSON/根不是 object | 视作「无任何字段」,不抛异常 |
| 字段查找 | **平铺、大小写不敏感**(对齐 `WfModelJson.Options.PropertyNameCaseInsensitive=true`);**不支持 `a.b` 嵌套路径** |
| 字段缺失 或 值为 JSON `null` | 除 `empty`/`notEmpty` 外**一律返回 false**(含 `ne`/`notIn` —— 缺失既不 eq 也不 ne,失败即落默认臂) |
| `eq`/`ne` 数字↔数字 | `decimal` 比较 |
| `eq`/`ne` 字符串↔字符串 | `OrdinalIgnoreCase` |
| `eq`/`ne` 布尔↔布尔 | 精确 |
| `eq`/`ne` 跨类型(宽松) | 数字↔字符串:字符串按 `decimal.TryParse(InvariantCulture)` 试转,成功则数值比,失败则退回字符串文本比(`OrdinalIgnoreCase`);布尔↔字符串:`bool.TryParse` |
| `gt`/`gte`/`lt`/`lte` | **仅数值**。两侧都用 `decimal` 强转(`JsonElement.TryGetDecimal` / `decimal.TryParse`),任一侧转不出 → **false**。日期串比较不做(已知天花板,注释写明,留给后续里程碑) |
| `in`/`notIn` | 右侧 `value` 是数组时按元素逐个走 `eq` 宽松规则;右侧非数组时当作单元素列表;**左侧字段本身是数组 → false**。`notIn` = 字段存在且不在列表 |
| `contains` | 字段是字符串 → `OrdinalIgnoreCase` 子串,**且要求 `value` 也是字符串**(`contains("order100", 100)` 不匹配 —— Round 3 评审提出,已定案保持此行为,更可预测);字段是数组 → 任一元素按 `eq` 宽松规则等于 `value`;其余 → false |
| `empty` | 字段缺失 / JSON `null` / 空或纯空白字符串 / 空数组 / 空对象 → true。**数字 0 与 `false` 不算空** |
| `notEmpty` | `!empty` |
| 组 vs 叶子 | `Children` 非 null → 组(`Field`/`Op` 忽略);否则叶子 |
| 组 `Logic` 缺省 | 按 `And` |
| 组 `Children` 为空数组 | **false**(没配就永不命中 → 落默认臂) |
| `expr` 为 null / 叶子缺 `Field` 或 `Op` | **false** |
| 递归深度 | 超过 64 层返回 false(与 `System.Text.Json` 默认 MaxDepth 对齐,防栈溢出) |

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段重写)

> **Task 8 — API 契约刷新 + 真实浏览器验收 + DONE-CONDITION**。Round 44 由当前 Codex 基于现有 Playwright 基建、真实 designer/start/todo/detail 页面与 contract-drift 工作流拆解。
> **边界**:不新增 M2a 功能；只把真实契约和完整业务链跑通并留下可重复 E2E/截图。若浏览器揭出阻塞 bug，以该 E2E 为红测做最小修复后复评。

### 当前任务关键定案

| 决策点 | 定案 |
|---|---|
| API 生成 | 用一个临时 MinimalHost 固定监听 `127.0.0.1:5100`，顺序执行 `web npm run gen:api` 与 `web-react npm run gen:api`；两份 schema 必须来自同一 openapi 响应，完成后只停止本轮启动的精确进程。 |
| 浏览器方式 | 新增永久 `web/e2e/workflow-m2a.spec.ts`，走仓库既有 Playwright config 的新数据库/唯一端口；用 UI 创建草稿、加 branch/approval、配 condition、发布、发起、审批，不直接 POST 定义/实例来冒充浏览器验收。 |
| 业务模型 | `start → branch{金额>1万 → 总经理审批; default → null} → end`。变量 key=`amount`、op=`gt`、value=`10000`;高额单 20000 必须先 Running/出现「总经理审批」待办再批准，低额单 5000 必须默认臂直接 Approved。 |
| 办理人 | 流程节点名称明确为「总经理审批」；优先用浏览器可登录/可办理的真实总经理用户。若权限种子不允许新用户进入路由，可把 superAdmin 作为该演示节点的指定成员，但测试必须断言选人和待办，不得用 initiator provider 伪造。 |
| 截图 | 写入 `.loop/wf-ui-shots/m2a-01-designer-published.png`、`m2a-02-high-approved.png`、`m2a-03-default-approved.png`；截图前各页面状态需有可读断言，避免截 loading/错误页。 |
| 完成判据 | 浏览器 E2E + 两份 schema + backend DONE 过滤器 + web workflow/typecheck/lint/build + 必要 web-react typecheck + diff/residue/screenshot 审计全部绿，才勾 Task8/宣告 M2a 完成。 |

### 步骤

1. **刷新双 schema**
   - 启动临时 MinimalHost；健康/openapi 可读后，顺序跑两模板 `npm run gen:api`。记录生成前后 hash/diff；即便零 diff 也记录确实执行。两份 schema 内容应一致。
   - 精确停止本轮 host，确认无遗留监听 5100/孤儿进程；不得终止用户已有服务。
2. **写真实业务 Playwright**
   - 登录并进入系统应用，直达 designer 空态；用 UI 新建唯一命名草稿。
   - 用 UI 插入 branch；在首非默认臂插 approval；打开 branch drawer 配 `amount > 10000`，打开 approval drawer命名「总经理审批」并选择指定成员；保存并发布。
   - 断言发布成功、模型在页面上可见，保存第一张截图。测试 selector 优先 role/label/text/本仓稳定 class，不用超时 sleep。
3. **走高额臂**
   - 浏览器进 `/workflow/start` 选择定义，填业务键和 `amount=20000`，发起后断言 Running 且存在「总经理审批」待办/批准按钮；通过浏览器完成批准，断言 Approved，截第二张图。
4. **走默认臂**
   - 再次从 start 页发起 `amount=5000`，断言无需待办直接 Approved，且与高额实例 id/businessKey 不同；截第三张图。
5. **浏览器验真与反假绿**
   - E2E 必须断言两单关键中间态与终态；临时把条件阈值/变量 key 或默认臂改错时至少一条用例应红，随后还原。若现有 UI 暴露 bug，以本 E2E 为红测最小修复并列出额外文件。
6. **DONE-CONDITION 门禁**
   - backend Release build，然后 `dotnet test backend/TenonAdmin.slnx -c Release --no-build --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow" -nodeReuse:false`。
   - web:`npx vitest run src/workflow/`;`npm run typecheck`;`npm run lint`;`npm run build`;定向 Playwright 业务+layout。
   - web-react 至少 `npm run typecheck` 验证新 schema；两份 schema hash一致；`git diff --check`、TODO/FIXME/.skip/变异/临时文件/监听端口/截图存在与尺寸全部审计。
7. **范围**
   - 预期永久新增 1 个 E2E + 3 张截图；两份 schema 可能是零 diff或生成 diff。除非浏览器红测揭 bug，不改生产代码；不改 M2b/M3/docs 设计定案，不 commit/push。

<details><summary>Task 5 历史计划(已收口,仅供追溯)</summary>

> **Task 5 — `model.ts` 树化**(纯前端模型层 + designer 调用点)。Round 27 由当前 Codex 用 CodeGraph 与真实 schema/后端发布校验拆解。
> **TDD seam 已由任务确认**:框架无关的公开模型函数(`create/flatten/find/insert/remove/validate/clone`)是唯一测试 seam;不 mount Vue 组件、不 mock 内部 helper。Task 6 才改分支 DOM/UI。

### 当前任务关键定案

| 决策点 | 定案 |
|---|---|
| 树的遍历顺序 | `flattenChain` 保留 API 名以免无谓扩散,语义升级为确定性 DFS:沿主链依次 push;遇 branch 时按 `conditions` 数组顺序递归每条臂子链,再继续 `branch.next`。同一顺序供 `find`/校验使用。 |
| 插入/删除落点 | `insertAfter` 命中任意主链或臂内节点后只改该节点自己的 `next`;命中 branch 本身时插到汇合后继(`branch.next`),**不**暗选某条臂。`removeNode` 在所属局部链里 splice,不得把臂尾接到 `branch.next`(汇合由引擎语义处理)。 |
| branch 出厂形状 | `createBranchNode` 生成一条普通臂 + 一条默认臂;普通臂的默认表达式为 `and + []`(求值恒 false 但结构非 null),所以什么都不配也能发布并安全落默认臂。两臂 `next=null`。 |
| 臂增删 | `addBranchArm` 把新普通臂插到默认臂前;`removeBranchArm` 只允许删非默认臂,默认臂不可删,始终保持恰好一个默认臂。 |
| 校验对齐 | `validateM1Model` 更名 `validateModel`,支持 `start|approval|cc|branch`;节点 Id 在整棵树非空且全局唯一;非 branch 禁带 conditions;branch 对齐后端 `branchNoArms/emptyArmId/duplicateArmId/branchArmWithoutExpr/branchDefaultArmCount`。Parallel/Webhook 仍 unsupported。 |
| schema 类型 | `schema.ts` 补 `WfConditionOp`(11 个)、`WfConditionLogic`、递归 `WfConditionExpr`、`WfBranchArm`;`WfNode.conditions` 从 `unknown[]` 收紧为 `WfBranchArm[]`。保持框架无关,不 import Vue。 |

### 步骤

1. **先在 `model.spec.ts` 写红测,一条垂直切片一个行为**
   - 构造 main `start→branch→merge`,两条臂分别有 `armA1→armA2` 与 `armB1`;断言 flatten 精确顺序、`findNode` 能找臂内深层节点。
   - 在 `armA1` 后 insert,断言只改 armA 局部顺序且 `branch.next` 仍是 merge;remove 新节点/臂首节点后正确 splice。
   - `createBranchNode`/`addBranchArm`/`removeBranchArm`:默认臂恒唯一且最后;默认臂拒删;新增普通臂有非 null 的恒假 expr。
   - `validateModel`:有效树无问题;分别钉整树重复 node Id、非 branch 带 conditions、空/重复 arm Id、非默认臂缺 expr、默认臂 0/2 条、parallel/webhook unsupported。
   - 扩展 reactive-proxy 克隆用例到 branch.conditions[].next 深层,禁止退回 `structuredClone`。
2. **`schema.ts` 收紧 M2a 类型**
   - 新增上述四类/接口;注释从「M1 不读写」更新为 M2a;新增 `WF_M2A_NODE_TYPES`。`WF_M1_INSERTABLE` 本轮不删(尚未有消费方,Task 6 再决定新增按钮集合),避免把 UI 改动偷渡进模型任务。
3. **`model.ts` 逐切片转绿**
   - 新增 `createBranchNode`、`createBranchArm`(可为内部或导出)、`addBranchArm`、`removeBranchArm`;`createNode` 扩成 approval|cc|branch。
   - 树化 `flattenChain`/`findNode`/`insertAfter`/`removeNode`;不得写四套彼此漂移的遍历,优先一个小型局部 walk/helper,但不要引入类层级。
   - `validateM1Model` → `validateModel`;扩展 `WfModelIssue` code/`armId`,规则与后端一致。
4. **更新唯一产品调用点**
   - `designer.vue` 两处改用 `validateModel`。`WfConfigDrawer` 自动受益于树化 `findNode`;`WfNodeTree.vue` 的线性渲染留给 Task 6,本轮不改 DOM/CSS。
5. **区分力变异(逐个转红并还原)**
   - flatten 跳过 `conditions[].next`;find 跳过臂;insert 只扫主链;remove 只扫主链;validate 只扫主链;默认臂计数校验删掉;clone 改 `structuredClone`。每条必须有明确失败用例,最终零残留。
6. **范围**
   - 预期永久改动 4 个文件:`workflow/schema.ts`,`workflow/model.ts`,`workflow/model.spec.ts`,`definition/designer.vue`。不碰 WfNodeTree/WfConfigDrawer DOM、locales、web-react、backend、Task 6/7 UI、M2b/M3;不 commit/push。
7. **验证**
   - `cd web && npx vitest run src/workflow/model.spec.ts`
   - `cd web && npx vitest run src/workflow/`
   - `cd web && npm run typecheck && npm run lint`
   - 检查无 TODO/FIXME/`.skip`/`structuredClone`/变异残留,`git diff --check` clean。

</details>

<details><summary>Task 4 历史计划(已收口,仅供追溯)</summary>

> **Task 4 — 后端测试固化**。Round 24 由当前 Codex 按真实代码与既有测试拆解。
> **已确认 seam**(用户给出的 Task 4 即预先确认):签核行为只从公开 HTTP `start` / `todo` / `task/approve|reject` 观察;SPI 可替换性只从 `AddTenonAdminWorkflow` 的前置 DI 注册观察。除定向变异外不查私有方法、不直接断言数据库实现细节。

### 当前任务步骤

1. **先复用既有分支契约,不重复造同义测试**
   - `WorkflowM2BranchRegressionTests.Arm_with_condition_creates_todo_inside_arm_then_merges_to_branch_next` 已覆盖:命中条件臂 → 臂内待办 → 臂尾汇合主链 → 完结。
   - `Default_arm_without_subchain_merges_directly_to_branch_next` 已覆盖:默认臂无子链 → 直接汇合主链 → 完结。
   - exec 只需定向复跑这两条;若不绿才修,**不得借 Task 4 改 branch 实现**。
2. **新建 `backend/tests/TenonAdmin.Tests/WorkflowM2RegressionTests.cs`,用同一公开 HTTP seam 各写一条独立签核契约**
   - 统一模型:`start → approval(provider=user,userIds=[first,second],mode=<any|all|sequential>) → end`;每条用例独立 factory/账号/定义,不共享状态。
   - **会签一票否决**:`all` 下 first 先同意 → 实例仍 `Running`;second 拒绝 → 实例 `Rejected`;拒绝后 first/second 都不能再推进同一 task。
   - **或签先表态即定局**:`any` 下 first 同意 → 立即 `Approved`;second 再办同一 task → `TaskConflict`。必须断言实例状态,不能只断言 HTTP code。
   - **顺序会签逐级晋级**:`sequential` 下只有 first 有待办,second 抢先审批 → `TaskConflict`;first 同意 → 仍 `Running` 且 `newAssigneeUserIds` **精确为 `[second]`**;second 用同一 taskId 同意 → `Approved`。
3. **补第七条 SPI 可替换性测试**(`WorkflowReplaceabilityTests.cs`)
   - 类注释由「六件套」改「七件套」并列出 `IWfConditionEvaluator`。
   - 新增 `PreRegisteredConditionEvaluator_ShouldWinOverBuiltIn`:前置 `AddScoped<IWfConditionEvaluator, FakeConditionEvaluator>()` → `AddTenonAdminWorkflow()` → 解析必须为 fake。
   - fake 只实现公开 `Evaluate(WfConditionExpr?,string?)`;不启动宿主、不 mock 包内协作者。
4. **TDD/区分力验真(每条变异单独改、跑定向测试、记录失败名、立即还原)**
   - All:`case WfSignMode.All` 暂改成首票即 `true` → 会签用例必须在 first 同意后的 `Running` 断言处红。
   - Any:`case WfSignMode.Any` 暂走未满票逻辑/返回 `false` → 或签用例必须在 first 同意后的 `Approved` 断言处红。
   - Sequential:`case WfSignMode.Sequential` 暂改成 `return true` → 顺序用例必须在 first 同意后的 `Running`/晋级名单处红。
   - Replaceability:`WorkflowSetup` 的 evaluator `TryAddScoped` 暂改 `AddScoped` → 第七条用例必须红。
   - **任何变异最终都不得残留**;每次还原后重跑对应定向测试。
5. **范围与实现纪律**
   - 预期只改两个测试文件:`WorkflowM2RegressionTests.cs`(新增)与 `WorkflowReplaceabilityTests.cs`;生产代码零永久改动。
   - 不碰 Task 5 前端模型、Task 6/7 UI、M2b/M3、错误码、现有 M1 测试;不 commit/push。
6. **验证**
   - `dotnet test backend/TenonAdmin.slnx -c Release --filter "FullyQualifiedName~WorkflowM2RegressionTests|FullyQualifiedName~WorkflowM2BranchRegressionTests|FullyQualifiedName~WorkflowReplaceabilityTests" -nodeReuse:false`
   - `dotnet build backend/TenonAdmin.slnx -c Release -nodeReuse:false`
   - `dotnet test backend/TenonAdmin.slnx -c Release --no-build --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow" -nodeReuse:false`
   - 基线 88;新增 4 条后应为 **92/92**。检查无 `TODO` / `FIXME` / `Skip` / 临时变异残留。

</details>

<details><summary>Task 3 历史计划(已收口,仅供追溯)</summary>

> **Task 3 — `multiLeader` 发起时快照**(纯后端)。Round 17 由 Opus 读真实代码后拆解。
> **定案出处**:设计文档 §十三 13.2 #1 —— 「**发起时快照**:发起瞬间沿 `SysUser.DirectorId` 链拍平存进实例,之后组织调整不影响在途单」。理由原文:与 JNPF 一致、审批链可预测、发起页能提前展示「将由谁审」、排查简单;**代价「发起后主管离职需人工干预」文档已明确接受**。
> **本任务不碰**:前端任何文件、`web-react/`、其余 7 个 provider、M2b、M3、Task 4 的签核模式 E2E。

### 本任务的关键设计定案(exec 不得自行发挥)

| 决策点 | 定案 | 理由 |
|---|---|---|
| **快照存哪儿** | **`wf_instance` 新列 `LeaderChainJson`**(`BigString`、可空),与既有 `SelectedUserIdsJson` 完全同构 | **不能存 `VariablesJson`** —— 它是前端提交、**后端全链路从不校验**的字段(见 `## 语义契约`)。发起人只要提交那个保留键就能**自己指定审批链**,是一条提权。这条比「与业务变量混住」严重得多,**是排除 `VariablesJson` 的决定性理由,别被后人以「少加一列」为由改回去** |
| 快照内容:原始链 or 启用过滤后 | **启用过滤后**,即「今天 `MultiLeaderApproverProvider` 返回什么就存什么」 | ① 那正是发起页要展示的「将由谁审」;② §十三 已接受「主管离职需人工干预」的代价,运行期不必再过滤;③ 运行期再过滤会部分抵消「组织调整不影响在途单」的保证 |
| 一条链 vs 每节点一条 | **每实例一条链**,按模型里**所有** `multiLeader` 节点的 **`level` 最大值**拍平;运行期每个节点取 `chain.Take(level)` | 链是有序的 1..N 级,截断即精确;不同节点 `level` 不同也只需存一份 |
| 谁来算这条链 | **复用 `IApproverResolver.ResolveAsync("multiLeader", …)`**,`Params` 传 `level = maxLevel` | ① **不给 `WorkflowEngine` 再加构造参数** —— Task 2 刚做过一次源码级破坏性变更,一个任务内做第二次不合适;② 消费者若前置替换了 `multiLeader` provider,快照自动走他们的实现 |
| 算快照时怎么避免自引用 | 快照阶段传 `LeaderChain = null`,provider 自然走实时 `DirectorId` 上溯 | 无循环 |
| **`LeaderChain` 的 null 与空数组必须分开** | `IReadOnlyList<long>? LeaderChain`:**`null` = 没有快照(老实例)→ 回退实时查**;**空数组 = 快照过、链本来就空 → 不回退** | 若把空数组也当没快照,会在运行期重新查库,**正好把「快照」这件事本身废掉**(发起后新增了主管就会被查出来)。这是本任务最容易写错的一处 |
| 老实例(本次改动之前发起的)怎么办 | `LeaderChainJson` 为 null → provider **回退到实时上溯**,行为与今天一致 | 不回退的话,所有在途实例的下一个 `multiLeader` 节点都会解析成无人 → 落空审批人策略,是线上事故 |
| 其余 7 个 provider | **行为一字不改**,不读新字段 | 台账 Task 3 明确「其余 7 个 provider 行为不变」。**`leader`(单第 N 级)也不要顺手改** |

### 步骤

1. **加字段(三处平铺接线,先把管道通了)**
   - `backend/src/TenonAdmin.Workflow/Entities/WfInstance.cs`:加 `LeaderChainJson`,**照抄 `SelectedUserIdsJson` 的写法**(`[SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "多级主管链快照 JSON")]`)。
   - `backend/src/TenonAdmin.Workflow/Abstractions/IApproverProvider.cs`:`ApproverResolveContext` 加 `public IReadOnlyList<long>? LeaderChain { get; init; }`,**XML 注释写明 null 与空数组的区别**(见定案表)。
   - `backend/src/TenonAdmin.Workflow/Engine/WfExecutionContext.cs`:加 `public IReadOnlyList<long>? LeaderChain { get; init; }`(**不要加 `required`** —— Task 2 已经因 `required` 造成过一次破坏性变更,这次给默认 `null` 即可,老代码路径不受影响)。
   - **判据**:`dotnet build -c Release` 零错零告警;`SelectedUserIdsJson` 与新列在实体里风格一致。
2. **`WfModelIndex` 暴露节点枚举**(`backend/src/TenonAdmin.Workflow/Schema/WfModelIndex.cs`)
   - 加 `public IEnumerable<WfNode> Nodes => _nodeById.Values;`(或等价只读视图),XML 注释说明「含分支臂内的所有节点」。
   - 配一条单测加进 `backend/tests/TenonAdmin.Tests/WfModelIndexTests.cs`:**臂内节点必须出现在 `Nodes` 里**。
   - **判据**:把 `Nodes` 改成只返回主链节点 → 该单测变红。**不要新写第三次树遍历**,索引已经有全部节点了。
3. **发起时算快照并落库**(`backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs` 的 `BeginStartAsync`)
   - 新增 `protected virtual int ResolveMaxLeaderLevel(WfModel model)`:用 `WfModelIndex.Build(model).Nodes` 找出所有 `Props?.Assignee?.Provider == ApproverProviderKeys.MultiLeader` 的节点,取 `ApproverParamReader.GetInt(params, "level", 1)` 的最大值;**一个都没有就返回 0**。
   - 新增 `protected virtual async Task<IReadOnlyList<long>?> SnapshotLeaderChainAsync(...)`:`maxLevel <= 0` → 返回 `null`(不存);否则调 `approverResolver.ResolveAsync(ApproverProviderKeys.MultiLeader, new ApproverResolveContext { InitiatorUserId = cmd.StarterUserId, InitiatorOrgId = cmd.StarterOrgId, Params = <level=maxLevel>, LeaderChain = null }, ct)`。
   - **在 `db.Insertable(instance)` 之前**把结果序列化进 `instance.LeaderChainJson`(用 `WfModelJson.Options`),并把链塞进 ctx 的 `LeaderChain`。
   - `BeginCompleteAsync` / `BeginTransferAsync`:把 `instance.LeaderChainJson` 反序列化进 ctx(**照 `DeserializeSelectedUsers` 的写法加一个同构 helper**;烂 JSON 的处置与它保持一致)。
   - **判据**:`WorkflowEngine` 的**构造函数签名一个字都不改**;模型里没有 `multiLeader` 节点时 `LeaderChainJson` 保持 `null`(不写空数组,免得把「没快照」与「空快照」搞混)。
4. **`MultiLeaderApproverProvider` 优先用快照**(`backend/src/TenonAdmin.Workflow/Providers/BuiltInApproverProviders.cs`)
   - `ResolveAsync` 开头:`if (context.LeaderChain is { } snapshot) return snapshot.Take(maxLevel).ToList();` —— **注意是 `is { }`(非 null 即用,空数组也用),不是 `is { Count: > 0 }`**。
   - `null` 时走现有实时上溯代码,**一行不改**。
   - 现有的环路防护(`chain.Contains(directorId)`、`directorId == InitiatorUserId`)与 `FilterEnabledAsync` 都留在实时分支里。
   - **判据**:把 `is { }` 写成 `is { Count: > 0 }` **必须让步骤 6 的「空快照不回退」用例变红**。
5. **`EnterNodeOp` 把快照传进解析上下文**(`backend/src/TenonAdmin.Workflow/Engine/Operations/EnterNodeOp.cs`)
   - `EnterApprovalAsync` 与 `EnterCcAsync` 各自构造 `ApproverResolveContext` 的地方,**都**加 `LeaderChain = ctx.LeaderChain`。
   - **判据**:**两处都要加**(这正是 Round 13 那次「修了两个调用点、判据只覆盖一个」的同款陷阱);漏任一处,步骤 6 的对应用例要能抓到。
6. **测试**(全部并入 `backend/tests/TenonAdmin.Tests/WorkflowM2BranchRegressionTests.cs` 或新建 `WorkflowMultiLeaderSnapshotTests.cs`,**名字须含 `Workflow` 或 `Tests.Wf` 前缀**才会被过滤器捞到)
   - ① **头号用例(台账点名的)**:starter→first→second、`level=2` 发起一单 → **发起后把 `first` 的 `DirectorId` 改成第三个人**(或把 starter 的 `DirectorId` 改掉)→ 审批推进,**断言第二级仍是 `second`**,与发起时一致。
   - ② **臂内 `multiLeader` 也被快照覆盖**:模型是 `start → branch{armHigh: → multiLeader 节点; armLow: 默认}`,`level=2`;断言臂内节点解析出的审批链正确。**这条钉步骤 2 的树枚举**(只扫主链会让 `maxLevel=0`、快照为 null)。
   - ③ **老实例回退**:构造 `LeaderChainJson` 为 null 的实例(直接发起一个模型里**没有** multiLeader 的流程不算数——要的是「有 multiLeader 但没快照」;可用 SQL/仓储把某实例的 `LeaderChainJson` 置 null,或在测试里直接调 provider 传 `LeaderChain = null`)→ 行为与今天一致。
   - ④ **空快照不回退**:直接单测 `MultiLeaderApproverProvider`,传 `LeaderChain = []` → 必须返回空,**不得**去查库。
   - **判据**:每条都要能杀死对应变异(见各步骤判据);**写断言前先问:我选的这组输入,能让正确实现和错误实现给出不同答案吗?**(这是本 loop 三次栽跟头总结出的检查项)
8. **补 cc 调用点的回归断言(Round 18 实测 blocker;Round 19 exec 只做这一步,步骤 1–7 已完成勿重做)**
   - **一行实现都不改。** 步骤 5 的两处 `LeaderChain = ctx.LeaderChain` 经 Opus 复核**都在位且正确**,缺的只是把 **`EnterCcAsync` 那一处**钉住的用例。
   - **问题**:把 `EnterCcAsync` 里的 `LeaderChain = ctx.LeaderChain,` 删掉 → **83/83 全绿**(Opus 亲手实测)。`EnterApprovalAsync` 那处删掉则 **82/83 变红**(红在 `Snapshot_survives_director_change_between_start_and_multi_leader_node_entry`),所以**两个调用点只钉住了一个** —— 正是步骤 5 判据写明要避免、也是 Round 13 栽过的同款。
   - ⚠️ **模型形状必须让 cc 节点在「改 DirectorId 之后」才被进入,否则这条用例没有区分力**(Round 19 写 prompt 前发现并修正的坑):
     若写成 `start → cc → approval`,cc 在**发起事务内**就解析完了,那时快照与实时链**完全相同**,删掉 `LeaderChain` 也照样绿 —— 等于又造一条假绿用例。
     **正确形状(照抄 approval 那条用例的 gate 套路)**:
     `start → approval#gate(provider=initiator,发起人自批,用来暂停)→ cc(provider=multiLeader, level=2)→ approval#final(provider=initiator)`
   - **改法**:加一条用例(并入 `WorkflowMultiLeaderSnapshotTests.cs`),starter→first→second:
     ① 发起(此时快照 = `[first, second]`,gate 待办挂起)
     ② **把 `first` 的 `DirectorId` 改成诱饵用户 decoy**(`PUT /api/v1/sys/user/{id}`)
     ③ 发起人批掉 gate → 引擎进入 cc 节点解析 multiLeader
     ④ **断言抄送名单含 `second` 且不含 `decoy`**。走快照 = `[first, second]`;走实时 = `[first, decoy]`,**两者不同,才有区分力**。
     取 cc 收件人:**优先用 ③ 那次 approve 返回的 `newCcUserIds`**(`WfEngineResult` 有该字段,`ctx.NewCcUserIds` 在同一事务内累积);不行再走 DI scope 直接查 `WfCc`(参考 `Multi_leader_node_inside_branch_arm_is_covered_by_snapshot` 查 `WfInstance.LeaderChainJson` 的写法)。
   - **判据**:删掉 `EnterCcAsync` 的 `LeaderChain = ctx.LeaderChain,` **必须让该用例变红**;**亲手实测并回报失败用例名与前后计数,然后还原**。
   - **不做**:不改任何实现;不碰 M2b/M3;不动 approval 那条已绿的用例。
   - **验证**:仍是步骤 7 那两条命令,83 → 84 全绿。
   - **本步做完 Task 3 进 review。**

9. **✅ 修复第一批 Findings(Round 21 评审判定 BLOCK;Round 22 exec 已完成,步骤 1–8 未重做)**
   - **① [P1] 改成「按 level 存快照」,彻底删掉 `Take`** —— 这是 Opus 在定案表里写错的一条(「截断即精确」在有停用主管时不成立),不是实现跑偏,**改定案不改责任人**。
     **为什么选这条而不是「接受偏差 + 改文档 + 补用例钉住现有行为」**:审批产品里「谁有权批这一步」不该有静默偏差;而且 `Take` 依赖的「下标即级数」这个不变式已被证明脆弱,**删掉它比给它补文档更省事**。
     **改法**:
     - `WorkflowEngine.ResolveMaxLeaderLevel` → 改成返回**去重后的 level 集合**(方法名一并改成 `ResolveLeaderLevels`),**两侧都用 `Math.Max(1, ...)` 归一化**(顺带修掉 ② 的 P2)。
     - `SnapshotLeaderChainAsync` → 对**每个** distinct level 各调一次 `IApproverResolver`,**每次把该 level 对应节点的真实 `assignee.Params` 传进去、只覆盖 `level`**(顺带修掉 ③ 的 P2)。N 通常 = 1。
     - `WfInstance.LeaderChainJson` 存 `{"2":[...],"3":[...]}` 形状。
     - `ApproverResolveContext` 的 `LeaderChain` → 改成 `IReadOnlyDictionary<int, IReadOnlyList<long>>? LeaderChainByLevel`(**null = 没快照 → 回退实时**;**map 在但缺该 level 的键**只可能是改库造成,**也按回退处理**,注释写明)。`WfExecutionContext` 同步改。
     - `MultiLeaderApproverProvider` → `if (context.LeaderChainByLevel?.TryGetValue(maxLevel, out var chain) == true) return chain;`,**`Take` 整个删掉**;`null`/缺键时走现有实时上溯,一行不改。
     - `EnterNodeOp` 两处照旧透传(字段名跟着改)。
     **判据(三条变异逐个必须变红,亲手实测并回报失败用例名)**:
     ⒜ 去掉 provider 的按 level 取值、改成取 map 里**任意一条**链;⒝ `ResolveLeaderLevels` 改成「只收集第一个节点的 level」;⒞ 归一化 `Math.Max(1,...)` 去掉后 `level:0` 的模型仍能生成快照。
     **配套用例(现有 5 条全是单节点 level=2,`Take` 恒 no-op,所以必须新增)**:模型含**两个 `multiLeader` 节点、level 分别 2 与 3**,链 `starter→l1→l2→l3`,**发起前先停用 `l1`**(`PUT /api/v1/sys/user/{id}/enabled`;必须在发起之前,否则快照里 `l1` 还在,复现不出来);断言 **level=2 的节点只拿到 `[l2]`,level=3 的节点拿到 `[l2,l3]`**。
     ⚠️ **断言形状有坑(Round 22 写 prompt 前发现,别踩)**:`multiLeader` 被 `MapSignMode` **强制 Sequential**,所以正确实现与错误实现下**第一个 Pending 办理人都是 `l2`**。**只断言「第一个待办是 l2」两种情况都绿,等于又造一条假绿用例。**
     必须断言**能区分的东西**,二选一:
     ⒜ 直接查该 task 的 **`WfTaskActor` 全集**(走 DI scope),断言 level=2 的节点只有 **1 个** actor;或
     ⒝ 让 `l2` 批掉,断言流程**推进到了下一个节点**,而**不是**晋级到 `l3`(错误实现下会多出一轮 `l3` 审批)。
     **⒝ 更贴近用户可见行为,优先用它;⒜ 作为补充。**
   - **⚠️ 本改法的已知天花板(必须写进 XML 注释,不许留成隐性行为)**:快照按 **level** 键控,所以**两个 `multiLeader` 节点若 level 相同但其余 `params` 不同**,该 level 只会存一份快照(**先遇到的节点的 params 胜出**)。内置 provider 只读 `level`,所以内置路径无影响;只有**消费者替换了 provider 且按节点传不同自定义参数**时才会碰到。
     **为什么不改成按 nodeId 键控**:那要给 `ApproverResolveContext` 加 `NodeId`,是对 SPI 的又一次扩面,应当单独立项论证,不该搭在一个缺陷修复上。**本轮只修已被证明的缺陷,天花板写明即可。**
   - **② [P2] `level<=0` 归一化** —— 已并入 ① 的改法(两侧都 `Math.Max(1,...)`)。**判据**:`level:0` 的模型必须**生成快照**(而非静默跳过);去掉归一化则该用例变红。
   - **③ [P2] 快照调用传节点真实 `Params`** —— 已并入 ① 的改法。**判据**:构造一个带额外自定义 param 的 `multiLeader` 节点,断言快照调用收到的 `Params` 里**含该键**(可用测试内替身 `IApproverProvider` 捕获入参)。
   - **④ [P2] 补「老实例 → 回退实时」的引擎级用例**(`WorkflowMultiLeaderSnapshotTests.cs`):发起后把 `wf_instance.LeaderChainJson` **置回 null**(走 DI scope 直接更新),再推进到 `multiLeader` 节点,断言**解析出实时主管链**且**没有走 AutoPass**(即该节点真的建了待办)。
     **判据**:把 `DeserializeLeaderChain` 的 `if (string.IsNullOrWhiteSpace(json)) return null;` 改成 `return [];` **必须让该用例变红**(**Opus 已实测:现在这个变异 84/84 全绿**)。
   - **不做**:`BeginTransferAsync` 的死代码(留作对称防御);`"null"` JSON 文本的注释出入;走不到的臂也拍快照的开销;任何 M2b/M3;Task 4 的签核 E2E。
   - **验证**:`dotnet build -c Release` + `dotnet test --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"`,84 → 84+N 全绿。**既有 5 条 multiLeader 用例 + `WorkflowM1RegressionTests` 的「顺序主管」都必须仍绿。**

7. **验证(全绿才算完,输出原样贴回)**
   ```bash
   dotnet build backend/TenonAdmin.slnx -c Release
   dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"
   ```
   基线 **78**,做完应为 78+N 且全绿。**`WorkflowM1RegressionTests` 那条「顺序主管」必须仍绿**(它是 multiLeader 的经典路径:starter→first→second、`level=2`、顺序会签逐级晋级)。

### 读码时发现的陷阱(exec 必看)

- **`maxLevel` 扫描必须走整棵树(含分支臂)**。Task 2 之后 `multiLeader` 节点可以待在臂里;只扫主链会让 `maxLevel` 算小甚至算成 0 → 快照为 null 或过短,臂内节点静默退化成实时查。**这与 Task 2 那条 P1 是同一个家族(一个逻辑只覆盖了主链)**,所以步骤 2 才要复用 `WfModelIndex` 而不是再写一次遍历。
- **`LeaderChain` 的 `null` 与 `[]` 语义不同**,见定案表。`is { Count: > 0 }` 是最自然也最错的写法。
- **老实例必须回退**,否则在途单的下一个 `multiLeader` 节点解析成无人 → 落 `WfNobodyAction` 策略(默认 AutoPass,即**静默跳过审批**),是线上事故且不报错。
- **不要给 `WorkflowEngine` 加构造参数**。Task 2 已加过一次 `IWfConditionEvaluator` 并写了破坏性变更说明;本任务复用 `IApproverResolver`(它已经是 ctx/引擎手上的东西)就够了。
- **`ApproverProviderBase.FilterEnabledAsync` 只在实时分支里**。快照分支不要再过滤一次 —— 快照存的就是过滤后的结果,再滤一遍等于运行期重新判断启用状态,与定案相反。
- **`MapSignMode` 对 `multiLeader` 强制 `Sequential`**(`EnterNodeOp.cs`),与本任务无关,**别动**。
- **CodeFirst 会自动补列**(`DatabaseInitializer` 注释:「表已存在则按实体差异补列,不删列不改窄」),所以新列对既有库是安全的。**但生产有闸门** `EnableCodeFirstInProduction`:关掉自动建表的消费者需要手工加列。**这属于发版说明,不在本任务改代码**,但要在回报里提一句,便于后续 release note。
- `ApproverParamReader.GetInt(params, "level", 1)` 已存在,直接用,别自己解析 `JsonElement`。
- 快照计算发生在**发起事务内**,会读 `SysUser`。这没问题(同事务内的普通读),但**要在 `Insertable(instance)` 之前算完**,免得为了回填再补一次 update。

### 本任务的测试边界

Task 3 自带上面 4 条用例,**风险面与测试面必须在同一侧**:快照的三条可观测出口分别是「发起时算得对不对」「运行期用不用它」「没快照时回不回退」,①②③④ 各钉一条。
**不做**:Task 4 的签核模式 E2E(会签一票否决/或签先表态/顺序逐级晋级);前端展示「将由谁审」(那是后续里程碑的发起页增强,本任务只落数据)。

</details>

## Findings(review 阶段产出;修完划掉)

### 当前任务 Task 8

- 暂无(Task 8 进入 plan)。

### Task 7 复评记录(Round 41–43)

- ~~**[P2][Standards]** 多臂/多叶子默认展开违反 ≤5。~~ ✅ Round42 两层 accordion + 真实组件测试，Round43 双轴 PASS。
- ~~**[P3]** operator 分类重复。~~ ✅ Round42 合并为共享 typed classifier。
- ~~**[P3]** config optional 字段袋缺省语义不一致。~~ ✅ Round42 改 discriminated union + mismatch 原子拒绝。
- **[P3][Round43]** classifier 用 `as Record` 而非 `satisfies Record`，未来 union 扩展时不保证编译期穷尽；记录不阻塞。
- **[P3][Round43]** accordion 测试仍依赖部分 Naive 私有 class，且未直接计数可见 editable 控件；现有真实 DOM/变异已守住当前行为，记录不阻塞。

### Task 6 复评记录(Round 32–38,已全部闭合)

- ~~**[P2][Standards]** 卡片键盘事件冒泡。~~ ✅ Round 33 `.self` 闭合，Round 34/38 复评 PASS。
- ~~**[P2][Standards]** 不完整的 `menu/menuitem` ARIA 模式。~~ ✅ Round 33 移除，Round 34/38 复评 PASS。
- ~~**[P2][Standards]** `WfNodeChain` spacing 未用 token。~~ ✅ Round 33 全量 token 化，Round 34/38 复评 PASS。
- ~~**[P2][Spec]** 固定 arm 宽度与 hidden 裁切。~~ ✅ Round 33 改 `max-content`/无裁切，Round 38 PASS。
- ~~**[P2][Spec]** 超宽 stage 左侧不可滚达。~~ ✅ Round 35 safe-start + Chromium 几何红绿，Round 38 PASS。
- ~~**[P1][Standards]** Chromium 测试误入 Vitest 层。~~ ✅ Round 37 迁入 `web/e2e`，Round 38 双 PASS。

### Task 5 复评记录(Round 29,无阻塞)

- **[P3]** `flattenChain` 名称沿用但语义从主链升级为整树 DFS,内部消费者若误以为仍只含主链会有兼容性判断成本。Task 5 规格明确点名原函数直接树化且仓内仅测试/校验消费,保持 API 名避免无谓扩散;记录不改。
- **[P3]** `validateM1Model` 直接更名为 `validateModel`,没有 deprecated alias。Task 5 明确要求顺带更名,该模块是当前仓设计器内部模块,全部仓内调用点已迁移且 typecheck 绿;不为未声明的外部 API 添加兼容层。

### Task 4 复评记录(Round 26,无阻塞)

- **[P3]** `WorkflowReplaceabilityTests.cs` 的「七件套」与 `CLAUDE.md`/`CONTEXT.md` 历史术语「六件套」不一致。Task 4 规格明确要求把 evaluator 纳入第七个 seam,故测试注释保持事实准确;权威文档术语统一留后续文档整理,不阻塞。
- **[P3]** `WorkflowM2RegressionTests.cs` 期望值引用生产 `WfInstanceStatus`/`WorkflowErrorCode`,枚举序列化值若与测试同编译变化可能同绿。现有仓库所有工作流 HTTP 回归均采用该约定,且本任务四条实现变异已证明关键语义断言有区分力;记录后不在本轮改成裸数字。

### Task 3 第一批(Round 21 评审产出,判定 **BLOCK**;Round 22 exec 处理)

- [x] **[P1]** `Providers/ApproverProviderBase.cs:24-33`(压缩式过滤)× `Providers/BuiltInApproverProviders.cs:65`(`snapshot.Take(maxLevel)`)× `Engine/WorkflowEngine.cs` 的 `ResolveMaxLeaderLevel` — **「一条链按最大 level 拍平 + 运行期 `Take(level)`」在链中有停用主管时会把更高一级主管发成审批人**。(Round 22 已改为按 level 分别存取,删除 `Take`)
  **机理(Opus 已逐行复核确认)**:`FilterEnabledAsync` 是 `ordered.Where(set.Contains).ToList()`,**被过滤的人不留空位**,于是快照的下标不再等于「第几级」,而 `Take(level)` 恰恰依赖下标即级数。**我在 Round 17 定案表里写的「链有序 1..N,截断即精确」,在有停用主管时不成立 —— 这是我的定案错了,不是实现跑偏。**
  **评审的运行时探针**(`starter→l1→l2→l3`,`l1` 停用):`snapshot(max=3) = [l2, l3]`;`level=2` 节点走快照得 `[l2, l3]`(**两个审批人,含第 3 级**),走实时(旧行为)得 `[l2]`(一个)。
  **触发条件**:同一模型里 **≥2 个 `multiLeader` 节点且 `level` 不同** + 链中任一人停用/软删。离职在 HR 场景是停用而非删除,**常见**。
  **后果**:模型只授权到第 2 级的节点,把**第 3 级主管**挂成了审批人 —— 不是「组织调整的代价」,是**模型从未授权的人拿到了审批权**。
  **佐证**:评审的 M2(去掉 `.Take(maxLevel)`)与 M3(`ResolveMaxLeaderLevel` 改成「最后一个赢」而非取最大)**双双 84/84 存活** —— 说明这条定案**根本没有测试守着**(现有 5 条用例全是单个 `multiLeader` 节点、`level` 恒为 2,`maxLevel` 恒等于节点 level,`Take` 恒为 no-op)。
  → **处置见 `## Plan` 步骤 9 的方案 (b):改成按 level 存快照,彻底删掉 `Take`。**

- [x] **[P2]** `Engine/WorkflowEngine.cs` 的 `ResolveMaxLeaderLevel` — `level <= 0` 时**静默跳过整个快照**。(Round 22 已在 `ResolveLeaderLevels` 用 `Math.Max(1, ...)` 对齐 provider,定向变异 8/9 红)
- [x] **[P2]** `Engine/WorkflowEngine.cs` 的 `SnapshotLeaderChainAsync` — 喂给 resolver 的参数包是**从零构造**的 `{level: maxLevel}`,**丢掉了节点真实 `assignee.params` 的其余键**。(Round 22 已复制节点真实 params 后仅覆盖归一化 level,捕获 resolver 用例锁定 `customMarker`)
- [x] **[P2]** `Engine/WorkflowEngine.cs` 的 `DeserializeLeaderChain` — **「老实例 → 回退实时」在引擎层零覆盖**。(Round 22 已补实例级实时链用例 + `null` 反序列化契约用例;指定变异 8/9 红)

### 评审记录但本轮不做的(P3)

- Round 23 Standards 复评:`Entities/WfInstance.cs:43` 的属性名 `LeaderChainJson` 沿用单链术语,而内容现为 `level → chain` 映射;建议名为 `LeaderChainsByLevelJson`。**不阻塞 Task 3**:持久化列已上线且 XML 注释已准确说明按层级映射,本轮不为命名引入额外实体/列映射变更,记录在案。
- `BeginTransferAsync` 传 `LeaderChain` 是**死代码**(评审 M5:改成 `null` → 84/84 存活)。静态确认:`TransferTaskOp` 只改 actor、不推进 token、不 `Plan` 任何 `EnterNodeOp`,转办路径永不调用 resolver。代价是 `LeaderChainJson` 损坏时连转办也会失败。**留作对称性防御,记录在案。**
- `DeserializeLeaderChain` 对 JSON 文本 `"null"` 的行为与自身注释相反(`"[]"` → 非 null 保留快照语义✅;`"null"` → 反序列化出 null → 被当没快照 → 回退)。引擎自己永远写不出 `"null"`,只有直接改库能造,且回退是安全方向。**属注释与行为的小出入,不改。**
- 快照对「永远走不到的分支臂」也会触发一次 provider 调用 + N 次 `SysUser` 往返。属「一条链服务整棵树」定案的自然代价,**记一笔不改**。

### 评审「查过、没发现问题」的(下轮别重复查)

- **快照时机与事务是对的**:`IRepository<>` 是 Scoped 包在单例 `SqlSugarScope` 上,快照读 `SysUser` 走**同一连接、同一事务**;放在 `Insertable(instance)` **之前**反而更安全(此时事务尚无写锁,读不与自己的写锁打架,SQLite 上尤其有意义)。消费者 provider 的写会随事务回滚,**与改动前一致,无新增暴露面**。
- **`SysUser` 不受数据范围过滤影响**(`SysUser : BaseEntity`,非 `DataEntity`/`IOrgScoped`),快照在发起人上下文解析、运行期在审批人上下文解析**不会得出不同结果**。
- **`WfModelIndex` 的 `TryAdd` 去重 / 空 Id 跳过不会让 `ResolveMaxLeaderLevel` 漏节点** —— 发布校验对整棵树强制节点 Id 非空且全树唯一,经正常发布的模型到不了那两条路径。
- **没有第三处实时查主管链**:全后端 `DirectorId` 只出现在 `LeaderApproverProvider` 与 `MultiLeaderApproverProvider`;`ApproverResolveContext` 构造点全后端只有 3 个(`EnterNodeOp` 两处 + `WorkflowEngine` 快照处),前两处都已传 `LeaderChain`。`MenuService` 那条 `ParentId` 上溯是菜单树,与审批人无关。
- **快照没泄漏到任何 DTO**(`LeaderChainJson` 不出现在 `WfRuntimeModels` / `WfInstanceService` 的任何映射里),实例可见者拿不到发起人的主管链。
- **其余 7 个 provider 一字未改**,`LeaderApproverProvider` 未被顺手改动。
- **可替换性达标**:三个新方法全 `protected virtual`;快照走注入的 `IApproverResolver`,消费者替换后**快照期与运行期都走他们的实现**。唯一写死是 P2-2 那个合成参数包。
- `level` 缺失/非数字/溢出 int32 时两侧 `GetInt` 都落默认 1,一致;`level` 极大时 provider 循环由链末端或环检测终止。
- **`BeginCompleteAsync` 的快照透传是真绿**(评审 M4:改成 `null` → **82/84 双红**)。



> **空。Task 2 已收口(Round 16),两批 findings(6 + 2 条)全部修完并逐条实测确认。**
> Task 3 的 review 阶段在此重新填写。

---

### 已归档:Task 2 的两批 findings

**第一批(Round 12 评审,判定 BLOCK;Round 13+14 全修)**:1×P1 + 2×P2 + 3×P3。
P1 = **同一个主链线性扫描在包里有三份**,Task 2 只换掉引擎那份,剩下两份(`WfTaskService`/`WfInstanceService` 的节点名解析)在分支上线后让臂内节点名恒 `null`。**成因是 Round 9 我 grep 的是符号名 `FindNode` 而不是代码形状** —— 教训已固化。两条 P2 = `SelectArm` 语义零覆盖、`conditionsOnNonBranch` 校验强度回退。

**第二批(Round 15 复评,判定 COMMENT;Round 16 全修)**:2×P2,**都是「测试没测它自己声称测的东西」**。
- `SelectArm` 那条**是第一批 P2 的再开**:建了文件、写了 XML 注释宣称钉住它,但断言取值(默认臂配**恒真** Expr)恰好落在两种语义重合的点上 → 变异照样全绿。修法是把恒真 Expr 挪到**后面还跟着一条能命中的条件臂**的那个用例上。
- 待办页缓存键控零覆盖 → 键改常量 `0L` 全绿;失败形态是**显示出别的流程的节点名**,比 `null` 更隐蔽(前端 `||` 兜底遮不住)。

**跨任务教训(三次同族错误,已写进检查项)**:Round 10「E2E 边界划错,风险面落在没测的一侧」、Round 13「修了两个调用点、判据只覆盖一个」、Round 15「断言取值让正确/错误实现答案重合」。共同点都是**判据没有真正的区分力**。
**下笔写判据前必问两句**:① 这次修改涉及**几个**调用点/可观测路径,判据是不是每个都覆盖到了?② 我选的这组输入,**能让正确实现和错误实现给出不同答案吗**?

### 复评明确判定「不是问题 / 现在不动」的(别再翻)

- `WfInstanceService.cs:383` 实例详情链路重复回表 —— **既有问题,非本次回归**,记在案供后续顺手收。
- 嵌套分支引擎 E2E —— `WfModelIndexTests` 已钉死上溯逻辑,引擎层不重复(Plan 早已定不在 Task 2)。
- 臂 `Id`/`Name` 长度校验无用例 —— 影响面为零(只落 `WfHistory.PayloadJson`,该列无长度上限),评审自己判定不作为 finding。
- `WfModelIndex` 是 `sealed`、汇合语义不可替换 —— 与包内既有风格一致,`EnterBranchAsync`/`TakeTransitionOp.ExecuteAsync` 都可覆写,逃生口存在。**若将来 M3 并行需要不同汇合策略,它是第一个要解封的类。**
- `WfDefinitionService` 的 `defaultCount` 检查位置(纯错误优先级,无功能影响)—— 有意不动。
- **全包按代码形状穷举过,没有第四处「同源重复逻辑」**:只剩两次树遍历(运行期 `WfModelIndex.WalkChain` + 发布期 `ValidateChain`);已办/历史列表读 `WfHisTask.NodeName` 落库字段;`RejectToNodeId`/`ReturnToNodeId` 全包零消费方。

## Tasks

> 顺序有意为之:先纯函数(可单测、零风险)→ 再引擎 → 再测试固化 → 再前端模型 → 最后 UI 与验收。
> 每轮只做一项。已有实现则核对测试,缺了再补。

- [x] **1. 条件求值器**(纯后端、零引擎耦合):新增 `Engine/WfConditionEvaluator.cs`,对 `wf_instance.VariablesJson` 求值 `WfConditionExpr`。覆盖 11 个操作符(`eq|ne|gt|gte|lt|lte|in|notIn|contains|empty|notEmpty`)+ `and|or` 组递归。类型策略要写死并注释:数字/字符串/布尔的宽松比较规则、字段缺失时的行为(缺失 ≠ 空值?)、`in`/`contains` 的两侧类型。`virtual` + `TryAdd` 走接口(`IWfConditionEvaluator`)以便消费者替换。**只写求值器和它的单测,不碰 `EnterNodeOp`。**

- [x] **2. `branch` 节点执行**:`EnterNodeOp` 加 `case WfNodeType.Branch`——按 `Conditions[]` 顺序求值,首个命中的臂进其 `Next`;无命中走 `IsDefault` 臂;臂的 `Next` 为 `null` 则直接汇合。`TakeTransitionOp` 处理臂子链走完后**汇合回 `branch.Next`**(当前只认 `FromNode.Next`,臂末节点的 `Next` 是 null 会误判成实例完结——这是本任务的核心陷阱)。追加 `WfHistoryEventType.GatewayTaken`(已有枚举值)。发布期模型校验:branch 必须恰好一条 `IsDefault` 臂、臂 Id 唯一、非默认臂必须有 `Expr`;违规抛 `ModelInvalid`(48002)或按需新增 48021+。

- [x] **3. `multiLeader` 发起时快照**(§十三 13.2 #1):`multiLeader` 主管链改为**发起瞬间**沿 `SysUser.DirectorId` 拍平存进实例(存哪儿要先定:`wf_instance` 新列 vs `VariablesJson` 保留键——倾向新列,免得与业务摘要变量混住),运行时按快照建任务而非现查。其余 7 个 provider 行为不变。补一条测试:发起后改 `DirectorId`,在途单的审批链不变。

- [x] **4. 后端测试固化**:分支两条臂各走一单(命中臂 + 默认臂)+ 臂子链汇合回主链 + 会签一票否决 + 或签先表态即定局 + 顺序会签逐级晋级。**后三项引擎在 M1 就实现了但没有测试**(`CompleteTaskOp.TryPassAsync`),M2a 把它们锁进契约。放 `WorkflowM2RegressionTests.cs`;`WorkflowReplaceabilityTests` 补 `IWfConditionEvaluator` 的可替换性用例。

- [x] **5. `model.ts` 树化**:`flattenChain`/`insertAfter`/`removeNode`/`findNode`/`validateM1Model` 目前全部假设 `.next` 线性链,要改成能走分支臂的树遍历(`validateM1Model` 顺带更名)。新增 `createBranchNode()`、增删臂的操作。**保持框架无关**(不 import 任何 Vue API,§六.2 交付节奏要求 port 时能直接复制)。`model.spec.ts` 同步补齐;注意 M1 的 reactive-proxy 坑——克隆一律走 `cloneModel`/`cloneNode`,不要 `structuredClone`。

- [x] **6. `WfNodeTree.vue` 分支容器**:递归渲染分支臂横排(交互语言学 StavinLi,**DOM/CSS 一行不拷**)、添加 `branch` 节点、臂的增删与改名。仍只改 `web/`。

- [x] **7. 条件编辑器 + `mode` 暴露**:配置抽屉里的结构化条件编辑(字段/操作符/值 + and/or 组,不是脚本输入框);同抽屉暴露审批节点 `props.mode`(或签/会签/顺序会签)。守 §〇 配置纪律:**每节点默认可见项 ≤5**,其余折叠「高级」,全部有默认值,什么都不点也能保存。

- [x] **8. 验收**:`gen:api` 双模板刷 `schema.d.ts`(`web-react/` 也要刷,否则 `contract-drift` 会红——M1 就栽在这)。真实浏览器建一条报销流程「金额>1万 → 总经理审批;否则 → 默认臂直接通过」,发布,**两条臂各发起一单走到完结**,留截图。跑齐 DONE-CONDITION 全部命令。

## 已知起点(2026-08-18 实测,免得重查)

- **M1 已超交付**:空审批人三级可配、会签/或签/顺序计票引擎、发起范围——§八 列在 M2 但已完成,见 §十三 13.1。
- **schema 空壳已就位**:`WfBranchArm`/`WfConditionExpr`/`WfConditionOp`/`WfConditionLogic` 后端类都在(`Schema/WfNode.cs`、`Schema/WfSchemaEnums.cs`),前端 `web/src/workflow/schema.ts` 的 `WfNodeType` 也已含 `'branch'`。**都是没人用的类型定义**,M2a 是给它们接上运行时。
- `EnterNodeOp` 的 `default:` 分支现在直接抛 `NodeTypeUnsupported`(48008)。
- `CompleteTaskOp.RejectInstanceAsync` 里写死 `// M1:忽略 toNode,统一 terminate`——**那是 M2b 的活,M2a 不碰**。
- `IWorkflowNotifier` 是**零方法空接口**,无实现无调用点;`ctx.NewAssigneeUserIds`/`NewCcUserIds` 在累积但没人消费——**M2b 的活,M2a 不碰**。
- 内核实时能力现成:`IRealtimePublisher` + `SignalRRealtimePublisher` + `TenonHub`(M2b 用)。
- **踩过的坑**:`structuredClone` 对 Vue reactive Proxy 抛 `DataCloneError`(`.loop/wf-m1-close.md` Round 2);`npm run typecheck` 三轮全绿也没发现设计器「添加节点」是坏的——**UI 改动必须真实浏览器点一遍**。

## Log

<!-- 每轮追加 ### Round {n} — 动作 / 结果 / NEXT -->

### Round 1 — 任务1/plan

- **动作**:读 `CLAUDE.md`、`CONTEXT.md` 工作流节、设计定案 §二/§四/§十/§十三,以及真实代码 `Schema/WfNode.cs`(`WfConditionExpr` 形状)、`Schema/WfSchemaEnums.cs`(11 个 op + `CamelCaseEnumConverter`)、`Abstractions/WorkflowErrorCode.cs`(已占 48001–48020)、`WorkflowSetup.cs`(TryAdd 注册面)、`Engine/Operations/EnterNodeOp.cs`、`Engine/WfExecutionContext.cs`、`Entities/WfInstance.cs`、`Services/WfInstanceService.cs`(发起路径)、`WorkflowReplaceabilityTests`/`WorkflowM1RegressionTests`(测试约定)。
- **结果**:Task 1 拆成 5 步写入 `## Plan`,附完整语义定案表(缺失字段/宽松比较/空值/组语义/深度)+ 6 条读码陷阱。**本任务不新增错误码**(求值器失败安全返回 false,不抛业务异常),故 48021+ 留给 Task 2 的发布期校验。关键发现:`VariablesJson` 全链路零校验,烂 JSON 必然到达求值器,求值又在引擎事务内 → 不抛异常是硬要求。
- **NEXT**:Round 2 走 exec —— `Agent(executor, model=sonnet)` 照 `## Plan` 实现 4 个文件改动并跑三条验证命令;Opus 只核对绿否与有无占位。

### Round 2 — 任务1/exec

- **动作**:`Agent(oh-my-claudecode:executor, model=sonnet, run_in_background=false)`,喂 `## Plan` 全文 + 硬约束原样。Opus 未写任何产品代码,只核对。
- **结果**:**绿**。三条验证命令 Opus **亲自复跑**(不采信 executor 自报):
  - `dotnet build backend/TenonAdmin.slnx -c Release` → `0 Warning(s) / 0 Error(s)`
  - `--filter "FullyQualifiedName~WfConditionEvaluator"` → `Failed: 0, Passed: 29, Skipped: 0`
  - `--filter "FullyQualifiedName~Workflow"` → `Failed: 0, Passed: 13, Skipped: 0`(M1 回归未受影响)
  核对结论:`git status` 只多出计划内的 3 个新文件 + `WorkflowSetup.cs` 的 3 行;grep 无 `TODO`/`FIXME`/`NotImplemented`/`Skip=`;测试文件 29 条断言均为实断言,无注释掉的断言;`TryAddScoped` 写对(非 `AddScoped`);11 个 op 全有分支且 `default:` 返回 false 不抛;未碰 `EnterNodeOp`/`TakeTransitionOp`/`WfExecutionContext`/前端/`web-react`;未新增错误码、未新增依赖、未 commit。
- **偏差**:executor 自报一处「测试助手键名写错致首跑红,已修,未动产品代码」,复跑绿,可接受。Opus 复核注意到两个**留给 review 判定**的点(不构成 exec blocker):`TryGetValueElement` 与 `LooseEqualsNumberString` 是 `private static`/`private`,未按模板方法全面 `virtual`;`contains` 对「字段是数组」的分支有实现但无对应用例。
- **NEXT**:Round 3 走 review —— `git status --short` + `git diff --stat` 后跑 Codex review,输出若点名无关文件即判定 Codex 子进程挂了、丢弃并改用 `code-reviewer`,并把上面四个改动文件路径显式列进 prompt。

### Round 3 — 任务1/review

- **动作**:先 `git status --short` + `git diff --stat` 锁定范围(3 个新文件 untracked + `WorkflowSetup.cs` +3 行;`docs/workflow/workflow-design-plan-2026-08-17.md` 的改动**早于本 loop**,不属于 Task 1)。跑 Codex companion review。
- **Codex 判定为无效并丢弃**:它点名的 5 个文件(`web/src/components/CronEditor/index.vue`、`web/src/views/workflow/start/index.vue`、`web/src/views/workflow/instance/detail.vue`、`web/src/components/ApiSelect/index.vue`、`web/src/views/workflow/definition/index.vue`)**没有一个在 Task 1 改动集内**。**根因这次查清了**(比「PowerShell 子进程挂了」更准):Task 1 的三个核心文件是 **untracked**,`git diff`(Codex 的 review target = working tree diff)只能看到 `WorkflowSetup.cs` 那 3 行,它没读到求值器一个字,于是靠 codegraph 漫游既有已提交代码编出了一堆前端异步竞态 finding。**教训:本 loop 后续每轮 review 前,新文件要么 `git add -N`,要么继续走 `code-reviewer` 并显式列路径。**
- **改用 `code-reviewer`**(显式列出 4 个文件路径 + 语义定案表 + 范围外清单)。结果:**1×P1 + 7×P2 + 3×P3**,已写进 `## Findings`。含金量够:它在临时目录引真实程序集跑了 30 个探针逐行实测语义表(20 行除 P1 外全部相符),P1 是**实测复现**的,7 条 P2 每条都给了「变异实现后 29 条测试仍全绿」的证据。
- **P1 摘要**:`ParseVariables` 的 `catch (JsonException)` 挡不住落单 UTF-16 代理项触发的 `ArgumentException` → 在引擎事务里炸掉整单发起。HTTP 入口不可达(外层 STJ 先 400),但消费者直调 `StartAsync` 与数据迁移直写库可达。
- **一条 finding 判定为已排期不在本轮修**:`WorkflowReplaceabilityTests` 缺 `IWfConditionEvaluator` 第七条用例 → 台账 Task 4 已明确排了,不提前。
- **NEXT**:Round 4 回 exec —— `Agent(executor, model=sonnet)` 只做 `## Plan` **步骤 6**(修 Findings),跑三条验证命令;Opus 复核绿否 + 变异是否真能抓到,再回 review。

### Round 4 — 任务1/exec(修 Findings)

- **动作**:`Agent(executor, model=sonnet)` 喂 `## Plan` 步骤 6 + `## Findings` 全文 + 语义表 + 硬约束。Opus 未写任何实现代码。
- **结果**:**绿**,11 条 findings 全修。只动了预期的 2 个文件(`Engine/WfConditionEvaluator.cs`、`WfConditionEvaluatorTests.cs`),`WorkflowSetup.cs`/接口文件/前端/`web-react`/台账均未被碰。
  - P1:`ParseVariables` 的 `catch (JsonException)` → `catch (Exception)`(第 46 行),配注释;新增落单代理项用例。
  - P3:`depth >= MaxDepth`(第 73 行)、`LooseEqualsNumberString` 改 `protected virtual`(第 299 行)、`EvaluateContains` XML 注释补 `contains("order100", 100)` 不匹配。**语义表已同步补上这条限制**(见 `## Plan` 的 contains 行)。
  - P2:新增 12 个测试方法,用例 29 → **41**。
- **Opus 复跑的证据**(不采信 executor 自报):
  - `dotnet build -c Release` → `0 Error(s)`
  - `--filter "FullyQualifiedName~WfConditionEvaluator"` → `Failed: 0, Passed: 41`
  - `--filter "FullyQualifiedName~Workflow"` → `Failed: 0, Passed: 13`(M1 回归未动)
  - **独立变异验真**(executor 只给了 `IsEmpty` 那条的证据,Opus 亲手补做本轮最关键的一条):把 `LooseEqualsNumberString` 方法体前插 `return false` → `Eq_covers_loose_and_exact_type_pairs(100, "100", True)` **变红**(`Failed: 1, Passed: 40`)→ 还原 → 重新 `Passed: 41`,并 grep 确认探针字符串已不在文件里。**这证明上一轮那条「零覆盖」的方法现在真被锁住了,不是假绿。**
- **偏差**:无。
- **NEXT**:Round 5 走 review 复评 —— 只评 Round 4 增量。**注意 Round 3 已查明:三个新文件 untracked 会让 Codex 读不到 diff,直接走 `code-reviewer` 并显式列 4 个文件路径,或先 `git add -N` 再跑 Codex。**

### Round 5 — 任务1/review(复评)

- **动作**:`git status --short` + `git diff --stat` 锁范围 → 按上轮记的办法 `git add -N` 三个新文件让 diff 完整可见(747 insertions)→ 跑 Codex review → 验真 → 丢弃 → `git reset` 还原 index → 改用 `code-reviewer` 做复评。
- **Codex 第二次判定无效并丢弃,且上轮的根因假说被证伪**:这次它拿到了完整 diff,却仍点名 `backend/src/TenonAdmin.AspNetCore/Security/HttpContextDataScopeContext.cs`、`backend/src/TenonAdmin.Services/Org/OrgService.cs`(4 条)、`web-react/src/components/UserPicker.tsx` —— **没有一个在 Task 1 改动集内**。所以不是「untracked 读不到 diff」,是**本机 Codex 根本不读 diff、只 codegraph 漫游全仓**。已在 Status 节写死结论:后续不再试 Codex。
  - 附带:它这轮报的是一组机构数据范围越权(IDOR)问题。**未经验证、且完全不在本 loop 范围内**,不予处理也不采信;若要追,应另开一轮独立核实 —— 已记在下面的「场外待办」。
- **`code-reviewer` 复评结论**:**实现侧无 P1**;语义表逐行核过无偏离;Round 4 的三处源码改动**逐条确认到位**且未引入新缺陷;`catch (Exception)` 经论证**不算过宽**(try 块只裹一次 `JsonDocument.Parse(string)`,无 `CancellationToken`,SOE 不可捕获,多吞的只有 OOM 而那正该退成 false)。新出 **4×P2 + 3×P3,全部是测试假绿**(实现对、定案没被用例钉死),每条都附实测变异证据,已写进 `## Findings`。
  - 最有价值的一条 P3:递归用例的 5000 层在深度保护回归时**不是干净的红,而是打爆栈杀死测试宿主**,摘要行还会打出 `Passed! - Failed: 0, Passed: 9, Total: 9` —— 另外 32 条根本没跑却**看起来是绿的**。降到 200 层。
- **Opus 复核**:`git status` 与复评前完全一致(reviewer 的变异已还原),四处修复原样还在(`catch (Exception)`:46、`depth >= MaxDepth`:73、`contains` value 类型判断:279、`protected virtual LooseEqualsNumberString`:299),`--filter WfConditionEvaluator` 仍 **41/41 绿**。
- **NEXT**:Round 6 回 exec —— 照 `## Plan` 步骤 7 补断言,**一行都不改实现**;Opus 复核绿否 + 亲手验至少一条变异,再回 review。

### Round 6 — 任务1/exec(修第二批 Findings)

- **动作**:`Agent(executor, model=sonnet)` 喂 `## Plan` 步骤 7 + `## Findings` 全文 + 「复评已判定不是问题、别去改」清单 + 硬约束。Opus 未写实现代码。
- **结果**:**绿**,7 条全修。只动 2 个文件,**实现行为零改动**(唯一源码改动是把 `LooseEqualsNumberString` 挪到 `LooseEquals` 后面并补 XML summary,签名/可见性/方法体一字未变)。用例 41 → **43**。
  - P2 四条断言按要求塞进**现有**测试方法:`In_and_contains_cover_bypass_branches`(contains 数字 value)、`Empty_covers_...`(非空数组/非空对象两条反方向)、`Leaf_missing_field_or_op_returns_false`(`{op:empty}` 无 field)。
  - P2④ 文本回退 true 路径拆成独立 `[Fact]` `Eq_number_string_falls_back_to_text_compare_when_decimal_parse_fails`(`{"v":1E5}` vs `"1e5"`)—— 因为 `1E5` 这种科学计数原文过不了 `Vars()` 序列化助手,Plan 本就允许这种情况单开 `[Fact]`。
  - P3①递归 5000 → **200**;P3② 已挪位补注释;P3③ 选了**补断言**(新 `[Fact]` `Leaf_with_undefined_json_element_value_is_treated_as_missing_value`,直接构造 `Value = default(JsonElement)` 让 `HasValue=true` 而 `ValueKind=Undefined`,断言 `ne` 仍 false)—— 比改注释更值,因为这条本来就可构造可测。
- **Opus 复跑的证据**:
  - `git status` 只多出预期的 2 个文件改动;grep 无 `TODO`/`Skip=`/变异残留;`catch (Exception)`:46、`depth >= MaxDepth`:73、`IsNullOrEmpty(expr.Field)`:107、`GetArrayLength() == 0`:180、`EnumerateObject().Any()`:181、`value.ValueKind != JsonValueKind.String`:293 **全部原样在位**(证明 executor 的变异都还原干净了)。
  - `dotnet build -c Release` → `0 Error(s)`;`~WfConditionEvaluator` → **43/43**;`~Workflow` → **13/13**。
  - **独立变异验真**:executor 自查了 `contains` 与 `IsEmpty(Array)` 两条,Opus **专挑它没做的第三条**亲手验:删掉第 107 行的 `string.IsNullOrEmpty(expr.Field) ||` → `Leaf_missing_field_or_op_returns_false` **变红**(`Failed: 1, Passed: 42`)→ 还原 → 43/43。这条正是复评指出的「失败**不**安全方向」(`{op:"empty"}` 无 field 会从 false 翻成 true 误命中分支),现已被钉死。
- **偏差**:无。
- **NEXT**:Round 7 走第三轮评审 —— 只评 Round 6 增量。**直接 `code-reviewer` 并显式列文件路径,Codex 已连续两轮判定不可用(见 Status 节)。**

### Round 7 — 任务1/review(第三轮)

- **动作**:`git status --short` + `git diff --stat` 锁范围(仍是那 4 个文件;34 个测试方法 / 43 条用例)。**按台账既定结论跳过 Codex**(连续两轮判定不可用,第二次是在拿到完整 747 行 diff 的前提下仍只点无关文件),直接 `code-reviewer` 并显式列路径 + 语义表 + 前两轮已判定「不是问题」的清单 + 范围外清单。
- **结果:Round 6 增量零缺陷。** 点名的 7 个变异**全部被抓红**(各 42/43):
  - `contains` 的 value 类型判断、`IsEmpty` 的 Array arm、`IsEmpty` 的 Object arm、`EvaluateLeaf` 的 `IsNullOrEmpty(expr.Field)`、`LooseEqualsNumberString` 的文本回退、`TryGetValueElement` 的 `Undefined` 半边、`depth >= MaxDepth`。
  - **上一轮最担心的假绿风险已排除**:`Leaf_with_undefined_json_element_value_...` 是真钉子,失败是干净的断言失败(`Expected: False / Actual: True`)。评审核过机理并点明关键设计 —— 用 `Ne` 而非 `Eq`,因为 `Eq` 在变异下也恰好是 false、钉不住。
  - 递归 5000 → 200 是**严格改进**:摘掉保护后这条用例干净地断言失败,宿主存活,不再出现「`Test Run Aborted` 却先打印 `Passed!`」的伪绿;且 200 层确实仍超 MaxDepth(64),真正区分了保护的有无。
  - `LooseEqualsNumberString` 挪位无行为漂移:全类唯一声明、无重载、无可见性漂移,两条分支都被变异钉死。评审诚实说明了局限——仓库里没有挪位前的产物可做 IL 比对,给的是结构证据 + 行为证据 + 「C# 类内方法声明顺序无语义」的构造性论据。
- **另翻出 5 条既有盲区**(语义表全量变异审计,**非 Round 6 引入**),已写进 `## Findings` 第三批。两条 P2 的故障方向与第一轮那条 P1 同类:一条会**往引擎 DB 事务里抛 `InvalidOperationException`**(`LooseEqualsNumberString` 实参写反),一条会**把脏数据路由进高额分支**(`EvaluateCompare` 的 `return null` 变 `return 0` 使 `gte`/`lte` 翻 true)。评审已预先验证建议改法有效(43 → 47 用例,5 个变异全转红)。
- **评审自己的判定是 COMMENT(不阻断)**,理由是两条 P2 均为既有盲区、实现侧行为正确。**Opus 仍按 loop 规则回 exec 修**——改法只 5 行、评审已验证有效,修比争便宜;但同时**定下收敛判据**(见 `## Findings`):Round 8 修完即收口 Task 1,不再安排第四轮完整评审,避免为一个还没有调用方的纯函数无限投产能。
- **还原状态**:评审报告 `git status --short` 与开始时逐行一致(diff IDENTICAL)、两个被变异文件 md5 与备份一致、无残留探针、43/43 全绿。
- **NEXT**:Round 8 回 exec —— 照 `## Plan` 步骤 8 补 5 条断言(4 条 `InlineData` + 1 条 `Gte` + 1 条嵌套路径),**不改实现**;Opus 复跑三条命令 + 亲手验「实参写反」与「`return 0`」两条变异,然后 **Task 1 收口,进 Task 2 的 plan**。

### Round 8 — 任务1/exec(修第三批 Findings)→ **Task 1 收口**

- **动作**:`Agent(executor, model=sonnet)` 喂 `## Plan` 步骤 8 + 第三批 `## Findings` 全文 + 硬约束。额外在 prompt 里点明 `[InlineData]` 的**参数顺序**(签名是 `(fieldValue, exprValue, expected)`,第一个参数才是字段值),防它把「左字符串↔右数字」写成反方向 —— 这是本步唯一容易写反的地方。Opus 未写实现代码。
- **结果**:**绿**,5 条全修。**实现零改动**(`WfConditionEvaluator.cs` 与本步开始前逐行一致),只动 `WfConditionEvaluatorTests.cs`,**未新增 `[Fact]`**,用例 43 → **47**。
  - `Eq_covers_loose_and_exact_type_pairs` 加 4 条 `[InlineData]`(`("100",100,true)`、`("N/A",100,false)`、`(true,false,false)`、`("true",true,true)`)
  - `Gte_matches_and_mismatches` 加 `Vars("N/A")` 那条(**用 `Gte` 不用 `Gt`**,否则杀不掉 `return 0` 变异)
  - `Field_lookup_is_case_insensitive` 加 `a.b` 嵌套路径断言(executor 自报一处自查:最初误加成新 `[Fact]`,自己发现后并回现有方法 —— 符合「不新增 `[Fact]`」的判据)
- **Opus 复跑的证据**(不采信 executor 自报):
  - grep 无 `TODO`/`FIXME`/`NotImplemented`/`Skip=`;`dotnet build -c Release` → `0 Warning(s) / 0 Error(s)`
  - `--filter "~WfConditionEvaluator"` → **47/47**;`--filter "~Workflow"` → **13/13**(M1 回归未动)
  - 实现侧关键行**全部原样在位**:`catch (Exception)`:46、`depth >= MaxDepth`:73、`IsNullOrEmpty(expr.Field)`:107、`IsEmpty` 的 Array/Object arm:180/181、布尔比较:201-202、`LooseEqualsNumberString(b, a)` **实参顺序未被写反**:208-209、`bool.TryParse(a.GetString(), ...)`:214-215、`EvaluateCompare` 的 `return null`:259
  - **独立变异验真**:executor 实测了「实参写反」(红在 `Eq_covers_...(fieldValue: "N/A", exprValue: 100, expected: False)`,抛 `InvalidOperationException`)与「`return 0`」(红在 `Gte_matches_and_mismatches`)两条;Opus **专挑它没测的第三条**亲手验 —— 把 `:201-202` 布尔比较改 `return true` → `Eq_covers_...(fieldValue: True, exprValue: False, expected: False)` **变红**(`Failed: 1, Passed: 46`)→ 还原 → 47/47,`git status` 与开始时六行逐行一致,无探针残留。
- **收口判定**:按 `## Findings` 收敛判据,Task 1 **不再安排第四轮评审**,直接打勾。三批 findings 累计 23 条全修,用例 29 → 47,期间没有一条是实现缺陷之外的返工。
- **顺手做的台账结构修整**:把「语义定案表」从 `## Plan` 里**提到独立的 `## 语义契约` 节** —— `## Plan` 每进新任务就被整节重写,那张表是跨任务契约(Task 2 引擎侧要照它接),留在 Plan 里下一轮就没了。
- **NEXT**:Round 9 走 Task 2 的 plan —— Opus 亲自 ultrathink,读真实代码(`EnterNodeOp` 的 `switch`/`default: NodeTypeUnsupported`、`TakeTransitionOp` 只认 `FromNode.Next` 这个核心陷阱、`WfBranchArm` 形状、`WfDefinitionService.PublishAsync` 的发布期校验挂点、`WorkflowErrorCode` 的 48021+ 空位),拆 3–8 步重写 `## Plan`。**不写产品代码。**

### Round 9 — 任务2/plan

- **动作**:Opus 亲自读真实代码(未 spawn):`EnterNodeOp`(全文)、`TakeTransitionOp`(全文)、`CompleteTaskOp`(全文)、`WfExecutionContext`、`WorkflowEngine`(三个 `Begin*Async`)、`Schema/WfNode.cs`(`WfBranchArm`/`WfConditionExpr` 真实形状)、`Schema/WfModel.cs` 的 `CreateOptions()`、`WfDefinitionService.ValidateModelForPublish`、`WorkflowErrorCode`(48001–48020)、`WfEnums` 的 `WfHistoryEventType`、`WfSchemaEnums` 的 `WfNodeType`、`WorkflowSetup` 注册面,并 grep 了 `FindNode` 的全部调用方。设计文档 §四/§十三 一并核对。**未写任何产品代码。**
- **结果**:Task 2 拆成 8 步写入 `## Plan`,含 9 行「关键设计定案」表 + 8 条读码陷阱 + 一节「测试边界(防止与 Task 4 打架)」。
- **本轮最重要的发现(台账原先没记)**:`WfExecutionContext.FindNode` **只沿主链 `.Next` 线性扫描**(`:42-50`,注释自己写着「M1 不进 branch 臂」),而它的两个调用方 `CompleteTaskOp.cs:54` 和 `TransferTaskOp.cs:81` 都是 `?? throw ModelInvalid`。**臂内审批节点的待办一旦被审批或转办就直接抛 48002** —— 这比台账原记的「`TakeTransitionOp` 误判完结」更靠前:后者让流程走错,前者让分支功能压根跑不起来。改法是在 `FindNode` 内部换成树索引实现,**两个调用方一行不改**(一处修、全调用方受益)。
- **其他定案**(写进 Plan 免得 exec 自由发挥):分支求值放 `EnterNodeOp` 而非 `TakeTransitionOp`(**设计文档 §四 那张草图与真实代码结构相反,已判定以 §十三 + 代码为准**);**不新增错误码**,分支违规一律 `ModelInvalid` + `reason`,48021+ 继续留空;汇合地址**从模型树现算不持久化**(引擎跨请求无内存态,排他分支下由结构唯一确定);树索引落在**纯 schema 层新类** `WfModelIndex`,可脱离 DB/宿主单测;发布期校验与运行期索引**各走各的遍历**,不为共用而变形。
- **顺手验掉一条假设**:Plan 里断言「超深模型已被 STJ `MaxDepth` 默认 64 挡住,不要再加第二道深度上限」—— Opus 本轮已逐行确认 `WfModel.cs:59-70` 的 `CreateOptions()` 确实没设 `MaxDepth`,该条已从「exec 去确认」升级为「已确认」。
- **NEXT**:Round 10 走 exec —— `Agent(executor, model=sonnet)` 照 `## Plan` 步骤 1–8,跑四条验证命令(总回归必须含 M1 的 13 条 + 求值器的 47 条且全绿);Opus 只核对绿否 / 有无占位 / 是否越界改了前端或 M2b。

### Round 10 — 任务2/exec(实现 1–8)→ **绿但记 blocker,阶段留 exec**

- **动作**:`Agent(executor, model=sonnet)` 喂 `## Plan` 全文 + 硬约束 + 开工前 git 基线。Opus 未写任何实现代码,只核对 + 亲手做变异验真。
- **结果:步骤 1–8 全部落地,命令全绿,实现经复核正确。** 改动集与预期完全一致:5 个既有文件 M(`EnterNodeOp`/`TakeTransitionOp`/`WfExecutionContext`/`WorkflowEngine`/`WfDefinitionService`)+ 3 个新文件(`Schema/WfModelIndex.cs`、`WfModelIndexTests.cs`、`WfBranchPublishValidationTests.cs`);**`WorkflowSetup.cs` 确实零改动**(求值器 Task 1 已 `TryAdd`,DI 自动注入),前端 / `web-react` / `RejectInstanceAsync` 一律未碰,**未新增错误码**(分支违规统一 `ModelInvalid` + `reason`,48021+ 仍空)。
- **Opus 复跑的证据**:`dotnet build -c Release` → `0 Warning(s) / 0 Error(s)`;`~WfModelIndex` → 8/8;`~WfBranchPublishValidation` → 3/3;`~WfConditionEvaluator` → 47/47;`~Workflow` → 13/13;`~Tests.Wf|~Workflow` → **71/71**。grep 无 `TODO`/`Skip=`/`NotImplemented`(命中的全在 `CaptchaServiceTests` 等**既有无关**测试的假服务桩里);executor 的临时探针文件 `_ScratchBranchManualCheck.cs` 已删净。
- **发布校验测试虽只 3 个 `[Fact]` 但覆盖是够的**:中间那条把 5 类违规打包成 5 次断言,且**同时断言 `code` 与 `args.reason`**(比 Plan 判据更严),7 个场景一个不少。
- 🚨 **Opus 亲手做的变异验真翻出 blocker —— 「绿」是假的**:
  - 变异①:`TakeTransitionOp.cs:18` 的 `ctx.ResolveMergeTarget(FromNode)` 改回 `FromNode.Next` → **71/71 全绿**。
  - 变异②:`WfExecutionContext.cs:48` 的 `ModelIndex.Find(nodeId)` 改回 M1 主链线性扫描 → **71/71 全绿**。
  - 即:**Task 2 的两条命门改动,没有任何测试能发现它们被改回去。** 两次变异都已还原,`grep` 确认原文在位、无探针残留、重跑 71/71。
- **责任判定:这是 Round 9 的 plan defect,不是 executor 的问题。** executor 严格照 Plan 的「测试边界」节做了,而那一节是 Opus 划的 ——「E2E 留给 Task 4」把**唯一能观测这两条改动的手段**划到了任务外。`WfModelIndexTests` 测的是索引自己,**引擎有没有用它没人管**。已在 Plan 里把那一节整节推翻重写,并留下通用教训:**划测试边界时先问「这次改动的失败模式落在边界哪一侧」**。
- **顺带修掉一个更早的坑(executor 发现,Opus 实测确认)**:验证命令里的 `--filter "FullyQualifiedName~Workflow"` **根本不匹配 `Wf*` 测试类**(`Workflow` 里没有相邻的 `Wf`),所以 **Task 1 钉死的 47 条从来没进过 DONE-CONDITION**,一直只跑 13 条。executor 建议的 `~Wf|~Workflow` 也不对 —— `~` 大小写不敏感,`Sno**wf**lake` 会被捞进来(实测 78)。**正确写法 `~Tests.Wf|~Workflow`,实测恰好 71 条**;`## DONE-CONDITION` 与 Plan 步骤 8 都已改。
- **NEXT**:Round 11 **仍是 exec** —— `Agent(executor, model=sonnet)` 只做新增的 `## Plan` **步骤 9**(建 `WorkflowM2BranchRegressionTests.cs`,**一行实现都不改**),判据是**上述两个变异逐个变红**并回报失败用例名。绿了再进 review。

### Round 11 — 任务2/exec(补步骤 9 的 E2E 回归)→ **blocker 解除**

- **动作**:`Agent(executor, model=sonnet)` 只喂 `## Plan` 步骤 9 + 两个变异的说明 + 硬约束。额外替它查好了接口事实(历史接口 `GET /api/v1/workflow/instance/history/{id}`、`WfHistoryItemOutput` 透出 `PayloadJson` 所以 `GatewayTaken` 的 payload 可在 HTTP 层直接断言),省掉一轮摸索。Opus 未写实现代码。
- **结果**:**绿**。只新增 `WorkflowM2BranchRegressionTests.cs`(3 条 `[Fact]`),**实现零改动**;`~Tests.Wf|~Workflow` 从 71 → **74**。
  - ① `Arm_with_condition_creates_todo_inside_arm_then_merges_to_branch_next`(amount=200):待办落 `high-approve` → 审批 `code==0` 且实例仍 `Running` → 下一待办 `merge-approve` → 再批 → `Approved`。
  - ② `Default_arm_without_subchain_merges_directly_to_branch_next`(amount=10):待办**直接**落 `merge-approve` → `Approved`。
  - ③ `Gateway_taken_history_records_arm_choice_and_branch_node_enter_leave_once_each`:两条臂各断言 `GatewayTaken` 的 `armId`/`isDefault`,并用 `Assert.Single` 钉死 branch 节点 **NodeEnter / NodeLeave 各恰好一条**。
- **Opus 亲手复验两个变异**(不采信 executor 自报):
  - `TakeTransitionOp.cs:18` → `FromNode.Next`:**Failed: 1, Passed: 73**,红在用例 ①,`Expected: 1 / Actual: 2`(Running → Approved,即整单提前完结)。
  - `WfExecutionContext.cs:48` → M1 主链线性扫描:**Failed: 1, Passed: 73**,同样红在用例 ①,`Expected: 0 / Actual: 48002`。
  - 两次均已还原;`grep` 确认 `ResolveMergeTarget(FromNode)`:18 与 `ModelIndex.Find(nodeId)`:48 原样在位、线性扫描残留计数为 0;最终重跑 **74/74**;`git status` = 基线 + 1 个新测试文件。
- **executor 主动纠正了 Plan 的一处错误主张,判断正确**:Plan 说用例 ②「也杀变异①」,**不成立** —— 默认臂那条路里 `TakeTransitionOp` 的 `FromNode` 是 `branch1` 本身,它在主链上且 `Next` 非 null,`ResolveMergeTarget` 与 `FromNode.Next` 算出来一样。它如实回报而**没有硬塞一条假断言凑数**,这正是本 loop 想要的行为。
- **遗留给 review 的观察**(Opus 只记录,不自评):两个变异**都只被用例 ① 一条杀死**,是结构决定的(差异仅在「节点 `Next` 为 null 且该节点在臂内」时出现)。是否需要再加一条正交用例,交给评审判断。
- **NEXT**:Round 12 走 review —— **直接 `code-reviewer`**,显式列出 9 个改动文件 + 关键设计定案表 + 范围外清单。**Codex 已连续两轮实测不可用,不再试**(见 Status 节)。

### Round 12 — 任务2/review(第一轮)→ **BLOCK,回 exec**

- **动作**:`git status --short` + `git diff --stat` 锁范围(Task 2 = **5 改 + 4 新**,共 9 个文件;`WorkflowSetup.cs`/`docs/review/...`/Task 1 那三个文件属基线,已在 prompt 里显式排除)。**按台账既定结论跳过 Codex**(连续两轮实测不可用),直接 `code-reviewer`,喂了 9 个文件路径 + 9 行关键设计定案表 + M2b/M3 范围外清单 + 已自查的两个变异结果。
- **结果:1×P1 + 2×P2 + 3×P3,判定 BLOCK。** 评审质量高,每条都带实测变异证据,且把「查过没发现问题」的部分逐项写清(见 `## Findings`),下一轮不必重查。
- **P1(Opus 已独立复核确认)**:`WfTaskService.cs:316` + `WfInstanceService.cs:390` 还有**另外两份**主链线性扫描,负责待办/详情的节点名解析。Task 2 让待办第一次落进臂内,这两处同步失效 → 臂内待办 `nodeName` 恒 `null`。Opus 复核方式:`grep` 扫描模式发现全包**三份**副本,并读 `WfTaskService.cs:86` 的 `NodeName = ResolveNodeNameCached(...)` 确认调用链。评审的实测是臂内断言 `Expected: "high-approve" / Actual: null`。
- **两条 P2**:① `SelectArm` 的定案语义**零覆盖** —— 把默认臂改成短路 `return arm` 后 **74/74 全绿**(现有 E2E 里条件臂恒排在默认臂之前,该顺序下坏代码行为一致);② 发布期校验**强度回退** —— 树化后非 branch 节点携带 `Conditions` 从「拒绝」变成「静默接受」,幽灵子树绕过 Id 唯一性与 provider 校验、并被写进不可变发布快照(运行期惰性所以不崩)。
- **评审对我留的问题给了有论证的否定回答,已采纳**:「两个变异只被一条用例杀死」**不是覆盖不足** —— 那两处接线各自只有一个调用点(`CompleteTaskOp:54` / `TakeTransitionOp`),一条用例足以证明线接上了;汇合算法本身已由 `WfModelIndexTests` 7 条独立钉死。`Default_arm_...` 那条**在结构上不可能**杀死变异①(它的 `FromNode` 是 `branch1`,在主链上且 `Next` 非 null)。**所以不补正交 E2E**,测试预算花在 P1 与 P2① 上更划算。
- **本轮我自己的教训**:Round 9 我确实做了「找所有调用方」这一步,但 grep 的是**符号名** `FindNode`,而不是**代码形状**(`for (var n = model.Root; ...)`)。同源重复逻辑不共享符号,按符号名找必然漏。已写进 Status 节。
- **NEXT**:Round 13 回 exec —— `Agent(executor, model=sonnet)` 只做 `## Plan` **步骤 10**(P1 两处改走 `WfModelIndex` + 固化 `nodeName` 断言;P2① 补 `SelectArm` 纯单测;P2② 还原 `conditionsOnNonBranch` 校验 + 测试;P3 改一行注释),**要求它先跑出 P1 的红再修**。Opus 复跑命令 + 亲手验「默认臂短路」变异。

### Round 13 — 任务2/exec(修第一批 Findings)→ **绿但记 blocker,阶段留 exec**

- **动作**:`Agent(executor, model=sonnet)` 喂 `## Plan` 步骤 10 + 每条 finding 的原始证据 + 硬约束 + git 基线。Opus 未写实现代码,只核对 + 亲手做变异验真。
- **结果**:**4 条 findings 全修,77/77 全绿(74 → 77),实现经复核全部正确。**
  - **P1**:`WfTaskService.ResolveNodeNameCached` 的 `modelCache` 由缓存 `WfModel` 改为缓存 `WfModelIndex`,方法体缩成 `index?.Find(nodeId)?.Name`;`WfInstanceService.ResolveNodeName` 缩成 `WfModelIndex.Build(model).Find(nodeId)?.Name`。**没有写第三次遍历**,两处都委托给索引。顺带把待办列表的 O(行数×链长) 降成每页建一次索引 + 每行 O(1)。
  - **executor 给出了要求的「先红后绿」证据**:加断言后未修前 `Expected: "high-approve" / Actual: null`,修完转绿。
  - **P2①**:新建 `WfSelectArmTests.cs`,三行 `Probe` 子类暴露 `SelectArm` + 真实 `WfConditionEvaluator`,两条断言(默认臂排第一时条件臂仍胜出 / 默认臂自带 `Expr` 也不参与求值)。
  - **P2③**:`ValidateNode` 补回 `conditionsOnNonBranch` 校验 + 一条对应测试。
  - **P3④**:只改了 `WfModelIndex.cs` 的一行注释,行为与测试均未动。
  - **明确未做**(与 Plan 一致):`defaultCount` 检查顺序、`WfModelIndex` 解封。
- **Opus 复跑的证据**:`dotnet build -c Release` → `0 Error(s)`;`~Tests.Wf|~Workflow` → **77/77**;grep 无 `TODO`/`Skip=`/`NotImplemented`;`git status` = 基线 + `WfTaskService.cs`/`WfInstanceService.cs` 转 M + 新增 `WfSelectArmTests.cs`,与 Plan 预期完全一致。读 diff 确认两处 P1 修复都是最小改动、无第三次遍历;`WfInstanceService.ResolveNodeName` 每次调用建一次索引,但其**唯一调用点 `:360` 不在循环里**且本来每次调用就要打一次 DB,不构成性能问题。
- **Opus 独立变异验真(专挑 executor 没做的)**:
  - `WfDefinitionService` 的 `node.Type != WfNodeType.Branch` → `false` → **76/77 变红**,红在 `Non_branch_node_with_conditions_is_rejected`。**P2③ 是真钉子。**
  - 🚨 `WfInstanceService.cs:389` 改回主链线性扫描 → **77/77 全绿**。**P1 只钉住了一半**,见 Status 节 blocker。
  - 两次变异均已还原,`grep` 确认两个 Service 里线性扫描计数为 0、无 `if (false` 残留,重跑 77/77。
- **NEXT**:Round 14 仍是 exec —— 照 `## Plan` **步骤 11** 给 `WfInstanceService` 那个调用点补一条断言(**不改实现**),判据是「改回线性扫描必须让它变红」。绿了再进 review 复评。

### Round 14 — 任务2/exec(补步骤 11)→ **blocker 解除,进 review 复评**

- **动作**:`Agent(executor, model=sonnet)` 只喂 `## Plan` 步骤 11 + 硬约束。**Opus 先替它查好了断言路径**(`WfInstanceDetailOutput.MyPendingTask` 在 `WfRuntimeModels.cs:104`,`WfTodoItemOutput` 有 `NodeName`,故信封路径是 `data.myPendingTask.nodeName`),省掉一轮摸索 —— 与 Round 11 同样的做法,同样有效。
- **结果**:**绿**。只改 `WorkflowM2BranchRegressionTests.cs` 一个文件,**实现零改动**,用例总数仍 **77**(断言并进现有用例,未新增 `[Fact]`,符合判据)。
  - 第一次审批**前**:`data.myPendingTask.nodeName == "high-approve"`(**杀变异**)。
  - 第一次审批**后**:同接口 `== "merge-approve"`(**对照组**,证明主链侧没被连带改坏)。
- **Opus 亲手复跑上一轮那个存活变异**:`WfInstanceService.cs:389` 改回主链线性扫描 → **Failed: 1, Passed: 76**,红在 `Arm_with_condition_creates_todo_inside_arm_then_merges_to_branch_next`,`Expected: "high-approve" / Actual: null`。**上一轮同一个变异是 77/77 全绿的,现在死了,证明这条断言是真钉子。** 已还原,`grep` 确认两个 Service 里 `for (var n = model.Root` 计数均为 0,重跑 **77/77**,`git status` 与基线逐行一致。
- **至此 Task 2 的每一处改动都有对应的、经实测的钉子**(七项清单见 Status 节)。这也是本 loop 第一次做到「改动集与测试面完全重合」。
- **两轮连续的同款 plan defect,已固化成检查项**:Round 10 是「E2E 边界划错,风险面落在没测的一侧」,Round 13 是「修了两个调用点、判据只覆盖一个」。共同点都是**我写判据时没有枚举完这次改动的全部可观测出口**。已写进 Status:**每次写判据先问「这次修改涉及几个调用点/几条可观测路径?判据是不是每个都覆盖到了?」**
- **NEXT**:Round 15 走 review 复评 —— 只评 Round 13+14 增量,**直接 `code-reviewer`**(Codex 连续两轮实测不可用),prompt 里带上「已钉死清单」避免重复劳动,并显式列出 Task 2 现在的 **11 个改动文件**。

### Round 15 — 任务2/review(复评)→ **COMMENT,但回 exec 修 2×P2**

- **动作**:`git status --short` + `git diff --stat` 锁范围(Task 2 已涨到 **12 个文件**:7 改 + 5 新)。**跳过 Codex**(既定结论)。顺手查了一件事:`git diff` 报 CRLF 警告 —— 实测三个改动文件是**统一 CRLF**(480/480、87/87、285/285)且仓库有 `.gitattributes`,属正常 Windows 检出,**不是缺陷**,已在 prompt 里告诉评审别报。
- **喂给评审的东西**:12 个文件路径 + 第一轮 6 条 findings 的处理结果 + **「已钉死清单」7 行**(让它别重复验)+ 上一轮「查过没发现问题」清单 + 设计定案 + M2b/M3 范围外清单。**重点点名要它按代码形状再扫一遍全包找「第四处同源重复逻辑」。**
- **结果:判定 COMMENT,无 P1,实现全对。** 评审逐条确认:三处节点名接线正确(缓存语义、**负缓存也生效**、同页同版本确实只建一次索引)、`conditionsOnNonBranch` 三种合法形状均不误伤、探针类继承写法合法、发布期臂内节点确实走完整校验。
- **「第四处重复逻辑」的答案:没有。** 评审按代码形状穷举了全包 49 个源文件,清点出所有「按 nodeId 找节点」的位置全部已走索引;已办/历史列表读的是 `WfHisTask.NodeName` **落库字段**(由 `CompleteTaskOp`/`TransferTaskOp` 经 `ctx.FindNode` 写入,已在索引侧);`RejectToNodeId`/`ReturnToNodeId` 全包**零消费方**(纯 schema 预留)。**全包恰好两次树遍历,符合定案。** —— 这条回答值本轮评审的钱。
- **2×P2,Opus 亲手复跑确认变异都存活**:
  - **`SelectArm` 那条是 Round 12 P2① 的再开**:删 `EnterNodeOp.cs:169` 的 `continue;` → **77/77 全绿**。两条用例都不判别(用例 1 默认臂 `Expr` 为 null、用例 2 默认臂配恒真 Expr —— **恒真恰恰是让两种语义重合的取值**)。评审给了实测有效的修法:只给用例 1 首位默认臂补一个恒真 `Expr` 字段。
  - **待办页缓存键控**:`modelCache` 键换成常量 `0L` → **77/77 全绿**。一页跨定义时后续行会拿第一份模型树解析 `nodeName`,节点 Id 撞名(`approval1` 是设计器默认 Id)就**显示出别的流程的节点名** —— 比修掉的 `null` 更糟,前端 `||` 兜底遮不住。
  - 两次变异均已还原,`grep` 确认无残留(`continue;` 计数 1、`0L` 计数 0),重跑 **77/77**,`git status` 18 行与开始时一致。
- **评审判 COMMENT 而非 BLOCK 的理由成立**(「没有任何缺陷会随本次合入进生产」),**但 Opus 仍按 loop 规则回 exec** —— 两条改法都已实测有效、成本极低,修比争便宜;且 `SelectArm` 这条已经是第二次回来了,再放过就是第三次。
- **NEXT**:Round 16 回 exec —— 照 `## Plan` **步骤 12**(两条 P2,**不改实现**),判据是那两个变异逐个变红。**做完 Task 2 收口进 Task 3 的 plan,不再安排第三轮完整评审**(理由同 Task 1 收敛判据:改法已验证,Opus 复跑 + 亲手验两个变异即可)。

### Round 16 — 任务2/exec(修第二批 Findings)→ **Task 2 收口**

- **动作**:`Agent(executor, model=sonnet)` 喂 `## Plan` 步骤 12 + 两条 finding 的失效机理 + 硬约束。额外替它查好 `Leaf()` 助手已存在、探针类写法已被评审确认合法、待办接口的字段形状 —— 省掉摸索。Opus 未写实现代码。
- **结果**:**绿**,2 条全修,**实现零改动**,用例 77 → **78**。
  - ① `WfSelectArmTests` 用例 1 的首位默认臂补了恒真 `Expr = Leaf("nope", WfConditionOp.Empty, "")`(**只动一个字段**,用例 2 未动,未新增 `[Fact]`)。
  - ② 新增 `Todo_page_resolves_node_name_per_row_when_two_definitions_share_arm_node_id`:发两个定义、臂内节点**同 Id 不同 name**(`A审批节点`/`B审批节点`)、同一用户各起一单、拉整页待办按 `instanceId` 分行断言。
- **Opus 亲手复跑两个变异(本轮重点,因为 `SelectArm` 那条已经逃掉两次)**:
  - 删 `EnterNodeOp` 的 `continue;` → **Failed: 1, Passed: 77**,红在 `Matching_condition_arm_wins_even_when_default_arm_is_listed_first`,`Expected: Id="high" / Actual: Id="default"`。**第三次尝试终于钉住了。**
  - `modelCache` 键改常量 `0L` → **Failed: 1, Passed: 77**,红在新用例,`Expected: "A审批节点" / Actual: "B审批节点"` —— **正是评审预言的「显示出别的流程的节点名」,不是 null 而是错的名字**。
  - 两次均已还原:`grep` 确认 `0L` 计数 0、线性扫描计数 0、`continue;` 计数 1;重跑 **78/78**;`git status` 18 行与基线一致。
- **Task 2 收口判定**:按 Round 15 定的收敛判据(同 Task 1),**不再安排第三轮完整评审** —— 两条 P2 的改法评审已预验证、Opus 又亲手复现了**两个**变异(不是抽验一个)。Task 2 历时 8 轮(Round 9–16),两轮评审共 8 条 findings 全修,**9 项改动全部有实测钉子**,改动集与测试面完全重合。
- **NEXT**:Round 17 走 Task 3 的 plan —— `multiLeader` 发起时快照。Opus 亲自 ultrathink 读真实代码,**先定「快照存哪儿」**(`wf_instance` 新列 vs `VariablesJson` 保留键,台账倾向新列),拆 3–8 步重写 `## Plan`。**不写产品代码。**

### Round 17 — 任务3/plan

- **动作**:Opus 亲自读真实代码(未 spawn):`Providers/BuiltInApproverProviders.cs` 的 `MultiLeaderApproverProvider`(实时沿 `DirectorId` 上溯 + 环路防护 + `FilterEnabledAsync`)、`ApproverProviderBase`、`DefaultApproverResolver`、`Abstractions/IApproverProvider.cs`(`ApproverResolveContext` 形状 + 8 个 provider 键)、`Entities/WfInstance.cs`、`Engine/WfCommands.cs` 的 `StartInstanceCmd`、`Services/WfInstanceService.StartAsync`、`Schema/WfModelIndex.cs` 的公开面、`SqlSugar/DatabaseInitializer.cs` 的建表语义,以及**既有的 `WorkflowM1RegressionTests` 那条「顺序主管」用例**。**未写任何产品代码。**
- **结果**:Task 3 拆成 7 步写入 `## Plan`,含 8 行关键设计定案表 + 8 条读码陷阱 + 测试边界。
- **本轮最重要的定案:快照存 `wf_instance` 新列,不存 `VariablesJson`** —— 台账原来只写「倾向新列,免得与业务摘要变量混住」,读码后发现**真正的理由是提权**:`VariablesJson` 是前端提交、后端全链路从不校验的字段(Task 1 语义契约已锁),发起人只要提交那个保留键就能**自己指定审批链**。已把这条写成「决定性理由,别被后人以少加一列为由改回去」。
- **第二条定案:不给 `WorkflowEngine` 加构造参数** —— 改为复用已经在手的 `IApproverResolver` 算快照。Task 2 刚做过一次源码级破坏性变更(加 `IWfConditionEvaluator` + `required` 成员),一个任务内做第二次不合适;复用 resolver 还顺带让「消费者替换了 `multiLeader` provider」这条路自动生效。
- **发现的最像 Task 2 那颗地雷的陷阱**:算 `maxLevel` 必须走**整棵树含分支臂**。Task 2 之后 `multiLeader` 节点可以待在臂里,只扫主链会让 `maxLevel` 算成 0 → 快照为 null → 臂内节点静默退化成实时查。**同一个家族(逻辑只覆盖主链)**,所以步骤 2 定为复用 `WfModelIndex`(给它加 `Nodes` 枚举)而不是再写一次遍历。
- **另一条容易写错的**:`LeaderChain` 的 `null`(没快照,回退实时)与 `[]`(快照过、链本来就空,**不回退**)语义不同。`is { Count: > 0 }` 是最自然也最错的写法,已写成步骤 4 的判据。
- **回退保底**:本次改动之前发起的在途实例没有快照,provider 必须回退实时上溯 —— 否则它们的下一个 `multiLeader` 节点解析成无人 → 落空审批人策略(默认 `AutoPass`,即**静默跳过审批**),不报错的线上事故。
- **顺手确认的事实**:CodeFirst「表已存在则按实体差异补列,不删列不改窄」,新列对既有库安全;但生产闸门 `EnableCodeFirstInProduction` 关掉时消费者需手工加列 —— 属发版说明,已要求 exec 在回报里提一句。
- **NEXT**:Round 18 走 exec —— `Agent(executor, model=sonnet)` 照 `## Plan` 步骤 1–7;基线 78,做完 78+N 全绿,且 `WorkflowM1RegressionTests` 那条「顺序主管」必须仍绿。

### Round 18 — 任务3/exec(实现 1–7)→ **绿但记 blocker,阶段留 exec**

- **动作**:`Agent(executor, model=sonnet)` 喂 `## Plan` 全文 + 硬约束 + 主上下文预查的事实(`ApproverParamReader.GetInt` 已存在、`Params` 是 `Dictionary<string, JsonElement>?` 需 `SerializeToElement`、`AddUser(..., directorId:)` 助手、`PUT /api/v1/sys/user/{id}`、既有「顺序主管」用例位置)。Opus 未写实现代码。
- **结果**:**步骤 1–7 全部落地,83/83 绿(78 → 83,+5),实现经复核全对。**
  - 快照落在 `WfInstance.LeaderChainJson`(与 `SelectedUserIdsJson` 同构);`ApproverResolveContext` / `WfExecutionContext` 各加一个**非 required** 的 `LeaderChain`。
  - `WfModelIndex` 加 `Nodes`;`ResolveMaxLeaderLevel` 用它扫**整棵树**;`SnapshotLeaderChainAsync` 复用 `IApproverResolver` 且传 `LeaderChain = null` 避免自引用;**`WorkflowEngine` 构造函数签名一字未改**(定案达成)。
  - **`DeserializeLeaderChain` 正确地没有把 null 折叠成空列表**(注释明确写了「与 `DeserializeSelectedUsers` 不同,这里不能把 null 与 `[]` 合并」)—— 这是本任务最容易写错的一处,写对了。
  - 快照在 `Insertable(instance)` **之前**算完;模型无 multiLeader 时 `LeaderChainJson` 保持 null。
  - `MultiLeaderApproverProvider` 用 `is { }`(非 `is { Count: > 0 }`),实时分支一行未动。
- **Opus 复跑的证据**:`dotnet build -c Release` → `0 Error(s)`;`~Tests.Wf|~Workflow` → **83/83**;grep 无 `TODO`/`Skip=`/`NotImplemented`;读 diff 确认上述每条。executor 自测了两个变异(`is { }` → `is { Count: > 0 }` 红在「空快照不回退」;`Nodes` 只返主链 红在两条)。
- 🚨 **Opus 独立变异翻出 blocker(专挑 executor 没测的步骤 5)**:
  - 删 `EnterApprovalAsync` 的 `LeaderChain` → **82/83 变红** ✅(钉住了)
  - 删 `EnterCcAsync` 的 `LeaderChain` → **83/83 全绿** ❌(**没钉住**)
  - 两次变异均已还原(`grep` 计数 2),重跑 83/83,`git status` 22 行。
- **责任判定:又是我的 plan defect。** 步骤 5 的判据写了「两处都要加,漏任一处用例要能抓到」,但**步骤 6 的四条用例没有一条走 cc 节点** —— 判据点名了两个出口、用例只覆盖一个。**这正是 Round 13 之后我写进检查项的那条,却在下一个任务又发生了一次。** 说明「写完判据后再回头核对用例是否逐个对应」这一步必须**真的执行**,而不只是记在台账里。
- **NEXT**:Round 19 仍是 exec —— 照 `## Plan` **步骤 8** 给 cc 调用点补一条用例(`cc` 节点用 multiLeader → 发起后改 `DirectorId` → 断言抄送名单仍是快照),**不改实现**,判据是「删 cc 那处 `LeaderChain` 必须变红」。绿了再进 review。

### Round 19 — 任务3/exec(**未执行**:spawn 被 API 会话上限打断)→ 阶段留 exec

- **动作**:读台账取 Task 3 / exec + `## Plan` 步骤 8;写 executor prompt 前**先自检步骤 8 的用例形状**,发现并修掉一个假绿隐患(见 Status 节);核实两条接口事实;调 `Agent(executor, model=sonnet)`。
- **结果:executor 未能运行** —— 返回 `Agent terminated early due to an API error: You've hit your session limit · resets 9:40pm (America/Los_Angeles)`。**步骤 8 一行都没做。**
- **代码零改动**(已核):`git status --short` 仍是 **22 行**,与 Round 18 收尾一致;`grep -c "LeaderChain = ctx.LeaderChain,"` = **2**(两个调用点都在);基线仍 **83/83**。**Opus 未代写实现或测试**(exec 阶段禁止)。
- **本轮的实际产出是 plan 侧的**:把步骤 8 的模型形状从 `start → cc → approval` 改成 `start → approval#gate → cc → approval#final`。原形状下 cc 在**发起事务内**就解析了,快照与实时链相同,变异照样全绿 —— 会变成第四条「判据没有区分力」的假绿用例。**这是那条检查项第一次在下笔前就拦住问题**(前三次 Round 10/13/15 都是事后实测才发现)。
- **偏差**:无(未执行,不存在实现偏差)。
- **NEXT**:Round 20 **重试 exec** —— 同一份步骤 8(已修正版)重新 spawn `Agent(executor, model=sonnet)`。若仍撞会话上限,继续在台账记一笔并等额度恢复,**不要自己代写测试**。绿了(84/84 且 cc 变异能变红)再进 review。

### Round 20 — 任务3/exec(补步骤 8)→ **blocker 解除,进 review**

- **动作**:重试 `Agent(executor, model=sonnet)`(Round 19 那次撞 API 会话上限),喂已修正版步骤 8 + 硬约束 + 预查事实。Opus 未写实现或测试代码。
- **结果**:**绿**。只改 `WorkflowMultiLeaderSnapshotTests.cs`(新增 1 条用例 + 一个模型构造助手),**实现零改动**,83 → **84**。
  - 模型:`start → gate(approval, initiator 自批) → cc(multiLeader, level=2) → final(approval, initiator)`。cc 夹在两次跨事务审批之间,**改 `DirectorId` 发生在快照之后、cc 解析之前** —— 这正是 Round 19 修掉的那个形状问题。
  - 断言走 approve 响应的 `data.newCcUserIds`(未用 DI/`WfCc` 兜底路径),断言含 `second` 且不含 `decoy`。
- **Opus 亲手复跑那个曾经存活的变异**(本轮重点,这个家族已连栽三次):
  - 用脚本**精确删掉 cc 侧那一行**(按缩进定位,`grep -c` 从 2 → 1,确认没误删 approval 侧)→ **Failed: 1, Passed: 83**,红在 `Cc_node_multi_leader_resolution_uses_snapshot_not_live_director`,失败形态 `Collection: [first, decoy], Not found: second` —— 正是「退回实时上溯」的签名。
  - 还原后 `grep -c` = **2**,重跑 **84/84**,`git status` 22 行与基线一致。
- **至此 Task 3 的两个调用点各自独立有钉子**,approval 与 cc 不再互相顶替。
- **NEXT**:Round 21 走 review —— **直接 `code-reviewer`**(Codex 连续两轮实测不可用),显式列 8 个改动文件 + 设计定案表(快照存列不存 `VariablesJson` 的提权理由、`null` vs `[]`、不加构造参数、老实例回退)+ 已钉死清单 + 范围外清单。

### Round 21 — 任务3/review → **BLOCK,回 exec**

- **动作**:`git status --short` + `git diff --stat` 锁范围(Task 3 = 6 改 + `WfModelIndex.cs` 加 `Nodes` + 2 个测试文件;**特别向评审说明 `EnterNodeOp.cs` 的 64 行 insert 里只有两行属 Task 3,其余是上个任务的**;`WfModelIndex.cs` 仍 untracked 所以 `git diff` 看不到它)。**按既定结论跳过 Codex**,直接 `code-reviewer`,喂 8 个文件 + 8 行设计定案表 + 4 行已钉死清单 + 范围外清单 + 6 个点名要查的问题。
- **结果:判定 BLOCK,1×P1 + 3×P2 + 3×P3。** 评审质量很高:P1 给了**运行时探针**证据,4 次变异实测,并把「查过没发现」的 8 项逐条写清(含一条我原本担心的数据范围过滤问题 —— `SysUser : BaseEntity` 非 `IOrgScoped`,不受影响)。
- **P1 是我的定案错误,不是实现跑偏**:Round 17 定案表写「链有序 1..N,截断即精确」,但 `FilterEnabledAsync` 是压缩式过滤,过滤后下标≠级数,`Take(level)` 的前提没了。Opus 已逐行复核 `ApproverProviderBase.cs:24-33` 与 `BuiltInApproverProviders.cs:65` 确认机理成立。**后果是越权**(level=2 的节点拿到第 3 级主管),不是性能或整洁问题。
- **评审的 M2/M3 双双存活**很关键:它证明这条定案**从来没有测试守着** —— 现有 5 条用例全是单个 `multiLeader` 节点、level 恒 2,`maxLevel` 恒等于节点 level,`Take` 恒为 no-op。**这是「用例形状太单一,判据看起来覆盖了其实恒真」的又一变体**,与本 loop 前几次(Round 10/13/15/18)同族,但这次是**参数维度**单一而非调用点遗漏。
- **Opus 独立复验**:`DeserializeLeaderChain` 的 null 分支改 `return []` → **84/84 存活**,确认 P2-3(老实例回退在引擎层零覆盖,失败模式是静默 AutoPass 跳过审批)。已还原,84/84,`git status` 22 行。
- **处置定案**:P1 不打补丁,**直接删掉「下标即级数」这个不变式** —— 改按 level 存快照(`{"2":[...],"3":[...]}`),provider 按自己的 level 取,`Take` 不要了。这同时一并解决 P2-2(快照参数包丢节点真实 params:改成每个 level 各调一次、各传各的 params)。P2-1 的 `Math.Max(1,...)` 归一化并入同一处。P2-3 单独补一条引擎级用例。三条 P3 明确不做并记录理由。
- **NEXT**:Round 22 回 exec —— 照 `## Plan` **步骤 9**;判据是三条新变异逐个变红 + 新增「两节点不同 level + 停用主管」用例断言 level=2 只拿 `[l2]`、level=3 拿 `[l2,l3]`。

### Round 22 — 任务3/exec(修第一批 Findings)→ **88/88 绿,进 review**

- **动作**:用户要求在 Claude 额度中断处由当前 Codex 接续。先用 CodeGraph 核对 `WorkflowEngine`→`ApproverResolveContext`→`MultiLeaderApproverProvider`→`EnterNodeOp` 的真实调用链;确认上次 executor 在步骤 9 开始前即失败,没有半写残留。只执行步骤 9,未碰 Task 4,未 commit/push。
- **实现**:快照从一条 `List<long>` 改成按 level 键控的 `IReadOnlyDictionary<int,IReadOnlyList<long>>`(`{"2":[...],"3":[...]}`);`ResolveLeaderLevels` 对 level 做 `Math.Max(1,...)`、按 level 去重并保留首个节点真实 params;`SnapshotLeaderChainsAsync` 每个 level 单独解析;provider 用 `TryGetValue(level)` 精确取链,**彻底删除 `Take`**;approval/cc 两处同步透传 `LeaderChainByLevel`。
- **测试**:`WorkflowMultiLeaderSnapshotTests` 5→**9**。新增:① 两个不同 level + 第一级停用,断言快照 2→`[l2]`、3→`[l2,l3]`并走完两节点;② `level:0` 归一化 + 自定义参数 `customMarker` 透传;③ 实例列置 null 后按组织调整后的实时链建待办、不得 AutoPass;④ `null` JSON 必须反序列化为 null。原臂内快照 JSON 断言同步到按 level 形状。
- **计划判据的一处自相矛盾已显式处理**:新定案要求「map 缺 level 键时回退实时」,所以把 null 错反序列化成空 map 后,引擎行为用例本身仍可能绿。没有掩盖它:拆成**用户行为用例**(老实例真的走实时链) + **反序列化契约用例**(必须保留 null),后者负责杀指定变异。
- **四条变异逐个实测,均已还原**:
  1. provider 改成 `snapshot.Values.First()` → **8/9**,红在 `Different_levels_keep_exact_filtered_chains_without_granting_higher_level_approval`;
  2. `ResolveLeaderLevels` 收到首个 multiLeader 后 `break` → **8/9**,同用例 `KeyNotFoundException: key 3`;
  3. 去掉 `Math.Max(1,...)` → **8/9**,红在 `Level_is_normalized_and_custom_params_are_preserved_when_snapshotting`(`Expected [1], Actual [0]`);
  4. `DeserializeLeaderChainsByLevel(null)` 改返空字典 → **8/9**,红在 `Null_snapshot_json_remains_null_when_deserialized`(`Assert.Null`,Actual `[]`)。
- **最终验证**:`dotnet build backend/TenonAdmin.slnx -c Release -nodeReuse:false` → **0 warning / 0 error**;`dotnet test backend/TenonAdmin.slnx -c Release --no-build --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow" -nodeReuse:false` → **88/88**。首次构建被前一次 5 秒超时留下的孤儿 MSBuild 节点锁住测试 DLL;按 PID/父进程/启动时间确认后只结束该批孤儿节点,重跑即绿。
- **NEXT**:Round 23 走 Task 3/review —— 只评 Round 22 增量;重点看按 level 数据形状、同 level 自定义 params 的已知天花板、空 map 缺键回退与四条新测试的区分力。无 P1/P2 则 Task 3 收口进入 Task 4/plan。

### Round 23 — 任务3/review → **PASS,Task 3 收口,进 Task 4/plan**

- **固定点与范围**:HEAD `a9d5351c29d52f13b63d47de8bb74ef4c3758559`,评审其上的未提交 WIP;用 `git status --short` / `git diff --stat` 锁定 Task 3 的 **9 个物理文件**(6 个 tracked 修改 + `WfModelIndex.cs`、`WfModelIndexTests.cs`、`WorkflowMultiLeaderSnapshotTests.cs` 三个 untracked),并明确排除同文件内 Task 2 的 branch 增量。
- **方式**:按 `code-review` skill 分两轴并行独立复评。Standards 轴只对照 `AGENTS.md` / `CLAUDE.md` / `CONTEXT.md` 与仓库风格;Spec 轴只对照设计文档 §13.2 #1 + 本台账步骤 9/Round 22。review 阶段未改产品代码。
- **Standards**:**0×P1 / 0×P2 / 1×P3**。唯一 P3 是实体属性 `LeaderChainJson` 仍沿用单链命名,而内容已是按 level 映射;已记入 P3 历史,不阻塞。
- **Spec**:**PASS**。逐项确认 distinct + `Math.Max(1,...)`、首个节点真实 params、逐 level 独立快照、JSON map、精确 `TryGetValue`、null/缺键实时回退、approval/cc 双出口、同 level 自定义参数天花板注释、四条变异区分力及零 Task 4/M2b/M3/frontend 越界。独立评审定向跑 `WorkflowMultiLeaderSnapshotTests` **9/9**。
- **结论**:无 P1/P2,按 loop 规则勾选 Task 3,当前任务切到 **4**,阶段切到 **plan**。未 commit/push。
- **NEXT**:Round 24 只做 Task 4/plan —— 用 CodeGraph 读 `CompleteTaskOp.TryPassAsync` 及现有 `WorkflowM1RegressionTests` / `WorkflowReplaceabilityTests`,把「分支两臂 + 汇合 + 三种签核模式 + 求值器可替换性」拆成带区分力判据的执行计划;不写实现。

### Round 24 — 任务4/plan → **计划完成,连续进 exec**

- **依据**:按用户要求以本台账为唯一执行源,但不再在每个 phase 后等待人工续跑。先读 `CLAUDE.md`/`CONTEXT.md`/设计 §2、§4、§13 与 Task 4;再用 CodeGraph 读取 `CompleteTaskOp.TryPassAsync`、分支 E2E、M1 签核回归、`WorkflowReplaceabilityTests` 和 `WorkflowSetup`。
- **覆盖审计**:条件臂、默认臂与臂尾汇合已有公开 HTTP E2E;`Any`/`Sequential` 在 M1 有混合用途回归,`All` 缺失;`IWfConditionEvaluator` 已 `TryAddScoped` 但六件套未扩到第七条。
- **定案**:保留既有分支测试并复跑;新增独立 `WorkflowM2RegressionTests` 用纯 `user` provider 对三种签核模式逐条锁契约,避免把 `multiLeader` 强制 Sequential 规则混进计票测试;补 evaluator 前置注册胜出测试。测试 seam 已由 Task 4 明确,符合 TDD skill 的预先确认要求。
- **区分力**:为 All/Any/Sequential/DI 各写一条具体变异,要求逐个转红后还原;预期永久改动仅 2 个测试文件,88 → 92。
- **NEXT**:Round 25 立即走 exec;不等待用户再次输入。

### Round 25 — 任务4/exec → **92/92 绿,连续进 review**

- **永久改动**:仅新增 `WorkflowM2RegressionTests.cs`(All/Any/Sequential 三条独立公开 HTTP E2E),以及修改 `WorkflowReplaceabilityTests.cs`(六件套→七件套、evaluator 前置注册胜出、`FakeConditionEvaluator`)。生产代码永久 diff 为零,未碰既有 M1/branch/snapshot 测试、前端、M2b/M3、docs。
- **TDD 变异验真(均已还原且各自重跑 1/1 绿)**:① All 首票直接通过 → `All_sign...` 预期 Running(1)、实际 Approved(2);② Any 返回未满票 → `Any_sign...` 预期 Approved(2)、实际 Running(1);③ Sequential 首票直接通过 → `Sequential_sign...` 预期 Running(1)、实际 Approved(2);④ evaluator `TryAddScoped`→`AddScoped` → 预期 fake、实际 `WfConditionEvaluator`。
- **计划偏差(按真实合同纠正)**:Plan 写了 `mode=sequential`,但 `WfApprovalMode` 的公共 JSON 值是 **`seq`**;`sequential` 会在发布期校验失败,所以用例改成 `seq` 才真正进入 `WfSignMode.Sequential`。这是 plan 拼写错误,不是实现偏差。
- **验证**:Task4+branch+replaceability 定向 **14/14**;Release build **0 warning / 0 error**;总过滤器 **92/92**。TODO/FIXME/Skip、尾随空白、变异残留、diff-check 均 clean;`CompleteTaskOp.cs` 无永久 diff,`WorkflowSetup` 已恢复 `TryAddScoped`。
- **NEXT**:Round 26 立即走 review;固定点仍为 HEAD `a9d5351c29d52f13b63d47de8bb74ef4c3758559`,但只评本轮两个测试文件(新文件需显式读取,不能只靠 `git diff`)。

### Round 26 — 任务4/review → **PASS,Task 4 收口,连续进 Task 5/plan**

- **方式**:按 `code-review` skill,固定点 HEAD `a9d5351c...`,显式读取 untracked `WorkflowM2RegressionTests.cs` 并只取 `WorkflowReplaceabilityTests.cs` 的本轮 tracked diff;Standards/Spec 两轴并行独立复评。
- **Standards**:0×P1 / 0×P2 / 2×P3。P3 为「六件套→七件套」术语未同步权威文档,以及 HTTP 期望值引用生产枚举而非独立数字常量;均已记入 Findings,不阻塞。
- **Spec**:**PASS**,独立复跑定向 **14/14**。确认 branch 既有 E2E 无重复、三签核模式所有关键状态/冲突/精确晋级、纯 user provider、evaluator 第七 seam、四条变异与范围边界全部符合。
- **结论**:无 P1/P2,勾选 Task 4,进入 Task 5/plan;未 commit/push。
- **NEXT**:Round 27 立即做 Task 5/plan,不等待用户输入。

### Round 27 — 任务5/plan → **计划完成,连续进 exec**

- **读码**:用 CodeGraph 读取 `model.ts` 六个公开操作及 `WfNodeTree`/drawer/designer 调用链,再读 `schema.ts`、`model.spec.ts`、`web/CLAUDE.md` 与后端 `ValidateModelForPublish/ValidateBranch`。确认 `flatten/insert/remove/find/validate` 全是主链线性实现,`conditions` 仍为 `unknown[]`;designer 是 `validateM1Model` 唯一产品调用方。
- **定案**:模型函数是 TDD 公开 seam;DFS 顺序=主链节点→各臂子链→主链后继;局部 insert/remove 不跨汇合;branch 出厂普通臂用恒假空 and 组 + 唯一默认臂;臂新增插默认前、默认不可删;校验原因与后端五条 branch 规则及 conditionsOnNonBranch 对齐。
- **拆解**:4 个预期文件、7 个步骤、7 条定向变异;Task 6 才改 `WfNodeTree` DOM/CSS,本轮只把 `designer.vue` 调用名换成 `validateModel`。
- **NEXT**:Round 28 立即走 exec;先红测后逐切片转绿,最后跑 workflow Vitest/typecheck/lint。

### Round 28 — 任务5/exec → **13/13 绿,连续进 review**

- **永久改动(严格 4 文件)**:`schema.ts` 增 11-op/logic/递归 expr/branch arm 类型 + `WF_M2A_NODE_TYPES`;`model.ts` 用共享确定性 DFS 树化 flatten/find/insert/remove,新增 branch/arm 工厂与不变量操作,`validateModel` 对齐后端;`model.spec.ts` 5→13 条并扩深 reactive clone;`designer.vue` 两处换 `validateModel`。
- **TDD**:先红后逐切片转绿。7 条变异均按计划转红并还原:① flatten 主链-only → 缺三条臂节点;② find 主链-only → 深层节点为 null;③ insert 主链-only → 臂内插入 false;④ remove 主链-only → 臂内删除 false;⑤ validate 主链-only → 漏整树重复 Id;⑥ 去默认臂计数 → 漏 `branchDefaultArmCount`;⑦ clone 换 `structuredClone` → reactive proxy `DataCloneError`。
- **验证**:`model.spec.ts` **13/13**;`src/workflow/` **13/13**;`vue-tsc` 0;`oxlint` 0。TODO/FIXME/.skip/structuredClone/旧 `validateM1Model` 与变异残留扫描 clean;`git diff --check` clean。
- **偏差**:无。操作符确认与后端 camelCase JSON 合同一致。未动 UI DOM/CSS、backend/web-react/docs/ledger 之外内容,未 commit/push。
- **NEXT**:Round 29 立即走 review;固定点 HEAD `a9d5351c...`,只评本轮 4 个 frontend 文件。

### Round 29 — 任务5/review → **PASS,Task 5 收口,连续进 Task 6/plan**

- **方式**:沿用 `code-review` skill 双轴独立评审,固定点 HEAD `a9d5351c...`,范围严格限定 Task 5 四文件。
- **Standards**:0×P1 / 0×P2 / 2×P3。P3 为 `flattenChain` 语义扩为整树但名称未变、`validateM1Model` 更名未留兼容 alias;两项都与台账 Task 5 的明确要求一致且仓内调用已全迁,记录不阻塞。
- **Spec**:**PASS**。独立复跑模型/workflow 均 **13/13**,vue-tsc/oxlint/diff-check 全绿;逐项确认 typed schema、共享 DFS、臂内局部 splice、branch/arm 不变量、后端发布规则、深 reactive clone、designer-only 调用迁移与零 UI 越界。
- **结论**:无 P1/P2,勾选 Task 5,进入 Task 6/plan;未 commit/push。
- **NEXT**:Round 30 立即做 Task 6/plan。

### Round 30 — 任务6/plan → **计划完成,连续进 exec**

- **读码**:CodeGraph 读取 `WfNodeTree→WfAddNode/WfNodeCard→designer` 动态调用链与全部 SFC 源码,核对 model helper、workflow tokens、zh/en 现有 key。确认现模板只有主链循环、add 只支持 approval/cc、branch 卡正文会误落审批人占位,也没有空臂插入 seam。
- **架构定案**:`WfNodeTree` 做 clone/mutate/emit 协调器,新增 `WfNodeChain` 做无状态递归展示;局部链竖排、branch 臂横排、臂尾汇合;新增 `insertIntoBranchArm` 解决空臂头插入并先用 model 红测守住。这样 UI 不复制树查找逻辑,嵌套 branch 由同一递归自然覆盖。
- **边界**:本轮只做结构编辑,不提前写 Task 7 条件/mode;Task 8 再用真实浏览器建模发布。预期 8 文件,三组前端门禁 + build。
- **NEXT**:Round 31 立即走 exec,不等待用户输入。

### Round 31 — 任务6/exec → **前端门禁全绿,连续进 review**

- **永久改动(严格 8 文件)**:`model.ts/.spec.ts` 新增空臂头插 seam;`WfAddNode` 开 branch;`WfNodeCard` 加 branch tone/icon/count;新增无状态递归 `WfNodeChain`;`WfNodeTree` 改唯一 mutation coordinator;zh/en 补最小文案。
- **TDD/变异**:初始测试因 `insertIntoBranchArm` 不存在而红;实现后绿。把正确的 `arm.next=node` 变成 `branch.next=node` → 同测试在 `ordinaryArm.next` 断言红,还原后绿;同时断言两次头插顺序、merge 引用不变、缺 Id=false。
- **UI**:支持新增 branch、横排 arms、空臂/嵌套 branch、臂增删改名、默认臂不可删;递归组件只上抛语义事件,coordinator 统一 clone→helper→emit;native controls 有 aria/focus,样式仅用本仓 token/dark override。
- **偏差**:Plan 误认 `common.isDefault` 已存在,实际仅 `module.isDefault`;为避免 workflow 耦合 module namespace,同步新增 zh/en 的 `common.isDefault`。
- **验证**:model **14/14**,workflow **14/14**,vue-tsc 0,oxlint 0,build exit 0(5771 modules;仅既有依赖/chunk warning)。残留/空白/外部引用扫描与 diff-check clean;未越界、未 commit/push。
- **NEXT**:Round 32 立即走 review,范围严格为本轮 8 文件(含 untracked `WfNodeChain.vue`)。

### Round 32 — 任务6/review → **BLOCK,4 个 P2 回 exec**

- **方式**:固定点仍为 HEAD `a9d5351c...`,按 code-review skill 对 Task 6 的 8 个文件做 Standards/Spec 独立双轴复评;review 阶段未修改产品代码。
- **Standards**:**3×P2**。① 卡片容器的 Space/Enter 会接到内部删除按钮冒泡事件;② `WfAddNode` 声明 `menu/menuitem` 却缺方向键/roving focus;③ `WfNodeChain` 多处硬编码间距违反 `web/DESIGN.md` token 规则。
- **Spec**:**1×P2**。`.wf-arm` 固定 `252px` 且 `overflow:hidden`,嵌套 branch 的子臂总宽度大于父臂时会被裁切,与「嵌套 branch 自然工作」不符。
- **结论**:Task 6 暂不勾选,阶段退回 exec;四项逐条写入 Findings 与计划步骤 8。无 commit/push。
- **NEXT**:Round 33 只修这 4 项并复跑前端全门禁,随后立刻做 Round 34 独立复评。

### Round 33 — 任务6/exec → **4 个 P2 修复，连续进复评**

- **精确增量(只改 3 个既有 SFC)**:`WfNodeCard` 的 Enter/Space 监听加 `.self`;`WfAddNode` 移除容器 `role=menu` 与三个 `role=menuitem`;`WfNodeChain` 的布局间距全部改用 `--space-*`，arm 改为 `flex-basis:auto + width:max-content` 并移除 overflow 裁切。
- **嵌套分支证据**:`arm.next` 仍递归渲染同一 `WfNodeChain`;链与臂均由 `max-content` 撑开，父臂不再固定 252px/hidden;spacing 属性零 px literal。递归 SFC 通过 vue-tsc 与 production build。
- **验证**:model **14/14**;workflow **14/14**;vue-tsc 0;oxlint 0;build exit 0(5771 modules，仅既有依赖/chunk warnings);`git diff --check` clean。未提前 Task 7，未改 ledger/docs/backend/web-react，未 commit/push。
- **NEXT**:Round 34 立即复评 Round 32 四项与 Task 6 整体规格；无 P1/P2 即收口 Task 6。

### Round 34 — 任务6/review → **BLOCK,超宽 stage 仍有不可滚达区域**

- **Standards**:**PASS,0×P1/P2/P3**。Round 32 的键盘冒泡、ARIA 菜单、spacing token 三项全部闭合；复跑 vue-tsc 与 model 14/14 绿。
- **Spec**:**1×P2**。arm 已能按 `max-content` 展开且无 hidden，但外层 `.wf-stage` 仍为 `width:100%; justify-content:center`。独立只读 Chromium 同构探针在 496px 画布/1200px 树下测得 `scrollWidth=848px`、树左边缘 `-342px`，约 350px 左侧内容永远不可达。
- **结论**:Task 6 继续不勾选，回 exec 修画布最外层 overflow seam；这不是 Task 7 扩展，而是让 Task 6 已承诺的嵌套 branch 真正可操作。无 commit/push。
- **轮次预算**:原 max=40 会在两次真实布局复评返工后刚好耗尽，无法覆盖 Task 7/8 的 plan→exec→review；为服从用户「一直完成 M2」且不跳过质量门，透明延长为 **max=50**，任务范围不变。
- **NEXT**:Round 35 改 `designer.vue` stage 为 `width:max-content; min-width:100%` 或等价 safe-start 布局，补几何回归并跑全门禁。

### Round 35 — 任务6/exec → **Chromium 几何红绿闭环，连续进最终复评**

- **永久改动(仅 2 文件)**:`designer.vue` 的 `.wf-stage` 从 `width:100%` 改为 `width:max-content; min-width:100%`，保留 flex/justify-center；新增 `workflow/layout.spec.ts`，读取真实 SFC `?raw` 样式并用 `@playwright/test` Chromium 在最小 DOM 实测几何。
- **TDD/变异**:旧 CSS 初始红与反向恢复变异都精确得到 `canvasLeft=0,width=496,scrollWidth=848,treeLeft=-352,treeRight=848,treeWidth=1200`，红在“超宽树从画布起点完整可达”；220px 窄树居中用例始终绿。修复后两条 **2/2** 绿。
- **验证**:workflow Vitest **16/16**;vue-tsc 0;oxlint 0;build exit0(5771 modules，仅既有 warnings);diff-check 与新文件尾随空白 clean。无变异残留、无 Task7 越界、未 commit/push。
- **NEXT**:Round 36 立即双轴复评这 2 文件和 Task 6 整体；无 P1/P2 则勾选 Task 6。

### Round 36 — 任务6/review → **BLOCK,测试分层 P1**

- **Spec**:**PASS,0×P1/P2/P3**。独立 Chromium 复验宽树 `left=0,right=1200,scrollWidth=1200`，窄树中心=248px；旧 CSS 内存变异精确红。arm/递归事件/coordinator 均保持正确。
- **Standards**:**1×P1**。`layout.spec.ts` 位于 `src/**/*.spec.ts`，会被 `npm test` 收集并启动 Chromium；但 `.github/workflows/web-ci.yml` 只 `npm ci`，Playwright 1.61 不会 postinstall browser，clean runner必然报 executable missing，也违反 `web/CLAUDE.md` 的 Vitest/Playwright 分层。
- **结论**:产品 CSS 已正确，不回退；仅把几何回归迁进既有 `web/e2e` Playwright 层并保持真实样式/区分力。Task 6 继续不勾选，未 commit/push。
- **NEXT**:Round 37 修测试分层，随后 Round 38 最终复评。

### Round 37 — 任务6/exec → **P1 闭合，连续进最终复评**

- **永久改动**:删除未跟踪 `src/workflow/layout.spec.ts`，新增 `e2e/workflow-layout.spec.ts`；用 `node:fs` 从磁盘读取真实 `designer.vue` scoped style，Playwright `page` fixture 实测宽/窄树，不复制产品 CSS。本轮未触碰 Round35 已正确的 stage CSS。
- **变异验真**:定向 Playwright 初次 **2/2** 绿；内存旧 `width:100%` 变异后 **1 红/1 绿**，宽树 `left=-352/scrollWidth=848` 精确红、窄树仍居中；清除变异后最终 **2/2** 绿。
- **分层证据**:`npx vitest run src/workflow/` 仅 1 file/**14/14**、tests=24ms/总1.81s，不启动浏览器；`src/workflow/layout.spec.ts` 已不存在。
- **验证**:typecheck、lint、build(5771 modules，仅既有 warnings)、diff-check 全绿；无变异残留、无 Task7 越界、未 commit/push。
- **NEXT**:Round 38 最终复评；无 P1/P2 即收口 Task 6。

### Round 38 — 任务6/review → **双 PASS，Task 6 收口，连续进 Task 7/plan**

- **Standards**:**PASS,0×P1/P2/P3**。确认旧 Vitest 布局测试不存在、`src/**/*.spec.ts` 不收集 `web/e2e`、Playwright 能发现新 2 条用例；fs URL 跨平台、page fixture 自动隔离关闭、CSS/token 无回归。
- **Spec**:**PASS,0×P1/P2/P3**。迁移后仍读取真实 SFC style；宽树 `left=0/right=1200/scrollWidth=1200`、窄树中心=248；旧 CSS 变异仍精确红。stage/arm/递归七事件/coordinator 全部满足 Task 6。
- **结论**:Round 32–36 的 6 个阻塞项全部闭合，勾选 Task 6，当前任务切到 **7/plan**。未 commit/push。
- **NEXT**:Round 39 立刻拆 Task 7 条件编辑器与审批 mode；不等待用户输入。

### Round 39 — 任务7/plan → **计划完成，连续进 exec**

- **读码/合同**:用 CodeGraph 读取 `WfConfigDrawer→designer` 数据流、前后端 `WfConditionExpr`/11 op/`WfApprovalMode`，并核对求值器 value 类型语义。确认真实缺陷是 drawer 第 123 行每次保存都硬写 `mode='any'`，且 branch 当前会误走办理人配置分支。
- **定案**:新增框架无关 `workflow/configuration.ts` 作为 TDD seam，统一条件树不可变操作与 node 配置应用；递归 `WfConditionEditor` 只 emit 新表达式；drawer 分 start/branch/approval|cc 三路。multiLeader 与后端强制顺序语义对齐为 `seq`。
- **值控件**:数值比较→number，in/notIn→tags 数组，empty/notEmpty→无值，其余→文本；后端既定 loose 规则承担数字/布尔文本，不引脚本或 raw JSON 输入。
- **范围**:预期严格 6 文件，仅 `web/`；真实浏览器发布与两臂运行留 Task 8。未写产品代码、未 commit/push。
- **NEXT**:Round 40 立即 exec，先 `configuration.spec.ts` 红，再逐切片转绿与变异验真。

### Round 40 — 任务7/exec → **26/26 与全门禁绿，连续进 review**

- **永久改动(严格 6 文件)**:新增 `workflow/configuration.ts/.spec.ts`、新增递归 `WfConditionEditor.vue`、修改 `WfConfigDrawer.vue`、同步 zh/en。无第 7 文件、无 Task6/model/backend/web-react/API/docs 越界。
- **生产 seam**:start/branch/approval|cc 均走 `applyNodeConfiguration`;approval 保存 any/all/seq，multiLeader 展示/保存 seq；branch 按 armId 写非默认 group expr，默认臂不动且不写 assignee/mode；历史 leaf 根加载时包进 and group，seam 拒绝 leaf 根；深层 branch/unknown id/immutability 全覆盖。
- **UI**:递归 and/or、叶/组增删、11 op；number/tags/none/text 控件；native button 有 title/aria；新增 CSS 全用 token。默认可见项 approval=4/cc=3/branch=2。
- **TDD/变异**:首红 `Failed to resolve import ./configuration`;最终 12 测。5 项变异逐个红并恢复:hardcode any、multiLeader 不归一、branch 只写第一臂、empty 留 value、remove 错 index；额外 leaf-root 变异也红后收紧。
- **验证**:configuration **12/12**;workflow **26/26**;typecheck/lint/build exit0(5775 modules，仅既有 warnings);本轮 zh/en key 27/27 差异0；残留/structuredClone/硬编码/尾空/diff-check clean。全仓既有英文-only role key 不在本轮范围。未真实发布、未 commit/push。
- **NEXT**:Round 41 立即双轴 review，固定点仍为 HEAD `a9d5351c...`，范围只含这 6 文件。

### Round 41 — 任务7/review → **BLOCK,默认展开违反配置纪律**

- **Spec**:**PASS,0×P1/P2/P3**。11 op/递归/root group/default/armId/历史 leaf/mode/multiLeader/start+cc/空组/变异与范围全部符合；复跑 configuration 12/12、workflow 26/26、typecheck/lint/diff-check 绿。
- **Standards**:**1×P2 + 2×P3**。P2:branch 抽屉所有 arm/leaf 默认展开，默认可见项无上限，违反 ≤5。P3:operator 分类两份；config optional 字段袋缺省语义不一致。
- **结论**:Task 7 暂不勾选，回 exec 做两层 accordion；两项 P3 与同一 seam/组件直接相关，趁本轮一并收敛。允许新增第 7 个组件测试文件，仍不提前 Task 8。未 commit/push。
- **NEXT**:Round 42 修复并全验，Round 43 复评。

### Round 42 — 任务7/exec → **P2/P3 全修，连续进复评**

- **聚合范围(严格 7 文件)**:原 Task7 六文件 + 新 `WfConditionEditor.spec.ts`；Round42 未再改 locale，无 Chromium/Task8/backend/web-react/API 越界。
- **P2 修复**:drawer arm accordion 默认只展开第一条非默认臂；condition children accordion 根层默认只开第一项、nested 后代默认折叠。组件测试真实挂载 Vue+Naive `WfConfigDrawer/NDrawer/NCollapse`，teleport 稳定，2/2 绿。
- **P3 修复**:operator typed classifier 由 configuration 单点导出、editor共用；`WfEditorNodeConfig` 改 start/branch/approval/cc discriminated union，apply 拒绝 node/config type mismatch。configuration 增至 14/14。
- **变异**:根 children 全展开→组件看到 3 active/期望1 红；移除 type mismatch guard→错误返回已改模型/期望null 红；均还原同测绿。
- **验证**:component **2/2**;configuration **14/14**;workflow **28/28**;typecheck/lint/build exit0(5775 modules，仅既有 warnings);workflow locale 160/160 差异0；残留与 diff-check clean(全仓仅既有 backend CRLF 提示)。未 commit/push。
- **NEXT**:Round 43 最终 review；无 P1/P2 则勾 Task7 进 Task8/plan。

### Round 43 — 任务7/review → **PASS,Task 7 收口，连续进 Task 8/plan**

- **Spec**:**PASS,0×P1/P2/P3**。accordion 未改保存 seam；default-first 时仍开首非默认 arm，root 首 child/nested 折叠与重挂不丢值；组件测试能杀全展开/错误首臂。11-op classifier/union 无规格回归。复跑 component 2/2、workflow 28/28、typecheck/lint/diff-check 全绿。
- **Standards**:**0×P1/P2 + 2×P3**。P3 为 classifier 的 `as Record` 非编译期穷尽，以及测试依赖部分 Naive 私有 class/未直接计数 visible editable controls；两项记录不阻塞。
- **结论**:无 P1/P2，Round41 P2 与两项 P3 修复均闭合，勾选 Task 7，切到 **8/plan**。未 commit/push。
- **NEXT**:Round44 立即计划 API 双生成、真实浏览器报销两臂与所有 DONE-CONDITION 门禁。

### Round 44 — 任务8/plan → **计划完成，连续进 exec**

- **读码/基建**:用 CodeGraph 与真实源码核对 Playwright config/helpers、designer create/save/publish、start 变量数值 coercion、todo/detail 批准路径、WorkflowMenuSeed 路由与 contract-drift 双 schema 要求。
- **定案**:永久新增一个 UI-first `workflow-m2a.spec.ts`;定义/发布/两单发起/高额批准均走浏览器，不用 API 偷建定义或实例。模型=`amount>10000→总经理审批;default→直接完结`。
- **证据**:三张截图固定写 `.loop/wf-ui-shots/m2a-01..03`;每张前有状态断言。双 schema 由同一临时 5100 host 顺序生成并核 hash；精确清理本轮进程。
- **门禁**:backend DONE filter、web workflow/typecheck/lint/build、定向 Playwright business+layout、web-react typecheck、diff/residue/process/screenshot 审计全绿后才勾 Task8。未写产品代码、未 commit/push。
- **NEXT**:Round45 立即 exec，不等待用户输入。

### Round 45 — 任务8/exec → **真实两臂验收与 DONE 门禁完成，连续进 review**

- **API 双生成**:5100 已有早于本轮的 PID11396 MinimalHost，health 200/openapi 572113 bytes；按安全规则未启动/未停止它，顺序执行 web/web-react `gen:api`，两端 SHA `9F016AF...E632EB0` 相同、零 diff。该复用是本轮唯一计划偏差，已获安全确认并记录。
- **永久证据**:仅新增 `web/e2e/workflow-m2a.spec.ts`(175 行) 与三张 1280×720 截图：`.loop/wf-ui-shots/m2a-01-designer-published.png`、`m2a-02-high-approved.png`、`m2a-03-default-approved.png`。产品代码零改动。
- **真实浏览器**:UI 建定义→插 branch/approval→配 `amount > 10000`→指定 superAdmin→发布；20000 单实际 Running/总经理审批待办→浏览器批准→Approved；5000 单默认臂直接 Approved。high 图已等待 modal 退场，目检无遮挡且含审批记录；default 图无审批历史。
- **反假绿**:临时阈值 `10000→30000` 时高额 Running 断言准确红(20000 落默认臂)，还原后业务 E2E 1/1 绿。
- **最终门禁**:backend Release build 0 error/13 既有 warning；精确 filter **92/92**；web workflow **28/28**、typecheck/lint/build 全绿；layout+M2a Playwright **3/3**；web-react typecheck 绿；残留/direct API/临时进程审计 clean。仅保留获准复用的早于本轮 5100 PID11396/父19108。未 commit/push。
- **NEXT**:Round 46 双轴复评 Task8，随后最终 DONE-CONDITION 审计。

### Round 46 — 任务8/review → **双轴通过，进入最终审计**

- **Spec**:**PASS,0×P1/P2/P3**。永久 E2E 175 行全程 UI-first；发布响应/模型可见；20000 先 Running+Approve、todo businessKey 锁行断言总经理审批、浏览器办理后 Approved；5000 不同 businessKey/instanceId 直接 Approved 且无 Approve；阈值反变异已红。三图 1280×720 无 loading/error/modal。双端 gen 同源同 SHA、零 diff；门禁证据完整。
- **Standards**:**0×P1/P2 + 1×P3**。E2E 部分选择器依赖 Naive 私有 class/结构，未来组件库升级时稳健性较弱；本轮只记录，不阻塞验收，业务断言和现有真实运行均通过。
- **结论**:Task8 无 P1/P2，进入 Round47 最终审计。未 commit/push。
- **NEXT**:核对 DONE-CONDITION 全部勾选、截图/进程/残留与工作树，完成 M2a。

### Round 47 — 最终审计 → **M2a 完成，8/8 任务勾选**

- **DONE-CONDITION**:ledger Tasks **8/8 `[x]`**；backend 精确 filter **92/92**；web workflow **28/28**、typecheck、lint、build 全绿；web-react typecheck 绿；真实 Playwright layout+M2a **3/3**；双 schema 同源 SHA 一致且零 diff。
- **业务证据**:三张截图均存在、1280×720、无遮挡且已目检：发布设计图、高额 20000 已通过并有总经理审批记录、默认 5000 已通过且无审批历史。E2E 阈值反变异已红后还原。
- **安全/范围**:E2E 无 `waitForTimeout`/sleep、无 direct API 建模绕行；5100 仅复用更早 PID11396，仍在监听且未被本轮启动/停止；无 Playwright/Vite 临时进程；残留/TODO/FIXME/.skip/变异/direct API 扫描 clean；`git diff --check` 无 whitespace error（仅既有 CRLF 提示）。
- **复评结论**:Task 4–8 均通过独立 Standards/Spec 复评，无未处理 P1/P2；仅保留已记录的 P3（E2E 对少量 Naive 私有 selector、classifier exhaustive map/test seam 可再加强），不影响完成条件。
- **工作树**:所有改动保持未提交 WIP，未 commit/push；未删除用户既有改动。M2a DONE。

## 场外待办(不属于本 loop,记着别丢)

- Codex 在 Round 5 报了一组**机构数据范围越权**疑点(`HttpContextDataScopeContext` 未绑定 scope 时按不受限处理;`OrgService` 的 update/copy/root 变更未校验 `id` 是否在范围内;`web-react/UserPicker` 请求页大小被后端 clamp 到 200 导致回显不全)。**来源不可信**(同一次输出里它连本任务改了什么都没看见),**且不在 M2a 范围内** —— 本 loop 不处理。若要追,需另起一轮独立核实真伪。
