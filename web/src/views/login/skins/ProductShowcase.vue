<script setup lang="ts">
import { computed } from 'vue'
import { NConfigProvider } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import TenonLogo from '@/components/TenonLogo.vue'
import { useSite } from '@/composables/useSite'
import { useAppStore } from '@/stores/app'
import LoginForm from '../LoginForm.vue'
import { loginFormTheme } from '../loginTheme'
import productPreview from '@/assets/login/product-preview.webp'

const { t } = useI18n()
const app = useAppStore()
const { site, appVersion } = useSite()
const year = new Date().getFullYear()
const documentationUrl = computed(() => `https://tenon.52moyu.net/${app.locale === 'zh-CN' ? 'zh/' : ''}`)
const points = computed(() => [
  { icon: 'ph:shield-check', title: t('login.featRbac'), description: t('login.productRbac') },
  { icon: 'ph:buildings', title: t('login.featScope'), description: t('login.productScope') },
  { icon: 'ph:squares-four', title: t('login.featPortal'), description: t('login.productPortal') },
])
</script>

<template>
  <div class="product-page" :class="{ 'product-dark': app.isDark }">
    <header class="product-header">
      <div class="product-brand">
        <img v-if="site.logo" :src="site.logo" alt="" class="product-logo" />
        <TenonLogo v-else :size="36" />
        <span class="product-brand-name">{{ site.title }}</span>
      </div>
      <span class="product-header-note">{{ t('login.productHeader') }}</span>
    </header>

    <main class="product-card">
      <section class="product-story" :aria-label="site.title">
        <div class="product-copy">
          <h1 class="product-title">{{ t('login.productTagline') }}</h1>
          <p class="product-description">{{ site.subtitle || t('login.productDescription') }}</p>
        </div>
        <!-- 真实产品截图完整呈现，不裁切成装饰边角，也不在匿名页请求业务数据。 -->
        <figure class="product-preview">
          <img :src="productPreview" :alt="t('login.productPreviewAlt')" width="1280" height="624" />
        </figure>
          <ul class="product-features">
            <li v-for="point in points" :key="point.icon" class="product-feature">
              <span class="product-feature-icon"><AppIcon :icon="point.icon" :size="22" /></span>
              <span>
                <span class="product-feature-title">{{ point.title }}</span>
                <span class="product-feature-description">{{ point.description }}</span>
              </span>
            </li>
          </ul>
      </section>

      <section class="product-auth" :aria-label="t('login.title')">
        <NConfigProvider :theme-overrides="loginFormTheme" class="product-form">
          <LoginForm presentation="brand" :show-logo="false" :show-footer="false" />
        </NConfigProvider>
        <a class="product-docs" :href="documentationUrl" target="_blank" rel="noopener noreferrer">
          <AppIcon icon="ph:book-open" :size="18" />
          {{ t('login.productDocs') }}
          <AppIcon icon="ph:arrow-up-right" :size="16" />
        </a>
        <p class="product-help">{{ t('login.accountHelp') }}</p>
      </section>
    </main>

    <footer class="product-footer">
      <span>© {{ year }}
        <a v-if="site.copyrightUrl" :href="site.copyrightUrl" target="_blank" rel="noopener">{{ site.copyright || site.title }}</a>
        <template v-else>{{ site.copyright || site.title }}</template>
      </span>
      <span v-if="appVersion">v{{ appVersion }}</span>
    </footer>
  </div>
</template>

<style scoped>
/* Hallmark：产品实图与登录表单双栏；沿用项目主题令牌，保持局部作用域。 */
.product-page {
  min-height: 100svh;
  padding: 116px 40px 32px;
  display: flex;
  flex-direction: column;
  color: var(--color-login-ink);
  background: var(--color-login-canvas);
}
.product-header {
  position: absolute;
  top: 28px;
  left: max(40px, calc((100% - 1200px) / 2));
  right: 180px;
  display: flex;
  align-items: center;
  gap: 20px;
}
.product-brand { display: flex; align-items: center; gap: 10px; min-width: 0; }
.product-brand > :first-child { flex-shrink: 0; }
.product-logo { width: 36px; height: 36px; object-fit: contain; }
.product-brand-name { font-size: 20px; font-weight: 650; letter-spacing: -.5px; overflow-wrap: anywhere; }
.product-header-note { border-left: 1px solid var(--color-login-line); padding-left: 20px; color: var(--color-login-muted); font-size: 12px; }
.product-card {
  width: 100%;
  max-width: 1200px;
  margin: auto;
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr);
  border: 1px solid var(--color-login-line);
  border-radius: 24px;
  background: var(--color-login-card);
  box-shadow: var(--shadow-login);
}
.product-story {
  min-width: 0;
  padding: 48px 40px 40px;
  border-radius: 24px 0 0 24px;
  background: linear-gradient(150deg, var(--color-login-panel), var(--color-login-product-panel));
  display: flex;
  flex-direction: column;
  justify-content: center;
}
.product-title { text-wrap: balance; min-width: 0; max-width: 460px; margin: 0; font-size: clamp(32px, 3vw, 42px); line-height: 1.35; letter-spacing: -.035em; font-weight: 650; overflow-wrap: anywhere; }
.product-description { max-width: 460px; margin: 20px 0 0; font-size: 15px; line-height: 1.85; color: var(--color-login-muted); }
.product-preview {
  margin: 36px -12px 32px 0;
  border: 1px solid var(--color-login-line);
  border-radius: 12px;
  background: var(--color-login-card);
  box-shadow: var(--shadow-2);
  overflow: clip;
}
.product-preview img { display: block; width: 100%; height: auto; }
.product-dark .product-preview { opacity: .88; }
.product-features { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; margin: 0; padding: 0; list-style: none; }
.product-feature { min-width: 0; }
.product-feature-icon { display: inline-flex; color: var(--color-login-action); margin-bottom: 10px; }
.product-feature-title { display: block; font-size: 13px; font-weight: 600; line-height: 1.6; }
.product-feature-description { display: block; color: var(--color-login-muted); margin-top: 4px; font-size: 12px; line-height: 1.7; }
.product-auth {
  --lf-title: var(--color-login-ink);
  --lf-link: var(--color-login-action);
  --lf-hint: var(--color-login-muted);
  --lf-border: var(--color-login-line);
  padding: 60px 48px 48px;
  min-width: 0;
  display: flex;
  flex-direction: column;
  justify-content: center;
}
.product-form { width: 100%; }
.product-auth :deep(.lf-title) { font-size: 30px; letter-spacing: -.025em; }
.product-auth :deep(.lf-welcome-sub) { margin-bottom: 32px; }
.product-auth :deep(.hero-btn) { height: 48px; }
.product-auth :deep(.lf-captcha) { min-width: 0; }
.product-auth :deep(.lf-captcha-img) { flex: 0 0 112px; min-width: 0; }
.product-auth :deep(.lf-captcha-img svg) { width: 100%; height: auto; max-height: 100%; }
.product-docs { display: flex; align-items: center; justify-content: center; gap: 8px; align-self: center; min-height: 40px; margin-top: 24px; color: var(--color-login-muted); font-size: 13px; text-decoration: none; }
.product-docs:hover { color: var(--color-login-action); }
.product-docs:focus-visible { outline: 2px solid var(--color-login-action); outline-offset: 4px; border-radius: var(--radius-sm); }
.product-help { margin: 12px 0 0; text-align: center; color: var(--color-login-muted); font-size: 12px; line-height: 1.8; }
.product-footer { display: flex; flex-wrap: wrap; justify-content: center; gap: 12px; margin-top: 36px; font-size: 12px; color: var(--color-login-muted); }
.product-footer a { color: inherit; text-decoration: none; }
.product-dark :deep(.lf-sso-btn[data-provider="github"] .oauth-brand-badge) { filter: invert(1); }
@media (max-width: 1100px) {
  .product-page { padding-left: 28px; padding-right: 28px; }
  .product-header { left: 28px; }
  .product-story { padding: 36px 28px; }
  .product-title { font-size: 32px; }
  .product-auth { padding: 40px 32px; }
  .product-features { gap: 12px; }
}
@media (max-width: 900px) {
  .product-page { padding: 112px 24px 28px; }
  .product-header { left: 24px; top: 26px; right: 160px; }
  .product-header-note, .product-story { display: none; }
  .product-brand-name { font-size: 18px; }
  .product-card { max-width: 480px; grid-template-columns: minmax(0, 1fr); }
  .product-auth { padding: 40px 32px 32px; }
  .product-footer { margin-top: auto; padding-top: 32px; }
}
@media (max-width: 480px) {
  .product-page { padding: 112px 20px 24px; }
  .product-header { left: 20px; }
  .product-brand { gap: 8px; }
  .product-brand-name { font-size: 16px; }
  .product-card { margin: 0 auto; border-radius: 18px; }
  .product-auth { padding: 28px 20px; }
  .product-auth :deep(.lf-title) { font-size: 28px; }
}
@media (max-width: 360px) {
  .product-page { padding-left: 12px; padding-right: 12px; }
  .product-auth { padding-left: 16px; padding-right: 16px; }
  .product-auth :deep(.lf-captcha-img) { flex-basis: 96px; }
}
@media (max-height: 850px) and (min-width: 901px) {
  .product-page { padding-top: 96px; padding-bottom: 24px; }
  .product-story { padding-top: 32px; padding-bottom: 32px; }
  .product-auth { padding-top: 36px; padding-bottom: 36px; }
  .product-preview { margin-top: 24px; margin-bottom: 24px; }
  .product-footer { margin-top: 24px; }
}
@media (prefers-reduced-motion: reduce) { .product-auth :deep(.hero-btn) { transition: none; transform: none; } }
</style>
