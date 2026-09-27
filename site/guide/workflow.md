# Approval Workflows (0.7.0 Preview)

Workflows ship as an optional package. The repository's MinimalHost already wires it up, so you can run an approval before integrating it into your own app. Both the Vue and React templates use the same workflow API.

## Add the optional package

Install matching versions of the kernel and workflow packages in your ASP.NET Core project:

```bash
dotnet add package TenonAdmin --version 0.7.0-preview.1
dotnet add package TenonAdmin.Workflow --version 0.7.0-preview.1
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

## Run your first approval

Follow [Quick Start](/guide/getting-started) to run MinimalHost and your chosen frontend, then sign in as `superAdmin`. That account can access both System and Business Center for a first run.

1. Under System → Workflow Management → Workflow Definitions, create a draft and name it. Open the designer, add an approval node below Start, and choose a user who can sign in as the approver. For a first run, you can choose the super admin.
2. Save and publish the definition. Only published definitions appear on the start page.
3. Switch to Business Center → Approval Center → Start Workflow, choose the published definition, fill in the requested fields, and submit.
4. Open the task under My Pending Approvals and approve it. Check My Requests and My Completed Tasks for the instance and task history. An administrator can also inspect the instance under System → Workflow Management → Workflow Monitor.

For a form, configure the built-in form and approval-node field permissions in the designer, then publish a new version. Conditional and parallel branches, CC, and long-term delegation are also available. Verify accounts and permissions with the single-node flow before adding branches.

## Grant employee access

Regular employees need the relevant menus under Business Center → Approval Center and their button permissions granted through role management. Seeing a menu alone does not allow every approval API call. Grant workflow administrators the appropriate Workflow Management permissions under System for definitions, monitoring, or long-term delegation. Do not grant design, publishing, or global monitoring to every employee by default.

Long-term delegation affects newly created tasks after a rule takes effect; it does not reassign existing pending tasks. Transfer or one-time delegation from a task detail is a separate operation.

## Preview boundaries

- The default `IWfOutboxTransport` is `NoOpWfOutboxTransport`. To deliver messages to an external system, register a real implementation **before** `AddTenonAdminWorkflow`; otherwise outgoing messages enter a failed state. APIs support listing failed records and replaying them, but there is no full Outbox dead-letter management page yet.
- AI Decision currently evaluates and records audit results in shadow mode only. It never automatically approves, rejects, or advances an approval.
- This is a `0.7.0-preview.1` feature. Read the [Changelog](/changelog) before upgrading; preview behavior is not a stable 1.0 contract.
