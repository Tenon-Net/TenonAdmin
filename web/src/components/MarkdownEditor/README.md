# MarkdownEditor / MarkdownView

通知公告的 Markdown 编辑与渲染,封 [`md-editor-v3`](https://github.com/imzbf/md-editor-v3)。跟随应用明暗主题(`useAppStore().isDark`)。
**存 Markdown 纯文本**(不存 HTML)→ 展示端 `MarkdownView` 用库自带渲染器,无 `v-html` XSS 面。

## MarkdownEditor(编辑)

| 名称 | 类型 | 说明 |
|---|---|---|
| `value?` | `string \| null` | v-model:value,Markdown 文本。 |
| `@update:value` | `(v: string)` | 变更。 |

图片上传走 `fileApi.upload` 回 `storagePath` 当 URL。
> ponytail 提醒:图片能否显示取决于后端是否**静态托管** `storagePath`(本地存储默认多可直连)。后端不托管则仅影响"上传图片"子功能,不阻塞文字/链接编辑。

## MarkdownView(只读渲染)

用 `MdPreview`(较编辑器轻)。`value?: string | null` 传 Markdown 文本。

## 用法

```vue
import MarkdownEditor from '@/components/MarkdownEditor/index.vue'
import MarkdownView from '@/components/MarkdownEditor/MarkdownView.vue'

<!-- 编辑(发布表单) -->
<MarkdownEditor v-model:value="form.content" />

<!-- 展示(详情) -->
<MarkdownView :value="row.content" />
```
