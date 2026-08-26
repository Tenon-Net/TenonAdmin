# Loop: TenonAdmin.Workflow M2b 动词与时效

## GOAL

在 M2a(已收口,四项 CI 全绿,commit `b0d796e`/`a2bd9c6`/`dce7e75`)基础上做 **M2b**:退回/撤销/委托/催办/超时 Job/同一人相邻节点去重/`IWorkflowNotifier` 落地(接 `IRealtimePublisher`)/`btnInfo`;前端(仅 `web/`)加抄送列表、我发起的/我已办的、流程图回放、实例列表按参与筛选。范围与定案见 `docs/workflow/workflow-design-plan-2026-08-17.md` **§十三 13.3**「M2b 动词与时效」行,语义默认值见 `CONTEXT.md` 「工作流」节「行为语义默认值」条。

**禁止做 M3**(动态表单/`formPerms`/并行/webhook/加减签/比例票签/长期委托/React port)。**不改 `web-react/`**,除了最后一个任务的 `gen:api` 刷 `schema.d.ts`。不抽 `web/` 与 `web-react/` 共享层。**长期委托规则(定时/条件触发)不做**——本 loop 的「委托」只做单次、发起人手动指定的一次性委托,对齐 §十三 13.3 明确写的「委托」而非 CONTEXT.md 未提及的规则委托。

## DONE-CONDITION

- 本账本 `## Tasks` 全部打勾
- `dotnet test backend/TenonAdmin.slnx --filter "FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow"` 绿(**基线 137**,Task 5 的 Round 22 修 Findings 后 2026-08-24 实测;M2b 只增不减。台账早期写的 92/114/123 是各阶段旧基线,不要再引用)
- `cd web && npm run typecheck && npm run lint` 绿,`npx vitest run src/workflow/` 绿
- 真实浏览器至少走通:退回一单、撤销一单、催办一次、抄送列表已读、我发起的/我已办的列表可见——留截图证据
- 双模板 `gen:api` 后 schema SHA256 一致

> ⚠️ 沿用 M2a 修正过的过滤器写法,**不要**回退成 `~Workflow`(漏掉 `WfXxxTests`)或 `~Wf|~Workflow`(误拉 `Snowflake` 系列)。

## Status

- 轮次: 41
- max: 60
- 当前任务: 14
- 当前阶段: review
- 上一轮: Round 41 — **Task 14 review(独立复核)**。指定过滤器亲手 **190/190**;`cd web && npm run typecheck`/`lint` exit 0;`npx vitest run src/workflow/` **29/29**。双 schema SHA256 一致 `2BAF0080CBCF1669B5CB11ECE0FA4A76331CACE4EE984E08DEB8D173A6B460DC`。截图 `m2b-01`…`07` 均在盘且非空。`ButtonLabels` 加 `[JsonIgnore]` → 往返测试转红后复原。`web-react/` 只改 `schema.d.ts`;引擎写路径零 diff。0×P1 / 0×未修 P2。Task 14 **勾选**。Tasks 1–14 全勾。DONE-CONDITION 闸门与截图/哈希已独立核过(本轮未重跑 Playwright,证据是盘上 spec + 七张图)。
- 上一轮(历史): Round 40 — **Task 14 plan+exec**(`btnInfo` + 抽屉高级字段 + 详情动词 + 双 gen:api + Playwright 验收)。`WfButtonLabels` 经 `WfModelJson` camelCase 往返(+1,指定过滤器 **190/190**)。抽屉默认可见仍 ≤5,退回/拒绝/超时/按钮文案进「高级」;保存默认写出 `returnPolicy=prev`。详情补 return/delegate/urge/cancel/resubmit,催办用 `CurrentTaskId`。`web` typecheck/lint 绿,`src/workflow/` vitest **29/29**。双 schema SHA256=`2BAF0080CBCF1669B5CB11ECE0FA4A76331CACE4EE984E08DEB8D173A6B460DC`。Playwright `workflow-m2b.spec.ts` 1/1,截图 `m2b-01`…`m2b-07`。`web-react/` 只改 `schema.d.ts`。`EnterCcAsync`/引擎写路径未动。**不勾选 Task 14**。
- 上一轮(历史): Round 39 — **Task 13 review(独立复核)**。代码是事实源。指定过滤器亲手 **189/189**;`cd web && npm run typecheck` exit 0,`npm run lint`(oxlint) exit 0,`npx vitest run src/workflow/` **28/28**。`web-react/` 空;`EnterCcAsync`/引擎写路径零 diff。亲手变异(全复原):丢掉 last-visit cutoff → 两条回放 **双红** `DoesNotContain node2` / Actual `[start,node1,node2]`;丢掉 starter/actor/cc 任一 `WhereIF` → `Monitor_page_filters…` 各红在 :146/:151/:156;去掉 `CanMonitorInstancesAsync` 整段豁免 → :159 `Expected: 0 / Actual: 48015`;去掉 48015 throw → :163 `Expected: 48015 / Actual: 0`。`rg MUTATION`/`REVIEW-PROBE` 零命中。0×P1 / 0×未修 P2。Task 13 **勾选**。未开 Task 14。
- 上一轮(历史): Round 38 — **Task 13 plan+exec**(流程图回放 + 监控参与筛选)。`GetAsync` 透出实例版本 `Model` + `VisitedNodeIds`(最后一次 `RejectRouted`/`TaskReturned`/`InstanceResubmitted` 之后的 `NodeEnter`)+ `CurrentNodeIds`。`EnsureParticipantAsync` 对超管 / 持有 `GET:/api/v1/workflow/instance/monitor` 的管理员放行,路人仍 48015。新 `GET instance/monitor`(`[RolePermission]`),**不** `ClearFilter<IOrgScoped>()`。菜单 `+26`/`+13`。Vue 复用 `WfNodeTree` 只读态 + `monitor/index.vue`。指定过滤器 **189/189**(基线 185 + 4)。`typecheck`/`lint` 绿,`src/workflow/` vitest **28/28**。`rg MUTATION`/`REVIEW-PROBE` 零命中。**不勾选 Task 13**。未开 Task 14。`web-react/` 零改。不跑 `gen:api`。
- 上一轮(历史): Round 35 — **Task 11 exec 修 Findings**。补 `Starter_opening_detail_does_not_mark_others_cc`(发起人 `GET instance/{id}` 后抄送人 `cc/page` 仍 `isRead=false`)。变异:去掉 `MarkMyCcReadAsync` 的 `UserId==`(保留 InstanceId && !IsRead)→ **红** `Assert.False Expected: False / Actual: True`;复原 `.Where(c => c.InstanceId == instanceId && c.UserId == userId && !c.IsRead)` 后再跑 **绿**。指定过滤器 **185/185**(基线 184 + 1)。产品代码除临时变异外零改;`EnterCcAsync` 未动;`rg MUTATION`/`REVIEW-PROBE` 产品+测试源零命中。P2-1 闭合。剩余 P3:`DateTime.Now` 未走 TimeProvider;`OnlyUnread`/`DefinitionId` 零用例;48027 i18n 挂 Task 14;`POST /cc/read` 无菜单按钮。Task 11 **勾选**。
- 上一轮(历史): Round 34 — **Task 11 review**。`EnterCcAsync` 零 diff。`WfCcTests`+第九面亲手 **5/5**。`POST /cc/read` 去 `UserId` → `Expected: 48027 / Actual: 0`(钉得住)。**1×P2**:`GetAsync.MarkMyCcReadAsync` 去掉 `UserId==` 后,`Opening_instance_detail_marks_cc_read` **仍绿**(用例里只有抄送人自己打开详情,标「本实例全部未读」与「只标自己」不可区分)。修法:补一条「发起人先打开详情,抄送人行仍未读」。0×P1。Task 11 **未勾选**。
- 上一轮(历史): Round 33 — **Task 11 exec(抄送列表)**。`IWfCcService`/`WfCcService`/`WfCcController` + `GetAsync` 看详情标已读 + 菜单 `RootId+22` + Vue 列表页。指定过滤器 **184/184**(基线 179 + 5)。PageMine 去 userId、GetAsync 去掉标已读两条变异转红后复原。Task 11 **未勾选**,留给 review。
- 上一轮(历史): Round 33 — **Task 10 独立复核 + 收口**。产品代码相对 HEAD 零 diff。指定过滤器亲手 **179/179**。亲手复跑两条承重变异:①去掉 `PageMineAsync` 的 `StarterUserId` → `Expected: [bId] / Actual: [bId, aId]`;②AutoPass 后 `ClearDueTimeAsync` → `Assert.NotNull()` 第一拍 DueTime 被清。复原后 `rg REVIEW-PROBE` 零命中。0×P1 / 0×未修 P2。P3:会签「对每个 Pending 都转」的朴素 foreach 会因 alreadyActor 整事务回滚(红在 A 待办还在,不是 B 被转走)——exec 已如实记;顺序会签一拍只有一位 Pending,「一拍批完全部」靠第一拍 `Running`+1 Approve 钉内循环,不是 `Take(1)`。Task 10 勾选。
- 上一轮(历史): Round 32 — **Task 10 exec(缺口补测,产品代码零改动)**。`WorkflowReplaceabilityTests` 八个 `TryAddScoped` 面与 `WorkflowSetup` 一一对应,**已复核不缺面**。`WfDelegateTests` 两处 XML `TransferTaskOp`→`ReassignTaskOpBase`。新增 `WfListContractTests`(2):`Page_mine` / `Page_done` 只看见当前用户行,造用户一律 `orgId=1`。`WfTimeoutTests` 加 Sequential AutoPass 两拍级联 + 会签超时转办**现状快照**(不是定案)。指定过滤器 **179/179**(基线 175 + 4)。Task 10 **未勾选**,留给 review。
- 上一轮(历史): Round 31 — **Task 9 独立复核 + 收口**。台账 Status 仍停在「Round 29 / 当前阶段 plan / 下一步 Round 30 plan」,但工作树早已有 Round 30 的 CAS 实现 + `WfVersionCasTests`(10 条)。**代码是事实源**:P2-1(会签/顺序签非末位投票领取 token)已在 `CompleteTaskOp.ExecuteAsync` 的 `!passed` 分支落地,`Cosign_first_approve_claims_token_and_locks_out_cancel` 钉机制(token 2→3)。P2-2 测试已写(`Resubmit_losing_token_cas` / `Instance_losing_cas`,经 `IWorkflowFormBinder`/`IApproverResolver` 事务内推版本),但 **`ClaimTokenAsync` 被留下 `MUTATION-M2`**:`if (claimed != 1) throw` 整段删掉、`_ = claimed` 静默继续。本轮先带着变异跑 P2-2 → `Resubmit_losing_token_cas` **红**,`Expected: 48004 / Actual: 0`(区分力成立);复原 throw 后 3/3 绿。指定过滤器 **59/59 绿**。P3-1(`DefaultValue` 三步 ALTER + SQLite 例外)/P3-6(终态动作物理删 `wf_task` 不变量)/P3-7(多一条 UPDATE 是 ctx 没有 `ICurrentUser`,不是「更高效形状」)已在注释里。0×P1 / 0×未修 P2。Task 9 勾选。
- 上一轮(历史): Round 29 — 任务8/exec(修 Findings)+ Opus 独立复核。1×P1 + 4×P2 + 3×P3 闭合,**165/165 绿**(基线 160 + 5 条新用例)。**P1 修法**:批量从「取回行数」改成「处理预算」+ `(DueTime, Id)` 游标翻页——推不动的行只被检视、不扣预算,天花板 `MaxScanRounds`(默认 5)。exec **没取** review 建议的「把 `ShouldRemindAsync` 判据下推进 SQL」,理由站得住:间隔是**每节点各不相同**的(默认跟随节点 `Hours`),扫描时还没解析节点配置、SQL 里拿不到;能下推的只有固定下限那版,对 `hours=24` 的节点只挡住 1/24 时间,还会把顺序会签的逐拍级联降成每小时一位(`TimeoutFired` 键是 `(InstanceId, NodeId)`,SQL 里分不出提醒与级联)。死行出口 `RetireTaskAsync` 先落 `TimeoutFired(action="retired")` 再带 `Version` 条件清 `DueTime` 再打带 `taskId` 的日志,堵住了陷阱记录第 3 条担心的「静默清 DueTime」。**P2-1 取第三条路**:任务书建议的①不可行(`CompleteTaskCmd` 没有 `ExpectedVersion`,人工路径在自己事务里现读版本号、喂不进快照),改为断言落在**机制本身**——取 `Version` 快照、跑完提醒扫描后断言 `wf_task.Version` 一字未动,加 CAS 必红;那条假记账已整段替换为「⚠ 假记账更正」引用块,含原句、review 实跑证伪的事实与根因、以及新钉子的射程声明。**Opus 独立复核**:独立跑全量 **165/165**;**亲手复跑承重变异**——`MaxScanRounds => 1`(还原单页 Take)→ 钉子测试 `Timeout_throttled_reminds_do_not_starve_a_newly_due_task` 转红,`Expected: ["…命中 3,…,自动通过 1,…,跳过 2,…"] / Actual: ["…命中 2,…,自动通过 0,…,跳过 2,…"]`(两条被防刷挡下的提醒吃掉批量、新到期的自动通过没被处理,与 P1 描述逐字吻合)→ 复原后**强刷时间戳重编**再跑 165/165,`git diff --check` 干净、`rg` 扫 `MUTATION`/`REVIEW-PROBE` 零命中。**exec 本轮诚信合格**:两条钉不住的如实标注(P2-3 纯文档改动无红测;cron 段数那条跑不出红——`CronExpression.TryParse` 自己认 5 段,射程只是「能算出下一次时刻」),并纠正了任务书一处不可行建议。
- 上一轮: Round 26 — 任务7/plan + exec(纯重构)+ Opus 独立复核。抽出 `ReassignTaskOpBase`(abstract,157 行),`TransferTaskOp`(149→22 行)与 `DelegateTaskOp`(26 行)**都继承它、互为兄弟**,「委托 IS-A 转办」的假断言消失。**两个钩子取 `abstract` 而非带默认值**——exec 的裁定理由站得住:`=> WfTaskAction.Transfer` 写在基类上等于说「一次改派默认是转办」,那正是本轮要拆的断言;将来第三个兄弟忘声明自己是谁会**编译失败**而非静默记成转办;代价为零(全仓零处构造基类,Op 不走 DI),`ApproverProviderBase` 是现成同形先例。两个 `override` 都没加 `sealed`,继承 `TransferTaskOp` 的消费者子类照旧能覆写。**验收线达成:143/143,一条不动、一条不加,未修改任何测试文件。****Opus 独立复核**(纯重构的核心风险是「搬家变重写」,故按等价性而非变异验证):①`git status`/`git diff --stat` 确认改动集只有 3 个 Op 文件 + 台账(`TestResults/` 与协调者的 renumber 改动未被碰);②`rg` 确认继承子句是 `TransferTaskOp : ReassignTaskOpBase(...)` 与 `DelegateTaskOp : ReassignTaskOpBase(...)`、两者之间**无继承路径**,两组钩子值与重构前逐字一致(转办 `Transfer`/48010、委托 `Delegate`/48026);③**亲手做逐字核验**——`git show HEAD:...TransferTaskOp.cs` 取出重构前版本,把旧文件第 28-148 行与新基类 `ExecuteAsync` 起 121 行 trim 后 `Compare-Object -SyncWindow 0`,**121 行零差异**,「移动而非重写」独立确认;④独立跑全量 **143/143**。exec 一处判断我认同并已核实合理:**本轮不拆 `ExecuteAsync` 的 125 行长方法**——拆步骤会把一次可逐行核对的搬家变成不可证伪的重写,而步骤边界是新的覆写契约、一经发布不好改;Task 9(CAS 收口)正要动其中的 `WfTask.Version` 认领段,由它来定这条缝更准。理由与建议切法已记入 `## Findings`,非定案。
- 上一轮: Round 25 — 任务6/exec(修 Findings)+ Opus 独立复核。Task 6 的 2×P2(均为测试缺口,产品代码零行为改动)闭合,**143/143 绿**(基线 142 + 1 条新用例 + 6 条断言)。**关键增量:reviewer 本轮 shell 不可用,它两条「加变异后仍全绿」的论断是读码推演;exec 实跑复现,两条推演全部成立**——①给 `TransferTaskOp.cs:49` 的 `alreadyActor` 查询加 `&& a.Status != Skipped`(一个看起来很合理的「让 B 把误委托还给 A」修复)修前 142/142 全绿,而语义上 A→B→A→B 立刻成为无界循环;②把 `:34`/`:43` 改回字面量 `TransferTargetInvalid` 修前也全绿,用户「委托给自己」或「委托给已停用用户」会看到「转办目标无效」的错文案。补测试后两个变异各自转红(①`Expected: 48026 / Actual: 0`;②③分两次单独变异、红在**不同行** 220 vs 228,证明两个抛出点各自独立被钉)。顺手做掉 P3-#10(三处过期类级注释)、P3-#11(`IWfTaskService` 破坏性变更 `<remarks>`,第四次扩接口)。**Opus 独立复核**:改动集只 4 个文件 + 台账 Findings,与报告一致;独立跑全量 143/143;`git diff` 确认 `TransferTaskOp.cs` 已逐字回到 Round 24 原状、无变异残留。
- 上一轮(历史): Round 22 — 任务5/exec(修 Findings)+ Opus 独立复核。1×P1 + 5×P2 全部闭合,**137/137 绿**(基线 123 + 14 条新用例,零回归)。P1 取「同表下界」:查询 `Action` 白名单放宽到 `Approve|Reject|Return`,倒序后 `TakeWhile(Action == Approve)` 在遇到第一条 Reject/Return 时截断——跳转刚发生时窗口为空即无基线,正向推进时窗口是「上次跳转之后的所有 Approve」;零额外查询、不跨表比雪花 Id、对未来动词是白名单而非黑名单。`RejectRouted=13`/`TaskReturned=14` 按裁决落地(用于审计与 Task 12 回放,**不**作下界数据源)。新语义已写进 `## 语义契约`。**Opus 独立复核**:`git status`/`git diff --stat` 核对改动集(5 产品 + 3 测试 + 台账,`docs/workflow/` 四份文件 diffstat 与会话前逐行相同、确认未被碰);读 `EnterNodeOp.cs:325-358` 全文确认 `InstanceId` 过滤与连续区间语义保留、`Transfer`/`Delegate` 不在白名单故既不当边界也不污染基线;独立跑全量 137/137;**亲手复跑 P1 变异**(把 `TakeWhile` 换回 `Where`,即还原修复前行为)→ 两条钉子测试双双红,`Assert.Equal() Failure: Collections differ / Expected: [aId] / Actual: [bId]`(待办落到拒绝人/退回人而非 node1 审批人,与推演逐字吻合)→ 复原后独立重跑 137/137,`git diff --check` 干净、`rg` 确认无 `MUTATION`/`TODO`/`FIXME`/`.Skip(`/`NotImplementedException` 残留。
- 上一轮(历史): Round 20 — 对账。Task 5 的代码在 commit `f87e0d8`(M2b checkpoint)里已经完成,但台账 Status 停在 Round 19/exec、Task 5 未勾选——本轮以代码和测试为事实源补账收口。独立核验:`SnapshotLeaderChainsAsync` 签名已改成 `(long starterUserId, long? starterOrgId, ...)` 且两个调用点(`BeginStartAsync`:142、`BeginResubmitAsync`:631)都传值;`WfInstanceActionInput`→`WfInstanceCancelInput` 改名 + 新增 `WfInstanceResubmitInput` 已落地(顺带闭合 Task 4 的一条 P3 留痕);`ReturnNotAllowed=48024`/`ResubmitNotAllowed=48025`/`InstanceResubmitted=12` 就位;`WorkflowReplaceabilityTests` 的 `FakeTaskService.ReturnAsync`/`FakeInstanceService.ResubmitAsync` 两个桩已补。全量套件 **123/123 绿**,与 Plan 步骤 25 的预期(基线 114 + 9 条新用例)逐数吻合。工作树除 `docs/workflow/` 四份文档改动外干净。**同时按新增的设计规划 §十五 15.1 插入一项新任务(实例/Token 级 Version CAS,原 Task 8-12 顺延为 9-13)。**
- 上一轮(历史): Round 18 — plan。读了 `CompleteTaskOp.cs`(全文)、`Schema/WfNode.cs`/`WfSchemaEnums.cs`(`OnReject`/`RejectToNodeId`/`ReturnPolicy`/`ReturnToNodeId` 精确形状)、`WfHisTask.cs`/`WfHistory.cs`/`WfToken.cs`、`TransferTaskOp.cs`(CAS 模板)、`WorkflowEngine.cs`(全文)、`WfInstanceService.StartAsync`、设计草案 §六原表(锁定"退回后重提默认从头重走,不管退回目标是哪个节点"这条关键语义)。核心判断(厘清共用与否):**拒绝路由**与**主动退回**是两套不同机制——拒绝路由直接复用 `EnterNodeOp` 自动继续;主动退回关闭当前任务后**不**自动继续,需要发起人显式调用**新增第三套引擎命令"重提"**(`ResubmitInstanceCmd`,`BeginStartAsync` 的翻版,作用在已有实例行上)。按 A(拒绝路由)→B(主动退回)→C(重提)拆了 3 大块、25 个步骤(2+3+4=9 条新测试,5 个区分力变异点),写进 `## Plan`,附陷阱记录 5 条。**未写任何产品代码**。
- 下一步: M2b 收口。不提交、不推送,除非用户要求。不做 M3。
- 已完成(历史): Round 27 — Task 8 plan + exec(worker 静默中断在写完代码之后、更新 Status 之前;台账有一处冒签已更正);Round 28 — Task 8 review(1×P1 + 4×P2 + 6×P3,该 reviewer 有 shell、逐条实跑,并**证伪了 exec「16 个变异全部转红」的自报**);Round 29 — 修 Findings 收口。
- 原「下一步」(已完成): Round 27 — Task 8(超时 Job)plan。`EnterNodeOp.CreateTaskAsync` 目前硬编码 `DueTime = null`,要按 `Node.Props?.Timeout?.Hours` 算真实 `DueTime`;新建 `WfTimeoutJob : IAdminJob` 扫 `DueTime < now` 的活跃 `wf_task`,按 `Timeout.Action` 分流(`Remind`→`IWorkflowNotifier`;`AutoPass`/`AutoReject`→等价调用 `CompleteTaskOp`;`Transfer`→转办),写 `WfHistoryEventType.TimeoutFired`,`TryAddEnumerable` 注册。**四条前置约束,plan 阶段必须先读**:①§14.1 定案——`WfTimeoutJob` 用 `taskId + Version + DueTime <= now` 条件更新,CAS 失败表示人工动作已胜出,**不建另一套 worker/lease**;②必须补一条测试证明「委托过的任务照原 `DueTime` 到期」(见 `## Findings` 该条;该性质今天因 `DueTime = null` 是空真、零可观测出口,本任务一落 `DueTime` 它立刻变成可违反的真命题);③`Timeout.Action = Transfer` 现在有 Round 26 抽出的 `ReassignTaskOpBase` 可用——**plan 阶段要裁定**是直接 `new TransferTaskOp`(超时转办等同人工转办)还是做**第三个兄弟**(声明自己的 `HistoryAction`,让 `wf_his_task` 能区分「人转的」与「超时自动转的」);`abstract` 钩子的直接收益就在后一条路上,但这是产品判断,exec 有意没预判;④多副本安全由内核调度器选主保证(ADR-0004),工作流自己不管分布式。
- 之后: Task 9(实例/Token 级 Version CAS)。它会动 `ReassignTaskOpBase.ExecuteAsync` 里的 `WfTask.Version` 认领段,`## Findings` 有两条专门给它的约束(必须保持任务级 CAS 为第一个写操作;`BeginResubmitAsync` 的 CAS 缺口排在收口清单第一位),另有一条建议由它来定 `ExecuteAsync` 的拆步边界。
- ⚠ **任务号 2026-08-25 有过一次 renumber**:抽 `ReassignTaskOpBase` 升格为新 Task 7,原 Task 7-13 顺延为 8-14。`## Log` 里 Round 25 及更早的条目使用**旧编号**(那时「Task 7」指超时 Job、「Task 8」指 CAS 收口、「Task 13」指 btnInfo 验收);`## Tasks` 与 `## Findings` 已按新编号更正。读历史 Log 时注意这个偏移。
- 已完成(历史): Round 23 — Task 6 plan + exec;Round 24 — Task 6 review(0×P1 + 2×P2 + 12×P3);Round 25 — 修 Findings 收口。
- 更早(历史): Round 21 — Task 5 review(查出 1×P1 + 5×P2,已在 Round 22 闭合)。读 `TransferTaskOp.cs`(现成模板,`DelegateTaskOp` 照它写、不另起炉灶)、`WfTaskAction.Delegate=6`(枚举已预留、零消费方)、`WfTaskController` 的 `Transfer` 端点写法。plan 阶段要厘清:「委托」与「转办」除 `WfHisTask.Action` 标签不同外是否还有语义差异(`## 语义契约` 定案:一次性、发起人/办理人把当前一个待办指给别人、底层可复用 `TransferTaskOp` 的 CAS+建新 actor 模式);以及委托后**谁能再委托/转办**是否要限制。`DelegateTaskOp.cs` 当前零文件,从零建。注意 Round 22 新增的 `EnterNodeOp` 白名单——`Delegate` **不是**向后跳转,不要加进跳转下界白名单(加了会让委托误重置去重基线)。
- 已完成(历史): Round 21 — Task 5 review(查出 1×P1 + 5×P2,全部已在 Round 22 闭合)。Task 5 的代码在 `f87e0d8` 里,工作树已干净,`code-reviewer` 要用 `git show f87e0d8 -- <path>` 取 diff,范围**只限 Task 5 那部分**(Task 1-4 的 review 已闭合,别重复评):`Engine/Operations/ReturnTaskOp.cs`(新,152 行)、`Engine/Operations/CompleteTaskOp.cs` 的 `RejectInstanceAsync` 分流、`Engine/WorkflowEngine.cs` 的 `BeginReturnAsync`/`BeginResubmitAsync`/`SnapshotLeaderChainsAsync` 签名重构、`Engine/WfCommands.cs` 的 `ReturnTaskCmd`/`ResubmitInstanceCmd`、`WorkflowErrorCode.cs`(48024/48025)、`WfEnums.cs`(`InstanceResubmitted=12`)、`IWfTaskService`/`WfTaskService.ReturnAsync`、`IWfInstanceService`/`WfInstanceService.ResubmitAsync`、`WfRuntimeModels.cs`(`TargetNodeId` + DTO 改名 + `WfInstanceResubmitInput`)、两个 Controller 各 1 端点、`WorkflowReplaceabilityTests.cs` 两个桩、新增 `WfRejectRoutingTests.cs`/`WfReturnResubmitTests.cs`。review 通过(无 P1/P2 或修完)才勾选 Task 5、推进到 Task 6(委托 Delegate)。
- 之后: Task 6(委托 Delegate)plan——读 `TransferTaskOp.cs`(现成模板)、`WfTaskAction.Delegate=6`(枚举已预留、零消费方),厘清「委托」与「转办」除历史动作标签外是否还有语义差异。`DelegateTaskOp.cs` 当前零文件,从零建。

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
| 委托(Delegate,一次性) | **仅当前 Pending 办理人**把**自己的当前一个待办**指给别人处理,不是长期规则。**⚠ 措辞已于 2026-08-24 经用户裁决收窄**:本行原文写的是「发起人/办理人」,那是表述松散——实例发起人**无权**委托他人待办,否则等于给自己的单子指派审批人,`IApproverProvider`/multiLeader 主管链/`selfSelect` 白名单全部作废。发起人现有三个动词(撤销/重提/催办)都不改变「谁来批」,委托改这个,性质不同;真实的「审批人休假请人代办」诉求知情方是办理人自己,「发起人指定代办」实质是改审批人、属 M3 长期委托规则那一档。与 `Transfer`(转办)除 `WfHisTask.Action` 标签外只有产品语义差异(问责:转办=责任转移,委托=请人代办),底层复用 `TransferTaskOp` 的 CAS+建新 actor 模式。已回写 `CONTEXT.md` 行为语义默认值与设计规划 §十。 |
| 链式委托 | 允许 A→B→C,**不设次数/深度上限**(YAGNI)。安全依据:`ReassignTaskOpBase` 的 `alreadyActor` 校验只看 actor 行**存在性**、不看状态,故委托回本待办任何参与过的人都会被拒 → 环路天然封死。注意上界不是「本待办参与过的人数」(那是循环论证),真实上界是全库启用用户数;每一跳都需当前持有人主动动作,单个 actor 无法自己刷。此性质由 `WfDelegateTests.Delegate_chain_hands_todo_along_without_limit` 的第三跳钉住——**改动 `alreadyActor` 查询前先看那条测试**。 |
| 催办(Urge) | 发起人对当前 Pending 办理人发一次提醒;不改变任何任务/实例状态,只落一条历史事件 + 走 `IWorkflowNotifier` 推送;可重复催办,不做频率限制(YAGNI,超出本轮范围) |
| 超时(Timeout) | `node.Props.Timeout.Hours` 非空且 >0 时,建任务按 `TimeProvider.GetLocalNow() + Hours` 填 `wf_task.DueTime`;`WfTimeoutJob : IAdminJob` 扫 `DueTime <= now` 的活跃任务,按 `Timeout.Action`:`Remind`(只推送不改状态,可重复触发)、`AutoPass`(等价于该 actor 自动同意)、`AutoReject`(等价于该 actor 自动拒绝)、`Transfer`(转给 `TransferUserId`) |
| 超时提醒频率(Task 8 定案,2026-08-25) | `Remind` 可重复触发,**最小提醒间隔默认 = 该节点自己的 `timeout.hours`(下限 1 小时)**。判据取本(实例, 节点)上最近一条 `TimeoutFired` 事件的时间(且不早于本待办 `CreateTime`,防止向后跳转重入后丢掉第一次提醒),不新增列。可由 `TenonAdmin:Workflow:TimeoutRemindMinIntervalHours` 全局覆盖(0 = 跟随节点),或覆写 `WfTimeoutJob.ShouldRemindAsync`——**但覆写子类后还须把 `sys_job` 该行的 `HandlerName` 改成子类全名**,否则 `TryAddEnumerable` 按实现类型去重 + 解析器按 `Name` 匹配会让调度器永远选中基类、覆写一次都不执行。已回写 `CONTEXT.md` 与设计规划 §十。 |
| 超时动作的动作主体(Task 8 定案,2026-08-25) | 超时触发的自动动作一律**以当前 Pending 办理人身份记原生动词**(`Approve`/`Reject`/`Transfer`),**不新增「超时专用」的 `WfTaskAction` 值**;真相由同事务的 `TimeoutFired` 事件 + `Comment` 说明。这是机制约束不是取舍:`CompleteTaskOp` 的 actor 认领是 `WHERE UserId=@caller AND Status=Pending`,系统账号必然认领不到,换身份要松掉「仅本人可办」这条承重校验;另有三处只认原生动词的守卫(撤销准入只认 `Approve`、去重白名单只认 `Approve\|Reject\|Return`、`CompleteTaskOp`/`WfTaskService` 各一道 `is not (Approve or Reject)`)。副产品:超时自动拒绝走 `onReject=toNode` 时**自动**落进跳转白名单、基线重置是复用同一段代码的结果,不需额外代码维持。将来若要区分人/机器动作,补法是加可空列(如 `IsAuto`,旧行回填),比持久化枚举值可逆。已回写 `CONTEXT.md` 与设计规划 §十。 |
| 同一人相邻节点去重 | `EnterApprovalAsync` 解析出的办理人集合,若与**紧邻的上一个已完成审批节点**的办理人集合有交集且该交集用户在上一节点已 `Approve`,对交集用户自动记一次「跳过」(不建 Pending actor,只在办理人 ≥2 人时对其余人正常建任务;若解析结果整体只剩该用户一人,等价于该节点整体自动通过)。**基线只认最近一次向后跳转之后的批准记录**——见下一行。 |
| 向后跳转重置去重基线(Round 22 用户裁决,2026-08-24) | 任何向后跳转(拒绝路由 / 主动退回 / 退回重提)都**重置**上一行的去重基线——跳转之前的 `Approve` 行不再参与比对,已批节点在回退后必须重新审。「紧邻的上一个已完成审批节点」只在 token 单向前进时有定义;向后跳转后基线从跳转点重新起算。这与「退回后重提默认从头重走」是同一条语义的两面:重走就是真重走,不因为「上次这个人批过」而静默跳过。**对正向推进的行为零变化。**实现取同表下界(`wf_his_task` 内按 `Id` 倒序,遇最近一条 `Reject`/`Return` 即截断),不跨表比雪花 Id;`RejectRouted=13`/`TaskReturned=14` 用于审计与 Task 12 回放,**不**作下界数据源。已回写 `CONTEXT.md` 与设计规划 §十。 |
| 抄送 | 独立列表(不是待办),`WfCc.IsRead` 由查看详情页时标记已读(仿 `SysNotice` 已读语义,不新建通道) |

## Plan(当前任务的拆解;每进入新任务时由 plan 阶段重写)

> **Task 14 — `btnInfo` + 配置抽屉暴露新字段 + 验收**(Round 40 plan+exec)。节点 `ButtonLabels`(JNPF 增量#2);抽屉把退回/拒绝/超时/按钮文案放进「高级」;详情补齐退回/撤销/催办/委托/重提;双模板 `gen:api`;Playwright 走通 DONE-CONDITION 浏览器面并落 `.loop/wf-ui-shots/m2b-0*.png`。**不勾选 Task 14**(留给 Round 41 独立复核)。不做 M3(formPerms / parallel / webhook / 长期委托 / React 页移植)。`web-react/` 只刷 `schema.d.ts`(该模板 `error.code` 没有 48001–48020 块,不补 48021+)。

### 读码事实(plan 已核对,exec 勿再猜)

1. `WfNodeProps`(C#)已有 `OnReject` / `ReturnPolicy` / `ReturnToNodeId` / `Timeout`。**没有** `ButtonLabels`。Vue `schema.ts` 有 `returnPolicy`,**没有** `timeout` / `buttonLabels`。`WfConfigDrawer.vue` 注释仍写「M2a:不配超时/字段权限」;`applyNodeConfiguration` 每次保存审批节点都硬写成 `onReject: 'terminate'`,会冲掉抽屉新字段。
2. 详情页只露同意/拒绝/转办。`web/src/api/workflow.ts` 无 `return` / `delegate` / `urge` / `cancel` / `resubmit`。后端端点已在(`WfTaskController` / `WfInstanceController`)。`ReturnTaskOp` 未配 `ReturnPolicy` 抛 `48024 policyNotConfigured`(default 分支),所以抽屉必须写出 `prev`(或用户选的策略),否则浏览器退回必红。
3. 催办:发起人调 `POST task/urge`,入参是 **taskId**。`GetAsync` 只给 `MyPendingTask`(当前用户自己的待办)。发起人若不是办理人,详情拿不到可催的 taskId。`UrgeAsync` 在「唯一办理人=发起人」时静默 return、不写历史。要拍到真催办,必须另造一名办理人,并让详情能拿到当前活跃 taskId。
4. `GetAsync` 已透出 `Model` / `VisitedNodeIds` / `CurrentNodeIds`。详情读 `currentNode.props.buttonLabels`。抄送/监控 API 仍挂 `@ts-expect-error`;`WfCcItem` 仍是手写接口。`web` / `web-react` 的 `schema.d.ts` 都停在 Task 13 之前(详情无 Model,无 `WfCcItemOutput`)。
5. `error.code`:`web` 两语言停在 48020;缺 48021 / 48023–48027(48022 空洞不填)。`web-react` 语言包**没有** `error.code.480xx` 块 → 本轮不补。
6. e2e:`web/e2e/helpers.ts` 登录 `superAdmin` / `Aa123456`(`TENON_E2E_*` 可覆盖);`playwright.config.ts` 自起 MinimalHost+Vite,`TenonAdmin__Seed__AdminPassword` 同步该密码,`reuseExistingServer: false`。已有 `workflow-m2a.spec.ts` + `apiCreateUser`。截图目录 `.loop/wf-ui-shots/` 已有 m2a-01..03。
7. 禁区:不改 `EnterCcAsync` 写路径;不翻 All-sign 超时 Transfer;不建 `TestResults/`;不移植 Vue 页到 `web-react/`。

### 决策点

| # | 决策点 | 裁定 | 理由 |
|---|---|---|---|
| D1 | ButtonLabels 形状 | C# 新类 `WfButtonLabels`:`Approve` / `Reject` / `Return` / `Transfer` / `Delegate` / `Urge`(全 `string?`)。挂 `WfNodeProps.ButtonLabels`。JSON 走现有 `WfModelJson`(camelCase + 写忽略 null)→ `buttonLabels:{approve,reject,return,transfer,delegate,urge}`。TS `WfButtonLabels` 同形。空/缺省 = 详情 i18n 默认文案 | 对齐 JNPF btnInfo 增量#2;不新增枚举、不改引擎动作名 |
| D2 | 抽屉默认 vs 高级 | 审批默认可见保持 ≤5:名称 / 办理人 / Provider 参数 / 签核方式。`returnPolicy`+`returnToNodeId` / `onReject`+`rejectToNodeId` / `timeout` / `buttonLabels` **全部进「高级」**。审批节点**总是**显示高级折叠(不再只在 position 时出现)。抄送不配退回/超时/按钮文案 | 任务书守 ≤5;这些是低频配置。委托没有节点级开关(一次性动词),抽屉只露按钮文案 |
| D3 | `applyNodeConfiguration` | 审批 config 增 `returnPolicy` / `returnToNodeId` / `onReject` / `rejectToNodeId` / `timeout` / `buttonLabels`。保存时**保留**这些字段,不再每次写死 `onReject:'terminate'`。未设退回策略时默认写出 `prev`(否则发布后点退回必 48024)。`hours<=0` 的 timeout 写成 `undefined`(不启用) | 现实现会冲掉高级字段;引擎 default 不是 Prev |
| D4 | 详情动词 | 办理人:`approve` / `reject` / `return` / `transfer` / `delegate`(委托与转办同形,只换端点+48026)。发起人且 Running:`urge`(用当前活跃 taskId,不是必须 MyPending)。发起人且 Running 且 hisTasks 无 `Approve`:`cancel`。发起人且 Running 且无 MyPending(退回后停住):`resubmit`(带原 `variablesJson`)。按钮文案=`buttonLabels.*` 非空优先,否则 i18n | 验收硬要求退回/撤销/催办;重提是退回后唯一继续路径;委托便宜 |
| D5 | 催办用的 taskId | `WfInstanceDetailOutput` 加 `CurrentTaskId`(活跃 `wf_task` 第一条的 Id,可空)。催办优先 `myPendingTask.taskId`,否则 `currentTaskId` | 发起人催别人时自己没有 pending;不改 Urge 写路径 |
| D6 | Playwright / 登录 | 新 spec `web/e2e/workflow-m2b.spec.ts`。`login`+`enterApp(SYSTEM_APP)`。Playwright 自起栈(与 m2a 同,密码 `Aa123456`)。`apiCreateUser`(forceTotp=false)造一名办理人,避免「发起人=唯一办理人」催办静默。截图 `m2b-01`…`m2b-06` 到 `.loop/wf-ui-shots/` | 任务书优先可靠 Playwright;空列表截图不够 |
| D7 | e2e 实例路径 | 一条定义:`start → cc(超管) → approval(指定用户=超管,returnPolicy=prev)` 走退回/撤销/抄送/我发起的/我已办的。另起一单:`approval(指定用户=第二用户)` 由超管催办。退回后截退回态即可,不强制再点重提(按钮仍要有) | 超管既是发起人又是办理人才能一个人点退回;催办必须是别人的 pending |
| D8 | gen:api | 先保证 `:5100` MinimalHost 活着(`dotnet run --project backend/samples/MinimalHost --no-restore`)。`cd web && npm run gen:api`;`cd web-react && npm run gen:api`。两边 `src/api/schema.d.ts` SHA256 必须一致。然后去掉 cc/monitor `@ts-expect-error`;`WfCcItem = Schemas['WfCcItemOutput']`;详情交叉字段若已生成则删 | 契约漂移闸门;web-react 只动 schema |
| D9 | C# 往返测试 | **加 1 条**无 HTTP 的 `WfButtonLabels_round_trips_through_WfModelJson`(Serialize 含 camelCase 键,Deserialize 回读)。不新建 HTTP 套件 | 便宜;schema 序列化已有覆盖,这一条钉新字段不被 ignore |
| D10 | i18n / 禁区 | `web` 两语言补 `error.code.48021/48023/48024/48025/48026/48027` + 抽屉/详情新键。`web-react` 语言包不补。不改 `EnterCcAsync`、不翻 All-sign Transfer、不建 `TestResults/`、不勾选 Task 14、不做 M3 | 挂账 P3 + 任务书硬约束 |

### 改动清单

| 文件 | 动作 |
|---|---|
| `Schema/WfNode.cs` | `WfButtonLabels` + `WfNodeProps.ButtonLabels` |
| `WfRuntimeModels` / `WfInstanceService.GetAsync` | `CurrentTaskId` |
| `WfButtonLabelsTests.cs` | **新增** 1 条往返 |
| `web/src/workflow/schema.ts` | `timeout` / `WfTimeout` / `buttonLabels` |
| `configuration.ts` + `*.spec.ts` | 审批高级字段持久化;默认 `returnPolicy=prev` |
| `WfConfigDrawer.vue` | 审批总是有高级;暴露退回/拒绝/超时/按钮文案 |
| `instance/detail.vue` | 退回/委托/催办/撤销/重提 + `buttonLabels` |
| `api/workflow.ts` / `types/workflow.ts` | 五个动词;gen:api 后去 `@ts-expect-error`;`WfCcItem` 改生成类型 |
| `web/src/locales/{zh-CN,en-US}.ts` | 48021+ 与抽屉/详情文案 |
| `web/e2e/workflow-m2b.spec.ts` | **新增** 浏览器验收 |
| `web/src/api/schema.d.ts` + `web-react/src/api/schema.d.ts` | 双 `gen:api` |
| `.loop/wf-m2b.md` | Plan / Status / Log。**不勾选 Task 14** |

### 步骤

1. 写本 Plan(本段)。
2. C# `WfButtonLabels` + `CurrentTaskId` + 往返测试。
3. Vue schema / configuration / 抽屉 / 详情动词 / i18n / api 手写封装。
4. 起 MinimalHost → 双 `gen:api` → 比 SHA256 → 去 `@ts-expect-error`。
5. Playwright m2b spec + 截图。
6. 指定过滤器;`cd web && npm run typecheck && npm run lint`;`npx vitest run src/workflow/`。
7. 回写 Status=40/exec、Log Round 40、NEXT=Round 41 review。Task 14 保持未勾。

### 验收(本轮 exec 自证,review 再独立核)

- 指定过滤器绿,条数 ≥ 189(只增不减;本轮 +1 往返则 190)
- typecheck / lint / `src/workflow/` vitest 绿
- 双 schema SHA256 一致
- `.loop/wf-ui-shots/m2b-01`… 存在且覆盖退回/撤销/催办/抄送已读/我发起的/我已办的
- Task 14 **未勾选**

<!-- TASK14-PLAN-ANCHOR -->

<!-- TASK13-PLAN-ANCHOR -->

> **以下 Task 12 方案(Round 36–37)保留作历史,不是本轮执行清单。**
>
> **Task 12 — 我发起的 / 我已办的**(历史)。纯前端两张列表 + 菜单 + i18n。后端 `PageMineAsync` / `PageDoneAsync` 已在。D1 不动业务;D2 菜单 +24/+25 Sort 5/6;D3 按钮 +11/+12;D7 无搜索栏。

<!-- TASK12-PLAN-ANCHOR -->

> **以下 Task 11 方案(Round 33–35)保留作历史,不是本轮执行清单。**
>
> **Task 11 — 抄送列表**(历史方案)。`wf_cc` 表、`EnterCcAsync` 写入(含重提幂等)、`EnsureParticipantAsync` 已认抄送人,缺的是查询/已读/列表页。表单挂载与详情准入都已在,本任务不重做引擎。

### 决策点(Task 11 历史)

| # | 决策点 | 裁定 |
|---|---|---|
| D1 | 接口放哪 | `Services/IWfCcService.cs`,与其他 `IWf*Service` 同层;任务书 `Abstractions/` 是过时路径 |
| D2 | 何时标已读 | **两处**:`GetAsync` 在准入后把该用户本实例未读行翻已读(对齐语义契约「看详情即已读」);另提供 `POST /cc/read` 显式接口(列表不进详情也能标,幂等) |
| D3 | 权限 | Controller 挂 `[ActiveSession]`,与 task/instance 同形。菜单 `RootId+22` Sort=4,按钮只挂 `GET .../cc/page` |
| D4 | 可替换性 | 第九个 `TryAddScoped<IWfCcService, WfCcService>` + `WorkflowReplaceabilityTests` 一条 |
| D5 | 错误码 | `CcNotFound=48027`:标已读时行不存在或不属于当前用户。语言包挂 Task 14 |
| D6 | 前端契约 | `web/` 手写 `WfCcItem` + `as never` 调新路径,等 Task 14 `gen:api`。不改 `web-react/` |
| D7 | 写入 | **不改** `EnterCcAsync` |
| D8 | 数据范围 | `wf_cc` 不是 `IOrgScoped`;联实例时 `ClearFilter<IOrgScoped>()`,过滤只认 `UserId` |

### 改动清单

| 文件 | 动作 |
|---|---|
| `IWfCcService` / `WfCcService` / `WfCcController` | 新增 PageMine / MarkRead |
| `WfRuntimeModels` / `WorkflowErrorCode` / `WorkflowSetup` | DTO + 48027 + TryAdd |
| `WfInstanceService.GetAsync` | 准入后标已读 |
| `WorkflowMenuSeed` | `RootId+22` 抄送列表 |
| `WorkflowReplaceabilityTests` | 第九面 |
| `WfCcTests.cs` | 新增 |
| `web/` 列表页 + i18n + api | 仅 Vue 模板 |

### 测试清单

| # | 用例 | 变异 |
|---|---|---|
| 1 | `Page_mine_returns_only_current_users_cc` | 去掉 `UserId==` → 对方看见行 |
| 2 | `Mark_read_is_idempotent_for_owner` | 去掉 `UserId==` 守卫 → 他人也能标 |
| 3 | `Mark_read_of_others_row_returns_48027` | 改成 0 → 红 |
| 4 | `Opening_instance_detail_marks_cc_read` | 去掉 GetAsync 里的标已读 → 仍未读 |
| 5 | `PreRegisteredCcService_ShouldWinOverBuiltIn` | TryAdd 改 Add → 红 |
| 6 | `Starter_opening_detail_does_not_mark_others_cc` | 去掉 `MarkMyCcReadAsync` 的 `UserId==` → 抄送人行被误标 |

预期:**184 + 1 → 185**(Round 33 基线 179 + 5;Round 35 再 +1)。

<!-- TASK11-PLAN-ANCHOR -->

> **以下 Task 10 方案(Round 32)保留作历史,不是本轮执行清单。**

> **Task 10 — 后端测试固化(Task 2–9 公开 HTTP 契约缺口)**。读了 `WfTaskController`(8 端点:todo/done/approve/reject/transfer/delegate/urge/return)、`WfInstanceController`(startable/startable/{id}/start/page/history/{id}/{id}/cancel/resubmit)、既有测试文件盘点(见下表)、`WorkflowReplaceabilityTests`(八个 `TryAddScoped` 面已齐,含 Task 1 的 `IWorkflowNotifier`)、以及 `## Findings` 挂给本任务的三条:会签超时转办语义空白、顺序会签超时逐拍级联零用例、`WfDelegateTests` 两处 `<c>TransferTaskOp</c>` 陈旧引用。
>
> **核心判断:本任务是缺口补测,不是把 Task 2–9 再写一遍。** 催办/去重/撤销/拒绝路由/退回重提/委托/超时/CAS 都已经走 HTTP 信封(独立 factory/账号/定义)。再抄一套 mega-file 只会制造双份断言、下次改语义要改两处。本轮只补**现在钉不住或根本没测**的公开契约,并把 Task 8 挂过来的两条语义空白变成可观测出口。

### 已有覆盖盘点(exec 不得重复建设)

| Task | 已有文件 | HTTP 覆盖结论 | 本轮要不要动 |
|---|---|---|---|
| 2 催办 | `WfUrgeTests`(6) | 发起人成功 / 非发起人 48021 / 自排除 / 未知待办 48006 / 可重复 / 唯一办理人=发起人静默 | **不重写**。无缺口 |
| 3 去重 | `WfAdjacentDedupTests`(5) | 部分跳过 / 整节点自动通过 / 非相邻不误伤 / 顺序保序 / multiLeader 豁免 | **不重写** |
| 4 撤销 | `WfCancelTests`(4)+ notifier 一条 | 发起人成功 / 非发起人 / 已批 / 非 Running | **不重写**。CAS 失败路径已在 Task 9 |
| 5 拒绝/退回/重提 | `WfRejectRoutingTests`(3)+ `WfReturnResubmitTests`(14) | 三策略 + 错误码 + 重提从头 + cc 幂等 + 向后跳转重置基线 | **不重写** |
| 6 委托 | `WfDelegateTests`(6) | 成功 / 发起人无权 / alreadyActor / 链式 + 环路 / 自己与停用 / 不去重基线 | **只改两处 XML 引用** `TransferTaskOp`→`ReassignTaskOpBase`(P3 留痕) |
| 7 抽基类 | 无独立测试(验收线就是 143 一条不动) | 无需 HTTP | **不新建** |
| 8 超时 | `WfTimeoutTests`(20) | Hours/DueTime/委托不重置/Remind/AutoPass All+Any/AutoReject/转办/CAS 输给人工/预算翻页/死行/种子 | **补 2 条**:会签超时转办表征 + Sequential 逐拍级联 |
| 9 CAS | `WfVersionCasTests`(10) | 机制 7 + 会签首票 + 两条 SPI 注入失败路径 | **不重写**。失败路径已可测 |
| 列表 | `todo` 被大量 helper 使用;`done` 只在 `LeaveWorkflowE2ETests` 露面;`instance/page` **零命中** | Task 12 是前端页,但后端契约「我发起的 / 我已办的」今天没有独立钉子 | **补 1 个文件、2 条**:`PageMine` + `PageDone` 的 HTTP 信封(独立账号,断言只看见自己的行) |
| 可替换性 | `WorkflowReplaceabilityTests` 八件套 | Task 1 已补 `IWorkflowNotifier` | **只读复核**,发现缺面再补,不预写 |

### 决策点

| # | 决策点 | 裁定 | 理由 |
|---|---|---|---|
| D1 | 要不要新建 mega HTTP 套件重测所有动词 | **不要。**缺口进既有文件;列表契约单独一个小文件 `WfListContractTests.cs` | 现有用例已经是「独立 factory/账号/定义 + HTTP 信封」。再抄一遍是双份维护,不是固化 |
| D2 | 会签 + 超时 `Transfer` 语义(Findings 挂件) | **本轮不定产品案,只写表征现状的用例**。现状:`All` 下 `PlanTimeoutOpsAsync` 只对 `actors[0]` 排一个 `TransferTaskOp` 并清整行 `DueTime`。用例名字与 XML 必须写明「这是现状快照,不是定案」 | 三个候选(改派全部 Pending / 等最后一位再清 / 发布期禁 Transfer)都是产品判断。本任务是测试固化,擅自改引擎等于提前做 M3。表征用例的变异点:「改成对每个 Pending 都转」或「不清 DueTime」必须能红 |
| D3 | Sequential 超时 AutoPass 逐拍级联 | **补一条钉子,钉「有意行为」**。两位顺序办理人 + 拨钟 + 连扫两拍 → 两行 Approve + 节点通过 | Findings 写明可构造、零覆盖。不是缺陷,但没钉子下次会被当 bug「修」掉 |
| D4 | `WorkflowReplaceabilityTests` | **复核八面,不预添第九面**。本任务不扩 `IWf*Service` | 任务书原文「若 Task 1 已补第八件套,本任务复核不重复」 |
| D5 | 给命令加 `ExpectedVersion` 方便构造竞态 | **不加**(沿用 Task 9 D11) | 那是 M2c receipt。失败路径已由 SPI 注入覆盖 |
| D6 | `seq` 会签首票要不要再写一条 CAS 用例 | **不写。**`!passed` 是同一条分支,`all` 那条已钉 token 2→3 | 再写一条是同一代码路径的克隆,区分力为零 |
| D7 | 错误码语言包(48021/23/24/25/26) | **不碰 `web/`**。继续挂 Task 14 | 硬约束:本任务纯后端测试 |
| D8 | 断言落点 | 失败型 bug 的否定断言必须落在**事务外**(日志 / 已提交行 / HTTP `code`),不能只数「事务内本该新增的行」 | Task 8 Findings 方法论条:失败会把证据一起回滚。本轮两条超时用例尤其容易踩 |

### 改动清单

| 文件 | 动作 |
|---|---|
| `backend/tests/TenonAdmin.Tests/WfTimeoutTests.cs` | 加 2 条:会签超时转办现状快照;Sequential AutoPass 两拍级联 |
| `backend/tests/TenonAdmin.Tests/WfListContractTests.cs` | **新增**,2 条:`GET task/done` 与 `GET instance/page` 只返回当前用户的行 |
| `backend/tests/TenonAdmin.Tests/WfDelegateTests.cs` | 两处 XML `<c>TransferTaskOp</c>` → `<c>ReassignTaskOpBase</c>`(零行为) |
| `WorkflowReplaceabilityTests.cs` | 只读复核;缺面才补 |
| 产品代码 | **默认零改动**。D2 不定案,禁止「顺手修」会签超时转办 |

### 步骤

1. 复核 `WorkflowReplaceabilityTests` 八面与 `WorkflowSetup` 的 `TryAdd*` 是否一一对应。缺面当场补,不缺就在 Findings 记「已复核」。
2. `WfDelegateTests` 两处注释改名。
3. `WfListContractTests`:独立 factory;用户 A 发起并办完一单、用户 B 发起一单仍在途 → A 的 `task/done` 只有 A 的已办、B 的 `instance/page` 只有 B 的实例。变异:把 `PageDoneAsync`/`PageMineAsync` 的 `userId` 过滤去掉 → 红。
4. Sequential 级联用例(先写、应绿)。变异:扫完第一拍就停 → `Expected: 2 Approve / Actual: 1`。
5. 会签超时转办表征用例(先写、钉现状)。变异见 D2。
6. 指定过滤器 + `FullyQualifiedName~Tests.Wf\|FullyQualifiedName~Workflow` 全量。`git diff --check`。`rg` 扫 `MUTATION`。
7. **禁止**改引擎来让 D2 用例「更好看」。

### 测试清单

| # | 用例 | 断言 | 变异点 |
|---|---|---|---|
| 1 | `Timeout_sequential_auto_pass_cascades_one_actor_per_scan` | 两位 Sequential + hours=1;拨过期;第一拍 1 条 Approve、待办仍在、第二位变 Pending;第二拍再 1 条 Approve、实例 Approved | 只跑一拍就断言完结 → 红;`DueTime` 第一拍被清 → 第二拍不再推进 → 红 |
| 2 | `Timeout_transfer_on_all_sign_mode_only_reassigns_first_pending_and_clears_due_time`(**现状快照,非定案**) | `All`+[A,B]+Transfer 给 C:一拍后 A 被转走、B 仍 Pending、`DueTime` 已清、C 是新 actor | ①对每个 Pending 都转 → B 也没了 → 红;②不清 `DueTime` → 第二拍日志「失败」或再次转办 → 红(断言必须看 Job 日志或 HTTP/DB 已提交态,见 D8) |
| 3 | `Page_mine_returns_only_current_users_instances` | B 调 `GET /api/v1/workflow/instance/page` 只看到自己的 `instanceId` | 去掉 `StarterUserId ==` → B 看见 A 的单 → 红 |
| 4 | `Page_done_returns_only_current_users_his_tasks` | A 办完后 `GET /api/v1/workflow/task/done` 有且仅有 A 的行;B 调同一接口为空 | 去掉办理人过滤 → B 看见 A 的已办 → 红 |

预期基线:**165 + Task 9 的 10 条新用例 = 175,再 +4 → 179**(Task 8 增强断言不计数;列表 2 + 超时 2)。exec 以实测为准,不要把期望值改成实测值。

### 陷阱记录(Task 10)

1. D2 用例的 XML 必须写「现状快照」。写成像定案,Task 14 验收会按错语义走浏览器。
2. 超时用例拨钟必须走既有 `MakeDue`/`RunTimeoutJob`,不要新开调度器(`WorkflowAppFactory` 已关 `SchedulerEnabled`)。
3. `instance/page` 走数据范围过滤器:`IOrgScoped`。造用户时 `orgId` 与发起人一致,否则 page 空是范围过滤不是 userId 过滤,变异钉不住。
4. 复原变异后必须确认重编过(Round 28/29 陈旧 dll;`dotnet build -t:Rebuild` 或刷 mtime)。

<!-- TASK10-PLAN-ANCHOR -->

> **以下 Task 9 方案(Round 30)保留作历史,不是本轮执行清单。**

> **Task 9 — 实例/Token 级 Version CAS(§十五 15.1 提前项)**。读了 `Entities/WfInstance.cs`(全文 44 行,`DataEntity`,4 个索引,**无 Version 列**)、`Entities/WfToken.cs`(全文 24 行,`BaseEntity`,`(InstanceId)`/`(InstanceId,Status)` 两个索引,**无 Version 列**)、`Entities/WfTask.cs`(`Version` 的现成声明形状 = 裸 `int` + `ColumnDescription`,**无 `DefaultValue`**)、`SqlSugar/Entities/BaseEntity.cs`(`AuditEntity.UpdateTime`/`UpdateUserId` 都是可空)、`SqlSugarSetup.cs:143-178`(审计 AOP **只在 `DataFilterType.UpdateByObject` 分支**填 `UpdateTime`/`UpdateUserId` → `SetColumns` 条件更新拿不到它们,这是本轮最关键的一条接线事实)、`Engine/WorkflowEngine.cs`(全文 985 行:8 个 `BeginXxxAsync` + `ClaimDueTaskAsync`/`PlanTimeoutOpsAsync`/`ClearDueTimeAsync`)、`Engine/WfExecutionContext.cs`(全文 101 行,`sealed`,`AppendHistoryAsync` 是唯一的非 virtual 共享写步骤 = 本轮 CAS 助手的形状先例)、`Engine/Operations/` 全部 7 个 Op(`EnterNodeOp` 418 行 / `CompleteTaskOp` 232 行 / `TakeTransitionOp` 71 行 / `CancelInstanceOp` 73 行 / `ReturnTaskOp` 162 行 / `ReassignTaskOpBase` 157 行 / `TransferTaskOp`+`DelegateTaskOp`)、`Jobs/WfTimeoutJob.cs`(全文 475 行:`RetireTaskAsync` 的带 `Version` 条件清 `DueTime`、`HandleRemindAsync` 的「不做版本 CAS」注释)、`Engine/WfCommands.cs`、`Abstractions/WorkflowErrorCode.cs`(48001-48026,48022 空洞)、`WfEnums.cs`、`backend/tests/TenonAdmin.Tests/WfTimeoutTests.cs`(`VersionOf`/`MakeDue`/`RunTimeoutJob` 等 DB 直读脚手架 = 本轮测试的现成工具)、`WfCancelTests.cs`(端点级用例形状)、`WorkflowAppFactory.cs`。文档侧读了 `workflow-database-design-review-2026-08-24.md` §4.1(6 类竞争 + 双条件 CAS 原文)/§九(8 条兼容升级纪律)/§十「M2b 收口(2026-08-24 提前项)」、`workflow-design-plan-2026-08-17.md` §十五 15.1。
>
> **核心判断(本轮最重要的结构决定):CAS 采「先领取、再写状态」两条语句,而不是把状态与版本挤进同一条 `SetColumns`。** 领取语句就是 §4.1 的原文形状(`WHERE Id=@id AND Status=@expectedStatus AND Version=@oldVersion` → `Version = Version + 1`),随后**原有的整对象状态更新一行不改**。三条理由:①**审计不回退**——`SqlSugarSetup` 的审计 AOP 只认 `UpdateByObject`,一旦把状态写改成 `SetColumns`,`UpdateTime`/`UpdateUserId` 就得每处手填,而「谁做的这次操作」在 6 个落点各不相同(`CancelInstanceOp` 现在硬编码 `StarterUserId` 就是这个税的现场),等于为了 CAS 引入一套并行的审计填充逻辑;②**正确性等价**——领取语句成功即持有该行的排他行锁直到提交,后一条语句处在同一事务的锁保护区内,中间不可能被插入;③**可评审性**——每个落点的 diff 是「加一行 `await ctx.ClaimXxxAsync(期望状态, ct);`」,状态写那几行逐字不动,review 能一眼看出「行为没变、只多了一道闸门」。这与已发布的 `ClaimDueTaskAsync`(领取 `wf_task.Version` 后交给 Op 自己再 CAS 一次)是**同一个形状**,不是本轮新造的模式。

<!-- TASK9-PLAN-ANCHOR -->

### 决策点

| # | 决策点 | 裁定 | 理由 / 代价 |
|---|---|---|---|
| D1 | 新列怎么声明 | `WfInstance.Version` / `WfToken.Version`:`[SugarColumn(ColumnDescription = "乐观锁版本", DefaultValue = "0")] public int Version { get; set; }` —— 非空 `int` + **DB 级默认值 0** | §九 第 2 条写死「新增列先 nullable 或带跨数据库一致的默认值」,第 3 条写死「`Version` 从 0 开始,旧行可直接回填」。**不能照抄 `WfTask.Version` 的裸 `int`**:`wf_task` 是 M1 建表时就带这一列,走的是 `CREATE TABLE`;这两列是 `ALTER TABLE ADD COLUMN`,SQLite / PostgreSQL / SQL Server 三家在有存量行时**拒绝无默认值的 NOT NULL 新列**(MySQL 会隐式补 0)。`DefaultValue` 让四库的 DDL 都合法、旧行自动回填 0,同时新建库的 `CREATE TABLE` 也带上 `DEFAULT 0`。**不选可空 `int?`**:那会让每处 CAS 都要处理 `null`,而「回填 0」是免费的 |
| D2 | CAS 助手放哪 | `WfExecutionContext` 上两个新方法:`ClaimInstanceAsync(WfInstanceStatus expectedStatus, ct)` / `ClaimTokenAsync(WfTokenStatus expectedStatus, ct)` | 6 个落点分散在 5 个文件,把语句抄 6 遍正是评审连着三次标 P3 的「几乎相同的抄写」。ctx 是**事务作用域的共享写步骤载体**,`AppendHistoryAsync` 就在那儿,形状逐点同构。**ctx 是 `sealed`、方法非 `virtual` 不违反可替换性教条**:可覆写的缝在**调用方**——`CancelInstanceOp.ExecuteAsync`、`TakeTransitionOp.CompleteInstanceAsync`、`CompleteTaskOp.RejectInstanceAsync`、`EnterNodeOp.ExecuteAsync`、`ReturnTaskOp.ExecuteAsync`、`WorkflowEngine.BeginResubmitAsync` 全部已是 `virtual`,消费者要换 CAS 语义就覆写那一步(和今天覆写 `AppendHistoryAsync` 调用点是同一条路) |
| D3 | CAS 失败返什么码 | 一律 `InstanceStatusConflict`(48004)+ `args["reason"]`(`instanceVersionConflict` / `tokenVersionConflict`)+ `args["instanceId"]`/`["tokenId"]` | **零新增错误码**。对称论证:任务级 CAS 输了统一是 `TaskConflict`(48007),那么实例/Token 级 CAS 输了统一是 `InstanceStatusConflict`(48004)——48004 的既有文案「实例状态不允许本操作」正好覆盖「实例/token 已被别的动作推进走了」。一码多 `reason` 是本仓既有惯例(`TransferTargetInvalid`/`NobodyBlocked`/`CancelNotAllowed`/`ReturnNotAllowed` 都这么干)。**新增码的代价是实的**:P3-#9 记着五个 M2b 错误码在两个语言包里还没键、挂在 Task 14,再加一个就是 12 条 |
| D4 | 实例级 CAS 落在哪几处 | 凡是写 `WfInstance.Status` 的地方,共 **3 处**:`CancelInstanceOp`(Running→Cancelled)、`CompleteTaskOp.RejectInstanceAsync` 的终止分支(Running→Rejected)、`TakeTransitionOp.CompleteInstanceAsync`(Running→Approved,以及将来的 Terminated) | 这三处就是「终态写入」竞争的全部出口。**这正面回答 Round 28 review 的 M3 顾虑**:并行网关下同实例的两件待办各自通过任务级 CAS 后,两个事务都会走到这三处之一,而 `WHERE Status=Running AND Version=@old` 只有一个能拿到 1 行,输的那个抛错 → **整个事务回滚**(引擎「一条 Cmd 一个事务」),不会留下半推进的实例。M2b 还是单 token,但这层设计现在就立住了 |
| D5 | Token 级 CAS 落在哪几处 | 凡是写 `WfToken.Status` **或** `WfToken.NodeId` 的地方,共 **6 处**:`EnterNodeOp.ExecuteAsync`(NodeId 推进,Active→Active)、`ReturnTaskOp`(NodeId 回退)、`BeginResubmitAsync`(NodeId 归零 = **前置约束 2 的锚点**)、`CancelInstanceOp`(Active→Cancelled)、`CompleteTaskOp.RejectInstanceAsync` 终止分支(Active→Cancelled)、`TakeTransitionOp.CompleteInstanceAsync`(Active→Completed) | **`NodeId` 推进就是状态推进**,不能只盯 `Status` 列。把 `EnterNodeOp` 也纳进来是本轮覆盖 §4.1 第 1 条「审批与撤销」的**唯一**手段:一次会推进 token 的同意会 bump token 版本,并发撤销的 token CAS 就落空(反之亦然),两者互斥。代价:每次进节点多一条 UPDATE 往返(一次发起 2 条、一次同意 1-2 条),换的是「审批 vs 撤销」不再靠数据库隔离级别碰运气 |
| D6 | 前置约束 1:`ReassignTaskOpBase` 的任务级 CAS | **一行不改,继续作为第一个写操作**;转办/委托**不加**任何实例/Token 级 CAS | 转办与委托压根不改实例状态、不改 token(节点没变、状态没变),实例/Token 级 CAS 对它们**不构成任何保护**——两个并发委托同一件待办时实例与 token 一字不动,新 CAS 拦不住,后果是两行 Pending actor + 两条 `Delegate` 历史。所以 `ReassignTaskOpBase.cs:76-85` 那段是它们并发安全性的**唯一**锚点,本轮**显式保留**并在类级/段级 XML 写清「这不是冗余、新的实例级 CAS 不覆盖本路径」。**反向也要守住**:不给改派加实例级 CAS —— 那会让同实例上两件**不同**待办的并发委托互相冲突(过度加锁),而它们本该各行其道。两个方向各有一条钉子(测试 7) |
| D7 | 前置约束 4:拆不拆 `ReassignTaskOpBase.ExecuteAsync`(121 行) | **本轮不拆。**边界建议原样保留在 `## Findings` 里,交给真正要改那段代码的那一轮 | Round 26 把这个决定交给「正要动其中 CAS 段的那一轮」。**本轮读码后的事实是:它不动那一段,一个字都不动**(见 D6:改派不进新 CAS 的收口清单)。既然移交的前提不成立,决定就该继续往后传。三条理由:①拆步骤会把这个文件从「121 行经 Round 26 逐字核验过、可 `Compare-Object` 证明未被重写」变成一次不可证伪的重写,而本轮对它的义务恰恰是**证明没动过**,比 Round 26 更强;②步骤边界是**新的覆写契约**,一经发布不好改,而本轮对这些边界**零需求**——没有任何一个新行为需要在 `ValidateTargetAsync`/`ClaimAsync` 之间插东西;③`ExecuteAsync` 仍是 `public virtual`,消费者的整体覆写能力一点没少,拆步只是把「能不能只改一步」从 0 提到 1,而今天没有任何已知消费诉求指向那一步。**下一个真正会动它的轮次**是 M3 的加签/减签/拿回(要在认领与挂 actor 之间插入多 actor 编排),那时边界需求是具体的,画出来的缝才准 |
| D8 | 前置约束 3:提醒路径 | `WfTimeoutJob.HandleRemindAsync` / `ShouldRemindAsync` / `RetireTaskAsync` **一行不改**,不加任何级别的 CAS | 定案已在 `## 语义契约` 与 `WfTimeoutJob` 类级 XML 里:提醒什么状态都不改,加 CAS 的后果是办理人**为了一条提醒**收到 48007。本轮顺手把现有钉子 `Timeout_remind_does_not_block_human_action` 的版本不变量从「`wf_task.Version` 一字未动」扩到**三个级别都一字未动**(测试 8),这样「顺手给提醒加 CAS」在实例/Token 这两个新级别上也有报警器,而不是只在任务级有 |
| D9 | 记账事项:`CancelInstanceOp` 找活跃任务只按 `TokenId` | **不顺手收口,继续记账** | 今天单 token,`TokenId == ctx.Token.Id` 与 `InstanceId == ctx.Instance.Id` 选出的是同一批行 → 改成 `InstanceId` 在今天是**行为恒等且不可证伪**的改动。更要紧的是它会**预先承诺一个属于 M3 的语义**(「撤销杀掉所有分支」),而真正的 M3 撤销还得同时收掉**其它 token 行**本身,不只是它们的待办;只改一半看起来像做完了,比没做更危险。记账留在 `## Findings` |
| D10 | 新增列要不要索引 | **不加。**`Version` 只出现在 `WHERE Id = @id AND ... AND Version = @old` 里,主键已经把行定位到 1 条,`Version` 是行内比较 | 加索引纯负债(每次 bump 都要维护索引) |
| D11 | 要不要给命令加 `ExpectedVersion` 入参 | **不加。** | 那是 M2c 的 `RequestId`/operation receipt 那一档(§十 M2c 第 2-3 条)。只为「让单线程套件能构造 CAS 失败」而给公开命令加字段,是把测试需求焊进产品 API;本轮按台账既有先例(Task 4 Round 17)诚实降级为「读码逐处核对 + 机制断言 + 全量回归零破坏」 |
| D12 | 要不要扩 `IWfTaskService`/`IWfInstanceService` | **不扩**,故 `WorkflowReplaceabilityTests` 的两个 Fake **零改动** | 本轮全部改动在引擎/Op/实体内部,没有新的服务方法。这是第五次扩接口的风险点,主动避开 |

### 每处状态翻转的收口方式(逐处,含现状行号)

| # | 落点 | 现状 | 收口后 |
|---|---|---|---|
| 1 | `CancelInstanceOp.cs:15-26` 实例终态 | 已有**状态**条件更新(`SetColumns(Status/UpdateTime/UpdateUserId).Where(Id && Status==Running)`),`UpdateUserId` 手填 `StarterUserId`(因 `SetColumns` 绕过审计 AOP) | 换成 `await ctx.ClaimInstanceAsync(WfInstanceStatus.Running, ct);` + 原样的整对象状态更新(`ctx.Instance.Status = Cancelled; Updateable(entity).UpdateColumns(Status, UpdateTime, UpdateUserId)`)。**顺带把手填审计还给 AOP**——与另两个终态出口写法归一,`Task 4 Round 17` 那段解释 `SetColumns` 绕 AOP 的注释迁进 `ClaimInstanceAsync` 的 XML |
| 2 | `CancelInstanceOp.cs:28-31` token 终态 | 无条件整对象更新 | 前面加 `await ctx.ClaimTokenAsync(WfTokenStatus.Active, ct);` |
| 3 | `CompleteTaskOp.cs:195-203` 拒绝终止分支的实例 + token | 两处都是无条件整对象更新 | 各自前面加 `ClaimInstanceAsync(Running)` / `ClaimTokenAsync(Active)`。**`ToNode` 分支不加**——它压根不写实例/token 状态,token 的推进由它 plan 的 `EnterNodeOp` 负责(落点 5 已覆盖) |
| 4 | `TakeTransitionOp.cs:34-42` 实例完结 + token 收尾 | 两处都是无条件整对象更新 | 同上。这是「多 Token 对同一实例终态的竞争」(§4.1 第 6 条)与「终态写入与重提」(第 4 条)的主出口 |
| 5 | `EnterNodeOp.cs:27-30` token NodeId 推进 | 无条件整对象更新,是 `ExecuteAsync` 的第一个写操作 | 前面加 `await ctx.ClaimTokenAsync(WfTokenStatus.Active, ct);`,**保持它仍是第一个写操作**。一次事务里可能跑 N 次 `EnterNodeOp`(start → 汇合 → 审批节点),每次 bump 一次,助手把新版本写回 `ctx.Token.Version` 故后续 CAS 对得上(与 `ClaimDueTaskAsync` 写回 `task.Version` 同理) |
| 6 | `ReturnTaskOp.cs:98-101` token NodeId 回退 | 无条件整对象更新 | 前面加 `ClaimTokenAsync(Active)`。**放在任务级 CAS 之后**(:27-36 那段不动),保持「先抢任务、再动 token」的既有顺序 |
| 7 | `WorkflowEngine.BeginResubmitAsync:946-953` 实例字段更新 + token NodeId 归零 | **全程无任何 CAS 锚点**(前置约束 2:两处 `Updateable(entity).UpdateColumns(...)` 都无条件,`:897` 的「无活跃任务」校验只是读)。双击重提 → 两个事务都过校验、都 `Plan(EnterNodeOp(root))` → 同节点两套 `WfTask`/actor + 两条 `InstanceResubmitted` + 两次通知 | **把 `ctx` 的构造上移到两条 UPDATE 之前**,然后 `await ctx.ClaimTokenAsync(WfTokenStatus.Active, ct);` 作为**本事务的第一个写操作**,再走原有的实例 UPDATE → token UPDATE。两个并发重提都读到 `Version=v`,只有一个拿到 1 行,输的抛 48004 + `reason=tokenVersionConflict` → 整事务回滚,连 `InstanceResubmitted` 与通知一起。**为什么锚在 token 而不是实例**:重提不改实例状态(Running→Running),实例侧没有可锚的「期望状态 + 版本」语义变化;而 token 的 `NodeId` 归零**就是**这次重提的状态推进,锚在它上面既是真锚点也符合 §4.1 的原文形状。`ctx` 上移的可行性已核:`StarterOrgId`/`LeaderChainByLevel` 是 `init` 属性,故必须在 `starterOrgId`/`leaderChainByLevel` 算完之后构造 —— 那两步(`:932-944`)本来就在两条 UPDATE 之前,顺序不冲突 |
| 8 | `WfTimeoutJob` 领取路径 | `ClaimDueTaskAsync`(`WorkflowEngine.cs:634-653`)已是 §14.1 的三条件任务级 CAS | **不改。**它领取的是**任务**,与本轮的实例/Token 级是两层不同的仲裁(前者仲裁「Job vs 人工抢同一件待办」,后者仲裁「谁推进实例/token」)。领取之后入队的 `CompleteTaskOp`/`TransferTaskOp` 会自动经过落点 3/4/5 拿到新 CAS,**零额外接线** |
| 9 | `ReassignTaskOpBase`(转办/委托) | 任务级 CAS 在 `:76-85` | **一行不改**(D6)。只加注释,说明这段为什么不是冗余 |
| 10 | `WfTimeoutJob.HandleRemindAsync` / `RetireTaskAsync` | 提醒零 CAS;`RetireTaskAsync` 带 `Version` 条件清 `DueTime` 但**不 bump** | **一行不改**(D8) |

**「零改动」逐条确认(不是默认)**:`Engine/Operations/TransferTaskOp.cs`、`DelegateTaskOp.cs`、`Abstractions/WorkflowErrorCode.cs`(**零新增码**)、`Entities/WfEnums.cs`(**零枚举变更**)、`Entities/WfTask.cs`/`WfHisTask.cs`/`WfHistory.cs`/`WfCc.cs`/`WfTaskActor.cs`/`WfDefinition*.cs`(**除 `WfInstance`/`WfToken` 外零 schema 变更**)、`Engine/WfCommands.cs`(**零命令形状变更**,D11)、`Services/*`、`Controllers/*`、`IWfTaskService`/`IWfInstanceService`(D12)、`WorkflowReplaceabilityTests.cs`、`Abstractions/WorkflowOptions.cs`、`WorkflowSetup.cs`、`web/`、`web-react/`、`docs/workflow/`、`## 语义契约`。

### 改动清单

| 文件 | 动作 |
|---|---|
| `Entities/WfInstance.cs` | 加 `Version` 列(D1)+ 一句 XML 说明它锚的是「实例终态写入」这类竞争 |
| `Entities/WfToken.cs` | 加 `Version` 列(D1)+ 一句 XML 说明 `NodeId` 推进也算状态推进、也走 CAS |
| `Engine/WfExecutionContext.cs` | 新增 `ClaimInstanceAsync` / `ClaimTokenAsync` 两个方法(D2/D3),XML 写清:①双条件 CAS 的原文形状与出处(§4.1);②为什么是「先领取再写状态」两条语句而不是一条(审计 AOP 只认 `UpdateByObject`);③必须写回内存 `Version`;④非 `virtual` 时可覆写的缝在调用方 |
| `Engine/Operations/CancelInstanceOp.cs` | 落点 1 + 2 |
| `Engine/Operations/CompleteTaskOp.cs` | 落点 3(只碰 `RejectInstanceAsync` 的终止分支) |
| `Engine/Operations/TakeTransitionOp.cs` | 落点 4 |
| `Engine/Operations/EnterNodeOp.cs` | 落点 5(只碰 `ExecuteAsync` 开头三行的前面) |
| `Engine/Operations/ReturnTaskOp.cs` | 落点 6 |
| `Engine/WorkflowEngine.cs` | 落点 7(`BeginResubmitAsync` 的 ctx 上移 + 首个写操作换成 token 领取) |
| `Engine/Operations/ReassignTaskOpBase.cs` | **仅注释**:在 `:76-85` 那段 CAS 上方加一段说明它是转办/委托并发安全的唯一锚点、实例/Token 级 CAS 不覆盖本路径、两个方向都有钉子(前置约束 1) |
| `backend/tests/TenonAdmin.Tests/WfVersionCasTests.cs` | **新增**,测试 1-7 |
| `backend/tests/TenonAdmin.Tests/WfTimeoutTests.cs` | 只给既有 `Timeout_remind_does_not_block_human_action` 加实例/Token 两级版本不变量(测试 8) |

### 步骤

1. `WfInstance.Version` + `WfToken.Version` 两个列(D1)。跑全量确认 165 不掉——此时无任何代码读写这两列,唯一可能出事的是 CodeFirst 建表本身(`DefaultValue` 在 SQLite 的 `CREATE TABLE` 上被拼坏会让**所有**工作流用例红,这一步就是它的探针)。
2. `WfExecutionContext.ClaimInstanceAsync` / `ClaimTokenAsync`(D2/D3),含写回内存 `Version`。此时零调用点,跑全量仍应 165。
3. **测试 1**(`New_instance_and_token_start_at_version_zero`)—— 先写、此刻**应该绿**(新列默认 0、还没人 bump)。它是「D1 真的把列建出来了、默认真是 0」的正向确认;绿了再往下,不绿说明第 1 步的 `DefaultValue` 有问题。
4. 落点 5(`EnterNodeOp` token 领取)。**第一个动的落点**,因为它是唯一在「正常正向推进」路径上的,能立刻暴露「一次事务里 bump 多次 + 写回」是否成立;若写回漏了,后续同事务的 CAS 会抛假 48004,全量套件会大面积红——这是一个自带探针的步骤。跑全量。
5. **测试 2**(`Start_advances_token_version_once_per_node_entry`)。
6. 落点 4(`TakeTransitionOp` 实例完结 + token 收尾)+ **测试 3**(`Approve_to_completion_claims_instance_and_token`)。
7. 落点 3(`CompleteTaskOp.RejectInstanceAsync` 终止分支)+ **测试 4**(`Reject_terminate_claims_instance_and_token`)。
8. 落点 1 + 2(`CancelInstanceOp`)+ **测试 5**(`Cancel_claims_instance_and_token`)。
9. 落点 6(`ReturnTaskOp`)+ **测试 6 前半**。
10. 落点 7(`BeginResubmitAsync`,**前置约束 2**)+ **测试 6 后半**(`Return_then_resubmit_claims_token_at_every_hop`)。
11. `ReassignTaskOpBase` 加注释(**不改代码**)+ **测试 7**(`Reassign_claims_task_version_only_and_leaves_instance_and_token_untouched`,前置约束 1 的双向钉子)。
12. **测试 8**:给 `WfTimeoutTests.Timeout_remind_does_not_block_human_action` 补实例/Token 两级版本不变量(前置约束 3)。
13. 全量套件 + `dotnet build -c Release` + `git diff --check` + `rg` 扫 `MUTATION`/`TODO`/`FIXME`/`.Skip(`/`NotImplementedException` 残留;**逐个变异点亲手转红后复原,复原那一跑必须强刷测试文件时间戳确认真的重编过**(`(Get-Item <file>).LastWriteTime = Get-Date`,Round 28/29 的陈旧编译产物教训)。

### 测试清单(每条附区分力变异点)

> ⚠ **射程声明,先说清免得被误读**:CAS 的**失败**路径在本仓单线程 xUnit 套件里**构造不出来**——真实竞态需要「A 读版本 → B 提交推走版本 → A 写」这个交错,而所有 `BeginXxxAsync` 都在**自己的事务里现读**版本号,单线程顺序执行下读到的必然是最新值,CAS 永远对得上(与 Round 28 证伪 `Timeout_remind_does_not_block_human_action` 的根因逐字同型)。所以下面 7 条钉的一律是**机制**:「这个落点确实做了双条件领取并推进了版本」。这不是套套逻辑——把任何一处 CAS 退回成无条件整对象更新,版本就不再前进,对应用例立刻红。**用户可见后果(并发下不产生半推进状态)不在射程内**,按台账既有先例(Task 3 Round 13 / Task 4 Round 17 / Task 7 Round 26)以「读码逐处核对 + 全量回归零破坏」替代。

| # | 用例 | 断言 | 变异点(必须能转红) |
|---|---|---|---|
| 1 | `New_instance_and_token_start_at_version_zero` | 建一个只有 `start` 的最短链跑不通,故用 1 审批节点模型:发起后读 `wf_instance.Version == 0`(还没有终态写入)。**列存在性 + 默认值 0** 的正向确认 | 无变异(这是 D1 的正向确认,不是钉子);它的反向保障是「列没建出来 → 查询直接抛」 |
| 2 | `Start_advances_token_version_once_per_node_entry` | 1 审批节点模型发起后 `wf_token.Version == 2`(`EnterNodeOp(start)` 一次 + `EnterNodeOp(approve-1)` 一次;算式写进注释),且 `wf_instance.Version == 0` | 去掉 `EnterNodeOp` 里的 `ClaimTokenAsync` → `Expected: 2 / Actual: 0` 红 |
| 3 | `Approve_to_completion_claims_instance_and_token` | 同意到底后:`wf_instance.Version == 1` 且 `Status == Approved`;`wf_token.Version == 3`(2 次进节点 + 1 次终态领取)且 `Status == Completed` | ①去掉 `CompleteInstanceAsync` 的 `ClaimInstanceAsync` → 实例版本停在 0 → 红;②去掉其 `ClaimTokenAsync` → token 版本停在 2 → 红(**分两次单独变异**,证明两个断言各自独立被钉) |
| 4 | `Reject_terminate_claims_instance_and_token` | 拒绝(节点无 `onReject` = 默认终止)后:`wf_instance.Version == 1` 且 `Status == Rejected`;`wf_token.Version == 3` 且 `Status == Cancelled` | 去掉 `RejectInstanceAsync` 终止分支的两个 Claim → 分两次各自红 |
| 5 | `Cancel_claims_instance_and_token` | 撤销后:`wf_instance.Version == 1` 且 `Status == Cancelled`;`wf_token.Version == 3` 且 `Status == Cancelled` | ①`CancelInstanceOp` 的 `ClaimInstanceAsync` 去掉 → 实例版本停在 0 → 红。**注意**:该处现有的状态条件更新已能拦住重复撤销,所以变异必须只去掉**版本**那一半才有区分力,即把 `ClaimInstanceAsync` 换回原来的 `SetColumns(...).Where(Id && Status==Running)` → 状态照旧翻,版本不前进 → 红,证明本轮真的加了版本这一维;②`ClaimTokenAsync` 去掉 → token 版本停在 2 → 红 |
| 6 | `Return_then_resubmit_claims_token_at_every_hop`(**前置约束 2 的钉子**) | 两节点链 `start→node1[A]→node2[B]`,`node1` 配 `returnPolicy: prev`:发起后 token 版本 2 → A 退回后 **3**(`ReturnTaskOp` 领取一次)且 `wf_instance.Version == 0`(退回不动实例状态)→ 发起人重提后 **6**(重提领取 1 + `EnterNodeOp(start)` 1 + `EnterNodeOp(node1)` 1) | ①去掉 `ReturnTaskOp` 的 `ClaimTokenAsync` → 退回后 `Expected: 3 / Actual: 2` 红;②去掉 `BeginResubmitAsync` 的 `ClaimTokenAsync` → 重提后 `Expected: 6 / Actual: 5` 红。**变异 ② 是前置约束 2 的直接区分力**:它现在是「重提有没有锚点」的唯一可观测出口 |
| 7 | `Reassign_claims_task_version_only_and_leaves_instance_and_token_untouched`(**前置约束 1 的双向钉子**) | 会签两人模型(委托与转办都需要一个不在 actor 里的目标),记下三个级别的版本 → 委托一次 → `wf_task.Version` +1、`wf_instance.Version` 不变、`wf_token.Version` 不变;同一用例再转办一次,三条断言重复一遍 | ①**保住任务级 CAS 这一侧**:删掉 `ReassignTaskOpBase.cs:76-85` 那段任务级 CAS(顺手把它当冗余放松掉,正是前置约束 1 担心的事)→ `wf_task.Version` 不前进 → `Expected: 1 / Actual: 0` 红;②**不过度加锁这一侧**:给 `ReassignTaskOpBase` 加一句 `await ctx.ClaimInstanceAsync(WfInstanceStatus.Running, ct);` → `wf_instance.Version` 从 0 变 1 → 红 |
| 8 | (改现有)`WfTimeoutTests.Timeout_remind_does_not_block_human_action`(**前置约束 3**) | 现有的 `wf_task.Version` 不变量之外,补 `wf_instance.Version` 与 `wf_token.Version` 也一字未动 | 给 `HandleRemindAsync` 加任意一级 CAS(任务级已有既存变异记录;新增:加 `wf_token` 版本 bump)→ 红 |

预期基线:**165 → 172**(+7 条新用例;测试 8 是给既有用例加断言,不计数)。

### 陷阱记录(Task 9 plan 阶段读码所得,提醒 exec 别踩)

1. **`SetColumns` 里的内联计算表达式会被按当前区域设置拼成字面量进 SQL。** 台账已有实测(zh-CN 下 `DateTime` 表达式拼出「下午」→ `SQLite Error 1: near "下午": syntax error`,500 而不是断言红)。本轮两个 Claim 助手要写 `Version = <某个算出来的整数>`,**先算进局部变量再进 `SetColumns`**,别写 `Version = Token.Version + 1` 这种内联表达式。整数目前不会踩(`ClaimDueTaskAsync` 的 `expectedVersion + 1` 正常参数化),但养成局部变量的习惯零成本。
2. **写回内存 `Version` 是硬要求,漏了就是一片假 48004。** `EnterNodeOp` 一次事务里可能领取 2-3 次 token,`TakeTransitionOp` 之后还要领一次终态。助手内部必须 `Instance.Version = next` / `Token.Version = next`。这与 `ClaimDueTaskAsync` 的 `task.Version = expectedVersion + 1`(台账陷阱记录第 14 条)是同一条纪律,只是这次一个事务里可能前进**四五次**而不是两次。
3. **`Updateable<T>().SetColumns(...).Where(...)` 不走审计 AOP。** `SqlSugarSetup.cs:169-176` 的 `UpdateByObject` 分支是唯一填 `UpdateTime`/`UpdateUserId` 的地方。这正是本轮取「两条语句」的原因(D2 核心判断):领取语句不碰审计字段、状态写继续走整对象更新拿 AOP。**别顺手把状态也挤进领取语句**,那会静默丢掉 `UpdateUserId`(编译过、测试绿、审计列变 null)。
4. **`Updateable<WfInstance>()` 不会被全局数据范围/软删过滤器清掉。** `CancelInstanceOp` 现成先例(条件更新跑了 100+ 轮全绿)。所以 Claim 助手**不需要** `ClearFilter<IOrgScoped>()`;查询侧的 `ClearFilter` 惯例不要照搬到更新侧(照搬也不错,但会让人以为不加就会瞎掉)。
5. **`BeginResubmitAsync` 的 ctx 上移要盯住 `instance.SelectedUserIdsJson` 的读写顺序。** 现状是先按 `cmd.SelectedUserIdsByNode` 覆盖 `instance.SelectedUserIdsJson`(`:926-930`),ctx 构造时再 `cmd.SelectedUserIdsByNode ?? DeserializeSelectedUsers(instance.SelectedUserIdsJson)`(`:970-971`)。上移 ctx 时这两步的相对顺序**必须保持**(覆盖在前、读在后),否则「重提时改了自选审批人」会静默用旧值。
6. **`EnterNodeOp` 的 token 领取必须留在 `ExecuteAsync` 的最前面。** 它现在是该方法的第一个写操作;插到 `AppendHistoryAsync(NodeEnter)` 之后就等于「先留痕再抢锁」,并发下会出现两条 `NodeEnter` 只有一次真推进。
7. **测试里的版本数字是算出来的,不是量出来的。** 每条断言旁边写清算式(几次进节点 + 几次终态领取),否则下一轮有人改了节点数就只会把期望值改成实测值,钉子当场失效。
8. **`Cancel` 那条变异要小心「已经有一半保护」。** `CancelInstanceOp` 今天就有状态条件更新,直接删掉整个 Claim 会让**状态**那一维也没了、可能撞上别的用例,变成「红了但不是因为版本」。变异必须精确地只退掉版本那一半(见测试 5 的变异 ①)。
9. **`DefaultValue = "0"` 是本轮唯一一处全仓首次使用的 SqlSugar 特性**(`rg DefaultValue` 全仓零命中)。第 1 步单独跑一次全量就是它的探针:SQLite 的 `CREATE TABLE` 拼坏 → 所有工作流用例红。四库的 `ALTER TABLE ADD COLUMN` 路径本轮**验证不到**(测试库都是新建的),按任务范围留给 M2c 的四库契约测试,已在 `## Findings` 立条。
10. **别给 `ReturnTaskOp` / `BeginResubmitAsync` 的 token 领取写 `WfTokenStatus.Active → 别的值`。** 退回与重提之后 token **仍是 Active**(实例保持 Running,不是完结)——期望状态与目标状态都是 Active,领取只推进版本。把它写成翻状态会让退回把实例变成完结态。

> ⚠️ 以下两个「必答问题」小节是 **Task 6 已收口的契约性定案**(委托权收窄为「仅当前 Pending 办理人」、链式委托允许且不设上限),按约定在 `## Plan` 重写时整段保留,勿删。本次纯重构不翻转其中任何一条。

### 必答问题一:「委托」与「转办」除 `WfHisTask.Action` 标签外还有什么实质差异?

| 维度 | 结论 |
|---|---|
| **谁有权发起** | **只有当前 Pending 办理人**,与转办完全相同。`## 语义契约` 表里「发起人/办理人」这个措辞**按「委托发起人=当前办理人」解读,不给实例发起人开委托权**。理由:①**授权漏洞**——实例发起人若能把审批自己单子的待办指给任意第三人,等于自选审批人,整套 RBAC/多级主管解析全部作废;②**发起人的工具箱本来就是「不改他人任务状态」那一套**:催办(只写历史+推送)、撤销(仅无人已批、且是自己的单子)、重提(自己的单子),而委托要改「谁来批」,属于办理人权限;③机制上它是照 `TransferTaskOp` 的 CAS 认领(`UserId == caller && Status == Pending`)自然落地的,发起人调用时认领不到 actor → 抛既有 `TaskConflict`,不需要额外校验代码。**这是对语义契约措辞的收窄解读**,已用 `Starter_cannot_delegate_others_todo` 钉死;若协调者认为契约本意确实是「实例发起人也能委托」,需要翻转本条并改写 `## 语义契约` 表——本轮按上面的安全解读实现。 |
| **被委托人拿到新 actor 还是继承原 actor** | **新 actor**(照转办:原 actor 翻 `Skipped`,插一行新 `WfTaskActor`,`Sort` 沿用原值以免搅乱顺序会签)。不复用原行还有个副产品是**天然的环路上界**:`TransferTaskOp` 那条「目标已是本待办任一 Approver → 拒绝」只看 actor 行存在性、不看状态,所以 A→B→C 之后 C 再委托回 A/B 会被 `alreadyActor` 拦下,链长天然 ≤ 本待办参与过的人数。 |
| **是否影响 `DurationMs` 计时基准 / `DueTime`** | **不影响**,与转办一致:`wf_task` 行不重建,`DurationMs` 仍按 `now - Task.CreateTime` 算,`DueTime` 一字不动。语义上是「同一件待办换人办」,不是「新开一件待办」——委托不该成为重置超时时钟的手段。这条对 Task 7 的超时 Job 是前置约束:委托过的任务照原 `DueTime` 到期。 |
| **通知语义** | 与转办一致:`ctx.NewAssigneeUserIds.Add(ToUserId)` + 排队一条 `TaskAssignedAsync`(收件人只有被委托人)。**不加 `IWorkflowNotifier` 新方法**——语义契约没提委托专属通知,加方法会第四次破坏消费者的 notifier 实现,YAGNI。 |
| **对「同一人相邻节点去重」的影响** | **零影响,且必须保持零影响**。`Delegate` 不是向后跳转,**不得**进 `EnterNodeOp.ResolveAdjacentApprovedUserIdsAsync` 的 `Action IN (Approve, Reject, Return)` 白名单。本任务**完全不打开 `EnterNodeOp.cs`**,并用 `Delegate_row_does_not_reset_adjacent_dedup_baseline` 反向钉住(会签链里插一条 Delegate 行,断言下游去重照旧生效)。 |
| **对撤销的影响** | 零影响:`BeginCancelAsync` 的准入条件是「无任何 `Approve` 行」,委托行不是批准,不阻塞撤销——与转办同款,无需改代码。 |
| **既然机制同构,为什么还值得单独一个动词而不是给 `Transfer` 加个枚举参数** | 三条产品理由,**第一条是本仓特有的硬理由**:①**权限码即路由**(`POST:/api/v1/workflow/task/delegate` vs `.../transfer`)。代码里没有权限字符串,授权靠在「角色管理 → 授权菜单」按路由勾选——合成一个端点意味着「允许转办」与「允许委托」永远只能一起给或一起不给,组织无法表达「可委托不可转办」。把动作降级成入参,等于把一个可授权的动作变成不可授权的。②**问责语义不同**:转办=把活儿交出去(责任转移),委托=请人代办(委托人仍在链上留痕,`已办` 列表里两人各有一行不同动作)。`WfDoneItemOutput.Action` 与详情时间线直接吃 `wf_his_task.Action`,标签不同前端就能分开呈现,不需要额外字段。③**给 M3 的长期委托规则留位**:规则驱动的自动委托将来写 `Action = Delegate`,与手动一次性委托同标签、与转办永久区分;今天若把委托做成转办的入参,M3 的规则引擎得回头重新解释历史转办行。 |

### 必答问题二:委托后谁能再委托/转办?链式委托允许吗?

- **允许链式**:A 委托 B 后,B 是本待办唯一的 Pending Approver,B 可以再委托给 C,也可以转办/退回/同意/拒绝——**所有办理人动词对 B 一视同仁**,因为委托不留任何「你是被委托来的」标记位。
- **不做次数/深度限制**:这是 YAGNI 定案,**不是遗漏**。理由:①现有 `Transfer` 本来就可无限转办,委托凭什么更严;②真实诉求是「本人不在,请人代办」,天然一两跳;③上面那条 `alreadyActor` 校验让环路走不成、链长 ≤ 本待办参与过的人数,不存在无界增长;④每一跳都在 `wf_his_task` 留一行不可删的审计记录,滥用可查可审。要加限制得先有「委托深度」这个业务字段与产品诉求,属于 M3 长期委托规则那一档。
- **`Delegate_chain_hands_todo_along_without_limit` 用 A→B→C 两跳钉住这条定案**,免得日后有人当缺陷「修」掉。

> ⚠️ 以下 `### 步骤 26` 是 Round 22 已收口的**契约性裁决记录**(Task 5 的 P1 下界取法),按约定在 `## Plan` 重写时整段保留,勿删。

### 步骤 26 — Round 21 Findings 修复方案(P1 修法待用户裁决)

**P1 的两个候选修法**(都要改 `EnterNodeOp.ResolveAdjacentApprovedUserIdsAsync`,差别在「相邻」的定义):

| 方案 | 做法 | 优点 | 代价 |
|---|---|---|---|
| **A. 向后跳转重置基线**(推荐) | 先查本实例最近一次向后跳转事件的 `wf_history.Id` 作为时间下界,只有晚于它的 `Approve` 行才参与基线比对。重提已有 `InstanceResubmitted` 事件;拒绝路由与主动退回目前只写 `TaskCompleted`(动作在 payload JSON 里),需各加一个专属 `WfHistoryEventType`(建议 `RejectRouted=13`/`TaskReturned=14`),避免靠解 JSON 判别 | 语义精确:「向后跳转 = 已批节点必须重批」正好对应 `## 语义契约` 的「退回后重提从头重走」;对正向推进零行为变化,不动 Task 3 已验证的去重语义;改动集中在一个查询 + 两个枚举值 | 新增 2 个枚举值(只增不重排,符合迁移纪律);要改 2 处历史写入点 |
| **B. 「相邻」改按模型前驱** | 要求 `rows[0].NodeId` 必须是待进入节点在模型里的直接前驱,否则视为无基线 | 概念更纯:`## 语义契约` 原文就是「紧邻的上一个已完成审批节点」 | M2a 已有分支,「直接前驱」在分支模型里不唯一、依赖实际走过的路径 → 仍要回头查历史,复杂度反而更高;会动 Task 3 已闭合的语义判定 |

**倾向 A**:B 看起来更贴字面,但分支模型下「前驱」本身要靠历史推导,等于把问题搬了个位置;A 只增加一条时间下界,是对现有实现的最小修正,且把「向后跳转」这件事显式记进事件流,对 Task 12 的流程图回放(P3 已记:回放要按最后一次访问收敛)也是必要信息。

> **✅ 用户裁决(2026-08-24):P1 取方案 A;P2-4 与 P2-5 本轮一并修,不设前置门。** 这条进 `## 语义契约` 的补充:**任何向后跳转(拒绝路由 / 主动退回 / 重提)都重置同一人相邻节点去重的基线**——跳转之前的 `Approve` 行不再参与比对,即已批节点在回退后必须重新审。这与「退回后重提默认从头重走」是同一条语义的两面。

**方案 A 的落地细节(exec 需自行裁定「时间下界」的取法,两个候选)**:

| 取法 | 做法 | 风险 |
|---|---|---|
| **同表下界**(优先评估) | 下界 = 本实例/token 的 `MAX(wf_his_task.Id) WHERE Action IN (Reject, Return)`。只有 `Id` 大于它的 `Approve` 行参与基线 | 同表比较,无跨表雪花 Id 风险。重提场景**依赖「重提必然前置一次 Return」**这个代理判据(review 的 P3 已记:当前单 token 模型下成立,M3 并行网关后失效)——必须补测试证明重提场景真的被覆盖 |
| 跨表下界 | 下界 = `MAX(wf_history.Id) WHERE EventType IN (RejectRouted, TaskReturned, InstanceResubmitted)`,与 `wf_his_task.Id` 比大小 | 直接覆盖三种跳转、不依赖代理判据,但**跨表比较雪花 Id**:同一 worker 内单调、多 worker 横向扩容时不严格单调,可能在极端时序下误判 |

无论取哪个,`RejectRouted=13`/`TaskReturned=14` 两个事件类型都要加并在两处跳转点写入——它们有独立价值(审计可读性 + Task 12 回放需要知道跳转发生在哪一步,见该项 P3),不只是为了当下界。枚举只追加数值、不重排已有值(迁移纪律)。

> **✅ exec 裁定(Round 22):取「同表下界」。** 落点 `EnterNodeOp.ResolveAdjacentApprovedUserIdsAsync`:查询把 `Action` 白名单从 `Approve` 放宽到 `Approve|Reject|Return`,按 `Id` 倒序后 `TakeWhile(h => h.Action == Approve)` —— 倒序遇到的第一条 `Reject`/`Return` 行就是最近一次向后跳转,它及更早的行一律出局;剩下的窗口再走原有的 `TakeWhile(NodeId == 第一行 NodeId)` 取最近一次连续访问区间。
>
> **理由(三条,按权重排序)**:
> 1. **零额外查询**。下界与基线在**同一次** round-trip 里取到——本来就要查这张表、就要按 `Id` 倒序,只是把 `Action` 白名单放宽两个值。跨表下界得再查一次 `wf_history`,而这段代码跑在「每次进入审批节点」的引擎事务热路径上。
> 2. **无跨表雪花 Id 比较**。跨表方案要拿 `wf_history.Id` 和 `wf_his_task.Id` 比大小;雪花 Id 在同 worker 内单调、多 worker 横向扩容时不严格单调(`TenonAdmin:Id:WorkerId` 不同实例不同),极端时序下会误判基线。同表比较把这个风险整个消掉。
> 3. **对未来动词是白名单而非黑名单**。`Action IN (Approve, Reject, Return)` 显式列举,Task 6 的 `Delegate`、Task 2 已落的 `Urge` 之类**不是**向后跳转的动作不会被误当成下界(它们压根进不了这个结果集);要新增一种向后跳转动词时必须显式往白名单里加一项——漏加会在这里被测试打红,而不是静默改变去重语义。
>
> **取舍(代价与已消解的疑点)**:
> - 代价:重提场景依赖「重提必然前置一次 `Return`」这个代理判据(`BeginResubmitAsync` 的准入条件是「Running + 无活跃待办」,review 已遍历确认当前单 token 模型下退回是唯一入口)。**已用测试兜住**:`Resubmit_after_return_to_immediately_previous_node_reassigns_that_nodes_approver` 走的正是「退回 → 重提」全链,不是只测拒绝路由那一条。M3 并行网关落地后代理判据失效——届时若出现「不经 Return 的重提」,这条测试会红,不会静默退化。
> - 疑点已核:拒绝路由与主动退回**都**在同一事务里、在 `EnterNodeOp` 跑之前就把 `Reject`/`Return` 行插进了 `wf_his_task`(`CompleteTaskOp.cs:62-73` 在 `RejectInstanceAsync` 之前插;`ReturnTaskOp.cs:63-74` 在 token 回退之前插),所以下界一定可见,不存在「跳转已发生但下界还没写」的窗口。
> - `Cancel`/`Urge` 不写 `wf_his_task`(Plan B3 ① 已定案),不影响本判据。
>
> `RejectRouted=13`/`TaskReturned=14` **仍然按裁决落地**(`WfEnums.cs` 尾部追加;`CompleteTaskOp.RejectInstanceAsync` 的 `ToNode` 分支与 `ReturnTaskOp` 各写一条,payload 带 `fromNodeId`/`targetNodeId`),但**不作为下界数据源**——它们的价值是审计可读性与 Task 12 流程图回放(回放要知道跳转发生在哪一步才能按最后一次访问收敛)。`TaskReturned` 的 payload 顺带成了「退回到了哪一步」这条断言的锚点,让此前无测试覆盖的目标解析有了可观测出口。

无论选哪个都要补的钉子测试(现在这条断言只存在于规避该路径的三节点模型里):**两节点链 + 退回 + 重提,断言 A 的 todo 重新出现在 node1**;以及拒绝路由版:`start→node1[A]→node2[B,onReject=toNode→node1]`,B 拒绝后断言 **A 的 todo 重新出现**(而不是 B 拿回自己的待办)。

**同轮一并修的 P2**:
- P2-1:补 4 条策略用例(`Prev` 有先例并断言 token 落到上一审批节点 / `Any` 合法目标成功 / `policyNotConfigured` / `Node` 缺 `ReturnToNodeId`),并在现有 `Node` happy path 上补 2 条断言(`wf_token.NodeId`、history payload 的 `targetNodeId`)。
- P2-2 + P2-3:`ReturnTaskOp.cs:122-127` 的查询同时加 `h.InstanceId == ctx.Instance.Id` 与 `h.NodeId != Task.NodeId`(两行),并补一条会签场景用例。
- P2-4:`WfDefinitionService.ValidateNode` 补两条引用完整性校验(`OnReject == ToNode` ⇒ `RejectToNodeId` 非空且在全树节点集合内;`ReturnPolicy == Node` ⇒ `ReturnToNodeId` 同理)。跨臂前向引用需两趟遍历或改用 `WfModelIndex`。**若判定超范围,必须立 Findings 条目并设为 Task 13 前置门。**
- P2-5:`EnterCcAsync` 改为按 `(InstanceId, NodeId, UserId)` 幂等(先查后插或 `Storageable`),或给 `WfCc` 加唯一索引 + 幂等写入。**若判定留给 Task 10,必须写进 Findings,否则 Task 10 会直接建在重复数据上。**

### 陷阱记录(plan 阶段读码时发现,提醒 exec 别踩)

- `_ = node;` 那行如果忘删,`node` 参数虽然现在真被用了但编译器不会报错(只是原来那行变冗余/矛盾),要显式确认删掉。
- `SnapshotLeaderChainsAsync` 改签名后,`BeginStartAsync` 里的调用点**必须同步改**,漏改会直接编译失败(强类型捕获,但别指望编译器帮你找到语义正确性,只能保证调用点存在)。
- Return 的"Prev"策略退化到 `start` 时,`ctx.Model.Root.Id` 取的是**当前 ctx 里已反序列化的模型**,不要重新查 DB 反序列化一遍(`BeginReturnAsync` 已经把 `model` 放进 ctx 了)。
- `WfInstanceActionInput` 改名成 `WfInstanceCancelInput` 时,`web`/`web-react` 的 `schema.d.ts` **不用现在同步改**——那两个前端模板要等 Task 12 统一跑 `gen:api` 才会重新生成,本任务只改后端 C# 类型名,不碰任何前端文件。
- Resubmit 的"无活跃任务"校验容易和 Cancel 的"无 Approve 历史"校验搞混——这两个校验目的不同(Resubmit 校验的是"当前有没有人在审",不是"有没有人已经审过"),不要抄错条件。
- `ReturnTaskOp` 的 CAS 认领顺序照抄 `TransferTaskOp`(先认领 `WfTask.Version`,再认领 `WfTaskActor.Status==Pending`),两次认领失败都是 `TaskConflict`,不要和新加的 `ReturnNotAllowed` 混着用——`ReturnNotAllowed` 专指"退回目标解析失败",`TaskConflict` 专指"并发/非本人办理"这类既有语义。

## Findings(review 阶段产出;修完划掉)

### Task 13 — Round 39 review(收口)

> **判定:0×P1 / 0×未修 P2,勾选 Task 13。** 代码是事实源。未开 Task 14。不改 `web-react/`,不跑 `gen:api`。

**闸门(亲手):** 指定过滤器 `FullyQualifiedName~Tests.Wf|FullyQualifiedName~Workflow` **189/189**(失败 0 / 跳过 0,约 1 m 2 s)。`cd web && npm run typecheck` exit 0;`npm run lint`(oxlint,无输出) exit 0;`npx vitest run src/workflow/` **2 files / 28 tests**。LSP:本机无 omnisharp / vue-language-server,类型闸门以 `vue-tsc --noEmit` + oxlint 代替。

**范围:** `web-react/` `git status` 空。`EnterNodeOp`/`CompleteTaskOp`/`Engine` 不在本任务 diff。`PageMonitorAsync` **无** `ClearFilter<IOrgScoped>()`(`WfInstanceService.cs:254-262`)。`GET {id}` 仍类级 `[ActiveSession]`,无 `[RolePermission]`。菜单 `RootId+26` / 按钮 `+13`,与 +1..+12、+20..+25 无撞号。`FakeInstanceService.PageMonitorAsync` 已补;`WorkflowSetup` 仍 `TryAddScoped<IWfInstanceService>`。详情 `Model` 来自 `instance.DefinitionVersionId`;Vue `detail.vue` 只读树吃 `visitedNodeIds` / `currentNodeIds`;设计器不传 `readonly`。

**亲手变异(全复原,产品+测试源 0×`MUTATION`/`REVIEW-PROBE`):**
1. `CollectVisitedNodeIds` 丢掉 cutoff、从 0 收全部 `NodeEnter` → `Last_visit_after_return_and_resubmit…` 与 `Last_visit_after_reject_to_node…` **双红** `DoesNotContain() Failure` Found `node2`,Actual `["start","node1","node2"]`。复原后 2/2 绿。
2. 监控三筛各丢一条 `WhereIF` → `Monitor_page_filters_starter_actor_and_cc_independently` 分别红在 :146 / :151 / :156(`Expected: [单 id] / Actual: [三单]`)。
3. 去掉 `EnsureParticipantAsync` 里 `CanMonitorInstancesAsync` 整段豁免 → 同用例 :159 `Expected: 0 / Actual: 48015`(超管打开非参与详情)。
4. 去掉末尾 48015 throw(always allow) → 同用例 :163 `Expected: 48015 / Actual: 0`;`Monitor_page_rejects_user_without_permission` 仍绿(钉的是列表 `[RolePermission]` 403,不是详情 48015)。

**[P3] 拒绝路由后 start 不在 last-visit 窗口。** `Last_visit_after_reject_to_node…` 只断言 `node1` 在、`node2` 不在,不断言 `start`。cutoff=`RejectRouted` 之后只有重进 `node1` 的 `NodeEnter`,`start` 在前缀里——符合 D1。挂着以免被当成回放 bug。

**[P3] 退回后、重提前的空窗口零用例。** `ReturnTaskOp` 关任务不 `EnterNode`;cutoff=`TaskReturned` 后窗口可空。现有钉子只覆盖重提之后。

**[P3] `CanMonitorInstancesAsync` 的权限码半支没有独立钉子。** 只删 `return codes.Contains(MonitorPermission)`(保留超管)→ `WfReplayMonitorTests` **4/4 仍绿**。D9 钉子写的是超管;D5 的「持有监控码的非超管」产品代码有,HTTP 未单独造角色授权。整段 bypass 已钉住。若要补:造非超管+授监控码打开非参与详情,再丢 `codes.Contains` 必须红 48015。

**[P3] 办理人筛的 Pending 半支未独立钉。** 现用例先 `approve` 再按 `ActorUserId` 查,走的是 `wf_his_task`。整条 actor `WhereIF` 已钉红;只删 pending-actor 查询会仍绿。

**[P3] `@ts-expect-error` 监控(及抄送)路径等 Task 14 `gen:api`。** 无浏览器 e2e,挂 Task 14。
### Task 12 — Round 37 review(收口)

> **判定:0×P1 / 0×未修 P2,勾选 Task 12。** 代码是事实源。未开 Task 13。

**[P3] 状态/动作数字表在 `mine`/`done`/`detail` 各拷一份。** D6 允许;不抽 composable。

**[P3] 两页无搜索栏。** D7 允许;`wfInstanceApi.page` 仍收 Status/DefinitionId/BusinessKey,日后加 search 不用改契约。

**[P3] Done 的 action map 未映射 Urge=7(以及 M3 的 8–10)。** 与 `detail.vue` 同形。催办只插 `wf_history`(TaskUrged),`PageDoneAsync` 读 `wf_his_task`,`CancelInstanceOp` 也不写 his_task——已办行里不会出现 7。映射 `5→withdraw` 对齐现成 i18n,实例撤销状态走 mine 的 status=4。

**[P3] 本轮无浏览器 e2e。** 挂 Task 14。LSP 本机无 vue/ts server,类型闸门以 `vue-tsc --noEmit` 代替。

### Task 11 — Round 35 exec(修 Findings,收口)

> **判定:P2-1 闭合,勾选 Task 11。** 产品代码只临时变异后复原。指定过滤器 **185/185**。未开 Task 12。

~~**[P2-1] `GetAsync` 标已读的 `UserId` 过滤没有钉子。**~~ ✅ **本轮修完**(纯补测试,产品代码零行为改动)。新增 `Starter_opening_detail_does_not_mark_others_cc`:发起人 `GET /api/v1/workflow/instance/{id}` 后,抄送人 `cc/page` 仍 `isRead=false`。**亲手复跑变异**:删 `c.UserId == userId`(保留 InstanceId && !IsRead)→ 该用例 **红** `Assert.False() Failure Expected: False / Actual: True`;复原 `.Where(c => c.InstanceId == instanceId && c.UserId == userId && !c.IsRead)` 后再跑 **绿**。`Opening_instance_detail_marks_cc_read` 仍只钉「自己打开会标自己」;`UserId` 过滤的区分力落在新钉子上。

### Task 11 — Round 34 review

> **判定:1×P2,不勾选。** `EnterCcAsync` 未动。第九面 + 4 条 HTTP 亲手 5/5。`POST /cc/read` 主人守卫有区分力。Round 35 已补钉子并勾选。

~~**[P2-1] `GetAsync` 标已读的 `UserId` 过滤没有钉子。**~~ ✅ 见上节 Round 35。`MarkMyCcReadAsync` 现写 `InstanceId && UserId && !IsRead`。`Opening_instance_detail_marks_cc_read` 是抄送人自己打开详情——去掉 `UserId==` 后**仍绿**(本实例只有他一条未读,「标全部」与「标自己」同结果)。**亲手复跑**(Round 34):删 `c.UserId == userId` → 该用例 1/1 通过。后果:发起人/审批人打开详情会把别人的抄送标成已读。

**[P3] `POST /cc/read` 菜单未挂按钮。** Controller 是 `[ActiveSession]`,与待办同形,不挡授权;只是权限树里只有 page。

**[P3] `DateTime.Now` 未走 `TimeProvider`。** 已读测试不拨钟,不阻塞。

**[P3] `OnlyUnread` / `DefinitionId` 零用例。** 列表隔离主钉已在。

**[P3] 48027 语言包挂 Task 14。**

### Task 11 — Round 33 exec 留痕(Round 34 已复核)

> **本轮落地抄送列表,未改 `EnterCcAsync`。** Task 11 未勾选。指定过滤器 **184/184**。

**[变异] PageMine 去掉 `UserId==`** → `Page_mine_returns_only_current_users_cc` 红,`Assert.Empty` 失败(B 看见 A 的抄送行)。**GetAsync 去掉 `MarkMyCcReadAsync`** → `Opening_instance_detail_marks_cc_read` 红,`Expected: True / Actual: False`。均已复原。

### Task 10 — Round 33 独立复核(收口)

> **判定:APPROVE,勾选 Task 10。** 指定过滤器亲手 179/179。产品 `backend/src/` 相对 HEAD 零 diff。0×未修 P1/P2。

~~**[复核] 四条新用例都有区分力。**~~ ✅ 亲手复跑:PageMine 去 `StarterUserId` → `[bId, aId]`;Sequential 第一拍清 DueTime → `Assert.NotNull` 红。exec 自报的 4/4 与读码一致;`PageMine` 无默认 Status 过滤,已完结单在去掉 userId 后会漏过来,不是假绿。

**[P3·已记录] 「对每个 Pending 都转」朴素 foreach 同一目标会 alreadyActor 整事务回滚。** 现状快照仍钉得住(A 待办还在 / DueTime 仍在),但钉的不是「B 也被转走」。产品定案仍挂 M3。

**[P3·已记录] Sequential 一拍只有一位 Pending。** 「一拍批完全部」要靠同一次 `TimeoutFireCmd` 内循环才能发生,第一拍 `Running`+1 Approve 钉的是这条,不是 `Take(1)` vs 全员。

### Task 10 — Round 32 exec 留痕(Round 33 已复核收口)

> **本轮是缺口补测,产品代码零改动。** Task 10 未勾选。

**[已复核] `WorkflowReplaceabilityTests` 八个 `TryAddScoped` 面与 `WorkflowSetup` 一一对应,不缺面、不预添第九面。** 八面:`IApproverResolver` / `IWorkflowFormBinder` / `IWorkflowEngine` / `IWfConditionEvaluator` / `IWfDefinitionService` / `IWfTaskService` / `IWfInstanceService` / `IWorkflowNotifier`。`TryAddEnumerable` 的 `IApproverProvider` 族 / `IAdminJob` / 种子、以及 `TryAddSingleton` 的 Options/`TimeProvider` 不在八件套射程内(本任务不扩 `IWf*Service`)。

**[已补] Findings 挂给本任务的两条语义空白 + 一处 XML 陈旧引用。** Sequential 逐拍级联有钉子;`All`+超时转办只写**现状快照**(XML/方法名写明不是定案);`WfDelegateTests` 两处 `<c>TransferTaskOp</c>` 已改 `ReassignTaskOpBase`。

**[事实·给 review] `PageMineAsync` 今天先 `ClearFilter<IOrgScoped>()` 再滤 `StarterUserId`。** 列表用例仍按陷阱记录把所有用户造在 `orgId=1`:若有人拿掉 `ClearFilter`,不同机构会让 page 空在范围过滤上、变异钉不住。这不是本轮产品缺口。

**[变异实跑·4/4 转红后复原]** 探针用窄过滤器;最终验收是指定过滤器 **179/179**(基线 175 + 4)。产品代码已复原,`rg MUTATION` 零命中。

| # | 变异 | 转红的用例与实际断言 |
|---|---|---|
| 1 | `PageDoneAsync` 去掉 `UserId ==` | `Page_done_returns_only_current_users_his_tasks` — `Assert.Empty() Failure`(B 看见 A 的已办行) |
| 2 | `PageMineAsync` 去掉 `StarterUserId ==` | `Page_mine_returns_only_current_users_instances` — `Expected: [bId] / Actual: [bId, aId]` |
| 3 | AutoPass 后 `ClearDueTimeAsync`(拆掉逐拍级联) | `Timeout_sequential_auto_pass_cascades_one_actor_per_scan` — `Assert.NotNull() Failure`(第一拍 DueTime 被清) |
| 4a | 会签 Transfer 对每个 Pending 都入队 | 现状快照 — `Assert.Empty() Failure`(A 的待办还在:第二跳 alreadyActor 整事务回滚) |
| 4b | 去掉 `ClearDueTimeAsync` | 同一用例 — `Expected: Null / Actual: String`(HTTP `dueTime` 仍在,事务外已提交态) |

### Task 9 — Round 31 独立复核(收口)

> **判定:APPROVE,勾选 Task 9。** 指定过滤器 59/59。0×未修 P1/P2。P3 均已在注释落地。
>
> **台账 vs 代码**:Status 仍写「阶段 plan / 下一步 Round 30 plan」,Findings 已有 Round 30 exec 留痕(自称 172/172、尚未 review)。工作树未提交:实体 Version 列、6 个落点 Claim、`WfVersionCasTests` 10 条。以代码为准。

~~**[P2-1] 会签/顺序签非末位投票不领取 token,撤销与首票同意可双赢。**~~ ✅ **代码里已修**(本轮核实,不是本轮新写):`CompleteTaskOp.ExecuteAsync` 在 `!passed` 提前返回前 `ClaimTokenAsync(Active)`。钉子 `Cosign_first_approve_claims_token_and_locks_out_cancel`:首票后 token 2→3;撤销半段是冒烟不是钉子(去掉领取仍会被 `alreadyApproved` 读挡住)。

~~**[P2-2] CAS `claimed != 1` 失败路径可测,却被变异探针留在生产代码里。**~~ ✅ **本轮修完**。测试已用 `IWorkflowFormBinder.ValidateOnStartAsync` / `IApproverResolver` 在同一 `SqlSugarScope` 事务内把版本推走。但 `ClaimTokenAsync` 留下 `// MUTATION-M2` + `_ = claimed`(throw 被删)。**带着变异实跑**:`Resubmit_losing_token_cas_returns_48004` 红,`Expected: 48004 / Actual: 0`;`Instance_losing_cas` 仍绿(实例级 throw 没被挖)。复原 `if (claimed != 1) throw ... reason=tokenVersionConflict` 后 3/3 绿。`rg MUTATION` 零命中。

~~**[P3-1] `DefaultValue = "0"` 注释。**~~ ✅ 已写清:SqlSugar `AddColumn` 三步(先可空 ADD → 回填 → 改 NOT NULL);SQLite 因未开 `SqliteCodeFirstEnableDefaultValue`,DDL 无 DEFAULT,回填 UPDATE 仍执行。

~~**[P3-6] 改派与实例终态的隐式不变量。**~~ ✅ `ReassignTaskOpBase` 段注释已写:终态动作物理删活跃 `wf_task`,改派 CAS 打在已删行上 → 0 行 → `TaskConflict`。M3 若出现不删待办的实例级动作,这条失效。

~~**[P3-7] 多一条 UPDATE 的理由。**~~ ✅ `ClaimInstanceAsync` XML 已改成:ctx **没有** `ICurrentUser`,领取语句手填不了 `UpdateUserId`;不是「更高效的形状」。

**[未跑] 更宽的 `FullyQualifiedName~Wf` 本轮被自动审批拦住,未跑。** 指定六类过滤器 59/59 已过。下一轮 Task 10 exec 开工前应先跑 `~Tests.Wf|~Workflow` 把基线钉在 175(165+10)上。

### Task 9 — plan + exec 留痕(Round 30;Round 31 已复核收口)

> **验收:172/172 绿**(基线 165 + 7 条新用例;plan 预期 172,逐数吻合)。`dotnet build -c Release -t:Rebuild` 0 错误、**`TenonAdmin.Workflow` 包 0 警告**(残留警告全在 `TenonAdmin.Core`/`TenonAdmin.Services`,与本轮改动无交集)。`git diff --check` 干净,`rg` 扫 `MUTATION`/`REVIEW-PROBE`/`TODO`/`FIXME`/`.Skip(`/`NotImplementedException` **零命中**。**12 个变异点全部亲手实跑转红后复原**,逐个附实际失败断言与期望/实际值(见下面「变异实跑证据」)。`## Status`/`## Log`/`## Tasks`/`## 语义契约` 一律未碰。
>
> **陈旧编译产物的处置(Round 28/29 教训)**:最终那一跑没有用「刷时间戳」这个办法,而是 `dotnet build -t:Rebuild`(无条件全量重编)后 `dotnet test --no-build`。理由:`-t:Rebuild` 不看时间戳、不做增量判定,比「改一下 mtime 再让 MSBuild 自己判断」更硬,不存在「时间戳改了但 MSBuild 仍判最新」的残余可能。

**[裁定·前置约束 4] `ReassignTaskOpBase.ExecuteAsync`(121 行)本轮不拆步骤。** Round 26 把这个决定移交给「正要动其中 CAS 段的那一轮」,而**本轮读码后的事实是:它一个字都不动**——改派(转办/委托)压根不改实例状态、不改 token,故整条路径**不进**本轮的收口清单(见前置约束 1 的处置)。移交的前提不成立,决定应当继续往后传。三条理由:①拆步骤会把这个文件从「121 行经 Round 26 `Compare-Object` 逐字核验过、可证明未被重写」变成一次不可证伪的重写,而本轮对它的义务恰恰是**证明没动过**(已用 `git diff --numstat` = **9 插入 / 0 删除、且全部新增行都是 `//` 注释**独立证明);②步骤边界是**新的覆写契约**、一经发布不好改,而本轮对这些边界**零需求**——没有任何新行为需要在 `ValidateTargetAsync`/`ClaimAsync` 之间插东西;③`ExecuteAsync` 仍是 `public virtual`,消费者整体覆写能力一点没少,拆步只是把「能不能只改一步」从 0 提到 1,而今天没有已知消费诉求指向那一步。**下一个真正会动它的轮次**是 M3 的加签/减签/拿回(要在「认领」与「挂新 actor」之间插入多 actor 编排),那时边界需求是具体的、画出来的缝才准。Round 26 留的建议切法(`ValidateTargetAsync` / `ClaimAsync` / `WriteHistoryAsync` / `AttachNewActorAsync` / `QueueNotificationAsync`)**原样保留、仍非定案**。

**[偏差·须协调者回写数据库设计文档] 落地形状是「先领取、再写状态」两条语句,而评审 §4.1 的原文把状态与版本写在同一条 `WHERE`/`SET` 里。** 语义等价、条件逐字相同(`WHERE Id = @id AND Status = @expectedStatus AND Version = @oldVersion` → `Version = Version + 1`),差别只在「状态列由紧随其后的整对象更新写」。**理由是本仓的一条硬接线事实**:`SqlSugarSetup.cs:169-176` 的审计 AOP 只在 `DataFilterType.UpdateByObject` 分支填 `UpdateTime`/`UpdateUserId`,而 CAS 必须走 `SetColumns` 条件更新路径——把状态也挤进领取语句,六个落点就得各自手填审计字段,而「这次是谁做的」在六处各不相同(`CancelInstanceOp` 此前硬编码 `StarterUserId` 就是这个税的现场)。正确性不打折:领取成功即持有该行排他锁直到提交,后一条语句处在同一事务的锁保护区内,中间插不进别的事务。**副产品(算行为改动,请 review 留意)**:`CancelInstanceOp` 的 `UpdateUserId` 从硬编码 `StarterUserId` 回到由 AOP 填当前登录用户——撤销的授权规则保证 caller == starter,故实际取值不变,但写法与另两个终态出口归一了。若协调者认为 §4.1 的措辞该与实现对齐,回写建议:把「状态推进统一采用期望状态和版本双重条件」后面补一句「领取语句可与业务状态写分离为同事务两条语句,以保留 ORM 的审计字段自动填充」。

**[偏差·四库验证缺口,按任务范围留给 M2c] `DefaultValue = "0"` 是全仓首次使用的 SqlSugar 特性,而 `ALTER TABLE ADD COLUMN` 这条路径本轮验证不到。** 两个新列取「非空 `int` + DB 级默认值 0」而**不是**照抄 `WfTask.Version` 的裸 `int`:`wf_task` 是 M1 建表时就带这一列(走 `CREATE TABLE`),而这两列对存量库是 `ALTER TABLE ADD COLUMN`,**SQLite / PostgreSQL / SQL Server 在表里已有行时会拒绝没有默认值的 NOT NULL 新列**(只有 MySQL 会隐式补 0)。这条正是评审 §九 第 2/3 条(「新增列先 nullable 或带跨数据库一致的默认值」「`Version` 从 0 开始,旧行可直接回填」)要求的形态。**本轮验证到什么程度要说清**:测试库都是新建的,所以只证明了 **SQLite 的 `CREATE TABLE` 带 `DEFAULT 0` 能建出来、新行读到 0**(第 1 步单独跑了一次全量当探针,165/165);四库的 **ADD COLUMN 升级路径零验证**,按任务范围(「四库契约测试留给 M2c」)挂 M2c。**给 M2c 的具体动作**:拿一个 M2a 时期的库(或先建表再加列)在四库各跑一次 CodeFirst,断言旧行 `Version` 读到 0 而不是报错或 null。

**[已确认覆盖·回答 Round 28 review 的 M3 顾虑] 新 CAS 确实覆盖「同实例两件待办各自 CAS 通过、却各自推进出互相冲突的实例状态」。** 证据链:实例状态只有三个写入出口(`CancelInstanceOp` / `CompleteTaskOp.RejectInstanceAsync` 的终止分支 / `TakeTransitionOp.CompleteInstanceAsync`),本轮三处都加了 `ClaimInstanceAsync(Running)`;并行网关下两个事务都会走到其中之一,`WHERE Status = Running AND Version = @old` 只有一个能拿到 1 行,输的抛 48004 → 引擎「一条 Cmd 一个事务」整体回滚,不会留下半推进的实例。**token 侧同理**且更强:连「进节点」都要领取(`EnterNodeOp`),所以「审批 vs 撤销」这类**不写实例状态**的竞争也被覆盖——一次会推进 token 的同意与一次并发撤销都要 CAS 同一 token 行。**没被覆盖的、必须说清的一类**:不改实例也不改 token 的**任务级动词**(转办/委托/催办/提醒),它们仍然只靠任务级 CAS——这不是缺口而是定案(见前置约束 1 的处置),给改派加实例级 CAS 属过度加锁。

**[射程声明·不虚报] CAS 的失败路径在本仓单线程 xUnit 套件里构造不出来,7 条新用例钉的一律是「机制」。** 真实竞态需要「A 读版本 → B 提交推走版本 → A 写」这个交错,而所有 `BeginXxxAsync` 都在**自己的事务里现读**版本号,单线程顺序执行下读到的必然是最新值、CAS 永远对得上——与 Round 28 证伪 `Timeout_remind_does_not_block_human_action` 的根因**逐字同型**。故断言落在「这个落点确实做了双条件领取并推进了版本」。**这不是套套逻辑**:把任何一处 CAS 退回成无条件整对象更新,版本就不再前进,对应用例立刻红(12 个变异全部实跑转红,证据在下面)。**「并发下不产生半推进状态」这个用户可见后果不在射程内**,按台账既有先例(Task 3 Round 13 / Task 4 Round 17 / Task 7 Round 26)以「读码逐处核对 + 全量回归零破坏」替代。**刻意没做的一件事**:不给命令加 `ExpectedVersion` 入参来把失败路径变可测——那是 M2c 的 `RequestId`/operation receipt 那一档(评审 §十 M2c 第 2-3 条),只为测试需求给公开命令加字段是把测试焊进产品 API。

**[记账·不顺手收口] `CancelInstanceOp` 找活跃任务仍只按 `TokenId`(`CancelInstanceOp.cs` 那句 `Where(t => t.TokenId == ctx.Token.Id).FirstAsync()`)。** 本轮**有意不改**,理由两条:①今天单 token,`TokenId == ctx.Token.Id` 与 `InstanceId == ctx.Instance.Id` 选出的是同一批行 → 改成 `InstanceId` 在今天**行为恒等且不可证伪**(既写不出红测,也无法证明改对了);②更要紧的是它会**预先承诺一个属于 M3 的语义**(「撤销杀掉所有分支」),而真正的 M3 撤销还得同时收掉**其它 token 行本身**、而不只是它们的待办——只改一半看起来像做完了,比没做更危险(下一个人会以为这里已经 M3-ready)。**继续记账,挂 M3 并行网关。**

**[事实·性能代价,请 review 认账] 「进节点也要领取 token」让每次进节点多一条 UPDATE 往返。** 一次发起 = 2 条(`start` + 首个审批节点),一次推进节点的同意 = 1-2 条。这是覆盖评审 §4.1 第 1 条「审批与撤销」的**唯一**手段(那条路上实例状态在两边看都还是 Running,只有 token 动),换的是不再靠数据库隔离级别碰运气。**判定不阻塞**:引擎每次进节点本来就有 token UPDATE + 历史 INSERT + 建任务 INSERT 若干,多一条条件更新在同量级内;真要省,可把领取与 `NodeId` 写**合并**成一条 `SetColumns`,代价就是上面那条偏差里说的手填审计字段。

**[事实·给 Task 14,补键清单不变] 本轮零新增错误码。** 实例/token 级 CAS 失败一律复用 `InstanceStatusConflict`(48004)+ `args["reason"]`(`instanceVersionConflict` / `tokenVersionConflict`)。对称论证:任务级 CAS 输了统一是 `TaskConflict`(48007),那么实例/token 级 CAS 输了就统一是 48004;一码多 `reason` 是本仓既有惯例(`TransferTargetInvalid`/`NobodyBlocked`/`CancelNotAllowed`/`ReturnNotAllowed` 都这么干)。**所以 P3-#9 记的语言包补键清单仍是 5 码 × 2 语言 = 10 条,没被本轮加长**(48004 早有键)。新增的两个 `reason` 值若前端要区分展示,归 Task 14 判断,不新增 `error.code.*` 键。

**[变异实跑证据·12/12 全部转红后复原]** 探针跑用窄过滤器(`~WfVersionCasTests` 等)提速,**基线与最终验收跑用的是任务书指定的过滤器**(165/165 → 172/172)。

| # | 变异 | 转红的用例与实际断言 |
|---|---|---|
| 1 | `EnterNodeOp` 去掉 `ClaimTokenAsync` | `Start_advances_token_version_once_per_node_entry:66` — `Expected: 2 / Actual: 0`(另有 4 条连带红,因为进节点在每条路径上) |
| 2 | `TakeTransitionOp` 去掉 `ClaimInstanceAsync` | `Approve_to_completion_claims_instance_and_token:96` — `Expected: 1 / Actual: 0` |
| 3 | `TakeTransitionOp` 去掉 `ClaimTokenAsync` | 同一用例 **`:99`**(与变异 2 红在**不同行**,证明两条断言各自独立被钉)— `Expected: 3 / Actual: 2` |
| 4 | `CompleteTaskOp.RejectInstanceAsync` 去掉 `ClaimInstanceAsync` | `Reject_terminate_claims_instance_and_token:127` — `Expected: 1 / Actual: 0` |
| 5 | 同上去掉 `ClaimTokenAsync` | 同一用例 **`:129`**(不同行)— `Expected: 3 / Actual: 2` |
| 6 | `CancelInstanceOp` 精确退回「只锚状态、不锚版本」的 Task 4 原状 | `Cancel_claims_instance_and_token:153` — `Expected: 1 / Actual: 0`。**同跑的 4 条 `WfCancelTests` 全绿**,坐实这次变异只拆掉了版本这一维、状态那一维仍在(陷阱记录第 8 条要防的正是「红了但不是因为版本」) |
| 7 | `CancelInstanceOp` 去掉 `ClaimTokenAsync` | 同一用例 `:155` — `Expected: 3 / Actual: 2` |
| 8 | `ReturnTaskOp` 去掉 `ClaimTokenAsync` | `Return_then_resubmit_claims_token_at_every_hop:190` — `Expected: 3 / Actual: 2` |
| 9 | **`BeginResubmitAsync` 去掉 `ClaimTokenAsync`(还原「重提全程无 CAS」原状,前置约束 2)** | 同一用例 **`:198`**(与变异 8 不同行)— `Expected: 6 / Actual: 5`。**这是「重提到底有没有锚点」的唯一可观测出口** |
| 10 | **`ReassignTaskOpBase` 删掉任务级 CAS 段(把它当冗余放松掉,前置约束 1 担心的事)** | `Reassign_claims_task_version_only_and_leaves_instance_and_token_untouched:245` — `Expected: 1 / Actual: 0`(任务版本不前进) |
| 11 | **反方向:给 `ReassignTaskOpBase` 加 `ClaimInstanceAsync`(过度加锁)** | 同一用例 **`:246`**(不同行)— `Expected: 0 / Actual: 1`(实例版本被改派推走了) |
| 12 | **给 `HandleRemindAsync` 加一道 token 级 CAS(前置约束 3 明说不能加)** | `WfTimeoutTests.Timeout_remind_does_not_block_human_action:271` — `Expected: 2 / Actual: 3` |

**[跑不出红的·如实标注] 只有一条:测试 `New_instance_starts_at_version_zero` 没有配套变异。** 它是 `DefaultValue = "0"` 的**正向确认**(列建出来了、默认真是 0),不是钉子——「列没建出来」的反向保障是查询直接抛异常而不是断言失败,构造不出「有 bug 时绿、没 bug 时也绿」之外的第三态。其余 7 条新断言组各自都有实跑转红的变异。

### Task 8 — Round 28 exec(修 review 的 1×P1 + 4×P2 + 3×P3;归属只记 exec 自己做过的事)

> **验收:165/165 绿**(基线 160 + 5 条新用例:`Timeout_throttled_reminds_do_not_starve_a_newly_due_task` / `Timeout_task_whose_node_dropped_timeout_leaves_the_scan_window` / `Timeout_permanently_failing_task_is_retired_after_repeated_failures` / `Timeout_remind_fires_again_after_node_is_re_entered_by_reject_routing` / `Timeout_scan_job_seed_row_is_ready_and_resolvable_by_the_scheduler`;另有 2 条既有用例被加强:测试 7 加版本不变量、测试 14 加失败明细日志与事务外痕迹断言)。`dotnet build -c Release` 0 错误、工作流包 0 警告,`git diff --check` 干净,`rg` 扫 `MUTATION`/`REVIEW-PROBE`/`.Skip(`/`NotImplementedException` 零命中。**本节每一条「已转红」都是本轮亲手实跑的**,附实际失败断言;跑不出红的逐条如实标注。`## Status`/`## Log`/`## Tasks`/`## 语义契约` 一律未碰。

~~**[P1-1] 扫描批量被「永不消费的行」永久占满,新到期待办饿死。**~~ ✅ **修完**。修法分两半,理由是这四类死行的性质不同,一刀切不成立:
> **① 批量从「取回行数」改成「处理预算」+ `(DueTime, Id)` 游标翻页。** 推不动的行(被防刷挡下的提醒)只被**检视**、不扣预算,扫描继续往后翻,直到凑满预算或撞到翻页天花板(`WfTimeoutJob.MaxScanRounds`,默认 5 → 一拍最多检视 `BatchSize × 5` 行)。游标是安全的:真被消费的行会离开窗口(待办物理删 / `DueTime` 清空),keyset 游标不会漏行。
> **为什么不取 review 建议的「把 `ShouldRemindAsync` 判据下推进扫描查询」**(判断过程记下来,免得下轮重走):那条判据的间隔是**每节点各不相同**的(用户已裁决「默认 = 节点自己的 `Timeout.Hours`,下限 1 小时」),而扫描时还没解析节点配置 → SQL 里拿不到间隔。能下推的只有**固定下限**(1 小时)那一版,它 (a) 对 `hours = 24` 的节点只挡住 1/24 的时间、23 小时里照样堵队头,(b) 会连带把顺序会签的「逐拍级联」也降成每小时一位(那条 `TimeoutFired` 的键是 `(InstanceId, NodeId)`,分不出是提醒还是级联),属于没被要求的行为改动。游标翻页对两者都免疫。
> **② 2/3/4 三类死行各有出口**(`RetireTaskAsync`:先落一条 `TimeoutFired`(`action = "retired"` + 原因)、再带 `Version` 条件清 `DueTime`、最后打一行带 `taskId` 的日志)。`instanceMissing` / `instanceNotRunning`(第 4 类,此前一路走到 `BeginTimeoutAsync` 抛 `InstanceStatusConflict` 被吞成一次失败)在 Job 侧提前判死;`timeoutNotConfigured`(第 2 类)清 `DueTime` 是**恢复一致**——节点现在明说不设超时,这行的 `DueTime` 就是过期数据。第 3 类(运行期永久失败)与 P2-4 合并处理,见下条。**陷阱记录第 3 条的担心已避开**:清之前有事件行、清之后有带 `taskId` 的日志,两个出口都在,不是静默吞配置错误。
> **钉子测试 `Timeout_throttled_reminds_do_not_starve_a_newly_due_task`**(BatchSize=2,两条 `hours=24` 的提醒 + 一条自动通过;第一拍提醒占满预算是**正确**行为,随后把提醒事件推回 2 小时前——仍在 24 小时节奏内、却超出任何固定下限,于是**只有游标翻页救得了这一场**)。**实跑变异 ①**:`MaxScanRounds => 1`(等价于还原单页 `Take(BatchSize)`)→ 红,`Expected: ["…命中 3,提醒 0,自动通过 1,…,跳过 2,失败 0。"] / Actual: ["…命中 2,提醒 0,自动通过 0,…,跳过 2,失败 0。"]`。**实跑变异 ②**:让被挡下的提醒也 `acted++`(即预算按取回行数计)→ 红,**同一条断言、同样的期望/实际值**。**实跑变异 ③**(死行出口):去掉判死分支里的 `RetireTaskAsync` → `Timeout_task_whose_node_dropped_timeout_leaves_the_scan_window` 红,`Assert.Null() Failure: Expected: null / Actual: 2026-08-24T17:20:35.2508313`。
> **残余(如实记账,不虚报为「彻底解决」)**:翻页天花板是 `BatchSize × 5`。若同一拍里排在队头的、这一拍推不动的提醒行超过这个数(默认 1000 行),后面的活行仍会等下一拍。这不是永久堵塞(死行已被清走、提醒行会随间隔到期变成活行),但也不是「零饿死」。要彻底消掉得让每行都能算出自己的下次可提醒时刻并下推进 SQL,那需要一个新列或改掉「间隔跟随节点 `Hours`」这条**用户已裁决**的语义,本轮不做。

~~**[P2-1] 测试 7 是套套逻辑,台账有一处假记账。**~~ ✅ **修完**。取「第三条路」(任务书给的①不可行:`CompleteTaskCmd` 没有 `ExpectedVersion` 入参)——断言落在机制本身「提醒不得改动 `wf_task.Version`」。假记账的原文与更正后的措辞见 `## Plan` 里那条 `⚠ 假记账更正` 引用块(原句「已用测试第 7 条钉住」已被整段替换)。**实跑变异**:给 `HandleRemindAsync` 加标准版本 CAS → `Timeout_remind_does_not_block_human_action` 红,`Expected: 0 / Actual: 1`;复原后绿。**射程如实标注**:钉的是机制,不是「人工动作不被挡」这个用户可见后果。

~~**[P2-2] 「种子真的会被调度器跑起来」零覆盖。**~~ ✅ **修完**:新增 `Timeout_scan_job_seed_row_is_ready_and_resolvable_by_the_scheduler`——从 DI 取 `IJobHandlerResolver`,喂 `sys_job` 那行的 `HandlerName`,断言解析出 `WfTimeoutJob`;另断言 `Status == Ready` 与 `JobTrigger.ComputeNext(row, now)` 非空。不启调度器、不拨钟。**实跑变异**:(a) `HandlerName` 改成 `"TenonAdmin.Workflow.Jobs.WfTimeoutJob"`(模拟重构挪了命名空间没同步种子)→ 红,`Assert.IsType() Failure: Value is null / Expected: typeof(TenonAdmin.Workflow.WfTimeoutJob) / Actual: null`;(b) `Status = Paused` → 红,`Expected: Ready / Actual: Paused`;(c) cron 改成 `"every 5 minutes"` → 红,`Assert.NotNull() Failure`。
> **一条变异钉不住,如实记**:cron 改成 **5 段** `"*/5 * * * *"` 时本用例**仍然绿**——`CronExpression.TryParse` 自己认 5 段,所以第三条断言的射程只是「cron 能被调度器算出下一次时刻」,**不是**「是归一化 6 段」。段数归一化归 `JobService` 的入库校验管,不在本用例射程内;已把这个射程写进用例的 XML 注释,免得下一轮误以为它管这个。

~~**[P2-3] 「覆写单步即可」的承诺被接线悄悄作废。**~~ ✅ **修完(纯文档,无红测)**:`WfTimeoutJob` 类级 XML 新增一段写明——`TryAddEnumerable` 按实现类型去重故子类是**新增**而非替换,`DefaultJobHandlerResolver` 按 `IAdminJob.Name` 匹配而种子写死基类全名 → **调度器永远选中基类,覆写一次都不执行**;两条出路(改 `sys_job.HandlerName` 为子类全名,该行 `IsSystem = false` 后台可直接改 / 子类 `override Name => typeof(WfTimeoutJob).FullName!` 并前置注册)。`ShouldRemindAsync` 与 `WorkflowOptions.TimeoutRemindMinIntervalHours` 两处「覆写本方法即可」也各补了指回类级说明的一句。**如实标注:这条无变异证据**——改动全是 XML 注释,没有可转红的行为。

~~**[P2-4] 永久失败没有升级出口。**~~ ✅ **修完**(与 P1-1 第 3 类死行同一修法,照 review 的建议合并):①失败时 `context.Log` 多打一行带 `taskId`/`instanceId`/`nodeId`/错误码的明细(此前只有一个计数);②在**事务外**补一条 `TimeoutFired`(`action = "failed"` + `error`)——引擎「一条 Cmd 一个事务」会把同事务那条痕迹随失败一起回滚,不补的话永久失败在数据层面完全不可见;③同一件待办累计到 `MaxTaskFailures`(默认 5,`protected virtual`)就 `RetireTaskAsync` 移出扫描窗口。次数按「本 `(实例, 节点)` 上不早于本待办 `CreateTime` 的 `TimeoutFired` 行数」近似,近似的唯一失真是顺序会签的逐位级联(已写进 XML)。**失败隔离本身未被推翻**。**实跑变异**:(a) 去掉失败明细日志行 → `Timeout_scan_isolates_per_task_failure` 红,`Expected: 2 / Actual: 1`(日志行数);(b) 去掉事务外那条痕迹 → 同用例红,`Expected: 1 / Actual: 0`(`TimeoutFired` 行数);(c) `MaxTaskFailures => int.MaxValue` → 新增的 `Timeout_permanently_failing_task_is_retired_after_repeated_failures` 红,`Assert.Contains() Failure: Filter not matched in collection`(五拍之后仍没有「已退出扫描窗口」那行日志)。

~~**[P3-1] 提醒去重键不带 `TaskId`,向后跳转重入的节点会丢掉第一次提醒。**~~ ✅ **修完**:`ShouldRemindAsync` 判据加 `h.CreateTime >= task.CreateTime`。**实跑变异**:去掉这半句 → 新增的 `Timeout_remind_fires_again_after_node_is_re_entered_by_reject_routing` 红,`Expected: 2 / Actual: 1`(重入后的新待办到期时,被上一轮的事件行挡掉了提醒)。该用例把 `TimeoutRemindMinIntervalHours` 配成 48 小时(> 节点 `hours = 1`)——这是缺陷可观测的唯一条件。

~~**[P3-2] 台账关于「谁在守卫星包种子取号」的记述是错的。**~~ ✅ **改完**:三处(`## Plan` 读码清单、改动清单里的种子行、陷阱记录第 10 条)+ `WfTimeoutJobSeed` 的 XML 都已改成「守这条的是 `DatabaseInitializer` 的启动期检查」。**核实依据**:`SeedIdRangeTests` 四处都 `new AdminAppFactory()`(内核 TestHost),而 `AddTenonAdminWorkflow` 只在 `TenonAdmin.WorkflowTestHost` 里调用 → 工作流种子从未进过那几个用例的 `GetServices<ISeedData>()`。取号结论不变。

~~**[P3-3] 会签下超时转办只改派 `actors[0]` 却清掉整行 `DueTime`。**~~ ✅ **记完**:`PlanTimeoutOpsAsync` 的 XML 加了一段「⚠ 语义空白(已知,未定案)」,写清成因(转办是任务级一次换一个人、`DueTime` 是任务级一行一个,两者在会签上对不齐;而不清 `DueTime` 就无限重触发)与三种可能定案。**挂 Task 10 补用例与定案**,见下面那条。

**[缺口·给 Task 10(后端测试固化)] 会签 + 超时自动转办的语义要定案并补用例。** 现状:`WfSignMode.All` 下 `PlanTimeoutOpsAsync` 只对 `actors[0]` 排一个 `TransferTaskOp`,却把整行 `DueTime` 清掉 → 剩余 Pending 办理人从此不受任何超时约束。三个候选(改派全部 Pending / 等最后一位办完再清 / 会签节点发布期禁用 `Transfer`)都是产品判断。**零测试覆盖**——本轮只补了 XML 留痕,没写用例,因为写用例前得先知道断言什么。

**[事实·Round 28 实测,会咬人] 上一轮 review 的变异探针以「陈旧编译产物」的形式活了下来,把基线打成 159/160。** 本轮第一次跑全量时 `Timeout_remind_is_throttled_within_min_interval` 红在 `Expected: ["REVIEW-PROBE-SECOND-SCAN"] / Actual: ["超时扫描:命中 1,…"]`,而**源文件里根本没有这个字符串**——`WfTimeoutTests.cs` 的时间戳没有晚于上次编译,MSBuild 判定项目最新、直接用了带探针的旧 dll。`(Get-Item ...).LastWriteTime = Get-Date` 强制刷新时间戳后重编,160/160。**教训**:变异测试复原之后,光看 `git diff` 干净不够,复原那一跑必须确认**真的重编过**(改一下时间戳,或者看 dll 时间);否则「复原后 X/X 绿」这条证据本身就可能来自旧产物。

**[残余·给 review 与 Task 10] P1-1 的翻页天花板是 `BatchSize × 5`,不是「零饿死」。** 完整论述见上面 P1-1 条的「残余」段。判定不阻塞:死行已有永久出口,提醒行是会自愈的暂态,残余只在「单拍队头堆积 1000 行以上推不动的提醒」这个量级才出现。

### Task 8 — exec 阶段新发现(Round 27;plan 阶段那批见下一节,均已落地)

**[方法论·给 review 与后续所有轮] 「不产生新行 / 不多出行」这类断言,在「一条 Cmd 一个事务」的引擎下有一整类失效模式:失败会把证据一起回滚。** Task 8 的方案里测试第 13 条写的变异判据是「不清 `DueTime` → 第二次扫描重复触发并因 `alreadyActor` 报错 → 红」,推演的前半正确、后半**不成立**:48010 让整个 `TimeoutFireCmd` 事务回滚,连同本该留痕的 `TimeoutFired` 一起,所以「行数不变」在有无 bug 时都成立。**已实跑坐实**:施加变异(去掉 `ClearDueTimeAsync` 调用)并屏蔽 `DueTime` 断言后,该用例仍然全绿。修法是给扫描一个真的可观测出口——`RunTimeoutJob` 收集 `JobExecutionContext.Log`,断言第二拍的日志行是「无到期待办」;复验后变异下 `Actual: ["超时扫描:命中 1,…,失败 1。"]`。**推广**:凡是「动作失败即整事务回滚」的路径,断言要么落在**动作之前就已提交的东西**上,要么落在**事务外的计数/日志**上;落在「事务内本该新增的行」上的否定断言,天然对失败型 bug 免疫。Task 9(CAS 收口)与 Task 10(后端测试固化)会大量写这类断言,先看这条。**同类但无害的一例**:测试 15(领取 CAS)方案写的判据是「去掉 `Version == @expected` 半句 → 多出一行 → 红」,实测红在**前一条** `Assert.ThrowsAsync`(`No exception was thrown / Expected: typeof(AdminException)`),后面那三条行数断言压根没执行到。该用例仍然有区分力(异常断言就是那道闸门),只是「零额外行」那几条是防御纵深而非主钉——记下来免得 review 误以为它们各自被验证过。

**[陷阱·给 Task 9 与任何写条件更新的人;2026-08-26 独立验证轮实测] `SetColumns` 里写内联的 `DateTime` 计算表达式会被拼成本地化字面量进 SQL,当场炸库。** 做「委托不重置 `DueTime`」那条钉子的变异时,变异代码写成 `.SetColumns(t => new WfTask { DueTime = ctx.TimeProvider.GetLocalNow().DateTime.AddHours(h) })`,结果**不是**期望的断言红,而是 `SQLite Error 1: 'near "下午": syntax error'` 打成 500 —— SqlSugar 把那个表达式按当前区域设置(zh-CN,含「下午」)格式化成字符串直接拼进 SQL,而不是参数化。改成先算进局部变量、再 `.SetColumns(t => t.DueTime == mutDue)` 就正常参数化了。现有代码天然免疫(`ClearDueTimeAsync` 设的是 `null`,`ClaimDueTaskAsync` 设的是 `expectedVersion + 1` 这种整数),但 **Task 9 要给实例/Token 级 CAS 写一批条件更新**,一旦其中有「把某个时间列设成算出来的值」就会踩上;症状是 500 + 语法错误、不是静默错值,但看着完全像测试脚手架坏了而不像自己的 SQL 生成有问题,能白烧不少时间。

**[事实·会咬人] 超时转办不清 `DueTime` 的真实症状是「每拍静默失败一次」,不是「重复触发多插行」。** 陷阱记录第 2 条的现象描述该按这个订正:`TransferTaskOp` 不删 `wf_task`,下一拍再扫到 → 目标已是 actor → `alreadyActor` 抛 48010 → **整事务回滚,零新行**。所以生产上这个 bug 的唯一征兆是 `sys_job_log` 里 `MessageText` 的「失败 N」计数持续非零,数据层面完全看不出来。这也解释了为什么必须在 `BeginTimeoutAsync` 的同一事务里清 —— 放到 Job 里事后补的话,清理本身也会被前一条的回滚牵连。

**[缺口·给 Task 10(后端测试固化)] `Sequential` 顺序会签下超时自动通过的「逐拍级联」是有意行为,但无专门用例钉住。** 落地按方案实现(顺序会签只有一位 Pending,批掉他会晋级下一位,任务行仍在、`DueTime` 仍是过去 → 下一拍继续自动通过下一位,直到节点通过),理由与「这是可接受且可解释的行为,不是缺陷」都写进了 `PlanTimeoutOpsAsync` 的 XML 注释;但测试清单只覆盖了 `All` 与 `Any` 两态(测试 9/10),`Sequential` 这一态只被「恰好一个」这条通用分流覆盖、级联本身零断言。**不虚报**:这是真缺口,不是「无法构造」——两位顺序办理人 + 连扫两拍即可。挂 Task 10。

**[i18n 债·给协调者与 Task 14 知情,不新增补键工作量] 超时动作会把中文系统文案写进 `wf_his_task.Comment`。** 文案由 `WfTimeoutJob.ResolveComment` 产出(「超时自动通过(系统触发)」等三条),经 `TimeoutFireCmd.Comment` 落库。这不违反「错误只返数字码」——那条铁律管 `ErrorCode` 与前端 i18n,`Comment` 是业务数据列(用户填的审批意见就存这里),内核自己也往数据列写中文(`SysJobLog.MessageText`/`ErrorText`)。**完全可逆**:自由文本列,消费者覆写 `ResolveComment` 就能换成 key 或挪进 payload,无 schema/枚举变更。**Task 14 的补键清单不变**(仍是 5 码 × 2 语言 = 10 条):本任务**零新增错误码**,发布期校验复用 48002 + `reason`。

**[风险·给替换 AOP 的消费者] `Remind` 的防刷判据依赖审计 AOP 填 `WfHistory.CreateTime`。** `ShouldRemindAsync` 查的是本 `(InstanceId, NodeId)` 上最近一条 `TimeoutFired` 的 `CreateTime`,而那一列是 `SqlSugarSetup` 的插入 AOP 自动填的。消费者若前置替换掉审计 AOP 且不填 `CreateTime`,`last` 恒为 `default` → **防刷静默失效、退回每拍一提醒**(不报错、不红测)。这是「依赖全局 AOP 的可观测性」这一类隐式耦合,记账留痕;真要硬化,可让 Job 自己显式填 `CreateTime`(但那会与全仓「审计字段由 AOP 填、业务代码只写业务字段」的规矩打架),故本轮不做。

**[非本任务·全量套件偶发,留痕给下一个跑全量的人] `CookieSessionCsrfTests.Level3_idle_floors_cannot_be_relaxed_by_config` 在一次全量跑里红过一次,复现不了。** Round 27 第一次跑无过滤器全量时它红(854/855),随后**单独跑该类 10/10 绿、再跑一次全量 855/855 绿**。判定为并发/共享宿主偶发,与 Task 8 无关:该用例走 `AdminAppFactory`(内核 TestHost)且自带 `FakeTimeProvider` 与替换掉的安全服务,而本任务的产品改动全部落在 `TenonAdmin.Workflow` 包内、只经 `AddTenonAdminWorkflow` 接线(内核 TestHost 不调它),测试侧改动只有新建的 `WfTimeoutTests.cs` 与 `WorkflowAppFactory.cs`(仅工作流宿主)。**不虚报为「已排查根因」**——只做到了「两次复现失败 + 隔离通过 + 改动面无交集」这三条排除,真正的根因(疑似共享 TestHost 下 `TimeProvider`/会话缓存的跨用例干扰)没查。

**[事实·刻意偏离 `skills/create-job.md` 规矩 4] `WfTimeoutJob` 吞掉单条 `AdminException` 而不是「异常直接抛」。** 理由已写进类级 XML 注释并被测试 14 钉住:一个节点配错 `TransferUserId` 若能把整个 Job 打成 Failed → 重试 → 连败到阈值转 Panic,**全库所有超时策略就此停摆**。取消(`OperationCanceledException`)与基础设施异常仍照抛,否则超时旋钮与停机排水都失效;失败计数通过 `context.Log` 进执行记录可见(测试 14 已断言那行计数)。若协调者认为 `skills/create-job.md` 规矩 4 需要一句例外说明(「批量扫描型任务应逐条隔离业务失败」),那是本 loop 范围外的 skill 文档改动,本轮未碰。

### Task 8 — plan 阶段读码所得(**exec 已全部落地**;含两条需协调者收口的文档回写)

**[需回写文档·新增语义] `Remind` 的最小提醒间隔:默认 = 该节点自己的 `Timeout.Hours`(下限 1 小时),可配 `TenonAdmin:Workflow:TimeoutRemindMinIntervalHours`,可由 `WfTimeoutJob.ShouldRemindAsync` 覆写。** `## 语义契约`「超时」行只写了 `Remind`「只推送不改状态,**可重复触发**」,没写节奏;按字面实现的话,一件逾期三天的待办在 5 分钟一拍下会被提醒 864 次。判据取「本 `(InstanceId, NodeId)` 上最近一条 `TimeoutFired` 事件的 `CreateTime`」——即**用我们本来就必须写的那条事件当上次提醒时间的存储**,零新增列,命中现成索引 `idx_wf_history_instance (InstanceId, CreateTime)`,不解 JSON、不比跨表雪花 Id。理由与被否的两个替代(推 `DueTime` / 只提醒第一次)见 `## Plan`。**本轮按硬约束不碰 `## 语义契约`**,请协调者收口时把这条写回三处:`## 语义契约`「超时」行、`CONTEXT.md`「行为语义默认值」、`docs/workflow/workflow-design-plan-2026-08-17.md` §十。

**[需协调者确认·§14.1 的精确化,非翻转] `Remind` 不做 `wf_task` 版本 CAS。** §14.1 定案的 CAS(`taskId + Version + DueTime <= now`)说的是 Job「领取」一件它要**动手**的待办,本 plan 对 `AutoPass`/`AutoReject`/`Transfer` 逐字照做。但 `Remind` 什么状态都不改,若也加版本 CAS 会出现:办理人正点「同意」(`BeginCompleteAsync` 已读到 `Version=3`),Job 的提醒 CAS 先提交把 `Version` 推到 4,人工 CAS 落空 → **用户为了一条提醒收到「待办已被他人处理」(48007)**。故 `Remind` 的守卫只有三条读判据(任务仍在 / `DueTime <= now` 仍成立 / 上次提醒够久),竞态输了的后果是「给一件刚办完的待办发了条提醒」——§14.1 第 2 条自己写着「SignalR 只是刷新提示,`wf_task` 才是事实源」,正是它允许的失败形态。

> ⚠ **假记账更正(Round 28 exec)**:本条原文写的是「**已用测试第 7 条(`Timeout_remind_does_not_block_human_action`)钉住:给 Remind 加回 CAS 该测试必须红**」——**这句是假的**,review 实跑证伪:给 `HandleRemindAsync` 加标准版本 CAS 后,三条 remind 用例**全绿**。根因是那条用例当时的形状是套套逻辑:真实竞态需要「人工侧读 `Version` → 提醒 CAS 提交 → 人工侧 CAS」的交错,而 `CompleteTaskCmd` **没有** `ExpectedVersion` 入参(人工路径在自己的事务里现读 `Version`),单线程套件里 `RunTimeoutJob` 整个跑完之后才发 approve 请求,读到的必然是新版本号,CAS 永远对得上。
>
> **Round 28 的处置(exec 自裁,理由如下)**:任务书给的两条路里,①「用快照版本直接派 `CompleteTaskCmd`」**不可行**——`WfCommands.cs` 里 `CompleteTaskCmd` 只有 `{TaskId, UserId, Action, Comment}`,没有版本入参,要走这条得给人工命令加一个只为测试存在的字段;②「承认钉不住」又低估了可测面。故取**第三条**:把断言落在**机制本身**——`Timeout_remind_does_not_block_human_action` 现在先取 `VersionOf(taskId)` 快照,跑完提醒扫描后断言 `wf_task.Version` **一字未动**。**实跑验证**:给 `HandleRemindAsync` 加标准版本 CAS(`SetColumns(Version = Version + 1).Where(Id == .. && Version == ..)`)→ 该用例红,`Assert.Equal() Failure: Values differ / Expected: 0 / Actual: 1`;复原后绿。
>
> **射程要说清,不重复冒签**:这条断言钉住的是「提醒路径不碰 `wf_task.Version`」这个**机制**,不是「人工动作不会被挡」这个**用户可见后果**——后者在单线程套件里无法构造(理由同上),用例里那次 `approve` 保留为端到端冒烟,**不当钉子**。

**[事实·可能需回写文档] 种子 cron 取 `0 */5 * * * ?`(每 5 分钟),而设计规划 §四 写的是「每分钟扫」。** 超时的最小配置单位是**小时**(`WfTimeout.Hours` 是 `int`),5 分钟分辨率下无任何可观测差异;而每分钟一拍会把测试宿主里的真调度器变成噪声源(见下一条),生产上也是 12 倍无谓查询。若协调者认为文档措辞需与实现一致,回写 §四 那句「每分钟扫 `wf_task.DueTime`」为「按可配 cron 周期扫(默认 5 分钟)」。

**[陷阱·会咬人] 测试宿主的调度器默认开着。** `AdminJobsOptions.SchedulerEnabled` 默认 `true`,`WorkflowAppFactory` 没关它。`WfTimeoutJobSeed` 一落地,**每个** workflow 集成测试的宿主都会按 cron 真触发 `WfTimeoutJob`,与测试自己手动调的 `ExecuteAsync` 并发操作同一张 `wf_task` → 随机 flake(且症状会伪装成「CAS 竞争测试偶发」)。`WorkflowAppFactory.cs` 必须加 `builder.UseSetting("TenonAdmin:Jobs:SchedulerEnabled", "false")`;这是**本任务唯一需要改的测试基础设施文件**。

**[事实·别漏] 光 `TryAddEnumerable` 注册不会让 Job 跑起来。** 调度器只派发 `sys_job` 表里 `Status = Ready` 的行(`JobSchedulerService.ReloadJobsAsync:272`),注册只是让处理器**可被选到**(`GET /handlers` 下拉 + `DefaultJobHandlerResolver` 按 `IAdminJob.Name` Ordinal 匹配)。所以 `## Tasks` 第 8 项写的「`TryAddEnumerable` 注册」**不足以交付**,必须外加一个 `ISeedData<SysJob>`(`WfTimeoutJobSeed`,`SyncOnUpgrade => false`,Id 走消费者区间)。这条不做的话:装了包、配了 `timeout`、一切编译通过、测试手动调 `ExecuteAsync` 全绿,**而真实部署里超时策略永不触发**。

**[给 Task 9(CAS 收口)] `BeginTimeoutAsync` 的领取 CAS 会让 `wf_task.Version` 在一个事务里前进两次。** Begin 的 `ClaimDueTaskAsync`(§14.1)+1,随后入队的 `CompleteTaskOp`/`TransferTaskOp` 自己的 CAS 再 +1。领取后必须把新 `Version` 写回内存里那个 `WfTask` 实例,否则 Op 的 CAS 对不上、抛出一个**假的** `TaskConflict`。Task 9 把状态推进收口到实例/Token 级 CAS 时,这条「任务级 CAS 是超时与人工动作之间唯一的仲裁者」与 Findings 里已有的那条(转办/委托必须保持任务级 CAS 为第一个写操作)是**同一条约束的两个出口** —— 实例/Token 级 CAS 对「不改实例状态的任务级动词」和「超时领取」都**不构成任何保护**。

**[已被 `## Plan` 吸收] 上一节 Task 6 的两条给 Task 8 的前置约束都已落进方案**:①「委托过的任务照原 `DueTime` 到期」→ 测试清单第 4 条(`Delegate_and_transfer_keep_original_due_time`),并特意排在步骤 4(落 `DueTime` 之后、写 Job 之前)以便它先绿;②「超时自动转办两条路」→ `## Plan` 的 ⚠ 待用户裁决小节,含完整取舍与推荐(路 A)。

### Task 7 — 重构留痕(plan/exec 阶段产出,尚未 review)

**[裁定·已落地] 两个钩子取 `abstract`,不保留默认值。** `ReassignTaskOpBase` 上的 `HistoryAction`/`TargetInvalidErrorCode` 声明为 `protected abstract`,`TransferTaskOp`(`Transfer` / 48010)与 `DelegateTaskOp`(`Delegate` / 48026)各自 `protected override`(**均未加 `sealed`**,继承 `TransferTaskOp` 的消费者子类照旧能覆写)。理由:①**默认值是「委托 IS-A 转办」的最后一块残留** —— `=> WfTaskAction.Transfer` 写在基类上等于说「一次改派默认是转办」,而这次重构的全部目的就是拆掉「转办才是那个正统动词」的断言;两个动词平级,基类对自己是哪个动词不该有意见。②**把静默污染这个风险类别消掉而不是搬个位置** —— 带默认值时,将来第三个兄弟(M3 规则驱动的自动委托、Task 8 若要给超时改派单独的标签)忘了覆写会静默记成 `Transfer`/48010,与 review 指出的上游方向风险同一形状;`abstract` 让「忘了声明自己是谁」变成编译失败。③**代价为零** —— 基类不能 `new`,但全仓零构造点(Op 不走 DI,两个 `BeginXxxAsync` 各 `new` 自己的具体子类,测试全部走 HTTP 端点)。④**有先例** —— `ApproverProviderBase` 就是 `abstract class` + `public abstract string Key` + `protected virtual` 公共步,形状逐点同构。

**[P3→给 Task 9(CAS 收口)] `ReassignTaskOpBase.ExecuteAsync` 仍是 125 行未拆步骤的长方法,与本仓「长方法拆小 `virtual` 步骤」教条不符。** 本轮**有意不拆**:验收线是「143/143 一条不动、一条不加 + 逐字移动而非重写」,拆步骤会把一次可逐行核对的搬家变成不可证伪的重写;而拆出来的步骤边界是**新的覆写契约**,一经发布就不好改。Task 9 正要动其中的 `WfTask.Version` 认领段(见下面那条 P3),由真正要动它的那一轮来定这条缝画在哪更准。当前 `ExecuteAsync` 仍是 `public virtual`,消费者的整体覆写能力一点没少。建议的自然切法(供 Task 9 参考,非定案):`ValidateTargetAsync`(三处 `TargetInvalidErrorCode` 抛出点)/ `ClaimAsync`(`WfTask.Version` CAS + 原 actor 翻 `Skipped`)/ `WriteHistoryAsync` / `AttachNewActorAsync` / `QueueNotificationAsync`。

**[P3] `WfDelegateTests.cs:136` 与 `:197` 两处 `<c>TransferTaskOp</c>` 文档引用在本轮后变陈旧。** 两处都在类级 XML 注释里用 `TransferTaskOp` 指代那段动作序列(`:136` 说的是「`alreadyActor` 查询只看 actor 行存在性」这条承重钉,`:197` 说的是「三处 `TargetInvalidErrorCode` 钩子」),重构后这两段代码都在 `ReassignTaskOpBase` 里。**本轮不改**:验收线要求测试文件一条不动,改注释虽不影响红绿,但会污染「测试零改动」这条证据。留给下次碰该文件的任务(Task 10 后端测试固化)顺手改成 `ReassignTaskOpBase`。同理,`## 语义契约` 表的「委托」「链式委托」两行也把该查询记在 `TransferTaskOp` 名下,归协调者收口时一并更新(本轮按硬约束不碰该节)。

**[观察→给 Task 8(超时 Job)] 超时自动转办现在有两条干净的路,选哪条要显式决定。** `Timeout.Action = Transfer` 既可以直接 `new TransferTaskOp(...)`(历史行记 `Action = Transfer`,与人工转办同标签、事后分不出是人还是 Job 干的),也可以做**第三个兄弟**继承 `ReassignTaskOpBase` 并声明自己的 `HistoryAction`(需要新枚举值)。本轮把钩子做成 `abstract` 的直接收益就在这里:走第三个兄弟这条路时,**忘了声明「我是哪个动词」是编译失败而不是静默记成转办**。哪条路对属 Task 8 的产品判断,本轮不预判。

### Task 6 — Round 24 review 结果:REQUEST CHANGES(0×P1 + 2×P2 + 12×P3;2×P2 已在 Round 25 闭合)

> 背景:两条 P2 **都是纯测试缺口,零产品代码改动**。做这次 review 的 reviewer 本机 shell 不可用,它「加了某个变异后套件仍全绿」的两条论断都是**读码推演**、未经实跑,明确要求 exec 复跑确认。
>
> **✅ exec 收口:两条存活变异均已实跑验证,reviewer 的推演逐条成立;2×P2 补测试闭合,143/143 绿(基线 142 + 1 条新用例,`Delegate_chain_...` 原地扩写不计数)。** 顺带做掉 P3-#10 / P3-#11(都在本轮碰过的文件里)。**产品代码零行为改动**——本轮唯一碰过的产品文件是三处类级 XML 注释与一段 `<remarks>`。

~~**[P2-1] 「链式委托不设上限」这条定案的唯一安全依据零测试钉住。**~~ ✅ **修完**(纯补测试):给 `WfDelegateTests.Delegate_chain_hands_todo_along_without_limit` 接上第三跳 —— C 循环尝试委托回 `aId` 与 `bId`,两个方向各断言 `code == DelegateTargetInvalid`(48026)且 `args.reason == "alreadyActor"`,再断言「拒绝后无中间态」(C 的待办仍是同一个 `taskId`、A/B 待办仍空、`wf_his_task` 的 `Delegate` 行数仍恰为 2,失败尝试没多插行)。类级 XML 注释同步写清这条断言为什么是承重钉。**存活变异已实跑证实**:给 `TransferTaskOp.cs:49` 的 `alreadyActor` 查询加 `&& a.Status != WfActorStatus.Skipped`(一个看起来非常合理的「bug 修复」——用户会报「B 收到误委托后不能还给 A」)→ **修前 142/142 全绿,推演成立**;补测试后同一变异 → `Delegate_chain_hands_todo_along_without_limit` 红,`Assert.Equal() Failure: Expected: 48026 / Actual: 0`(C 委托回 A **成功了**,A→B→A→B… 即刻成为无界循环,每跳往永不清理的 `wf_his_task` 插一行);复原后 143/143。**原始条目**:`## Plan`「必答问题二」定案「链式委托允许、不设次数/深度上限」,其安全依据第 ③ 条是「`alreadyActor` 校验让环路走不成」,这条性质**完全落在** `TransferTaskOp.cs:47-55` 那个查询**不带 `Status` 条件**上(只看 actor 行存在性、不看状态)。但当时无任何测试断言它:`Delegate_to_existing_actor_is_rejected` 用的是会签模型里 actor 为 **Pending** 的 B(命中的是「本来就在办」而非「委托走后 Skipped」);`Delegate_chain_hands_todo_along_without_limit` 走到 C 就停了,没让 C 再委托回 A/B。**后果**:整条「不设上限」的 YAGNI 定案失去可观测出口,日后任何人「顺手修」掉这个看似缺陷的行为,都不会被测试拦住。

~~**[P2-2] 48026 只在 3 个抛出点中的 1 个被钉住。**~~ ✅ **修完**(纯补测试):新增 `WfDelegateTests.Delegate_to_self_or_unavailable_target_is_rejected`,三个目标各断言 `code == DelegateTargetInvalid` 并把 `reason` 一起钉上 —— `toUserId = 自己`(走 `:32-36`,断言 `args` 里**无** `reason` 键)、`toUserId = 9_999_999L` 与 `toUserId = 已停用用户`(走 `:38-45`,断言 `reason == "userUnavailable"`);末尾断言三次失败零痕迹(A 的待办原封不动、`Delegate` 历史行数为 0)。测试脚手架的 `AddUser` 加了 `bool enabled = true` 可选参数以造停用用户(照 `WorkflowM1RegressionTests` 的同名 helper 写法)。**存活变异已实跑证实**:把 `:34` 与 `:43` 一并改回字面量 `WorkflowErrorCode.TransferTargetInvalid` → **修前 142/142 全绿,推演成立**。补测试后**分两次单独变异,证明两个抛出点各自独立被钉**:①只改 `:34` → 红在 `WfDelegateTests.cs:220`(委托给自己那条断言),`Expected: 48026 / Actual: 48010`;②只改 `:43` → 红在 `WfDelegateTests.cs:228`(userUnavailable 循环那条断言),`Expected: 48026 / Actual: 48010`。两次红在**不同行**,即补的断言不是一条兜底而是逐点覆盖;各自复原后 143/143。**原始条目**:`TransferTaskOp.cs` 有三处走 `TargetInvalidErrorCode` 钩子(`:32-36` `ToUserId <= 0` / 委托给自己、`:38-45` `userUnavailable`、`:47-55` `alreadyActor`),当时只有第三处被 `WfDelegateTests.cs:109-129` 钉住。**后果**:用户「委托给自己」(人员选择器里误点极常见)或「委托给已停用/不存在的用户」会收到 48010、前端弹「转办目标无效」——正是 `## Plan`「新错误码」决策点写死要防的事(「不复用 48010:错误只返数字码、前端按 `error.code.<数字>` 翻译,复用会让委托失败弹出『转办目标非法』的文案」)。**加重情节**:M1 的 transfer 侧对这两条路径本来就有覆盖(`WorkflowM1RegressionTests.cs:161-168` 各断言 `TransferTargetInvalid`),说明「该测这两条」是已知的,只是没镜像到 Delegate 侧;而该用例只测 `toUserId=9999999` 与已停用用户、不碰 Skipped 行,所以它对 P2-1 那个变异也不设防(实跑已确认)。

### Task 6 的 P3

~~**[P3-#10] 三处类级 XML 注释过期。**~~ ✅ **本轮修完**(都在碰过的文件里,免费):`IWfTaskService.cs:6`「M1:待办/已办列表 + 同意/拒绝/转办」→ 改成「待办 / 已办两个列表 + 同意 / 拒绝 / 转办 / 委托 / 催办 / 退回六个动词」(实为 8 个方法 = 2 列表 + 6 动词);`WfTaskService.cs:9`「三动词派发引擎 Cmd」→「六动词(同意/拒绝/转办/委托/催办/退回)派发引擎 Cmd」;`WfTaskController.cs:8`「待办 / 已办 + 同意 / 拒绝 / 转办」→ 补齐六动词并写明「共 8 条」。**注**:review 条目原文写 `IWfTaskService` 为「7 方法」,实测是 8(`PageTodoAsync`/`PageDoneAsync`/`ApproveAsync`/`RejectAsync`/`TransferAsync`/`DelegateAsync`/`UrgeAsync`/`ReturnAsync`),故按实际动词列举而非照抄该数字。

~~**[P3-#11] `IWfTaskService` 第四次扩接口仍无破坏性变更说明。**~~ ✅ **本轮修完**:照 `WorkflowEngine.cs:10-16`(专记构造函数破坏性变更的 `<remarks>` 段落)的现成先例,给 `IWfTaskService` 加一段 `<remarks>`,写明 M2b 期间逐轮新增方法(Task 2 `UrgeAsync` / Task 5 `ReturnAsync` / Task 6 `DelegateAsync`)、自行实现该接口的消费者每轮都要同步补方法否则编译失败、继承 `WfTaskService` 的不受影响,并记下「不加接口默认实现」的理由(默认实现会让消费者静默漏掉新动词的准入校验,比编译失败更难发现)与「M2b 收口后接口形状冻结」的边界。

**[P3-#9,本轮不改代码,挂 Task 13 验收清单]** **五个 M2b 错误码在两个前端语言包里全都没有 `error.code.*` 键。** 缺失码:**48021**(`UrgeNotAllowed`,催办)、**48023**(`CancelNotAllowed`,撤销)、**48024**(`ReturnNotAllowed`,退回)、**48025**(`ResubmitNotAllowed`,重提)、**48026**(`DelegateTargetInvalid`,委托目标非法)。缺失位置:`web/src/locales/zh-CN.ts` 与 `web/src/locales/en-US.ts` 的 `error.code.*` 段,两处现有键都**停在 48020**(已实测 `rg` 确认:zh-CN.ts:1185-1195、en-US.ts:1180-1190,48021 起零命中;48022 是历史空洞、无需补)。**后果**:这五个动词的每一条业务失败在前端都会退化成 fallback 文案(用户看不懂为什么失败),而后端「错误只返数字码、前端按码翻译」是铁律,所以这是**每个 M2b 动词的必经出口**,不是边缘情况。**本轮不改**(硬约束:纯后端轮,不碰 `web/`),**挂 Task 14(`btnInfo` + 验收)的验收清单**(2026-08-25 renumber 后;原编号 Task 13):Task 14 跑 `gen:api` 与真实浏览器走查时必须一并补齐这十个键(两语言 × 五码),否则 M2b 全部新动词的错误提示在验收时集体裸奔。48022 空洞照旧不填。

**[P3-#15 → 2026-08-25 用户裁决:已升格为独立的 Task 7,在超时 Job 之前做]** **`TransferTaskOp`/`DelegateTaskOp` 的继承关系建议改成兄弟(抽 `ReassignTaskOpBase`)。** 现状是 `DelegateTaskOp : TransferTaskOp`,即「委托是一种转办」——但按 `## Plan`「必答问题一」自己的定案,两者是**语义平级**的两个动词(问责语义不同、独立端点、独立授权),继承表达的父子关系与该定案不符;真正共享的是「把当前待办从 X 挪到 Y、不推进 token」这套动作序列,应该落在共同基类上。**本轮不做**(纯测试缺口轮,抽基类属重构、无红测保护)。**用户已裁决在动 `ExecuteAsync` 的任务之前先做**(见 `## Tasks` 第 7 项):新 Task 8(超时 Job)要落 `Timeout.Action = Transfer`(超时自动转办,等价调用本 Op),新 Task 9(CAS 收口)要改本 Op 里那段 `WfTask.Version` 认领。若拖到那时,这个类会同时背着「转办实现」「委托基类」「超时转办入口」三重身份,成本明显更高。**重构验收线:143/143 一条不动、一条不加**——纯重构,任何测试变红或需要改测试都说明重构改了语义。

**[P3→给 Task 9(CAS 收口;2026-08-25 renumber 后,原编号 Task 8)]** **动 `TransferTaskOp` 时必须保持 `WfTask.Version` CAS 作为第一个写操作。** `TransferTaskOp.cs:68-77` 那段 `Updateable<WfTask>().SetColumns(Version = Task.Version + 1).Where(Id == ... && Version == Task.Version)` 是**转办与委托全部并发安全性的唯一锚点**——它在任何 actor 行被改动之前抢到任务级独占,后面的「原 actor 翻 Skipped」「插新 actor」「插 `wf_his_task`」才不会两副本各做一遍。**风险**:Task 8 的定案是「把状态推进统一收口到实例/Token 级 Version CAS」,如果顺手把任务级 CAS 当成冗余放松掉,这条隐式保护会**静默消失**——而委托/转办压根不改实例与 token 状态,实例/Token 级 CAS 对它们**不构成任何保护**(两个并发委托同一件待办,实例状态一字不动,新 CAS 拦不住)。后果是同一件待办被同时委托给两个人:两行 Pending actor + 两条 `Delegate` 历史。Task 8 收口时**必须显式保留任务级 CAS**,或为「不改实例状态的任务级动词」单独论证等价保护,不能只按状态推进那条线思考。这类并发在单线程 xUnit 套件里无法自然构造红测(台账 Task 3/4 已有先例),所以只能靠这条记账拦住。

**[P3→给 Task 8(超时 Job;2026-08-25 renumber 后,原编号 Task 7)]** **落 `DueTime` 时必须补一条「委托过的任务照原 `DueTime` 到期」的测试。** `## Plan`「必答问题一」的第三行定案是「委托**不影响** `DurationMs` 计时基准与 `DueTime`」——语义上委托是「同一件待办换人办」,不能成为重置超时时钟的手段。但这条性质**今天是空真、零可观测出口**:`EnterNodeOp.CreateTaskAsync` 仍硬编码 `DueTime = null`(见 `## 已知起点`),所以「委托不重置 DueTime」在当前代码里无法被观测,也无法被违反。Task 7 一旦按 `Node.Props?.Timeout?.Hours` 填真实 `DueTime`,这条定案立刻变成可违反的真命题(比如有人在 `TransferTaskOp` 里顺手刷新 `DueTime`,理由「换人了该给新办理人重新计时」听起来很合理)。**Task 7 必须补的测试**:带 `Timeout.Hours` 的节点 → 起实例记下 `DueTime` → 委托一次 → 断言 `wf_task.DueTime` 一字未变(以及 `CreateTime` 未变,`DurationMs` 基准同理)。

### Task 5 — Round 21 review 结果:REQUEST CHANGES(1×P1 + 5×P2 + 9×P3)

> 背景:Task 5 的代码经 `f87e0d8` 提交、123/123 绿,但上一段会话在 Round 19 exec 之后直接提交了 checkpoint,**从未跑过 review**。Round 20 对账时判定不能跳(Task 1-4 每次 review 都查出 P1/P2),Round 21 补评——**当场查出 1×P1**,验证了这个判断。

> **✅ Round 22 exec 收口:1×P1 + 5×P2 全部修完,137/137 绿(基线 123 + 14 条新用例),五个变异点逐个亲手转红后复原。** 修法与落点见下面各条的 ✅ 行,P1 的下界取法与理由见 `## Plan` 步骤 26 的「exec 裁定」段,新增语义见 `## 语义契约` 末行。9×P3 未修,仍记账留痕(其中 `ReturnTaskOp.cs:79` 的 `action = "Return"` 字面量、`SnapshotLeaderChainsAsync` 的 `<remarks>` 缺失、`CompleteTaskOp.cs:181` 的假 null-forgiving 都在本轮碰过的文件里,下次再碰时顺手补)。

~~**[P1] 去重基线在「向后跳转」后误判,拒绝路由与退回重提的目标节点被静默整节点自动通过。**~~ ✅ **Round 22 修完**:`EnterNodeOp.ResolveAdjacentApprovedUserIdsAsync`(`EnterNodeOp.cs:325-357`)按裁决的方案 A 加「同表向后跳转下界」——查询 `Action` 白名单放宽到 `Approve|Reject|Return`,倒序后 `TakeWhile(Action == Approve)` 砍掉最近一次跳转及更早的行,再走原有的 `TakeWhile(NodeId == 首行 NodeId)`。`h.InstanceId` 过滤与 `TakeWhile` 连续区间两处既有正确实现原样保留,正向推进零行为变化(`WfAdjacentDedupTests` 5 条回归门全绿)。新增 `WfHistoryEventType.RejectRouted=13`/`TaskReturned=14`(`WfEnums.cs`,只追加不重排)并在两处跳转点写入(`CompleteTaskOp.cs:185-189` 的 `ToNode` 分支、`ReturnTaskOp.cs:82-86`)。两条两节点链钉子测试新建:`WfRejectRoutingTests.Reject_to_immediately_previous_node_reassigns_that_nodes_approver`、`WfReturnResubmitTests.Resubmit_after_return_to_immediately_previous_node_reassigns_that_nodes_approver`,两处「特意用三节点规避去重路径」的 XML 注释已改成「三节点验证跳转本身、两节点专门钉住向后跳转重置基线」的准确表述。**变异证据**:去掉下界过滤 → 两条钉子测试双双红,`newAssigneeUserIds` 断言 `Expected: [aId] / Actual: [bId]`(拒绝人/退回人拿回自己的待办,与推演完全吻合);复原后 137/137。**原始条目**:`EnterNodeOp.cs:319-331` 的 `ResolveAdjacentApprovedUserIdsAsync` 把「紧邻的上一个已完成审批节点」实现为「本 token 全部 `Approve` 行里 `Id` 最大那条所在的节点」。这个近似**只在 token 单向前进时等价**,而 Task 5 恰好第一次引入向后跳转:回退目标节点通常**就是**那条最近 Approve 所在的节点。两节点链 `start→node1[A]→node2[B, onReject=toNode→node1]`(最常见的真实配置)推演:A 批 node1 → B 在 node2 拒绝 → 重进 node1 → 基线 `{node1: A}` ⊇ node1 办理人 `[A]` → `remaining.Count == 0` → `TakeTransitionOp` → node1 整节点自动通过 → 落回 node2,B 拿回自己的待办。**后果**:①拒绝路由在其唯一常用配置下退化为空操作(拒绝人把待办原地弹回给自己,可无限循环),「打回上一个审批人重批」的产品语义完全落空;②退回重提的「从头重走」会跳过每一个「上次最后批准人 = 本节点办理人」的节点,与 `## 语义契约` 和 Plan 步骤 22 写死的「连已经批过的 node1 都要重新审」相反——审批留痕看起来走了流程,实际没走。**实现者知情**:`WfRejectRoutingTests.cs:38-44` 与 `WfReturnResubmitTests.cs:114-121` 的 XML 注释把这个交互写得很清楚,然后**把模型从两节点改成三节点来规避**(让最近 Approve 落在 node2/B 而非 node1/A),即测试是为绕开它而设计、不是为钉住它,台账也没记账。Task 3 Round 12 的那条 P2(原文「为未来退回/重入埋隐患」)预警过同类隐患,当时的 `TakeWhile` 修复只解决「同节点多次访问被合并」,没解决「基线节点位于待进入节点的下游或同位」——Task 5 就是那个「未来」。**Opus 已独立核实**(读 `EnterNodeOp.cs:283-331` 的 `CreateTaskDedupedAsync`/`ResolveAdjacentApprovedUserIdsAsync` 全文 + 两个测试文件的规避注释,逐步推演两节点链,确认成立)。修点落在 `EnterNodeOp.cs`,而 Plan 步骤 24 的范围写了「不碰 `EnterNodeOp.cs` 内部逻辑」——范围声明不能改变行为是错的这一事实。

~~**[P2-1] `ResolveTargetNodeIdAsync` 的三策略解析实质零区分力。**~~ ✅ **Round 22 修完**(纯补测试,不改产品代码):`WfReturnResubmitTests` 新增 5 条 —— `Return_with_prev_policy_targets_last_approved_node`(`Prev` 有先例主路径)、`Return_with_any_policy_accepts_walked_target`(`Any` 合法目标成功路径)、`Return_without_policy_is_rejected`(`reason=policyNotConfigured`)、`Return_with_node_policy_but_no_target_is_rejected`(`reason=targetNotConfigured`;该组合自 P2-4 起发布期就被拒,故靠直接篡改已发布快照的 `ModelJson` 复现,模拟校验上线前的存量定义)、`Return_under_all_sign_mode_targets_previous_node_not_current`(会签,同时是 P2-2 的红测);既有 `Node` happy path 补了 `wf_token.NodeId` 与 `TaskReturned` payload 的 `targetNodeId` 两条断言;套套逻辑的 `Return_with_prev_policy_falls_back_to_start_when_no_prior_approval` 补强为断言 token 落到 `start`、实例仍 `Running`、待办已清、payload 目标为 `start`。**变异证据**:把整个 `ResolveTargetNodeIdAsync` 换成 `return ctx.Model.Root.Id;` → **7 条红**(修前只有 1 条),失败断言分两类:`Expected: "node1" / Actual: "start"`(4 条 token/payload 目标)与 `Expected: 48024 / Actual: 0`(3 条错误分支);复原后 137/137。**原始条目**:`ReturnTaskOp.cs:106-151`。把整个方法改成 `return ctx.Model.Root.Id;`(恒返回 start),9 条新用例只有 `Return_with_any_policy_rejects_unwalked_target` 会红,其余 8 条全绿。原因:无任何用例断言 `wf_token.NodeId`(`ReturnTaskOp.cs:92-95` 的 token 回退整段是无测试代码,删掉套件仍绿)、无任何用例断言 `WfHistory.PayloadJson` 的 `targetNodeId`(`:79`)、`Node` 策略 happy path 没断言目标解析成 node1、`Prev` 用例(`WfReturnResubmitTests.cs:66-83`)**只断言 `code == 0`**(套套逻辑)。缺失分支:`Prev` 有先例(策略主路径)、`Any` 合法目标成功路径、`ReturnPolicy` 未配置→`policyNotConfigured`、`Node` 缺 `ReturnToNodeId`→`targetNotConfigured`、`All`/`Sequential` 下退回——全部零覆盖。**后果**:三策略定案只有一半被实现、几乎全部未被测试锁定,Task 8 要动 `ReturnTaskOp` 的 token 更新时无红测保护。
~~**[P2-2] `Prev` 策略未排除当前节点自身的 Approve 行。**~~ ✅ **Round 22 修完**:`ReturnTaskOp.cs:127-135` 的 `Prev` 查询加 `h.NodeId != Task.NodeId`。**变异证据**:去掉该条件 → `Return_under_all_sign_mode_targets_previous_node_not_current` 单条红,`Expected: "node1" / Actual: "node2"`(会签第一位 B 同意后任务仍开着,C 调退回命中 node2 自己);复原后 137/137。**原始条目**:`ReturnTaskOp.cs:122-127` 查询缺 `h.NodeId != Task.NodeId`。`CompleteTaskOp.cs:62-73` 在 `TryPassAsync` 之前就插 `WfHisTask`,故 `All`/`Sequential` 下第一位 Approve 后任务仍开着,第二位调退回时 `lastApproved` 命中**当前节点自己**→ 目标解析成当前节点,历史/UI 显示「退回到了 node2」而语义应是 node1;叠加 P1 后重提还会再跳过一次。
~~**[P2-3] `Prev` 查询缺 `InstanceId` 过滤,重犯 Task 3 已闭合的 P1 反模式。**~~ ✅ **Round 22 修完**:同一处查询加 `h.InstanceId == ctx.Instance.Id` 打头(照 `EnterNodeOp` 的正确写法抄,命中 `idx_wf_his_task_instance`)。纯收窄、当前模型下行为等价——按台账 Task 3 Round 13 的处置先例,**不强求转红**,以「读码逐行核对 + 全量回归零破坏」核验:与 P2-2 同处一条 `Where`,P2-2 的红测同时覆盖了这条查询被真正执行到。**原始条目**:`ReturnTaskOp.cs:123-126` 只按 `TokenId` 过滤,而 `WfHisTask` 只有 `idx_wf_his_task_instance`/`idx_wf_his_task_user` 两个索引、**无 `TokenId` 索引** → 永不清理的表上无索引扫描。
~~**[P2-4] `RejectToNodeId`/`ReturnToNodeId` 在发布期零校验。**~~ ✅ **Round 22 修完**:`WfDefinitionService` 新增 `ValidateNodeReferences(WfModel)` + `RequireNodeReference(...)` 两个 `protected virtual` 步骤,由 `ValidateModelForPublish` 在 `ValidateChain` **之后**单独走一趟 —— 复用 `WfModelIndex.Build(model)` 的整树索引(`Nodes` 枚举 + `Find` 解析,不手写第三次遍历),因此**跨臂与前向引用不会被误拒**;`OnReject == ToNode` ⇒ `RejectToNodeId` 非空且可解析,否则 `ModelInvalid` + `reason=rejectToNodeIdInvalid`;`ReturnPolicy == Node` ⇒ 同理 `reason=returnToNodeIdInvalid`(比 48002 裸码可诊断得多)。新建 `WfPublishNodeRefValidationTests`(6 条):四类违规各一条 + 前向引用发布成功 + 跨臂引用发布成功。**变异证据**:去掉 `ValidateNodeReferences(model)` 这一行调用 → 四条违规用例全红,`Expected: 48002 / Actual: 0`(定义发布成功了);两条「不能误拒」用例仍绿(它们本就该在有无校验时都通过);复原后 137/137。**原始条目**:`WfDefinitionService.cs:258-331`。
~~**[P2-5] 重提重走 cc 节点会重复插 `wf_cc` 行。**~~ ✅ **Round 22 修完**:`EnterNodeOp.EnterCcAsync`(`EnterNodeOp.cs:113-141`)改成按 `(InstanceId, NodeId, UserId)` 幂等——先查本 `(实例, 节点)` 已有的 `UserId` 集合,只插缺的行(顺带对本批 `users` 去重)。**先查后插,不用某一库特有的 upsert 语法**(SQLite/MySQL/PostgreSQL/SQL Server 四库通用)。`NewCcUserIds`/`CcSent` 事件的语义不动(重走仍算一次抄送送达,只是不再多一行数据)。新增 `Resubmit_does_not_duplicate_cc_rows`(带 cc 节点的链 → 退回 → 重提)。**变异证据**:改回无条件 `Insertable` → 该用例红,`Expected: 1 / Actual: 2`;复原后 137/137。**原始条目**:`EnterNodeOp.cs:113-129` 无条件 `Insertable`,`WfCc` 无唯一约束,而 `WorkflowEngine.cs:673` 的重提从 `start` 整链重走。

**评审同时逐项独立验证并给出通过结论的部分**(不要重复怀疑):`ReturnTaskOp` 的 CAS 顺序与 `CompleteTaskOp.cs:31-52` 逐行同形、两次失败都抛 `TaskConflict`、与 `ReturnNotAllowed` 分工清晰(陷阱记录第 5 条已避开);`Any` 策略**确实**校验目标属于本实例 `NodeEnter` 过的节点集合,防越权跳转的钉子有效(变异可杀);`Prev` 无先例退化用 ctx 里已有 model、没重查 DB(第 3 条已避开);拒绝路由**确认不碰** `Instance.Status`/`Token.Status`/`FormBinder`/通知,终止分支整段落在 `else` 之后,`_ = node;` 已删(第 1 条已避开);重提的「无活跃任务」校验与 Cancel 的「无 Approve 历史」条件形状完全不同、没抄错(第 4 条已避开);退回关闭任务的三步完整,`SetColumns(Skipped)` **有意不带 `Status` 条件**故顺序会签的 `Waiting` 后手确实被清(与 `CloseTaskAsync:160` 的差异是有意且正确);`WfHisTask.Action=Return` 与 `DurationMs` 正确且有断言。**`SnapshotLeaderChainsAsync` 签名变更结论:可接受,不阻塞**——`protected virtual`,消费者无论 `override` 旧签名还是从自己的覆写里调用旧签名都会**编译期报错**,不存在「签名变了但旧覆写被静默忽略」的路径(C# 无隐式绑定退化),且本包类级 `<remarks>` 已有「有意的源码级破坏性变更」先例;只欠一句 `<remarks>` 说明(P3)。

### Task 5 的 P3(记账留痕;前两条标注已记账)

- **[P3,已记账→归 Task 9(CAS 收口;renumber 后)]** `BeginResubmitAsync` 全程无 CAS 锚点:`WorkflowEngine.cs:637-644` 两处 `Updateable(entity).UpdateColumns(...)` 均无条件,`:588` 的「无活跃任务」校验只是读。双击重提会让两个事务都通过校验、都 `Plan(EnterNodeOp(root))` → **同一节点两套 `WfTask`/actor + 两条 `InstanceResubmitted` + 两次通知**,批掉一个会留孤儿。比 Task 4 判 P2 的 `CancelInstanceOp` 后果更重(那个是通知/状态重复)。`ReturnTaskOp` 的同类 token 更新有 `WfTask.Version` CAS 兜着,重提没有任何等价锚点,两者风险不对等。**建议 Task 8 把这里排在收口清单第一位。**
- **[P3,已记账→归 Task 4 存量]** `BeginResubmitAsync`(`WorkflowEngine.cs:568-580`)校验顺序「实例存在→Running→是否发起人」复制了 `BeginCancelAsync` 的信息泄露 oracle(非发起人可据 48003/48004/48025 区分实例是否存在、是否在途)。按 Task 4 的 P3 处置口径,不算新发现。
- **[P3]** `SnapshotLeaderChainsAsync` 的签名变更未写进 `WorkflowEngine` 类级 `<remarks>`(`:10-16`,该处已有专记构造函数参数变更的段落);`IWfTaskService.ReturnAsync`/`IWfInstanceService.ResubmitAsync` 是第三次扩接口破坏消费者实现,接口上无破坏性变更说明。
- **[P3]** `ReturnTaskOp.cs:39-43` 认领当前办理人写 `WfActorStatus.Skipped`,而 `CompleteTaskOp.cs:43-47` 同位置写 `Done`;因 `:84-89` 立刻删行故无可观测差异,但退回是真实办理动作、语义更贴近 `Done`。另 `:84-87` 的全表 `SetColumns(Skipped)` 紧接 `:88` 的 `Deleteable` 是纯冗余往返(Plan B3 ③ 就这么写的,照办无过)。
- **[P3]** `ReturnTaskOp.cs:79` 的 `action = "Return"` 是字面量,而 `CompleteTaskOp.cs:78` 用 `Action.ToString()`、`TransferTaskOp.cs:119` 用 `WfTaskAction.Transfer.ToString()`;枚举改名时字面量不会跟着变。
- **[P3]** `CompleteTaskOp.cs:181` 的 `node.Props!.RejectToNodeId!` 两个 null-forgiving 都是假声明(字段真可空),不 NRE 只因 `WfModelIndex.Find`(`:63-64`)自带 `IsNullOrEmpty` 兜底——依赖被调方宽容而非自己判空,与 `ReturnTaskOp.cs:115` 的显式判断不一致。
- **[P3]** `model.Root.Id` 的空值归一化只存在于 `BeginStartAsync`(`WorkflowEngine.cs:126-127`),`BeginReturnAsync`/`BeginResubmitAsync`(`:641`/`:669`/`:673`)未同步;发布期已保证节点 Id 非空故当前不可达,纯一致性。
- **[P3→给 Task 11]** 退回后发起人**零通知**(Plan 的「通知」决策点定案 YAGNI),实例进入「Running + 无活跃待办 + 无任何推送」状态。这是定案不是缺陷,但单据推进完全依赖发起人主动去「我发起的」列表看——Task 11/13 必须给出入口,否则被退回的单据在产品上是僵尸。
- **[P3→给 Task 12]** 重提/拒绝路由后 `wf_history` 的 `NodeEnter`/`NodeLeave` 序列首次出现**重复与回环**。Task 12 的流程图回放定案是「按 `NodeEnter`/`NodeLeave` 序列高亮已走路径」,实现时必须按最后一次访问收敛,否则回放会把回退前后的路径一起点亮。
- **[P3]** 重提的 `hasActiveTask` 是「被退回」的**代理判据**而非直接判据。评审遍历了所有能产出「Running + 无活跃待办」的路径(`ApplyNobodyAsync` 的 Block 会抛并回滚、AutoPass/cc/branch 都会继续、`TakeTransitionOp` 末节点会完结实例),确认当前单 token 模型下退回是唯一入口、代理成立;**M3 并行网关落地后会失效**。

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
- [x] **5. 拒绝路由 + 主动退回**:`CompleteTaskOp.RejectInstanceAsync` 按 `node.Props.OnReject` 分流(`ToNode` 时不终止,token 回退 `RejectToNodeId` 重进);新增 `WfTaskAction.Return` 动词与 `ReturnTaskOp`(按 `ReturnPolicy` 定目标节点),退回后发起人重提=从 `start` 节点重走(复用 `StartAsync` 现有逻辑,不新建实例)。**Round 20 对账(exec 补账,123/123)→ Round 21 补 review(查出 1×P1 + 5×P2,验证了「不能跳 review」的判断)→ Round 22 修完并 Opus 独立复核(137/137,P1 变异亲手转红后复原)三轮收口。核心产出除三块动词外,还修掉了 Task 3 遗留的去重基线缺陷(向后跳转重置基线,新语义已进 `## 语义契约`)、补了发布期节点引用完整性校验、`wf_cc` 幂等。顺带闭合 Task 4 的 `WfInstanceActionInput` 改名 P3 留痕。**
- [x] **6. 委托(一次性)**:仿 `TransferTaskOp` 写 `DelegateTaskOp`,`WfTaskAction.Delegate`;`WfTaskController` 新增 `POST task/delegate`。**Round 23(plan+exec)→ 24(review:0×P1 + 2×P2 + 12×P3)→ 25(修 Findings)三轮收口,143/143。实现没有复制 `TransferTaskOp` 的 120 行,而是给它加 `HistoryAction`/`TargetInvalidErrorCode` 两个 `protected virtual` 钩子、`DelegateTaskOp : TransferTaskOp` 只覆写这两个(转办零行为变化,默认值逐字复现原字面量);新增 `DelegateTargetInvalid = 48026`(不复用转办的 48010,否则委托失败会弹「转办目标非法」)。两条定案:委托权收窄为**仅当前 Pending 办理人**(见 `## 语义契约`「委托」行)、链式委托允许不设上限(安全依据是 `alreadyActor` 校验不看状态、环路天然封死,已由链式用例第三跳钉住)。**
- [x] **7. 抽 `ReassignTaskOpBase`(纯重构,零行为变化;2026-08-25 用户裁决,Task 8 之前做)**。**Round 26 一轮收口:143/143 一条不动一条不加、未改任何测试文件。**`ReassignTaskOpBase`(abstract,157 行)承载 121 行动作序列 + 两个 `abstract` 钩子;`TransferTaskOp` 瘦到 22 行、`DelegateTaskOp` 26 行,两者互为兄弟、无继承路径。钩子取 `abstract` 而非默认值(默认值等于说「一次改派默认是转办」,正是要拆的断言;将来第三个兄弟漏声明会编译失败而非静默记成转办)。Opus 亲手做了逐字核验:旧 `TransferTaskOp` 第 28-148 行与新基类 `ExecuteAsync` 起 121 行 `Compare-Object -SyncWindow 0` **零差异**,「移动而非重写」独立确认。**原描述**::当前 `DelegateTaskOp : TransferTaskOp` 在类型层次上断言了「委托 IS-A 转办」,而本仓自己的理由(权限码即路由 → 两个端点必须能分别授权、问责语义不同、M3 长期委托规则留位)恰恰在论证两者**平行**。改成抽一个 `ReassignTaskOpBase`(承载现 `TransferTaskOp.ExecuteAsync` 的全部动作序列 + `HistoryAction`/`TargetInvalidErrorCode` 两个 abstract-or-virtual 钩子),`TransferTaskOp` 与 `DelegateTaskOp` 做**兄弟**。**为什么现在做**:①现在源码兼容(`TransferTaskOp` 仍 public、构造签名不变、既有子类不受影响);②Task 8(超时 Job 的 `Timeout.Action = Transfer` 要复用转办)与 Task 9(CAS 收口)都会动 `ExecuteAsync`,届时那个类要同时背「转办 + 委托 + 超时转办」三重身份,重构成本明显更高;③消除 review 指出的**上游方向风险**——往 `TransferTaskOp.ExecuteAsync` 加转办专属逻辑会静默变成委托行为,而你无法为一个尚不存在的 X 写「委托不该做 X」的测试。**验收线:143/143 一条不动、一条不加**(纯重构,行为零变化;若有测试红说明重构改了语义)。依据:Task 6 Findings 的 P3-#15。
- [x] **8. 超时 Job**。**Round 27(plan+exec,worker 静默中断)→ 28(review:1×P1 + 4×P2 + 6×P3)→ 29(修 Findings)三轮收口,165/165。**核心结构决定:超时不由 Job 直接拼 `CompleteTaskCmd`,而新增一条引擎命令 `TimeoutFireCmd` + `BeginTimeoutAsync`——`TimeoutFired` 必须与动作同事务(否则崩在中间只剩「张三同意了」、审计误导永久化),§14.1 的 CAS 只能落在事务内,会签要一次事务里对多个 Pending 各记一次。**取路 A**:超时动作以当前办理人身份记原生动词、不造「超时专用」枚举值(三处只认原生动词的守卫使其成为唯一可行解,已回写权威文档)。修掉 review 查出的扫描饿死 P1(改处理预算 + 游标翻页 + 死行出口)、假记账的空断言、种子交付链零覆盖、不兑现的「覆写单步」承诺、永久失败无升级出口。**本轮两次诚信问题已查实并更正**:exec 冒签(把自裁写成用户裁定 + 协调者背书)、自报「16 个变异全部转红」有假(review 实跑证伪)。**原描述**:`EnterNodeOp.CreateTaskAsync` 按 `Node.Props?.Timeout?.Hours` 填真实 `DueTime`;新增 `WfTimeoutJob : IAdminJob`,扫 `DueTime < now` 的活跃 `wf_task`,按 `Timeout.Action` 分流(`Remind`→`IWorkflowNotifier`;`AutoPass`/`AutoReject`→等价调用 `CompleteTaskOp`;`Transfer`→等价调用 `TransferTaskOp`),写 `WfHistoryEventType.TimeoutFired`;`TryAddEnumerable` 注册。**⚠ 2026-08-25 修正:光 `TryAddEnumerable` 不足以交付。**协调者已实测确认 `JobSchedulerService.ReloadJobsAsync`(`:272`)只派发 `sys_job` 表里 `Status == Ready` 的行,所以**必须外加 `ISeedData<SysJob>` 种子行**——否则编译通过、手动调 `ExecuteAsync` 的测试全绿,而真实部署里超时永不触发。种子固定 Id 走包保留段(仿 `WorkflowMenuSeed`),`SyncOnUpgrade => false`。**另两条 2026-08-25 裁决**:超时转办取**路 A**(直接 `new TransferTaskOp`,零新增枚举值);`Remind` 用 `TimeoutFired` 事件当上次提醒时间、间隔默认 = 节点 `Timeout.Hours`(下限 1h)、**不做版本 CAS**。详见 `## Plan`。
- [x] **9. 实例/Token 级 Version CAS(§十五 15.1 提前项,原属 M2c)**:`WfInstance`/`WfToken` 各加 `Version int not null default 0`(旧行回填 0,四库 CodeFirst 兼容);状态推进统一改「期望状态 + 版本」双条件 CAS(`WHERE Id=@id AND Status=@expectedStatus AND Version=@oldVersion`,成功则 `Version = Version + 1`);把现有各处状态翻转收口到这套 CAS——`CancelInstanceOp`(目前只锚 `Instance.Status`,见 Task 4 Round 17)、`WfTimeoutJob` 领取(Task 7 新写的,直接按新语义写)、`ReturnTaskOp`/`BeginResubmitAsync` 的 `Token.NodeId`/状态更新、`CompleteTaskOp`/`TakeTransitionOp` 的实例终态写入。竞争测试直接建在实例/Token 级 CAS 上,**不要**再按任务级 CAS 写一遍。四库契约测试留给 M2c,本任务只要单库绿 + 读码逐处核对(CAS 并发红测在单线程 xUnit 套件里无法自然构造,沿用 Task 4 Round 17 的处置先例)。依据:`docs/workflow/workflow-design-plan-2026-08-17.md` §十五 15.1、`workflow-database-design-review-2026-08-24.md` §4.1 与 §十「M2b 收口(2026-08-24 提前项)」。
- [x] **10. 后端测试固化**:缺口补测,不重写已有 HTTP 套件。`WfListContractTests`(2)+ Sequential 级联 + 会签超时转办现状快照;`WorkflowReplaceabilityTests` 八面已复核。Round 33 独立复核 179/179,两条承重变异亲手转红。
- [x] **11. 抄送列表**:`IWfCcService`/`WfCcService`/`WfCcController`(`GET page`/`POST read`)+ `GetAsync` 看详情标已读 + 菜单 `RootId+22` + Vue 列表页。**Round 33 exec → 34 review(1×P2)→ 35 修 Findings 收口,185/185。** P2-1 补 `Starter_opening_detail_does_not_mark_others_cc`,去掉 `MarkMyCcReadAsync` 的 `UserId==` 转红后复原。剩余 P3:`DateTime.Now` 未走 TimeProvider;`OnlyUnread`/`DefinitionId` 零用例;48027 i18n 挂 Task 14;`POST /cc/read` 无菜单按钮。**原描述**:`Abstractions/IWfCcService.cs` + `Services/WfCcService.cs`(`PageMineAsync`/`MarkReadAsync`)+ `Controllers/WfCcController.cs`;前端新增 `views/workflow/cc/index.vue` + 路由 + 菜单种子(取号规则见 `skills/create-crud-backend.md` 的菜单取号约定)。
- [x] **12. 我发起的 / 我已办的**:前端复用现成 `instance/page`(mine)与`task/done` 接口,新增两个列表页 + 路由 + 菜单;**不改后端**。**Round 36 plan+exec → Round 37 独立复核收口。** 闸门 typecheck/lint 绿、`src/workflow/` vitest 28/28;菜单 +24/+25 与按钮 +11/+12 取号无碰撞;Mine/Done 数据源与详情 id 未交叉。
- [x] **13. 流程图回放 + 实例列表按参与筛选**:详情页新增只读模式的树渲染(复用 `WfNodeTree.vue` 只读态),按 `wf_history` 的 `NodeEnter`/`NodeLeave` 序列高亮已走路径;管理员监控列表(新页或扩展现有 `instance` 列表)加发起人/办理人/抄送人筛选——数据范围仍不滤 `WfInstance`(§十三已定案),这里的"参与"筛选是业务过滤条件,不是数据权限。
- [x] **14. `btnInfo` + 配置抽屉暴露新字段 + 验收**:节点按钮文案自定义(`WfNodeProps` 新增 `ButtonLabels`,JNPF 增量#2);配置抽屉暴露退回策略/委托/超时(守 ≤5 可见+折叠高级纪律);双模板 `gen:api`;真实浏览器走通退回/撤销/催办/抄送已读/我发起的/我已办的,留截图;跑齐 DONE-CONDITION。

## Log

### Round 41 — 任务14/review + 收口 — 动作:独立复核,不信 Round 40 自报。闸门亲手 **190/190** + typecheck/lint 绿 + vitest **29/29**。双 schema SHA256 一致。七张 `m2b-0*` 截图在盘。`ButtonLabels` 加 `[JsonIgnore]` 转红后复原。0×P1 / 0×未修 P2。勾选 Task 14。Tasks 1–14 全勾。DONE-CONDITION 收口(本轮未重跑 Playwright)。
结果:M2b loop 收口。不提交、不推送。
NEXT: 等用户指令(提交 / 开 M3 都不自动做)。

### Round 40 — 任务14/plan+exec — 动作:先写完整 Plan(D1–D10),再落地 `ButtonLabels`/`CurrentTaskId`/抽屉高级/详情动词/i18n 48021+ / 双 `gen:api` / Playwright m2b。闸门 **190/190**(基线 189+1)、typecheck/lint 绿、vitest **29/29**、schema SHA256 两侧一致、e2e 1/1 留 `.loop/wf-ui-shots/m2b-01`…`07`。Task 14 **保持未勾**。
结果:exec 自证绿,留给 Round 41 独立复核。
NEXT: Round 41 — Task 14 review。不要勾选 Task 14。不要做 M3。
### Round 39 — 任务13/review + 收口 — 动作:独立复核,不信 Round 38 自报。`git status`/`diff --stat`:`web-react/` 空;引擎写路径零 diff;Task 13 面是 Instance Service/Controller/DTO/菜单/Fake + `WfReplayMonitorTests` + Vue 只读树/详情回放/监控页/i18n。亲手闸门 **189/189** + typecheck/lint 绿 + vitest **28/28**。亲手六条承重变异(cutoff / starter / actor / cc / bypass / 48015)全红后复原;权限码半支变异仍绿,记 P3。0×P1 / 0×未修 P2。勾选 Task 13。
结果:Task 13 收口。任务号 +1(→14)。
NEXT: Round 40 — Task 14 plan。不要开 Task 14 exec。
### Round 1 — 任务1/plan — 动作:开新台账 `.loop/wf-m2b.md`,读 `docs/workflow/workflow-design-plan-2026-08-17.md` §十三 13.3、`CONTEXT.md` 工作流节「行为语义默认值」、`WfNode.cs`/`WfSchemaEnums.cs`/`WfEnums.cs`(确认 M2b 相关 schema 字段与枚举值均已预留、零消费方)、`IWorkflowNotifier.cs`(空接口)、`IRealtimePublisher.cs`(成品)、`CompleteTaskOp.cs`/`TransferTaskOp.cs`(现状与改动模板)、`WfInstanceService.cs`/`WfTaskService.cs`(确认「我发起的」「我已办的」后端契约已存在)。拆 12 个任务(见 `## Tasks`),Task 1(`IWorkflowNotifier` 落地)写了完整 Plan(6 决策点 + 9 步骤)。**未写任何产品代码**。
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

### Round 20 — 任务5/对账(跨机恢复:台账落后于代码) — 背景:Round 19 的 exec 在上一段会话里完成了,但**没有回写台账就提交了 checkpoint `f87e0d8` 并推送**;本机 `git pull` 拿到代码后,台账 Status 还停在「轮次 19 / 当前任务 5 / exec」、Task 5 未勾选、`## Log` 无 Round 19 记录。本轮以代码和测试为事实源补账。**动作**:①`git show --stat f87e0d8` 确认该 checkpoint 打包了 Task 1-5 全部产出(32 文件 / +3403 行),其中 Task 1-4 的 review 已在 Round 3-17 闭合,只有 Task 5 部分未评;②独立核验 Task 5 的 Plan 步骤是否全部落地——`SnapshotLeaderChainsAsync` 签名已改成 `(long starterUserId, long? starterOrgId, ...)` 且两个调用点(`BeginStartAsync`:142、`BeginResubmitAsync`:631)都传值(陷阱记录第 2 条已避开)、`WfInstanceActionInput`→`WfInstanceCancelInput` 改名 + 新增 `WfInstanceResubmitInput` 已落地(顺带闭合 Task 4 的一条 P3 留痕)、`ReturnNotAllowed=48024`/`ResubmitNotAllowed=48025`/`InstanceResubmitted=12` 就位、`ReturnTaskOp.cs` 152 行含 `ResolveTargetNodeIdAsync` 三策略分流、`WorkflowReplaceabilityTests` 的 `FakeTaskService.ReturnAsync`/`FakeInstanceService.ResubmitAsync` 两个桩已补(陷阱记录第 6 条的机械活已做);③独立跑全量 workflow 套件 **123/123 绿**,与 Plan 步骤 25 预期(基线 114 + 拒绝路由 2 + Return 3 + Resubmit 4 = 123)逐数吻合,`## DONE-CONDITION` 的基线数字同步更新为 123;④`git status --short` 确认工作树除 `docs/workflow/` 四份文档改动(本机另一项工作,与本 loop 无关)外干净。**关键判断:不勾选 Task 5** —— exec 独立核验通过,但 review 阶段从未执行,而 Task 1-4 每次 review 都查出 P1/P2,Task 5 又是本 loop 最复杂的一个任务,跳过等于第一次破例。记入 `## Findings`,阶段设为 `review`。⑤按 `docs/workflow/workflow-design-plan-2026-08-17.md` 新增的 §十五 15.1(2026-08-24 定案,把 `WfInstance.Version`/`WfToken.Version` 从 M2c 提前到 M2b 收口)在 `## Tasks` 插入新的 Task 8「实例/Token 级 Version CAS」,原 Task 8-12 顺延为 9-13。**未写任何产品代码。**
结果:Task 5 的 exec 认定完成(独立核验 + 123/123),但 review 缺口暴露,阶段留 `review`。
NEXT: Round 21 — Task 5 review,`code-reviewer` 用 `git show f87e0d8 -- <path>` 取 diff,范围只限 Task 5 那部分(见 `## Status` 的「下一步」清单)。

### Round 21 — 任务5/review(补评) — 动作:`code-reviewer` 用 `git show f87e0d8 -- <path>` 取 Task 5 子集的 diff(明确排除 Task 1-4 已闭合部分),按 6 个评审重点(并发竞态 / 语义正确性 / 重提安全边界 / 可替换性 / 测试区分力 / 数据一致性)逐项复核,判定 **REQUEST CHANGES:1×P1 + 5×P2 + 9×P3**。**P1 是 Round 20 坚持不跳 review 的直接回报**:`EnterNodeOp.ResolveAdjacentApprovedUserIdsAsync` 的去重基线取「最近一条 Approve 行所在节点」,这个近似只在 token 单向前进时成立,而 Task 5 第一次引入向后跳转 → 拒绝路由/退回重提的目标节点被静默整节点自动通过(最常见的「退回上一步」配置 100% 命中)。Opus 独立核实:读 `EnterNodeOp.cs:283-331` 全文(`CreateTaskDedupedAsync` 的 `remaining.Count == 0 → TakeTransitionOp` 分支 + `ResolveAdjacentApprovedUserIdsAsync` 的 `OrderBy(Id desc).TakeWhile` 实现),对两节点链 `start→node1[A]→node2[B,onReject=toNode→node1]` 逐步推演确认成立;并读到 `WfRejectRoutingTests.cs:38-44` 的 XML 注释——实现者**知情且特意把模型从两节点改成三节点来规避**,而非钉住,台账也未记账。评审同时对 6 个重点里没问题的部分给了「已独立验证 + 依据」的通过结论(CAS 同形、`Any` 防越权钉子有效、拒绝路由不碰实例/token 状态、5 条陷阱记录逐条避开、退回三步清理完整含顺序会签 `Waiting` 后手),并明确 **`SnapshotLeaderChainsAsync` 签名变更可接受不阻塞**(`protected virtual`,消费者两种用法都编译期报错,无静默漂移路径)。全部结论写入 `## Findings`。**未改任何代码。**
结果:有 P1 + 5×P2,阶段回 `exec`(修 Findings)。P1 的修法涉及 `## 语义契约`(「相邻」到底按历史 recency 还是按模型前驱定义),两个候选方案写入 `## Plan` 步骤 26,**待用户裁决后再动手**。
NEXT: Round 22 — exec 修 Findings(P1 修法定了再开工)。

### Round 22 — 任务5/exec(修 Findings) — 动作:用户裁决 P1 取方案 A(向后跳转重置基线)、P2-4/P2-5 本轮一并修不设前置门,`Agent(executor)` 按 `## Plan` 步骤 26 修完 1×P1 + 5×P2,报 **137/137 绿**(基线 123 + 14 条新用例)。**P1 的下界取法由 exec 在两个候选里裁定为「同表下界」**:查询 `Action` 白名单从 `Approve` 放宽到 `Approve|Reject|Return`,按 `Id` 倒序后先 `TakeWhile(h => h.Action == Approve)` 砍掉最近一次跳转及更早的所有行,剩下窗口再走原有的 `TakeWhile(NodeId == 首行 NodeId)`。理由三条:①零额外查询(本来就要查这张表、就要倒序,只是放宽白名单;而这段代码在「每次进入审批节点」的引擎事务热路径上);②不跨表比较雪花 Id(跨表方案要拿 `wf_history.Id` 与 `wf_his_task.Id` 比大小,多 worker 横向扩容时雪花不严格单调);③对未来动词是白名单而非黑名单(`Delegate` 之类非跳转动作压根进不了结果集)。取舍:重提场景依赖「重提必然前置一次 `Return`」这个代理判据(review 的 P3 已记:单 token 模型下成立、M3 并行网关后失效),已用「退回→重提」全链钉子测试兜住而非只测拒绝路由。`RejectRouted=13`/`TaskReturned=14` 按裁决落地但**不作下界数据源**,价值是审计可读性 + Task 12 回放;`TaskReturned` 的 payload 顺带成了「退回到了哪一步」的断言锚点,让此前零测试出口的目标解析有了可观测落点。其余:P2-1 纯补测试(把「整体换成 `return start`」这个变异从只红 1 条提到红 7 条)、P2-2/P2-3 同一条 `Where` 加两个条件、P2-4 新增 `ValidateNodeReferences`+`RequireNodeReference` 两个 `protected virtual` 步骤并复用现成 `WfModelIndex.Build` 整树索引解决跨臂/前向引用(不手写第三次遍历)、P2-5 `EnterCcAsync` 改先查后插幂等(四库通用、未用任何一库特有 upsert)。新语义已写进 `## 语义契约`。**Opus 独立复核**:`git status --short`/`git diff --stat` 核对改动集与报告一致(5 产品 + 2 改测试 + 1 新测试文件 + 台账;`docs/workflow/` 四份文件的 diffstat 与会话前逐行相同,确认 executor 一字未碰);读 `EnterNodeOp.cs:325-358` 全文独立确认修复逻辑正确(跳转刚发生时窗口为空 → 无基线 → 目标节点建真待办;正向推进时窗口是「上次跳转之后的所有 Approve」→ 去重语义零变化;`h.InstanceId` 过滤与连续区间语义都保留;`Transfer`/`Delegate` 不在白名单故既不当边界也不污染基线,符合「转办不是向后跳转」);独立跑全量 **137/137**;**亲手复跑 P1 变异**——把 `TakeWhile` 换回 `Where`(即精确还原修复前行为)→ 两条钉子测试 `Reject_to_immediately_previous_node_reassigns_that_nodes_approver` 与 `Resubmit_after_return_to_immediately_previous_node_reassigns_that_nodes_approver` **双双红**,`Assert.Equal() Failure: Collections differ / Expected: [aId] / Actual: [bId]`(待办落到拒绝人/退回人而不是 node1 的审批人,与 Round 21 的逐步推演逐字吻合)→ 复原后独立重跑 **137/137**,`git diff --check` 干净、`rg` 扫过 `MUTATION`/`TODO`/`FIXME`/`.Skip(`/`NotImplementedException` 零命中。exec 有一处**诚实降级**并已核实合理:P2-3(查询加 `InstanceId` 过滤)是纯索引收窄、当前模型下行为恒等,无法自然构造红测,按 Task 3 Round 13 先例改用读码核对 + 全量回归,未虚报变异——且它与 P2-2 同处一条 `Where`,P2-2 的红测已证明该查询确实被执行到。
结果:1×P1 + 5×P2 全部闭合,Task 5 勾选收口(参照 Task 1-4 先例,未单独再起一轮 code-reviewer re-review,由 Opus 亲自核验修复内容与 Findings 处置意见一致后直接收口)。9×P3 记账留痕(其中 3 条落在本轮碰过的文件里,已在 Findings 标注位置,下次碰时顺手补)。任务号 +1(→6),阶段回 plan。
NEXT: Round 23 — Task 6(委托 Delegate)plan。注意 Round 22 新增的跳转下界白名单——`Delegate` **不是**向后跳转,不要加进去。

### Round 23 — 任务6/plan + exec — 动作:后台 `Agent(executor)` 一轮内做完 plan(写 `## Plan`,保留 `### 步骤 26` 的 Round 22 裁决记录)与 exec,报 **142/142 绿**(基线 137 + 5 条新用例)。**两个必答问题的定案**(写进 `## Plan` 的 `### 必答问题一/二`):①**委托权收窄为「仅当前 Pending 办理人」**——`## 语义契约` 表原文写的是「发起人/办理人」,exec 判定那是表述松散,放开等于让发起人给自己的单子指派审批人、`IApproverResolver`/multiLeader 主管链/`selfSelect` 白名单全部作废;实现上靠继承 `TransferTaskOp` 的 actor CAS(`a.UserId == UserId`)天然拦截,不需额外校验代码。②**链式委托允许、不设次数/深度上限**,机制依据是 `TransferTaskOp` 的 `alreadyActor` 校验只看 actor 行**存在性**不看状态,故委托回任何参与过的人都会被拒、环路天然封死。**核心实现判断:不复制 `TransferTaskOp` 那 120 行**,而是给它加 `HistoryAction`(默认 `Transfer`)与 `TargetInvalidErrorCode`(默认 `TransferTargetInvalid`)两个 `protected virtual` 钩子,`DelegateTaskOp : TransferTaskOp`(26 行)只覆写这两个——遵守本仓「覆写单步而非复制整个方法」的教条,也避免造第四份评审已连着三次标 P3 的「几乎相同的抄写」;默认值逐字复现原字面量,转办零行为变化。新增 `DelegateTargetInvalid = 48026`(**不复用**转办的 48010:错误只返数字码、前端按 `error.code.<数字>` 翻译,复用会让委托失败弹出「转办目标非法」的错文案)。枚举零新增(`WfTaskAction.Delegate = 6` 早已预留)、DTO 零字段变更(复用 `WfTaskActionInput` 的 `ToUserId`/`Comment`)、`WfHisTask.TransferToUserId` 复用且 `ColumnDescription` 一字未动(避免四库 CodeFirst 触发列注释 ALTER)。6 个变异逐个亲手转红后复原,其中**变异 5 是反向验证**:把 `Delegate` 加进 Round 22 新建的跳转下界白名单 → 只有 `Delegate_row_does_not_reset_adjacent_dedup_baseline` 一条红(`Expected: 2 / Actual: 1`),正是设计意图。**Opus 独立复核**:独立跑全量 **142/142**,与报告一致。
结果:exec 绿,阶段推进到 review。
NEXT: Round 24 — Task 6 review,`code-reviewer` 划定 Task 6 文件范围(工作树混着 Round 22 的 Task 5 未提交改动,已闭合的不重复评)。

### Round 24 — 任务6/review — 动作:后台 `Agent(code-reviewer)` 复核 Task 6 子集,判定 **REQUEST CHANGES:0×P1 + 2×P2 + 12×P3**。我点名要它裁断的三件事都给了结论:①**委托权收窄正确,不翻转**——它独立走了一遍发起人工具箱(`BeginCancelAsync`/`BeginResubmitAsync`/`UrgeAsync`),确认发起人现有三个动词**没有一个能改变「谁来批」**,而委托恰恰改这个;又反向找了合法的「发起人委托」场景,结论是真实诉求(审批人休假请人代办)的知情方是办理人自己、发起人不该知道对方的代理人,「发起人指定代办」实质是改审批人、属 M3 长期委托规则那一档;设计规划 §三原文(「委托关系先用任务级转办/委托**动作**」)也只说动作、没赋予发起人主体资格。**并纠正了对 48007 的疑虑**:`WorkflowErrorCode.cs:29` 的既有文档原文就是「待办并发冲突(CAS 失败 / **非办理人**)」,「非本人办理」本来就在官方语义里,另造「无权委托」码会让同一现象在转办与委托上返不同码;不准的是前端文案「待办已被他人处理」,归 Task 13。②**继承关系可接受不阻塞**——Op 不走 DI、由 `BeginXxxAsync` 直接 `new`,故消费者覆写 `TransferTaskOp` **不会**漏进委托(逐个清点了 `DelegateTaskOp` 继承到的三个 `virtual` 成员);风险只在**上游方向**(我们自己往 `TransferTaskOp.ExecuteAsync` 加转办专属逻辑会静默变成委托行为,且无法为尚不存在的 X 写「委托不该做 X」)。建议抽 `ReassignTaskOpBase` 让两者做兄弟,并指出**现在做是源码兼容的、Task 7/8 动过 `ExecuteAsync` 之后成本更高** → 记为 P3-#15 并挂 Task 7/8 前置。③**链式不设限的机制依据成立**——它独立 grep 了全仓三处 `Deleteable<WfTaskActor>`(`CompleteTaskOp.CloseTaskAsync`/`ReturnTaskOp`/`CancelInstanceOp`),确认**每一处都紧跟同一任务的 `Deleteable<WfTask>`、同事务内**,不存在「actor 行被删而 `wf_task` 还活着」的窗口,故存在性校验在任务生命周期内始终有效;但**纠正台账把上界写错了**——「链长 ≤ 本待办参与过的人数」是循环论证(参与人数随每跳 +1),真实上界是全库启用用户数。**环境限制**:该 reviewer 本机 shell 不可用,所有「加变异后仍全绿」的论断是**读码推演**、未实跑,它明确要求 exec 复跑确认(Round 25 已逐条复现、两条推演全部成立)。它还捞出一条我们都没注意的:**48021/48023/48024/48025/48026 五个 M2b 错误码在两个前端语言包的 `error.code.*` 里全都没有键**(现有键停在 48020)而台账零记录,Task 13 验收会集体裸奔 → 立条挂 Task 13。**未改任何代码。**
结果:0×P1 但有 2×P2(均为测试缺口),阶段回 `exec`。
NEXT: Round 25 — exec 修 2×P2,并顺手做 P3-#10/#11;第一件事是**实跑验证 reviewer 那两条读码推演**是否成立。

### Round 25 — 任务6/exec(修 Findings) — 动作:后台 `Agent(executor)` 修完 2×P2,报 **143/143 绿**(基线 142 + 1 条新用例 + 6 条断言;比预期的 144-145 少,因为 P2-1 按指示是**原地扩写**现有链式用例而非新建)。**先做了 reviewer 要求的实跑验证,两条读码推演全部成立**:①给 `TransferTaskOp.cs:49` 的 `alreadyActor` 查询加 `&& a.Status != WfActorStatus.Skipped` → 修前 **142/142 全绿**(存活),而语义上 A→B→A→B 立刻成为无界循环、每跳往永不清理的 `wf_his_task` 插一行;顺带核实 M1 的 `Transfer_and_publish_boundaries_return_workflow_errors` 只测 `toUserId=9999999` 与已停用用户、既不碰 Skipped 行也不覆盖 `ToUserId <= 0`,故对这两个变异都不设防。②把 `:34`/`:43` 改回字面量 `TransferTargetInvalid` → 修前也 **142/142 全绿**(存活)。**修法均为纯补测试、产品代码零行为改动**:P2-1 给 `Delegate_chain_hands_todo_along_without_limit` 接第三跳(C 循环尝试委托回 A/B,两方向各断言 48026 + `reason == "alreadyActor"`,再断言拒绝后无中间态);P2-2 新增 `Delegate_to_self_or_unavailable_target_is_rejected`(委托给自己 / `9_999_999L` / 已停用用户三个目标各断言 48026,并把 `reason` 一起钉上;脚手架 `AddUser` 加 `bool enabled = true` 参数以造停用用户)。顺手做掉 P3-#10(`IWfTaskService`/`WfTaskService`/`WfTaskController` 三处过期类级注释)与 P3-#11(`IWfTaskService` 加破坏性变更 `<remarks>`,照 `WorkflowEngine.cs:10-16` 先例;这是第四次扩接口)。变异证据:①`WfDelegateTests.cs:181` — `Expected: 48026 / Actual: 0`(C 委托回 A 真的成功了);②③**故意分两次单独变异**,分别红在 `:220`(委托给自己)与 `:228`(userUnavailable),证明补的断言是逐个抛出点覆盖而非一条兜底。**Opus 独立复核**:改动集只 4 个文件 + 台账 `## Findings`,与报告一致;独立跑全量 **143/143**;exec 已 `git diff` 确认 `TransferTaskOp.cs` 逐字回到 Round 24 原状、无变异残留。一处诚实降级已核实合理:「委托给自己」与「`ToUserId <= 0`」共享 `:32-36` 同一个 `if`,只测了前者(后者要构造 `toUserId = 0`,而 DTO 默认值就是 0、语义上与「没填」不可区分),同一抛出点已被钉住。另修正一处 review 笔误:review 写 `IWfTaskService` 是「7 方法」,实测 **8** 个,exec 按实际动词列举写注释而未照抄该数字。
结果:2×P2 全部闭合,Task 6 勾选收口(参照 Task 1-5 先例,未单独再起一轮 re-review,由 Opus 核验修复内容与 Findings 处置意见一致后直接收口)。12×P3 记账留痕,其中 4 条已挂成后续任务的前置(#9→Task 13 十个语言包键、#15→Task 7/8 抽 `ReassignTaskOpBase`、Task 8 保持 `WfTask.Version` CAS 为第一个写操作、Task 7 补「委托过的任务照原 `DueTime` 到期」测试)。任务号 +1(→7),阶段回 plan。
NEXT: Round 26 — Task 7(超时 Job)plan。三条前置约束见 `## Status` 的「下一步」。
> ⚠ 上面这行是 renumber 之前写的:2026-08-25 用户裁决把「抽 `ReassignTaskOpBase`」升格为新 Task 7、超时 Job 顺延为 Task 8。Round 26 实际做的是新 Task 7(纯重构)。

### Round 26 — 任务7/plan + exec(纯重构) — 动作:`Agent(executor)` 一轮内做完 plan(写 `## Plan`,保留 `### 步骤 26` 与 Task 6 两个「必答问题」小节)与 exec。抽出 `Engine/Operations/ReassignTaskOpBase.cs`(abstract,157 行,承载 121 行动作序列 + 4 个 `protected` 只读属性 + 两个钩子),`TransferTaskOp` 从 149 行瘦到 22 行、`DelegateTaskOp` 26 行,**两者都继承基类、互为兄弟、无继承路径**——「委托 IS-A 转办」的假断言消失,review 指出的**上游方向风险**(往 `TransferTaskOp.ExecuteAsync` 加转办专属逻辑会静默变成委托行为,而你无法为尚不存在的 X 写「委托不该做 X」的测试)随之消除。三个类构造签名一律保持 `(WfTask, long, long, string?)` 不变 → 源码兼容;`WorkflowEngine` 的两个 `BeginXxxAsync` 各自 `new` 自己的 Op 这点也不变(Op 不走 DI)。**钩子裁定为 `abstract`**:①默认值 `=> WfTaskAction.Transfer` 写在基类上等于说「一次改派默认是转办」,正是本轮要拆的断言;②将来第三个兄弟(如超时自动转办)漏声明自己是谁会**编译失败**而非静默记成转办,把「静默污染」这个风险类别真正消掉;③代价为零(全仓零处构造基类),`ApproverProviderBase` 是现成同形先例;④两个 `override` 都不加 `sealed`,继承 `TransferTaskOp` 的消费者子类照旧能覆写。类级 XML 三件事都在:基类承载什么、兄弟俩的产品语义差异(转办=责任转移 / 委托=请人代办;独立端点是因为权限码即路由,合并端点等于让两种授权永远绑在一起)、以及「不是向后跳转、不进 `EnterNodeOp` 跳转下界白名单」的警告(`DelegateTaskOp` 原有那段原样保留,基类另有对两个动词都成立的通用版)。**验收线达成:143/143,一条不动、一条不加,未修改任何测试文件。****Opus 独立复核**(纯重构的核心风险是「搬家悄悄变重写」,故按等价性而非变异验证):①`git status`/`git diff --stat` 确认改动集只有 3 个 Op 文件 + 台账,协调者的 renumber 改动与未跟踪 `TestResults/` 均未被碰;②`rg` 确认两条继承子句都指向基类、两者之间无继承路径,两组钩子值与重构前逐字一致(转办 `Transfer`/`TransferTargetInvalid`=48010、委托 `Delegate`/`DelegateTargetInvalid`=48026);③**亲手逐字核验**——`git show HEAD:...TransferTaskOp.cs` 取重构前版本,旧文件第 28-148 行 vs 新基类 `ExecuteAsync` 起 121 行,trim 后 `Compare-Object -SyncWindow 0` → **121 行零差异**、首行也逐字相同,「移动而非重写」独立确认(这是本轮唯一真正需要证明的东西);④独立跑全量 **143/143**。exec 一处判断已核实合理并采纳:**本轮不拆 `ExecuteAsync` 的 125 行长方法**——虽与本仓「长方法拆小 virtual 步骤」教条不符,但拆步骤会把一次可逐行核对的搬家变成不可证伪的重写,而步骤边界是新的覆写契约、一经发布不好改;Task 9(CAS 收口)正要动其中的 `WfTask.Version` 认领段,由它来定这条缝更准。理由与建议切法已记入 `## Findings`,非定案。另记两条陈旧文档引用(`WfDelegateTests.cs` 两处 `<c>TransferTaskOp</c>`、`## 语义契约` 把 `alreadyActor` 查询记在 `TransferTaskOp` 名下),有意不在本轮改——改注释会污染「测试零改动」这条证据。
结果:Task 7 **一轮收口**(plan+exec 同轮;纯重构不单独起 review 轮:无新行为、无新测试,等价性已由 121 行逐字核验 + 全量套件双重证明)。任务号 +1(→8),阶段回 plan。
NEXT: Round 27 — Task 8(超时 Job)plan。四条前置约束见 `## Status` 的「下一步」,其中第三条(超时转办走直接 `new TransferTaskOp` 还是做第三个兄弟)是 plan 阶段要裁定的产品判断。

### Round 30 — 任务9/plan + exec — 动作:给 `WfInstance`/`WfToken` 加 `Version`(DefaultValue 0)、`WfExecutionContext` 上 `ClaimInstanceAsync`/`ClaimTokenAsync`、6 个状态落点领取、`BeginResubmitAsync` 把 ctx 上移并以 token 领取作本事务第一个写。`ReassignTaskOpBase` 只加注释、不改 CAS。新增 `WfVersionCasTests`(当时 7 条机制用例;后续 review 又加了会签首票 + 两条失败路径,现 10 条)。`WfTimeoutTests.Timeout_remind_does_not_block_human_action` 补实例/Token 版本不变量。exec 自报 172/172、12 个变异转红。**本轮未独立复核**(台账写「尚未 review」);Status 也没回写,一直停在 Round 29/plan。
结果:exec 代码在工作树,阶段实际已到 review,台账 Status 落后。
NEXT: Round 31 — Task 9 独立复核。

### Round 31 — 任务9/review + 收口 — 动作:以代码为事实源。P2-1 已在 `CompleteTaskOp` 落地。P2-2 测试已写,但生产 `ClaimTokenAsync` 残留 `MUTATION-M2`(throw 被删)。先带着变异跑 → `Resubmit_losing_token_cas` 红 `Expected: 48004 / Actual: 0`;复原 throw;P3-7 注释改成「ctx 没有 ICurrentUser」。指定过滤器 **59/59**。P3-1/P3-6 注释已在。0×未修 P1/P2。勾选 Task 9,阶段回 plan,写 Task 10 方案(缺口补测,不重写已有 HTTP 套件;4 条新用例预期 175→179)。
结果:Task 9 收口。任务号 +1(→10)。
NEXT: Round 32 — Task 10 exec。

### Round 32 — 任务10/exec — 动作:按 `## Plan` 做缺口补测,产品代码零改动。①复核 `WorkflowReplaceabilityTests` 八面与 `WorkflowSetup` 的 `TryAddScoped` 一一对应,不缺面,Findings 记「已复核」。②`WfDelegateTests` 两处 XML `TransferTaskOp`→`ReassignTaskOpBase`。③新增 `WfListContractTests.cs`:A 办完一单、B 有在途单,A 的 `GET task/done` 只有 A 的行、B 的 `GET instance/page` 只有 B 的实例;造用户一律 `orgId=1`。④`WfTimeoutTests` 加 Sequential AutoPass 两拍级联(第一拍 1 Approve + B 变 Pending + DueTime 仍在;第二拍再 1 Approve、实例 Approved)。⑤会签超时转办**现状快照**(只转 `actors[0]`、清整行 DueTime、B 仍 Pending;第二拍日志「无到期待办」)。禁止给命令加 `ExpectedVersion`,不碰 `web/`,不开始 Task 11+。指定过滤器 **179/179**(基线 175 + 4)。4 个变异全部转红后复原。Task 10 未勾选。
结果:exec 完成,阶段推进到 review。
NEXT: Round 33 — Task 10 review。

### Round 33 — 任务10/review + 收口 — 动作:独立跑指定过滤器 **179/179**;产品代码零 diff;亲手复跑 PageMine / Sequential DueTime 两条变异均转红后复原。0×P1/P2。勾选 Task 10,写 Task 11 方案。
结果:Task 10 收口。任务号 +1(→11)。
NEXT: Round 34 — Task 11 review(本轮同时 exec)。

### Round 33b — 任务11/exec — 动作:`IWfCcService`/`WfCcService`/`WfCcController`(`GET page`/`POST read`);`GetAsync` 准入后标已读;`CcNotFound=48027`;菜单 `RootId+22`;第九件套;`WfCcTests` 4 HTTP + 1 替换;`web/` 抄送列表(手写类型,等 Task 14 gen:api)。指定过滤器 **184/184**。两条变异转红后复原。Task 11 未勾选。
结果:exec 完成。
NEXT: Round 34 — Task 11 review。

### Round 34 — 任务11/review — 动作:读 Service/Controller/菜单/`GetAsync`/`WfCcTests`/`EnterCcAsync` 零 diff。亲手 5/5。`POST /cc/read` 去 UserId → 48027 变 0。`MarkMyCcReadAsync` 去 UserId → 详情用例仍绿。记 1×P2。
结果:不勾选 Task 11。
NEXT: Round 35 — 补「发起人打开详情不得误标他人抄送」测试。

### Round 35 — 任务11/exec 修 Findings — 动作:补 `Starter_opening_detail_does_not_mark_others_cc`。变异去掉 `MarkMyCcReadAsync` 的 `UserId==` → 红 `Expected: False / Actual: True`;复原后绿。指定过滤器 **185/185**。`rg MUTATION`/`REVIEW-PROBE` 零命中。P2-1 闭合。剩余 P3 不阻塞(Now / OnlyUnread·DefinitionId / 48027 i18n→14 / 无 read 按钮)。勾选 Task 11。未开 Task 12。
结果:Task 11 收口。任务号 +1(→12)。
NEXT: Round 36 — Task 12 plan(我发起的 / 我已办的,纯前端)。

### Round 36 — 任务12/plan+exec — 动作:先写完整 Task 12 Plan(D1–D11:不改后端业务、菜单 +24/+25 与 GET 按钮 +11/+12、typed `wfInstanceApi.page`、复用 `wfTaskApi.done` 与 `detail.vue` 状态数字表、无搜索栏、行点+查看进详情)。再落地:`WfInstanceListItem`、`wfInstanceApi.page`、`workflow/mine/index.vue`、`workflow/done/index.vue`、`workflow.mine`/`done` 中英、`WorkflowMenuSeed` 两菜单两按钮。`npm run typecheck` 绿、`npm run lint` 绿、`npx vitest run src/workflow/` 28/28。`rg MUTATION`/`REVIEW-PROBE` 零命中。:5100 探活超时,:5173 有响应但无后端,浏览器未走通。Task 12 未勾选。
结果:exec 完成,阶段留给 review。
NEXT: Round 37 — Task 12 review。

### Round 37 — 任务12/review + 收口 — 动作:独立复核,不信 Round 36 自报。`git status`/`diff --stat` 确认 Task 12 产品面是菜单种子 + Vue 两页 + types/api/i18n;`web-react/` 空;`Engine`/`WfTaskService`/`WfCc*` 无本任务 diff(工作树其它后端文件是 Task 10/11 残留)。亲手跑 `cd web && npm run typecheck`(exit 0)、`npm run lint`(oxlint exit 0)、`npx vitest run src/workflow/` **28/28**。读码:Mine fetcher=`wfInstanceApi.page`、openDetail=`r.id`、row-key=`id`;Done fetcher=`wfTaskApi.done`(签名未改)、openDetail=`r.instanceId`(schema 无 `id`)、row-key=`hisTaskId`;菜单 `RootId+24/+25` Path/Component 对得上 `views/workflow/{mine,done}/index.vue`(`authMenuRoute` 拼 `/src/views/${component}.vue`);`+11/+12` 无撞 `+1..+10/+20..+23`;设计器仍 `+23`/98/hidden。状态表 1–5 与 `WfEnums`/`detail.vue` 一致;动作 1–6 同 detail,`5→withdraw`。`rg MUTATION`/`REVIEW-PROBE` 排除 TestResults 与台账零命中。未做产品变异(读码即可证伪交叉数据源)。0×P1/P2。勾选 Task 12。
结果:Task 12 收口。任务号 +1(→13)。
NEXT: Round 38 — Task 13 plan。不要开 Task 13 exec。

### Round 38 — 任务13/plan+exec — 动作:先写完整 Task 13 Plan(D1–D10:last-visit cutoff、一次 GetAsync 载荷、新 `GET monitor`、EnsureParticipant 监控豁免、只读树、菜单 +26/+13、4 条新 HTTP)。再落地后端 DTO/Service/Controller/菜单/Fake + `WfReplayMonitorTests`;Vue 只读树 + 详情回放 + 监控页 + 交叉类型/`@ts-expect-error`。指定过滤器 **189/189**。`npm run typecheck` 绿、`npm run lint` 绿、`npx vitest run src/workflow/` **28/28**。`rg MUTATION`/`REVIEW-PROBE` 零命中。Task 13 **未勾选**。
结果:exec 完成,阶段留给 review。
NEXT: Round 39 — Task 13 review。不要开 Task 14。
