<script setup lang="ts">
import { computed } from 'vue'
import { NConfigProvider } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import BrandIcon from '@/components/oauth/BrandIcon.vue'
import TenonLogo from '@/components/TenonLogo.vue'
import { useSite } from '@/composables/useSite'
import { useAppStore } from '@/stores/app'
import LoginForm from '../LoginForm.vue'
import { loginFormTheme } from '../loginTheme'
import scene from '@/assets/login/light-brand-scene.webp'

const { t } = useI18n()
const { site, appVersion } = useSite()
const app = useAppStore()
// 仅开发环境的显式外观预览，不注册提供方、不发起认证。
const previewSso = import.meta.env.DEV && new URLSearchParams(window.location.search).get('previewSso') === '1'
const previewProviders = [{ code: 'github', name: 'GitHub' }, { code: 'gitee', name: 'Gitee' }, { code: 'wechat', name: 'WeChat' }]
const year = new Date().getFullYear()
const points = [
  { icon: 'ph:squares-four', key: 'Flexible' },
  { icon: 'ph:shield-check', key: 'Secure' },
  { icon: 'ph:users', key: 'Teams' },
]
const formTheme = computed(() => ({
  ...loginFormTheme,
  Input: { ...loginFormTheme.Input, heightLarge: '58px', fontSizeLarge: '16px', color: 'var(--lb-card)', colorFocus: 'var(--lb-card)', textColor: 'var(--lb-ink)', placeholderColor: 'var(--lb-muted)', border: '1px solid var(--lb-line)' },
  Form: { labelTextColor: 'var(--lb-ink)' },
  Checkbox: { textColor: 'var(--lb-muted)' },
}))
</script>

<template>
  <main class="light-brand" :class="{ 'light-brand-dark': app.isDark }">
    <section class="lb-panel" :aria-label="site.title">
      <img class="lb-scene" :src="scene" width="725" height="536" alt="" />
      <div class="lb-brand">
        <img v-if="site.logo" :src="site.logo" class="lb-logo" alt="" />
        <TenonLogo v-else :size="36" />
        <span>{{ site.title }}</span>
      </div>
      <div class="lb-copy">
        <h1 class="lb-heading">
          <img v-if="site.logo" :src="site.logo" class="lb-hero-logo" alt="" />
          <TenonLogo v-else :size="84" />
          <span>{{ site.title }}</span>
        </h1>
        <p class="lb-headline">{{ t('login.brandHeadlineFirst') }}<br />{{ t('login.brandHeadlineSecond') }}</p>
        <p class="lb-intro">{{ site.subtitle || t('login.brandIntro') }}</p>
        <ul class="lb-points">
          <li v-for="point in points" :key="point.key">
            <span class="lb-point-icon"><AppIcon :icon="point.icon" :size="28" /></span>
            <div><strong>{{ t(`login.brandFeature${point.key}Title`) }}</strong><p>{{ t(`login.brandFeature${point.key}Desc`) }}</p></div>
          </li>
        </ul>
      </div>
      <p class="lb-caption"><span />{{ t('login.brandCaption') }}</p>
    </section>
    <section class="lb-form-region" :aria-label="t('login.title')">
      <div class="lb-card">
        <NConfigProvider :theme-overrides="formTheme">
          <LoginForm presentation="brand" :show-logo="false" :show-footer="false" />
        </NConfigProvider>
        <div v-if="previewSso" class="lb-sso-preview">
          <div class="lb-preview-divider">{{ t('login.otherMethods') }}</div>
          <div class="lb-preview-buttons">
            <button v-for="provider in previewProviders" :key="provider.code" type="button" disabled :title="t('login.ssoPreviewNote')">
              <BrandIcon :code="provider.code" :size="24" />{{ provider.name }}
            </button>
          </div>
          <p class="lb-preview-note">{{ t('login.ssoPreviewNote') }}</p>
        </div>
        <p class="lb-help">{{ t('login.accountHelp') }}</p>
      </div>
      <footer class="lb-footer">
        <span>© {{ year }} <a v-if="site.copyrightUrl" :href="site.copyrightUrl" target="_blank" rel="noopener">{{ site.copyright || site.title }}</a><template v-else>{{ site.copyright || site.title }}</template></span>
        <span v-if="appVersion">v{{ appVersion }}</span>
      </footer>
    </section>
  </main>
</template>

<style scoped>
/* 仅此皮肤的参考图色阶，主题切换沿用全站状态，不修改全局令牌。 */
.light-brand {
  --lb-canvas: #f8fafc;
  --lb-card: #fff;
  --lb-ink: #10152c;
  --lb-muted: #69758f;
  --lb-line: #e0e5ef;
  --lb-accent: #505eff;
  min-height: 100svh;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  background: var(--lb-canvas);
  color: var(--lb-ink);
}
.light-brand-dark {
  --lb-canvas: var(--color-bg-body);
  --lb-card: var(--color-bg-container);
  --lb-ink: var(--color-text-primary);
  --lb-muted: var(--color-text-secondary);
  --lb-line: var(--color-border);
  --lb-accent: #9ba4ff;
}
.lb-panel { position: relative; min-width: 0; background: radial-gradient(ellipse at 100% 0%, #cfd6ff 0, #f8fbff 38%, #fbfcfe 68%); }
.light-brand-dark .lb-panel { background: var(--color-bg-elevated); }
.lb-scene { position: absolute; bottom: 0; width: 100%; height: auto; pointer-events: none; mask-image: linear-gradient(transparent, #000 12%); }
.light-brand-dark .lb-scene { opacity: .36; mix-blend-mode: luminosity; mask-image: linear-gradient(transparent, #000 50%); }
.lb-brand { display: flex; align-items: center; gap: 10px; padding: 30px 7%; font-size: 20px; font-weight: 650; }
.lb-logo { width: 36px; height: 36px; object-fit: contain; }
.lb-copy { position: relative; padding: 100px 9% 300px 12%; }
.lb-heading { display: flex; align-items: center; gap: 20px; margin: 0; font-size: clamp(30px, 3vw, 44px); line-height: 1.2; letter-spacing: -.035em; overflow-wrap: anywhere; }
.lb-heading > :first-child { flex-shrink: 0; }
.lb-hero-logo { width: 84px; height: 84px; object-fit: contain; }
.lb-headline { font-size: clamp(28px, 2.5vw, 36px); font-weight: 400; line-height: 1.2; letter-spacing: -.025em; margin: 34px 0 28px; }
.lb-intro { max-width: 485px; margin: 0 0 38px; color: var(--lb-muted); font-size: 19px; line-height: 1.45; }
.lb-points { list-style: none; padding: 0; margin: 0; display: grid; gap: 24px; }
.lb-points li { display: flex; align-items: center; gap: 24px; }
.lb-point-icon { width: 62px; height: 62px; flex-shrink: 0; display: grid; place-items: center; background: color-mix(in srgb, var(--lb-accent) 6%, var(--lb-card)); color: var(--lb-accent); border-radius: 18px; }
.lb-points strong { font-size: 17px; font-weight: 600; }
.lb-points p { margin: 3px 0 0; font-size: 15px; line-height: 1.5; color: var(--lb-muted); }
.lb-caption { position: absolute; bottom: 50px; left: 8.5%; right: 5%; display: flex; align-items: center; gap: 16px; margin: 0; font-size: 12px; color: var(--lb-muted); }
.lb-caption > span { width: 34px; height: 2px; background: var(--lb-accent); flex-shrink: 0; }
.lb-form-region { position: relative; display: flex; align-items: center; justify-content: center; padding: 110px 10.3% 134px; min-width: 0; }
.lb-card { --lf-title: var(--lb-ink); --lf-hint: var(--lb-muted); --lf-link: var(--lb-accent); --lf-border: var(--lb-line); width: 100%; max-width: 574px; min-height: 728px; padding: 64px 42px 48px; border: 1px solid color-mix(in srgb, var(--lb-line) 45%, transparent); border-radius: 18px; background: var(--lb-card); box-shadow: 0 16px 44px #22345c06; display: flex; flex-direction: column; justify-content: flex-start; }
.lb-card :deep(.lf-title) { text-align: center; font-size: 36px; margin-bottom: 10px; }
.lb-card :deep(.lf-welcome-sub) { text-align: center; font-size: 18px; margin-bottom: 32px; }
.lb-card :deep(.hero-btn) { height: 60px; font-size: 18px; }
.lb-card :deep(.lf-captcha-img), .lb-card :deep(.lf-send-btn) { height: 58px; }
.lb-card :deep(.lf-sso-btn) { border: 1px solid var(--lb-line); border-radius: var(--radius-md); width: 72px; height: 52px; }
.lb-card :deep(.oauth-brand-badge) { width: 32px; height: 32px; }
.lb-card :deep(.lf-captcha-img) { flex: 0 0 120px; min-width: 0; }
.lb-card :deep(.lf-captcha-img svg) { width: 100%; height: auto; max-height: 100%; }
.light-brand-dark :deep(.lf-sso-btn[data-provider="github"] .oauth-brand-badge) { filter: invert(1); }
.lb-card :deep(.lf-between) { margin-bottom: 30px; }
.lb-help { text-align: center; color: var(--lb-muted); font-size: 13px; margin: 38px 0 0; line-height: 1.6; }
.lb-footer { position: absolute; bottom: 50px; display: flex; gap: 12px; color: var(--lb-muted); font-size: 12px; }
.lb-footer a { color: inherit; }
.lb-card :deep(:focus-visible) { outline: 2px solid var(--lb-accent); outline-offset: 3px; }
@media (min-width: 1600px) { .lb-copy { padding-top: 100px; } }
@media (max-height: 850px) and (min-width: 901px) {
  .lb-copy { padding-top: 35px; padding-bottom: 200px; }
  .lb-heading { font-size: 36px; }
  .lb-heading > :first-child { width: 64px !important; height: 64px !important; }
  .lb-headline { margin: 20px 0; font-size: 30px; }
  .lb-intro { font-size: 16px; margin-bottom: 24px; }
  .lb-points { gap: 16px; }
  .lb-point-icon { width: 48px; height: 48px; }
  .lb-form-region { padding: 95px 8% 100px; }
  .lb-card { min-height: 0; padding: 36px; }
  .lb-caption, .lb-footer { bottom: 32px; }
}
@media (max-width: 1100px) and (min-width: 901px) { .lb-copy { padding-left: 8%; padding-right: 6%; } .lb-form-region { padding-left: 6%; padding-right: 6%; } .lb-card { padding-left: 26px; padding-right: 26px; } }
@media (max-width: 900px) {
  .light-brand { grid-template-columns: minmax(0, 1fr); grid-template-rows: auto 1fr; }
  .lb-panel, .light-brand-dark .lb-panel { background: transparent; }
  .lb-brand { padding: 26px 150px 26px 20px; min-height: 88px; gap: 8px; font-size: 16px; overflow-wrap: anywhere; }
  .lb-brand > :first-child { flex-shrink: 0; }
  .lb-copy, .lb-scene, .lb-caption { display: none; }
  .lb-form-region { display: flex; flex-direction: column; justify-content: flex-start; padding: 36px 20px 24px; }
  .lb-card { min-height: 0; padding: 32px 24px; }
  .lb-card :deep(.lf-title) { font-size: 30px; }
  .lb-card :deep(.lf-welcome-sub) { font-size: 15px; }
  .lb-footer { position: static; margin-top: auto; padding-top: 36px; flex-wrap: wrap; justify-content: center; }
}
@media (max-width: 480px) { .lb-card { padding: 28px 20px; } .lb-points p { font-size: 14px; } }
@media (prefers-reduced-motion: reduce) { .lb-card :deep(*) { transition: none !important; animation: none !important; } .lb-card :deep(.hero-btn:hover) { transform: none; } }

.lb-sso-preview { margin-top: 30px; }
.lb-preview-divider { display: flex; align-items: center; gap: 16px; color: var(--lb-muted); font-size: 13px; margin-bottom: 24px; }
.lb-preview-divider::before, .lb-preview-divider::after { content: ''; height: 1px; flex: 1; background: var(--lb-line); }
.lb-preview-buttons { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.lb-preview-buttons button { display: flex; align-items: center; justify-content: center; gap: 10px; min-height: 54px; padding: 8px; border: 1px solid var(--lb-line); border-radius: 10px; background: var(--lb-card); color: var(--lb-ink); font: inherit; font-size: 14px; cursor: default; opacity: 1; }
.lb-preview-buttons :deep(.oauth-brand-badge) { width: 24px; height: 24px; }
.lb-preview-note { margin: 12px 0 0; text-align: center; color: var(--lb-muted); font-size: 12px; }
@media (max-width: 480px) { .lb-preview-buttons { gap: 8px; } .lb-preview-buttons button { flex-direction: column; gap: 6px; font-size: 12px; } }
</style>
