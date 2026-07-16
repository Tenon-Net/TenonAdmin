# DictSelect — 字典下拉

`typeCode` 取数(经 `src/stores/dict.ts` 会话级缓存),渲染 `n-select`。表单/搜索栏的字典下拉不再手拼 options。

## Props

| Prop | 类型 | 说明 |
|---|---|---|
| `typeCode` | `string` | 必传,字典类型编码(如 `common_status`) |

**其余一切经 `$attrs` 透传给 `n-select`**:`v-model:value`、`placeholder`、`clearable`、`multiple`、`size`、`disabled`……不重复声明,Naive 文档即本组件文档。

```vue
<DictSelect type-code="common_status" v-model:value="params.status" clearable style="width: 160px" />
```

## 行为

- 停用项(`enabled=false`)直接过滤——选项里不出现不可选值。
- `typeCode` 可动态变化(级联场景):变化即自动加载新字典。
- 加载中自带 `loading`(本地跟踪请求在途,不是「缓存是否存在」);请求失败停止转圈、静默留空(字典是配角,不打断主流程),typeCode 变化或重新挂载时自动重试。
- 值类型是 **string**(与后端 SysDictItem.value 对齐);boolean 字段(如 `enabled`)不适用字典组件。

## 数据基座(三个字典组件共用)

`src/stores/dict.ts`:按 typeCode 缓存 + 并发去重(同 typeCode 多组件同时挂载只发一次请求),失败不写缓存(下次访问自然重试);**字典管理页增删改后调 `useDictStore().invalidate(typeCode?)` 失效**。页面要拿原始选项(自己拼 UI)用:

```ts
const options = useDictOptions('common_status')   // ComputedRef<DictItem[]>,自动触发加载
```

同族组件:[DictTag](../DictTag/README.md)(表格列翻译)。
