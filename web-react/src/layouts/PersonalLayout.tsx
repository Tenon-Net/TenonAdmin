// 个人中心二级壳:主布局内容区内左侧子导航 + 右侧 Outlet。
// 路径仍为 /personal/*;强制改密时只露出「修改密码」导航项。
import { useMemo } from 'react'
import { Grid, Menu, Select } from 'antd'
import type { MenuProps } from 'antd'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useUserStore } from '@/stores/user'

const ALL_NAV = [
  { key: '/personal/profile', labelKey: 'menu.profile' },
  { key: '/personal/password', labelKey: 'menu.password' },
  { key: '/personal/security', labelKey: 'menu.security' },
  { key: '/personal/sessions', labelKey: 'menu.sessions' },
  { key: '/personal/bindings', labelKey: 'menu.bindings' },
] as const

export function PersonalLayout() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const location = useLocation()
  const mustChange = useUserStore((s) => s.userInfo?.mustChangePassword ?? false)
  const screens = Grid.useBreakpoint()
  const isMobile = screens.md === false

  const navItems = useMemo(() => {
    const list = mustChange ? ALL_NAV.filter((x) => x.key === '/personal/password') : [...ALL_NAV]
    return list.map((x) => ({ key: x.key, label: t(x.labelKey) }))
  }, [mustChange, t])

  const activeKey =
    navItems.find((x) => location.pathname === x.key || location.pathname.startsWith(x.key + '/'))?.key ??
    navItems[0]?.key ??
    '/personal/profile'

  const onMenu: MenuProps['onClick'] = ({ key }) => {
    if (key !== location.pathname) navigate(String(key))
  }

  return (
    <div
      style={{
        display: 'flex',
        gap: 16,
        alignItems: 'stretch',
        minHeight: '100%',
        flexDirection: isMobile ? 'column' : 'row',
      }}
    >
      {!isMobile ? (
        <aside
          style={{
            width: 200,
            flexShrink: 0,
            background: 'var(--ant-color-bg-container, #fff)',
            border: '1px solid var(--ant-color-border, #e5e7eb)',
            borderRadius: 8,
            padding: '8px 0 12px',
          }}
        >
          <Menu
            mode="inline"
            selectedKeys={[activeKey]}
            items={navItems.map((x) => ({ key: x.key, label: x.label }))}
            onClick={onMenu}
            style={{ border: 'none', background: 'transparent' }}
          />
        </aside>
      ) : (
        <div style={{ maxWidth: 360 }}>
          <Select
            value={activeKey}
            options={navItems.map((x) => ({ label: x.label, value: x.key }))}
            onChange={(v) => navigate(v)}
            style={{ width: '100%' }}
          />
        </div>
      )}
      <div style={{ flex: 1, minWidth: 0 }}>
        <Outlet />
      </div>
    </div>
  )
}

export default PersonalLayout
