import { describe, it, expect } from 'vitest'
import { XSSPlugin } from 'md-editor-v3'
import type { MarkdownItConfigPlugin } from 'md-editor-v3'
import { withXssPlugin } from './markdown'

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
})
