# Approval Workflows {#approval-workflows-0-7-0-preview}

Use approval workflows to assign approvers and processing order for requests such as leave or expenses. Workflows are an optional package with pages in both Vue and React. For a first run, use MinimalHost, which already integrates it, and complete a single-approval-node flow before integrating your application.

## Run your first approval

Follow [Quick Start](/guide/getting-started) to run MinimalHost and your chosen frontend, then sign in as `superAdmin`. That account can access both System and Business Center for a first run.

1. Under System → Workflow Management → Workflow Definitions, create a draft and name it. Open the designer, add an approval node below Start, and choose a user who can sign in as the approver. For a first run, you can choose the super admin.
2. Save and publish the definition. Only published definitions appear on the start page.
3. Switch to Business Center → Approval Center → Start Workflow, choose the published definition, fill in the requested fields, and submit.
4. Open the task under My Pending Approvals and approve it. Check My Requests and My Completed Tasks for the instance and task history. An administrator can also inspect the instance under System → Workflow Management → Workflow Monitor.

For a form, configure the built-in form and approval-node field permissions in the designer, then publish a new version. Conditional and parallel branches, CC, and long-term delegation are also available. Verify accounts and permissions with the single-node flow before adding branches.

After approval, confirm the result in My Requests and the handling record in My Completed Tasks. If the definition is missing from the start page, check that it is published. If an approver cannot see a task, check the assigned account and role permissions.

This expense flow includes a condition branch. Identify Start, Approval, and End first; add branches after completing the single-node exercise. The screenshots show the Chinese interface.

[![Workflow designer with approval nodes and a condition branch](/screenshots/workflow-designer.png)](/screenshots/workflow-designer.png)

The approver checks the request and its history before approving or rejecting. Click a screenshot to open the full-size image.

[![Approval details and processing history](/screenshots/workflow-approval.png)](/screenshots/workflow-approval.png)

## Add the optional package

Check the [changelog](/changelog) before installation, choose the release you need, and install matching kernel and workflow versions in your ASP.NET Core project:

```bash
dotnet add package TenonAdmin
dotnet add package TenonAdmin.Workflow
```

Register workflow services in `Program.cs` and include the workflow assembly in kernel discovery:

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

Call `AddTenonAdminWorkflow` before `AddTenonAdmin` so earlier registrations of replaceable services can win. `UseWorkflow` includes workflow entities in table creation and controllers in route discovery. The repository's `backend/samples/MinimalHost` is wired this way. The bundled `web/` and `web-react/` apps include workflow pages; the standalone app under `templates/` does not yet wire up workflow, so install and register it as shown above when using that template.

## Grant employee access

Regular employees need the relevant menus under Business Center → Approval Center and their button permissions granted through role management. Seeing a menu alone does not allow every approval API call. Grant workflow administrators the appropriate Workflow Management permissions under System for definitions, monitoring, or long-term delegation. Do not grant design, publishing, or global monitoring to every employee by default.

Long-term delegation affects newly created tasks after a rule takes effect; it does not reassign existing pending tasks. Transfer or one-time delegation from a task detail is a separate operation.

## External delivery and AI behavior {#preview-boundaries}

- The default `IWfOutboxTransport` is `NoOpWfOutboxTransport`. To deliver messages to an external system, register a real implementation **before** `AddTenonAdminWorkflow`; otherwise outgoing messages enter a failed state. APIs support listing failed records and replaying them, but there is no full Outbox dead-letter management page yet.
- AI Decision currently evaluates and records audit results in shadow mode only. It never automatically approves, rejects, or advances an approval.
