// Markdown 渲染的全局安全 + 气隙自包含接线。与 web-react 侧 `lib/markdown.ts` 同源(两模板各自自包含、不共享)。
//
// 1) XSS:md-editor-v3 默认 `html:true`(markdown-it 原样放行内联 HTML)、`sanitize` 恒等空操作、预设插件链
//    **不含** XSS 过滤。通知作者(带权限即可,未必超管)存 `<img src=x onerror=…>`,任意查看者(含读通知的超管)
//    打开即执行 → 偷 localStorage 里的 JWT 达成账号接管。**这否证了原注释「存纯 Markdown → 无 XSS 面」的说法**
//    (Markdown 天然放行 HTML)。修:挂库自带 `XSSPlugin`(基于已随库打进来的 js-xss,零新依赖)。
// 2) 气隙:md-editor-v3 挂载即从 unpkg 懒加载 highlight/katex/mermaid/prettier/echarts。前四个在组件上用
//    `no-*` 关掉;echarts 无 `no*` 旗标,只能给空 no-op instance —— 既断 CDN,又避开其默认 `parseOption` 用
//    `new Function` 对通知正文求值的隐患(通知用不到 echarts 代码块)。
import { config, XSSPlugin } from 'md-editor-v3'
import type { MarkdownItConfigPlugin } from 'md-editor-v3'

/** 把 XSS 过滤插件追加到 markdown-it 预设插件链尾。抽成纯函数便于单测钉住「确实登记了」。 */
export function withXssPlugin(plugins: MarkdownItConfigPlugin[]): MarkdownItConfigPlugin[] {
  return [...plugins, { type: 'xss', plugin: XSSPlugin, options: {} }]
}

let done = false
/** 幂等:全局登记 XSS 过滤 + echarts 空 instance(断 unpkg)。在 main.ts mount 前调用一次。 */
export function setupMarkdown(): void {
  if (done) return
  done = true
  config({
    markdownItPlugins: (plugins) => withXssPlugin(plugins),
    editorExtensions: { echarts: { instance: {} } },
  })
}
