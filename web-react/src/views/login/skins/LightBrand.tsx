import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { BrandIcon } from '@/components/oauth/BrandIcon'
import { TenonLogo } from '@/components/TenonLogo'
import { appVersion, useSiteStore } from '@/stores/site'
import scene from '@/assets/login/light-brand-scene.webp'
import { LoginForm } from '../LoginForm'
import './lightbrand.css'

const previewProviders = [{ code: 'github', name: 'GitHub' }, { code: 'gitee', name: 'Gitee' }, { code: 'wechat', name: 'WeChat' }]
const features = [
  { icon: 'ph:chart-bar', title: 'brandFeatureFlexibleTitle', desc: 'brandFeatureFlexibleDesc' },
  { icon: 'ph:shield-check', title: 'brandFeatureSecureTitle', desc: 'brandFeatureSecureDesc' },
  { icon: 'ph:users-three', title: 'brandFeatureTeamsTitle', desc: 'brandFeatureTeamsDesc' },
] as const

/** 参考浅色品牌稿的双栏皮肤；表单与认证状态继续由共享 LoginForm 独占。 */
export function LightBrand() {
  const { t } = useTranslation()
  const site = useSiteStore((s) => s.site)
  // 仅开发环境的显式外观预览，不注册提供方、不发起认证。
  const previewSso = import.meta.env.DEV && new URLSearchParams(window.location.search).get('previewSso') === '1'
  const year = new Date().getFullYear()

  return (
    <main className="light-brand">
      <section className="light-brand-panel" aria-label={site.title}>
        <div className="light-brand-mark light-brand-mark-small">
          {site.logo ? <img src={site.logo} alt="" /> : <TenonLogo size={36} />}
          <span>{site.title}</span>
        </div>

        <div className="light-brand-copy">
          <div className="light-brand-mark light-brand-mark-hero">
            {site.logo ? <img src={site.logo} alt="" /> : <TenonLogo size={84} />}
            <span>{site.title}</span>
          </div>
          <h1>
            <span>{t('login.brandHeadlineFirst')}</span>
            <span>{t('login.brandHeadlineSecond')}</span>
          </h1>
          <p className="light-brand-intro">{site.subtitle || t('login.brandIntro')}</p>
          <ul className="light-brand-features">
            {features.map((feature) => (
              <li key={feature.icon}>
                <span className="light-brand-feature-icon" aria-hidden="true">
                  <AppIcon icon={feature.icon} size={25} />
                </span>
                <span>
                  <strong>{t(`login.${feature.title}`)}</strong>
                  <small>{t(`login.${feature.desc}`)}</small>
                </span>
              </li>
            ))}
          </ul>
        </div>

        <img className="light-brand-scene" src={scene} alt="" />
        <p className="light-brand-caption"><span />{t('login.brandCaption')}</p>
      </section>

      <section className="light-brand-form-side" aria-label={t('login.title')}>
        <div className="light-brand-card">
          <LoginForm showBrand={false} showFooter={false} presentation="brand" />
          {previewSso && <div className="lb-sso-preview">
            <div className="lb-preview-divider">{t('login.otherMethods')}</div>
            <div className="lb-preview-buttons">
              {previewProviders.map((provider) => <button key={provider.code} type="button" disabled title={t('login.ssoPreviewNote')}>
                <BrandIcon code={provider.code} size={24} />{provider.name}
              </button>)}
            </div>
            <p className="lb-preview-note">{t('login.ssoPreviewNote')}</p>
          </div>}
          <p className="light-brand-help">{t('login.accountHelp')}</p>
        </div>
        <footer className="light-brand-footer">
          <span>
            © {year}{' '}
            {site.copyrightUrl ? (
              <a href={site.copyrightUrl} target="_blank" rel="noreferrer">{site.copyright || site.title}</a>
            ) : (
              site.copyright || site.title
            )}
          </span>
          {appVersion ? <span>v{appVersion}</span> : null}
        </footer>
      </section>
    </main>
  )
}
