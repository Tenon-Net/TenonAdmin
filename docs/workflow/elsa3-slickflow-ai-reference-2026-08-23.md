# Elsa Workflows 3 与 Slickflow.AI 源码研究

> 文档入口：[`README.md`](./README.md)
> 日期：2026-08-23
> 研究范围：仅检查两个已存在的本地官方仓、仓内官方文档、Slickflow 官方 Wiki 以及官方 GitHub/NuGet 信息；未重新 clone，未修改参考仓源码。
> 固定基线：Elsa `ae146a17655664645f3761720b998d65f587344a`；Slickflow `646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e`。

## 结论先行

1. **“OpenWorkflow 是 .NET AI 审批库”是误认。** OpenWorkflow 是 TS/Node 方向的工作流库，不能拿它证明 .NET 生态已经存在 AI 审批能力。本报告只纠正归类，不再把 OpenWorkflow 与 Elsa/Slickflow 混作同一项目。
2. **Elsa 当前主仓是 Elsa Workflows 3。** 固定 commit 的核心源码面向 `net8.0;net9.0;net10.0`，仓库许可证为 MIT。该 commit 位于 3.8 release-candidate 之后的主线开发状态；官方 NuGet 当前稳定版仍是 3.7.1，因此不能把该 commit 简写成一个已发布的“3.8 正式版”。
3. **Elsa AI 是 authoring/diagnostics copilot，不是运行时业务审批。** 它为工作流查询、诊断、创建设计稿和更新设计稿提供 AI 工具与提案治理；提案写入独立存储，不直接持久化工作流定义。源码中没有 AI 自动同意/拒绝业务审批任务的运行时活动。
4. **Slickflow 有真正参与流程运行的 LLM/RAG/Agent 节点，但没有内置“AI 审批”。** AI 节点把模型响应写成流程变量；人工审批仍由 `ApprovalStatus`、任务完成和通过率逻辑处理。README/Wiki 中的 `confidence → human review`、`ApprovalDecisionAgent`、Human-in-the-Loop 是组合或文档示例，不是当前固定源码已经实现的内置路由或审批语义。
5. TenonAdmin.Workflow 最值得吸收的是：Elsa 的 provider/tool/proposal 安全边界，以及 Slickflow 的 AI 节点、工具适配器和规则后处理思路。M3 应同时交付可靠自动节点执行层和最小 `AI Decision Node` 纵切；模型只生成 proposal，服务端 schema/policy 决定低风险自动放行或转人工，不让 LLM 直接调用同意/拒绝。RAG、Agent 和设计 Copilot 后置。

## 1. 固定基线、版本与许可证

| 项目 | 固定 commit | 源码版本与目标框架 | 许可证 | 判断 |
| --- | --- | --- | --- | --- |
| Elsa | [`ae146a1`](https://github.com/elsa-workflows/elsa-core/commit/ae146a17655664645f3761720b998d65f587344a) | 主仓 README 明确为 Elsa 3；[`src/Directory.Build.props#L4-L6`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/Directory.Build.props#L4-L6) 为 `net8.0;net9.0;net10.0` | [MIT](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/LICENSE) | 当前主仓确为 Elsa 3 |
| Slickflow | [`646392d`](https://github.com/besley/Slickflow/commit/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e) | [`Slickflow.AI.csproj#L4-L15`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.AI/Slickflow.AI.csproj#L4-L15) 与 [`Slickflow.Engine.csproj#L4-L16`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Slickflow.Engine.csproj#L4-L16) 均为 `net8.0`、版本 `3.5.0` | [MIT](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/LICENSE) | 源码/NuGet 元数据是 3.5.0；不能用 GitHub Release 标题“5.0.0”替代源码事实 |

Elsa 官方 NuGet 的 [`Elsa.Workflows.Core`](https://www.nuget.org/packages/Elsa.Workflows.Core/) 当前稳定版是 3.7.1，AI 包 [`Elsa.AI.Abstractions`](https://www.nuget.org/packages/Elsa.AI.Abstractions/3.8.0-rc2)、[`Elsa.AI.Host`](https://www.nuget.org/packages/Elsa.AI.Host/3.8.0-rc2) 和 [`Elsa.AI.Copilot`](https://www.nuget.org/packages/Elsa.AI.Copilot/3.8.0-rc2) 为 3.8.0-rc2。因此，本报告把固定 commit 表述为“Elsa 3 主线、3.8 RC 之后”，不虚构正式版本号。

Slickflow 的官方 [`Slickflow.Engine`](https://www.nuget.org/packages/Slickflow.Engine/) 可见 3.5.0；但官方 NuGet 未找到可独立获取的同名 `Slickflow.AI` 包，而 Engine 包元数据又声明了该依赖。这是实际采用前必须复核的包发布一致性风险。

## 2. Elsa Workflows 3：AI 能力到底在哪里

### 2.1 模块边界

固定 commit 的 AI 代码分为：

- `Elsa.AI.Abstractions`：provider、orchestrator、tool、proposal 等中立契约；项目自身说明见 [`Elsa.AI.Abstractions.csproj#L3-L8`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Abstractions/Elsa.AI.Abstractions.csproj#L3-L8)。
- `Elsa.AI.Host`：会话编排、上下文收集、工具筛选、审计、工作流提案；它引用 Workflow Core/Management，说明 AI 位于核心引擎之上，而不是核心执行器内部，见 [`Elsa.AI.Host.csproj#L3-L21`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Host/Elsa.AI.Host.csproj#L3-L21)。
- `Elsa.AI.Copilot`：GitHub Copilot SDK 的 provider Adapter，见 [`Elsa.AI.Copilot.csproj#L3-L18`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Copilot/Elsa.AI.Copilot.csproj#L3-L18)。
- `Elsa.AI.Persistence.EFCore.*`：AI 会话、提案等数据的 EF Core 持久化适配。

Studio 不在这个本地仓中。仓内 roadmap 提到 `Elsa.Studio.AI`，但它属于单独的 `elsa-studio` 仓，不能把 Studio UI 当成已在本次源码中验证的核心能力。当前主仓也没有 Semantic Kernel/OpenAI provider 实现；该 commit 可确认的生产 provider 是 Copilot，其他 AI/Agent 扩展属于另一扩展仓或未来集成范围。

### 2.2 Interface、Implementation 与执行链

核心 Interface 很小：[`IAIProvider.cs#L5-L20`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Abstractions/Contracts/IAIProvider.cs#L5-L20) 定义 provider/orchestrator/tool invoker；[`IAITool.cs#L5-L15`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Abstractions/Contracts/IAITool.cs#L5-L15) 定义工具与 registry。工具元数据不只描述输入，还包含 mutability、danger level、权限、租户、审计和 agent scope，见 [`AIToolDefinition.cs#L3-L19`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Abstractions/Models/AIToolDefinition.cs#L3-L19) 与 [`#L66-L108`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Abstractions/Models/AIToolDefinition.cs#L66-L108)。

Implementation 集中在 Host：注册、provider 选择、上下文构建、工具过滤、流式响应、会话持久化和审计都被隐藏在模块内部。提案工具默认不启用，必须显式打开，见 [`AIFeature.cs#L10-L36`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Host/Features/AIFeature.cs#L10-L36)。创建提案工具明确只验证并保存 proposal，**不会持久化工作流定义**，见 [`WorkflowProposeCreateTool.cs#L7-L40`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Host/Tools/Workflows/WorkflowProposeCreateTool.cs#L7-L40)；更新提案还绑定 baseline version，见 [`WorkflowProposeUpdateTool.cs#L7-L56`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Host/Tools/Workflows/WorkflowProposeUpdateTool.cs#L7-L56)。

`AIProposal` 的状态包含 Draft、Validated、Blocked、Approved、Rejected、Applied、Expired，并保存 baseline、payload、理由、警告、诊断和 diff，见 [`AIProposal.cs#L3-L23`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Abstractions/Models/AIProposal.cs#L3-L23) 与 [`#L41-L62`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Abstractions/Models/AIProposal.cs#L41-L62)。但是该 commit 中 `ApproveProposals`/`ApplyProposals` 只出现为权限常量，未发现把 proposal 转为 Approved/Applied 并落到工作流定义的实现。因此 proposal 审核/应用是已经设计出的治理边界，但尚不能当成闭环完成的功能。

Copilot Adapter 的会话配置使用 `PermissionHandler.ApproveAll`，见 [`CopilotProvider.cs#L126-L149`](https://github.com/elsa-workflows/elsa-core/blob/ae146a17655664645f3761720b998d65f587344a/src/modules/Elsa.AI.Copilot/Adapters/CopilotProvider.cs#L126-L149)。Elsa Host 自己还有工具白名单、权限和危险级过滤，但 Tenon 不应复制这个“provider 侧全部批准”策略。

### 2.3 是否属于 AI 审批

**不属于运行时 AI 审批。** Elsa AI 面向 Weaver 的工作流 authoring、查询与 diagnostics：AI 可以读工作流、验证设计，并提出“创建/更新工作流定义”的 proposal。这里的 Approved/Rejected 是对 **AI 生成的定义变更提案** 的治理，不是对请假、采购等 **运行中业务任务** 的审批。

在固定 commit 的 `Elsa.AI.*` 中未发现 AI workflow Activity，也未发现 AI 自动完成、同意或拒绝运行时人工任务的链路。因此准确产品语义是：

> Elsa AI = workflow authoring/diagnostics copilot + definition-change proposal boundary；不是 runtime approval agent。

### 2.4 设计质量分析

| 维度 | 评价 |
| --- | --- |
| Module | `Elsa.AI.Host` 是较深的独立模块，统一封装会话、上下文、工具、权限、审计和 proposal。 |
| Interface | `IAIProvider`、`IAITool` 等接口小而稳定；调用方无需理解 provider 的会话细节。 |
| Implementation | Host 的 Implementation 完整度高；proposal 审核/应用闭环在该 commit 尚未落地。 |
| Seam | provider、tool、conversation store、proposal store、EF provider 都有 Seam。当前仓只有一个真实 AI provider，provider 可替换性仍需更多生产 Adapter 证明。 |
| Adapter | Copilot 与多种 EF Core 数据库是明确 Adapter。 |
| Depth | 高。小接口背后隐藏较多治理与编排复杂度。 |
| Leverage | 高。新增 provider/tool/persistence Adapter 可复用同一套 Host 治理。 |
| Locality | 较好。AI 代码集中在独立 modules，未渗入 Workflow Core 的执行语义。 |

## 3. Slickflow.AI：运行时 AI 节点与审批的真实边界

### 3.1 AI 节点如何参与流程

Slickflow 把 AI 作为流程节点类型：[`AIServiceTypeEnum.cs#L12-L33`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Xpdl/Common/AIServiceTypeEnum.cs#L12-L33) 定义 LLM、RAG、Agent；[`NodeBuilder.cs#L343-L424`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Graph/Model/NodeBuilder.cs#L343-L424) 可构建对应节点。

固定 commit 存在两套运行时执行路径，后续不能笼统地说“三类节点行为一致”：

1. **经典持久化引擎路径：`NodeMediator → AIServiceExecutor`。** [`AIServiceExecutor.cs#L23-L69`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Core/Pattern/Auto/AIServiceExecutor.cs#L23-L69) 在引擎推进链内同步等待远程调用。LLM 读取前序节点变量和 `ai_activity_config`，调用模型后把文本保存为活动级流程变量，见 [`#L75-L150`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Core/Pattern/Auto/AIServiceExecutor.cs#L75-L150)；Agent 运行 ReAct 后也只保存文本变量，见 [`#L174-L280`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Core/Pattern/Auto/AIServiceExecutor.cs#L174-L280)。该路径的 RAG 分支实际调用 `ExecuteLlmService`，而 `ExecuteRagService` 是空方法，因此不能把这条路径描述成已完成独立 RAG。
2. **`WorkflowExecutor → WorkflowActivityExecutor` 路径。** [`WorkflowActivityExecutor.cs#L321-L388`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Executor/WorkflowActivityExecutor.cs#L321-L388) 根据类型调用 `LlmMultiTurnService`、`RagMultiTurnService` 或 `AgentMultiTurnService`；结果写入指定输出变量或 `AIServiceResult`，RAG/Agent 还写 `ai_response` 并可通知客户端，见 [`#L409-L445`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Executor/WorkflowActivityExecutor.cs#L409-L445)。这条路径才兑现了三种类型的差异化执行。

两条路径共同证明 Slickflow 已把 AI 做成运行时自动节点，而不只是设计器 Copilot；同时也暴露出执行语义重复、能力随入口变化的问题。Tenon 必须只有一条自动节点执行链，所有触发入口都跨同一个 Interface。

模型 Seam 是 [`IAILlmService.cs#L11-L27`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.AI/Implementation/IAILlmService.cs#L11-L27)。[`AILlmServiceFactory.cs#L10-L53`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.AI/Implementation/AILlmServiceFactory.cs#L10-L53) 用字符串选择 OpenAI、QianWen、DeepSeek Adapter；具体实现使用原始 HTTP，并未依赖 Semantic Kernel、OpenAI SDK 或 `Microsoft.Extensions.AI`。

### 3.2 LLM、RAG、Agent、MCP 与规则引擎

| 能力 | 固定源码的实际 Implementation | 输出与限制 |
| --- | --- | --- |
| LLM | 从 `ai_activity_config → ai_model_provider` 读取模型与加密密钥，factory 选择 OpenAI/QianWen/DeepSeek Adapter，构造消息后调用模型 | 返回普通字符串；`ResponseFormat` 在配置中存在，但 provider 请求仍可见 `null`，没有强制 schema |
| RAG | 新执行路径使用 [`RagMultiTurnService.cs#L26-L70`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.AI/Service/RagMultiTurnService.cs#L26-L70)；[`LlmChatMessageBuilder.cs#L24-L79`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.AI/Implementation/LlmChatMessageBuilder.cs#L24-L79) 生成 embedding；[`RagMessageBuilderCore.cs#L55-L130`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.AI/Implementation/RagMessageBuilderCore.cs#L55-L130) 调 Supabase RPC 检索后把文档拼入 prompt | 可按相似度和数量检索，但 Implementation 直接绑定 Supabase、`industry_id` 与具体 RPC；经典路径没有独立 RAG |
| Agent | `AgentMultiTurnService` 按 `ActivityId` 从全局 registry 取工具，直接创建 OpenAI tool-call client，`AgentNodeBase` 执行 ReAct 循环 | 无工具时退化为 LLM；最终仍返回字符串；迭代数复用 `MemoryTurns`，语义混合 |
| Rule | 独立 RuleTask 读取流程变量，执行 JSON bindings/NRules 后再写变量 | 能拼出“AI proposal → 确定性规则 → 网关”，但未封装 AI 审批 policy |

- Multi-agent 的核心是 ReAct 式循环。[`AgentMultiTurnService.cs#L16-L103`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.AI/Service/AgentMultiTurnService.cs#L16-L103) 解析活动工具、构造 LLM client 和 agent node；[`AgentNodeBase.cs#L15-L53`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.AI/Agent/Core/AgentNodeBase.cs#L15-L53) 实现迭代调用。
- 工具 Interface 是 `IAgentTool`，本地函数与 MCP client 都是 Adapter。MCP 实现见 [`McpClientTool.cs#L14-L113`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.AI/Agent/Tools/McpClientTool.cs#L14-L113)。
- 固定 commit 中不存在 README 所称的 `source/sfmcp`，也没有可追踪的 MCP server 项目；现有源码只能证明 **MCP client Adapter**，不能证明仓内已经交付 MCP server。
- 规则任务独立于 AI。[`RuleExecutor.cs#L18-L63`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Core/Pattern/Auto/RuleExecutor.cs#L18-L63) 执行规则并写流程变量；[`RuleSetExecutionManager.cs#L17-L49`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Business/Manager/RuleSetExecutionManager.cs#L17-L49) 支持 JSON bindings 或 NRules。可组合成“AI 输出结构化结果 → 规则作确定性路由”，但不是一个已封装的 AI 审批模块。

### 3.3 `confidence → human review` 是否引擎内置

**不是。它是 README 描述的组合模式。** 固定 commit 的 README 提到 `ApprovalDecisionAgent` 和按 confidence 转人工审核，但仓内跟踪源码中没有 `ApprovalDecisionAgent` 实现，也没有把 confidence 映射为人工任务或审批状态的内置执行器。

源码中的两个机制彼此分离：

1. AI 执行器输出普通文本/流程变量；
2. 人工审批由 [`ApprovalStatusEnum.cs#L12-L31`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Common/ApprovalStatusEnum.cs#L12-L31) 的 Refused/Agreed 状态，以及 [`ActivityInstanceManager.cs#L1541-L1618`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Business/Manager/ActivityInstanceManager.cs#L1541-L1618) 的任务同意、拒绝与会签通过率逻辑处理；
3. [`NextActivityScheduleSplit.cs#L137-L160`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Xpdl/Schedule/NextActivityScheduleSplit.cs#L137-L160) 根据已形成的人工审批状态路由；
4. 未发现 AI 输出直接设置 `ApprovalStatus`、完成任务或调用 Agree/Refuse 的源码链路。

所以 Slickflow 的准确定位是：

> 带 LLM/RAG/Agent 自动节点、规则节点和人工审批能力的工作流引擎；`confidence → human review` 需要使用者自行定义输出格式、变量映射、规则/分支和人工节点，并非引擎内置 AI 审批。

### 3.4 官方 Wiki 与固定源码的差异

官方[文档中心](https://www.slickflow.com/wiki/index)把产品定位为“.NET 8 的 AI 多智能体工作流引擎”，并分别提供 [AI 概述](https://www.slickflow.com/wiki/aiguide)、[LLM 指南](https://www.slickflow.com/wiki/llmguide)、[RAG 指南](https://www.slickflow.com/wiki/ragguide)和[多智能体编排](https://www.slickflow.com/wiki/multiagent)。这些页面能说明产品方向，但**不能直接当作当前开源提交的 API 契约**。对固定 commit 全仓检索后，结论如下：

| Wiki 宣称或示例 | 固定源码核对 | 采用时的判断 |
| --- | --- | --- |
| BPMN 中集成 LLM/RAG/Agent | 已确认：有 `AIServiceNode`、`AiServiceTypeEnum` 的三种 AI 类型、设计器配置和运行时执行器 | 可作为源码事实 |
| Agent 工具、ReAct 循环、MCP | 已确认 `AgentToolSetAttribute`、agent loop 和 MCP client Adapter；未发现 README 所称 MCP server 项目 | 只能引用已落地部分 |
| OpenAI、通义千问、DeepSeek | 已确认三个 provider Adapter | 可作为源码事实 |
| Claude、Gemini 等更多模型 | Wiki 有宣称，固定源码 factory/Adapter 未发现对应实现 | 文档方向，不能写成当前能力 |
| `[LLMTask]`、`LLMSettings`、`LLMService`、`AgentResult.Score/NextAction` | 未发现前三者对应的 attribute/settings/service 类型，也未发现 `Score/NextAction` 类型化结果契约；源码中的 `AgentResult` 只是默认输出变量名，`LLMService` 只是前端 palette 名称 | 文档示例，不是当前公开 API |
| `[AgentRole]`、`RunMultiAgentAsync`、`WfAppRunner.Agents` | 固定源码未发现；实际实现以 `AgentMultiTurnService`、agent node 和 tool registry 为主 | 文档示例与源码 API 不一致 |
| `IRAGService`、`KnowledgeImporter`、`ScoreThreshold`，以及 Milvus/Qdrant/Pinecone/Chroma 集成 | 固定源码未发现这些接口、类型或数据库 Adapter | 不能据此判断已交付完整 RAG 平台 |
| 自动重试、fallback agent、人机协同审核节点、完整推理链持久化 | 配置实体能看到 `MaxRetries`、`FallbackAgent` 等字段，但未发现执行闭环；也未发现独立 Human-in-the-Loop 审核节点 | 属于设计意图或待完成能力 |

因此，Wiki 对这次调研的价值是补全“产品想做成什么”，源码负责回答“现在实际有什么”。涉及选型、排期和接口设计时，以固定源码和可获取 NuGet 包为准；Wiki 中未落地的 API 只记为候选需求，不进入 Tenon 的已具备能力清单。

### 3.5 实现成熟度与设计质量

当前实现还有几处不宜直接复制的风险：

- `NodeMediator → AIServiceExecutor` 与 `WorkflowExecutor → WorkflowActivityExecutor` 两套执行路径重复且行为不同；经典路径的 RAG 实际退化成普通 LLM。能力随入口变化，削弱 Locality。
- provider 通过静态 factory 和字符串分支直接构造，调用方仍要携带 endpoint、key、模型等细节，Seam 存在但 Depth 一般。
- agent 工具 registry 是进程级静态状态；日志直接写 Console，削弱 Locality 与宿主可控性。
- conversation memory 是静态内存，而调用处把 `ProcessId` 当作实例标识，可能让同一流程定义的多个实例共享上下文；未发现驱逐调用。
- `MaxRetries`、`ErrorHandling`、`FallbackAgent` 等字段在配置实体存在，但固定 commit 中未发现相应运行时闭环；`ResponseFormat` 也未真正传给 provider。
- 执行器直接 `new` 服务并存在 sync-over-async；经典路径在引擎推进/session 链内等待远程模型，缺少“短事务领取 → 事务外调用 → 短事务提交结果”的隔离。
- 没有独立的持久化 attempt、幂等键、租约/fence、可靠重试结果协议。
- 固定 commit 的测试目录未发现覆盖 AI executor、multi-agent、MCP 或规则执行主链的测试。

| 维度 | 评价 |
| --- | --- |
| Module | AI provider、agent、流程 AI 节点和规则引擎有目录边界，但运行时组装仍与 Engine 紧耦合。 |
| Interface | `IAILlmService`、`IAgentTool`、`ILlmClient` 提供了有价值的小 Interface。 |
| Implementation | LLM/Agent 已有运行实现；RAG 只在新路径完整分流。可靠执行、配置兑现、隔离与测试不足。 |
| Seam | 三种 LLM provider 和本地函数/MCP client 是真实 Seam；静态 factory/direct `new` 限制替换能力。 |
| Adapter | OpenAI/QianWen/DeepSeek、FunctionCallingTool、McpClientTool 是 Adapter。 |
| Depth | agent loop 较深；provider 与节点执行器偏浅，较多配置和基础设施细节外泄。 |
| Leverage | AI 节点模型、统一工具接口、规则后处理具有较高复用价值。 |
| Locality | 一般。静态 registry/memory、Console 日志和 Engine 直接构造使改动影响面扩大。 |

## 4. 对照 TenonAdmin.Workflow

> **后续实施基线：**本节把固定提交中值得保留的产品形状，转换为 Tenon 自己的目标架构、Interface、不变量和验收线。只要 Slickflow 对照 commit 不变，后续开发 AI 工作流无需重新通读该项目；若上游升级，只需针对本节列出的源码锚点做增量核对。

### 4.1 可直接借鉴的模式

1. **Elsa 的 proposal-only 写边界。** AI 只能产生候选结果；验证、审批、应用是不同动作，并保存 baseline version、diff、warning、diagnostics 和审计信息。
2. **Elsa 的工具策略元数据。** 为工具声明只读/提案/管理级变更、危险度、权限、租户和审计要求，在服务端过滤，而不是只依赖 prompt。
3. **provider 与工具 Adapter。** 核心只依赖小接口，以 fake Adapter 做确定性测试，以外部 provider Adapter 承担 SDK/HTTP 差异。
4. **Slickflow 的“一等 AI 节点 + 流程变量输出”。** AI 是普通自动节点，后续仍走引擎已有的变量、规则与分支，而不是另造一套流程引擎。
5. **Slickflow 的工具统一接口和规则后处理。** LLM 负责不确定性判断，确定性规则负责阈值、权限和最终路由。

### 4.2 需改造成 Tenon AI 预审节点

模型只能生成 proposal，不生成工作流命令。建议把模型输出固定为下面的版本化 JSON 契约：

```json
{
  "schemaVersion": "1.0",
  "recommendation": "approve | reject | manual",
  "confidence": 0.93,
  "reasonCodes": ["POLICY_MATCH"],
  "rationale": "金额、申请人与历史采购记录一致",
  "evidence": [
    { "id": "invoice:123", "source": "invoice", "contentHash": "sha256:..." }
  ],
  "riskFlags": []
}
```

`provider/model/modelVersion/promptVersion`、token、费用和耗时属于执行审计 envelope，由 Adapter/宿主记录，不信任模型自行回填。必须使用严格 schema 校验；超时、provider 错误、格式错误、低 confidence 或证据不足一律转人工。即使流程定义显式允许自动处理，也必须由服务端确定性 policy 检查阈值、权限、租户数据范围、风险标记和幂等性后再执行。

`confidence` 在这份契约里是被降级的字段：模型自报置信度业界已知普遍未校准，把“confidence 超过阈值”单独作为自动放行条件等于把路由权变相还给模型。policy 的主判据必须是 `reasonCodes`、`riskFlags` 与证据完整性这些可确定性核验的字段；`confidence` 只作为审计记录与事后评测输入。自动放行的真实门槛来自 shadow mode 期间按场景统计的实际准确率，不来自模型自评分。

### 4.3 明确不借

- 不把 Elsa Core/Studio/Copilot SDK 整体引入 Tenon 工作流核心；不复制 Copilot provider 的 `ApproveAll`。
- 不复制 Slickflow 的静态 registry、静态 memory、direct `new`、sync-over-async、字符串 provider factory 和只写文本变量的审批判断。
- 不把 README 中未落地的 MCP server、`ApprovalDecisionAgent` 或 confidence 路由当成可用功能。
- 不允许 agent tool 直接调用 Agree/Refuse/CompleteTask；不把 LLM 当作审批权威。
- 不在 `TenonAdmin.Workflow` 核心包中直接绑定 OpenAI、Copilot 或 Semantic Kernel SDK。

### 4.4 目标架构：一个可靠执行 Module，AI 只是 Adapter

```text
Workflow Token/Agenda
  → Reliable Automatic Node Execution Module
      → WebhookNodeHandler Adapter
      → FakeNodeHandler Adapter
      → AiDecisionNodeHandler Adapter
          → Context/Evidence Assembler
          → Model Provider Adapter
          → JSON Schema Validator
          → Deterministic Approval Policy
  → 原子保存 result / variables / history / outbox
  → 继续流程或创建人工任务
```

外部只暴露自动节点执行 Interface；provider、Prompt、RAG、tool calling、schema 修复和 policy 编排都藏在 AI Adapter 的 Implementation 内。这样删除该 Module 时，attempt、deadline、重试、fence、审计和故障恢复复杂度会重新散落到每一种机器节点，说明它具备足够 Depth；Webhook、AI 和测试 fake 共用后，产生真实 Leverage 与 Locality。

### 4.5 自动节点 Interface 与结果语义

后续实现任务可以从下面的最小 Interface 起步，命名可随仓内规范调整，但语义不可拆散：

```csharp
public interface IWorkflowNodeHandler
{
    string NodeType { get; }

    ValueTask<WfNodeExecutionResult> ExecuteAsync(
        WfNodeExecutionContext context,
        CancellationToken cancellationToken);
}
```

- `WfNodeExecutionContext` 只包含不可变的租户/组织、流程定义版本、实例、token、节点配置快照、变量/证据快照、`ExecutionKey`、attempt、deadline；不泄漏 SqlSugar entity、数据库 session 或模型 SDK 类型。
- `WfNodeExecutionResult` 只能是 `Succeeded`、`RetryableFailure`、`ManualFallback`、`TerminalFailure`/`Cancelled` 等显式结果；handler 返回结果，不直接推进 token、写任务状态或自行提交数据库事务。
- `AiDecisionNodeHandler` 是这个 Seam 上的 Adapter。内部 provider Seam 至少有一个真实 OpenAI-compatible Adapter 和一个 deterministic fake Adapter 后再固定；不要先复制 Slickflow 的字符串 factory。
- AI 输出变量使用版本化 schema，规则/policy 读取结构化字段，不从自然语言中二次猜测。

### 4.6 持久化与恢复模型

概念数据模型如下，具体表名留给实现需求定稿：

| 记录 | 必要字段 | 作用 |
| --- | --- | --- |
| `WfNodeExecution` | tenant/org、instance、token、node、definitionVersion、ExecutionKey、status、attempt、deadline、lease/fence、nextRetryAt、inputHash、outputHash | 每个机器节点的一次稳定逻辑执行；唯一键防止重复推进 |
| `WfNodeExecutionAttempt` | executionId、attemptNo、provider/model/prompt/schema/policyVersion、started/ended、token/cost、result/error 摘要 | 保留每次真实外部调用，不把重试覆盖掉 |
| `WfAiDecision` | executionId、proposal JSON、schema 校验、policy outcome、confidence、risk/evidence refs、人工推翻结果 | AI 决策审计、评测和后续优化事实源 |
| `WfOutbox` | executionId、messageType、payloadHash、status、retry | 结果提交后可靠触发通知或外部副作用 |

建议状态机：`Pending → Running → Succeeded`；失败可进入 `RetryScheduled → Running`，策略或模型不确定进入 `ManualFallback`，取消/不可恢复错误进入 `Cancelled/Failed`。`ExecutionKey` 至少由租户、实例、token、节点、定义版本构成；同一个逻辑执行允许多次 attempt，但只允许一次结果推进流程。

固定执行时序：

1. 短事务创建/领取 execution，CAS 更新 lease/fence 后提交；
2. 事务外构建已授权证据快照并调用 provider，传 deadline、cancellation 和可用的 provider idempotency key；
3. schema 校验失败不尝试从自然语言直接推进审批；可有限修复一次，仍失败则 `ManualFallback`；
4. 服务端 policy 根据 proposal、流程配置和权限作确定性裁决；
5. 短事务用 fence/CAS 原子保存 attempt、proposal、变量、历史、outbox，并推进 token 或创建人工任务；
6. worker 崩溃后可重新领取。允许模型调用偶发重复，但状态副作用和流程推进必须幂等。

### 4.7 AI 审批安全不变量

1. 模型输出永远不能直接修改 `WfTask`、`WfToken` 或调用 Approve/Reject/CompleteTask。
2. V0 只允许“显式授权场景下的低风险自动放行”；AI 建议拒绝、低置信度、风险标记、证据不足和任何异常全部转人工，不自动拒绝。
3. 自动放行必须同时满足：流程版本显式启用、租户 policy 允许、schema 有效、confidence 达标、无禁止风险、证据可追溯、execution fence 有效。
4. Prompt、模型和 policy 都版本化；回放使用已保存 proposal + policy version，不重新调用模型伪造历史确定性。
5. RAG 只返回调用者有权限读取的证据；保存引用和 hash，敏感正文按策略脱敏或不落审计。
6. Agent 工具首批只读；写工具必须单独声明危险度、权限、租户、幂等和人工确认策略。审批写命令不注册为模型可调用工具。
7. Provider 密钥不进流程定义、变量或日志；供应商 SDK 与配置只存在于 Adapter。
8. 自动放行默认关闭。内核只交付 shadow mode 机制、指标采集与审计视图；开关按流程定义显式开启，放行阈值由消费者部署在自己的数据上校准，内核不提供“开箱即用”的阈值默认值。

### 4.8 验收线与产品指标

工程验收至少覆盖：同一 `ExecutionKey` 串行/并发重放只推进一次；worker 在调用前后崩溃可恢复；远程调用期间无工作流数据库长事务；无效 JSON、超时、限流、低置信度稳定转人工；provider fake 可覆盖成功/重试/人工 fallback；四库上的 execution 唯一约束、CAS/fence、事务回滚和 outbox 契约一致；跨租户证据与工具调用被拒绝。

产品指标不再只看“支持多少节点”，而看人工触达率、平均审批时长、自动放行覆盖率、人工推翻率、错放/逃逸风险率、schema 失败率、fallback 率、provider 延迟与单次成本。首版必须有 shadow mode：只记录 AI proposal，不改变路由；达到场景级评测阈值后再打开低风险自动放行。

评测阈值属于部署方，不属于内核：TenonAdmin 以内核包分发、自身没有生产流量，各消费者的审批场景与单据分布互不相同。内核负责指标采集与审计视图，“何时由 shadow 切自动放行”是每个消费者按自己场景数据做的决定；产品文档必须明确这一责任边界，防止消费者拿默认配置直接开自动放行。

## 5. 建议开发阶段

| 阶段 | 建议 | 原因 |
| --- | --- | --- |
| M2b | 不开发 AI。完成超时与通知可观测性。 | AI 失败最终仍要安全回退到人工/超时链路。 |
| M2c | 不开发 AI。完成请求幂等、operation receipt 与多数据库契约测试。 | 未来任何自动动作都必须先有可靠的幂等与恢复语义。 |
| M3a | 做通用可靠自动节点执行 Module，以 Webhook/测试 fake 为首批 Adapter。增加稳定执行键、attempt、deadline、retry、fence、结构化结果、变量/历史落库和 node-handler SPI。 | 这是 AI、Webhook 和其他外部节点共用的基石；先证明真实 Seam。 |
| M3b | 同一里程碑交付最小 `AI Decision Node`：OpenAI-compatible + fake Adapter、结构化 proposal、schema/policy、shadow mode、低风险自动放行、人工 fallback、审计与限额。 | AI 处理审批是产品战略，不再作为无承诺的 M3+ 附件；仍通过 Adapter 接入，不污染核心状态机。 |
| M3+ | 增加证据/RAG Adapter、只读 Agent tools、更多 provider、评测集与灰度策略；设计/诊断 Copilot 最后做。 | 先证明 AI 能可靠减少人工触达，再扩展自主性和设计体验。 |

推荐的最终链路是：

```text
AI 生成预审 proposal
  → 服务端校验 schema / 权限 / policy / confidence
  → 确定性路由
  → 人工任务（默认）或流程定义显式授权的自动动作
  → 全链路审计
```

在产品命名上应优先使用“AI 预审/辅助审核”，只有在产品明确授权自动决策、具备可解释 policy、幂等执行和完整审计后，才考虑称为“AI 审批”。

## 6. 许可证与依赖风险

- Elsa 与 Slickflow 仓库源码都是 MIT。借鉴架构模式风险低；若复制实质性源码，仍须保留原版权和许可证声明。更稳妥的方式是记录来源后独立实现。
- Elsa 的 GitHub Copilot Adapter 依赖 [`GitHub.Copilot.SDK`](https://github.com/github/copilot-sdk)。SDK 本身为 MIT，但仍带来 preview 稳定性、CLI/runtime、Copilot 订阅或 BYOK、计费与供应商绑定问题，不适合成为 Tenon 核心依赖。
- Slickflow 仓库 MIT 不代表其完整依赖树全是 MIT。固定 commit 的 [`Slickflow.Engine.csproj#L42-L51`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.Engine/Slickflow.Engine.csproj#L42-L51) 包含 Devart Oracle provider 与 [`iTextSharp 5.5.13.4`](https://www.nuget.org/packages/iTextSharp/5.5.13.4)；后者涉及 AGPL/商业授权，Devart 也需要单独核对商业许可。直接依赖 Engine 前必须做完整 SBOM/许可证审查。
- `Slickflow.AI` 自身还引用项目内自定义 Dapper/Data/WebUtility，见 [`Slickflow.AI.csproj#L15-L31`](https://github.com/besley/Slickflow/blob/646392d3e9be7e34b79f6fa8ca0f23dd80db2b6e/source/core/Slickflow.AI/Slickflow.AI.csproj#L15-L31)，并不是一个可轻量摘取的独立 AI abstraction 包。

## 最终判断

| 问题 | Elsa 3 | Slickflow |
| --- | --- | --- |
| AI 在哪里 | 设计/诊断 Copilot 与定义变更 proposal | 运行时 LLM/RAG/Agent 自动节点 |
| 是否直接处理业务审批 | 否 | 否 |
| 人工审核含义 | 审 AI 生成的工作流定义提案（且闭环尚未完成） | 原有人工任务审批；与 AI 输出需由使用者自行组合 |
| confidence → human review | 不适用其当前定位 | README/Wiki 组合或示例模式，不是引擎内置 |
| Tenon 最值得借鉴 | 工具策略、provider 隔离、proposal-only、审计 | AI 节点、工具 Adapter、变量输出、确定性规则后处理 |
| 是否建议直接依赖 | 否 | 否 |

因此，TenonAdmin.Workflow 不需要寻找或复制一个所谓“.NET AI 审批库”。正确路线是 M3a 建成可靠、可插拔的自动节点执行 Seam，M3b 随即交付受约束的 AI Decision Adapter：**AI 提 proposal，服务端作 schema 校验和确定性 policy，低风险可自动放行，其余由人处理。** RAG、Agent 和设计 Copilot 在这块基石稳定后扩展。
