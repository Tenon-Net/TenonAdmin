# CodeBlock 代码/JSON 只读展示

Naive `NCode` + `highlight.js/lib/core` 按需注册语言(现仅 `json`),右上角悬浮复制按钮(`useClipboard`,复制成功图标变对勾约 1.5s)。配色走 Naive 主题的 code 变量,明暗随主题,不引 hljs css。

## Props

| 名称 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `code` | `string` | 必填 | 已格式化的文本;格式化由调用方负责(如 `JSON.stringify(v, null, 2)`) |
| `language` | `string` | `'json'` | hljs 语言名;未注册的语言 NCode 自动降级纯文本渲染,不报错 |
| `wordWrap` | `boolean` | `true` | 长行自动换行(抽屉等窄容器必开) |
| `copyable` | `boolean` | `true` | 是否显示复制按钮 |

## 用法

```vue
<CodeBlock v-if="row.paramJson" :code="pretty(row.paramJson)" />
<span v-else>—</span>
```

- 空值占位(`—`)由调用方判空,组件不渲染空壳(与 DictTag 同惯例)。
- 需要新语言(yaml/sql…)在 `index.vue` 顶部 `hljs.registerLanguage` 追加,别全量引 `highlight.js`。
- 异常堆栈不要用本组件——堆栈非代码,高亮无意义,危险色 `<pre>` 更达意(见 `log/op/index.vue`)。

范例页:`src/views/system/log/op/index.vue`(详情抽屉的请求入参)。
