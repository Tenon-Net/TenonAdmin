import { MdEditor } from 'md-editor-rt'
import 'md-editor-rt/lib/style.css'
import { useAppStore, isDark } from '@/stores/app'
import { fileApi } from '@/api'

/**
 * md-editor-rt 的图片上传:插的是后端签发的**签名直链 viewUrl**(不是 storagePath —— 后端默认不静态
 * 托管上传目录,storagePath 当 src 必是坏链)。viewUrl 匿名可取、不可伪造,<img> 直接能加载。抽出便于单测。
 */
export async function uploadImages(files: File[], callback: (urls: string[]) => void): Promise<void> {
  const outs = await Promise.all(files.map((f) => fileApi.upload(f)))
  callback(outs.map((o) => o.viewUrl ?? ''))
}

export interface MarkdownEditorProps {
  value?: string | null
  onChange?: (v: string) => void
}

/**
 * 通知公告 Markdown 编辑器:封 md-editor-rt,跟随应用明暗主题。存 Markdown 源文本 —— 注意 Markdown
 * **天然放行内联 HTML**,不等于无 XSS 面;`main.tsx` 会在首次渲染前全局挂上 XSSPlugin 兜底。
 * 对应 Vue 侧 `MarkdownEditor/index.vue`。
 */
export function MarkdownEditor({ value, onChange }: MarkdownEditorProps) {
  const dark = useAppStore(isDark)
  // no{Katex,Mermaid,Highlight,Prettier}:同 MarkdownView —— 关掉会从 unpkg 懒加载的扩展,气隙下零触网(E7)。
  return (
    <MdEditor
      modelValue={value ?? ''}
      onChange={(v) => onChange?.(v)}
      theme={dark ? 'dark' : 'light'}
      onUploadImg={uploadImages}
      noKatex
      noMermaid
      noHighlight
      noPrettier
    />
  )
}
