import { MdPreview } from 'md-editor-rt'
import 'md-editor-rt/lib/style.css'
import { useAppStore, isDark } from '@/stores/app'

/**
 * 只读渲染 Markdown(MdPreview,较 MdEditor 轻)。跟随应用明暗主题。通知详情/列表展示用。
 * 全局 Markdown 运行时由 `main.tsx` 在首次渲染前挂上 XSS 过滤。
 * 对应 Vue 侧 `MarkdownEditor/MarkdownView.vue`。
 */
export function MarkdownView({ value }: { value?: string | null }) {
  const dark = useAppStore(isDark)
  // no{Katex,Mermaid,Highlight}:md-editor-rt 默认挂载即从 unpkg 懒加载这些扩展的 JS/CSS —— 破坏气隙自包含
  // (E7)。通知正文用不到公式/图表,代码高亮设为可选(消费者要则经 editorExtensions 喂本地 hljs 实例)。全关 = 零触网。
  return <MdPreview modelValue={value ?? ''} theme={dark ? 'dark' : 'light'} noKatex noMermaid noHighlight />
}
