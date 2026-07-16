# C. End-to-End Checklist

**Backend**
- [ ] Entity (choose `BaseEntity`/`DataEntity`) + Sugar attributes + unique index
- [ ] `*Models.cs` DTOs (records)
- [ ] `I*Service` + `*Service` (virtual, transactional, duplicate check includes soft-deleted rows)
- [ ] `TryAddScoped` registration in `ServicesSetup`
- [ ] Controller (`[ApiController]`/`[Route]`/`[Module]`, `[RolePermission]` on every action)
- [ ] Add codes to `ErrorCode`
- [ ] Cache only hot reads (`CacheKeys` + cache-aside + invalidation)
- [ ] Seed data (optional, fixed Id)
- [ ] Tests (`WebApplicationFactory`, both SQLite/MySQL legs green)

**Frontend**
- [ ] `npm run gen:api` to regenerate types
- [ ] Add a group to `api/index.ts`
- [ ] `views/<module>/<entity>/index.vue` (`useTable` + Naive table/form)
- [ ] i18n text + error-code translations
- [ ] `lint` + `typecheck` pass

**Configure permissions (at runtime)**
- [ ] Create the node in menu management (Path/Component matched up)
- [ ] Check off the grant in role management

**Previous:** [B. Frontend](/guide/new-business/frontend)
