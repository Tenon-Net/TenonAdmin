import { App as AntdApp, ConfigProvider, Spin } from 'antd'
import { lazy, Suspense, useEffect } from 'react'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import antdZhCN from 'antd/locale/zh_CN'
import antdEnUS from 'antd/locale/en_US'
import { useAntdTheme } from '@/theme/useAntdTheme'
import { useDocumentGrayscale } from '@/theme/useDocumentGrayscale'
import { useAppStore, isDark } from '@/stores/app'
import { useSiteStore } from '@/stores/site'
import { ReauthModal } from '@/components/ReauthModal'
const LoginPage = lazy(() => import('@/views/login/LoginPage'))
const CallbackPage = lazy(() => import('@/views/oauth/CallbackPage'))
const MfaBindPage = lazy(() => import('@/views/mfa/BindPage'))
const Protected = lazy(() => import('@/router/Protected').then((module) => ({ default: module.Protected })))
const routeFallback = (
  <div style={{ display: 'grid', minHeight: '100vh', placeItems: 'center' }}>
    <Spin size="large" />
  </div>
)

/**
 * 应用根:主题桥(ConfigProvider)+ antd `App` 上下文 + 路由。
 * B2–B4 的探针页(检查项已被 antd-theme/i18n/app spec 覆盖)已随 B8 布局壳落地删除。
 */
export default function App() {
  // 分开订阅而不是整体订阅 —— 无关字段(collapsed / 界面开关)变动不该重建整棵 ConfigProvider。
  const dark = useAppStore(isDark)
  const accent = useAppStore((s) => s.accent)
  const density = useAppStore((s) => s.density)
  const locale = useAppStore((s) => s.locale)

  const themeConfig = useAntdTheme({ dark, accent, density })
  // data-theme/data-density 由 useAntdTheme 打;灰阶单独一条(不进 antd 主题依赖)。见该 hook 注释。
  useDocumentGrayscale()

  // 站点信息启动即拉(对齐 Vue App.vue:onMounted → loadSite):硬刷新已登录会话也能同步
  // 后台配置的标题到浏览器标签页。store 内部去重,登录页再次调用无害。
  useEffect(() => {
    void useSiteStore.getState().load().then(() => {
      const { title } = useSiteStore.getState().site
      if (title) document.title = title
    })
  }, [])

  return (
    // antd 自带文案(空态/分页/日期)是**另一套 locale**,与我们的 i18n 无关,必须一起切 ——
    // 否则是「中文界面 + No data」。
    <ConfigProvider theme={themeConfig} locale={locale === 'en-US' ? antdEnUS : antdZhCN}>
      {/* antd 的 `App` 提供 `message`/`modal`/`notification` 的上下文实例,必须在 ConfigProvider 之内。 */}
      <AntdApp>
        <BrowserRouter>
          <Suspense fallback={routeFallback}>
            <Routes>
              <Route path="/login" element={<LoginPage />} />
              <Route path="/oauth/callback" element={<CallbackPage />} />
              <Route path="/mfa/bind" element={<MfaBindPage />} />
            {/* 受保护区:登录守卫 + 强制改密守卫 + F5 深链重建 + 布局壳 + 菜单派生的动态路由。 */}
              <Route path="/*" element={<Protected />} />
            </Routes>
          </Suspense>
        </BrowserRouter>
        {/* Level3 高危写 40024:client 中间件唤起,须在 AntdApp 内以享用 message 上下文 */}
        <ReauthModal />
      </AntdApp>
    </ConfigProvider>
  )
}
