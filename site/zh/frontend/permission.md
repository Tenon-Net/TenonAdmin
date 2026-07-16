# 前端权限

前端把授权相关的状态拆到两个 Pinia store 里：`user` 存身份和令牌，`auth` 存权限、菜单树和应用状态。本页讲按钮级权限判定是怎么串起来的——`v-auth` 指令、它背后依赖的 `hasPerm` getter，以及指令语法用不上时的 render 函数兜底写法。

## 全景：两个 store，按持久化需求拆分

- **`user` store**（`src/stores/user.ts`）——`accessToken`、`refreshToken`、`userInfo`（`userId`、`account`、`name`、`mustChangePassword`），以及 `isLoggedIn` getter 和 `setSession`/`clear` action。声明为 `persist: true`——**整个 store** 落 `localStorage`，所以刷新页面还能保持登录。
- **`auth` store**（`src/stores/auth.ts`）——`modules`、`currentModuleId`、`defaultModuleId`、`menuTree`、`permissionCodes`、`permissionsLoaded`、`isSuperAdmin`、`routesReady`，以及 `homePath` 和 `hasPerm` 两个 getter。声明为 `persist: { pick: ['currentModuleId'] }`——**只持久化 `currentModuleId` 一项**。

这么拆是因为两边的生命周期不一样。令牌和用户资料要扛得住刷新（不然"保持登录"就无从谈起）。权限码、菜单树、`routesReady` 则相反，每次应用启动都要重新拉：动态路由只活在 router 的内存里，`routesReady` 一旦被持久化成 `true`，刷新就会跳过路由重建，导致每条动态路由都 404。`currentModuleId` 是唯一的例外——持久化它是为了让 F5 或深链能先恢复"上次在哪个应用"，再由守卫经 `useModule().enterInitial()` 重新拉取其余一切。

## `v-auth`：按钮级权限指令

在 `main.ts` 里全局注册：

```ts
app.directive('auth', vAuth)
```

支持单个权限码、权限码数组（默认 OR）、以及带 `.and` 修饰符的数组（AND）：

```vue
<!-- 单个权限码 -->
<n-button v-auth="'POST:/api/v1/sample/doc'" @click="openAdd">{{ t('common.add') }}</n-button>

<!-- 数组,OR 语义:命中任意一个即显示 -->
<n-button v-auth="['POST:/api/v1/sample/doc', 'PUT:/api/v1/sample/doc/{id}']">...</n-button>

<!-- 数组 + .and 修饰符,AND 语义:必须全部命中才显示 -->
<n-button v-auth.and="['GET:/api/v1/sample/doc', 'DELETE:/api/v1/sample/doc/{id}']">...</n-button>
```

指令只实现了 `mounted` 钩子——挂载之后权限变化不会触发重新判定。挂载时它拿权限码去问 `authStore.hasPerm`，不通过就直接 `el.remove()`：

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

**它是物理移除 DOM 节点**——不是 `display: none`，也不是 `v-if` 条件渲染那种可以被重新触发的隐藏。没权限的按钮压根不存在于 DOM 里，没法靠改客户端状态或开发者工具把它"变出来"；真正的授权判定仍然始终在服务端完成（`[RolePermission]`，见[请求管线](/zh/backend/request-pipeline)）——这个指令只管 UX，把用户用不了的按钮挡在视线之外。

## `hasPerm`：超管放行 / 未加载藏起来 / 精确匹配

`v-auth` 指令和 render 函数里的按钮走的是同一个 getter，显隐规则因此只收敛在一处：

```ts
hasPerm(state): (code: string) => boolean {
  return (code) => (state.isSuperAdmin ? true : state.permissionsLoaded && state.permissionCodes.includes(code))
},
```

三种状态：

1. **超管（`isSuperAdmin`）→ fail-open。** 全部放行，和后端 `[RolePermission]` 里 `sadm` claim 的绕过逻辑呼应。
2. **权限码还没加载完（`permissionsLoaded === false`）→ fail-closed。** 所有受权限控制的按钮都藏起来。这不是可以忽略的边角情况——登录成功后、`GET /personal/permissions` 还没返回之前，每个页面都会短暂处于这个状态。如果把"未加载"当成"有权限"处理，所有受控按钮(包括用户根本没权限的那些)都会先闪一下再消失。fail-closed 保证用户看到的永远只有自己能用的按钮,不会有一闪而过的越权 UI。
3. **已加载的普通用户 → 按 `permissionCodes` 精确匹配。** 空的 `permissionCodes`(没有任何授权的用户)必然匹配不上任何码,受控按钮全部保持隐藏。这也堵上了历史上一个 bug 的口子——曾经"空集合"被误当成"超管"处理;现在两者由完全独立的字段(`isSuperAdmin` 和 `permissionCodes`)分别承载,空权限集不可能意外解锁一切。

## Render 函数里构建的按钮

`v-auth` 是模板语法,在 `h()` 调用里用不了——比如表格列是用 render 函数动态渲染的场景。这种情况下直接调 `authStore.hasPerm(code)`,拿到结果自己分支,判定规则和指令背后是同一套,只是换成命令式写法:

```ts
const columns: DataTableColumns<SampleDoc> = [
  // ...
  {
    title: () => t('common.operation'),
    key: 'op',
    render: (r) =>
      h(NSpace, { size: 4 }, () => [
        authStore.hasPerm('PUT:/api/v1/sample/doc/{id}')
          ? h(NButton, { onClick: () => openEdit(r) }, () => t('common.edit'))
          : null,
        authStore.hasPerm('DELETE:/api/v1/sample/doc/{id}')
          ? h(NButton, { type: 'error' }, () => t('common.delete'))
          : null,
      ]),
  },
]
```

## 权限码约定

权限码就是规范化后的路由本身——`{METHOD}:/{路由模板}`(例如 `GET:/api/v1/ping`)——不存在一套独立的字符串词汇需要另外对齐。前端登录后调一次 `personalApi.permissions`(`GET /personal/permissions`)拉取当前用户的权限码集合,存进 `authStore.permissionCodes`;超管标志则来自 profile 接口,填进 `authStore.isSuperAdmin`。既然权限码就是路由本身,前端也就没必要自造一套权限词汇——后端如何计算并强制执行同一个码,见[请求管线](/zh/backend/request-pipeline);围绕授权可替换的那部分设计,见[可替换性模型](/zh/backend/replaceability)。

## 接下来看什么

- [前端路由](/zh/frontend/routing/)
- [请求管线](/zh/backend/request-pipeline)
- [多组织数据权限](/zh/backend/data-scope)
