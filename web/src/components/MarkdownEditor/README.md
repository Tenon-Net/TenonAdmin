# MarkdownEditor / MarkdownView

通知公告的 Markdown 编辑与渲染,封 [`md-editor-v3`](https://github.com/imzbf/md-editor-v3)。跟随应用明暗主题(`useAppStore().isDark`)。
**存 Markdown 纯文本**(不存 HTML)→ 展示端 `MarkdownView` 用库自带渲染器,无 `v-html` XSS 面。

## MarkdownEditor(编辑)

| 名称 | 类型 | 说明 |
|---|---|---|
| `value?` | `string \| null` | v-model:value,Markdown 文本。 |
| `@update:value` | `(v: string)` | 变更。 |

图片上传走 `fileApi.upload`,插进正文的是后端签发的 **`viewUrl`**(签名直链)。

> **不要用 `storagePath` 当 URL。** 它是存储层的相对路径,而后端**默认不静态托管上传目录**——真去 `UseStaticFiles()` 托管它就是鉴权绕过(整个上传目录任人匿名下载,见 `docs/deployment.md`)。`viewUrl` 指向 `GET /api/v1/sys/file/{id}/view?sig=…`:匿名可取(`<img>` 带不了 Authorization 头),但签名是文件 Id 的 HMAC,伪造不了。
>
> 它是**永久**能力链接(拿得到链接就拿得到图):正文是持久内容,带过期时间的 URL 等于"发布半小时后图片全坏"。撤销手段是删文件或轮换 JWT 密钥。

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
