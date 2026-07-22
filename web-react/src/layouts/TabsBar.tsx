import { useEffect, useMemo, useRef } from 'react'
import { Dropdown, type MenuProps } from 'antd'
import { useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { useTabsStore, type TabItem } from '@/stores/tabs'
import './tabsbar.css'

/**
 * 多标签栏(替代 Vue `TabsBar.vue`)。横滚 chip 条 + 右键菜单(antd Dropdown `trigger=['contextMenu']`)+ 中键关闭。
 * store 返回导航目标,这里(有 useNavigate)负责导航。affix(应用首页)/pinned 不显示关闭 X。
 */
export function TabsBar() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const tabs = useTabsStore((s) => s.tabs)
  const stripRef = useRef<HTMLDivElement>(null)

  const nav = (next: string | null) => {
    if (next) navigate(next)
  }
  const tabTitle = (tab: TabItem) => (tab.title.includes('.') ? t(tab.title) : tab.title)
  const goTab = (tab: TabItem) => {
    if (tab.path !== pathname) navigate(tab.fullPath)
  }
  const closeTab = (path: string) => nav(useTabsStore.getState().removeTab(path, pathname))

  const ctxItems = (tab: TabItem): MenuProps['items'] => [
    { key: 'refresh', label: t('tabs.refresh'), icon: <AppIcon icon="ph:arrow-clockwise" size={15} /> },
    // 应用首页(affix)恒固定,不提供固定/取消项。
    { key: 'pin', label: tab.pinned ? t('tabs.unpin') : t('tabs.pin'), icon: <AppIcon icon="ph:push-pin" size={15} />, disabled: tab.affix },
    { key: 'close', label: t('tabs.close'), icon: <AppIcon icon="ph:x" size={15} />, disabled: tab.affix || tab.pinned },
    { key: 'others', label: t('tabs.closeOthers'), icon: <AppIcon icon="ph:arrows-in-line-horizontal" size={15} /> },
    { key: 'left', label: t('tabs.closeLeft'), icon: <AppIcon icon="ph:arrow-line-left" size={15} /> },
    { key: 'right', label: t('tabs.closeRight'), icon: <AppIcon icon="ph:arrow-line-right" size={15} /> },
    { key: 'all', label: t('tabs.closeAll'), icon: <AppIcon icon="ph:list-dashes" size={15} /> },
  ]

  const onCtx = (tab: TabItem, key: string) => {
    const s = useTabsStore.getState()
    if (key === 'refresh') s.refreshTab(tab.path)
    else if (key === 'pin') s.togglePin(tab.path)
    else if (key === 'close') nav(s.removeTab(tab.path, pathname))
    else if (key === 'others') nav(s.closeOthers(tab.path, pathname))
    else if (key === 'left') nav(s.closeLeft(tab.path, pathname))
    else if (key === 'right') nav(s.closeRight(tab.path, pathname))
    else if (key === 'all') nav(s.closeAll(pathname))
  }

  // 激活标签滚入视野
  const activeKey = useMemo(() => tabs.find((tt) => tt.path === pathname)?.path, [tabs, pathname])
  useEffect(() => {
    stripRef.current?.querySelector('.tab-chip.active')?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
  }, [activeKey])

  return (
    <div className="tabsbar">
      <div ref={stripRef} className="tab-strip">
        {tabs.map((tab) => (
          <Dropdown key={tab.path} menu={{ items: ctxItems(tab), onClick: ({ key }) => onCtx(tab, key) }} trigger={['contextMenu']}>
            {/* chip 是可点非按钮元素:补 role/tabindex/键盘激活,键盘用户也能切换(右键菜单是鼠标专属)。 */}
            <div
              className={`tab-chip${tab.path === pathname ? ' active' : ''}`}
              role="button"
              tabIndex={0}
              onClick={() => goTab(tab)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault()
                  goTab(tab)
                }
              }}
              onAuxClick={(e) => {
                if (e.button === 1) {
                  e.preventDefault()
                  closeTab(tab.path)
                }
              }}
            >
              {tab.icon ? <AppIcon icon={tab.icon} size={15} /> : null}
              <span className="tab-label">{tabTitle(tab)}</span>
              {tab.pinned ? (
                <AppIcon icon="ph:push-pin-fill" size={12} />
              ) : !tab.affix ? (
                <span
                  className="tab-close"
                  role="button"
                  tabIndex={0}
                  aria-label={t('tabs.close')}
                  onClick={(e) => {
                    e.stopPropagation()
                    closeTab(tab.path)
                  }}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.stopPropagation()
                      e.preventDefault()
                      closeTab(tab.path)
                    }
                  }}
                >
                  <AppIcon icon="ph:x" size={13} />
                </span>
              ) : null}
            </div>
          </Dropdown>
        ))}
      </div>
    </div>
  )
}
