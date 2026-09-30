<!-- 本文件与中文基准版 README.zh-CN.md 保持同步 -->

English | [简体中文](README.zh-CN.md) | [日本語](README.ja.md)

<p align="center">
  <img src="web/design-mockups/brand/icon-128.png" width="96" height="96" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin</h1>

<p align="center"><strong>Add an admin framework with RBAC and data permissions to your ASP.NET Core project in three lines of code.</strong></p>

<p align="center">NuGet packages · Replaceable services and business extensions · Vue and React frontends</p>

<p align="center">
  <a href="https://www.nuget.org/packages/TenonAdmin"><img src="https://img.shields.io/nuget/v/TenonAdmin" alt="NuGet version"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions/workflows/backend-ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="Backend build status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Tenon-Net/TenonAdmin" alt="Apache License 2.0"></a>
</p>

<p align="center">
  <a href="#quick-start"><strong>Quick start</strong></a> ·
  <a href="https://tenonadmin.52moyu.net/login"><strong>Live demo</strong></a> ·
  <a href="https://tenon.52moyu.net/"><strong>Documentation</strong></a>
</p>

## About TenonAdmin

TenonAdmin is an admin framework for ASP.NET Core. It packages users, roles, menus, organization data permissions, dictionaries, configuration, operation logs, and file management as NuGet packages. The frontend comes in Vue and React versions.

Install the `TenonAdmin` package, then register its services and map its endpoints in `Program.cs`:

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

The framework handles shared administration features, while the application project owns business entities, services, and pages. Replace services through interfaces or inherit a built-in service and override its processing steps. Framework updates ship as NuGet package versions; the changelog documents compatibility changes.

- **Start with SQLite:** The first run creates the database and tables, then loads seed data.
- **Customize services:** Dependency injection and `virtual` methods let the application replace a service or one of its processing steps. Custom code remains in the application project.
- **Upgrade packages:** NuGet delivers fixes and features. Follow the changelog when a release includes compatibility changes.
- **Choose a frontend:** Use Vue 3 with Naive UI or React 19 with Ant Design 6. Both include table, form, and permission components for business pages.

## Quick start

The backend requires the .NET 10 SDK. The frontends require Node.js 22.12+.

### Add TenonAdmin to an existing project

Install the framework from your ASP.NET Core project directory:

```bash
dotnet add package TenonAdmin
```

Add the service registration and endpoint mapping shown above to `Program.cs`, keeping the application's existing creation and startup code. The framework registers JWT authentication, RBAC, data permissions, and the administration APIs.

See the [getting started guide](https://tenon.52moyu.net/guide/getting-started) for database configuration and integration steps. New projects can use the guide's `dotnet new tenon-app` template to create a backend host.

### Run the complete sample

Clone the repository and start the backend:

```bash
git clone https://github.com/Tenon-Net/TenonAdmin.git
cd TenonAdmin
dotnet run --project backend/samples/MinimalHost
```

The backend runs at http://localhost:5100. The sample uses SQLite. On the first run, it creates the database and tables, loads seed data, and prints a random password for `superAdmin` to the console.

Open another terminal at the repository root and start one frontend.

**Vue** — http://localhost:5173

```bash
cd web
npm install
npm run dev
```

**React** — http://localhost:5174

```bash
cd web-react
npm install
npm run dev
```

Sign in as `superAdmin` with the password from the console. On Windows, run `dev.bat` from the repository root to start the backend and both frontends.

## Features

### Replace services and extend your application

Services such as password hashing and file storage are provided through interfaces. Register your implementation before `AddTenonAdmin`, and the framework uses it in place of the built-in implementation. To change one processing step, inherit the built-in service and override the corresponding `virtual` method.

Service implementations remain in the application project, while NuGet packages maintain the framework code. Contract tests cover service replacement and business entity and controller discovery. See the [service replacement example](skills/replace-service.md).

### Authorize endpoints and scope data

When users open the same customer list, their role's data scope determines which records they receive. When a business entity implements `IOrgScoped` or inherits `DataEntity`, SqlSugar global filters add organization conditions to its queries. The business service owns the query, and the framework applies the data scope.

Menus, actions, and backend endpoints use permission codes composed of the HTTP method and route. Endpoint authorization controls available operations, while data scopes control the business records a user can access.

### Choose Vue or React

The Vue template uses Vue 3 and Naive UI. The React template uses React 19 and Ant Design 6. Each template owns its dependencies, routes, state, and components, and both connect to the same backend APIs.

API types are generated from OpenAPI. Business pages can reuse search tables, forms, dictionaries, organization and user selectors, file uploads, import wizards, and other components.

### Build approval workflows

The process designer supports approval steps, conditional branches, parallel branches, carbon copies, dynamic forms, and field permissions for each step. The approval interface supports starting requests, pending and completed work, return, withdrawal, transfer, delegation, adding or removing approvers, and reminders.

Runtime records include approval history, timeout actions, and execution retries. AI decision evaluation supports OpenAI-compatible model services and records results for review and audit. The approval process controls decisions and workflow progression.

### Connect external systems

Open APIs manage application credentials, endpoint authorization, and business data scopes for callers. External calls manage destination addresses, credentials, and call records for services the application invokes.

For operations that require delivery guarantees, the application can commit business data and a delivery record in the same database transaction, then send the message in the background. Follow-up handling can retry, query the result, or request manual review according to the receiver's idempotency and result-query support. Each attempt is recorded.

### Vibe Coding: follow project conventions

The repository provides [development skills](skills/README.md) for entities, CRUD, service replacement, scheduled jobs, import and export, and system integration. Each skill defines task steps, code references, and validation requirements.

AI assistants use these instructions to apply the project's entity base classes, service interfaces, permission rules, and frontend components when generating a business module. Developers review the business rules and generated code.

## Feature matrix

| Category | Capabilities |
| --- | --- |
| Authentication and sessions | Account and password, captcha, JWT and refresh-token rotation, online sessions, and forced sign-out; Cookie sessions, SMS sign-in, and external sign-in can be enabled through configuration |
| Authentication security | Login lockout, rate limiting, TOTP, recovery codes, account MFA policies, and identity verification for sensitive operations |
| Permissions and organizations | Roles, menus, action permissions, organization tree, and positions; all, current organization, current organization and descendants, current user, and selected organizations as data scopes |
| Administration | Multi-application portal, dictionaries, configuration, announcements, SignalR messages, operation logs, sensitive-input masking, audit fields, and recycle bin |
| Files and Excel | Upload and download, signed access, chunked upload, resumable upload, and instant upload; import templates, data preview, cell validation, duplicate detection, error reports, and export column selection |
| Approval workflows | Process design, dynamic forms, field permissions for each step, approval handling, timeout actions, Webhooks, runtime monitoring, and AI decision evaluation |
| System integration | Application credentials, open API authorization, business data scopes, application rate limiting, external calls, transactional delivery records, and processing logs |
| Scheduled jobs | Six-field cron, fixed intervals, and one-time triggers; code, HTTP, and SQL jobs, with SQL execution enabled through configuration; timeouts, retries, logs, failure alerts, and a standalone Worker |
| Frontend templates | Vue 3 + Naive UI and React 19 + Ant Design 6; Chinese and English, light and dark themes, layout settings, business components, and OpenAPI type generation |
| Data and deployment | SQLite, MySQL, SQL Server, and PostgreSQL; CodeFirst, multiple database connections, Redis shared cache, database leases, multiple replicas, Docker, and health checks |
| Development tools | Backend project template and AI development skills; Vue ProTable and IconPicker npm packages |

## Screenshots

<table>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/vue-admin.png"><img src="docs/screenshots/vue-admin.png" alt="Vue user management" width="480"></a><br>Vue user management</td>
    <td width="50%" align="center"><a href="docs/screenshots/react-admin.png"><img src="docs/screenshots/react-admin.png" alt="React user management" width="480"></a><br>React user management</td>
  </tr>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/workflow-designer.png"><img src="docs/screenshots/workflow-designer.png" alt="Workflow designer" width="480"></a><br>Workflow designer</td>
    <td width="50%" align="center"><a href="docs/screenshots/workflow-approval.png"><img src="docs/screenshots/workflow-approval.png" alt="Approval details" width="480"></a><br>Approval details</td>
  </tr>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/role-permissions.png"><img src="docs/screenshots/role-permissions.png" alt="Role permissions" width="480"></a><br>Role permissions</td>
    <td width="50%" align="center"><a href="docs/screenshots/integration-apps.png"><img src="docs/screenshots/integration-apps.png" alt="Integration applications" width="480"></a><br>Integration applications</td>
  </tr>
</table>

<details>
<summary>More pages</summary>

<table>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/org-management.png"><img src="docs/screenshots/org-management.png" alt="Organization management" width="480"></a><br>Organization management</td>
    <td width="50%" align="center"><a href="docs/screenshots/dictionary.png"><img src="docs/screenshots/dictionary.png" alt="Dictionary management" width="480"></a><br>Dictionary management</td>
  </tr>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/file-management.png"><img src="docs/screenshots/file-management.png" alt="File management" width="480"></a><br>File management</td>
    <td width="50%" align="center"><a href="docs/screenshots/scheduled-jobs.png"><img src="docs/screenshots/scheduled-jobs.png" alt="Scheduled jobs" width="480"></a><br>Scheduled jobs</td>
  </tr>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/workflow-pending.png"><img src="docs/screenshots/workflow-pending.png" alt="Pending approvals" width="480"></a><br>Pending approvals</td>
    <td width="50%" align="center"><a href="docs/screenshots/delivery-tasks.png"><img src="docs/screenshots/delivery-tasks.png" alt="Reliable delivery" width="480"></a><br>Reliable delivery</td>
  </tr>
</table>

</details>

## Live demo and example project

[Open the live demo](https://tenonadmin.52moyu.net/login) · [View the example project](https://github.com/Tenon-Net/tenon-example)

The demo runs as a separate application, `tenon-example`. Its backend integrates the framework through NuGet, its frontend is based on an admin template, and its business code includes a CRM module.

Use the business accounts on the sign-in page to open the customer list and observe how organization and data scopes affect the query result. See [One query, three numbers](https://github.com/Tenon-Net/tenon-example/blob/dev/docs/showcase-multi-org-data-scope.md) for the sample's business query and permission configuration.

## Repository layout

```text
TenonAdmin/
├── backend/                           # .NET backend
│   ├── src/                           # NuGet package source
│   │   ├── TenonAdmin/                # Installation entry point; references the ASP.NET Core integration package
│   │   ├── TenonAdmin.Core/           # Interfaces, configuration, results, and error codes
│   │   ├── TenonAdmin.SqlSugar/       # Data access, table creation, and global filters
│   │   ├── TenonAdmin.Services/       # Administration entities, business services, and seed data
│   │   ├── TenonAdmin.AspNetCore/     # Authentication, controllers, and host integration
│   │   ├── TenonAdmin.Workflow/       # Approval workflows
│   │   ├── TenonAdmin.Integration/    # Open APIs, external calls, and reliable delivery
│   │   ├── TenonAdmin.Excel/          # Excel import and export
│   │   ├── TenonAdmin.Caching.Redis/  # Redis cache
│   │   └── TenonAdmin.Auth.*/         # GitHub, WeCom, DingTalk, and WeChat sign-in
│   ├── samples/                       # Sample hosts
│   │   ├── MinimalHost/               # Administration API sample
│   │   ├── WorkerHost/                # Standalone job process
│   │   ├── IntegrationSample/         # System integration sample
│   │   └── IntegrationMockPartner/    # Mock partner for the integration sample
│   ├── tests/                         # Backend tests and test hosts
│   ├── Directory.Packages.props       # Backend dependency versions
│   └── TenonAdmin.slnx                # Backend solution
├── web/                               # Vue 3 + Naive UI admin template
├── web-react/                         # React 19 + Ant Design 6 admin template
├── templates/                         # dotnet new project template
├── skills/                            # AI development skills and reference templates
├── site/                              # Documentation site
├── docs/                              # Architecture, deployment, screenshots, and development references
├── scripts/                           # Contract checks, tests, and validation scripts
├── .github/workflows/                 # CI and release workflows
├── docker-compose.yml                 # Container deployment configuration
├── docker-compose.scale.yml           # Multiple-replica deployment configuration
└── LICENSE                            # Apache License 2.0
```

`web/` and `web-react/` maintain their own dependencies and build configuration. A business project references the backend packages through NuGet and uses one frontend template as its starting point.

## Documentation

| Task | Reference |
| --- | --- |
| Add TenonAdmin to a project | [Getting started](https://tenon.52moyu.net/guide/getting-started) |
| Develop business features | [Development skills and reference templates](skills/README.md) · [Vue components](web/COMPONENTS.md) · [React components](web-react/COMPONENTS.md) |
| Replace services | [Service replacement](skills/replace-service.md) |
| Add workflows and external systems | [Approval workflows](https://tenon.52moyu.net/guide/workflow) · [System integration](https://tenon.52moyu.net/guide/integration) · [Excel import and export](skills/wire-import-export.md) |
| Understand the architecture | [Architecture](https://tenon.52moyu.net/backend/architecture) · [Runtime architecture diagram](docs/architecture/tenon-runtime.en.architecture.html) |
| Deploy and upgrade | [Deployment guide](docs/deployment.md) · [Changelog](CHANGELOG.md) |

## Contributing

Development takes place on the `dev` branch. Report problems through [GitHub Issues](https://github.com/Tenon-Net/TenonAdmin/issues) and submit code changes as pull requests to `dev`.

## Copyright and license

Copyright © Tenon-Net

This project is licensed under the [Apache License 2.0](LICENSE), which permits commercial use, modification, and distribution.

When distributing this project or a derivative work, include a copy of the license, mark changed files, and retain applicable copyright, patent, trademark, and attribution notices in distributed source code. Follow Section 4 of the license for attribution notices from a NOTICE file.

Third-party components and assets remain subject to their respective licenses.
