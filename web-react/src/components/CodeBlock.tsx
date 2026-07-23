import { useState, type CSSProperties } from 'react'
import { Button } from 'antd'
import { CheckOutlined, CopyOutlined } from '@ant-design/icons'
import { useTranslation } from 'react-i18next'
import hljs from 'highlight.js/lib/core'
import json from 'highlight.js/lib/languages/json'
// hljs 词法配色走 src/styles/code.css(main.tsx 全局引入),不引 hljs 自带 css。
// 后续配置页要 yaml/sql 再在此 registerLanguage,别提前加(对齐 Vue 版按需注册)。
hljs.registerLanguage('json', json)

const ESCAPE: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }

/** HTML 转义。未注册语言降级纯文本时走 dangerouslySetInnerHTML,**必须**转义防 XSS(安全边界)。 */
export function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => ESCAPE[c])
}

/** 高亮:已注册语言走 hljs(其输出本身已转义);未注册语言降级为**转义后**的纯文本。 */
export function highlightCode(code: string, language: string): string {
  if (hljs.getLanguage(language)) return hljs.highlight(code, { language }).value
  return escapeHtml(code)
}

export interface CodeBlockProps {
  /** 已格式化的文本(格式化由调用方负责,如 JSON.stringify(v, null, 2))。 */
  code: string
  /** hljs 语言名;未注册的语言降级纯文本。 */
  language?: string
  /** 长行自动换行(抽屉等窄容器必开,否则撑宽)。 */
  wordWrap?: boolean
  /** 右上角悬浮复制按钮。 */
  copyable?: boolean
}

/** 只读代码/JSON 展示 + 复制。对应 Vue 侧 `CodeBlock/index.vue`(那边 NCode + hljs,这里 hljs 直出)。 */
export function CodeBlock({ code, language = 'json', wordWrap = true, copyable = true }: CodeBlockProps) {
  const { t } = useTranslation()
  const [copied, setCopied] = useState(false)
  async function copy() {
    try {
      await navigator.clipboard.writeText(code)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      /* 剪贴板不可用(非 https / 无权限)静默 */
    }
  }
  const preStyle: CSSProperties = {
    margin: 0,
    whiteSpace: wordWrap ? 'pre-wrap' : 'pre',
    wordBreak: wordWrap ? 'break-all' : 'normal',
  }
  return (
    <div style={{ position: 'relative', fontSize: 12, lineHeight: 1.5 }}>
      <pre style={preStyle}>
        <code className="hljs" dangerouslySetInnerHTML={{ __html: highlightCode(code, language) }} />
      </pre>
      {copyable ? (
        <Button
          type="text"
          size="small"
          style={{ position: 'absolute', top: 0, right: 0 }}
          aria-label={copied ? t('common.copied') : t('common.copy')}
          title={copied ? t('common.copied') : t('common.copy')}
          icon={copied ? <CheckOutlined /> : <CopyOutlined />}
          onClick={() => void copy()}
        />
      ) : null}
    </div>
  )
}
