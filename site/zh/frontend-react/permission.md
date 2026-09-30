# 前端权限

没权限的按钮不会渲染出来。React 没有指令系统，Vue 的 `v-auth` 到这边成了一个组件 `<Can>`：命中权限码就渲染子节点，不命中返回 `null`，按钮不进虚拟 DOM。授权状态存在哪、判定怎么算，决定了这套门控怎么搭起来。

> 前置：先完成[入门教程的普通角色授权](/zh/frontend-react/getting-started#_5-给普通角色补齐访问权)。

## 先给刷新按钮加一道门

在教程页导入 `Can`，再包住刷新按钮：

```tsx
<Can code="GET:/api/v1/sys/position/page">
  <Button loading={loading} onClick={() => void load()}>刷新</Button>
</Can>
```

有岗位查询权限时按钮渲染；撤掉权限并重新登录后，`<Can>` 返回 `null`。直接请求接口仍必须收到 403。`<Can>` 改善操作体验，服务端才是权限边界。

[![在角色权限树中同时检查菜单与接口权限](/screenshots/role-permissions.png)](/screenshots/role-permissions.png)

## 全景：两个 store，按持久化需求拆分

- **`user` store**（`src/stores/user.ts`）：保存令牌、会话模式、CSRF 标记和用户资料。body 会话持久化 access/refresh 与资料；Cookie 会话只持久化模式、CSRF 标记和资料，access 留在内存，refresh 只存在 HttpOnly Cookie。
- **`auth` store**（`src/stores/auth.ts`）：`modules`、`currentModuleId`、`defaultModuleId`、`menuTree`、`permissionCodes`、`permissionsLoaded`、`isSuperAdmin`、`routesReady`。判定逻辑走 `hasPerm` 纯函数和 `useHasPerm` hook，不是 Vue 那种 store getter。persist 的 `partialize` 只落 `currentModuleId` 一项。

两边生命周期不同。body 会话靠持久化令牌维持登录；Cookie 会话刷新后用 HttpOnly refresh Cookie 静默换取新的 access。权限码、菜单树和 `routesReady` 每次启动都要重新拉取，因为动态路由只存在于内存。若持久化 `routesReady: true`，刷新会跳过重建并让动态页面落到 404。`currentModuleId` 单独保留，用于恢复上次应用，再由守卫拉取其余授权状态。

## `<Can>`：没有指令，用组件包门控

React 没有指令系统，因此把受保护内容作为 children 放进 `<Can>`：

```tsx
// web-react/src/components/Can.tsx
export function Can({ code, every = false, children }: { code: string | string[]; every?: boolean; children: ReactNode }) {
  const has = useHasPerm()
  const codes = Array.isArray(code) ? code : [code]
  const ok = every ? codes.every(has) : codes.some(has)
  return ok ? <>{children}</> : null
}
```

单个字符串是最常见的写法，也收权限码数组：默认 OR（`some`，命中任一即显），加 `every` 变 AND（全命中才显）。真实的用户管理页（`web-react/src/views/system/user/index.tsx`）工具栏就这样写：

```tsx
<Can code="POST:/api/v1/sys/user">
  <Button type="primary" onClick={openAdd}>{t('common.add')}</Button>
</Can>
<Can code="POST:/api/v1/sys/user/batch-delete">
  <Button danger disabled={!batch.hasSelection} onClick={batch.run}>{t('common.batchDelete')}</Button>
</Can>
```

不命中时 `<Can>` 返回 `null`，React 不会渲染这棵子树，按钮也不会进入 DOM。Vue 的 `v-auth` 保留节点并切换 `display`，React 则从一开始就不创建节点。两边都会在授权状态变化后重新判定。

`<Can>` 通过 `useHasPerm()` 订阅权限相关字段，权限码变化会触发重新渲染。Vue 侧通过 `watchEffect` 达到同样效果，只是更新现有 DOM 节点的显示状态。

别把 `<Can>` 当安全边界。真正的授权判定始终在服务端，后端的 `[RolePermission]` 过滤器才是权威。这个组件只管 UX，把用户用不了的按钮挡在视线外。

## `hasPerm`：超管放行 / 未加载藏起来 / 精确匹配

`<Can>` 和操作列里命令式判权限的按钮，走的是同一个判定，显隐规则因此只收敛在一处：

```ts
export function hasPerm(
  s: Pick<AuthState, 'isSuperAdmin' | 'permissionsLoaded' | 'permissionCodes'>,
  code: string,
): boolean {
  return s.isSuperAdmin ? true : s.permissionsLoaded && s.permissionCodes.includes(code)
}
```

它写成纯函数，不是 store 里返回闭包的选择器，这是 zustand 逼出来的。zustand 的选择器每次渲染都会被调用，再拿返回值和上次做 `Object.is` 比对。选择器要是返回一个新建的函数，每次都「变了」，就无限重渲染。所以判定收敛在这个纯函数里，组件里的反应式取用交给 `useHasPerm()`，非组件处直接 `hasPerm(useAuthStore.getState(), code)`。

`useHasPerm()` 只订三个细粒度字段（`isSuperAdmin`、`permissionsLoaded`、`permissionCodes`），不订整个 store。否则 `menuTree`、`routesReady` 这些无关字段一动，全页带权限门的按钮都会跟着重渲染。

三种状态：

1. **超管（`isSuperAdmin`）→ fail-open。** 全放行，和后端 `[RolePermission]` 里 `sadm` claim 的绕过呼应。
2. **权限码没加载完（`permissionsLoaded === false`）→ fail-closed。** 所有受控按钮先藏起来。这不是可忽略的边角情况。守卫会 await 住 `enterInitial`，正常登录不会出现权限没到位的闪烁窗口。fail-closed 真正兜的是另一种情况：`/personal/permissions` 取码失败。这时候不知道用户到底有没有权限，谎报「有」比谎报「无」糟得多。把「未加载」当成「有权限」，所有受控按钮会先闪一下再消失，包括用户根本没权限的那些。
3. **已加载的普通用户 → 按 `permissionCodes` 精确匹配。** 空的 `permissionCodes` 匹配不上任何码，受控按钮全部保持隐藏。超管与空权限集由两个独立字段（`isSuperAdmin` 和 `permissionCodes`）分别承载，空集不会被误当成超管而意外解锁一切。

## 把门控铺到每个操作按钮

工具栏用 `<Can>` 包一下就够，操作列的行内按钮多半不走 `<Can>`，而是直接调 `useHasPerm()` 拿到的谓词。原因是操作列常要在权限码之外再叠一层判断，比如超管行不给删除、不给停用（防自锁），把这类判据和权限码合成一个谓词，比在 JSX 里套两层条件清楚。

组件顶部取一次谓词：

```ts
const has = useHasPerm()
```

用户管理页把组合判据放进 `userForm.ts`，操作列只消费最终的 `true` 或 `false`：

```ts
// web-react/src/views/system/user/userForm.ts
export const canEdit = (_r: { isSuperAdmin: boolean }, has: (c: string) => boolean) =>
  has('PUT:/api/v1/sys/user/{id}')

// 删除:超管行一律不可(自锁保护),叠加删除权限码。
export const canDelete = (r: { isSuperAdmin: boolean }, has: (c: string) => boolean) =>
  !r.isSuperAdmin && has('DELETE:/api/v1/sys/user/{id}')
```

操作列的 render 只问「能不能」，命中才出按钮：

```tsx
// web-react/src/views/system/user/index.tsx —— 操作列
render: (_, r) => (
  <Space size={4}>
    {canEdit(r, has) && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
    {canReset(r, has) && <Button type="link" size="small" onClick={() => openReset(r)}>{t('user.resetPassword')}</Button>}
    {canDelete(r, has) && <Button type="link" size="small" danger onClick={() => handleDelete(r)}>{t('common.delete')}</Button>}
  </Space>
),
```

操作多到一行放不下时（机构页把编辑之外的操作收进「更多▾」），下拉里每个选项同样按码过滤，一个都不剩就不出这个下拉：

```tsx
// web-react/src/views/system/org/index.tsx —— 操作列
const moreItems = ([
  has('POST:/api/v1/sys/org/add') ? { key: 'addChild', label: t('org.addChild') } : null,
  has('POST:/api/v1/sys/org/{id}/copy') ? { key: 'copy', label: t('org.copy') } : null,
  has('DELETE:/api/v1/sys/org/{id}') ? { key: 'delete', label: t('common.delete'), danger: true } : null,
] as MenuProps['items'])!.filter(Boolean)
// ...
{moreItems!.length > 0 && (
  <Dropdown menu={{ items: moreItems, onClick: onMore }} trigger={['click']}>
    <Button type="link" size="small">{t('common.more')}</Button>
  </Dropdown>
)}
```

不是所有门控都靠隐藏。启停开关这类按钮更适合置灰而不是藏起来：藏了用户以为功能不存在，置灰是在告诉他「这里有个开关，你动不了」。所以状态列的 `StatusSwitch` 把权限接到 `disabled` 上，判据仍是那个组合谓词（超管行禁停，叠加启停权限码）：

```tsx
// web-react/src/views/system/user/index.tsx —— 状态列
<StatusSwitch
  value={r.enabled}
  disabled={!canToggleEnabled(r, has)}
  request={(next) => userApi.setEnabled(r.id, next)}
  onChange={reload}
/>
```

## 权限码约定

权限码就是规范化后的路由本身，形如 `{METHOD}:/{路由模板}`，例如 `GET:/api/v1/ping`，没有另一套独立字符串要对齐。前端进入门户时，`enterInitial()` 并行发两个请求：`GET /personal/permissions` 拉权限码集合，成功后把 `permissionsLoaded` 置真；请求失败则保留 `false`，普通权限门控按 fail-closed 隐藏。`GET /personal/profile` 提供超管标记，失败时沿用登录响应里的用户快照，没有快照才按普通用户处理。

既然权限码就是路由，前端也没必要自造一套权限词汇，两端分工因此很清楚：前端只按码决定按钮的显隐与禁用，后端才计算并强制同一个码。后端怎么把路由归一化成权限码、`[RolePermission]` 又怎么校验会话与授权，见[请求管线](/zh/backend/request-pipeline)。想围绕授权环节做替换，比如换权限计算、换会话校验，那部分设计见[可替换性模型](/zh/backend/replaceability)。

## 用两个账号验收

准备一个拥有目标权限的角色和一个没有该权限的角色。前者应看到按钮并能完成操作；后者不应看到普通操作按钮，受保护的状态开关应保持禁用。随后直接调用同一个后端接口，确认无权限账号仍返回 403。界面检查证明 `<Can>`、render 谓词和禁用态没有漏铺，直接请求则证明安全边界仍在服务端。
