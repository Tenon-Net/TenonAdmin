# 前端权限

每个操作按钮都想按权限显隐：有权限的人看得到，没权限的人这个按钮压根不出现，点不到也就不会点了再吃一个 403。前端把这条规则收敛成一个判定，再用两种写法把它落到页面上：模板里的 `v-auth` 指令，和 render 函数里直接调的 `hasPerm` getter。要看清它怎么串起来，先从授权状态存在哪儿说起：两个 Pinia store，按各自的持久化需求拆开。

## 全景：两个 store，按持久化需求拆分

- **`user` store**（`src/stores/user.ts`）：`accessToken`、`refreshToken`、`userInfo`（`userId`、`account`、`name`、`mustChangePassword`），以及 `isLoggedIn` getter 和 `setSession`/`clear` action。声明为 `persist: true`，所以**整个 store** 都落 `localStorage`，刷新页面还能保持登录。
- **`auth` store**（`src/stores/auth.ts`）：`modules`、`currentModuleId`、`defaultModuleId`、`menuTree`、`permissionCodes`、`permissionsLoaded`、`isSuperAdmin`、`routesReady`，以及 `homePath` 和 `hasPerm` 两个 getter。声明为 `persist: { pick: ['currentModuleId'] }`，持久化的**只有 `currentModuleId` 一项**。

这么拆是因为两边的生命周期不一样。令牌和用户资料要扛得住刷新（不然"保持登录"就无从谈起）。权限码、菜单树、`routesReady` 则相反，每次应用启动都要重新拉：动态路由只活在 router 的内存里，`routesReady` 一旦被持久化成 `true`，刷新就会跳过路由重建，导致每条动态路由都 404。`currentModuleId` 是唯一的例外。持久化它是为了让 F5 或深链能先恢复"上次在哪个应用"，再由守卫经 `useModule().enterInitial()` 重新拉取其余一切。

## `v-auth`：模板里的按钮级指令

在 `main.ts` 里全局注册：

```ts
app.directive('auth', vAuth)
```

页面工具栏上的按钮就是它的用武之地。真实的用户管理页（`web/src/views/system/user/index.vue`）工具栏这样写：

```vue
<template #toolbar>
  <n-button v-auth="'POST:/api/v1/sys/user'" type="primary" @click="openAdd">
    {{ t('common.add') }}
  </n-button>
  <n-button
    v-auth="'POST:/api/v1/sys/user/batch-delete'"
    type="error"
    :disabled="!hasSelection"
    @click="batchDelete"
  >
    {{ t('common.batchDelete') }}
  </n-button>
</template>
```

指令值就是这颗按钮对应接口的权限码。除了单个字符串，它还接受权限码数组（默认 OR：命中任意一个即显示）和带 `.and` 修饰符的数组（AND：全部命中才显示）:

```vue
<n-button v-auth="['a', 'b']">命中 a 或 b 就显示</n-button>
<n-button v-auth.and="['a', 'b']">a 和 b 都命中才显示</n-button>
```

指令只实现了 `mounted` 钩子，所以挂载之后权限变化不会触发重新判定。挂载时它拿权限码去问 `authStore.hasPerm`，不通过就直接 `el.remove()`:

```ts
export const vAuth: Directive<HTMLElement, string | string[]> = {
  mounted(el, binding) {
    const auth = useAuthStore()
    const need = binding.value
    const mode = binding.modifiers.and ? 'every' : 'some'
    const ok = Array.isArray(need) ? need[mode]((c) => auth.hasPerm(c)) : auth.hasPerm(need)
    if (!ok) el.remove()
  },
}
```

**它是物理移除 DOM 节点**，而不是 `display: none`，也不是 `v-if` 条件渲染那种可以被重新触发的隐藏。没权限的按钮压根不存在于 DOM 里，没法靠改客户端状态或开发者工具把它"变出来"。但别把这当成安全边界：真正的授权判定始终在服务端完成（后端的 `[RolePermission]` 过滤器才是权威），这个指令只管 UX，把用户用不了的按钮挡在视线之外。

## `hasPerm`：超管放行 / 未加载藏起来 / 精确匹配

`v-auth` 指令和 render 函数里的按钮走的是同一个 getter，显隐规则因此只收敛在一处：

```ts
hasPerm(state): (code: string) => boolean {
  return (code) => (state.isSuperAdmin ? true : state.permissionsLoaded && state.permissionCodes.includes(code))
},
```

三种状态：

1. **超管（`isSuperAdmin`）→ fail-open。** 全部放行，和后端 `[RolePermission]` 里 `sadm` claim 的绕过逻辑呼应。
2. **权限码还没加载完（`permissionsLoaded === false`）→ fail-closed。** 所有受权限控制的按钮都藏起来。这不是可以忽略的边角情况。登录成功后、`GET /personal/permissions` 还没返回之前，每个页面都会短暂处于这个状态。如果把"未加载"当成"有权限"处理，所有受控按钮（包括用户根本没权限的那些）都会先闪一下再消失。fail-closed 保证用户看到的永远只有自己能用的按钮，不会有一闪而过的越权 UI。
3. **已加载的普通用户 → 按 `permissionCodes` 精确匹配。** 空的 `permissionCodes`（没有任何授权的用户）必然匹配不上任何码，受控按钮全部保持隐藏。这也堵上了历史上一个 bug 的口子。曾经"空集合"被误当成"超管"处理;现在两者由完全独立的字段（`isSuperAdmin` 和 `permissionCodes`）分别承载，空权限集不可能意外解锁一切。

## 把门控铺到每个操作按钮

`v-auth` 是模板语法，只在 `<template>` 里生效。可列表页的行内操作按钮它够不着：编辑、删除、复制、重置密码、强制下线、还原，都是在列的 `render` 函数里用 `h()` 拼出来的。这些按钮曾经对没权限的人照样显示，点下去才在服务端吃 403;组织机构页更是一处门控都没有。现在同一套判定铺到了每一颗操作按钮上：render 里直接调 `authStore.hasPerm(code)`，命中才 `h()` 出按钮，不命中就返回 `null`。判定规则和指令背后是同一套，只是从声明式换成命令式。

用户管理页的操作列就是最典型的写法：

```ts
// web/src/views/system/user/index.vue —— 操作列
render: (r) =>
  h(NSpace, { size: 4 }, () => [
    authStore.hasPerm('PUT:/api/v1/sys/user/{id}')
      ? h(NButton, { onClick: () => openEdit(r) }, () => t('common.edit'))
      : null,
    authStore.hasPerm('PUT:/api/v1/sys/user/{id}/password')
      ? h(NButton, { onClick: () => openReset(r) }, () => t('user.resetPassword'))
      : null,
    // 超管行不给删除按钮:既没权限也防误删;普通用户按 DELETE 码显隐
    r.isSuperAdmin || !authStore.hasPerm('DELETE:/api/v1/sys/user/{id}')
      ? null
      : h(NPopconfirm, { onPositiveClick: () => remove(r) }, {
          trigger: () => h(NButton, { type: 'error' }, () => t('common.delete')),
          default: () => t('user.deleteConfirm', { name: r.name }),
        }),
  ]),
```

操作多到一行放不下时（组织机构页把 4 个操作收成「编辑 + 更多▾」），下拉里的每个选项同样按码过滤，一个都没剩就干脆不出这个下拉：

```ts
// web/src/views/system/org/index.vue —— 操作列
const dropdownOptions = [
  authStore.hasPerm('POST:/api/v1/sys/org/add') ? { key: 'addChild', label: t('org.addChild') } : null,
  authStore.hasPerm('POST:/api/v1/sys/org/{id}/copy') ? { key: 'copy', label: t('org.copy') } : null,
  authStore.hasPerm('DELETE:/api/v1/sys/org/{id}') ? { key: 'delete', label: t('common.delete') } : null,
].filter((o) => o !== null)
// ...
dropdownOptions.length ? h(NDropdown, { options: dropdownOptions }) : null
```

不是所有门控都靠隐藏。启停开关这类按钮更适合置灰而非藏起来。藏了用户会以为功能不存在，置灰则告诉他"这里有个开关，只是你动不了"。所以状态列的 `StatusSwitch` 是把权限接到 `disabled` 上：

```ts
// web/src/views/system/user/index.vue —— 状态列
h(StatusSwitch, {
  value: r.enabled,
  // 超管不可停用(防自锁——停了就没法从 UI 恢复,后端也保护);无启停权限亦置灰
  disabled: r.isSuperAdmin || !authStore.hasPerm('PUT:/api/v1/sys/user/{id}/enabled'),
  request: (next: boolean) => userApi.setEnabled(r.id, next),
})
```

## 权限码约定

权限码就是规范化后的路由本身，形如 `{METHOD}:/{路由模板}`（例如 `GET:/api/v1/ping`）。也就是说，不存在一套独立的字符串词汇需要另外对齐。前端登录进门户时，`useModule().enterInitial()` 并行调 `GET /personal/permissions` 拉当前用户的权限码集合（存进 `authStore.permissionCodes`）和 `GET /personal/profile` 拿超管标记（存进 `authStore.isSuperAdmin`）;两个请求成功才把 `permissionsLoaded` 置真，任何一个失败都按普通用户、按 fail-closed 处理，不往越权那一侧倒。

既然权限码就是路由本身，前端也就没必要自造一套权限词汇。这也划清了两端的分工：前端只按码决定按钮的显隐与禁用，后端才计算并强制同一个码。它怎么把路由归一化成权限码、`[RolePermission]` 又怎么校验会话与授权，见[请求管线](/zh/backend/request-pipeline);围绕授权环节做替换（换权限计算、换会话校验）的那部分设计，见[可替换性模型](/zh/backend/replaceability)。
