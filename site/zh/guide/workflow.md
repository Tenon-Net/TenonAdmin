# 审批工作流 {#审批工作流-0-7-0-预览版}

请假、费用申请等需要多人确认的业务，可以通过审批工作流安排办理人和处理顺序。工作流通过可选包接入，Vue 与 React 提供对应页面。首次体验建议使用已接入的 MinimalHost，先完成一个单审批节点流程，再接入自己的应用。

## 跑通一次审批

先按[快速开始](/zh/guide/getting-started)启动 MinimalHost 和所选前端，以 `superAdmin` 登录。这个账号可同时访问「系统」与「业务中心」，便于首次验证。

1. 在「系统 → 流程管理 → 流程定义」新建草稿，输入流程名称。进入设计器，在开始节点下添加审批节点，并指定一个可登录的办理人；首次体验可指定超管自己。
2. 保存并发布流程定义。只有已发布的定义能从发起页选择。
3. 切到「业务中心 → 审批中心 → 发起流程」，选择刚发布的定义，填写页面要求的字段并提交。
4. 在「待我审批」打开待办并同意；到「我发起的」和「我已办的」查看实例与办理记录。管理员还可在「系统 → 流程管理 → 流程监控」查看实例。

需要表单时，在设计器中配置内置表单及审批节点的字段权限，再发布新版本。条件分支、并行分支、抄送和长期委托也已提供；建议先用上面的单节点流程确认账号与权限，再增加分支。

完成后应能在「我发起的」看到审批结果，在「我已办的」看到处理记录。如果发起页找不到流程，先确认定义已发布；如果办理人看不到待办，检查所选账号与角色权限。

下图展示带条件分支的报销流程。先找到开始、审批与结束节点；完成单节点练习后，再参考它增加分支。

[![流程设计器：审批节点与条件分支](/screenshots/workflow-designer.png)](/screenshots/workflow-designer.png)

办理人在详情中核对申请内容和流转记录后，再选择同意或拒绝。点击截图可查看原图。

[![审批详情与办理记录](/screenshots/workflow-approval.png)](/screenshots/workflow-approval.png)

## 接入可选包

安装前查看[更新日志](/zh/changelog)，选择需要的版本，并在自己的 ASP.NET Core 项目中安装同版本内核与工作流包：

```bash
dotnet add package TenonAdmin
dotnet add package TenonAdmin.Workflow
```

在 `Program.cs` 注册工作流服务，并把工作流程序集交给内核扫描：

```csharp
using TenonAdmin.AspNetCore;
using TenonAdmin.Workflow;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdminWorkflow(builder.Configuration);
builder.Services.AddTenonAdmin(builder.Configuration, options => options.UseWorkflow());

var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

`AddTenonAdminWorkflow` 要在 `AddTenonAdmin` 前调用，才能让预先注册的可替换服务生效；`UseWorkflow` 让工作流实体参与建表、控制器参与路由发现。仓库的 `backend/samples/MinimalHost` 已按此方式接入。仓库提供的 `web/` 和 `web-react/` 有工作流界面；`templates/` 中的独立应用模板尚未接入工作流，使用它时仍需按上面的步骤安装和注册。

## 给员工授权

普通员工需要在角色管理中获得「业务中心 → 审批中心」下所需菜单，以及对应的按钮权限；仅能看到菜单不代表能调用所有审批接口。流程管理员则在「系统 → 流程管理」获得流程定义、监控或长期委托的相应权限。设计、发布和查看全局监控不应默认授给所有员工。

长期委托只影响规则生效后新产生的任务，不改派已有待办。转办或一次性委托是待办详情中的另一种操作。

## 判断运行时状态

`status=1` 表示实例尚未进入终态，不保证当前一定有待办。列表与详情接口还会返回 `blockedNodeIds`、`hasPendingTask` 和 `returnedToStarter`；接入方应按以下顺序判断，才能与 Vue、React 的展示一致：

| 顺序 | 条件 | 判断结果 | 内置界面标签 |
|---|---|---|---|
| 1 | `returnedToStarter=true` | 退回发起人，等待发起人修改后重提 | 退回发起人 |
| 2 | `returnedToStarter=false`、`status=1`、`blockedNodeIds` 非空、`hasPendingTask=true` | 部分阻断；至少一个活跃 token 没有关联待办，实例其他位置仍有待办 | 审批中（有节点已阻断） |
| 3 | `returnedToStarter=false`、`status=1`、`blockedNodeIds` 非空、`hasPendingTask=false` | 阻断；活跃 token 没有关联待办，实例也没有其他待办 | 已阻断 |
| 4 | 不符合前三项 | 按 `status` 判断；此时 `status=1` 为普通运行中 | 审批中 |

`blockedNodeIds` 是当前实现根据运行时数据推导的标记：它收集活跃、没有关联待办且不在 Start 节点上的 token 所在节点 ID。除了缺少办理人的审批节点，它也可能包含等待异步执行完成的 Webhook 或 AI Decision 节点。因此，不能只凭这个字段断定审批人缺失或节点执行失败。`hasPendingTask` 表示整个实例是否仍有待办，不是某个节点的属性。`returnedToStarter` 的优先级最高。实例退回后仍是 `status=1`，因为它还可以重提，并未结束。

管理员监控接口 `GET /api/v1/workflow/instance/monitor` 支持按 `Status`、`DefinitionId`、`BusinessKey`、`StarterUserId`、`ActorUserId` 和 `CcUserId` 筛选。其中业务键为精确匹配；办理人会匹配当前待办办理人或历史办理人。内置监控页面提供状态、业务键、发起人、办理人和抄送人筛选，`DefinitionId` 目前只由接口接收。

监控接口目前没有“仅看阻断”“仅看部分阻断”或“仅看退回发起人”的筛选参数，也没有对应的独立状态枚举。按 `Status=1` 查询会返回所有未结束实例；接入方可以对当前页按上表分类，但不能把客户端过滤后的当前页数量当作全局统计。

## 接入外部系统与 AI 的注意事项 {#预览版边界}

- 默认的 `IWfOutboxTransport` 是 `NoOpWfOutboxTransport`。需要向外部系统投递消息时，在 `AddTenonAdminWorkflow` **之前**注册真实实现；否则外发消息会进入失败状态。失败记录与重放有 API，但目前没有完整的 Outbox 死信管理页面。
- AI Decision 目前只在后台评估并记录审计结果（shadow-only），不会自动同意、拒绝或推进审批。
