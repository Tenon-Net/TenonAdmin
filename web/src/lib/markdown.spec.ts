import { afterEach, describe, it, expect } from 'vitest'
import { createApp, h, nextTick, type App } from 'vue'
import { MdPreview, XSSPlugin } from 'md-editor-v3'
import type { MarkdownItConfigPlugin } from 'md-editor-v3'
import { setupMarkdown, withXssPlugin } from './markdown'

let app: App<Element> | undefined
afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

// 纯逻辑接线(变异钉死:确实把库自带 XSSPlugin 登记进了 markdown-it 链)。渲染级/气隙守卫在 web-react 侧,
// 那边建了 happy-dom 禁外部加载的测试基建;web/ 这里只钉纯逻辑,不引入渲染(避免真拉 unpkg)。
describe('withXssPlugin', () => {
  const preset: MarkdownItConfigPlugin[] = [{ type: 'image', plugin: () => {}, options: {} }]

  it('保留原有插件、把 XSS 条目追加到末尾', () => {
    const out = withXssPlugin(preset)
    expect(out).toHaveLength(preset.length + 1)
    expect(out[0]).toBe(preset[0])
  })

  it('追加的条目 type=xss 且挂的正是库自带 XSSPlugin', () => {
    const last = withXssPlugin(preset).at(-1)!
    expect(last.type).toBe('xss')
    expect(last.plugin).toBe(XSSPlugin)
  })

  it('实际渲染时转义围栏语言属性，阻止 GHSA-3rm2-h79c-8qw6', async () => {
    setupMarkdown()
    const host = document.createElement('div')
    document.body.append(host)
    const payload = '```x"><details/open/ontoggle=alert(document.domain)>\nSAFE\n```'
    app = createApp({
      render: () => h(MdPreview, { modelValue: payload, noHighlight: true, noKatex: true, noMermaid: true }),
    })
    app.mount(host)
    await nextTick()

    expect(host.querySelectorAll('details')).toHaveLength(1)
    expect(host.querySelector('[ontoggle]')).toBeNull()
    expect(host.textContent).toContain('SAFE')
  })
})
