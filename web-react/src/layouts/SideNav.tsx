import { useEffect, useMemo, useState } from 'react'
import { Menu, Typography, type MenuProps } from 'antd'
import { useAppStore, isDark } from '@/stores/app'
import { useSiteStore } from '@/stores/site'
import { TenonLogo } from '@/components/TenonLogo'
import { openKeysFor, type MenuItem } from './menuItems'
import './sidenav.css'

/**
 * 侧栏薄壳(对齐 Vue `components/SideNav.vue`)。default 壳的多处复用:完整树 / 二级子树 / 一级图标 rail / 移动抽屉。
 * `collapsed`(用户折叠开关)与 `rail`(一级细栏,恒图标态、更窄、无品牌字)是两条独立的"收窄"来源,任一为真都令 Menu 进 inlineCollapsed。
 * openKeys 本地自管:路由变 → 并入选中项祖先目录(不覆盖用户手动展开的其它目录);图标态不控 openKeys(antd 走悬浮子菜单)。
 */
export function SideNav({
  items,
  value,
  collapsed,
  rail,
  showBrand,
  onSelect,
}: {
  items: MenuItem[]
  value?: string
  collapsed?: boolean
  rail?: boolean
  showBrand?: boolean
  onSelect: (key: string) => void
}) {
  const dark = useAppStore(isDark)
  const site = useSiteStore((s) => s.site)
  const iconOnly = !!(collapsed || rail)

  const routeOpen = useMemo(() => openKeysFor(items, value ?? ''), [items, value])
  const [openKeys, setOpenKeys] = useState<string[]>(routeOpen)
  useEffect(() => {
    setOpenKeys((prev) => Array.from(new Set([...prev, ...routeOpen])))
  }, [routeOpen])

  return (
    <div className={`sidenav${rail ? ' rail' : ''}`}>
      {showBrand ? (
        <div className="sidenav-brand">
          {/* 壳层品牌恒用内置矢量 logo(对齐 Vue SideNav);后台配的 site.logo 只用于登录页。 */}
          <TenonLogo size={28} />
          {!iconOnly ? (
            <Typography.Text strong style={{ fontSize: 16, color: dark ? '#fff' : undefined, whiteSpace: 'nowrap' }}>
              {site.title}
            </Typography.Text>
          ) : null}
        </div>
      ) : null}
      <div className="sidenav-scroll">
        <Menu
          theme={dark ? 'dark' : 'light'}
          mode="inline"
          inlineCollapsed={iconOnly}
          items={items as MenuProps['items']}
          selectedKeys={value ? [value] : []}
          openKeys={iconOnly ? undefined : openKeys}
          onOpenChange={(keys) => setOpenKeys(keys as string[])}
          onClick={({ key }) => onSelect(key)}
        />
      </div>
    </div>
  )
}
