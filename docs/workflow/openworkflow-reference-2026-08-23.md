# OpenWorkflow 源码研究与 TenonAdmin 工作流对照

> 文档入口：[`README.md`](./README.md)
> 调研日期：2026-08-23
> OpenWorkflow 基线：`openworkflowdev/openworkflow@46dcc85d230bb54894dc4bab022a1ce34cc11c13`（提交时间 `2026-08-21T23:48:32Z`，包版本 `0.9.2`）
> TenonAdmin 基线：`2ee061d0edfb1d63f4ccb34bfe0f81439a05afe9`
> 本地参考仓：`C:\HuHuHu\参考项目\工作流\openworkflow`
> 资料边界：只使用上述源码、仓内官方文档和官方 GitHub；未使用二手博客。参考仓是浅克隆且未安装 `node_modules`，本次做静态源码核验，未运行其测试。

## 一、结论先行

OpenWorkflow 是一个 **TypeScript 代码优先的 durable execution 框架**，不是 OA 人工审批引擎。它通过数据库里的 `workflow_runs`、`step_attempts`、`workflow_signals` 三类运行记录，加上 worker 租约和从头重放，把崩溃恢复、步骤重试、持久 sleep、外部 signal、子工作流藏在很小的 `step` Interface 后面。

它不会推翻 TenonAdmin 已定的方向：Tenon 仍应保持 **JSON 审批树 + Token/Agenda + 活跃任务表 + 版本快照 + 审批领域动作**，不引入代码工作流和确定性重放。OpenWorkflow 真正值得借的是执行可靠性，而不是产品模型：

1. 为发起、同意、拒绝、退回、撤销、转办补 **请求幂等键与操作回执**，让客户端重试返回第一次结果，而不是只靠 CAS 报冲突。
2. 给当前“事务提交后直接通知、失败静默吞掉”补 **日志、指标和失败诊断**；只有通道承诺可靠送达时才使用事务型 outbox。M3 Webhook 属于必须持久化 attempt/outbox 的机器副作用。
3. 完成超时 Job 时借鉴 `availableAt + attempts + backoff + ownership fence`，但复用 Tenon 现有调度器，不新造 worker 集群。
4. 未来 Webhook/自定义自动节点使用独立的 **自动节点 attempt 模型**；人工审批动作不自动重放。
5. 为四种数据库建立同一套工作流持久化契约测试，特别覆盖并发 CAS、幂等、超时领取和 outbox 去重。

OpenWorkflow 的 `step.run` 不是 exactly-once。已完成步骤会命中缓存，但 worker 在“外部副作用成功、完成记录落库之前”崩溃时，该步骤会再次执行；它提供的是持久检查点和 **at-least-once + memoization**。Tenon 若借用，外部 HTTP、消息和业务写入仍必须带业务幂等键。

## 二、项目身份、许可证与成熟度

| 项目 | 核验结果 |
| --- | --- |
| 仓库 | `https://github.com/openworkflowdev/openworkflow` |
| commit | `46dcc85d230bb54894dc4bab022a1ce34cc11c13`，2026-08-21 |
| npm 包 | `openworkflow@0.9.2`，Node `>=20`；仓库工具链要求 Node `>=22.5` |
| 许可证 | Apache-2.0，可借鉴和复用，但分发修改版本仍需保留许可证、归属和修改声明 |
| 持久化 | SQLite（单机）与 PostgreSQL（生产、多 worker） |
| 测试 | 27 个 `*.test.ts` / `*.testsuite.ts` 文件；核心包覆盖率阈值 100%，总体 statements/functions/lines 90%、branches 80% |
| CI | Node 与 Bun 双运行时；含格式、构建、lint、重复代码、拼写、死代码、类型和覆盖率检查，并启动真实 PostgreSQL 服务 |
| 仍未落地 | roadmap 将 cron、补偿、优先级/更细并发控制、OpenTelemetry、Redis、Go/Python 列为 Coming Soon |

证据：[包元数据](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/package.json#L1-L75)、[Apache-2.0 许可证](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/LICENSE.md#L1-L10)、[CI](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/.github/workflows/ci.yaml#L14-L59)、[覆盖率门槛](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/vitest.config.ts#L3-L28)、[roadmap](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/apps/docs/docs/roadmap.mdx#L6-L32)。

成熟度判断：核心恢复和数据库并发测试明显超过普通早期项目，但版本仍是 `0.x`，公开能力也还在快速演进。例如 `sleeping/succeeded` 正迁移到 `running/completed`；官方重试文档提到 dashboard 可手工重试失败 run，但当前 `Backend`/Client/dashboard 源码只有创建、查询、取消等入口，没有对应 retry 状态转换。可把它当高质量设计样本，不宜当稳定协议照搬。

## 三、公开 Interface 与模块深度

### 3.1 `defineWorkflow` 与 `step`

公开定义分两层：

- `WorkflowSpec` 只有 `name`、可选 `version`、输入 schema、workflow retry policy；`defineWorkflow` 本身只是把 `spec` 和函数装成对象。[源码](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/workflow-definition.ts#L9-L22)、[实现](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/workflow-definition.ts#L62-L102)
- workflow 函数只拿到 `{ input, step, version, run }`；`StepApi` 只有 `run`、`runWorkflow`、`sleep`、`sendSignal`、`waitForSignal` 五个原语。[源码](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/workflow-function.ts#L49-L107)
- Client 将定义注册、启动、等待结果和取消收敛为 `OpenWorkflow`、Runnable workflow 和 run handle。[源码](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/client/client.ts#L37-L176)、[run 选项](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/client/client.ts#L238-L259)

按 `codebase-design` 词汇评价：

| 维度 | 判断 |
| --- | --- |
| Module | `defineWorkflow` 自身是浅 Module；真正的深 Module 是 `StepApi + StepExecutor + StepHistory + Backend` 的组合 |
| Interface | 五个 step 原语很小，用户无需接触租约、重放、SQL、attempt 状态和唤醒协议 |
| Implementation | `StepExecutor` 和两个 Backend Adapter 吸收了大部分复杂度，Interface/Implementation 比值优秀 |
| Depth | `step.sleep` 一行背后包含持久 attempt、释放 worker、定时唤醒、恢复后完成检查点，Depth 很高 |
| Leverage | 同一套 attempt/history 机制复用到函数、sleep、子工作流、signal send/wait，Leverage 高 |
| Seam | `Backend` 是真实的部署 Seam，SQLite/Postgres 是两个 Adapter；workflow name+version 是代码版本 Seam |
| Locality | 单次 step 的规则集中在 `StepExecutor`，数据库所有权规则集中在 Adapter，Locality 较好；代价是 SQLite/Postgres 各约千行、状态 SQL 有重复 |

`Backend` Interface 有 20 余个状态转换和查询方法，直接暴露 `workerId`、lease、run/attempt 细节。它比 `StepApi` 浅，但这是有意为持久化 Adapter 留的内部 Seam；共享 backend test suite 抑制了两个实现的语义漂移。[Backend Interface](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/backend.ts#L14-L79)、[共享契约测试入口](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/testing/backend.testsuite.ts#L14-L36)

### 3.2 对 Tenon 的界面启示

Tenon 已经有较深的执行 Module：`IWorkflowEngine.ExecuteAsync(IWfCommand)` 对外很小，Implementation 在一个事务内把命令转成 Agenda operations 并跑空，提交后才发通知（`backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs:27-62`）。审批人、条件、表单、通知分别有 `IApproverProvider`/resolver、`IWfConditionEvaluator`、`IWorkflowFormBinder`、`IWorkflowNotifier` Seam。

不应把所有审批能力压成 OpenWorkflow 风格的 `step` DSL。Tenon 的公开 Interface 应继续是发起、同意、拒绝、退回、撤销、转办、重提、催办等领域动词；可以借的是这些动词内部统一走一个“可靠操作执行器”，而不是让消费者理解 token、attempt 或 worker。

## 四、状态、历史与恢复机制

### 4.1 它不是完整 event sourcing

OpenWorkflow 没有独立的 journal/event 表。实际权威状态是：

- `workflow_runs`：当前 run 状态、输入输出、错误、attempts、父步骤、worker、`availableAt`、deadline 和时间戳。
- `step_attempts`：步骤尝试历史及结果，是重放时的检查点/备忘录。
- `workflow_signals`：只记录已投递给当时正在等待的 signal delivery。

数据库 schema 证据：[workflow_runs / step_attempts](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/postgres/postgres.ts#L69-L117)、[workflow_signals](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/postgres/postgres.ts#L211-L240)。对象状态定义见 [WorkflowRun](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/workflow-run.ts#L8-L83) 与 [StepAttempt](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/step-attempt.ts#L7-L75)。

因此，更准确的说法是“当前状态 + attempt 日志驱动重放”，而不是从不可变事件重建全部状态。

### 4.2 崩溃恢复路径

1. Client 写入 `pending` run。
2. worker 原子领取可用 run，写 `running`、`workerId`、30 秒 lease，并增加 run attempts。
3. worker 每 15 秒延长 lease；崩溃后 heartbeat 停止，`availableAt` 到期，别的 worker 可重新领取。
4. 新 worker 读取该 run 全部 step attempts，构建 `StepHistory`。
5. workflow 函数从头执行；已 `completed` 的 step 直接返回持久化 output，失败或未完成处继续执行。
6. sleep、等待 signal、等待子流程时，run 保持 `running`，清空 `workerId` 并把 `availableAt` 设为恢复时刻；worker slot 被释放。

证据：[Worker concurrency/claim](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/worker.ts#L12-L26)、[worker slots](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/worker.ts#L33-L66)、[heartbeat](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/worker.ts#L250-L295)、[执行入口与重放](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/execution.ts#L970-L1101)、[Postgres 原子领取](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/postgres/backend.ts#L476-L528)。

`StepHistory` 按 step name 缓存完成结果、累计同名失败次数、保留 running wait，并把同名调用自动编号为 `name`、`name:1`……；单个 run 最多 1000 条 attempts。[源码](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/step-history.ts#L169-L230)、[attempt 上限](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/step-history.ts#L320-L338)。

### 4.3 与 Tenon 的根本差异

Tenon 的 `WfInstance/WfToken/WfTask/WfTaskActor` 已经是可直接恢复的持久运行态；一次人工动作在单个 DB 事务里把状态推进到下一个“等人”点，不需要重新执行流程定义。`wf_history` 明确只是 append-only 审计和排查投影，不参与 Temporal 式重放（`backend/src/TenonAdmin.Workflow/Entities/WfHistory.cs:6-26`）。

这对人工审批更合适：一个审批节点可能等待数天，状态已经自然落在待办表中；重放代码不会增加价值，反而会引入时间、随机数、组织查询和外部 I/O 的确定性约束。Tenon 现有 Module 在产品匹配度和 Locality 上优于 OpenWorkflow。

## 五、重试、超时、sleep、事件等待与取消

### 5.1 重试和超时

- `step.run` 默认最多 10 次，指数退避从 1 秒开始、上限 100 秒；可逐 step 覆盖。[源码](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/execution.ts#L135-L183)
- workflow 函数 step 之外的错误默认不重试（`maximumAttempts: 1`），定义可覆盖，`0` 表示无限重试。[源码](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/workflow-definition.ts#L131-L141)
- run 可设 `deadlineAt`；领取时会批量把超期活动 run 标成 failed，下一次重试若越过 deadline 也直接终止。[领取 SQL](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/postgres/backend.ts#L479-L506)、[失败决策](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/workflow-definition.ts#L162-L209)
- `runWorkflow` 和 `waitForSignal` 有等待超时；`step.sleep` 是持久计时器。`StepFunctionConfig` 没有单次函数执行 timeout/AbortSignal，所以慢或挂死的 step 仍依赖进程/调用方自行超时。

### 5.2 signal

`waitForSignal` 会建立 running signal-wait attempt，signal 到达后写 delivery 并把已停放 run 的 `availableAt` 提前到当前时间；超时返回 `null`。[等待实现](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/execution.ts#L815-L925)

但 signal **不缓冲**：发送时没有 waiter 就丢失。这不适合替换 Tenon 的审批待办；`wf_task + wf_task_actor` 是 durable inbox，先有任务后有人处理，语义更强。将来外部 webhook callback 若采用 signal 思路，应改成按 correlation key 先落事件、后匹配 waiter 的持久 inbox，而不是照搬广播式、非缓冲 signal。

### 5.3 取消

取消会把活动 run 原子改成 `canceled`、清空 owner/availability，重复取消同一 canceled run 成功返回。[状态转换](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/postgres/backend.ts#L734-L760)、[幂等冲突处理](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/workflow-run.ts#L26-L56)。

它不是强制中断：当前 `step.run` 函数可能继续执行，但后续 attempt/完成写入会被 `status='running' AND worker_id=当前 worker` 的 ownership fence 拒绝；执行层将已失去所有权的写冲突视为 stale transition。[所有权 fence](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/postgres/backend.ts#L930-L966)、[stale transition](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/execution.ts#L929-L965)。因此外部副作用仍可能已经发生。

Tenon 当前撤销是审批领域规则（仅特定实例状态/办理历史允许），不应退化为通用 run cancel。可借的是“终态写入后，所有旧 owner 的后续写都失败”的 fencing 原则。

## 六、幂等语义

### 6.1 workflow run 幂等

Client 可传 `idempotencyKey`；同 namespace、workflow name、key 在 24 小时内返回最早已有 run。version 不在幂等 identity 中，因此同名 workflow 的不同 version 复用同一个 key，仍会返回旧 run。[常量与参数](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/backend.ts#L11-L21)、[Client 说明](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/client/client.ts#L242-L259)、[Postgres 实现](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/postgres/backend.ts#L188-L219)。

Postgres 用 transaction-scoped advisory lock 把并发 create 串行化；SQLite 用 `BEGIN IMMEDIATE`。这比“先查再插”可靠，但 24 小时窗口和忽略 version 是 OpenWorkflow 的产品选择，不应原样复制。

### 6.2 step 幂等

- 同一 replay 中 step name 是 durable identity；完成记录存在就直接返回 output，不执行函数。[`step.run`](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/execution.ts#L428-L459)
- child workflow 用父 step attempt ID 生成稳定 key，避免“子 run 已创建、父 linkage 未落库”后的重复创建。[源码](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/execution.ts#L315-L335)、[创建与关联](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/execution.ts#L651-L695)
- workflow 内 sendSignal 用 `runId + stepName` 生成稳定 key，并把发送结果也记成 completed attempt。[源码](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/execution.ts#L757-L812)

边界必须写死：`step.run` 的函数和 step completion 不是同一事务。崩溃窗口内会留下 running attempt；恢复时 function step 不复用 running attempt，而是新建 attempt 再执行。因此调用支付、发消息、HTTP 等副作用时仍需下游幂等键。OpenWorkflow 的 chaos test证明 worker 重启后最终完成，不证明外部副作用 exactly-once。[chaos test](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/chaos.test.ts#L15-L70)

### 6.3 Tenon 当前缺口

Tenon 的 `WfTask.Version` 和 `CompleteTaskOp` CAS 能保证并发双击只有一个事务推进（`backend/src/TenonAdmin.Workflow/Entities/WfTask.cs:28-34`、`backend/src/TenonAdmin.Workflow/Engine/Operations/CompleteTaskOp.cs:30-52`），但这属于 conflict protection，不是 request idempotency：第一次已提交但 HTTP 响应丢失时，客户端重试得到 `TaskConflict`，拿不到第一次的成功结果。

建议增加显式 request key/operation receipt，至少覆盖 start、approve、reject、transfer、return、cancel、resubmit；幂等 identity 应包含租户/机构、命令类型、目标实例或任务、操作者和客户端 key，并在同一业务事务内写入结果。不要简单把 `businessKey` 设唯一，因为一张业务单据可能合法发起多个流程或多次重提。

## 七、dispatcher、worker、claim、并发与 Adapter

OpenWorkflow 没有中央 dispatcher 服务；数据库同时是状态库和队列。一个 Worker 的每个并发 slot 有独立 worker UUID，默认 concurrency=1；空轮询使用指数退避加 jitter。多进程通过数据库 claim 协调。[Worker](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/worker.ts#L29-L66)、[poll loop](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/worker.ts#L95-L147)。

- PostgreSQL Adapter 用 `FOR UPDATE SKIP LOCKED` 选择一条 run，再在同一语句写 owner 与 lease，适合水平扩展。
- SQLite 没有 `SKIP LOCKED`，用 `BEGIN IMMEDIATE` 串行化领取；适合单机，不适合高并发 worker。
- 每次 run/step 写入都校验 run 仍由当前 worker 持有；并行 `Promise.all` 另有进程内 `ExecutionFence`，避免某分支已经停放 run 后，兄弟分支继续写新 attempt。[ExecutionFence](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/execution.ts#L69-L106)、[step 写 fence](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/postgres/backend.ts#L804-L845)。

`availableAt` 同时承担首次调度、失败退避、sleep 唤醒、signal/child 唤醒和 lease expiry，Leverage 很高，也有语义过载：排查时必须结合 `status + workerId + attempt context` 才知道它代表哪一种时间。

Tenon 没必要为人工作业复制 worker pool：审批停留态已经由任务表表达，用户请求就是 dispatcher，且现有调度内核已有选主。真正适合建立 claim/lease Seam 的范围只有后台自动工作：超时扫描、要求可靠送达的通知、Webhook/自定义节点。对这些工作应优先使用 Tenon 已有 `IAdminJob` 和数据库 CAS；只有任务可能执行超过调度 lease、或允许多 worker 并行时，再引入 owner/lease/heartbeat。

## 八、版本、可观测性与测试

### 8.1 版本

OpenWorkflow 将 `(name, version)` 注册到进程内 registry，run 持久化 version；滚动升级时新旧实现并存，旧 run 完成后才能删旧代码。worker 缺少对应定义时不是终态失败，而是按 5 秒到 5 分钟、无限次数重排队。[registry](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/core/workflow-registry.ts#L7-L59)、[missing definition](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/packages/openworkflow/worker/worker.ts#L156-L184)、[官方迁移策略](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/apps/docs/docs/versioning.mdx#L82-L107)。

Tenon 的 `WfDefinitionVersion` 保存不可变 `ModelJson`，实例固定引用版本 ID（`backend/src/TenonAdmin.Workflow/Entities/WfDefinitionVersion.cs:6-30`），不要求保留旧代码部署。对数据驱动审批而言，Tenon 更简单、更稳；只有未来自定义节点 Implementation 自身发生不兼容变化时，才需要给节点 handler 增加 implementation version。

### 8.2 可观测性

OpenWorkflow dashboard 能看 run 状态、输入输出/错误、step attempts、尝试序号、父子 run 和取消操作；Prometheus 当前只有按状态的 run 数量 gauge。[run/step inspector](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/apps/dashboard/src/routes/runs/$runId.tsx#L499-L741)、[metrics](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/apps/docs/docs/prometheus.mdx#L62-L80)。dashboard 没有内置认证，官方要求放在 VPN、私网或认证代理后。[生产文档](https://github.com/openworkflowdev/openworkflow/blob/46dcc85d230bb54894dc4bab022a1ce34cc11c13/apps/docs/docs/production.mdx#L175-L210)

Tenon 已有 `wf_history` 和 `wf_his_task`，审批时间线比 OpenWorkflow 更贴近用户，但运维诊断还可补：命令 request key、CAS 冲突原因、通知调用状态、超时 attempt/error/nextRetryAt、Webhook 请求摘要与响应摘要。指标至少应有待办/逾期数、动作冲突数、通知失败数、超时触发数和自动节点延迟；启用可靠通道 outbox 后再增加 backlog。只抄状态总数 gauge 不够。

### 8.3 测试

最值得直接借的是共享 Adapter 契约测试：OpenWorkflow 用同一个约 2965 行 backend testsuite 验证 SQLite/Postgres 的创建幂等、并发创建、领取/lease、stale owner、取消、step 状态、signal delivery 等，再各自补数据库特有测试。Tenon 已跑四库 CI，但应把工作流关键语义收成一套 provider-neutral fixtures，避免 SqlSugar 在不同数据库上的事务/CAS/索引差异漏测。

## 九、按适配程度分类

### 9.1 可直接借鉴

1. **命令幂等回执**：从 start 到所有用户动作统一传 request key，同一 identity 返回第一次 `WfEngineResult`。
2. **可靠副作用记录**：领域状态、history 和必须保证执行的副作用意图在一个事务提交；独立 dispatcher 重试，发送端携带稳定 idempotency key。对当前纯 SignalR 提示先做可观测性，不把“调用成功”误当“用户已送达”。
3. **attempt 记录形状**：给超时动作和自动节点记录 `status/attempt/error/availableAt/startedAt/finishedAt`，成功结果可复用。
4. **ownership fence**：后台执行的每次写都校验 task version/owner；过期执行不能覆盖新 owner 或终态。
5. **共享持久化契约测试**：同一套测试跑 SQLite/MySQL/PostgreSQL/SQL Server。
6. **运维 inspector**：把失败 attempt、错误、耗时、重试和 next retry 集中展示。

这些都能作为 Tenon 现有 Module 的内部 Implementation，保持公开审批 Interface 不变，Leverage 高且 Seam 真实。

### 9.2 需要按人工审批改造

1. **`sleep` → 到期任务**：保留 `wf_task.DueTime` 和现有调度器，由 `WfTimeoutJob` 扫描并派发领域命令；不要让整个审批实例进入 replay sleep。
2. **step retry → 自动节点 retry**：只用于要求可靠送达的通知、Webhook、外部 callback 等机器动作；人的 approve/reject 不自动重放。
3. **signal → durable external event inbox**：按实例/节点/correlation key 持久化并可先到后等；不采用 OpenWorkflow 的非缓冲广播。
4. **cancel fence → 撤销领域规则**：保留“谁可撤销、何时可撤销、撤销后关闭哪些任务”的审批语义，只借 stale writer fence。
5. **child workflow → 子流程**：若 M3+ 出现真实需求，使用 definitionVersionId + parent instance/node 关联；超时不应默认让子流程继续运行，需按审批业务明确级联或脱离。
6. **handler version**：只给 Webhook/自定义节点 Implementation 加版本，不复制所有旧 workflow 代码。

### 9.3 不匹配，不建议引入

1. **确定性代码重放**：与 Tenon“数据库运行态直接恢复”冲突，会迫使组织查询、时间、随机和业务 I/O 遵守额外规则。
2. **通用 worker fleet**：人工审批大部分时间没有可执行代码，常驻 polling worker 没有收益。
3. **用 `stepName`/自动编号当审批节点身份**：Tenon 已有稳定 node ID 和版本快照，语义更明确。
4. **用 signal 代替待办**：非缓冲 signal 会丢提前到达的审批动作，也没有候选人、会签、转办、字段权限等领域模型。
5. **把 `Backend` Interface 搬进 Tenon**：Tenon 已有 SqlSugar 和四库抽象；再包一层同规模 CRUD/transition 接口会形成 hypothetical Seam。
6. **OpenWorkflow dashboard 直接嵌入**：无认证，页面与 OA 权限/参与者可见范围不匹配；只借 inspector 信息架构。

## 十、建议实施顺序与里程碑落点

### M2b：只吸收自然重合项

1. 落地 `WfTimeoutJob : IAdminJob`。截至本次基线，代码中已有 `WfTask.DueTime`、`WfTimeout` schema 和 `TimeoutFired` 枚举，但未找到具体 Job/handler Implementation。
2. 超时领取使用 `taskId + Version + DueTime <= now` CAS；CAS 失败表示人工动作或其他执行者已经胜出，继续复用现有调度器。
3. `IWorkflowNotifier` 保持 Adapter Seam；通知失败写结构化日志和指标，不再静默无痕。

当前 `WorkflowEngine` 在事务提交后直接调用 notifier，异常静默吞掉（`backend/src/TenonAdmin.Workflow/Engine/WorkflowEngine.cs:57-60,70-96`）。这保证通知不会早于提交，但运维侧无法知道失败。当前 SignalR 只是刷新提示，`wf_task` 才是事实源，因此先补日志/指标；若未来接入短信、邮件、企业 IM 或 Webhook 等承诺送达的通道，再新增 `wf_outbox`（`eventType + aggregateId + eventId` 唯一）和 dispatcher。

### M2c：可靠性收口

1. 新增 workflow operation receipt（名称在实现任务定稿）：唯一 identity 至少包含组织/租户、command type、target ID、actor user ID、request key；与领域事务一起落库，重复请求返回已存结果。
2. `Start/Approve/Reject/Transfer/Return/Cancel/Resubmit` 等写命令统一接收 `RequestId/IdempotencyKey`。前端在一次提交生命周期内复用同一 key，新动作才生成新 key。
3. 建 workflow persistence contract tests，四库共用，覆盖并发 start/action、重复 request key、事务回滚、超时 CAS、stale owner、终态保护。
4. 本阶段不新增审批动词、通用 worker 或 OpenWorkflow 风格 Backend Interface。

### M3：自动节点可靠执行

1. 为 Webhook/自定义节点建立统一执行器：稳定 execution key、attempt、deadline、retry policy、输出/错误摘要、取消检查和 ownership fence。
2. 必须保证执行的外部副作用使用事务型 outbox/attempt；外部请求携带业务幂等键。
3. 详情页增加“系统执行”区域，和人的审批意见时间线分开；指标接入现有观测体系。

### M3+：可选 AI 预审与按需能力

AI 预审基于节点 SPI/Webhook 做扩展 Adapter：高置信度按配置自动处理，低置信度、模型异常、超时或结构化输出失败统一转人工；模型 SDK、Prompt 和供应商配置不进入工作流内核，也不作为 GA 门槛。

durable external event inbox、子流程、补偿动作、自动节点并行度/优先级同样等真实消费者出现后再做；没有需求前不先建这些 Seam。

## 十一、最终判断

OpenWorkflow 的设计质量高点在于一个很深的执行 Module：小 `step` Interface 获得了高 Depth 和 Leverage，复杂租约、重放、attempt、Adapter 都留在 Implementation 内。它对 Tenon 的价值，是证明“可靠自动执行”可以压缩成少量内部原语。

Tenon 的优势则是领域 Locality：任务、办理人、会签、退回、转办、发起权限、表单权限和版本快照都直接表达人工审批，不必绕成 code workflow。正确路线不是融合两套引擎，而是在 Tenon 现有 Token/Agenda 下增加一条窄的可靠执行 Seam：

`审批命令 → 幂等回执 + 单事务推进 → 按通道语义记录副作用意图`，以及 `后台自动动作 → claim/fence → attempt/backoff → 终态`。

这样能拿到 OpenWorkflow 最有价值的可靠性，又不承担 deterministic replay、通用 worker 和非缓冲 signal 的产品错位成本。
