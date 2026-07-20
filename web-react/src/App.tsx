import { useLayoutEffect } from 'react'
import { App as AntdApp, ConfigProvider } from 'antd'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import antdZhCN from 'antd/locale/zh_CN'
import antdEnUS from 'antd/locale/en_US'
import { useAntdTheme } from '@/theme/useAntdTheme'
import { useAppStore, isDark } from '@/stores/app'
import LoginPage from '@/views/login/LoginPage'
import { Protected } from '@/router/Protected'

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
  const grayscale = useAppStore((s) => s.grayscale)

  const themeConfig = useAntdTheme({ dark, accent, density })

  // 灰阶只是 <html> 上的一个 CSS filter,不进 antd 主题 —— 单独一条 effect,不塞进 useAntdTheme 的
  // 依赖数组(否则只切灰阶也会白重建整棵 antd 主题)。data-theme/data-density 由 useAntdTheme 打。
  useLayoutEffect(() => {
    document.documentElement.toggleAttribute('data-gray', grayscale)
  }, [grayscale])

  return (
    // antd 自带文案(空态/分页/日期)是**另一套 locale**,与我们的 i18n 无关,必须一起切 ——
    // 否则是「中文界面 + No data」。
    <ConfigProvider theme={themeConfig} locale={locale === 'en-US' ? antdEnUS : antdZhCN}>
      {/* antd 的 `App` 提供 `message`/`modal`/`notification` 的上下文实例,必须在 ConfigProvider 之内。 */}
      <AntdApp>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            {/* 受保护区:登录守卫 + 强制改密守卫 + F5 深链重建 + 布局壳 + 菜单派生的动态路由。 */}
            <Route path="/*" element={<Protected />} />
          </Routes>
        </BrowserRouter>
      </AntdApp>
    </ConfigProvider>
  )
}
