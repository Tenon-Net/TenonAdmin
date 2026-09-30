# Core Concepts

For customer management, order management, and similar applications, TenonAdmin supplies shared capabilities such as accounts, roles, menus, and permissions. Your project owns customer fields, order rules, and business pages. Understanding that boundary helps you decide whether a requirement belongs in business code or a framework extension.

## Why not just another admin template

The backend integrates into your ASP.NET Core project through NuGet. The documentation calls these shared backend capabilities the *kernel*. The *host* is the application that starts them. A *business assembly* is a compiled module containing your entities, services, or controllers.

The frontend is delivered as a source template. Choose Vue or React and add pages to it; you do not need both. Backend updates arrive through NuGet, while frontend updates require merging or migrating upstream changes.

For customer management, the framework handles sign-in and permission checks while you implement the customer entity, endpoints, and list page. Register the assembly containing your entities and controllers in `ApplicationAssemblies` so the framework can discover them. Follow [Add a Business Module](/guide/business-module) for the steps.

## Users, roles, and data permissions

Role permissions determine which operations a user can perform. Data permissions determine which records they can access. Configure both separately.

| Business question | Concept | Customer management example |
|---|---|---|
| Who is using the system? | User | A sales employee's account |
| Which pages and operations are available? | Roles, menus, and endpoint permissions | View customers without permission to delete them |
| Which records are accessible? | Role data scope | Self, own organization, organization and descendants, selected organizations, or all |
| Where does a record belong? | Creator and creating organization | The framework fills these audit fields when a record is created |

RBAC means role-based access control: grant permissions to roles, then assign roles to users. A sales manager and a sales employee can open the same customer list while having different data scopes.

Menus and buttons control visible entry points. Backend endpoints still check permissions independently; hiding a button is not authorization. Test with a regular user too, because the super administrator bypasses role permission checks.

## Request pipeline

Opening a customer list leads to these checks:

1. **Identify the user**: the frontend sends an access token obtained during sign-in. The backend validates the token and session; invalid authentication returns `401`.
2. **Check the operation**: endpoints marked with `[RolePermission]` use normalized routes as permission codes, such as `GET:/api/v1/sample/doc`. Administrators grant the matching permission through role management.
3. **Resolve the data scope**: the framework combines records allowed by the user's roles into `IDataScopeContext`. Queries that support data scopes filter records accordingly.
4. **Return the result**: data and status normally use `Result<T>`. Numeric `ErrorCode` values identify business errors, and the frontend provides localized messages. Responses such as file downloads use their own format.

See [Request Pipeline](/backend/request-pipeline) for the detailed flow and extension points.

## Data layer conventions

An entity is a C# type mapped to a database table. Tables without organization isolation normally inherit `BaseEntity`; tables that need data scopes inherit `DataEntity`. The framework accesses the database through `SqlSugarScope` and applies soft-delete and data-scope filters to supported queries.

The framework fills fields such as `Id`, `CreateTime`, `CreateUserId`, `CreateOrgId`, `UpdateTime`, and `UpdateUserId` during creation and updates. Business code supplies fields such as customer names. Soft deletion marks records as deleted, and ordinary queries exclude them.

Query filtering does not secure every write operation. Use protected repository write paths and check record visibility before updating or deleting. Raw SQL and direct ORM writes need their own permission checks. See [Multi-Org Data Permissions](/backend/data-scope) for the boundaries.

Record IDs use a snowflake generator. Instance `WorkerId` values must not conflict; see [Multi-Replica Deployment](/guide/deployment/docker) for automatic allocation and explicit configuration.

## The replaceability model

Choose an extension based on the behavior you need to change:

| Requirement | Approach |
|---|---|
| Store files in object storage | Implement the storage interface and replace the default service |
| Add processing after sign-in | Inherit the built-in service and override the relevant `virtual` method |
| Add entities and endpoints | Register business services explicitly, and add the entity and controller assembly to `ApplicationAssemblies` |

Built-in services use `TryAdd*`, which preserves an existing registration of the same interface. Register replacements before `AddTenonAdmin()`. `ReplaceabilityTests` protect interfaces, overridable methods, and assembly discovery, but upgrades still require checking extension compatibility. See [Replace Built-in Services](/guide/replace-service).

## Package layering

Most applications only need to install `TenonAdmin`. Use the following table when tracing dependencies or writing extensions:

| Package | Responsibility |
|---|---|
| `TenonAdmin.Core` | Interfaces, configuration, results, and error codes; no SqlSugar or ASP.NET dependency |
| `TenonAdmin.SqlSugar` | Data access, entity base classes, automatic table creation, and seed data |
| `TenonAdmin.Services` | Built-in entities, services, role permissions, and data scopes |
| `TenonAdmin.AspNetCore` | Service registration, authentication, controllers, and request filters |
| `TenonAdmin` | Entry package referencing host integration and bringing in its dependencies |

Dependencies follow `AspNetCore → Services → SqlSugar → Core`. See [Architecture & Package Dependencies](/backend/architecture).

For ERP, warehouse, or partner connections, use the optional [System Integration](/guide/integration) package to assign separate credentials and API grants to each external application. These are managed separately from employee sign-in. Reliable delivery also provides delivery tasks, retries, and manual reconciliation.
