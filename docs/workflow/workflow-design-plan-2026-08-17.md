# TenonAdmin.Workflow 设计规划(基于 31 仓摸底)

> 文档入口：[`README.md`](./README.md)

日期:2026-08-17。前置材料:[`workflow-engine-research-2026-08-10.md`](./workflow-engine-research-2026-08-10.md)(调研)、本地参考库 `../参考项目/工作流/SUMMARY.md` 与各仓 `_TENON_REF.md`。本文把调研收敛为**可开工的设计决策**;2026-08-17 经磨盘八问逐条裁决(见§十),2026-08-23 在不翻转审批内核的前提下把 AI Decision 上调为 M3b 战略交付。

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
| 前端交互 | **钉钉树状设计器,双模板各自自研**(Vue+Naive / React+antd),**MVP 不引画布库**;交付节奏:**Vue 先行打磨并完成全部功能测试,测试全绿后再一次性 port 到 React(GA 前追平)**,纯 TS 校验/序列化逻辑写成框架无关 | StavinLi 交互母体;零共享铁律;设计器打磨期改一处=改两处,双做返工翻倍 |
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

接线约定沿内核铁律:所有服务 `TryAdd*`、方法 `virtual`、模板方法拆小步;消费者在 `AddTenonAdmin()` 之前注册同接口即可整体替换(如换掉 `IApproverProvider` 接自家 HR 系统)。替换保证进"六件套"式测试(`WorkflowReplaceabilityTests`)。整体替换 `IWorkflowEngine` 属受信任的完全接管，消费者同时承担事务、幂等、fence、审计和 AI shadow-only 不变量；自定义节点 handler 也不得旁路写状态。

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

### 5.1 简易动态表单契约（T05 定稿，2026-09-09）

`formSchema` 是定义级内置表单，`formComponent` 是消费者自定义表单挂载点。二者只能选一个，也可以都没有；两者同时存在属于模型错误。没有表单时 `formSchema = null`、`formComponent = null`，节点上的 `formPerms` 不产生运行时效果。`formComponent` 只保存消费者页面的相对路径，去除首尾空白后最多 256 字符且不得含控制字符；服务端不探测消费者程序集或前端文件是否存在。

内置表单固定使用现有 10 个 `WfFormFieldType`：`text`、`textarea`、`number`、`money`、`date`、`datetime`、`select`、`multiSelect`、`user`、`attachment`。schema 版本固定为 `1`；字段按数组顺序单列展示，数组顺序就是展示和提交顺序。一个 schema 至少有 1 个字段、最多 50 个字段，整个 JSON 不超过 64 KiB。删除最后一个字段时设计器将 schema 置为 `null`，不保存空表单对象。

字段公共契约如下：

| 字段 | 契约 |
|---|---|
| `key` | 机器键，必须匹配 ASCII 正则 `^[A-Za-z][A-Za-z0-9_]{0,63}$`；大小写敏感；同一 schema 内必须精确唯一。它是变量 JSON、条件表达式引用和 `formPerms.field` 的稳定身份，改名等同于删旧字段再建新字段。 |
| `label` | 展示名，去除首尾空白后长度 1–128；不得含控制字符。 |
| `required` | 布尔值，缺省为 `false`。发起态和可编辑办理态校验必填；隐藏或只读字段不因该标记制造无法填写的任务，服务端保留已有值。 |
| `placeholder` | 可选字符串；空字符串按未设置处理，去除首尾空白后最多 256 字符，不得含控制字符。它只提供提示，不是默认值。 |
| `props` | 可选对象；`null`、缺省和空对象使用该控件的默认值。除下表列出的键外不得出现未知键，不能把字符串化 JSON 再嵌套一层。 |

`props` 的类型专属键和边界如下。表中未列出的键一律拒绝；数值必须是有限 JSON number，整数字段必须是整数。

| 类型 | `props` |
|---|---|
| `text` | `maxLength` 可选，整数 1–256，缺省 256。 |
| `textarea` | `maxLength` 可选，整数 1–4000，缺省 4000；`rows` 可选，整数 2–8，缺省 4。 |
| `number` | `min`、`max`、`precision` 均可选；`precision` 为整数 0–6，且 `min <= max`。 |
| `money` | `min`、`max` 可选且 `min <= max`；提交值固定为最多 2 位小数，不提供货币符号或汇率配置。 |
| `date` | `min`、`max` 可选，格式严格为 `YYYY-MM-DD` 且 `min <= max`。 |
| `datetime` | `min`、`max` 可选，必须是可解析的 ISO 8601 时间且 `min <= max`；保存时保留带时区的字符串。 |
| `select` | 必须有 `options`，数量 1–100；每项为 `{label,value}`，`label` 1–128 字符、`value` 1–64 字符，`value` 精确唯一。 |
| `multiSelect` | 同 `select`；`maxSelected` 可选，整数 1–100，且不能超过选项数。 |
| `user` | `multiple` 可选布尔值，缺省 `false`；`maxSelected` 仅在 `multiple=true` 时可用，整数 1–100，缺省 20。单选提交一个正用户 Id，多选提交不重复的正用户 Id 数组。 |
| `attachment` | `multiple` 可选布尔值，缺省 `false`；`maxCount` 可选整数 1–20（单选固定为 1）；`accept` 可选字符串最多 256 字符；`maxSizeMb` 可选整数 1–100，缺省 10。文件实际类型、大小和存储权限仍由文件服务端强制。 |

字段权限只在 `approval` 节点解释，保留现有 `hidden`、`readonly`、`editable` 三值。`formPerms` 缺省或没有某字段时默认为 `editable`；不要求为每个字段写一行。相同 `field` 出现两次属于发布模型错误，不能采用最后一项覆盖；当内置 schema 存在时，引用不存在的字段也属于发布模型错误。`hidden` 不渲染且客户端提交的同名值被服务端丢弃，`readonly` 渲染但办理人不能修改，`editable` 允许修改；`required` 只对当前允许编辑的字段执行输入校验。发起态不使用审批节点权限矩阵，所有未被消费者表单自行约束的内置字段按 schema 规则处理。

为兼容 M1/M2 草案，`formSchema = null` 或使用 `formComponent` 时，`formPerms` 不参与内置表单运行时；旧定义中的已有数组原样保留但不赋予权限效果，设计器不会继续生成它。只有 `approval + formSchema` 的组合才执行上述未知字段和重复字段发布校验；非审批节点的 `formPerms` 同样不生效。内置 schema 与消费者 `formComponent` 不可叠加，不能借 `formPerms` 约束消费者自定义字段。

服务端必须强制 schema 版本、组合关系、JSON 大小、字段数量、键/标签/占位符长度与字符集、键唯一性、控件类型、专属 props 的未知键和数值/选项边界、`formPerms` 的重复/未知字段规则，以及发起和办理时的类型、必填、访问级别和文件安全校验。前端负责单列编辑器、即时提示、控件默认值、排序和可用选项展示；前端校验只是体验优化，不能替代服务端校验。发布时将 `WfModel.FormSchema` 按同一 canonical JSON 同步写入版本的 `FormSchema` 快照；已发布版本和在途实例只读该快照。

### 5.2 Vue 内置表单运行时值（T08 定稿，2026-09-09）

发起态从已发布版本的 `formSchema` 按字段顺序渲染单列表单，编辑值写入 `variablesJson` 对象；查看态反序列化同一对象并以只读控件回放。文本、选项和日期值保持字符串，数字/金额保持 JSON number；`datetime` 保存带时区的 ISO 8601 字符串。人员保存正用户 Id（单选为一个值，多选为不重复数组），附件保存文件服务返回的正文件 Id（单选为一个值，多选为不重复数组），不把 `storagePath` 写入业务变量。

Vue 在发起提交前执行必填、类型、范围、选项、人员 Id 和附件 Id 校验；该校验只改善交互，服务端仍是最终边界。变量 JSON 根不是对象或无法解析时，运行时显示数据错误并阻止提交，不把损坏数据转换为空对象；合法的未知变量键在编辑过程中保留。`formComponent` 与 `formSchema` 的组合仍由服务端拒绝，前端入口在异常双配置时优先保留消费者挂载点，避免内置表单静默替代业务组件。

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

**交付节奏(磨盘问题 7 定案)**:设计器 **Vue 先行**——M1/M2 只做 `web/`,打磨期频繁改交互不必两边返工;先完成 Vue/后端全部功能测试并清除失败，再在 GA 前一次性 port 到 `web-react/`。校验/序列化等纯 TS 逻辑从第一天写成框架无关(不 import 任何 Vue/React API),port 时直接复制。运行时页面(待办/详情/操作)交互简单,双模板照常同步。无框架 npm 共享包方案已评估否决:画布只占设计器约 30% 工作量,占 60% 的节点配置抽屉全是表单交互,无法框架无关化,强行共享等于多维护一个第三方包还得桥接两套 design tokens。

### 6.3 页面清单(菜单管理 UI 配置,不写路由代码)

- 流程管理:定义列表 / 设计器 / 版本历史(管理员)
- 审批中心:待我审批 / 我发起的 / 我已办的 / 抄送我的(`[ActiveSession]` 端点)
- 审批详情:进度时间线 + 意见记录 + 业务表单挂载点 + 操作按钮组

## 七、API 面(权限码即路由)

```
POST /api/v1/workflow/definition/add|update|publish|disable // [RolePermission]
GET  /api/v1/workflow/definition/page|{id}|versions/{id}   // [RolePermission]
POST /api/v1/workflow/instance/start          // [ActiveSession],businessKey + 摘要变量
POST /api/v1/workflow/instance/cancel         // [ActiveSession],仅发起人且无人审批
POST /api/v1/workflow/instance/resubmit       // [ActiveSession],仅发起人按退回规则重提
GET  /api/v1/workflow/instance/startable|page|{id}|history/{id} // [ActiveSession]
GET  /api/v1/workflow/instance/monitor        // [ActiveSession]+[RolePermission]
GET  /api/v1/workflow/task/todo|done|cc       // [ActiveSession]
POST /api/v1/workflow/task/approve|reject|return|transfer|delegate|urge // [ActiveSession]
POST /api/v1/workflow/task/add-sign|remove-sign|take-back // [ActiveSession]+[RolePermission]
GET/POST/PUT/DELETE /api/v1/workflow/delegation/* // [ActiveSession]+[RolePermission]
GET/POST /api/v1/workflow/outbox/*             // [ActiveSession]+[RolePermission]
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
| **M3b AI Decision v0** | M3b-0 已以 M3a Seam 交付 AI Decision Adapter：OpenAI-compatible + fake Provider、结构化 proposal、schema/policy、shadow-only、人工 fallback、审计/脱敏/限额；受控自动化仍属后续切片 | API 已提供脱敏的 proposal/证据/策略结果审计投影，复用实例参与者/监控权限；双模板设计器与独立管理页后置 | M3b-0 全程 shadow-only，AI 不自动批准、拒绝或推进 task/token；无效输出、低置信度、风险和异常均转人工 |
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
| 7 | 设计器双模板成本? | **Vue 先行打磨并完成全部功能测试，测试全绿后一次性 port 到 React(GA 前)**;纯 TS 逻辑框架无关;无框架共享包否决(配置抽屉占 60% 工作量,无法框架无关化) |
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

两条线都不把模型 SDK、Prompt 或供应商配置耦合进 `TenonAdmin.Workflow`。Provider 类型留在 Adapter；审计只保存必要的模型/Prompt/schema/policy 版本 hash、输入摘要、证据引用 hash、受限结构化结果、耗时与 token/费用统计，模型自由文本不落库。M3b AI Decision v0 是 AI 原生路线的战略交付；RAG、Agent、更多 Provider 与设计 Copilot 后置到 M3+。

当前已交付的是 M3b-0 shadow-only 基础切片；上文“达到评测阈值后开放低风险自动放行”属于后续受控阶段，不代表当前实现已经取得自动审批权限。M3B0-17 至 M3B0-20 及后续审查修复已经完成，最终聚焦矩阵 326/326 通过，代码与架构复核无 blocker；下一阶段为 M3a-2 Vue 产品面。

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

当前执行再分为两个交付阶段：先完成 **M3a-2 Vue**（Webhook 设计器、简易动态表单/字段权限、高级动词和并行分支），并先让 Vue、后端、契约及完整功能测试全部无失败，再做 **M3a-2 React port**。2026-09-14 功能测试收口已使该闸门为真，但本轮仍未实现 React port。M3a-2 Vue 完成只关闭 Vue 阶段，不能宣称完整 M3a-2 或 GA 完成。Vue 阶段的稳定任务拆分见 [`m3a2-vue-task-plan-2026-09.md`](./m3a2-vue-task-plan-2026-09.md)。

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

外部 Webhook 副作用的交付语义是 at-least-once：租约过期或 tx2 提交前崩溃都可能导致同一请求再次发送。消费者必须使用 `ExecutionKey` 作为幂等身份；本地 workflow 状态通过 fence/CAS 保证同一 execution 的 Token 最多推进一次。重提复用同一 token 但会生成新的 `NodeVisitId`：为与完成 tx2 保持统一的 `execution → token` 锁顺序，引擎先在同一事务中将该 token 上全部 `Pending`/`RetryScheduled`/`Running` 的旧 execution 置为 `Cancelled`、清 lease/retry 并增加 Fence，再以 token CAS 取得重提权，随后从 start 进入新 visit；若 CAS 输掉，事务回滚先前的 invalidation。迟到 handler 的 tx2 因 `Status != Running` 与 Fence 失配整体回滚；回写还会核验 token 的节点/visit 仍与 execution 一致，旧结果不能推进新 traversal。Task 8b 仍只在 execution 终态提交 `Pending` outbox；Task 8c 已落地 `Dispatching/Dispatched/Failed` 的领取、transport、重投、死信和人工重放，详见 §15.8。

### 15.4 M3b 自动放行默认关闭，阈值校准是消费者的责任

TenonAdmin 以内核包分发，自身没有生产流量，shadow mode 的评测数据只能来自消费者各自的部署。因此：

- 内核交付**机制**：shadow 记录、指标采集（人工推翻率、逃逸率、schema 失败率、fallback 率、provider 延迟与成本）与现成审计视图；
- 自动放行**默认关闭**，按流程定义显式开启；内核不提供“开箱即用”的放行阈值默认值；
- 消费者必须先在自己的数据上跑 shadow 达标，“何时由 shadow 切自动放行”是每个部署自己的决定；
- 模型自报 `confidence` 只作记录与事后评测，不单独作为放行条件——policy 主判据是可确定性核验的 `reasonCodes`、`riskFlags` 与证据完整性（细则见 AI 基石 §4.2、§4.7）。

### 15.5 M3 高级审批动词语义矩阵（T11，2026-09-09）

本节冻结 M3a-2 Vue 的五个高级动词。后续实现可以增加存储字段和适配层，但不能改变调用者、状态机、审计、幂等和并发语义。这里的“权限”始终是两道门：规范化路由权限码 + 动词自己的业务资格；超级管理员没有绕过业务资格的隐式后门。

#### 共同规则

- **状态范围**：比例票签、加签、减签和拿回只作用于 `Running` 实例的当前人工审批任务；终态实例、已删除任务、非 `Pending` 的调用者和不属于当前实例的目标统一拒绝。并行多 Token 尚未定稿前，拿回只接受当前单 Token 主链；T17 再单独定义并行语义。
- **并发锚**：只改变当前待办办理人的动作先用 `wf_task.Version` CAS；会改变 token 位置或实例状态的动作必须在同一事务内依次使用 task、token、instance 的期望状态 + 版本 CAS。任何 CAS 失败返回 `TaskConflict (48007)`，事务不留下半条历史或通知。
- **历史**：人工动作追加 `wf_his_task`，流程事件追加 `wf_history`；两者只追加不改写。`AddSign`/`RemoveSign` 不伪装成 `Transfer`，`TakeBack` 不删除原审批记录。新增事件值只能追加，不能重排已有 `WfHistoryEventType` 数值。
- **通知**：事务提交后才调用现有 `IWorkflowNotifier`/`IRealtimePublisher`；不新建 `SysNotice` 通道。回执重放不重复发通知；失败按现有结构化日志与指标规则记录，不回滚已提交领域状态。
- **RequestId/receipt**：所有用户触发的五类动作及长期委托规则变更都必须走同事务 `TryBeginAsync` → 领域变更 → `CommitAsync`。同一 scope、命令、目标、操作者和 request key 的重试原样返回第一次成功结果；业务失败回滚且不留回执。动作的目标用户/规则窗口等参数必须纳入规范化请求摘要，摘要不一致返回 `RequestIdConflict`，绝不把不同动作当重试执行。`Urge` 的既有“可重复、无 receipt”语义不改变。
- **权限码与错误码**：实现使用独立路由权限 `POST:/api/v1/workflow/task/add-sign`、`POST:/api/v1/workflow/task/remove-sign`、`POST:/api/v1/workflow/task/take-back`，长期委托规则管理使用独立的规则路由权限；业务错误不复用一次性 `DelegateTargetInvalid (48026)`。新增错误码预留为 `48035 SignNotAllowed`、`48036 SignTargetInvalid`、`48037 TakeBackNotAllowed`、`48038 DelegationRuleNotFound`、`48039 DelegationRuleInvalid`、`48040 DelegationCycle`、`48041 DelegationScopeDenied`、`48042 RequestPayloadConflict`、`48043 DelegationRuleConflict`。`TaskConflict (48007)` 仍专用于任务/实例/token 竞争。

#### 比例票签

比例只对 `mode=all` 的会签生效；`mode=any` 继续一票通过，忽略比例；`mode=sequential` 不允许小于 100 的比例。`allPassRatio` 缺省值为 100，因而现有会签行为保持不变。

| 项目 | 冻结语义 |
|---|---|
| 调用者 | 当前任务的任一 `Pending` 审批人，沿用 `Approve/Reject` 的权限与办理资格。 |
| 分母与门槛 | 当前 `NodeVisitId` 上所有未 `Skipped` 的审批 actor（`Waiting`、`Pending`、`Done` 都计入）为分母；所需同意数为 `ceil(分母 × allPassRatio / 100)`，最小为 1。被加签/减签后在同一事务按新 actor 集合重算。 |
| 提前通过 | `Done` 的同意数达到门槛立即关闭当前 task，推进 token；已满足门槛的后续动作因 task CAS 失败。 |
| 提前失败 | `同意数 + 尚未表态的非 Skipped actor 数 < 门槛` 时立即失败并终止/按既有拒绝路由处理；因此 100% 会签任一拒绝即失败，低比例会签只有在无法达到门槛时才失败。 |
| 影响与审计 | `Approve/Reject` 各写一条既有 `WfHisTask` 和 `TaskCompleted` 事件；未达门槛的同意仍推进 token 版本，避免与撤销竞争无 CAS 保护。比例计算不产生新 token。 |

#### 加签

| 项目 | 冻结语义 |
|---|---|
| 调用者与状态 | 当前 task 的 `Pending` 审批人，且实例仍为 `Running`；一次命令只加一名用户，重复目标拒绝。 |
| 目标 | 已启用、与调用者处于同一租户/机构范围的用户；不得是自己、现有 actor、已 `Done` 的历史办理人或无效用户。 |
| actor/task/token | `Any/All` 新 actor 为 `Pending` 并增加比例分母；`Sequential` 新 actor 追加到末位并先为 `Waiting`。只递增 task 版本；不移动 token、不改变实例状态。 |
| 历史与通知 | 追加 `WfHisTask.Action=AddSign`，并在结构化载荷中记录 caller、target、NodeVisitId、原/新分母和门槛；追加 sign-change 流程事件。提交后只给新增办理人发送待办通知，并刷新当前参与者。 |
| 失败 | 目标非法用 `SignTargetInvalid (48036)`；调用者、实例或 task 状态不符用 `SignNotAllowed (48035)`；CAS 竞争用 `TaskConflict (48007)`。 |

#### 减签

| 项目 | 冻结语义 |
|---|---|
| 调用者与目标 | 调用者仍须是当前 task 的 `Pending` 审批人；目标只能是本 task 的 `Pending` 或 `Waiting` actor，不能是 `Done/Skipped`，也不能移除自己。一次命令只减一名用户。 |
| 最后一人保护 | 减除后至少保留一名未 `Skipped` actor；`Sequential` 不得移除当前唯一 `Pending` actor。目标是最后一名可办理人时返回 `SignTargetInvalid`，不留下空 task。 |
| actor/task/token | 目标 actor 翻为 `Skipped`，task 版本递增；token 不移动。按剩余 actor 重算比例门槛，若已达到门槛则在本事务内完成 task 并推进 token，否则保持当前 task。 |
| 历史与通知 | 追加 `WfHisTask.Action=RemoveSign` 与结构化目标/门槛载荷，追加 sign-change 事件；提交后通知被移除人刷新权限，并刷新剩余办理人。 |
| 失败 | 资格/状态不符用 `SignNotAllowed`，目标不合法或最后一人保护用 `SignTargetInvalid`，CAS 竞争用 `TaskConflict`。 |

#### 拿回

“拿回”是审批人撤回自己刚刚通过的节点，不是发起人撤销，也不是退回后重提；三者不共用资格或状态码。

| 项目 | 冻结语义 |
|---|---|
| 调用者与目标 | `Running` 实例中，调用者必须是自己最近一次 `Approve` 的人工审批人。目标不由客户端任意指定，只能是该条审批记录对应的上一节点访问；同一节点重开也生成新的 `NodeVisitId`。界面只展示这一合法目标。 |
| 时间窗口 | 调用者通过后，目标下游不得出现任何新的人工表态、转办、一次性委托、加减签或机器 execution/outbox 终态；下游只创建了待办不算表态。任一后续动作已发生，返回 `TakeBackNotAllowed (48037)`。已拒绝、已终态或无自己的最近通过记录同样拒绝。 |
| actor/task/token | 在同一事务中 CAS 当前下游 task 并关闭其 actor，CAS token/instance 后回到目标节点重新进入；旧审批和下游历史保留，生成新的访问、task 和办理人解析结果，变量快照不回滚。 |
| 历史与通知 | 追加 caller 的 `WfHisTask.Action=TakeBack`、原 task/目标 node/新 NodeVisitId 载荷和 take-back 事件；提交后通知被关闭的下游办理人，并按新 task 的既有规则通知目标节点办理人。 |
| 失败与幂等 | 资格、时间窗、目标或非主链状态不符用 `TakeBackNotAllowed`；task/token/instance CAS 竞争用 `TaskConflict`；重试复用同一 receipt，不重复回退或通知。 |

#### 长期委托

长期委托独立于现有 `IWfTaskService.DelegateAsync`：后者仍是“只委托这一件待办”的一次性动作，不读取规则、不改写为长期委托。

| 项目 | 冻结语义 |
|---|---|
| 调用者与范围 | 规则所有者只能管理自己在当前租户/机构 scope 内的规则；管理员必须持有独立规则管理权限，且只能管理同一 scope。规则管理不是审批 task 动作，不因持有 task 权限自动获得。 |
| 规则形状 | 每个原责任人在一个 scope 只允许一个有效规则槽：`OriginalUserId → DelegateUserId`、`Enabled`、UTC `[StartsAt, EndsAt)`（`StartsAt < EndsAt`）。自委托、无效用户、跨 scope、空窗口和冲突窗口拒绝；启停与删除均保留规则历史。软删规则重新启用时复用原 `RuleId`，并分别保留删除与启用审计。 |
| 链式与循环 | 解析只做一跳：原责任人命中规则后得到有效 delegate，不递归套用 delegate 的规则；因此 A→B、B→C 时 A 的任务交给 B。规则写入/启用时沿当前有效图检测自环和环路，发现环路返回 `DelegationCycle (48040)`，不保存或启用。 |
| actor/task/token | 规则只在节点创建 actor 时生效；有效期外或 disabled 回退原责任人。有效规则把 actor 指派给 delegate，同时在 assignment snapshot/历史中保留 `OriginalUserId`、`DelegateUserId`、RuleId 和 scope。对既有 task 不追溯改派，task/token 不因规则启停而移动；按有效 delegate 去重，避免重复 Pending actor。 |
| 历史与通知 | 节点任务创建/办理历史记录原责任人和实际办理人，规则变更另写规则审计；提交后待办只通知实际 delegate，规则启停通知规则所有者和 delegate。规则变化不为已经创建的 task 重新发通知。 |
| 幂等与并发 | 创建用 scope + owner + request key 的规则变更 receipt，更新/启停/删除用 RuleId + owner + request key；重试必须先按完整 identity 命中 receipt 并原样返回首次结果，再检查当前规则实体状态。因此原 identity 在规则 disabled 或软删后仍可重放，软删规则的新 request 仍返回 `DelegationRuleNotFound`。规则行使用版本 CAS，同一 owner 的两个更新只允许一个胜出。规则解析是节点创建事务内的只读快照，不另开事务、不持有远程调用。 |
| 错误 | 不存在/已删除规则用 `DelegationRuleNotFound (48038)`；窗口、用户或重复槽非法用 `DelegationRuleInvalid (48039)`；循环用 `DelegationCycle (48040)`；scope 越权用 `DelegationScopeDenied (48041)`；规则更新 CAS 竞争用 `DelegationRuleConflict (48043)`，不能静默覆盖。 |

### 15.6 M3a-2 并行 fork/join 语义与数据模型（T17，2026-09-09）

本节冻结并行节点的最小契约；T17 只定稿，不实现字段、表、校验或运行时。

#### 定义与身份

- 并行节点使用 `parallelArms[]`，每臂只有 `id/name/next`；节点自身的 `parallel.next` 是 join 后唯一后继。发布时要求至少 2 臂、同一并行节点内 `id` 非空且唯一；`next` 可空，表示空臂。并行臂不复用排他分支的 `conditions`，臂内禁止再出现并行节点。
- 每次访问并行节点只生成一次 `ForkId`；同一访问的重试必须复用它，新的访问（包括重提后重新走到该节点）才生成新的 `ForkId`。父 token 保留该次 `ParentNodeVisitId`，作为 fork 历史和回放身份。
- `WfToken` 增加 nullable `ParentTokenId/ForkId/PendingArmCount`。fork 后父 token 停在并行节点并置为 `WaitingJoin`，`PendingArmCount` 为未完成臂数；非空臂各创建一个 `Active` 子 token，子 token 的 `ParentTokenId/ForkId` 指向本次 fork。旧串行 token 三列均为 null。
- 新增 `WfParallelArm`，以 `(ForkId, ArmId)` 为主键，字段为 `ParentTokenId`、nullable `ChildTokenId`、`Status`、`Version`、`ParentNodeVisitId`、nullable `ChildEntryNodeVisitId`、nullable `Reason`。状态只需 `Active/Completed/Cancelled`；空臂不创建 child token，直接以 `Completed` 写入，`ChildTokenId/ChildEntryNodeVisitId` 为 null。

#### 完成与汇合

- 人工、Webhook 和 AI 节点继续共用现有 task/execution → Agenda → token 执行链。retry、AI shadow、manual fallback 或人工任务未完成时，该臂仍为 `Active`，不得参与 join。
- 非空臂到达末端时，在同一事务内 CAS 子 token `Active → Completed`、CAS 对应 arm `Active → Completed`，并原子递减父 token 的 `PendingArmCount`。迟到或重复完成因 child/arm CAS 失败只读取既有结果，不重复递减或推进。
- 只有完成者将 `PendingArmCount` 从 `1 → 0`，且同时命中父 token `Status=WaitingJoin + ForkId + Version` CAS，才有权把父 token 恢复为 `Active` 并推进到 `parallel.next`；其余竞争者退出。递减和 join 都以数据库条件更新为准，Repeatable Read 下 CAS 失败者必须在新事务读取新版本后重试，不能依赖旧快照判断“尚未归零”，避免丢失唤醒。
- fork 创建时若所有臂均为空，在同一事务内写完 `Completed` arm、令父 token 完成 `1 → 0` join 并进入 `parallel.next`，不留下 `WaitingJoin`。混合空臂只把空臂计为已完成，等待其余臂。

#### 控制动作、历史与阶段边界

- `reject` 的 `terminate` 策略终止实例，并在同一事务取消本 fork 的父/兄弟子 token、未完成 arm、活跃 task 和 execution。`toNode` 只能跳到当前 `ForkId + ArmId` 内的节点；跨臂目标及从 fork 外发布到臂内的目标在发布时拒绝。
- 实例 `cancel` 取消父子 token、未完成 arm 以及活跃 task/execution。`return` 离开当前并行区域时先取消整个 fork，再只恢复 parked parent 为唯一 `Active` token，并清空其 `ForkId/PendingArmCount` 后执行既有退回；不得保留孤儿子 token。退回后的 `resubmit` 从 start 重走，走到新的并行访问时生成新 `ForkId`。
- `wf_history` 追加 `fork/armComplete/join/cancel` 事件；payload 至少包含 `ForkId/ArmId/ParentTokenId/ChildTokenId/ParentNodeVisitId/ChildEntryNodeVisitId` 及状态/原因。回放以 `ForkId + ArmId + Sequence` 归组和排序，不用当前 token 状态反推旧 fork。
- T18 实现 schema、字段、表、索引、迁移和发布校验；T19 实现 fork/join 运行时；T20 验证竞争与崩溃恢复。T17 不宣称上述实现或四数据库契约已经通过。

### 15.7 M3a-2 Vue 最终实现语义（2026-09-13）

本节是 T25A 收口后的实现记录，和上面的冻结契约一起作为当前事实源。

- **表单历史权限**：有当前待办时按当前节点的 `formPerms` 渲染；历史办理人没有当前待办时，按其历史办理任务对应节点的权限渲染；发起人、抄送人和监控者按当前节点或终态已访问节点回退。`hidden` 字段不返回或不渲染，`readonly` 只能查看，`editable` 才能修改；服务端仍负责合并变量、过滤隐藏值和校验恶意提交。
- **Vue 权限与清空**：实例详情按当前用户全部 pending task 的适用节点合并字段权限，冲突时固定取 `hidden > readonly > editable`；内置日期/时间控件清空时写入显式 `null`，与附件清空保持同一删除语义，序列化后由服务端合并并持久化。
- **拿回资格**：调用者必须是同一主 token 上最近一次本人 `Approve` 的办理人；下游动作窗口按该 token 判断，目标 task、token 和实例依次 CAS，详情只从当前用户全部 pending task 投影合法 `MyTakeBackTaskId`。
- **委托通知事务边界**：长期委托规则的领域写入、审计和 receipt 在事务提交后才触发通知。通知失败只记录结构化 `Warning`，不回滚已经提交的规则；receipt 重放不重复发送通知。
- **委托并发与异常**：长期委托 Add/Update/Delete 在同一机构 scope 的数据库锚点写锁下读取规则图并写入；receipt/委托只有在确认唯一键冲突时才查询竞争记录，连接、权限和其他基础设施异常原样传播。
- **并行退回**：退回离开并行区域时先取消旧 fork 的未完成臂及其子 token/task/execution，再恢复唯一 parked parent。恢复同时把数据库和内存中的 parent `NodeId` 设为退回目标节点，清空 `ForkId/PendingArmCount`，随后沿既有退回命令继续执行；重提再次进入并行节点时生成新的 `ForkId`。
- **运行时安全投影**：发起、实例详情、待办和历史回放使用受限 runtime projection，只公开可运行的节点类型、名称、结构、字段权限和必要执行状态；Webhook 凭据、AI 指令、办理人参数及未白名单的原始 `PayloadJson` 不进入这些读取模型。历史回放按 `Sequence → CreateTime → Id` 排序，并以 `ForkId + ArmId` 保持多 Token 语义。去重事件 `DuplicateApproverSkipped` 的 `userIds` 是被跳过办理人的 `long` 雪花 Id 数组，属于白名单原始值，必须出现在历史投影中；未知键、秘密字段和嵌套对象/数组仍丢弃。
- **表单 ID 表示**：表单 schema 不生成独立的表单 ID；`user` 控件中的用户 ID 和 `attachment` 控件中的 `SysFile.Id` 仍是后端 `long` 雪花 ID。当前雪花布局低于 JavaScript 安全整数上限，前端通常提交 JSON number；运行时同时接受十进制 JSON string 以兼容历史变量和通用 `int64` 客户端，解析后仍按 `long` 校验和查询。
- **表单变量损坏处理**：变量 JSON 必须是对象；非法 JSON、数组/标量根节点、重复键和非法 ID 均拒绝提交或读取，不转换为空对象掩盖数据损坏。重提内置表单先按当前 schema 校验并保存最新变量；自定义 `formComponent` 继续由消费者负责其业务表单协议。
- **OpenAPI 动态字典**：`WfFormField.props` 与 `WfAssignee.params` 是自由 JSON 对象，生成的两套 `schema.d.ts` 必须来自真实 Host，不能退化为 `Record<string, never>`，也不得手工编辑。

T18–T23 已实现并验证；SQLite、MySQL、PostgreSQL、SQL Server 的代表流程各为 `29/29` 通过。T24/T25/T25A 的本地证据为 T25A 前置后端回归 `122/122`、最终修复后回执/回填/identity 聚焦 `33/33`、独立 `code-reviewer` 为 `APPROVE`、`architect` 为 `CLEAR`。2026-09-14 功能测试收口后：Vue 单元测试 `183/183`、完整 Vue Playwright `16/16`、后端 Release `1536/1536`、Release build `0 warning / 0 error`、contract drift `in sync`、两套前端 typecheck/build 通过。此前 MFA `input[readonly]` 超时的根因是 E2E 未打开运行时 TOTP 总闸；RBAC 重复 `.n-message` 的根因是连续保存堆叠相同成功提示；历史投影曾丢掉去重 `userIds`。React 工作流 port 只在上述功能测试无失败后才允许启动；本轮未进入 React 工作流页面、AI 自动放行和 M3+。Task 8c 已另开切片完成，见 §15.8。

当前 Vue 产品面只关闭 **M3a-2 Vue**。React 工作流页面 port、AI 自动放行和 M3+ 仍是后续范围。

### 15.8 Task 8c outbox consumer（2026-09-14）

本节是 outbox 消费闭环的实现记录。入队契约不变：`WfOutboxStore` 仍然只暴露 `EnqueueAsync`，execution 终态 tx2 继续幂等插入 `Pending`。

- **状态机**：`Pending → Dispatching`（领取：`AttemptCount + 1`，`AvailableAtUtc = now + 可见性超时`）；`Dispatching → Dispatching`（可见性超时后重领）；`Dispatching → Dispatched`（transport 成功，写 `CompletedAtUtc`）；`Dispatching → Pending`（可重试失败，`AvailableAtUtc = now + 退避`，写 `LastError`）；`Dispatching → Failed`（永久失败或预算耗尽）；`Failed → Pending`（人工重放，`AttemptCount = 0`）。`Dispatched` 无出边。
- **Fence 与租约**：表不设 `LeaseOwner/LeaseExpiresAtUtc/Fence`。`AttemptCount` 就是 fence；回写必须 `WHERE AttemptCount = @mine AND Status = Dispatching`。影响 0 行视为迟到 owner，丢弃且不抛异常——transport 是 at-least-once，新 owner 会再投。可见性租约就是 `AvailableAtUtc`。领取谓词：`Status IN (Pending, Dispatching) AND AvailableAtUtc <= nowUtc`。领取的 UPDATE 与读回必须在同一事务里。
- **事务边界**：tx1 领取；事务外调用 `IWfOutboxTransport`；tx2 按结果 Complete / ScheduleRetry / Fail。`OperationCanceledException` 原样穿透，行停在 `Dispatching`，超时后可重领。其他未分类 transport 异常收敛为可重试 `48046`，摘要只含异常类型。
- **退避与预算**：`RetryAfter` 仅在 `(0, 24h]` 内采纳，否则 `30s << min(max(AttemptCount - 1, 0), 5)`。领取后若 `AttemptCount >= OutboxMaxAttempts`（配置 `TenonAdmin:Workflow:OutboxMaxAttempts`，默认 3、值域与 execution 相同 `[1,100]`），可重试失败也进死信。扫描批量 `OutboxScanBatchSize` 默认 20、最大 1000；可见性超时 `OutboxVisibilityTimeoutSeconds` 默认 60、最大 3600。写入 `DateTime`/`null` 必须先落局部变量再进 SqlSugar `SetColumns`。`LastError` 在 C# 侧截断到 512。
- **默认 transport**：`NoOpWfOutboxTransport` 明确返回终态失败，不伪造外部投递成功。消费者在 `AddTenonAdminWorkflow()` 之前注册同接口即可换成 HTTP/MQ；实现不得推进 task/token，也不得自行开工作流事务。生产调度器跑 `wf-outbox-scan` 后，未替换 transport 的消息进入 `Failed`，由监控 API 暴露；只跑 `WfNodeExecutionJob` 的测试仍可能看到 `Pending`。
- **Worker**：`WfOutboxJob` + 种子 `wf-outbox-scan`（`TenonSeedIds.ConsumerMin + 47_002`），间隔 5 秒，`SerialSkip`，`SyncOnUpgrade=false`。扫描 `Pending` 和可见性已到期的 `Dispatching`，真正单赢家仍是 dispatcher 的 CAS。
- **人工重放**：只接受 `Failed`。回执身份为 `(execution.ScopeKey, OutboxReplay, Outbox, outboxId, actor, requestId)`，不额外带 payload hash。同一 `requestId` 返回首次结果；并发第二人 `48045`。`Dispatching`/`Dispatched` 不可重放，卡住的 `Dispatching` 靠可见性超时重领。分页经 `wf_outbox → wf_node_execution → wf_instance` 走实例机构过滤器；行不存在或越权为 `48044`。
- **本轮不做**：React 工作流页面、outbox 前端页、AI 自动放行、M3+。Vue 只补 `48044`/`48045`/`48046` 错误文案；双前端 `schema.d.ts` 由真实 Host 重新生成，禁止手改。

本地 SQLite 证据：Task 8c 聚焦 `68/68`（`WfOutboxConsumerTests`/`WfOutboxDispatcherTests`/`WfOutboxWorkerTests` 加 `WfOutboxTests`/`WorkflowReplaceabilityTests`/`WfIdentityHashTests`），相关回归（含 `WfNodeExecution*` 与 `WfTakeBackTests`）`180/180`，Release build `0 warning / 0 error`。四数据库由 `WorkflowAppFactory` + CI `TENON_TEST_DBTYPE` 覆盖，本轮未在本地再跑四库。OpenAPI 已重新生成两套 `schema.d.ts`，内容一致。
