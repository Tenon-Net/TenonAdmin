# 前端权限

没有权限的操作按钮不会显示，用户也就不会进入一个必然返回 403 的流程。前端需要解决的是授权状态放在哪里，以及模板按钮、行内操作和状态开关分别怎样使用同一套判定。

> 前置：先完成[入门教程的普通角色授权](/zh/frontend/getting-started#_5-给普通角色补齐访问权)。

## 先给刷新按钮加一道门

把教程页的刷新按钮改成下面这样：

```vue
<n-button v-auth="'GET:/api/v1/sys/position/page'" :loading="loading" @click="load">
  刷新
</n-button>
```

用有岗位查询权限的账号登录，按钮可见；撤掉该权限并重新登录，按钮隐藏。即使按钮消失，直接请求接口仍应由后端返回 403。`v-auth` 负责不让用户走进必败操作，服务端负责真正拒绝越权。

[![在角色权限树中同时检查菜单与接口权限](/screenshots/role-permissions.png)](/screenshots/role-permissions.png)

## 全景：两个 store，按持久化需求拆分

- **`user` store**（`src/stores/user.ts`）：保存令牌、会话模式和用户资料。body 会话把 access/refresh 与资料持久化；Cookie 会话只持久化 `cookieSession` 标记，access 只留内存，refresh 只存在 HttpOnly Cookie，两个令牌都不会写入 Web Storage。
- **`auth` store**（`src/stores/auth.ts`）：`modules`、`currentModuleId`、`defaultModuleId`、`menuTree`、`permissionCodes`、`permissionsLoaded`、`isSuperAdmin`、`routesReady`，以及 `homePath` 和 `hasPerm` 两个 getter。声明为 `persist: { pick: ['currentModuleId'] }`，持久化的**只有 `currentModuleId` 一项**。

两边的生命周期不同。body 会话靠持久化令牌维持登录；Cookie 会话刷新后根据 `cookieSession` 标记，用 HttpOnly refresh Cookie 静默换取新的 access。权限码、菜单树和 `routesReady` 每次启动都要重新拉取，因为动态路由只存在于 router 内存中。若持久化 `routesReady: true`，刷新会跳过重建并让动态页面落到 404。`currentModuleId` 单独保留，用于恢复上次应用，再由 `enterInitial()` 拉取其余授权状态。

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

指令值就是这颗按钮对应接口的权限码。除了单个字符串，它还接受权限码数组（默认 OR：命中任意一个即显示）和带 `.and` 修饰符的数组（AND：全部命中才显示）：

```vue
<n-button v-auth="['a', 'b']">命中 a 或 b 就显示</n-button>
<n-button v-auth.and="['a', 'b']">a 和 b 都命中才显示</n-button>
```

指令在 `mounted` 时建立 `watchEffect`，持续订阅 `authStore.hasPerm`。权限加载完成或缓存刷新后，它会重新判定并更新显示状态；卸载时停止订阅：

```ts
export const vAuth: Directive<HTMLElement, string | string[]> = {
  mounted(el, binding) {
    const auth = useAuthStore()
    const need = binding.value
    const mode = binding.modifiers.and ? 'every' : 'some'
    const stop = watchEffect(() => {
      const ok = Array.isArray(need) ? need[mode]((c) => auth.hasPerm(c)) : auth.hasPerm(need)
      el.style.display = ok ? '' : 'none'
    })
    stopHandles.set(el, stop)
  },
  unmounted(el) {
    stopHandles.get(el)?.()
    stopHandles.delete(el)
  },
}
```

指令用 `display: none` 隐藏节点，目的是允许权限状态变化后恢复显示。节点仍在 DOM 中，因此不能把它当成安全边界。真正的授权始终由服务端 `[RolePermission]` 过滤器执行；指令只负责界面呈现。

## `hasPerm`：超管放行 / 未加载藏起来 / 精确匹配

`v-auth` 指令和 render 函数里的按钮走的是同一个 getter，显隐规则因此只收敛在一处：

```ts
hasPerm(state): (code: string) => boolean {
  return (code) => (state.isSuperAdmin ? true : state.permissionsLoaded && state.permissionCodes.includes(code))
},
```

三种状态：

1. **超管（`isSuperAdmin`）→ fail-open。** 全部放行，和后端 `[RolePermission]` 里 `sadm` claim 的绕过逻辑呼应。
2. **权限码还没加载完（`permissionsLoaded === false`）→ fail-closed。** 所有受权限控制的按钮都藏起来。这不是可以忽略的边角情况。守卫会 await 住 `enterInitial`，所以正常登录不会出现权限码没到位的闪烁窗口。fail-closed 真正兜的是另一种情况，`/personal/permissions` 取码失败。这时候不知道用户到底有没有权限，谎报「有」比谎报「无」糟得多。要是把「未加载」当成「有权限」，所有受控按钮都会先闪一下再消失，包括用户根本没权限的那些。fail-closed 保证用户看到的永远只有自己能用的按钮，不会有一闪而过的越权 UI。
3. **已加载的普通用户 → 按 `permissionCodes` 精确匹配。** 空集合表示没有任何授权，必然匹配不上权限码。超管状态由独立的 `isSuperAdmin` 字段表达，不能从权限集合是否为空推断。

## 把门控铺到每个操作按钮

`v-auth` 是模板语法，只在 `<template>` 里生效，列表页的行内操作则由列的 `render` 函数用 `h()` 创建。编辑、删除、复制、重置密码、强制下线和还原等操作也必须走同一套判定。列表页、树表页和菜单管理的 `ButtonManager.vue` 都在 render 中直接调用 `authStore.hasPerm(code)`：命中才创建按钮，不命中返回 `null`。判定规则没有变化，只是从模板指令换成了 render 函数。

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

不是所有门控都靠隐藏。启停开关这类按钮更适合置灰而非藏起来。藏了用户会以为功能不存在，置灰则告诉他「这里有个开关，只是你动不了」。所以状态列的 `StatusSwitch` 是把权限接到 `disabled` 上：

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

权限码就是规范化后的路由本身，形如 `{METHOD}:/{路由模板}`，例如 `GET:/api/v1/ping`。也就是说，没有另一套独立的字符串词汇要对齐。前端登录进门户时，`useModule().enterInitial()` 并行发两个请求：`GET /personal/permissions` 拉权限码集合，成功后把 `permissionsLoaded` 置真；请求失败则保留 `false`，所有普通权限门控都按 fail-closed 隐藏。`GET /personal/profile` 提供超管标记，失败时沿用登录响应里的用户快照，没有快照才按普通用户处理。

既然权限码就是路由本身，前端也就没必要自造一套权限词汇。两端的分工也就清楚了。前端只按码决定按钮的显隐与禁用，后端才计算并强制同一个码。后端怎么把路由归一化成权限码、`[RolePermission]` 又怎么校验会话与授权，见[请求管线](/zh/backend/request-pipeline)。想围绕授权环节做替换，比如换权限计算、换会话校验，那部分设计见[可替换性模型](/zh/backend/replaceability)。

## 用两个账号验收

准备一个拥有目标权限的角色和一个没有该权限的角色。前者应看到按钮并能完成操作；后者不应看到普通操作按钮，受保护的状态开关应保持禁用。随后直接调用同一个后端接口，确认无权限账号仍返回 403。界面检查证明门控没有漏铺，直接请求则证明安全边界确实在服务端。
