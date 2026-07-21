import { describe, it, expect, afterEach, beforeEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import '@/locales'
import { escapeHtml, highlightCode, CodeBlock } from './CodeBlock'

afterEach(cleanup)

// ── 纯逻辑(变异钉死)──
describe('escapeHtml', () => {
  it('转义 & < > " \'', () => {
    expect(escapeHtml(`&<>"'`)).toBe('&amp;&lt;&gt;&quot;&#39;')
  })
})

describe('highlightCode', () => {
  it('已注册语言(json)走高亮,产出 hljs 词法 span', () => {
    // if(false) 分支会退成 escapeHtml,产不出 hljs- 类 → 断言即红。
    expect(highlightCode('{"a":1}', 'json')).toContain('hljs-')
  })
  it('未注册语言降级为转义纯文本(安全:防 dangerouslySetInnerHTML XSS)', () => {
    // 钉两件事:① if(true) 会让 hljs.highlight 收未知语言抛错 → 用例炸红;
    // ② 把 escapeHtml(code) 改成 code(丢转义)→ 返回 '<b>' ≠ 期望 → 红。
    expect(highlightCode('<b>', 'nosuchlang')).toBe('&lt;b&gt;')
  })
})

// ── 组件接线 ──
describe('CodeBlock', () => {
  beforeEach(() => {
    // happy-dom 的 navigator.clipboard 是只读 getter,Object.assign 设不了,用 defineProperty(configurable) 覆写。
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
    })
  })

  it('渲染代码文本', () => {
    render(<CodeBlock code={'{"k":1}'} />)
    // hljs 把内容切进多个 span,用容器 textContent 断整体。
    expect(screen.getByText((_, el) => el?.tagName === 'CODE' && el.textContent === '{"k":1}')).toBeTruthy()
  })

  it('copyable(默认)显示复制按钮,点击写入剪贴板', async () => {
    render(<CodeBlock code="hello" />)
    fireEvent.click(screen.getByRole('button', { name: '复制' }))
    await waitFor(() => expect(navigator.clipboard.writeText).toHaveBeenCalledWith('hello'))
  })

  it('copyable=false 不显示复制按钮', () => {
    render(<CodeBlock code="hello" copyable={false} />)
    expect(screen.queryByRole('button', { name: '复制' })).toBeNull()
  })
})
