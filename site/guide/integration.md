# System Integration: From Credentials to Reliable Delivery

When an ERP, warehouse system, or partner platform needs to call your business API, you must answer four questions: who is calling, which endpoints it may call, which data it may access, and how failures can be traced. `TenonAdmin.Integration` brings these controls into one module and adds outbound call records and reliable delivery for calls in the other direction.

This guide follows one complete path: create a partner application, issue a credential, grant only the required endpoints and data scopes, test with the separate OpenAPI document, and observe one delivery task through completion.

When you are implementing open endpoints, custom data scopes, outbound clients, or delivery adapters, use the [System Integration Technical Reference](./integration-reference.md) alongside this tutorial.

## Choose the right capability first

The following four concepts solve different problems. Separating them prevents machine credentials from being confused with user roles or admin navigation.

| Capability | Represents | Used for |
| --- | --- | --- |
| Integration application | A machine identity such as an ERP, warehouse, or partner service | Calling open APIs with endpoint and data-scope grants |
| Portal application | A business entry in the admin UI | Organizing navigation, menus, and pages; it does not issue machine credentials |
| SSO | A person signing in to the admin UI | Unifying user identity and the sign-in flow |
| Workflow | A business approval | Orchestrating approval nodes, assignees, and routing conditions |

This guide uses an integration application. It does not inherit admin user roles, and seeing a menu in a portal does not grant the matching API.

::: info Version availability
Use a matching TenonAdmin release that contains the system integration module, and keep `TenonAdmin.Integration` on the same version as `TenonAdmin`. The meta-package does not pull in or enable this module automatically.
:::

## Step 1: Install and enable the module

For a new project, install the matching template release with `dotnet new install TenonAdmin.Templates`, then run `dotnet new tenon-app --integration -n PartnerAdmin`. The template references `TenonAdmin.Integration`, completes the two wiring steps below, and generates an `Integrations/` directory with open API, plain outbound call, and reliable delivery examples.

Install the optional package in the consumer project:

```bash
dotnet add package TenonAdmin.Integration
```

Then register its services and assembly:

```csharp
using TenonAdmin.AspNetCore;
using TenonAdmin.Integration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdminIntegration(builder.Configuration);
builder.Services.AddTenonAdmin(builder.Configuration, options =>
{
    options.UseIntegration();
});

var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

`AddTenonAdminIntegration()` registers options, the authentication scheme, and replaceable services. `UseIntegration()` belongs in the `AddTenonAdmin` options callback. It includes the module entities in table creation and mounts the admin and open controllers. If the second step is missing, the host refuses to start so jobs cannot run without their required tables and endpoints.

To replace a built-in module service, register your implementation before `AddTenonAdminIntegration()`. The module follows TenonAdmin's `TryAdd*` convention, so a consumer registration made first wins.

Installing the module provides integration infrastructure; it does not invent business open APIs or delivery tasks. The grant catalog is empty until the application defines business controllers as open endpoints. The delivery list remains empty until business code enqueues work through `IDeliveryOutbox`.

To follow the remaining steps end to end, use [`IntegrationSample`](https://github.com/Tenon-Net/TenonAdmin/tree/main/backend/samples/IntegrationSample). It contains ticket open APIs, a custom data scope, an outbound client, and two delivery adapters, together with the locally controlled [`IntegrationMockPartner`](https://github.com/Tenon-Net/TenonAdmin/tree/main/backend/samples/IntegrationMockPartner). From the root of a matching source distribution that includes both samples, open three terminals:

```bash
# Terminal 1: local partner, fixed at http://127.0.0.1:5300
dotnet run --no-launch-profile --project backend/samples/IntegrationMockPartner

# Terminal 2: sample host, fixed at http://localhost:5100
ASPNETCORE_URLS=http://localhost:5100 \
TenonAdmin__Integration__Outbound__Targets__partner__Secret=partner-secret \
  dotnet run --no-launch-profile --project backend/samples/IntegrationSample

# Terminal 3: Vue admin UI at http://localhost:5175
npm --prefix web install
npm --prefix web run dev
```

`partner-secret` is the fixed value used only by the local mock. After the sample host starts for the first time, sign in with the super-admin password printed to its console. The sample adds open endpoints to the grant catalog. A delivery task appears only after you create a ticket through the admin endpoint `POST /api/v1/sample/partner-ticket`. If your project already implements business endpoints and delivery enqueueing, skip the sample and continue below.

## Step 2: Create an integration application

Sign in to the admin UI, open **第三方接入 → 接入应用**, and select “新增”. The screenshots use the Chinese UI. Create one application for one real caller instead of sharing an identity across several partners.

Focus on four fields:

1. **Application code:** choose a stable code that identifies the caller, such as `partner-warehouse`. Do not later reuse the same code for a different system.
2. **Application name:** use a name operations staff will recognize, such as “East China Warehouse API”.
3. **Owning organization:** select one when organization isolation applies. It anchors organization fields written by this application; it is separate from an admin user's data permissions.
4. **Rate limit:** set a per-minute limit that matches the partner's expected capacity. `0` means unlimited and should be an explicit decision.

[![Chinese integration application list showing application code, rate limit, active credentials, status, and the credential and more-action entries](/screenshots/integration-apps.png)](/screenshots/integration-apps.png)

In the screenshot, “凭据” manages API keys, while “更多” opens endpoint and data-scope actions. The “下载接口文档（JSON）” button above the table is used during API testing.

## Step 3: Issue and store a credential

Select “凭据” on the target application and issue a credential. A complete credential looks like `tna_<key-id>.<secret>` and is shown only in the successful issue or rotation response. The database and later lists retain or display only the identifiable part, so the plaintext cannot be recovered later.

Complete these actions before closing the dialog:

1. Store the credential in the caller's secret manager or a protected environment variable.
2. Record the owner, purpose, and expiry so the credential does not become an unowned long-lived secret.
3. Confirm that the caller received it securely. Never put the real value in screenshots, tickets, chat, or source control.

Use rotation when changing keys. The system issues a new credential and can keep the old one valid for an overlap window. Revoke the old credential after the caller switches. If compromise is suspected, revoke it immediately instead of waiting for expiry.

Issuing, rotating, or extending a credential widens or preserves application access, so only a super admin may perform these actions. A regular administrator receives `41003` even with the matching button permission. Revocation is a stop-loss action and can be delegated through button permissions.

## Step 4: Grant endpoints and data scopes

A credential proves which application is calling; it does not grant business access by itself. Open the application's endpoint and data permissions and apply least privilege at two levels:

1. **Endpoint grants:** select only the open endpoints the caller needs. Permissions use normalized routes such as `GET:/api/open/v1/orders`.
2. **Data scopes:** bind the allowed values for every scope declared by those endpoints. A missing required scope is denied and never falls back to unrestricted access.

| Scope | Suitable for | Grant |
| --- | --- | --- |
| `none` | Endpoints that do not touch scoped data | No business scope value |
| `org` | Organization and descendant-organization isolation | Select the allowed organization |
| Custom scope | Partner code, tenant, warehouse, or another business partition | Select values supplied by the business scope policy |

Admin sign-in, portal menus, and user roles do not count as integration authorization. An integration application is evaluated only from its endpoint grants, data scopes, enabled state, and credential state.

Granting endpoints, binding data scopes, enabling an application, or changing its owning organization widens or restores access and is restricted to super admins. Regular administrators with the corresponding read permission may inspect existing configuration but cannot widen application access through button permissions.

## Step 5: Test with the separate OpenAPI document

Return to the integration application list and select “下载接口文档（JSON）”. This document contains only open endpoints and can be imported into a common API client or used for client generation. In production, obtain it through this admin-protected action; do not make open endpoints anonymous to simplify testing.

Store the credential in a local environment variable in the API client and send it through the `X-Api-Key` header. Examples and shared collections should contain only an obvious placeholder such as `tna_<key-id>.<secret>`. Test in this order so authorization errors are visible early:

1. Call an endpoint with a grant and a bound scope; confirm the normal business envelope.
2. Call an ungranted endpoint; confirm HTTP 403 with business code `49002`.
3. Remove a required scope and retry; confirm HTTP 403 with business code `49003`.
4. Revoke the test credential; confirm subsequent requests return HTTP 401 with business code `49001`.

After validation, issue the production credential and keep it only in the target environment's secret store.

## Step 6: Configure reliable delivery

When your application calls a third party, first define a named target under `TenonAdmin:Integration:Outbound:Targets`. The following configuration contains no secret and can be committed:

```json
{
  "TenonAdmin": {
    "Integration": {
      "Outbound": {
        "Targets": {
          "partner": {
            "BaseUrl": "https://partner.example.com/api/",
            "TimeoutSeconds": 10,
            "TrustedCidrs": [],
            "Auth": {
              "Type": "Bearer"
            }
          }
        }
      }
    }
  }
}
```

Supply the secret through the `TenonAdmin__Integration__Outbound__Targets__partner__Secret` environment variable, user secrets, or a secret-backed configuration provider. Do not commit a `Secret` value. Private addresses are blocked by default. If an internal target is required, allow only its explicit ranges in `TrustedCidrs`. Link-local and cloud metadata addresses remain blocked.

Reliable delivery also requires business code to implement an `IDeliveryAdapter` for the partner protocol, register it in DI, and enqueue through `IDeliveryOutbox` inside the business transaction. The adapter must truthfully declare whether the partner deduplicates by idempotency key and supports status queries; those capabilities determine whether an unknown result can be retried safely. The matching source distribution includes a complete example under `backend/samples/IntegrationSample`.

Put business data only in the queued payload. Credentials are read at send time and must never enter the payload, delivery key, or business key. The framework preserves one delivery key across attempts, but exactly-once execution still depends on the partner actually deduplicating by that key.

## Step 7: Observe delivery tasks

Open **第三方接入 → 发送任务**. Search by delivery key, adapter, business operation, or business key, then use the status tabs to decide what to do next.

[![Chinese delivery task list showing delivery key, adapter, business operation, business key, status, send count, and next processing time](/screenshots/delivery-tasks.png)](/screenshots/delivery-tasks.png)

The screenshot shows “成功”, “待核对”, “失败”, and “已受理待确认” together. They describe different conclusions:

| Status | Meaning |
| --- | --- |
| Pending / Processing | Waiting for a scan or already claimed by the dispatcher |
| Accepted, awaiting confirmation | The partner accepted the request but has not reported a final result; this is not success |
| Succeeded | The partner explicitly completed the operation |
| Needs reconciliation | The result is unknown and cannot be retried safely under the current capabilities; an operator must verify it with the partner |
| Retry exhausted / Failed | The automatic budget is used up, or the partner explicitly rejected or failed authentication; fix the cause first |
| Cancelled | An administrator intentionally closed the task |

Select “详情” to inspect attempt history, call IDs, and the operations currently allowed. Manual retry, confirm succeeded, confirm not executed, and cancel are checked again by the server and preserve the original delivery key. Confirmation actions should include the evidence for later audit.

## Step 8: Inspect call records

The module records each direction separately:

- **开放接口调用记录:** a third party called your system. Inspect the application, credential identifier, endpoint, HTTP status, business code, duration, source IP, and trace ID.
- **第三方接口调用记录:** your system called a third party. Inspect the target, business operation, method, status, result classification, failure reason, duration, and call ID.

For delivery issues, copy the call ID from the task detail and find the same request in the outbound record. For open API issues, use the response trace ID to correlate the inbound record with server logs.

Call records do not store authentication headers, request bodies, or response bodies, and error summaries must not include credentials or sensitive business data. If business troubleshooting needs more context, record an irreversible digest or a business-side reference instead of copying secrets or full payloads.

## Step 9: Verify the path and troubleshoot failures

A complete integration path should pass these checks:

- The host starts and the “第三方接入” menu is visible.
- The test application has one clearly justified set of endpoints and data scopes.
- The credential is in a secret store, with no plaintext in source control, screenshots, or logs.
- The separate OpenAPI document imports successfully, and success, denied, and revoked cases behave as expected.
- A test business action creates a delivery task with visible attempt history.
- The task and both call-record pages can be correlated by delivery key, call ID, or trace ID.

| Symptom | Check first |
| --- | --- |
| Startup reports a missing `UseIntegration()` | Confirm that `options.UseIntegration()` is called inside the `AddTenonAdmin` options callback |
| Open API returns 401 / `49001` | Check whether the credential is complete, expired, or revoked, and whether the application is disabled |
| Open API returns 403 / `49002` | Check whether the normalized route is granted to the application |
| Open API returns 403 / `49003` | Check whether the endpoint's declared data scope is bound |
| Open API returns 429 | Check the application rate limit and source authentication-failure limit |
| Outbound result is `target_blocked` | Check whether the resolved address is denied and whether an exact private range belongs in `TrustedCidrs` |
| Outbound result is `credential_missing` | Check whether the secret-backed configuration source provides a value for the target |
| Task remains “已受理待确认” | Check whether the partner supports status queries or has called an authorized callback endpoint |
| Task enters “待核对” | Verify execution with the partner, then use an allowed detail action; do not bypass reconciliation by creating a new task |
