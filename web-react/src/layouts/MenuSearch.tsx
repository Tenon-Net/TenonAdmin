import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Modal, Input, Empty, type InputRef } from 'antd'
import { SearchOutlined } from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { flatLeaves, openIfExternal, type MenuItem, type MenuLeaf } from './menuItems'
import './menusearch.css'

// 关键词命中切成 前/命中/后 三段,用 <mark> 高亮。React 天然转义文本,免 Vue 版的 v-html + 手写 esc(即那处 XSS 面)。
function highlight(text: string, kw: string): ReactNode {
  if (!kw) return text
  const i = text.toLowerCase().indexOf(kw.toLowerCase())
  if (i === -1) return text
  return (
    <>
      {text.slice(0, i)}
      <mark>{text.slice(i, i + kw.length)}</mark>
      {text.slice(i + kw.length)}
    </>
  )
}

interface Props {
  open: boolean
  onClose: () => void
  items: MenuItem[]
}

/**
 * 命令面板:全局菜单搜索 + 键盘跳转(Ctrl/Cmd+K 由 LayoutShell 开合)。移植 Vue `components/MenuSearch.vue`。
 * 叶子取自 `flatLeaves(items)`(已翻译 label + 面包屑);内部路径 navigate、外链 window.open。
 */
export function MenuSearch({ open, onClose, items }: Props) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const inputRef = useRef<InputRef>(null)
  const [q, setQ] = useState('')
  const [cursor, setCursor] = useState(0)

  const leaves = useMemo(() => flatLeaves(items), [items])
  const results = useMemo<MenuLeaf[]>(() => {
    const kw = q.trim().toLowerCase()
    if (!kw) return leaves
    return leaves.filter((l) => l.label.toLowerCase().includes(kw) || l.key.toLowerCase().includes(kw))
  }, [leaves, q])

  // 结果集变化(改关键词)→ 高亮光标回首项。
  useEffect(() => setCursor(0), [results])

  // 每次打开重置为干净态(对齐 Vue watch(show):打开即清空关键词+光标)。依赖 open prop 而非 Modal
  // 过渡回调,故快速开关也稳、且可确定性单测(afterOpenChange 在测试环境无真实过渡不触发)。
  useEffect(() => {
    if (open) {
      setQ('')
      setCursor(0)
    }
  }, [open])

  function go(item?: MenuLeaf) {
    if (item && !openIfExternal(item.key)) navigate(item.key)
    onClose()
  }

  function onKeyDown(e: KeyboardEvent) {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setCursor((c) => Math.min(c + 1, results.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setCursor((c) => Math.max(c - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      go(results[cursor])
    }
  }

  const kw = q.trim()

  return (
    <Modal
      open={open}
      onCancel={onClose}
      footer={null}
      closable={false}
      width={560}
      styles={{ container: { padding: 0 } }}
      // 过渡结束后聚焦输入框(此时它已可见)。清空放在依赖 open 的 effect 里,不走此回调。
      afterOpenChange={(o) => {
        if (o) inputRef.current?.focus()
      }}
    >
      {/* onKeyDown 挂在容器:输入框的方向键/回车冒泡上来统一处理(Esc 由 Modal keyboard 关闭)。 */}
      <div className="palette" onKeyDown={onKeyDown}>
        <Input
          ref={inputRef}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder={t('settings.searchPlaceholder')}
          prefix={<SearchOutlined />}
          variant="borderless"
          size="large"
          className="palette-input"
        />
        {results.length ? (
          <div className="palette-list">
            {results.map((item, i) => (
              <div
                key={item.key}
                className={`palette-item${i === cursor ? ' active' : ''}`}
                onClick={() => go(item)}
                onMouseEnter={() => setCursor(i)}
              >
                <span className="palette-title">{highlight(item.label, kw)}</span>
                <span className="palette-path">{item.breadcrumb.join(' / ')}</span>
              </div>
            ))}
          </div>
        ) : (
          <Empty description={t('settings.searchEmpty')} className="palette-empty" />
        )}
        <div className="palette-hint">{t('settings.searchHint')}</div>
      </div>
    </Modal>
  )
}
