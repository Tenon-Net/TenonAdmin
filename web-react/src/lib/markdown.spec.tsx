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
