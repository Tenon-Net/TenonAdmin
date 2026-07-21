import { MdPreview } from 'md-editor-rt'
import 'md-editor-rt/lib/style.css'
import { useAppStore, isDark } from '@/stores/app'

/**
 * 只读渲染 Markdown(MdPreview,较 MdEditor 轻)。跟随应用明暗主题。通知详情/列表展示用。
 * XSS 过滤由 `main.tsx` 的 `setupMarkdown()` 全局兜底(`@/lib/markdown`)—— MdPreview 默认会渲染正文里的
 * 内联 HTML,必须过滤。对应 Vue 侧 `MarkdownEditor/MarkdownView.vue`。
 */
export function MarkdownView({ value }: { value?: string | null }) {
  const dark = useAppStore(isDark)
  return <MdPreview modelValue={value ?? ''} theme={dark ? 'dark' : 'light'} />
}
