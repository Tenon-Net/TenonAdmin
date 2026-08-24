# 工作流模块调研(自研引擎,参考不引用)

> 文档入口：[`README.md`](./README.md)

调研日期:2026-08-10。范围:为 TenonAdmin 自研一个 BPMN 风格的审批工作流卫星包(`TenonAdmin.Workflow`)收集设计参考——**不打算直接依赖别人的执行引擎**,只借架构思路;前端可视化画布则可能直接引用现成库。

本地参考仓不进本仓库,约定放在本仓**上级目录**(与 `tenon-admin` 并列):`../参考项目/`。工作流摸底在 `../参考项目/工作流/`。

## 结论

TenonAdmin 要做的是一个**单机可写、DB 驱动、以人工审批链为核心场景**的 BPMN 风格引擎,跑在一个已经有过一次分布式协调经验的内核里(调度中心的选主模式)。不需要为工作流再解决一次分布式共识问题。最值得抄的架构决定来自 **Flowable**(token/agenda 执行模型)、**Zeebe**(状态即日志投影的思路,但只落单机)、**Netflix Conductor**(任务收件箱而非引擎主动推送)、**JeecgBoot/RuoYi-Office**(引擎基建/业务分派/设计器三层惯例)。**Temporal 的确定性重放**、**Zeebe 的 Raft/分区**、**Kogito 的 AOT 代码生成**、**Conductor 的跨语言 worker 轮询协议**都是过度工程,明确不抄。

前端画布倾向直接引用现成库而非自研:严格 BPMN 2.0 用 `bpmn-js`,更贴近钉钉/飞书审批流形状用 `LogicFlow` 或 `React Flow`(`web-react/`)+ `Vue Flow`(`web/`)组合。

2026-08-17 补:追加考察了国产老牌引擎 **CCFlow/驰骋**(见第四节)。它对执行架构没有增量,但贡献了一份别处拿不到的**国内审批流功能词汇表**,并且反证了"不做 BPMN"这条路在国内可行。它是 GPL-3.0,**只可读公开文档与本地结构,不可复制源码**。

同日第二轮本地克隆后补强:**国内审批流产品形态**并不走 BPMN——主流是「钉钉/飞书树状审批设计器 + 自研或嵌 Flowable 的引擎」。对本仓最值得对照的新增邻居是 **AntFlow.net**(纯 .NET)、**Warm-Flow** / **FlowLong**(轻量国产引擎)、以及 **StavinLi/Workflow-Vue3·React**(钉钉风前端母体)。第五轮又补了商业/半商业公开仓(**Slickflow**、**WorkflowEngine.NET**、Camunda 7/8、AntFlow Java、Elsa Studio)——只作参考,落地仍自研。完整本地目录见 `../参考项目/工作流/README.md`;**摸底结论与分仓笔记**见同目录 `SUMMARY.md` 与各仓 `_TENON_REF.md`。

2026-08-18 第六轮:本地补了商业低代码整机 **JNPF 7.0**(见第六节)。执行骨架没有新东西(仍是嵌 Flowable),增量在**权限如何与流程相交**以及「图引擎 / OA 业务层」切分。合同许可,**不引依赖、不复制源码**;不翻转已定稿的自研 JSON 树 + 零自带组织模型。

## 一、后端执行引擎(仅供参考,不作依赖)

| 候选 | 语言 | 值得看的地方 | 不采用/不直接依赖的原因 |
|---|---|---|---|
| **Elsa Workflows**(elsa-workflows/elsa-core) | .NET | MIT,DI 原生集成方式可参考;活跃度高(7.9k★,daily commit) | 自研前提下不作依赖;且它自己也不是严格 BPMN 2.0(BPMN XML 导入导出被官方无限期推迟),自带的 Elsa Studio 设计器是 Blazor,塞不进 Vue/React |
| **WorkflowCore**(danielgerlag/workflow-core) | .NET | 轻量 code-first DSL 的注册方式可参考 | 无设计器(issue #65 挂了多年没人做)、单一维护者、无 BPMN 语义 |
| **Camunda 8 / Zeebe** | Java/Rust | 见下方"跨语言架构借鉴" | 2024-10 起自托管生产环境收费(Camunda License v1);引擎是独立 Zeebe 集群,不是进程内库,双重不满足约束 |
| **Camunda 7 / Operaton** | Java | 完整 BPMN 2.0 参考实现 | JVM 引擎,嵌不进 .NET 进程 |
| **Flowable**(Activiti 分支) | Java | 见下方——token/agenda 模型是本次调研最有价值的一条 | 同上,JVM,只借架构不引依赖 |
| **jBPM / Kogito** | Java | AOT 代码生成应对 Serverless 冷启动的思路 | 场景不匹配(进程内库不需要解决冷启动) |
| **Netflix Conductor** | Java | worker 拉取式任务模型可借鉴任务收件箱设计 | 解决跨语言跨机器 worker 池问题,本仓不存在这个场景 |
| **Temporal** | Go 核心 | 事件历史 + 确定性重放,免费拿审计与崩溃恢复 | 对"等人点一下"的审批链是杀鸡用牛刀,确定性约束成本不划算 |
| **CCFlow/驰骋**(opencc/ccflow) | .NET | 国内审批流功能清单是本次调研里最完整的一份;见第四节 | **GPL-3.0**,内核以 NuGet 分发给闭源消费者,链进去会传染整个消费者应用;技术栈也对不上(见第四节) |

## 二、前端可视化设计器(可直接引用)

| 候选 | License | 定位 | Vue 3 | React 19 | 备注 |
|---|---|---|---|---|---|
| **bpmn-js**(bpmn-io) | bpmn.io License(类 MIT + 强制水印) | 严格 BPMN 2.0,业界参考实现 | 无官方封装,需自己包 | 无官方封装,需自己包 | 若"BPMN 2.0 XML 互操作"是硬需求,只有它能打;水印去除要买商业授权 |
| **LogicFlow**(滴滴开源) | Apache-2.0 | 通用流程图 + BPMN 风格扩展包 | 无官方封装但中文生态好 | 无官方封装 | 11.6k★ 全场最高;一套库两端各接一遍胶水代码 |
| **React Flow**(`@xyflow/react`) | MIT | 通用节点编辑器 | — | 官方一线维护,38k★ | `web-react/` 的事实标准 |
| **Vue Flow**(bcakmakoglu/vue-flow) | MIT | 通用节点编辑器 | 专为 Vue 3 设计,6.8k★ | — | **不是** React Flow 的官方姐妹项目(独立作者),API 相近但会漂移 |
| jsPlumb Community | MIT/GPLv2 双授权 | 连线库 | — | — | 上游已停止维护,出局 |

**决策分水岭**:是否需要与外部 BPMN 工具(Camunda Modeler 等)XML 互通?钉钉/飞书式审批流通常不需要——只是网关/流程图形状,不是标准驱动。不需要则 bpmn-js 出局,LogicFlow 或 React Flow + Vue Flow 组合更省事。

## 三、跨语言架构借鉴(自研引擎该抄什么)

| 引擎 | 核心想法 | 代价 |
|---|---|---|
| **Zeebe** | 状态是 append-only 日志的投影,每分区一个 Raft group,换水平扩展和自动故障转移 | 整套 Raft/分区/gossip 是为"每秒海量实例"的规模问题,本仓量级用不上 |
| **Flowable** | **Token + Agenda 模型**:运行实例是图上一组 token,由扁平操作队列(而非递归调用栈)驱动,网关/并行分支因此可持久化、可中断恢复 | 比朴素解释器多一层机制,但支持并行/包容网关 + 中途持久化时是必要的 |
| **jBPM → Kogito** | 从运行时解释改成 AOT 代码生成,适配 Serverless 冷启动 | 只在 K8s 规模化部署划算,进程内库用不上 |
| **Netflix Conductor** | 编排服务与 worker **拉取式**任务分离,任务完成后引擎重新评估状态而非持有内存执行线程 | 解决跨进程跨语言 worker 池问题,本仓"worker"永远是同一个 ASP.NET Core 进程 |
| **Temporal** | 事件历史 + 确定性重放恢复状态,免费拿审计轨迹和崩溃恢复 | 工作流代码必须确定性(不能直接 I/O/随机数/时间),对纯"等人审批"场景是杀鸡用牛刀 |
| **JeecgBoot / RuoYi-Office**(国内生态,Java 一侧) | 嵌入 Flowable 而不自研;三层惯例:引擎基建 / 业务层(任务分派/表单绑定)/ Vue BPMN 设计器 | 说明这个细分市场对"长得像标准 BPMN procdef/task 表"有预期,自研也最好模仿这套词汇。**但这不是国内生态的全貌**——CCFlow 走的是自研且明确不做 BPMN 的路,见第四节 |

### 该借的

1. **Flowable 的 Token/Agenda 模型**——最高价值的一条,天然映射成 SqlSugar 实体(`WorkflowInstance`/`WorkflowToken`/`ActivityInstance`),网关和中途持久化不用发明新解释器。
2. **Zeebe "状态即日志投影"的思路,但只在单机落地**——维护一张不可变执行历史表(任务创建/完成/网关选择…),当前状态从表推导,白得审计轨迹;跳过 Raft/分区,本仓调度中心已有的分布式锁模式够用。
3. **Conductor 的"任务落 inbox 等认领"设计形状**(不是它的 worker-RPC 协议)——直接映射"我的待办"审批收件箱。
4. **JeecgBoot/RuoYi 的分层惯例**——`TenonAdmin.Workflow` 核心执行包 + 业务层(任务分派/通知/表单绑定)分开,对上本仓已有的分层约定。

### 明确该跳过的(过度工程)

- Temporal 的重放确定性模型——第 2 条的历史表已经免费给崩溃恢复,不必强加确定性约束。
- Zeebe 的 Raft/分区/gossip——本仓已有调度中心选主,没必要为工作流再建一套独立分布式共识层。
- Kogito 的 AOT 代码生成流水线——针对 Serverless 冷启动,进程内库用不上。
- Conductor 的跨语言 worker 轮询协议本身——没有跨机器 worker 舰队要解耦。

## 四、CCFlow/驰骋——不可依赖,但需求清单可借(2026-08-17 追加)

### 先说法律边界

CCFlow 是 **GPL-3.0**。TenonAdmin 以 NuGet 分发给消费者构建闭源业务系统,链进 GPL-3.0 会把消费者的整个应用一起传染,这条一票否决。且它与 Flowable(Apache-2.0)/Elsa(MIT)**不是一个性质**:那两个照着源码重写没问题,GPL 的代码"读了再重写"仍有衍生作品风险。

**因此本节全部结论只来自公开 README、官网下载页与在线演示,不涉及源码阅读;后续也不要去读它的源码。**

### 技术栈事实(排除依赖的第二重理由)

> 2026-08-17 **本地浅克隆核对**(`../参考项目/工作流/`)。此前在线转述的「.NET Core 3.0 Preview 5」来自空壳仓 README 的历史安装段与第三方 README,**已作废**。

| 项 | 现状 |
|---|---|
| `CCFlowForNetcore` 独立仓 | **空壳**(仅 README/LICENSE/docs,~2.7 MB,无 `.csproj`)。README 顶栏写明:2023-12-18 已合并进 `ccflow`,请转主仓下载。末次提交 2023-12-25 |
| 主仓 `ccflow` | 真代码在 `.NetCore/`:`CCFlow.sln` + `CCFlow`/`BP.WF`/`BP.En30`/`BP.App` **四个项目全部 `net6.0`**(Sdk Web / classlib)。末次提交 2026-08-12。浅克隆的 `master` 顶层几乎只有 `.NetCore`(外加 doc/SQLScript/Toolkit),未见 Framework 老树 |
| License | 主仓根目录 `LICENSE` = **GPL-3.0**。空壳仓页面标 GPL-2.0——以主仓为准。仍不可链进 NuGet 内核 |
| 现代前端 | 官网下载页仍标注 Vue3 为「商业版、开源日期待定」;开源侧是 H5 那套 |
| 依赖形态观察(本地 `.csproj`) | 大量 `HintPath` 指向 `RefDLL\*.dll` + `System.Data.SqlClient` / Oracle / MySql / Npgsql;与 TenonAdmin「运行时只靠 SqlSugarCore + Microsoft.*」的约束完全两套路 |

### 该借的:国内审批流功能词汇表

这是它唯一但确实值钱的贡献。第一、三节的候选(Flowable/Zeebe/Temporal/Conductor)给的是**执行架构**;CCFlow 在那个维度没有增量,它的模型比它们都老、都简单。它给的是二十年国内落地打磨出的**行为清单**:

前进、后退、转向、转发、撤销、抄送、挂起、草稿、任务池共享、取回审批、项目组、来宾/外部用户。

自研引擎最容易翻车的不是执行模型,是漏掉"退回是回上一节点还是回指定节点""撤回窗口期怎么定""抄送算不算待办"这类没人会主动提、上线第一周必被问的行为。这份清单适合直接当需求验收项用,对应第五节的开放问题。

### 对"要不要 BPMN"的反证

CCFlow 自 2003 年自研、**明确不做 BPMN**,只有四个概念(线性流程、同表单分合流、异表单分合流、父子流程),照样进了大型企业与政府单位。这说明国内审批流市场对 BPMN 2.0 XML 互操作的需求远低于直觉,支持第二节"决策分水岭"里**不做标准互通**那一支——`bpmn-js` 可以更放心地出局。

### 反面教材:两条范围警戒线

1. **它自带 GPM(组织结构 + 菜单权限)**,这正是它只能整体套用、无法当库嵌进别人系统的根因。`TenonAdmin.Workflow` 必须复用现有 RBAC、机构树与数据范围,一行自己的组织权限都不能带——本仓原有约束,CCFlow 给出了违反它的代价。
2. **节点/流程属性 300+ 项、手册四十余万字**,是"全配置无代码"走到尽头的样子。配置面要有意识地收窄,不以此为榜样。

### 一个待决策的信号

CCFlow 把**表单引擎与流程引擎的耦合**当头号卖点(流程引擎驱动表单引擎做数据处理与权限控制)。这对第七节最后一个开放问题是个反向信号:在这个市场里只做"审批 + 备注"、表单甩给消费者,可能被认为只做了半个产品。但这也正是它复杂度的来源,值得单独决策而非顺手纳入。

## 五、国内钉钉风产品与全网补检(2026-08-17 第二轮)

本地克隆根目录:`../参考项目/工作流/`(索引见该目录 `README.md`)。本节只记**选型影响**,不展开逐仓精读。

### 市场形态修正

第一轮把国内生态概括成「Jeecg/RuoYi 嵌 Flowable」。更准确的图景是**两条并行线**:

1. **BPMN 线**:JeecgBoot / RuoYi-Office / **FlyFlow** —— 引擎用 Flowable,前端尽量做成钉钉/飞书。
2. **中国式审批线**:不宣称 BPMN 互通——**Warm-Flow**、**FlowLong**、**AntFlow(.NET)**、**CCFlow**、以及大量「只开源设计器」的产品(**wflow**、**jsonflow-ui**、**smart-flow-design**)。前端交互母体高度同源:**StavinLi/Workflow**(Vue2/Vue3/React)。

对本仓「单机 DB、人工审批链、双前端模板」而言,**第 2 条线的产品形状比第 1 条更贴近**;引擎实现仍应自研(Flowable 嵌不进 .NET;GPL/附加协议也不能链)。

### 你点名的仓(本地已拉)

| 项目 | 本地目录 | 价值 | 风险/注意 |
|---|---|---|---|
| wflow / wflow-web-next | `wflow` `wflow-web-next` | 表单+审批树一体设计器 | 开源偏前端;完整后端在 Pro |
| FlyFlow | `flyflow` `flyflow-vue3` | 钉钉风产品壳如何包 Flowable | **嵌 Flowable**;附加协议禁转售源码 |
| jsonflow-ui | `jsonflow-ui` | 自动布局 + JSON 交互的设计器 | 开源≠完整引擎;商业版另算 |
| smart-flow-design | `smart-flow-design` | antdv 组件库形态 | 看封装方式,非执行引擎 |
| Warm-Flow | `warm-flow` | 轻量表(~7)、经典+钉钉双模式、jar 设计器 | Java;Apache-2.0,可对照表结构/API 面 |
| Rete.js | `rete` | — | **视觉编程**,不是 OA 审批;降权 |
| LogicFlow | `LogicFlow` | 通用流程图底座 | 已在第二节 |

### 检索新发现(建议优先精读)

| 项目 | 本地目录 | 为什么重要 |
|---|---|---|
| **AntFlow.net** | `AntFlow.net` | 纯 .NET + 钉钉风 UI,依赖面相对干净(ASP.NET + FreeSql)——**与 TenonAdmin 技术栈最近邻的产品对照** |
| **FlowLong** | `flowlong` | 国产 JSON 模型 + 中国式操作清单;Apache-2.0 带附加协议 |
| **StavinLi Workflow-Vue3 / React** | `Workflow-Vue3` `Workflow-React` | 钉钉树状设计器事实源头;`web/` 与 `web-react/` 可各对一版 |
| bpmn-js / vue-flow / xyflow / X6 | 同名目录 | 补全第二节画布底座本地可读副本 |

### 对既有决策的增量影响

- **前端**:若不做 BPMN XML 互通,优先研究 **StavinLi 树状交互** +(可选) LogicFlow/Vue Flow/React Flow 作自由图画布;Rete 出局。
- **后端**:执行模型仍以 Flowable Token/Agenda + 单机历史表为准;表结构与「退回/转办/委托/会签」操作面可对照 Warm-Flow / FlowLong / AntFlow.net,**不要**对照 CCFlow 源码算法(GPL)。
- **范围警戒**:wflow/jsonflow「设计器开源、引擎商业」很常见——需求文档里要把「设计器」和「执行引擎」拆开写,避免一期范围膨胀成低代码平台。

## 五附、商业 / 半商业公开源码对照(2026-08-17 第五轮)

**立场**:商业或半商业仓也可以本地浅克隆——只作产品、表结构、宿主集成方式的参考;**落地代码仍自研 `TenonAdmin.Workflow`,不引入依赖、不复制源码**。商业条款约束的是「能不能当依赖卖」,不是「能不能读」。

本地已补:`WorkflowEngine.NET`、`Slickflow`、`camunda`(C8)、`camunda-bpm-platform`(C7)、`operaton`、`AntFlow` / `AntFlow-activiti`(Java 主线)、`elsa-studio`。索引见 `../参考项目/工作流/README.md` 节 D。

| 项目 | 对本仓的参考价值 | 不做什么 |
|---|---|---|
| **Slickflow** | 国产 .NET 可嵌入 BPMN 引擎(NET8)+ 设计器;看宿主挂载、命令面、表形状 | 不嵌其 NuGet;产品 UX 仍偏 BPMN,审批树以 StavinLi 为准 |
| **WorkflowEngine.NET** | 成熟 .NET 嵌入式引擎 + HTML5 设计器拆分方式 | 商业授权 + 免费上限;不可当依赖 |
| **AntFlow (Java)** | 与已有 `AntFlow.net` 对照同一产品线的能力清单 | Java/Activiti 系不进 .NET 进程 |
| **Camunda 7 / Operaton** | 与 Flowable 并列的经典 BPMN 运行时形状 | JVM;不嵌 |
| **Camunda 8** | 继续只借「历史即状态」单机简化 | 不学 Zeebe 集群;自托管有 Camunda License |
| **Elsa Studio** | .NET 设计器壳(MudBlazor)怎么拆模块 | 偏自动化编排 UI,不是钉钉审批树 |

**对既有决策无翻转**:仍自研引擎;前端优先钉钉树状;商业仓提升的是「.NET 嵌入式引擎邻居」样本密度(Slickflow / WorkflowEngine.NET),不是选型改投。

## 六、JNPF 7.0——权限 × 流程的整机对照(2026-08-18 第六轮)

本地:`../参考项目/工作流/jnpf`(主树 `jnpf7.0/`)。分仓笔记:`jnpf/_TENON_REF.md`。

### 先说法律

JNPF 是**合同交付的商业产品**(福建引迈)。仓根没有 Apache/MIT/AGPL;`jnpf-java-boot/README.md` 写明交付范围「以双方签订的合同约定为准」;引擎 jar 经 Allatori 混淆。与 CCFlow 的 GPL 传染不同路径,结论一样:**可读设计,源码/jar 不进 Tenon / NuGet**。

### 它是什么(避免和 Warm-Flow 搞混)

不是轻量引擎 SDK。它是低代码整机:在线表单 + 工作流 + 组织权限 + 多租户。工作流内部再拆两层:

| 层 | 位置 | 做什么 |
|---|---|---|
| 图运行时 | `jnpf-workflow-core` + 独立宿主 `:31000` | 嵌 **Flowable**(6.8/7.0);`ITaskService` 只有 complete / jump / back / retract / compensate |
| OA 业务 | `jnpf-java-boot/jnpf-flowable` | 中国式动词、经办、表单字段权限、候选人、委托、抄送;表前缀 `workflow_*`(与 ACT_* **分库**) |

定义是**混合**:拓扑走 BPMN XML(`workflow_version.f_xml` → 部署成 `*.bpmn20.xml`),节点业务走 JSON(`workflow_node.f_node_json`:审批人、`formOperates`、按钮、超时、会签)。前端设计器是 bpmn-js(`jnpf-bpmn`),对本仓画布选型**无增量**(已否 BPMN 互通)。

命名陷阱:JNPF 的 `workflow_task` = **流程实例**(Tenon `WfInstance`);`workflow_operator` = **经办/待办**(Tenon `WfTask`)。对照代码时先换算。

### 该借的:权限四层,不要揉进一张授权表

这是本轮相对前 31 仓最大的增量。JNPF 的组织/角色仍在 `jnpf-permission`(`base_organize` / `base_authorize`),工作流**不自带第二套 GPM**——这点它比 CCFlow 干净,和 Tenon「Workflow 零自带组织模型」同向。

流程相关的权限却**故意不全部进授权表**:

| 层 | 落点 | 含义 | Tenon 落点 |
|---|---|---|---|
| 发起 | `base_authorize.itemType=flow` + 模板 `visibleType` 全局/权限 | 谁能启动该模板 | 定义上的发起范围,或复用角色菜单;不新建 GPM |
| 待办 | 运行时物化 `workflow_operator` | 收件箱按办理人,不再每次查菜单 RBAC | 已有 `WfTask` / `WfTaskActor` |
| 字段 | 节点 JSON `formOperates[{read,write,required}]` | 该节点打开表单时的字段矩阵 | 已预留 `formPerms`(M3 启用) |
| 按钮 | 节点 JSON `ButtonModel`(hasAuditBtn 等) | 审批页出现哪些动词 | M2/M3 节点 `btnInfo`;默认值满足「≤5 可见项」 |
| 事后可见 | 我发起 / 已办 / 抄送 / 监控 | **按参与**,不用组织数据范围扫全表实例 | 列表语义按此切;数据范围继续只管业务实体 |

数据范围本身是另一套:每模块 condition JSON 方案(`AuthorizeConditionEnum`:当前用户/组织/岗位及下级)。那是低代码列表过滤, **不要**拿来替换 Tenon 的 `DataScopeType` + `IOrgScoped`。

### 该借的:引擎切分与词汇补丁

- 图推进 API 保持薄;加签/转审/委托/协办长在业务服务,不长进 Agenda。  
- 空审批人(`ErrorRuleEnum`)对照已有三级策略;解析后过滤(`ExtraRuleEnum` 同一部门/角色…)可做 Provider 参数。  
- 动词增量(补 FlowLong,不进 MVP):转审≠转办、审批节点≠办理节点、待签/在办、协办、复活、监控箱。  
- 逐级主管:发起时把组织快照写入 `workflow_launch_user`——Tenon 连续多级主管若运行时现查 `DirectorId`,M2 要写死「发起时快照 vs 实时」以免调岗改写在途。

### 明确不抄

嵌 Flowable;BPMN XML;二十多张 `workflow_*` + ACT_*;任务流/决策流/规则集;组织/岗位/分组/分级管理员进 Workflow 包;`id::btn` 权限串(本仓权限码仍是 `{METHOD}:/route`)。

### 对既有决策

**无翻转**。自研 JSON 树、9 表冷启动、`IApproverProvider`、零自带组织、钉钉树双模板——全部维持。JNPF 把「权限交点」从开放问题收成可执行的分层清单,供 M2 发起范围 / M3 字段矩阵与按钮开关对照。

## 七、OpenWorkflow——可靠执行层专项对照（2026-08-23）

本地浅克隆：`C:\HuHuHu\参考项目\工作流\openworkflow`；对照提交 `46dcc85d230bb54894dc4bab022a1ce34cc11c13`，包版本 `0.9.2`，Apache-2.0。分仓笔记：`openworkflow/_TENON_REF.md`；详细源码对照另见 [`openworkflow-reference-2026-08-23.md`](./openworkflow-reference-2026-08-23.md)。

它是 TypeScript durable/resumable workflow 框架，不是审批产品。工作流以代码函数定义，worker 从数据库领取 run，每次从头重放函数；完成过的 `step.run` 从 `step_attempts` 返回缓存结果，新步骤才执行。`step.sleep`、子工作流和 `waitForSignal` 会持久化挂起，`availableAt` 同时承担唤醒时间与 worker 租约，SQLite/Postgres 是 `Backend` 的两个 Adapter。

### 与 Tenon 当前实现的正面对照

| 维度 | OpenWorkflow | TenonAdmin.Workflow | 判断 |
|---|---|---|---|
| 产品模型 | 代码优先自动化编排 | JSON 定义快照 + 人工审批任务/办理人/表单权限 | **Tenon 方向正确**；两者不是替代关系 |
| 执行指针 | 函数重放 + step attempt memoization | `WfToken` + 单事务 `WfAgenda`，停在活跃 `WfTask` | 人工审批不需要重放；继续保留表状态恢复 |
| 并发 | worker 原子领取、租约、心跳、崩溃再领取 | 用户动作按 `WfTask.Version` CAS；调度器选主 | 前台审批已有正确 CAS；后台超时/自动节点仍需完整领取语义 |
| 幂等 | run 支持 idempotency key；完成步骤按 durable step key 复用 | 命令无通用 `RequestId`；`BusinessKey` 只是普通索引 | **明确缺口**：CAS 防双赢，不等于重复请求返回同一结果 |
| 长等待 | durable sleep / signal / child workflow | 活跃任务持久化；`DueTime`/`TimeoutFired` 已预留，超时 Job 尚未收口 | OpenWorkflow 验证了“持久化唤醒而非阻塞线程”的路线 |
| 版本 | run version + 业务代码保留旧分支；step 改名会破坏在途重放 | 实例固定 `WfDefinitionVersion` JSON 快照 | 无代码审批里 Tenon 的不可变定义快照更稳、更容易解释 |
| 可观测性 | run/attempt/error/retry + dashboard | `WfHistory`/`WfHisTask`；事务后通知失败静默吞掉 | 历史有基础，但缺 request/attempt/最后错误关联 |

### deep-module 判断

OpenWorkflow 的 `step.run/sleep/runWorkflow/sendSignal/waitForSignal` 是小 **Interface**，持久化、租约、重试、恢复藏在 **Implementation** 中，形成高 **Depth**、高 **Leverage** 的 **Module**；`Backend` 是真实 **Seam**，Postgres/SQLite **Adapter** 共用契约测试，**Locality** 很好。代价也很清楚：确定性与版本兼容复杂度泄漏到工作流代码和 durable step 名称中。

Tenon 的 `IWorkflowEngine.ExecuteAsync(IWfCommand)` + Agenda 对审批域同样够深；人员解析、表单绑定、通知是现实存在的业务 Seam。不要因为 OpenWorkflow 接口多就为每张表再造 Repository/Adapter。真正值得新增的 Seam 只有“命令去重/后台工作领取/自动节点 attempt”等能通过删除测试、且已有两个实现或消费者替换需求的边界。

### 借鉴优先级

1. **M2b：完成超时闭环与通知观测。** 建任务计算 `DueTime`；`WfTimeoutJob` 以任务 `Version` CAS 处理到期动作；失败通知写日志/指标。继续复用 ADR-0004 调度器，不建 OpenWorkflow worker 集群，也不为纯 SignalR 提示建 outbox。
2. **M2c：给所有变更命令增加请求幂等。** API 接受 `RequestId/IdempotencyKey`，以“组织/租户 + 实例/任务 + 操作 + 操作者 + request id”唯一约束保存操作回执；重复请求返回第一次结果。它解决 HTTP 重试与双击，和现有 `Version` CAS 互补。
3. **M2c：建立四库持久化契约测试。** 针对回执唯一性、CAS、事务回滚、超时与人工动作竞争建立 provider-neutral 测试，不为抽象好看照搬 OpenWorkflow 的 Backend Interface。
4. **M3：为机器动作增加 attempt。** Webhook/自定义节点记录 `OperationId/RequestId/Attempt/LastError/NextRetryAt`；需要保证执行的外部副作用使用 outbox + 领取/fence，人的审批动作不自动重放。
5. **M3b：AI Decision 作为可靠节点 SPI 的首个战略 Adapter。** M3a 先完成 execution/attempt/deadline/fence/outbox；M3b 同一阶段交付结构化 proposal、schema/policy、shadow mode、低风险自动放行和人工 fallback。模型供应商 SDK 不进入工作流内核。

### 明确不借

- 不把 JSON 审批定义改成 C#/TypeScript 代码工作流，不引入确定性重放。
- 不引入独立 worker 舰队、通用子工作流、signal 字符串总线或另一套调度器。
- 不用 `step.run` 包人工审批；审批等待继续由 `WfTask/WfTaskActor/WfToken` 表达。
- 不把 OpenWorkflow 的 step memoization 误当 exactly-once：worker 在步骤完成落盘前崩溃，外部副作用仍可能再次执行；未来 Webhook 必须自带幂等键/outbox。

**对既有决策无翻转。** OpenWorkflow 不是新的引擎候选，而是补上此前调研较弱的“可靠执行层”样本。它强化了 M2b 超时 Job、命令幂等和 M3 Webhook attempt/outbox 的必要性，同时也反证人工审批无需 Temporal 式重放。

## 八、Elsa 3 / Slickflow.AI——.NET AI × 工作流专项对照（2026-08-23）

本地仓均已存在，无需新增克隆：`../参考项目/工作流/elsa-core` 已 ff-only 更新到 `ae146a17655664645f3761720b998d65f587344a`；`Slickflow` 已是远端最新 `646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e`。详细报告见 [`elsa3-slickflow-ai-reference-2026-08-23.md`](./elsa3-slickflow-ai-reference-2026-08-23.md)。

先纠正称呼：这两个项目都属于 .NET 工作流生态，但不能并称为“开箱 AI 审批库”。Elsa 3 的 AI 是 Weaver 设计/诊断 Copilot；Slickflow 才把 LLM/RAG/Agent 做成运行时节点，但“AI 审核”仍是节点输出与规则网关的组合，不是引擎内置的审批安全策略。

Slickflow 官方[文档中心](https://www.slickflow.com/wiki/index)及 [LLM](https://www.slickflow.com/wiki/llmguide)、[RAG](https://www.slickflow.com/wiki/ragguide)、[多智能体](https://www.slickflow.com/wiki/multiagent)指南描述了更完整的产品形态，但部分示例 API（如 `[LLMTask]`、`[AgentRole]`、`RunMultiAgentAsync`、`IRAGService`、Human-in-the-Loop 节点）在固定提交中未找到。调研据此把它们归为“文档方向”，不当作当前源码已交付能力；详细逐项核对见专项报告 §3.4。

| 维度 | Elsa Workflows 3 | Slickflow.NET / Slickflow.AI |
|---|---|---|
| 当前身份 | 官方主仓；稳定 `3.7.1`，AI 模块随 `3.8.0-rc2` 预发布；MIT | 官方主仓；GitHub release `V5.0.0`，项目/NuGet 仍标 `3.5.0`；MIT |
| AI 所在位置 | `Elsa.AI.Abstractions/Host/Copilot/Persistence` + Studio Weaver | `Slickflow.AI` + 引擎 `AIServiceNode` + BPMN 设计器属性面板 |
| 主要用途 | 对话查询 Activity/定义/实例/incident；生成/修改流程定义 proposal；诊断 | 运行时调用 LLM/RAG/Agent，将结果写回流程变量；另有文生 BPMN、RAG、Agent/MCP |
| “审核”含义 | 用户审核 AI 生成的**流程定义提案**，不是 AI 审核业务单据 | 模型输出结构化结果/score，确定性网关路由到接受或人工复核 |
| 安全亮点 | Provider SDK 隔离；RBAC/租户过滤、脱敏、tool enablement、proposal-only mutation、audit | AI 结果不必直接改流程状态，可先写变量再走规则分支 |
| 当前短板 | 3.8 RC / Weaver 仍在产品化；实际 Provider Adapter 目前是 GitHub Copilot SDK；无运行时 AI 审批 Activity | Engine 直接引用 AI 项目；两套执行路径行为不同，经典路径 RAG 退化为 LLM；存在 sync-over-async；缺统一 schema 校验、attempt/deadline/重试/幂等/人工兜底 Module |

### 真正值得借的两条线

1. **运行时 AI 预审——借 Slickflow 的形状，不借实现。** `AI structured output → JSON schema validation → deterministic threshold/rule → 自动路由或人工复核`。模型只给建议，不直接调用 Approve/Reject；低置信度、异常、超时、无效 JSON 一律转人工。
2. **AI 流程设计/诊断——借 Elsa 的治理。** AI 对定义的创建/修改只生成 proposal；服务端做权限过滤、脱敏、模型校验、图 diff 和 validation；用户显式 approve/apply 后才保存草稿，并记录 prompt/tool/proposal/apply audit。AI 不直接发布流程，也不改在途版本。

Slickflow 固定源码的完整执行链、两套 executor 差异、LLM/RAG/Agent 真实完成度、Tenon 目标 Interface、execution/attempt/AI decision/outbox 概念模型、安全不变量与验收指标均已固化在专项报告 §3–§5。后续开发不再重新通读该参考仓；只有上游 commit 变化时做增量核对。

### 对开发阶段的影响

- **M2b/M2c 无新增 AI 工作**：先完成审批功能、超时闭环、命令幂等与四库契约测试。
- **M3a**：Webhook/节点 SPI 建好可靠机器动作执行 Module（execution、attempt、deadline、退避、fence、outbox），统一所有机器节点入口。
- **M3b**：交付最小 AI Decision Node，先 shadow、再按场景评测开放低风险自动放行；自动拒绝不进 V0，异常统一转人工。
- **M3+**：增加证据/RAG、只读 Agent tools、更多 Provider；设计器有真实需求后再做 Elsa 式 Copilot proposal/diff/audit。

### deep-module 判断

Elsa 的 AI Host 把 Provider、tool、context、proposal、audit 分开，外部 Interface 不泄漏 Copilot SDK，Provider Adapter Seam 有较好 Locality；但当前只有一个实际 Adapter，Tenon 不应提前照抄全部抽象。Slickflow 的 `AIServiceNode` Interface 对设计者很直观、Leverage 高，但 Implementation 把远程模型调用、引擎推进和 Provider 实现耦在一起，Depth 被可靠性缺口抵消。Tenon 应把“可靠自动节点执行”做成深 Module，再让 AI 只是其中一个 Adapter。

**底层路线不翻转，产品优先级上调。** OpenWorkflow 提供可靠执行参考；Slickflow 提供运行时 AI 节点产品形状；Elsa 提供 AI 设计/诊断治理。三者分别解决不同问题，不能互相替代。AI Decision 从“M3+ 可选”前移为 M3b 战略交付，但仍必须建立在 M2c 幂等和 M3a 可靠机器节点 Module 之上。

## 九、写需求前建议先定的问题

> 2026-08-17:以下问题已在 [`workflow-design-plan-2026-08-17.md`](./workflow-design-plan-2026-08-17.md) §九给出建议决议(不做 BPMN 互通、钉钉树自研×2、Provider 分派、表单出圈等),本节保留原始提问作对照。

- 是否需要 BPMN 2.0 XML 互操作(决定前端画布选型)?——第五节后更倾向「不需要」,前端走钉钉树状。
- 网关支持范围:排他/并行是否够用,还是要包容网关、多实例(会签/或签)?
- 任务分派复用现有 RBAC(按角色/岗位找审批人)+ 多机构数据范围,还是要单独的候选人表达式?
- 通知复用现有 Notice/SignalR 模块(`docs/adr/0003-realtime-signalr.md`),不新建推送通道。
- 表单绑定:动态表单引擎是否在范围内,还是先只做"审批 + 备注",表单留给消费者自己接。——注意国内产品常把表单设计器与流程设计器绑死,一期范围要显式拆开。
- 前端交互形态:BPMN 自由图(LogicFlow/Vue Flow/React Flow)还是钉钉树状(StavinLi 系)?双模板是否各跟一套。

## 来源

第一轮(库对比):[elsa-workflows/elsa-core](https://github.com/elsa-workflows/elsa-core)、[danielgerlag/workflow-core](https://github.com/danielgerlag/workflow-core)、[Camunda 8 licensing](https://docs.camunda.io/docs/reference/licenses/)、[bpmn-io/bpmn-js](https://github.com/bpmn-io/bpmn-js)、[didi/LogicFlow](https://github.com/didi/LogicFlow)、[xyflow/xyflow](https://github.com/xyflow/xyflow)、[bcakmakoglu/vue-flow](https://github.com/bcakmakoglu/vue-flow)。

第二轮(架构借鉴):[Bernd Rücker — Zeebe distributed state machine](https://blog.bernd-ruecker.com/how-we-built-a-highly-scalable-distributed-state-machine-f2595e3c0422)、[Camunda 8 Zeebe technical concepts](https://docs.camunda.io/docs/components/zeebe/technical-concepts/technical-concepts-overview/)、[Flowable BPMN execution](https://deepwiki.com/flowable/flowable-engine/4.1-bpmn-execution)、[Flowable: Top 10 advances since Activiti](https://www.flowable.com/blog/top-10-advances-flowable-made-since-activiti)、[Baeldung: Kogito](https://www.baeldung.com/java-kogito)、[Netflix TechBlog: Conductor](https://netflixtechblog.com/netflix-conductor-a-microservices-orchestrator-2e8d4771bf40)、[Temporal: Events and Event History](https://docs.temporal.io/workflow-execution/event)、[Temporal Workflow docs](https://docs.temporal.io/workflows)、[JeecgBoot + Flowable 集成](https://blog.gitcode.com/c2bdfcc10839f95d6e36be0b2ee390e6.html)(技术博客,非一手文档,方向性参考)。

第三轮(2026-08-17,国产自研引擎):[opencc/CCFlow](https://gitee.com/opencc/ccflow)、[opencc/CCFlowForNetcore](https://gitee.com/opencc/CCFlowForNetcore)(空壳仓)、[驰骋官网下载页](https://ccflow.org/Down.html)。**本地浅克隆**:`../参考项目/工作流/`。**GPL-3.0:可读结构,不复制源码进本仓。**

第四轮(2026-08-17,钉钉风产品与全网补检):[willianfu/wflow](https://github.com/willianfu/wflow)、[wflow-web-next](https://gitee.com/willianfu/wflow-web-next)、[junyue/flyflow](https://gitee.com/junyue/flyflow)、[JackRolling/jsonflow-ui](https://github.com/JackRolling/jsonflow-ui)、[smart-flow/smart-flow-design](https://github.com/smart-flow/smart-flow-design)、[dromara/warm-flow](https://github.com/dromara/warm-flow)、[retejs/rete](https://github.com/retejs/rete)、[StavinLi/Workflow-Vue3](https://github.com/StavinLi/Workflow-Vue3)、[StavinLi/Workflow-React](https://github.com/StavinLi/Workflow-React)、[aizuda/flowlong](https://github.com/aizuda/flowlong)、[mrtylerzhou/AntFlow.net](https://github.com/mrtylerzhou/AntFlow.net)、[antvis/X6](https://github.com/antvis/X6)。索引与降权说明见本地 `../参考项目/工作流/README.md`。

第五轮(2026-08-17,商业/半商业公开源码对照):[optimajet/WorkflowEngine.NET](https://github.com/optimajet/WorkflowEngine.NET)、[besley/Slickflow](https://github.com/besley/Slickflow)、[camunda/camunda](https://github.com/camunda/camunda)、[camunda/camunda-bpm-platform](https://github.com/camunda/camunda-bpm-platform)、[operaton/operaton](https://github.com/operaton/operaton)、[mrtylerzhou/AntFlow](https://github.com/mrtylerzhou/AntFlow)、[mrtylerzhou/AntFlow-activiti](https://github.com/mrtylerzhou/AntFlow-activiti)、[elsa-workflows/elsa-studio](https://github.com/elsa-workflows/elsa-studio)。**可读参考,不引入依赖、不复制源码。**

第六轮(2026-08-18,JNPF 7.0 商业整机):本地 `../参考项目/工作流/jnpf`。一手来源:`jnpf7.0/jnpf-java-boot/README.md`(合同交付)、`jnpf-workflow-core`(Flowable 适配 + `ITaskService`)、`jnpf-flowable`(RecordEnum / OperatorEnum / FlowNature / ButtonModel / TaskEntity=实例 / OperatorEntity=经办)、`jnpf-permission` + `AuthorizeConst`(含 `itemType=flow`)、`jnpf-web-apps-main/.../useFlowForm.ts`(字段矩阵)。公开站点 [jnpfsoft.com](https://www.jnpfsoft.com)。**合同许可:可读设计,不复制源码。**

OpenWorkflow 专项(2026-08-23,可靠执行层):[openworkflowdev/openworkflow](https://github.com/openworkflowdev/openworkflow)、[官方 Workflows 文档](https://openworkflow.dev/docs/workflows)、本地 `../参考项目/工作流/openworkflow`（固定提交 `46dcc85d230bb54894dc4bab022a1ce34cc11c13`）。重点源码：`packages/openworkflow/{client,core,worker,postgres,sqlite,testing}` 与 `ARCHITECTURE.md`。**Apache-2.0；只借幂等、持久化唤醒、租约、重试和契约测试，不引入代码工作流重放。**

Elsa 3 / Slickflow.AI 专项(2026-08-23):[Elsa Core 固定提交](https://github.com/elsa-workflows/elsa-core/tree/ae146a17655664645f3761720b998d65f587344a)、[Elsa.AI.Host 3.8.0-rc2](https://www.nuget.org/packages/Elsa.AI.Host/3.8.0-rc2)、[Elsa.AI.Copilot 3.8.0-rc2](https://www.nuget.org/packages/Elsa.AI.Copilot/3.8.0-rc2)、[Slickflow 固定提交](https://github.com/besley/Slickflow/tree/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e)、[Slickflow 官方 Wiki](https://www.slickflow.com/wiki/index)、[Slickflow.Engine 3.5.0](https://www.nuget.org/packages/Slickflow.Engine/3.5.0)。本地 `../参考项目/工作流/{elsa-core,Slickflow}`。**两仓 MIT；借治理和产品形状，不复制实现、不引入整引擎；Wiki 超前于固定源码的条目仅作路线参考。**
