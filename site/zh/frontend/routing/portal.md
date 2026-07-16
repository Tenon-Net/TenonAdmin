# 多应用门户

TenonAdmin 的外壳是个多应用门户——一个用户可能属于好几个应用(模块),从一个选择器里切来切去。`useModule().enterInitial()`(定义在 `useModule.ts`)就是登录后或硬刷新时,决定直接把用户丢进某个应用还是弹出选择器的那个函数:

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

模块列表、权限码、profile 三者并行拉取。权限码和超管标记都是失败即收紧:`personalApi.permissions()` 一旦失败,`permissionsLoaded` 就留在 `false`,`v-auth` 指令把它当成「藏起来」而不是在拿不准的情况下放行。

进门户的判定顺序:

1. **没分配任何应用** → 弹选择器(带一个空态提示)。
2. **有记住的应用**(`auth.currentModuleId`,auth store 里唯一持久化的字段)且它还在用户的应用列表里 → 直接进它。硬刷新或深链能落回原来那个应用,靠的就是这一条。
3. **只有一个应用** → 直接进,不用弹选择器。
4. **配置了默认应用**(`defaultModuleId`,可通过 `setDefault` 设置)→ 直接进。
5. **以上都不满足** → 弹选择器。

`switchModule(moduleId)` 会重新走一遍 `enter()`、清空标签页 store(换了应用,标签栏理应从零开始)、再把当前路由替换成新应用自己的 `homePath`。

**上一节:** [动态路由:菜单树→真实路由](/zh/frontend/routing/dynamic)
**下一节:** [路由守卫](/zh/frontend/routing/guards)
