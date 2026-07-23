import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { Avatar, Breadcrumb, Button, Drawer, Dropdown, Grid, Menu, Space, Tooltip, Typography, Watermark, type MenuProps } from 'antd'
import {
  AppstoreOutlined, DesktopOutlined, FullscreenExitOutlined, FullscreenOutlined, KeyOutlined, LinkOutlined, LogoutOutlined,
  MenuFoldOutlined, MenuOutlined, MenuUnfoldOutlined, MoonOutlined, SearchOutlined, SettingOutlined, SunOutlined, TranslationOutlined, UserOutlined,
} from '@ant-design/icons'
import { useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAppStore, isDark, type LayoutMode, type Locale } from '@/stores/app'
import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'
import { useSiteStore } from '@/stores/site'
import { useTabsStore } from '@/stores/tabs'
import { authApi } from '@/api'
import { TenonLogo } from '@/components/TenonLogo'
import { useLayoutMenu } from './useLayoutMenu'
import { NoticeBell } from './NoticeBell'
import { SideNav } from './SideNav'
import { SettingsDrawer } from './SettingsDrawer'
import { MenuSearch } from './MenuSearch'
import { TabsBar } from './TabsBar'
import { KeepAliveOutlet } from './KeepAliveOutlet'
import { useTabSync } from './useTabSync'
import { useRealtime } from '@/composables/useRealtime'
import { breadcrumbFor, type MenuItem } from './menuItems'
import './shell.css'

// 模式集合(对齐 Vue layouts/default.vue)。窄屏统一走 mobile(抽屉侧栏),不吃 layoutMode。
const WITH_SIDER = new Set<LayoutMode>(['vertical', 'vertical-mix', 'vertical-hybrid-header-first', 'top-hybrid-sidebar-first', 'top-hybrid-header-first'])
const HEADER_BRAND = new Set<LayoutMode>(['horizontal', 'vertical-hybrid-header-first', 'top-hybrid-sidebar-first', 'top-hybrid-header-first'])
const COLLAPSIBLE = new Set<LayoutMode>(['vertical', 'vertical-mix', 'vertical-hybrid-header-first', 'top-hybrid-header-first'])

type TopMenu = 'full' | 'l1' | 'l2' | null
// 顶栏菜单:horizontal=完整树;两个 header-first=一级;侧边优先=二级(选中一级的子菜单)。
function topMenuFor(mode: LayoutMode): TopMenu {
  if (mode === 'horizontal') return 'full'
  if (mode === 'vertical-hybrid-header-first' || mode === 'top-hybrid-header-first') return 'l1'
  if (mode === 'top-hybrid-sidebar-first') return 'l2'
  return null
}

/**
 * 布局壳:CSS-grid 自建(**不用 ProLayout**),按 `app.layoutMode` 在 6 种模式间重排 aside/header/content(见 shell.css)。
 * 侧栏/顶栏菜单的一/二级派生在 `useLayoutMenu`,展示层在 `SideNav`。窄屏(<md)走 mobile:单列 + 抽屉侧栏。
 * 水印(app.watermark,原生 antd Watermark)与毛玻璃(app.fixedHeader)在此接线;tabs(D1)在内容区上方。
 */
export function LayoutShell() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const screens = Grid.useBreakpoint()
  // 仅"明确测到 < md"才 mobile;首帧 breakpoint 尚未测出(md===undefined)按桌面渲染,避免 mobile 闪一帧。
  const isMobile = screens.md === false
  const layoutMode = useAppStore((s) => s.layoutMode)
  const collapsed = useAppStore((s) => s.collapsed)
  const toggleCollapsed = useAppStore((s) => s.toggleCollapsed)
  const toggleDark = useAppStore((s) => s.toggleDark)
  const setLocale = useAppStore((s) => s.setLocale)
  const dark = useAppStore(isDark)
  const showTabs = useAppStore((s) => s.showTabs)
  const showBreadcrumb = useAppStore((s) => s.showBreadcrumb)
  const fixedHeader = useAppStore((s) => s.fixedHeader)
  const watermark = useAppStore((s) => s.watermark)
  const watermarkText = useAppStore((s) => s.watermarkText)
  const site = useSiteStore((s) => s.site)
  const userInfo = useUserStore((s) => s.userInfo)
  const modules = useAuthStore((s) => s.modules)
  const currentTab = useTabsStore((s) => s.tabs.find((tt) => tt.path === pathname))
  const { items, l1Items, l2Items, selectedL1, activeKey, onSelect, onSelectL1 } = useLayoutMenu()
  useTabSync() // 路由 → 标签同步(当前页进标签栏 + KeepAlive)
  useRealtime() // 实时通知(SignalR):挂载即连、卸载即断;后端未开启实时时静默退回下方轮询兜底

  const [mobileOpen, setMobileOpen] = useState(false)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false)

  // 全屏(对齐 Vue AppHeader 的 toggleFullscreen):监听 fullscreenchange,Esc/F11 退出时图标同步。
  const [fullscreen, setFullscreen] = useState(false)
  useEffect(() => {
    const sync = () => setFullscreen(!!document.fullscreenElement)
    document.addEventListener('fullscreenchange', sync)
    return () => document.removeEventListener('fullscreenchange', sync)
  }, [])
  const toggleFullscreen = () => {
    if (document.fullscreenElement) void document.exitFullscreen()
    else void document.documentElement.requestFullscreen()
  }

  // 全局 Ctrl/Cmd+K:开合命令面板(菜单搜索)。
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
        e.preventDefault()
        setSearchOpen((o) => !o)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  // 用户下拉:个人中心四页 + 登出。key=personal 段名,logout 单独处理。
  const userMenu: MenuProps['items'] = [
    { key: 'profile', label: t('menu.profile'), icon: <UserOutlined /> },
    { key: 'password', label: t('menu.password'), icon: <KeyOutlined /> },
    { key: 'sessions', label: t('menu.sessions'), icon: <DesktopOutlined /> },
    { key: 'bindings', label: t('menu.bindings'), icon: <LinkOutlined /> },
    { type: 'divider' },
    { key: 'logout', label: t('app.logout'), icon: <LogoutOutlined />, danger: true },
  ]
  const onUserMenu: MenuProps['onClick'] = ({ key }) => (key === 'logout' ? void logout() : navigate(`/personal/${key}`))
  const avatarChar = (userInfo?.name ?? '').trim().charAt(0).toUpperCase() || '?'

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

  // 模式派生(!isMobile 时 mode===layoutMode;isMobile 走单列 + 抽屉)。
  const mode = isMobile ? 'mobile' : layoutMode
  const showSider = !isMobile && WITH_SIDER.has(layoutMode)
  const sideShowBrand = !isMobile && (layoutMode === 'vertical' || layoutMode === 'vertical-mix')
  const headerShowBrand = isMobile || (!isMobile && HEADER_BRAND.has(layoutMode))
  const showCollapse = !isMobile && COLLAPSIBLE.has(layoutMode)
  const topMenu = isMobile ? null : topMenuFor(layoutMode)
  // 非特殊侧栏内容:vertical=完整树;两个 header-first hybrid=二级子菜单树。
  const asideItems = layoutMode === 'vertical' ? items : l2Items
  const asideStyle: CSSProperties =
    layoutMode === 'vertical-mix'
      ? { width: collapsed ? 'var(--rail-w)' : 'calc(var(--rail-w) + var(--sidebar-w))' }
      : layoutMode === 'top-hybrid-sidebar-first'
        ? { width: 'var(--rail-w)' }
        : { width: collapsed ? 'var(--sidebar-w-collapsed)' : 'var(--sidebar-w)' }

  const topMenuItems: MenuItem[] = topMenu === 'full' ? items : topMenu === 'l1' ? l1Items : l2Items
  const pageTitle = currentTab ? (currentTab.title.includes('.') ? t(currentTab.title) : currentTab.title) : ''
  const crumbs = useMemo(() => breadcrumbFor(items, pathname), [items, pathname])
  const wmContent = [userInfo?.name, watermarkText].filter(Boolean).join(' · ') || site.title
  const wmColor = dark ? 'rgba(255,255,255,0.12)' : 'rgba(0,0,0,0.06)'

  return (
    <div className={`shell mode-${mode}`}>
      {showSider ? (
        <aside className={`shell-aside${layoutMode === 'vertical-mix' ? ' shell-aside--mix' : ''}`} style={asideStyle}>
          {layoutMode === 'vertical-mix' ? (
            <>
              {/* 左侧混合:一级 icon rail(带品牌) + 二级列(折叠时隐藏,只留 rail) */}
              <SideNav rail showBrand items={l1Items} value={selectedL1} onSelect={onSelectL1} />
              {!collapsed ? <SideNav items={l2Items} value={activeKey} onSelect={onSelect} /> : null}
            </>
          ) : layoutMode === 'top-hybrid-sidebar-first' ? (
            // 顶部混合-侧边优先:侧栏只放一级 icon rail(二级在顶栏),固定宽、不折叠、不显品牌(品牌在顶栏)
            <SideNav rail items={l1Items} value={selectedL1} onSelect={onSelectL1} />
          ) : (
            <SideNav items={asideItems} value={activeKey} collapsed={collapsed} showBrand={sideShowBrand} onSelect={onSelect} />
          )}
        </aside>
      ) : null}

      <header className={`shell-header${fixedHeader ? '' : ' shell-header--static'}`}>
        {isMobile ? (
          <Button className="shell-hamburger" type="text" aria-label={t('app.openMenu')} icon={<MenuOutlined />} onClick={() => setMobileOpen(true)} />
        ) : null}
        <div className="shell-headerbar">
          <div className="shell-header-left">
            {showCollapse ? (
              <Button
                type="text"
                aria-label={collapsed ? t('app.openMenu') : t('app.collapse')}
                icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
                onClick={toggleCollapsed}
              />
            ) : null}
            {/* 品牌与「面包屑/标题」是两段独立块(镜像 Vue AppHeader):品牌模式下二者并存,
                否则只出面包屑/标题。别合成互斥三元,否则 header-brand 模式下 showBreadcrumb 成死控件。 */}
            {headerShowBrand ? (
              <div className="shell-brand">
                {/* 壳层品牌恒用内置矢量 logo(对齐 Vue AppHeader);后台配的 site.logo 只用于登录页。 */}
                <TenonLogo size={26} />
                <Typography.Text strong style={{ color: dark ? '#fff' : undefined, whiteSpace: 'nowrap' }}>
                  {site.title}
                </Typography.Text>
              </div>
            ) : null}
            {showBreadcrumb && crumbs.length ? (
              <Breadcrumb items={crumbs.map((c, i) => ({ key: i, title: c }))} />
            ) : (
              <span className="shell-title">{pageTitle}</span>
            )}
          </div>

          {topMenu ? (
            <div className="shell-header-center">
              <Menu
                theme={dark ? 'dark' : 'light'}
                mode="horizontal"
                items={topMenuItems as MenuProps['items']}
                selectedKeys={topMenu === 'l1' ? (selectedL1 ? [selectedL1] : []) : [activeKey]}
                onClick={({ key }) => (topMenu === 'l1' ? onSelectL1(key) : onSelect(key))}
              />
            </div>
          ) : null}

          {/* 按钮序对齐 Vue AppHeader:搜索 → 全屏 → 主题 → 语言 → 设置 → 切应用 → 通知 → 用户 */}
          <div className="shell-header-right">
            <Tooltip title={`${t('app.search')} (Ctrl+K)`}>
              <Button type="text" aria-label={t('app.search')} icon={<SearchOutlined />} onClick={() => setSearchOpen(true)} />
            </Tooltip>
            <Tooltip title={fullscreen ? t('app.exitFullscreen') : t('app.fullscreen')}>
              <Button
                type="text"
                aria-label={fullscreen ? t('app.exitFullscreen') : t('app.fullscreen')}
                icon={fullscreen ? <FullscreenExitOutlined /> : <FullscreenOutlined />}
                onClick={toggleFullscreen}
              />
            </Tooltip>
            <Tooltip title={t('app.theme')}>
              <Button type="text" aria-label={t('app.theme')} icon={dark ? <SunOutlined /> : <MoonOutlined />} onClick={toggleDark} />
            </Tooltip>
            {/* 语言下拉(对齐 Vue 的 localeOptions):店招用固定双语文案,不走 t()(切错语言时也要认得出) */}
            <Dropdown
              menu={{
                items: [
                  { key: 'zh-CN', label: '简体中文' },
                  { key: 'en-US', label: 'English' },
                ],
                onClick: ({ key }) => setLocale(key as Locale),
              }}
              trigger={['click']}
            >
              <Button type="text" aria-label={t('app.language')} icon={<TranslationOutlined />} />
            </Dropdown>
            <Tooltip title={t('app.settings')}>
              <Button type="text" aria-label={t('app.settings')} icon={<SettingOutlined />} onClick={() => setSettingsOpen(true)} />
            </Tooltip>
            {modules.length > 1 ? (
              <Tooltip title={t('app.switchModule')}>
                <Button type="text" aria-label={t('app.switchModule')} icon={<AppstoreOutlined />} onClick={() => navigate('/module')} />
              </Tooltip>
            ) : null}
            {/* 通知铃铛:富面板 Popover(未读角标 + 列表 + 正文弹层),对齐 Vue <NoticeBell /> */}
            <NoticeBell />
            <Dropdown menu={{ items: userMenu, onClick: onUserMenu }} trigger={['click']}>
              <Space size={8} style={{ cursor: 'pointer', padding: '0 4px' }}>
                <Avatar size="small" src={userInfo?.avatar || undefined}>{avatarChar}</Avatar>
                <Typography.Text>{userInfo?.name}</Typography.Text>
              </Space>
            </Dropdown>
          </div>
        </div>
      </header>

      <main className="shell-content">
        {/* 水印恒挂、content 空则不绘 —— 切换水印开关不 remount 内容区(否则 KeepAlive 缓存全丢)。 */}
        <Watermark
          content={watermark ? wmContent : ''}
          font={{ color: wmColor }}
          rotate={-16}
          gap={[192, 128]}
          style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}
        >
          {showTabs ? <div className="shell-tabs"><TabsBar /></div> : null}
          <div className="shell-page"><KeepAliveOutlet /></div>
        </Watermark>
      </main>

      {/* 移动端抽屉侧栏:完整树,选中即关。 */}
      {/* v6:自定义面板宽走 styles.section(`width` 已废、`size` 只有 378/736 两档,对移动菜单太宽)。 */}
      <Drawer placement="left" open={mobileOpen} onClose={() => setMobileOpen(false)} closable={false} styles={{ section: { width: 236 }, body: { padding: 0 } }}>
        <SideNav showBrand items={items} value={activeKey} onSelect={(k) => { onSelect(k); setMobileOpen(false) }} />
      </Drawer>

      <SettingsDrawer open={settingsOpen} onClose={() => setSettingsOpen(false)} />
      <MenuSearch open={searchOpen} onClose={() => setSearchOpen(false)} items={items} />
    </div>
  )
}
