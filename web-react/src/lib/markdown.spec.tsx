import { describe, it, expect, afterEach } from 'vitest'
import { render, cleanup, waitFor } from '@testing-library/react'
import { XSSPlugin } from 'md-editor-rt'
import type { MarkdownItConfigPlugin } from 'md-editor-rt'
import { withXssPlugin, setupMarkdown } from './markdown'

afterEach(cleanup)

// ── 纯逻辑接线(变异钉死:确实把库自带 XSSPlugin 登记进了 markdown-it 链)──
describe('withXssPlugin', () => {
  const preset: MarkdownItConfigPlugin[] = [{ type: 'image', plugin: () => {}, options: {} }]

  it('保留原有插件、把 XSS 条目追加到末尾', () => {
    const out = withXssPlugin(preset)
    expect(out).toHaveLength(preset.length + 1)
    expect(out.slice(0, preset.length)).toEqual(preset) // 原有原样保留
    expect(out[0]).toBe(preset[0]) // 顺序不变
  })

  it('追加的条目 type=xss 且挂的正是库自带 XSSPlugin', () => {
    const last = withXssPlugin(preset).at(-1)!
    expect(last.type).toBe('xss')
    expect(last.plugin).toBe(XSSPlugin) // 不是别的空插件
  })
})

// ── 集成:setupMarkdown 全局挂过后,MdPreview 渲染正文里的内联 HTML 必须被过滤(HIGH-1 的真实行为判据)──
describe('setupMarkdown 端到端过滤', () => {
  it('通知正文里的 <img onerror> 渲染后不带 onerror 属性', async () => {
    setupMarkdown()
    // 动态引以确保 setupMarkdown() 的全局 config 先于组件求值生效
    const { MarkdownView } = await import('@/components/MarkdownView')
    const { container } = render(
      <MarkdownView value={`正文\n\n<img src="x" onerror="alert(document.cookie)">`} />,
    )
    const img = await waitFor(() => {
      const el = container.querySelector('img')
      if (!el) throw new Error('img 未渲染')
      return el
    })
    expect(img.getAttribute('onerror')).toBeNull() // 危险事件属性被 XSS 过滤剥掉
  })
})

// ── 自包含守卫(E7):MarkdownView/Editor 关掉 katex/mermaid/highlight/prettier 后,挂载不得注入任何指向
// 外网 CDN 的 <link>/<script>。happy-dom 禁了外部资源**加载**但仍会**注入 DOM 标签**,故可查。摘掉任一 no* 旗标
// → 对应扩展的 unpkg 标签重现 → 本用例转红(见 c7 变异)。──
describe('MarkdownView 气隙自包含', () => {
  const externalResources = () =>
    [...document.querySelectorAll('link[href], script[src]')]
      .map((el) => el.getAttribute('href') || el.getAttribute('src') || '')
      .filter((u) => /^https?:\/\//i.test(u))

  it('挂载后不注入任何指向外网 CDN 的 link/script', async () => {
    const { MarkdownView } = await import('@/components/MarkdownView')
    render(<MarkdownView value={'# 标题\n\n```ts\nconst a = 1\n```'} />)
    await new Promise((r) => setTimeout(r, 0)) // 放过 md-editor 挂载副作用(它注入 CDN 标签的时机)
    expect(externalResources()).toEqual([])
  })
})
