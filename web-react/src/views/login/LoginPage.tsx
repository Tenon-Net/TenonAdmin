import { useState } from 'react'
import { Tooltip } from 'antd'
import { MoonOutlined, SunOutlined } from '@ant-design/icons'
import { useTranslation } from 'react-i18next'
import { useAppStore, isDark } from '@/stores/app'
import { LOGIN_SKINS, DEFAULT_SKIN } from './skins'
import './loginshell.css'

const KEY = 'login-skin'

/** ?skin= 一次性覆盖(便于对比预览),否则读记忆,再回退默认。 */
function initSkin(): string {
  const q = new URLSearchParams(window.location.search).get('skin')
  if (q && LOGIN_SKINS.some((s) => s.id === q)) return q
  const saved = localStorage.getItem(KEY)
  if (saved && LOGIN_SKINS.some((s) => s.id === saved)) return saved
  return DEFAULT_SKIN
}

/**
 * 登录页外壳(移植 Vue `views/login/index.vue`)。持有皮肤选择(`?skin=`→记忆→默认),渲染激活皮肤,
 * 右上角工具栏放皮肤切换 + 主题/语言(三种皮肤通用的深色磨砂药丸,在明/暗/极光三底色上都可读)。
 * 账号密码表单在共享 `LoginForm`,由各皮肤内嵌。
 */
export default function LoginPage() {
  const { t } = useTranslation()
  const [skin, setSkin] = useState(initSkin)
  const dark = useAppStore(isDark)
  const toggleDark = useAppStore((s) => s.toggleDark)
  const locale = useAppStore((s) => s.locale)
  const setLocale = useAppStore((s) => s.setLocale)

  // 切换即写盘;`?skin=` 预览只影响本次(initSkin 读它),不落库 —— 对齐 Vue `watch(skin)`(非 immediate)。
  // **别放 useEffect([skin])**:那会在挂载时也写,把预览链接永久改掉用户记忆的皮肤。
  function pick(id: string) {
    setSkin(id)
    localStorage.setItem(KEY, id)
  }

  const active = LOGIN_SKINS.find((s) => s.id === skin) ?? LOGIN_SKINS[0]!
  const Skin = active.component

  return (
    <div className="login-shell">
      <Skin />

      {/* 右上角工具栏:皮肤切换 + 主题/语言,两枚磨砂药丸并排。 */}
      <div className="login-topbar">
        <div className="skin-switch" role="tablist" aria-label={t('app.loginStyle')}>
          {LOGIN_SKINS.map((s) => (
            <button
              key={s.id}
              className={`skin-seg${s.id === skin ? ' active' : ''}`}
              type="button"
              role="tab"
              aria-selected={s.id === skin}
              onClick={() => pick(s.id)}
            >
              {s.label}
            </button>
          ))}
        </div>

        <div className="login-tools">
          <Tooltip title={dark ? t('app.light') : t('app.dark')}>
            <button
              className="tool-btn"
              type="button"
              aria-label={dark ? t('app.light') : t('app.dark')}
              onClick={toggleDark}
            >
              {dark ? <SunOutlined /> : <MoonOutlined />}
            </button>
          </Tooltip>
          <Tooltip title={t('app.language')}>
            <button
              className="tool-btn tool-lang"
              type="button"
              aria-label={t('app.language')}
              onClick={() => setLocale(locale === 'zh-CN' ? 'en-US' : 'zh-CN')}
            >
              {locale === 'zh-CN' ? '中' : 'EN'}
            </button>
          </Tooltip>
        </div>
      </div>
    </div>
  )
}
