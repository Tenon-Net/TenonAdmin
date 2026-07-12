# JsonEditor

JSON 值编辑,零依赖薄版(不引 Monaco/CodeMirror):`n-input` textarea + 实时校验 + 一键格式化。非法时红边框 + 原生 `JSON.parse` 错误提示。
`inheritAttrs:false`:`autosize`/`placeholder` 等经 `$attrs` 透传给内部 textarea。

> **别无脑套在所有配置值上**:通用配置值是自由文本,纯字符串会被判非法。仅对**约定为 JSON** 的字段用(如某些结构化配置项、扩展元数据)。

## Props / Emits / Expose

| 名称 | 类型 | 说明 |
|---|---|---|
| `value?` | `string \| null` | v-model:value,JSON 文本。 |
| `@update:value` | `(v: string)` | 文本变更。 |
| `expose.valid()` | `() => boolean` | 供父表单提交前校验(空串视为合法)。 |

## 用法

```vue
<script setup>
const editor = ref()
async function save() {
  if (!editor.value.valid()) return false // 阻止提交
  // …
}
</script>

<JsonEditor ref="editor" v-model:value="form.metaJson" />
```
