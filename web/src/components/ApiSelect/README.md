# ApiSelect

通用远程分页下拉,其余远程选择器(UserSelect…)的基座。封 `n-select`,负责:初次加载、远程搜索(防抖)、loading、竞态守卫(只认最新一次)、选项归一。
`inheritAttrs:false` + `v-bind="$attrs"`:`v-model:value`、`multiple`、`placeholder`、`size`、`disabled` 等透传给 `n-select`。加载失败时显示错误与重试入口，并通过 `error` 事件提供原始异常；不弹全局提示。新筛选失败时清空旧选项，避免选到不属于当前筛选的值。

搜索输入或 `reloadOn` 变化会立即废弃旧响应；筛选重载会取消未执行的搜索防抖。KeepAlive 停用时停止防抖并废弃在途响应，恢复后仅对已发起过加载的实例刷新。

## Props

| prop | 类型 | 默认 | 说明 |
|---|---|---|---|
| `fetch` | `(keyword: string) => Promise<SelectOption[] \| { items: SelectOption[] }>` | — | **必填**。传搜索词回选项(已归一 `{label,value}`)或 `{items}`;`keyword` 为空 = 初次/全量拉一页。 |
| `remote?` | `boolean` | `true` | 服务端搜索:每次输入调 `fetch`。`false` 则只加载一次,交 `n-select` 客户端过滤。 |
| `immediate?` | `boolean` | `true` | 挂载即加载(预取首屏)。 |
| `debounce?` | `number` | `300` | 搜索防抖 ms。 |
| `reloadOn?` | `unknown` | — | 该值变化时重新加载(如 UserSelect 的 `orgId` 部门过滤)。 |

## 用法

```vue
<!-- 直接用:自备 fetch 归一成 {label,value} -->
<ApiSelect
  v-model:value="form.tagId"
  :fetch="(kw) => tagApi.page({ page: 1, pageSize: 50, name: kw }).then(r => r.items.map(t => ({ label: t.name, value: t.id })))"
/>
```

多数场景直接用封装好的 `UserSelect`。
