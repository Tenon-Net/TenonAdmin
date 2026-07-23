import { useEffect, useState } from 'react'
import { Empty, Input, Modal, Spin, Tabs } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { COLLECTIONS, LOCAL_PREFIX, getLocalIconNames, loadIconNames } from '@/lib/icons'
import './IconPicker.css'

// 单页可见上限;超出提示继续输入缩小范围(对齐 Vue 版 cap=300,避免一次渲染上千 <Icon>)。
const CAP = 300

/** 图标名本地过滤:trim + 小写 includes;空 keyword 返回原列表。 */
export function filterNames(names: string[], keyword: string): string[] {
  const k = keyword.trim().toLowerCase()
  return k ? names.filter((n) => n.toLowerCase().includes(k)) : names
}

export interface IconPickerProps {
  /** 值契约:`prefix:name`(如 `ph:folder`)或 `local:name`;空串 = 未选。受控。
   *  value/onChange 声明为可选,以便直接放进 antd `Form.Item`(由 Form 运行时注入)。 */
  value?: string
  onChange?: (v: string) => void
  placeholder?: string
  clearable?: boolean
}

/**
 * 轻量内联图标选择器(离线):触发器 + 弹窗(搜索 + 集合 Tab + 图标网格)。**不做在线搜索**(本模板气隙自包含,
 * 对齐 C3 的 /offline 决策)。渲染器基建(4 离线集 + 本地 svg)在 `lib/icons`;此处只做选择 UI。
 * 对应 Vue 侧 `IconPicker`(那边是 tenon-naive-iconify-picker 包 + 在线 tab;React 版内联且去在线)。
 */
export function IconPicker({ value, onChange, placeholder, clearable = true }: IconPickerProps) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState<string>(COLLECTIONS[0].prefix)
  const [keyword, setKeyword] = useState('')
  const [names, setNames] = useState<string[]>([])
  const [loading, setLoading] = useState(false)

  // 打开或切 Tab 时加载该集图标名(local 同步取;内置集懒加载),并清空搜索。
  useEffect(() => {
    if (!open) return
    setKeyword('')
    if (active === LOCAL_PREFIX) {
      setNames(getLocalIconNames())
      setLoading(false)
      return
    }
    setLoading(true)
    let alive = true
    loadIconNames(active)
      .then((list) => {
        if (alive) setNames(list)
      })
      .catch(() => {
        // chunk 加载失败(理论上离线内置集不该)→ 清空该 Tab,不留上一个 Tab 的陈旧列表 + 吞未处理拒绝。
        if (alive) setNames([])
      })
      .finally(() => {
        if (alive) setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [active, open])

  const filtered = filterNames(names, keyword)
  const visible = filtered.slice(0, CAP)
  const overflow = filtered.length - visible.length
  const iconId = (name: string) => `${active}:${name}`

  function pick(name: string) {
    onChange?.(iconId(name))
    setOpen(false)
  }

  const tabItems = [...COLLECTIONS.map((c) => ({ key: c.prefix, label: c.name })), { key: LOCAL_PREFIX, label: t('iconPicker.local') }]

  return (
    <>
      <div
        className={`icon-picker-trigger${value ? '' : ' empty'}`}
        role="button"
        tabIndex={0}
        onClick={() => setOpen(true)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault()
            setOpen(true)
          }
        }}
      >
        {value ? <AppIcon icon={value} size={18} /> : null}
        <span className="icon-picker-val">{value || placeholder || t('iconPicker.placeholder')}</span>
        {clearable && value ? (
          <span
            className="icon-picker-clear"
            role="button"
            tabIndex={0}
            aria-label={t('common.clear')}
            onClick={(e) => {
              e.stopPropagation() // 触发器本身可点(开弹窗),清空不该顺带开弹窗
              onChange?.('')
            }}
            onKeyDown={(e) => {
              // 键盘可达:清空自己接 Enter/Space,并 stop 冒泡免触发外层触发器的开弹窗。
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault()
                e.stopPropagation()
                onChange?.('')
              }
            }}
          >
            <AppIcon icon="ph:x" size={13} />
          </span>
        ) : null}
      </div>

      <Modal open={open} onCancel={() => setOpen(false)} footer={null} title={t('iconPicker.title')} width={600} destroyOnHidden>
        <Input
          value={keyword}
          onChange={(e) => setKeyword(e.target.value)}
          allowClear
          placeholder={t('iconPicker.search')}
          prefix={<AppIcon icon="ph:magnifying-glass" size={16} />}
          style={{ marginBottom: 8 }}
        />
        <Tabs activeKey={active} onChange={setActive} type="line" size="small" items={tabItems} />
        <div className="icon-picker-scroll">
          {loading ? (
            <div className="icon-picker-state">
              <Spin />
            </div>
          ) : visible.length === 0 ? (
            <Empty className="icon-picker-state" description={t('iconPicker.empty')} />
          ) : (
            <>
              <div className="icon-picker-grid">
                {visible.map((name) => (
                  <button
                    key={name}
                    type="button"
                    title={iconId(name)}
                    className={`icon-picker-cell${value === iconId(name) ? ' sel' : ''}`}
                    onClick={() => pick(name)}
                  >
                    <AppIcon icon={iconId(name)} size={22} />
                    <span className="icon-picker-name">{name}</span>
                  </button>
                ))}
              </div>
              {overflow > 0 ? <p className="icon-picker-more">{t('iconPicker.more', { n: overflow })}</p> : null}
            </>
          )}
        </div>
      </Modal>
    </>
  )
}
