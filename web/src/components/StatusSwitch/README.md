# StatusSwitch — 表格行内启停开关

把「只读状态标签 + 进编辑弹窗才能改状态」升级为行内直改。**悲观更新**:点击不先翻转,`n-switch` 转 loading,请求成功才 emit;失败不 emit = UI 保持原值即回滚,没有「闪一下又弹回」的错误视觉;loading 自锁防连点。

## Props

| Prop | 类型 | 默认 | 说明 |
|---|---|---|---|
| `value` | `boolean` | 必传 | 当前状态(非 v-model,配 `@update:value` 回写行数据) |
| `request` | `(next: boolean) => Promise` | 必传 | 更新请求;resolve 即成功。后端无独立启停端点 → 传全量 update |
| `confirm` | `string \| ((next: boolean) => string \| null)` | — | 提供则切换前先 dialog 二次确认(基于 `useConfirm().ask`,Esc/遮罩/取消均视为放弃);函数形态按目标态出文案,**返回空值则本次跳过确认**(典型:只确认停用,启用直通) |
| `disabled` | `boolean` | — | 无权限等场景置灰(行内置灰比移除 DOM 合适) |
| `size` | `'small' \| 'medium'` | `'small'` | 表格行内用 small |
| `successMsg` | `string \| false` | `t('common.success')` | `false` 不弹成功 toast |

Emits:`update:value`。失败 toast 内置(`translateError`),业务不用管。

## 表格列用法

`toInput` 把行数据转完整入参(与 openEdit 回填共用一个函数,别写两份):

```ts
render: (r) => h(StatusSwitch, {
  value: r.enabled,
  request: (next: boolean) => xxApi.update(r.id, { ...toInput(r), enabled: next }),
  'onUpdate:value': (v: boolean) => { r.enabled = v },
})
```

只对停用(有副作用的一侧)确认,启用直通:

```ts
confirm: (next) => (next ? null : t('xx.disableConfirm', { title: r.title })),
```

一键停用会造成大面积不可见/不可达的行(如内置 system 应用、承载管理页的目录),直接 `disabled` 置灰,别只靠确认框。

## 注意

- `h()` 渲染进表格列时 `useDialog`/`useMessage` 依赖的 Provider 注入仍然可用(组件树在 App.vue Provider 之内)。
- 值类型钉死 `boolean`(后端 enabled 全线是 boolean);出现非 boolean 状态字段时再做泛型值(可加但先不加:`checkedValue/uncheckedValue`、乐观模式)。

范例页:`src/views/system/menu/index.vue`、`src/views/system/module/index.vue` 的状态列。
