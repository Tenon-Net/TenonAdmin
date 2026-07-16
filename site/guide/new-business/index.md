# Guide: Adding a New Business Module

> Using the existing "dictionary" module as a template, walk through the end-to-end process of adding a business module (e.g. `Product`).
> For detailed conventions, see the [Coding Standards](/standard/backend).
> Two routes: **A. Add it inside the kernel** (this repo, adding `Sys*`/built-in controllers directly); **B. Consumer adds it in their own business assembly** (recommended for consumers, mounted via `ApplicationAssemblies`, no kernel changes). The steps are mostly the same; differences are covered in A11.

The example below uses `Product` (swap in your own entity name).

---

## 0. Quick start (consumer scaffolding, Route B)

Consumers (those who install the NuGet packages) can use the official template to generate a **runnable admin host** in one command, pre-wired with a sample org-scoped business module (`Modules/SampleDoc*`):

```bash
dotnet new install TenonAdmin.Templates      # install the template (once)
dotnet new tenon-app -n Shop                 # generate the host + sample module
cd Shop && dotnet run                        # run it directly; a random super-admin password is printed to the console
```

- Zero-config default is SQLite, with automatic table creation + seeding; to switch databases, edit `TenonAdmin:Database` in `appsettings.json`.
- The generated `Modules/SampleDoc*` four-piece set (entity / interface / implementation / controller) is the **copy-paste template** for "adding the next business module": copy it, rename it, then add one line `TryAddScoped<IYourService, YourService>()` to `Program.cs` (the entity's table is created automatically, and the controller's routes are wired automatically).
- If you need to write it from scratch, or add it inside the kernel (Route A), keep reading.

## In this section

- [A. Backend](/guide/new-business/backend) — entity, DTOs, service, controller, error codes, caching, seed data, menus/authorization, tests, and the consumer route (Route B)
- [B. Frontend](/guide/new-business/frontend) — regenerating API types, wrapping the API, the CRUD view, mounting the menu, routing, i18n
- [C. End-to-End Checklist](/guide/new-business/checklist) — the full backend/frontend/permissions checklist

**Next:** [A. Backend](/guide/new-business/backend)
