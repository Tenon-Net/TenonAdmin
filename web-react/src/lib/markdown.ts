// Markdown 渲染的全局安全接线(C5 review HIGH-1)。
//
// 背景:md-editor-rt 默认 `html:true`(markdown-it 原样放行内联 HTML),且默认 `sanitize` 是恒等空操作、
// 预设插件链里**不含** XSS 过滤。于是通知正文里的 `<img src=x onerror=…>`、`<a href="javascript:…">`
// 等会原样进 DOM —— 通知作者(普通带权限用户,未必超管)可借此偷读任意查看者的 localStorage(含 JWT)
// 达成账号接管。**这否证了 C5b 提交里「存 Markdown 纯文本 → 无 XSS 面」的说法**(Markdown 天然放行 HTML)。
//
// 修法:注册库自带的 `XSSPlugin`(基于已随 md-editor-rt 打进来的 js-xss,**零新依赖**)。它对 MdEditor 与
// MdPreview 全局生效。Vue 侧 md-editor-v3 有同样的默认缺口 —— 见台账,建议一并修 web/(本轮不动 web/)。
import { config, XSSPlugin } from 'md-editor-rt'
import type { MarkdownItConfigPlugin } from 'md-editor-rt'

/** 把 XSS 过滤插件追加到 markdown-it 预设插件链尾。抽成纯函数便于单测/变异钉死「确实登记了」。 */
export function withXssPlugin(plugins: MarkdownItConfigPlugin[]): MarkdownItConfigPlugin[] {
  return [...plugins, { type: 'xss', plugin: XSSPlugin, options: {} }]
}

let done = false
/**
 * 幂等:全局配置 md-editor-rt。在 main.tsx 渲染前调用一次。做两件事:
 *   1. markdownItPlugins:挂 XSS 过滤(见上)。
 *   2. editorExtensions.echarts:给个**空 no-op instance**。echarts 是唯一没有 `no*` 旗标的懒加载扩展
 *      (katex/mermaid/highlight/prettier 都在组件上用 `no*` 关了),不给 instance 它就挂载即从 unpkg 拉
 *      echarts.min.js —— 破坏气隙(E7)。给空对象:md-editor 视作「已提供」而不再触网;且顺带避开它默认
 *      `parseOption` 用 `new Function` 对(管理员可写的)通知正文求值的隐患 —— 通知本就用不到 echarts 代码块。
 */
export function setupMarkdown(): void {
  if (done) return
  done = true
  config({
    markdownItPlugins: (plugins) => withXssPlugin(plugins),
    editorExtensions: { echarts: { instance: {} } },
  })
}
