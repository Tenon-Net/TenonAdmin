# DictTag — 字典值 → 标签

表格列的字典翻译:值 → 字典 label + 语义色 `n-tag`。替代每页手写 `h(NTag, …)` 三元映射。

## Props

| Prop | 类型 | 说明 |
|---|---|---|
| `typeCode` | `string` | 必传,字典类型编码 |
| `value` | `string \| null` | 待翻译的值(string,与后端 SysDictItem.value 对齐) |
| `typeMap` | `Record<string, TagType>` | 显式 value→标签色映射,优先于 fallback 约定 |

`TagType = 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error'`。

```ts
// 表格列
render: (r) => h(DictTag, { typeCode: 'common_status', value: r.status })
// 指定配色
render: (r) => h(DictTag, { typeCode: 'order_state', value: r.state, typeMap: { '2': 'error' } })
```

## 配色 fallback 约定(重要)

后端 SysDictItem **暂无样式字段**,缺省配色 = 启用项按 sort 升序取下标 `i % 4` 映射 `info/success/warning/error`——即**后端通过 sort 间接控制颜色**,顺序稳定则颜色稳定。这是临时约定:后端加 `tagType` 字段后改读字段并废弃(见组件头注释)。对颜色有语义要求的列(如「失败=红」)显式传 `typeMap`,别依赖 fallback。

## 容错

- value 未命中字典 / 字典未加载完 → 渲染原始值文本(`type="default"`),不空白不闪烁;
- 空值(`null`/`''`)→ 渲染「—」,与既有页面空值惯例一致。

数据基座与失效时机见 [DictSelect README](../DictSelect/README.md) 的「数据基座」一节。
