# 工作流模块调研(自研引擎,参考不引用)

调研日期:2026-08-10。范围:为 TenonAdmin 自研一个 BPMN 风格的审批工作流卫星包(`TenonAdmin.Workflow`)收集设计参考——**不打算直接依赖别人的执行引擎**,只借架构思路;前端可视化画布则可能直接引用现成库。

## 结论

TenonAdmin 要做的是一个**单机可写、DB 驱动、以人工审批链为核心场景**的 BPMN 风格引擎,跑在一个已经有过一次分布式协调经验的内核里(调度中心的选主模式)。不需要为工作流再解决一次分布式共识问题。最值得抄的架构决定来自 **Flowable**(token/agenda 执行模型)、**Zeebe**(状态即日志投影的思路,但只落单机)、**Netflix Conductor**(任务收件箱而非引擎主动推送)、**JeecgBoot/RuoYi-Office**(引擎基建/业务分派/设计器三层惯例)。**Temporal 的确定性重放**、**Zeebe 的 Raft/分区**、**Kogito 的 AOT 代码生成**、**Conductor 的跨语言 worker 轮询协议**都是过度工程,明确不抄。

前端画布倾向直接引用现成库而非自研:严格 BPMN 2.0 用 `bpmn-js`,更贴近钉钉/飞书审批流形状用 `LogicFlow` 或 `React Flow`(`web-react/`)+ `Vue Flow`(`web/`)组合。

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
| **JeecgBoot / RuoYi-Office**(国内生态) | 都是嵌入 Flowable,不自研引擎;三层惯例:引擎基建 / 业务层(任务分派/表单绑定)/ Vue BPMN 设计器 | 说明这个细分市场对"长得像标准 BPMN procdef/task 表"有预期,自研也最好模仿这套词汇 |

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

## 四、写需求前建议先定的问题

- 是否需要 BPMN 2.0 XML 互操作(决定前端画布选型)?
- 网关支持范围:排他/并行是否够用,还是要包容网关、多实例(会签/或签)?
- 任务分派复用现有 RBAC(按角色/岗位找审批人)+ 多机构数据范围,还是要单独的候选人表达式?
- 通知复用现有 Notice/SignalR 模块(`docs/adr/0003-realtime-signalr.md`),不新建推送通道。
- 表单绑定:动态表单引擎是否在范围内,还是先只做"审批 + 备注",表单留给消费者自己接。

## 来源

第一轮(库对比):[elsa-workflows/elsa-core](https://github.com/elsa-workflows/elsa-core)、[danielgerlag/workflow-core](https://github.com/danielgerlag/workflow-core)、[Camunda 8 licensing](https://docs.camunda.io/docs/reference/licenses/)、[bpmn-io/bpmn-js](https://github.com/bpmn-io/bpmn-js)、[didi/LogicFlow](https://github.com/didi/LogicFlow)、[xyflow/xyflow](https://github.com/xyflow/xyflow)、[bcakmakoglu/vue-flow](https://github.com/bcakmakoglu/vue-flow)。

第二轮(架构借鉴):[Bernd Rücker — Zeebe distributed state machine](https://blog.bernd-ruecker.com/how-we-built-a-highly-scalable-distributed-state-machine-f2595e3c0422)、[Camunda 8 Zeebe technical concepts](https://docs.camunda.io/docs/components/zeebe/technical-concepts/technical-concepts-overview/)、[Flowable BPMN execution](https://deepwiki.com/flowable/flowable-engine/4.1-bpmn-execution)、[Flowable: Top 10 advances since Activiti](https://www.flowable.com/blog/top-10-advances-flowable-made-since-activiti)、[Baeldung: Kogito](https://www.baeldung.com/java-kogito)、[Netflix TechBlog: Conductor](https://netflixtechblog.com/netflix-conductor-a-microservices-orchestrator-2e8d4771bf40)、[Temporal: Events and Event History](https://docs.temporal.io/workflow-execution/event)、[Temporal Workflow docs](https://docs.temporal.io/workflows)、[JeecgBoot + Flowable 集成](https://blog.gitcode.com/c2bdfcc10839f95d6e36be0b2ee390e6.html)(技术博客,非一手文档,方向性参考)。
