# TenonAdmin.Workflow 设计规划(基于 31 仓摸底)

> 文档入口：[`README.md`](./README.md)

日期:2026-08-17。前置材料:[`workflow-engine-research-2026-08-10.md`](./workflow-engine-research-2026-08-10.md)(调研)、本地参考库 `C:\HuHuHu\参考项目\工作流\SUMMARY.md` 与各仓 `_TENON_REF.md`。本文把调研收敛为**可开工的设计决策**;2026-08-17 经磨盘八问逐条裁决(见§十),2026-08-23 在不翻转审批内核的前提下把 AI Decision 上调为 M3b 战略交付。

## 〇、决策总览(一屏版)

| 维度 | 决策 | 主要依据 |
|---|---|---|
| 产品定位 | **AI 原生审批为核**：M1–M2 先完成可信人工审批链，M3a 建可靠机器节点，M3b 用 AI Decision 处理低风险审批、人工只接异常；不扩成通用自动化编排平台 | Slickflow 给出 AI 节点产品形状，OpenWorkflow 补可靠执行，Elsa 补 proposal 治理；三者组合但仍围绕审批 |
| 引擎 | **自研**,零第三方运行时依赖(只靠 SqlSugarCore + Microsoft.*) | 所有 .NET 候选要么许可不干净(WorkflowEngine.NET EULA、CCFlow GPL、AntFlow.net 附加条款),要么产品面错位(Elsa/WorkflowCore 是编排不是审批) |
| 归属 | **卫星包 `TenonAdmin.Workflow`**,不进内核 | 对照 ADR-0004 三条判据反着全中:没有内核功能把它当载体、表/页面体量大、"可选"名副其实;`TenonAdmin.Excel` 已趟平卫星包接线模式 |
| 流程定义 | **自研 JSON 钉钉树模型**(非 BPMN XML),后端为唯一 schema 源 | CCFlow 反证 + FlyFlow/Warm-Flow/FlowLong/AntFlow 全走这条线;bpmn-js 出局 |
| 运行时 | **Token + Agenda(单机简化版)** + **append-only 历史表** | Flowable 执行骨架 + Zeebe 思想单机落地(调研§三既定结论) |
| 表规模 | **9 张表冷启动**(见§三),对齐 Warm-Flow 7 表/FlowLong 8 表的克制路线 | 反例:AntFlow 80+ 表、Camunda ACT_* 数十张 |
| 审批动词 | 分里程碑铺开:M1 同意/拒绝/转办;M2 退回/撤销/委托/催办 + 会签/或签 + 超时;M3 加签/减签/拿回/比例票签 + 长期委托 | FlowLong 词汇表 = **路线图上限**,不是 MVP 清单 |
| 配置纪律 | 每种节点配置抽屉默认可见项 **≤5**,其余折叠「高级」;所有配置项必有默认值,什么都不点也能保存 | 功能强 ≠ 配置繁;验收线:请假流程 3 分钟不看文档建完发布 |
| 人员解析 | `IApproverProvider` SPI,复用现有 RBAC/机构树/数据范围,**零自带组织模型**;内置 **8 种**(含机构负责人,为此给 `SysOrg` 加 `LeaderUserId`) | CCFlow 自带 GPM 是反面教材(调研§四警戒线 1) |
| 超时 | 复用内核调度器(ADR-0004),注册一个 `IAdminJob` 扫描到期任务 | 不再造定时轮 |
| 通知 | 复用 Notice/SignalR(ADR-0003) | 不新建推送通道 |
| 前端交互 | **钉钉树状设计器,双模板各自自研**(Vue+Naive / React+antd),**MVP 不引画布库**;交付节奏:**Vue 先行打磨,schema 定稿后一次性 port 到 React(GA 前追平)**,纯 TS 校验/序列化逻辑写成框架无关 | StavinLi 交互母体;零共享铁律;设计器打磨期改一处=改两处,双做返工翻倍 |
| 表单 | M1 只做「审批 + 摘要变量 + 业务表单挂载点」;**M3 简易动态表单进包内**(~10 控件、单列、JSON 渲染)+ 字段权限矩阵(`formPerms`);schema 字段 **M1 即预留**。布局设计器/公式/联动/子表**永久不做** | 字段权限是表单与审批链的耦合点→表单能力做在包内不拆包;复杂表单走挂载点 |

## 一、包结构与接线

### 1.1 为什么是卫星包

用 ADR-0004"进内核"的三条理由反检:①工作流不需要"3 行 Program.cs 开箱即得"——要用审批的消费者装个包不是负担;②没有任何既有内核功能把工作流当载体;③表、错误码、双前端页面体量都远超调度模块,塞内核会显著加重不需要它的消费者。`TenonAdmin.Excel` 已经验证了卫星包的全套接线(独立 NuGet、自带端点、菜单种子取号规则),照抄即可。

### 1.2 包内分层(单包,内部分层,不拆多包)

```
TenonAdmin.Workflow  (引用 TenonAdmin.AspNetCore)
├─ Abstractions/   IWorkflowEngine、IApproverProvider、IWorkflowNotifier、
│                  IWorkflowFormBinder、WorkflowOptions、错误码常量
├─ Entities/       Wf* 实体(SqlSugar,DataEntity/BaseEntity 基类)
├─ Engine/         Agenda、Operation、各 Cmd(SubmitCmd/ApproveCmd/…)、
│                  条件求值器、会签计票器
├─ Services/       WfDefinitionService、WfInstanceService、WfTaskService(全 virtual)
├─ Providers/      内置 IApproverProvider 实现(用户/角色/岗位/机构/发起人自选/上级)
├─ Controllers/    api/v1/workflow/* (全部 [RolePermission],写操作 [OperationLog])
├─ Jobs/           WfTimeoutJob : IAdminJob(超时扫描)
└─ WorkflowSetup.cs  AddTenonAdminWorkflow():TryAdd 全家 + 实体入 CodeFirst
                     + 控制器 AddApplicationPart + 种子(菜单/字典,Id 走包保留段)
```

接线约定沿内核铁律:所有服务 `TryAdd*`、方法 `virtual`、模板方法拆小步;消费者在 `AddTenonAdmin()` 之前注册同接口即可整体替换(如换掉 `IApproverProvider` 接自家 HR 系统)。替换保证进"六件套"式测试(`WorkflowReplaceabilityTests`)。

### 1.3 错误码与种子

- 错误码:选一段未占用的内核保留段(建议 **48xxx**,开工前对照 `ErrorCode` 清单确认不撞),集中常量类;前端语言包 `error.code.<数字>` 双模板各补一份。
- 菜单/字典种子:按 `TenonAdmin.Excel` 的取号规则走包保留段;权限码即路由(`POST:/api/v1/workflow/task/approve` 之类),不写权限字符串。

## 二、流程定义:JSON 钉钉树 Schema(草案)

后端 `Wf` 模型是唯一权威,双前端各自消费同一 schema(通过 `gen:api` 拿到 DTO 类型)。**不采用 StavinLi 的数字 type 协议**,用自有可读枚举:

```jsonc
{
  "version": 1,
  "root": {
    "id": "n1", "type": "start", "name": "发起人",
    "props": { "initiatorScope": [] },          // 可发起范围(空=全员)
    "next": {
      "id": "n2", "type": "approval", "name": "部门审批",
      "props": {
        "assignee": { "provider": "leader", "params": { "level": 1 } }, // IApproverProvider 键
        "mode": "any|all|seq",                   // 或签/会签/顺序会签
        "allPassRatio": 100,                     // 会签通过比例(二期)
        "onReject": "terminate|toNode",          // 拒绝走向
        "returnPolicy": "prev|any|node",         // 退回策略
        "timeout": { "hours": 24, "action": "remind|autoPass|autoReject|transfer" },
        "nobody": "autoPass|transfer|block",     // 空审批人策略(三级可配:节点>流程默认>全局配置)
        "formPerms": []                          // 字段权限矩阵(M1 预留空数组,M3 启用)
      },
      "next": {
        "id": "n3", "type": "branch",            // 条件分支容器
        "conditions": [
          { "id": "n3a", "name": "金额>1万",
            "expr": { "field": "amount", "op": "gt", "value": 10000 },
            "next": { /* 子链 */ } },
          { "id": "n3b", "name": "默认", "isDefault": true, "next": null }
        ],
        "next": { "id": "n4", "type": "cc", "name": "抄送人事",
                  "props": { "assignee": { "provider": "role", "params": { "roleId": 0 } } },
                  "next": null }
      }
    }
  }
}
```

要点:

- `type` 分里程碑铺开:M1 只有 `start | approval | cc`(纯串行链);`branch`(排他分支)M2;`parallel`(并行)与 `webhook` M3;**包容网关永久不做**。节点类型体系走 SPI:消费者可注册自定义类型而不改内核。
- 条件表达式是**结构化 JSON**(字段/操作符/值 + and/or 组),不是脚本字符串——安全、可翻译、可在两端 UI 复原。变量来源:发起时提交的业务摘要字段(见§五表单边界)。
- 定义发布即**版本快照**:实例永远跑自己发布时的版本,改定义不影响在途(Warm-Flow/FlowLong 一致做法)。
- 定义级预留字段:`formSchema`(M3 简易动态表单的控件描述)与节点级 `formPerms`——**M1 就进 schema 与实体列**,避免 M3 加列迁移;M1/M2 始终存空值。
- **发布期校验节点引用完整性**(2026-08-24 落地):`onReject=toNode` 必须配一个合法的 `rejectToNodeId`,`returnPolicy=node` 必须配一个合法的 `returnToNodeId`——目标须存在于全树,允许跨分支臂与前向引用(按整树索引解析,不是只查已遍历过的节点)。不校验的后果是能发布出「该节点的拒绝动作永久不可用」的定义:运行到该节点点拒绝会抛 `ModelInvalid`、整事务回滚,而该错误码的含义(根非 start / 缺节点)完全看不出是配置问题。

## 三、数据模型(9 表冷启动)

| 表 | 基类 | 说明 |
|---|---|---|
| `wf_definition` | DataEntity | 流程定义:名称/图标/分组/状态/当前版本号;机构隔离 |
| `wf_definition_version` | BaseEntity | 版本快照:defId + version + **modelJson**(上文 schema)+ 发布时间;不可变 |
| `wf_instance` | DataEntity | 实例:defVersionId、businessKey、发起人、状态(运行/通过/拒绝/撤销/终止)、摘要变量 JSON;`CreateOrgId` 即数据范围锚点 |
| `wf_token` | BaseEntity | 运行 token:instanceId、nodeId、状态;一期线性≈每实例 1 活跃 token,并行网关启用后多 token,**表结构先到位** |
| `wf_task` | BaseEntity | 活跃待办:instanceId、nodeId、tokenId、签核模式、到期时间;完成即删(转历史) |
| `wf_task_actor` | BaseEntity | 任务办理人(1:N):taskId、userId、类型(审批/抄送)、状态;"我的待办"= 查本表 |
| `wf_his_task` | BaseEntity | 历史任务:动作(同意/拒绝/退回/转办/…)、意见、耗时;审批记录页数据源 |
| `wf_history` | BaseEntity | **append-only 事件流**:实例创建/节点进入离开/网关选择/超时触发…;审计 + 崩溃恢复推导(Zeebe 思想单机版) |
| `wf_cc` | BaseEntity | 抄送已读表(抄送不算待办,单独列表) |

不建的表与理由:变量不单独建表(摘要进 `wf_instance` JSON,业务数据在消费者自己的表,`businessKey` 关联——AntFlow 80+ 表的教训);委托关系先用任务级转办(M1)/委托(M2)动作而非"长期委托规则表"(FlowLong 式 entrust 规则表放 M3)。

## 四、运行时:Token + Agenda 单机版

```
Controller → WfTaskService.ApproveAsync()          (virtual,可覆写)
  → Engine.Execute(new CompleteTaskCmd(...))       (一次 DB 事务)
      Agenda 队列循环:
        CompleteTaskOp → 计票(或签一票通过/会签全票/顺序下一位)
        → TakeTransitionOp → 求值 branch 条件,token 移动
        → EnterNodeOp → approval:解析审批人建 wf_task(+actor)
                        cc:写 wf_cc + 通知
                        end:实例完结,回调 IWorkflowFormBinder
        每步追加 wf_history 事件
  → 事务提交后:SignalR 通知新办理人(ADR-0003)
```

- **扁平操作队列而非递归**(Flowable Agenda):退回/并行/中途持久化都不需要新解释器;每个用户动作 = 一条 Cmd = 一个事务,天然与 SqlSugar 单机模型契合。
- **崩溃恢复不需要重放**:状态就在表里;`wf_history` 是审计与排查用的投影副产品,不做 Temporal 式确定性约束(调研§三既定)。
- **超时**:`WfTimeoutJob : IAdminJob` 每分钟扫 `wf_task.DueTime`,按节点 `timeout.action` 派发 Cmd。多副本安全由调度器选主保证(ADR-0004),工作流自己不管分布式。
- **多副本**:引擎无内存状态,所有动作走 DB 事务 + 乐观并发(task 状态 CAS),两副本同点一个任务只有一个成功——沿用内核既有模式,不需要新锁。

## 五、表单与业务绑定(范围最重要的一刀)

两条腿并存,面向两类人:

1. **开发者腿(M1)——挂载点**:
   - **发起摘要字段**:定义里声明少量条件变量(金额/天数/类型…),发起时随实例提交,存 `wf_instance` JSON——够 branch 条件用。
   - **`IWorkflowFormBinder`(TryAdd,可替换)**:消费者实现"发起时校验业务单据 / 完结时回写业务状态";前端定义里存 `formComponent`(消费者页面路径),审批详情页动态挂载消费者自己的表单组件。
2. **业务管理员腿(M3)——简易动态表单,做在包内不拆包**:~10 种控件(文本/数字/金额/日期/单多选/人员/附件…)、单列排布、JSON schema 渲染,让不写代码的管理员建"表单+审批"流程;审批节点上按字段配可见/可编辑(`formPerms`)。**做在包内的原因**:字段权限是表单与审批链的耦合点,拆包会把耦合变成跨包契约。

**永久不做**:布局设计器、公式/联动、子表——要这些的场景走挂载点自写表单页。这条线挡住 CCFlow"表单引擎与流程引擎绑死"的复杂度,也躲开 wflow/jsonflow"设计器开源、引擎商业"的陷阱。

## 六、前端设计(UI 选型的最终答案)

### 6.1 结论:钉钉树自研,MVP 零画布依赖

- 审批流的国内用户心智是**钉钉/飞书树**,不是自由图(31 仓摸底一致指向);树状 UI 就是递归组件 + 抽屉,自研成本可控(StavinLi 全套才 20 来个文件)。
- 因此 **MVP 不引 LogicFlow/vue-flow/xyflow/bpmn-js 任何一个**。它们解决的是"自由拖拽连线"问题,而钉钉树根本没有自由连线。
- 二期若真要"经典流程图模式"(Warm-Flow 双模式那种),Vue/React **各自**接 LogicFlow(Apache-2.0,一库两端最省心智);vue-flow/xyflow 组合作备选。bpmn-js 除非出现"必须与 Camunda Modeler XML 互通"的硬需求,否则永久出局(水印条款 + 心智错位)。

### 6.2 双模板各自实现(零共享铁律)

| | `web/`(Vue3 + Naive) | `web-react/`(React19 + antd6) |
|---|---|---|
| 设计器递归树 | `WfNodeTree.vue` 递归组件 | `<NodeTree>` 递归渲染 |
| 节点配置 | `NDrawer` 抽屉(审批人/条件/抄送) | `Drawer` 同构信息架构 |
| 添加节点 | `NPopover` 四选一 | `Popover` |
| 人员选择 | 复用现有用户/角色/机构选择组件 | 同 |
| 审批进度 | `NTimeline` | `Steps`/`Timeline` |
| 待办列表 | ProTable | DataTable |
| 按钮权限 | `v-auth` | `<Can>` |

交互语言学 StavinLi(缩放条、节点错误红点汇总、条件分支横排),**DOM/CSS/协议一行不拷**(该两仓无 LICENSE 文件,拷贝有版权风险;且数字 type 协议劣于自有 schema)。设计器产出的 JSON 即§二 schema,两端序列化结果必须逐字节一致(加一条双端 schema 快照对拍测试)。

**交付节奏(磨盘问题 7 定案)**:设计器 **Vue 先行**——M1/M2 只做 `web/`,打磨期频繁改交互不必两边返工;JSON schema 与交互定稿后,GA 前一次性 port 到 `web-react/`。校验/序列化等纯 TS 逻辑从第一天写成框架无关(不 import 任何 Vue/React API),port 时直接复制。运行时页面(待办/详情/操作)交互简单,双模板照常同步。无框架 npm 共享包方案已评估否决:画布只占设计器约 30% 工作量,占 60% 的节点配置抽屉全是表单交互,无法框架无关化,强行共享等于多维护一个第三方包还得桥接两套 design tokens。

### 6.3 页面清单(菜单管理 UI 配置,不写路由代码)

- 流程管理:定义列表 / 设计器 / 版本历史(管理员)
- 审批中心:待我审批 / 我发起的 / 我已办的 / 抄送我的(`[ActiveSession]` 端点)
- 审批详情:进度时间线 + 意见记录 + 业务表单挂载点 + 操作按钮组

## 七、API 面(草案,权限码即路由)

```
POST /api/v1/workflow/definition/add|update|publish|disable
GET  /api/v1/workflow/definition/page|{id}|versions/{id}
POST /api/v1/workflow/instance/start          // businessKey + 摘要变量
POST /api/v1/workflow/instance/withdraw/{id}  // 撤销(仅当无任何节点被审批过)
GET  /api/v1/workflow/instance/page|{id}|history/{id}
GET  /api/v1/workflow/task/todo|done|cc       // [ActiveSession]
POST /api/v1/workflow/task/approve|reject|return|transfer|delegate  // 各带意见
```

裸返回 DTO 走信封过滤器;业务错误抛 `AdminException`(48xxx 段)。

## 八、里程碑(磨盘问题 8 定案)

切分原则:**每个里程碑结束都是可发布、可演示的完整产品,不是半成品**。

| 阶段 | 后端 | 前端 | 验收线 |
|---|---|---|---|
| **M1 能走通一单** | 包骨架(TryAdd/virtual/CodeFirst)+ 9 表 + JSON schema v1(`formSchema`/`formPerms` 预留)+ 引擎核心(token 推进、串行)+ 动词:同意/拒绝/转办 + 8 种 Provider + SPI + `SysOrg.LeaderUserId` + 定义发布/版本 + 发起/待办/已办/详情 API + 历史事件流 | 仅 Vue:设计器 MVP(串行链:审批+抄送节点)+ 配置抽屉 + 发起页/待办列表/详情页(含 `IWorkflowFormBinder` 挂载点) | "请假审批"3 分钟不看文档建完、发布、走通一单;四库 CI 绿;`WorkflowReplaceabilityTests` 六件套 |
| **M2 正经审批产品** | 排他分支(结构化条件 + 可视化编辑器)+ 会签(一票否决)/或签(先表态即定局)+ 动词:退回(可配目标)/撤销/委托/催办 + 超时策略(`IAdminJob`:提醒/自动通过/自动拒绝)+ 空审批人三级可配 + 同一人去重 + SignalR 通知 | 仅 Vue:分支容器 + 条件编辑器 + 抄送独立列表 + 流程图回放(高亮已走路径) | 钉钉上一个典型报销流程(条件分支+会签+超时提醒)1:1 复刻;CCFlow 行为清单逐项过 |
| **M2c 可靠性收口** | 所有写命令增加 `RequestId/IdempotencyKey` + 同事务操作回执；通知失败日志/指标；把超时领取、CAS、事务回滚和回执唯一性收成四库共享契约测试 | Vue 在一次提交生命周期内生成并复用 request key；刷新双前端 API 类型，但不提前 port React 工作流页面 | 同一请求串行/并发重放均返回第一次结果；超时与人工动作竞争只允许一个胜出；四库 CI 全绿 |
| **M3a 通用性拉满 → GA 基石** | 简易动态表单(~10 控件)+ 字段权限矩阵 + 动词封顶:加签/减签/拿回/比例票签 + 长期委托规则 + 并行分支(多 token)+ Webhook 节点 + 可靠自动节点执行 Module（execution/attempt/deadline/retry/fence/outbox）+ 节点类型 SPI 对外文档化 | 表单设计器(单列)+ **React 模板整体 port** | **基础 GA 门槛:双模板 feature 对齐 + 文档站 guide 上线；远程节点无长事务、崩溃可恢复、同一 execution 只推进一次** |
| **M3b AI Decision v0** | 以 M3a Seam 接入 AI Decision Adapter：OpenAI-compatible + fake Provider、结构化 proposal、schema/policy、shadow mode、低风险自动放行、人工 fallback、审计/脱敏/限额 | 双模板 AI 节点配置、proposal/证据/策略结果审计视图 | AI 不直接写任务状态；无效输出/低置信度/风险/异常全部转人工；场景评测达标后才能由 shadow 切自动放行 |
| **M3+ AI 扩展/按需** | 证据与 RAG Adapter、只读 Agent tools、更多 Provider、评测集/灰度策略；AI 设计 Copilot、子流程、经典图模式、统计报表按真实需求进入 | — | AI 自动拒绝和写工具不进入首版；每项扩权单独做安全与审计验收 |

## 九、调研§九开放问题——决议

| 问题 | 决议 |
|---|---|
| BPMN XML 互操作? | **不做**。钉钉树 + 自有 JSON;bpmn-js 出局 |
| 网关范围? | 排他 M2;并行 M3(token 模型 M1 预留);**包容网关永久不做**;子流程 M3+ 按需 |
| 任务分派? | Provider SPI 复用 RBAC/机构/数据范围;内置 **8 种**(含机构负责人,`SysOrg` 加 `LeaderUserId`),消费者可换 |
| 通知? | 复用 Notice/SignalR,零新通道 |
| 表单? | M1"审批+摘要+挂载点";M3 简易动态表单**进包内**(字段权限耦合决定不拆包);布局/公式/子表永不做 |
| 前端形态? | 钉钉树自研×2;双模板不共享;**Vue 先行,React GA 前追平**;经典图模式二期评估 LogicFlow |

## 十、磨盘八问定案(2026-08-17,均经用户裁决)

| # | 问题 | 裁决 |
|---|---|---|
| 1 | 产品定位:审批 or 编排? | **AI 原生审批为核**；Webhook(M3a)+AI Decision(M3b) 都走节点 SPI；机器处理低风险、人处理异常；完整通用编排平台仍不做 |
| 2 | 功能丰富 vs 配置简单? | FlowLong 动词全集 = **路线图上限**分三期铺开;配置纪律:每节点默认可见项 ≤5、全部有默认值;验收线 = 请假流程 3 分钟建完 |
| 3 | 表单怎么整合? | 双腿并存:M1 挂载点(开发者)、M3 简易动态表单进包内(业务管理员);`formSchema`/`formPerms` M1 即预留;布局/公式/联动/子表永久不做 |
| 4 | 建模能力边界? | 排他分支 M2、并行 M3、**包容永不**、子流程 M3+ 按需 |
| 5 | 内置审批人 Provider? | **8 种**:指定成员/直属主管(N 级)/连续多级主管/角色/职位(可限机构)/发起人自选/发起人本人/机构负责人——最后一种要求 `SysOrg` 新增 `LeaderUserId`;HRBP 等私有逻辑走消费者自注册 |
| 6 | 行为语义默认值? | 见下表;空审批人策略**三级可配**(节点>流程默认>全局配置),出厂默认自动通过 |
| 7 | 设计器双模板成本? | **Vue 先行打磨,schema 定稿后一次性 port 到 React(GA 前)**;纯 TS 逻辑框架无关;无框架共享包否决(配置抽屉占 60% 工作量,无法框架无关化) |
| 8 | 里程碑切分? | 见§八；M2a/M2b 后先做 M2c 可靠性收口，再进 M3；Webhook 留在 M3 不提前 |

### 行为语义默认值(问题 6 细则,M2 写代码前的既定答案)

| 场景 | 默认行为 | 可配项 |
|---|---|---|
| 拒绝后 | 整个流程终止 | 节点可配"退回到指定节点" |
| 退回后重新提交 | 从头重走 | 可配"从退回节点继续,已过节点不再审" |
| 发起人撤销 | 仅当无任何节点被审批过 | 审批人"拿回"是 M3 动词 |
| 抄送 | **不算待办**:独立"抄送我的"列表 + 已读标记,不催不超时 | — |
| 审批人解析为空 | 自动通过(出厂全局默认) | 三级可配:自动通过 / 转指定人 / 卡住通知管理员 |
| 同一人相邻节点 | 自动通过后一次(去重)。**去重基线只认最近一次向后跳转之后的批准记录**——拒绝路由 / 主动退回 / 退回重提都重置基线,跳转之前批过的节点在回退后必须重新审(2026-08-24 定案,见下方「向后跳转重置去重基线」) | 节点可配"仍需重复审批" |
| 委托(一次性) | **仅当前 Pending 办理人可委托**,实例发起人无权委托他人待办;被委托人拿新 actor(原 actor 翻 `Skipped`),同一 `taskId` 换人而非新建待办;不重置 `DurationMs` 基准与 `DueTime` | — |
| 超时提醒频率 | `Remind` 可重复触发,**最小提醒间隔默认 = 该节点自己的 `timeout.hours`(下限 1 小时)**,即「配 24 小时超时的节点每 24 小时催一次」。判据取本(实例, 节点)上最近一条 `TimeoutFired` 事件的时间,不新增列 | `TenonAdmin:Workflow:TimeoutRemindMinIntervalHours` 全局覆盖(0 = 跟随节点);或覆写 `WfTimeoutJob.ShouldRemindAsync` 换节奏(「只提醒一次」是它的第一个用例)——**注意**:覆写子类后还须把 `sys_job` 中该行的 `HandlerName` 改成子类全名,否则调度器仍选中基类、覆写不生效 |
| 超时动作的动作主体 | 超时触发的自动动作一律**以当前 Pending 办理人身份记原生动词**(`Approve`/`Reject`/`Transfer`),不新增「超时专用」的 `WfTaskAction` 值;真相由同事务的 `TimeoutFired` 事件 + `Comment` 说明。机制约束:`CompleteTaskOp` 的 actor 认领是 `WHERE UserId=@caller AND Status=Pending`,系统账号必然认领不到,换身份要松掉「仅本人可办」这条承重校验 | 将来若要区分人/机器动作,补法是加一个可空列(如 `IsAuto`,旧行回填),比持久化枚举值可逆 |
| 链式委托 | 允许 A→B→C,不设次数/深度上限;委托回本待办任何参与过的人会被拒(`alreadyActor` 校验只看 actor 行存在性、不看状态),故环路天然封死 | — |
| 会签中一人拒绝 | 一票否决立即拒 | 比例票签是 M3 动词 |
| 或签中第一人表态 | 先表态即定局(先拒即拒/先过即过),其余任务自动取消 | — |

#### 向后跳转重置去重基线(2026-08-24 定案)

「同一人相邻节点去重」的「紧邻的上一个已完成审批节点」**只在 token 单向前进时有定义**。M2b 的拒绝路由(`onReject=toNode`)、主动退回和退回重提都会让 token 向后跳,而跳转目标往往正是最近一条批准记录所在的节点——若沿用它当基线,回退目标会被判成「已审过」而整节点自动通过:拒绝路由退化成把待办原样弹回拒绝人(可无限循环),重提的「从头重走」会跳过已批节点。两者都与本表既有定案相反。

因此:**任何向后跳转都重置去重基线,跳转之前的批准记录不再参与比对**。「重走就是真重走,不因为上次这个人批过而静默跳过」与本表「退回后重新提交 → 从头重走」是同一条语义的两面。对正向推进的去重行为零变化。

实现取的是同表下界(在 `wf_his_task` 内按 `Id` 倒序,遇到最近一条 `Reject`/`Return` 行即截断),不跨表比较雪花 Id;`RejectRouted`/`TaskReturned` 两个历史事件类型用于审计与流程图回放,不作下界数据源。**流程图回放(M2b 后半程)必须按最后一次节点访问收敛**,否则会把回退前后的路径一起点亮。

## 十一、风险与警戒线

1. **许可污染**:参考库任何源码不入仓;CCFlow 连算法细节都不读(GPL 衍生风险);StavinLi 无 LICENSE,只学交互。
2. **范围膨胀**:动态表单、长期委托、并行网关、统计大盘全部立牌"二期"——FlowLong 企业版功能清单是**词汇表不是排期表**。
3. **组织模型越界**:Workflow 包内出现任何"自己的用户/部门/权限"概念即回退重设计(CCFlow 教训)。
4. **CI 成本**:新增 9 表进 CodeFirst,SqlServer leg 的 per-DB 建表成本会再涨——新表控制数量本身就是缓解;必要时把 Workflow 专属测试并入 nightly 全量而非 PR 子集。
5. **双模板漂移**:设计器 JSON 双端对拍测试进 CI(各自 `npm test`),防止两端 schema 悄悄分叉。

## 十二、JNPF 增量(2026-08-18,不改定稿)

本地对照:`../参考项目/工作流/jnpf/_TENON_REF.md`;调研第六节。**不翻转**§〇/§九/§十。只补三条 M2/M3 对照:

1. **发起范围**是定义级能力(全员 / 指定角色),不要为此新建授权表。  
2. **审批页按钮**跟字段权限一样挂在节点 JSON(`btnInfo`),与角色菜单按钮分家;默认值仍遵守「每节点可见项 ≤5」。  
3. **实例列表按参与**(发起/待办/已办/抄送/监控),组织数据范围继续只管业务实体,不要拿来滤 `WfInstance`。

连续多级主管若运行时现查 `DirectorId`:M2 须写死「发起时快照 vs 实时」,JNPF 选择发起时拍 `workflow_launch_user`。

**M1 专项复核(结论:无新增)**:对照已完成的 M1 代码(8 个 `IApproverProvider`、三级空审批人策略、串行节点 schema、同意/拒绝/转办动词集)逐项核对 JNPF 全部材料(`OperatorEnum`/`RecordEnum`/`ErrorRuleEnum`/`NodeModel` 等)——JNPF 没有 Tenon M1 缺失的基础能力:其审批人类型与 Tenon 8 种 1:1 对应,独有的 VARIATE/LINK/SERVE 分别依赖动态表单/多节点历史/webhook,均已按既定分级挂在 M2/M3/永不做;空审批人更细的选项、签收、转审等均已在既有分级或本节其余条目覆盖。M1 范围不变。

## 十三、M2 开工定案(2026-08-18,经用户裁决)

不翻转 §〇/§八/§九/§十;本节只把 M2 开工前必须写死的三条定下来,并按 §八「每个里程碑结束都是可发布、可演示的完整产品」把 M2 切成两个各自可发布的半程。

### 13.1 M1 已超交付(§八 M2 行里已经不必再做的)

对照已落地代码,§八 M2 列的三项在 M1 就已完成,**不重复排期**:

| §八 列在 M2 | 真实状态 |
|---|---|
| 空审批人三级可配 | 已完成:`EnterNodeOp.ResolveNobody`(节点 > 流程 > 全局 `WorkflowOptions.Nobody`) |
| 会签一票否决 / 或签先表态即定局 | 引擎已完成:`CompleteTaskOp.TryPassAsync` 三态计票(Any/All/Sequential),拒绝走 `skipRemaining`。**缺的只是设计器没暴露 `props.mode`** |
| 发起范围 | 已完成:`initiatorScope` + `InitiatorNotAllowed`(48016)。JNPF 增量第 1 条已满足 |

同理,`WfBranchArm`/`WfConditionExpr`/`WfTimeout`/`WfReturnPolicy` 的 schema 空壳、`wf_task.DueTime` 列、`WfTaskAction` 的 M2 枚举值、引擎里累积却无人消费的 `ctx.NewAssigneeUserIds`/`NewCcUserIds`——**M2 大量工作是把 M1 预留的插头插上,不是新建**。

### 13.2 三条写死

| # | 问题 | 裁决 | 理由 |
|---|---|---|---|
| 1 | 连续多级主管(`multiLeader`)何时解析主管链? | **发起时快照**:发起瞬间沿 `SysUser.DirectorId` 链拍平存进实例,之后组织调整不影响在途单 | 与 JNPF 一致(拍 `workflow_launch_user`);审批链可预测、发起页能提前展示「将由谁审」、排查简单。代价(发起后主管离职需人工干预)可接受 |
| 2 | 待办通知落地成什么? | **只推 `IRealtimePublisher`,不落 `SysNotice`** | 待办本身已在 `wf_task` 且有独立待办列表,再存一份站内信是重复数据,还要为「待办已办完」同步清理站内信状态。§〇「复用 Notice/SignalR,零新通道」的约束是**不新建通道**,不是两个都用 |
| 3 | M2 交付切分? | **切成 M2a / M2b 两段,各自可发布可演示** | 见 13.3 |

### 13.3 M2a / M2b 切分

| 段 | 后端 | 前端(仅 Vue) | 验收线 |
|---|---|---|---|
| **M2a 分支** | 结构化条件求值器 + `branch` 节点执行(`EnterNodeOp` 现在 `default:` 直接抛 `NodeTypeUnsupported`)+ `GatewayTaken` 事件 + `multiLeader` 主管链改发起时快照(13.2 #1,小而独立,先锁语义免得后续代码依赖旧行为) | `model.ts` 由串行链改树(`flattenChain`/`insertAfter`/`removeNode`/`validate` 全部要处理分支臂)+ `WfNodeTree.vue` 分支容器横排 + 条件编辑器 + 配置抽屉暴露 `mode`(会签/或签/顺序) | 报销流程「金额>1万 走总经理,否则直接通过」建完发布走通两条臂;会签一票否决、或签先表态各走一单 |
| **M2b 动词与时效** | 退回(`returnPolicy` + `onReject=toNode`)/撤销/委托/催办 + `WfTimeoutJob : IAdminJob` + 写 `wf_task.DueTime` + 同一人相邻节点去重 + `IWorkflowNotifier` 接 `IRealtimePublisher` + 节点 `btnInfo`(JNPF 增量第 2 条) | 抄送独立列表 + 我发起的 / 我已办的 + 流程图回放(高亮已走路径)+ 实例列表按参与筛选(JNPF 增量第 3 条) | 钉钉典型报销流 1:1 复刻(条件分支 + 会签 + 超时提醒);退回后重提、发起人撤销各走一单;CCFlow 行为清单逐项过 |

**先 M2a 的理由**:M2a 是 M2 里唯一改 schema 形状的一段(树化 + 条件),定了才谈得上 §六.2 的「schema 定稿后一次性 port 到 React」;M2b 全是加动词加页面,对 schema 只增不改,压在后面更稳。

**警戒线**:M2a 的 `model.ts` 树化会动 M1 刚修好的设计器(`cloneModel`/`cloneNode` 的 reactive-proxy 坑见 `.loop/wf-m1-close.md` Round 2),改完必须重跑一遍真实浏览器全链路,不能只信 `npm run typecheck`——M1 三轮 typecheck 全绿也没发现「添加节点」是坏的。

## 十四、OpenWorkflow 增量与 M2c 定案（2026-08-23）

对照报告：[`openworkflow-reference-2026-08-23.md`](./openworkflow-reference-2026-08-23.md)。本节不翻转 JSON 审批树、Token/Agenda、版本快照和“AI 原生审批为核”的既有决策，只补执行可靠性及其开发落点。

### 14.1 为什么不塞进 M2b

M2b 正在收口审批动词、超时和 Vue 产品面。通用请求幂等会同时改命令 DTO、HTTP API、数据库唯一约束、双前端生成类型和并发测试；混入 M2b 会扩大回归面，也会把“功能是否做完”和“重复请求是否可靠”两个验收目标搅在一起。

M2b 只吸收与现有任务天然重合的两项：

1. `WfTimeoutJob` 使用 `taskId + Version + DueTime <= now` 条件更新，CAS 失败表示人工动作或其他执行者已经胜出；写 `TimeoutFired` 历史，不建另一套 worker/lease。
2. `IWorkflowNotifier` 继续事务提交后调用，但失败必须写结构化日志与指标，不能静默无痕。SignalR 只是刷新提示，`wf_task` 才是事实源，因此此时不建 outbox。

### 14.2 M2c 交付内容

1. 新增操作回执存储（表名在实现任务定稿），唯一 identity 至少包含组织/租户、命令类型、目标实例或任务、操作者、客户端 request key；回执与领域状态在同一事务落库。
2. `Start/Approve/Reject/Transfer/Return/Cancel/Resubmit` 等所有写命令接收 `RequestId/IdempotencyKey`。第一次已提交但 HTTP 响应丢失时，重试返回第一次 `WfEngineResult`，不再只报 `TaskConflict`。
3. Vue 在一次提交生命周期内持有同一个 request key；用户明确发起一个新动作才生成新 key。按钮防连点仍保留，但不把 UI 防抖当并发正确性。
4. 建 provider-neutral 工作流持久化契约测试，同一套用例跑 SQLite、MySQL、PostgreSQL、SQL Server，覆盖回执唯一性、并发 CAS、事务回滚、超时与人工动作竞争、终态保护。
5. M2c 不新增审批动词、页面或通用 Backend Interface。幂等回执是现实 Seam；照搬 OpenWorkflow 二十多个 Backend 方法会形成 hypothetical Seam。

### 14.3 M3 与 AI 的落点

详细源码结论与目标架构见 [`elsa3-slickflow-ai-reference-2026-08-23.md`](./elsa3-slickflow-ai-reference-2026-08-23.md) §3–§5。后续实现以该报告为基线，不因开发 AI 节点再次通读 Slickflow 固定提交。

M3 分成连续的 M3a/M3b。M3a 在 Webhook/自定义节点引入可靠机器动作执行 Module：稳定 execution key、持久化 attempt、指数退避、deadline、ownership fence、输出/错误摘要。Webhook 等必须执行的外部副作用使用事务型 outbox/attempt；当前 SignalR 提示不一刀切持久化。所有入口只跨同一个节点 Interface，禁止复制 Slickflow 两套执行链。

AI 分成两条互不混用的能力线：

1. **运行时 AI Decision（Slickflow.AI 对照，M3b）**：在 M3a 节点 Interface 稳定后立即交付最小纵切，不再列为无承诺的 M3+。模型只能返回受 JSON schema 约束的 proposal（结论、分数、理由、证据引用）；服务端确定性 policy 再决定低风险自动放行或创建人工任务。V0 不自动拒绝。低置信度、风险标记、模型异常、超时、结构化输出校验失败一律转人工。模型不得直接调用 Approve/Reject，也不得在远程模型调用期间持有工作流数据库事务。
2. **AI 流程设计/诊断 Copilot（Elsa 3 Weaver 对照）**：这是设计器/运维能力，不是执行节点。AI 生成或修改模型时只创建 proposal，经过权限过滤、脱敏、schema 校验、图 diff、用户 approve/apply 和完整 audit 后，才能写入定义草稿；不得直接发布流程或修改在途版本。

两条线都不把模型 SDK、Prompt 或供应商配置耦合进 `TenonAdmin.Workflow`。Provider 类型留在 Adapter；审计只保存必要的模型/Prompt/schema/policy 版本、输入摘要、证据引用、结构化输出、耗时与 token/费用统计，敏感业务字段按配置脱敏。M3b AI Decision v0 是 AI 原生路线的战略交付；RAG、Agent、更多 Provider 与设计 Copilot 后置到 M3+。

## 十五、数据库评审增量与 M3a 切片定案（2026-08-24）

对照报告：[`workflow-database-design-review-2026-08-24.md`](./workflow-database-design-review-2026-08-24.md)。本节不翻转 §〇/§八/§十四 的既有决策，只把评审结论落成排期与契约裁决。

### 15.1 两条契约性裁决

1. **`WfInstance.Version`/`WfToken.Version` 提前到 M2b 收口，不等 M2c。** M2b 刚交付的撤销、催办与超时正是“审批 vs 撤销”“超时 vs 人工”竞争的高发区；字段是可回填的增量迁移（旧行回填 0），早一个里程碑落地，M2b 的竞争测试直接建立在实例/Token 级 CAS 上，M2c 不必重写。四库契约测试仍随 M2c 收口。
2. **`IdentityHash` 构造规则是发包后不可逆的契约。** 字段顺序、分隔符、大小写、null 归一化和哈希算法在 M2c 首个实现里一次定死，之后只增不改；细则与快照用例要求见评审 §五。

### 15.2 M3a 切成 M3a-1 / M3a-2，只有 M3a-1 挡 M3b

§八 的 M3a 行把可靠执行层与产品面装在同一个里程碑里，但 M3b 的唯一前置是可靠执行层。切分如下：

| 段 | 内容 | 定位 |
|---|---|---|
| **M3a-1 可靠执行** | execution/attempt/outbox/lease/fence、`NodeVisitId` 贯穿 Token/任务/历史/抄送、`wf_history` 补 Token/序号/actor/payload version、Webhook 节点 | **M3b 的唯一前置**；本身即可独立发布 |
| **M3a-2 产品面** | 简易动态表单 + 字段权限矩阵、动词封顶（加签/减签/拿回/比例票签 + 长期委托）、并行分支（多 token）、React 模板整体 port | 与 M3b 并行推进，只挡 GA |

依赖关系：`M2c → M3a-1 → M3b`；`M3a-2` 与 `M3b` 并行。GA 门槛不变（双模板 feature 对齐 + 文档站 guide + 远程节点无长事务/崩溃可恢复/同一 execution 只推进一次）。

### 15.3 Webhook 按一等功能交付

Webhook 节点不只是给 AI 铺路的试金石——“审批完结后可靠回调业务系统”本身是消费者高频刚需。M3a-1 已交付带可靠执行闭环和权威文档的正式 Webhook 节点类型，使它成为可发布、可演示的独立里程碑，符合 §八“每个里程碑结束都是可发布的完整产品”原则。Webhook 设计器 UI 仍属于 M3a-2，不在本轮范围内。

#### Task 8b 生产闭环（已实现）

Task 8b 将 Webhook 接入真实流程入口和既有后台调度体系，生产时序固定为：

```text
EnterNodeOp 进入 Webhook
  → 同一工作流事务内生成 NodeVisitId 并 Ensure 唯一 WfNodeExecution(Pending)
  → sys_job: wf-node-execution-scan 触发 WfNodeExecutionJob
  → worker 领取 Pending / 到期 RetryScheduled / 过期 Running
  → WfNodeExecutionDispatcher 在事务外调用 handler
  → fence/CAS 回写 attempt、execution、history、outbox 和 Token
```

入口只创建或幂等复用 execution，不在数据库事务内发送 HTTP。worker 是现有 `IAdminJob`/`JobExecutor`/`JobSchedulerService` 的一个编译类任务，不新增通用 worker fleet；固定种子每 5 秒触发，扫描批量由 `TenonAdmin:Workflow:NodeExecutionScanBatchSize` 控制（默认 20、最大 1000），execution 租约由 `TenonAdmin:Workflow:NodeExecutionLeaseSeconds` 控制（默认 300 秒、最大 3600 秒），实际单赢家仍由 dispatcher 的 lease/fence CAS 决定。

自动节点的总尝试次数（含首次执行）来源按以下优先级解析：节点 `props.maxAttempts` → `TenonAdmin:Workflow:MaxAttempts` → 内置默认 3。全局和节点值必须在 `[1,100]` 内，并在 execution 创建时固化；配置后续变化不影响既有 execution。未知非取消 handler 异常由 dispatcher 转成受控 `RetryableFailure/48032`，只把异常类型写进安全摘要，完整异常进入结构化日志；`OperationCanceledException` 仍原样传播。

领取后如果 instance、token、definition version 或模型/节点快照已经永久缺失或损坏，dispatcher 不把这类数据错误当成可无限续租的暂时故障，而是发出内部 quarantine command。引擎在同一 tx2 中用旧 `Fence + Running` CAS 将 execution 置为 `Failed`、清除 lease、追加 terminal attempt 并幂等写入一条 `Pending` outbox；该旁路不读取缺失上下文、不推进 Token。数据库/基础设施瞬时异常仍原样退出，保留 lease 到期后的恢复路径。

外部 Webhook 副作用的交付语义是 at-least-once：租约过期或 tx2 提交前崩溃都可能导致同一请求再次发送。消费者必须使用 `ExecutionKey` 作为幂等身份；本地 workflow 状态通过 fence/CAS 保证同一 execution 的 Token 最多推进一次。Task 8b 只在 execution 终态提交 `Pending` outbox，`Dispatching/Dispatched/Failed` 的消费、重投和传输闭环延期到 Task 8c。

### 15.4 M3b 自动放行默认关闭，阈值校准是消费者的责任

TenonAdmin 以内核包分发，自身没有生产流量，shadow mode 的评测数据只能来自消费者各自的部署。因此：

- 内核交付**机制**：shadow 记录、指标采集（人工推翻率、逃逸率、schema 失败率、fallback 率、provider 延迟与成本）与现成审计视图；
- 自动放行**默认关闭**，按流程定义显式开启；内核不提供“开箱即用”的放行阈值默认值；
- 消费者必须先在自己的数据上跑 shadow 达标，“何时由 shadow 切自动放行”是每个部署自己的决定；
- 模型自报 `confidence` 只作记录与事后评测，不单独作为放行条件——policy 主判据是可确定性核验的 `reasonCodes`、`riskFlags` 与证据完整性（细则见 AI 基石 §4.2、§4.7）。
