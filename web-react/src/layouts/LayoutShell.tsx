import { Button, Layout, Menu, Space, Tooltip, Typography, type MenuProps } from 'antd'
import { AppstoreOutlined, LogoutOutlined, MenuFoldOutlined, MenuUnfoldOutlined, MoonOutlined, SunOutlined } from '@ant-design/icons'
import { Outlet, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAppStore, isDark } from '@/stores/app'
import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'
import { useSiteStore } from '@/stores/site'
import { authApi } from '@/api'
import { useLayoutMenu } from './useLayoutMenu'

const { Sider, Header, Content } = Layout

/**
 * 布局壳:antd 原生 `Layout` 自建(**不用 ProLayout**)。Sider 236/76 折叠 + Header 62 + Content。
 * 只做 vertical —— 多布局模式(mix/horizontal)、tabs(D1)、设置抽屉、水印都是后续批次。
 * 菜单派生与选中/展开态在 `useLayoutMenu`;这里只搭骨架。菜单页经 `<Outlet/>` 渲染在内容区。
 */
export function LayoutShell() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const collapsed = useAppStore((s) => s.collapsed)
  const toggleCollapsed = useAppStore((s) => s.toggleCollapsed)
  const toggleDark = useAppStore((s) => s.toggleDark)
  const dark = useAppStore(isDark)
  const site = useSiteStore((s) => s.site)
  const { items, selectedKeys, openKeys, setOpenKeys, onClick } = useLayoutMenu()

  async function logout() {
    try {
      await authApi.logout()
    } catch {
      // 后端登出失败不阻断前端清理。
    }
    useAuthStore.getState().reset()
    useUserStore.getState().clear()
    navigate('/login', { replace: true })
  }

  const menuTheme = dark ? 'dark' : 'light'

  return (
    <Layout style={{ height: '100vh' }}>
      <Sider theme={menuTheme} collapsible collapsed={collapsed} trigger={null} width={236} collapsedWidth={76} style={{ overflow: 'auto' }}>
        {/* 品牌:折叠时只留 logo/首字,展开显全名。侧栏顶部是本模板品牌唯一出处(vertical)。 */}
        <div style={{ height: 62, display: 'flex', alignItems: 'center', gap: 10, padding: '0 20px', overflow: 'hidden', whiteSpace: 'nowrap' }}>
          {site.logo ? <img src={site.logo} alt="" style={{ height: 28 }} /> : <AppstoreOutlined style={{ fontSize: 22 }} />}
          {!collapsed ? (
            <Typography.Text strong style={{ fontSize: 16, color: dark ? '#fff' : undefined }}>
              {site.title}
            </Typography.Text>
          ) : null}
        </div>
        <Menu
          theme={menuTheme}
          mode="inline"
          // 我们的 MenuItem 结构与 antd 的 ItemType 一致(key/label/icon/children),但 antd 的联合类型
          // 带 null 等成员,递归结构 TS 不直接认;精确到 MenuProps['items'] 的结构转换,非 any。
          items={items as MenuProps['items']}
          selectedKeys={selectedKeys}
          openKeys={openKeys}
          onOpenChange={(keys) => setOpenKeys(keys as string[])}
          onClick={({ key }) => onClick(key)}
        />
      </Sider>

      <Layout>
        <Header
          style={{
            height: 62,
            display: 'flex',
            alignItems: 'center',
            padding: '0 16px',
            background: 'var(--color-header-bg, rgba(255,255,255,0.72))',
            backdropFilter: 'blur(12px)',
            borderBottom: '1px solid var(--color-border, rgba(0,0,0,0.06))',
          }}
        >
          <Button
            type="text"
            aria-label={collapsed ? t('app.openMenu') : t('app.collapse')}
            icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
            onClick={toggleCollapsed}
          />
          <div style={{ flex: 1 }} />
          <Space size="small">
            <Tooltip title={t('app.theme')}>
              <Button type="text" aria-label={t('app.theme')} icon={dark ? <SunOutlined /> : <MoonOutlined />} onClick={toggleDark} />
            </Tooltip>
            <Tooltip title={t('app.logout')}>
              <Button type="text" aria-label={t('app.logout')} icon={<LogoutOutlined />} onClick={() => void logout()} />
            </Tooltip>
          </Space>
        </Header>

        <Content style={{ overflow: 'auto', padding: 'var(--pad-page)' }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  )
}
