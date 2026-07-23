import { type CSSProperties } from 'react'
import { CheckCircleFilled } from '@ant-design/icons'
import { useTranslation } from 'react-i18next'
import { useAppStore } from '@/stores/app'
import { useSiteStore, appVersion } from '@/stores/site'
import { mix, rgba } from '@/theme/mix'
import { TenonLogo } from '@/components/TenonLogo'
import { LoginForm } from '../LoginForm'
import './splitpanel.css'

/**
 * 双栏皮肤:左品牌面板(窄屏隐藏)+ 右全高工作区(问候 + 表单 + 版权)。移植 Vue `skins/SplitPanel.vue`。
 * 左栏自绘品牌/标题/卖点/页脚,故给 `LoginForm` 传 `showBrand={false}` `showFooter={false}` 免卡内重复。
 */
export function SplitPanel() {
  const { t } = useTranslation()
  const accent = useAppStore((s) => s.accent)
  const site = useSiteStore((s) => s.site)
  const year = new Date().getFullYear()

  // 卖点随语言切换。
  const points = [t('login.featRbac'), t('login.featScope'), t('login.featPortal')]

  const vars = {
    '--a1': rgba(accent, 0.5),
    '--a2': rgba(mix(accent, '#8B5CF6', 0.6), 0.4),
  } as CSSProperties

  return (
    <div className="split" style={vars}>
      {/* 左:品牌面板(窄屏隐藏)*/}
      <div className="split-panel">
        <div className="split-panel-aurora" />
        <div className="split-panel-inner">
          <div className="split-logo">
            {site.logo ? (
              <img src={site.logo} alt={site.title} className="split-logo-img" />
            ) : (
              <TenonLogo size={40} />
            )}
            <span>{site.title}</span>
          </div>
          <div className="split-bar" style={{ background: accent }} />
          {/* 标题拆 pre/accent/post 三段以支持 i18n,中间词染 accent 色 */}
          <h1 className="split-headline">
            {t('login.headlinePre')}
            <span style={{ color: accent }}>{t('login.headlineAccent')}</span>
            {t('login.headlinePost')}
          </h1>
          <p className="split-sub">{site.subtitle || t('login.subtitle')}</p>
          <ul className="split-points">
            {points.map((p) => (
              <li key={p}>
                <CheckCircleFilled style={{ color: accent }} />
                {p}
              </li>
            ))}
          </ul>
        </div>
      </div>

      {/* 右:全高工作区(居中表单 + 版权页脚);主题/语言切换在外壳全局工具药丸 */}
      <div className="split-form-wrap">
        <div className="split-mid">
          <div className="split-form-inner">
            <p className="split-greet">{t('login.welcome')}</p>
            <p className="split-greet-sub">{t('login.welcomeSub')}</p>
            <LoginForm showBrand={false} showFooter={false} />
          </div>
        </div>

        <p className="split-foot">
          © {year}{' '}
          {site.copyrightUrl ? (
            <a href={site.copyrightUrl} target="_blank" rel="noreferrer">
              {site.copyright || site.title}
            </a>
          ) : (
            site.copyright || site.title
          )}
          {appVersion ? <span className="split-foot-ver">v{appVersion}</span> : null}
        </p>
      </div>
    </div>
  )
}
