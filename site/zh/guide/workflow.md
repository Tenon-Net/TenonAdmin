# 审批工作流（0.7.0 预览版）

工作流作为可选包发布。仓库的 MinimalHost 已接好它，适合先跑通一笔审批；自己的应用则需同时安装内核与工作流包。Vue 和 React 模板都能操作同一套流程 API。

## 接入可选包

在自己的 ASP.NET Core 项目中安装同版本的两个包：

```bash
dotnet add package TenonAdmin --version 0.7.0-preview.1
dotnet add package TenonAdmin.Workflow --version 0.7.0-preview.1
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

## 跑通一次审批

先按[快速开始](/zh/guide/getting-started)启动 MinimalHost 和所选前端，以 `superAdmin` 登录。这个账号可同时访问「系统」与「业务中心」，便于首次验证。

1. 在「系统 → 流程管理 → 流程定义」新建草稿，输入流程名称。进入设计器，在开始节点下添加审批节点，并指定一个可登录的办理人；首次体验可指定超管自己。
2. 保存并发布流程定义。只有已发布的定义能从发起页选择。
3. 切到「业务中心 → 审批中心 → 发起流程」，选择刚发布的定义，填写页面要求的字段并提交。
4. 在「待我审批」打开待办并同意；到「我发起的」和「我已办的」查看实例与办理记录。管理员还可在「系统 → 流程管理 → 流程监控」查看实例。

需要表单时，在设计器中配置内置表单及审批节点的字段权限，再发布新版本。条件分支、并行分支、抄送和长期委托也已提供；建议先用上面的单节点流程确认账号与权限，再增加分支。

## 给员工授权

普通员工需要在角色管理中获得「业务中心 → 审批中心」下所需菜单，以及对应的按钮权限；仅能看到菜单不代表能调用所有审批接口。流程管理员则在「系统 → 流程管理」获得流程定义、监控或长期委托的相应权限。设计、发布和查看全局监控不应默认授给所有员工。

长期委托只影响规则生效后新产生的任务，不改派已有待办。转办或一次性委托是待办详情中的另一种操作。

## 预览版边界

- 默认的 `IWfOutboxTransport` 是 `NoOpWfOutboxTransport`。需要向外部系统投递消息时，在 `AddTenonAdminWorkflow` **之前**注册真实实现；否则外发消息会进入失败状态。失败记录与重放有 API，但目前没有完整的 Outbox 死信管理页面。
- AI Decision 目前只在后台评估并记录审计结果（shadow-only），不会自动同意、拒绝或推进审批。
- 这是 `0.7.0-preview.1` 预览功能。升级前查看[更新日志](/zh/changelog)，不要把预览能力当作稳定的 1.0 契约。
