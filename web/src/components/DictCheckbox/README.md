# DictCheckbox

字典多选(复选框组)。补齐字典族的多选形态,与 `DictSelect` / `DictRadio` 同源(基座 `stores/dict.ts`,`typeCode` 取数 + 缓存 + 并发去重)。
`inheritAttrs:false` + `v-bind="$attrs"`:`v-model:value`(**数组**)、`disabled`、`size` 等透传给 `n-checkbox-group`。停用项自动过滤。加载中/失败渲染空组,数据到达自动补齐。

## Props

| prop | 类型 | 说明 |
|---|---|---|
| `typeCode` | `string` | 字典类型编码。可变(级联)→ 自动重载。 |

## 用法

```vue
<DictCheckbox v-model:value="form.channels" type-code="notify_channel" />
```

> 字典项增删改后,字典管理页调 `useDictStore().invalidate(typeCode)` 失效缓存。
