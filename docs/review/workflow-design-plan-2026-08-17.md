# TenonAdmin.Workflow 设计规划(基于 31 仓摸底)

日期:2026-08-17。前置材料:`docs/review/workflow-engine-research-2026-08-10.md`(调研)、本地参考库 `D:\胡国东\家宽服务器\参考项目\工作流\SUMMARY.md` 与各仓 `_TENON_REF.md`(摸底笔记)。本文把调研收敛为**可开工的设计决策**;2026-08-17 经磨盘八问逐条裁决(见§十),**已定稿**。

## 〇、决策总览(一屏版)

| 维度 | 决策 | 主要依据 |
|---|---|---|
| 产品定位 | **审批为核**(人工审批链),不做自动化编排;保留一颗自动化种子:**Webhook 节点(M3)** + 节点类型 SPI(消费者可注册自定义节点类型) | Elsa/Conductor 那类编排是另一个产品;SPI 保住"通用性"不牺牲焦点 |
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
| **M3 通用性拉满 → GA** | 简易动态表单(~10 控件)+ 字段权限矩阵 + 动词封顶:加签/减签/拿回/比例票签 + 长期委托规则 + 并行分支(多 token)+ Webhook 节点 + 节点类型 SPI 对外文档化 | 表单设计器(单列)+ **React 模板整体 port** | **GA 门槛:双模板 feature 对齐 + 文档站 guide 上线** |
| **M3+ 按需** | 子流程(仅当真实需求出现)、经典图模式(LogicFlow)、统计报表 | — | 不进任何承诺 |

## 九、调研§七开放问题——决议

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
| 1 | 产品定位:审批 or 编排? | **审批为核**;唯一自动化种子 = Webhook 节点(M3);节点类型走 SPI 可扩展;完整编排(脚本/定时/重试)明确不做 |
| 2 | 功能丰富 vs 配置简单? | FlowLong 动词全集 = **路线图上限**分三期铺开;配置纪律:每节点默认可见项 ≤5、全部有默认值;验收线 = 请假流程 3 分钟建完 |
| 3 | 表单怎么整合? | 双腿并存:M1 挂载点(开发者)、M3 简易动态表单进包内(业务管理员);`formSchema`/`formPerms` M1 即预留;布局/公式/联动/子表永久不做 |
| 4 | 建模能力边界? | 排他分支 M2、并行 M3、**包容永不**、子流程 M3+ 按需 |
| 5 | 内置审批人 Provider? | **8 种**:指定成员/直属主管(N 级)/连续多级主管/角色/职位(可限机构)/发起人自选/发起人本人/机构负责人——最后一种要求 `SysOrg` 新增 `LeaderUserId`;HRBP 等私有逻辑走消费者自注册 |
| 6 | 行为语义默认值? | 见下表;空审批人策略**三级可配**(节点>流程默认>全局配置),出厂默认自动通过 |
| 7 | 设计器双模板成本? | **Vue 先行打磨,schema 定稿后一次性 port 到 React(GA 前)**;纯 TS 逻辑框架无关;无框架共享包否决(配置抽屉占 60% 工作量,无法框架无关化) |
| 8 | 里程碑切分? | 见§八,照此执行;Webhook 留在 M3 不提前 |

### 行为语义默认值(问题 6 细则,M2 写代码前的既定答案)

| 场景 | 默认行为 | 可配项 |
|---|---|---|
| 拒绝后 | 整个流程终止 | 节点可配"退回到指定节点" |
| 退回后重新提交 | 从头重走 | 可配"从退回节点继续,已过节点不再审" |
| 发起人撤销 | 仅当无任何节点被审批过 | 审批人"拿回"是 M3 动词 |
| 抄送 | **不算待办**:独立"抄送我的"列表 + 已读标记,不催不超时 | — |
| 审批人解析为空 | 自动通过(出厂全局默认) | 三级可配:自动通过 / 转指定人 / 卡住通知管理员 |
| 同一人相邻节点 | 自动通过后一次(去重) | 节点可配"仍需重复审批" |
| 会签中一人拒绝 | 一票否决立即拒 | 比例票签是 M3 动词 |
| 或签中第一人表态 | 先表态即定局(先拒即拒/先过即过),其余任务自动取消 | — |

## 十一、风险与警戒线

1. **许可污染**:参考库任何源码不入仓;CCFlow 连算法细节都不读(GPL 衍生风险);StavinLi 无 LICENSE,只学交互。
2. **范围膨胀**:动态表单、长期委托、并行网关、统计大盘全部立牌"二期"——FlowLong 企业版功能清单是**词汇表不是排期表**。
3. **组织模型越界**:Workflow 包内出现任何"自己的用户/部门/权限"概念即回退重设计(CCFlow 教训)。
4. **CI 成本**:新增 9 表进 CodeFirst,SqlServer leg 的 per-DB 建表成本会再涨——新表控制数量本身就是缓解;必要时把 Workflow 专属测试并入 nightly 全量而非 PR 子集。
5. **双模板漂移**:设计器 JSON 双端对拍测试进 CI(各自 `npm test`),防止两端 schema 悄悄分叉。

## 十二、JNPF 增量(2026-08-18,不改定稿)

本地对照:`参考项目/工作流/jnpf/_TENON_REF.md`;调研第六节。**不翻转**§〇/§九/§十。只补三条 M2/M3 对照:

1. **发起范围**是定义级能力(全员 / 指定角色),不要为此新建授权表。  
2. **审批页按钮**跟字段权限一样挂在节点 JSON(`btnInfo`),与角色菜单按钮分家;默认值仍遵守「每节点可见项 ≤5」。  
3. **实例列表按参与**(发起/待办/已办/抄送/监控),组织数据范围继续只管业务实体,不要拿来滤 `WfInstance`。

连续多级主管若运行时现查 `DirectorId`:M2 须写死「发起时快照 vs 实时」,JNPF 选择发起时拍 `workflow_launch_user`。
