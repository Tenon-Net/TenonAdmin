# 前端权限

没权限的按钮不会渲染出来。React 没有指令系统，Vue 的 `v-auth` 到这边成了一个组件 `<Can>`：命中权限码就渲染子节点，不命中返回 `null`，按钮不进虚拟 DOM。授权状态存在哪、判定怎么算，决定了这套门控怎么搭起来。

## 全景：两个 store，按持久化需求拆分

- **`user` store**（`src/stores/user.ts`）：`accessToken`、`refreshToken`、`userInfo`（`userId`、`account`、`name`、`avatar`、`mustChangePassword`），加 `isLoggedIn` 选择器和 `setSession`/`clear` action。用 zustand 的 persist 落 `localStorage`，`partialize` 白名单只放这三项，等价于 Vue 侧的 `persist: true`。刷新页面还留着登录态。
- **`auth` store**（`src/stores/auth.ts`）：`modules`、`currentModuleId`、`defaultModuleId`、`menuTree`、`permissionCodes`、`permissionsLoaded`、`isSuperAdmin`、`routesReady`。判定逻辑走 `hasPerm` 纯函数和 `useHasPerm` hook，不是 Vue 那种 store getter。persist 的 `partialize` 只落 `currentModuleId` 一项。

这么拆是因为两边生命周期不一样。令牌和用户资料要扛住刷新，否则「保持登录」无从谈起。权限码、菜单树、`routesReady` 相反，每次应用启动都得重新拉。`routesReady` 尤其不能持久化：动态路由只活在 router 的内存里，一旦它被存成 `true`，刷新就会跳过路由重建，每条动态路由都变 404。`currentModuleId` 是唯一例外，持久化它是为了让 F5 或深链先恢复「上次在哪个应用」，剩下的再由守卫调 `useModule().enterInitial()` 重新拉（见[门户与守卫](/zh/frontend-react/portal-guards)）。

## `<Can>`：没有指令，用组件包门控

React 里门控是个组件，不是指令。`v-auth` 那种「挂在元素上、由框架在挂载时跑一次」的机制 React 没有，取而代之的是把要保护的内容当 children 塞进 `<Can>`：

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

不命中时 `<Can>` 返回 `null`，React 根本不渲染这棵子树，按钮从头到尾没进过 DOM。效果和 Vue `v-auth` 的物理移除 DOM 一样：没权限的按钮不在页面里，靠改客户端状态或开发者工具都「变」不出来。机制不一样：Vue 先把节点挂上再 `el.remove()` 删掉，React 是压根不创建这个节点。

还有一处行为差别。`v-auth` 只实现了 `mounted` 钩子，挂载后权限再变不会重判；`<Can>` 靠 `useHasPerm()` 订阅 store，权限码一变就重渲染、跟着改显隐。正常登录时权限一次拉齐，这点差别多数时候碰不到，但它真实存在。

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

用户管理页把这些判据抽进 `userForm.ts`，让它们能被单测钉住：

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

权限码就是规范化后的路由本身，形如 `{METHOD}:/{路由模板}`，例如 `GET:/api/v1/ping`。没有另一套独立字符串要你去对齐。前端登录进门户时，`useModule().enterInitial()` 并行发两个请求：`GET /personal/permissions` 拉当前用户的权限码集合，存进 `authStore.permissionCodes`；`GET /personal/profile` 拿超管标记，存进 `authStore.isSuperAdmin`。两个都成功才把 `permissionsLoaded` 置真，只要有一个失败，就按普通用户、按 fail-closed 处理，绝不往越权那侧倒。

既然权限码就是路由，前端也没必要自造一套权限词汇，两端分工因此很清楚：前端只按码决定按钮的显隐与禁用，后端才计算并强制同一个码。后端怎么把路由归一化成权限码、`[RolePermission]` 又怎么校验会话与授权，见[请求管线](/zh/backend/request-pipeline)。想围绕授权环节做替换，比如换权限计算、换会话校验，那部分设计见[可替换性模型](/zh/backend/replaceability)。
