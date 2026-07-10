# DictRadio — 字典单选组

与 [DictSelect](../DictSelect/README.md) 同源(同一 dict store、同样过滤停用项、同样失败静默),渲染为 `n-radio-group` + `n-radio-button`,适合搜索栏少量互斥条件(状态/类型筛选)。

## Props

| Prop | 类型 | 说明 |
|---|---|---|
| `typeCode` | `string` | 必传,字典类型编码 |

其余(`v-model:value`、`size`、`disabled`……)经 `$attrs` 透传给 `n-radio-group`。

```vue
<DictRadio type-code="common_status" v-model:value="params.status" size="small" />
```

加载中/失败时渲染空组,数据到达自动补齐。数据基座与失效时机见 DictSelect README 的「数据基座」一节。
