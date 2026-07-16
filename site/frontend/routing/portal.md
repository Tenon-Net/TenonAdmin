# Multi-App Portal

TenonAdmin's shell is a multi-app portal — a user can belong to several apps (modules) and switch between them from a chooser. `useModule().enterInitial()` (in `useModule.ts`) is what decides, right after login or on a hard refresh, whether to drop the user straight into an app or show the chooser:

```ts
async function enterInitial(): Promise<EnterResult> {
  const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
    personalApi.modules(),
    personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] })),
    personalApi.profile().then((p) => ({ sadm: p.isSuperAdmin })).catch(() => ({ sadm: false })),
  ])
  auth.modules = modules
  auth.defaultModuleId = defaultModuleId ?? null
  auth.permissionCodes = perm.codes
  auth.permissionsLoaded = perm.ok
  auth.isSuperAdmin = profile.sadm
  if (modules.length === 0) return { chooser: true }
  const remembered = auth.currentModuleId
  if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
  if (modules.length === 1) return enter(modules[0]!.id)
  if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
  return { chooser: true }
}
```

Modules, permission codes, and the profile flag are fetched in parallel. Permission codes and the super-admin flag fail closed on error — a failed `personalApi.permissions()` call leaves `permissionsLoaded = false`, and the `v-auth` directive treats that as "hide everything" rather than granting access it can't confirm.

The entry decision, in order:

1. **No modules assigned** → chooser (with an empty-state hint).
2. **A remembered app** (`auth.currentModuleId`, the *only* field persisted from the auth store) that's still in the user's module list → re-enter it. This is what makes a hard refresh or a deep link land back in the same app instead of bouncing to the chooser.
3. **Exactly one module** → auto-enter it, no chooser needed.
4. **A default module is configured** (`defaultModuleId`, settable via `setDefault`) → auto-enter it.
5. **Otherwise** → show the chooser.

`switchModule(moduleId)` re-runs `enter()` for the new app, clears the tabs store (a fresh app means a fresh tab bar), and replaces the current route with the new app's `homePath`.

**Previous:** [Dynamic Routes: Menu Tree → Real Routes](/frontend/routing/dynamic)
**Next:** [Router Guards](/frontend/routing/guards)
