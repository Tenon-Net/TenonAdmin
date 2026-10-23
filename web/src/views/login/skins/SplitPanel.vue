<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { NConfigProvider } from 'naive-ui'
import { loginFormTheme } from '../loginTheme'
import AppIcon from '@/components/AppIcon.vue'
import TenonLogo from '@/components/TenonLogo.vue'
import { useSite } from '@/composables/useSite'
import { useAppStore } from '@/stores/app'
import LoginForm from '../LoginForm.vue'
import lightScene from '@/assets/login/brand-light.webp'
import darkScene from '@/assets/login/brand-dark.webp'

const { t } = useI18n()
const { site, appVersion } = useSite()
const app = useAppStore()
const year = new Date().getFullYear()
const points = computed(() => [
  { icon: 'ph:shield-check', label: t('login.featRbac') },
  { icon: 'ph:buildings', label: t('login.featScope') },
  { icon: 'ph:squares-four', label: t('login.featPortal') },
])
</script>

<template>
  <main class="split" :class="{ 'split-dark': app.isDark }">
    <section class="panel" :aria-label="site.title">
      <img class="panel-scene" :src="app.isDark ? darkScene : lightScene" alt="" />
      <div class="brand">
        <img v-if="site.logo" :src="site.logo" alt="" class="brand-logo" />
        <TenonLogo v-else :size="38" />
        <span class="brand-name">{{ site.title }}</span>
      </div>
      <div class="panel-copy">
        <h1 class="headline">
          <span>{{ t('login.brandHeadlineFirst') }}</span>
          <span>{{ t('login.brandHeadlineSecond') }}</span>
        </h1>
        <p class="sub">{{ site.subtitle || t('login.subtitle') }}</p>
        <ul class="points">
          <li v-for="point in points" :key="point.icon" class="point">
            <span class="point-icon"><AppIcon :icon="point.icon" :size="20" /></span>
            <span>{{ point.label }}</span>
          </li>
        </ul>
      </div>
      <p class="panel-caption"><span />{{ t('login.brandCaption') }}</p>
    </section>

    <section class="form-wrap" :aria-label="t('login.title')">
      <div class="form-card">
        <NConfigProvider :theme-overrides="loginFormTheme">
          <LoginForm presentation="brand" :show-logo="false" :show-footer="false" />
        </NConfigProvider>
        <p class="help-note">{{ t('login.accountHelp') }}</p>
      </div>
      <footer class="foot">
        <span>© {{ year }}
          <a v-if="site.copyrightUrl" :href="site.copyrightUrl" target="_blank" rel="noopener">{{ site.copyright || site.title }}</a>
          <template v-else>{{ site.copyright || site.title }}</template>
        </span>
        <span v-if="appVersion" class="foot-ver">v{{ appVersion }}</span>
      </footer>
    </section>
  </main>
</template>

<style scoped>
/* 参考图 1 的双栏留白；深色借图 2 的石墨材质。度量与表单沿用现有设计系统。 */
.split {
  min-height: 100svh;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  background: var(--color-login-canvas);
  color: var(--color-login-ink);
}

.panel {
  position: relative;
  min-width: 0;
  overflow: clip;
  background: var(--color-login-panel);
  border-right: 1px solid var(--color-login-line);
  display: flex;
  flex-direction: column;
}
.panel-scene {
  position: absolute;
  width: 100%;
  height: 40%;
  bottom: 0;
  object-fit: cover;
  object-position: right bottom;
  mask-image: linear-gradient(to bottom, transparent, #000 22%, #000 85%, transparent);
  pointer-events: none;
}
.split-dark .panel-scene {
  height: 100%;
  object-position: 48% center;
  opacity: 0.48;
  mask-image: linear-gradient(to bottom, transparent, #000 22%, #000 80%, transparent);
}
.brand {
  position: relative;
  display: flex;
  align-items: center;
  gap: var(--space-12);
  padding: 30px clamp(28px, 4vw, 64px);
}
.brand-logo { width: 38px; height: 38px; object-fit: contain; }
.brand-name { font-size: 20px; font-weight: 650; letter-spacing: -0.5px; overflow-wrap: anywhere; }
.panel-copy {
  position: relative;
  padding: clamp(50px, 9vh, 110px) clamp(32px, 6vw, 88px) 300px;
}
.headline {
  margin: 0;
  font-size: clamp(32px, 3.25vw, 48px);
  line-height: 1.32;
  letter-spacing: -0.035em;
  font-weight: 600;
  overflow-wrap: anywhere;
}
.headline span { display: block; }
.sub { max-width: 390px; margin: 24px 0 32px; font-size: 16px; line-height: 1.8; color: var(--color-login-muted); }
.points { display: grid; gap: 16px; list-style: none; padding: 0; margin: 0; }
.point { display: flex; align-items: center; gap: 14px; font-size: 14px; }
.point-icon {
  display: grid;
  place-items: center;
  width: 38px;
  height: 38px;
  flex-shrink: 0;
  border-radius: var(--radius-md);
  background: var(--color-login-icon-bg);
  color: var(--color-login-action);
}
.panel-caption {
  position: absolute;
  left: clamp(32px, 4vw, 64px);
  bottom: 28px;
  display: flex;
  align-items: center;
  gap: 14px;
  margin: 0;
  font-size: 12px;
  color: var(--color-login-muted);
}
.panel-caption span { height: 1px; width: 28px; background: var(--color-primary); }
.form-wrap {
  min-width: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 110px clamp(28px, 4.5vw, 72px) 84px;
  position: relative;
}
.form-card {
  --lf-title: var(--color-login-ink);
  --lf-link: var(--color-login-action);
  --lf-hint: var(--color-login-muted);
  --lf-border: var(--color-login-line);
  width: 100%;
  max-width: 560px;
  padding: 38px 42px 28px;
  background: var(--color-login-card);
  border: 1px solid var(--color-login-line);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-login);
}
.help-note {
  margin: 24px 0 0;
  color: var(--color-login-muted);
  text-align: center;
  font-size: 12px;
  line-height: 1.8;
}
.foot {
  position: absolute;
  bottom: 28px;
  display: flex;
  justify-content: center;
  flex-wrap: wrap;
  gap: 12px;
  padding: 0 24px;
  color: var(--color-login-muted);
  font-size: 12px;
}
.foot a { color: inherit; text-decoration: none; }
.foot a:hover { color: var(--color-primary); }
.split-dark :deep(.lf-sso-btn[data-provider="github"] .oauth-brand-badge) { filter: invert(1); }
.foot-ver { color: var(--color-login-muted); }
@media (max-width: 1100px) {
  .panel-copy { padding-left: 40px; padding-right: 32px; }
  .form-wrap { padding-left: 28px; padding-right: 28px; }
  .form-card { padding: 36px 28px 28px; }
}
@media (max-width: 900px) {
  .split { grid-template-columns: minmax(0, 1fr); grid-template-rows: auto 1fr; }
  .panel { border: 0; background: var(--color-login-canvas); overflow: visible; }
  .brand { padding: 24px; padding-right: 160px; min-height: 86px; }
  .brand-name { font-size: 18px; }
  .panel-copy, .panel-scene, .panel-caption { display: none; }
  .form-wrap { padding: 38px 24px 28px; justify-content: flex-start; }
  .foot { position: static; margin-top: auto; padding-top: 40px; }
}
@media (max-width: 480px) {
  .split { grid-template-rows: auto 1fr; }
  .brand { padding-left: 20px; gap: 8px; }
  .brand-name { max-width: 130px; font-size: 16px; }
  .form-wrap { padding: 38px 20px 24px; }
  .form-card { padding: 0; border: 0; box-shadow: none; background: transparent; }
  .help-note { margin-top: 28px; }
}
@media (max-height: 800px) and (min-width: 901px) {
  .panel-copy { padding-top: 30px; padding-bottom: 200px; }
  .headline { font-size: 36px; }
  .sub { margin: 20px 0 24px; }
  .points { gap: 12px; }
  .form-wrap { padding-top: 76px; padding-bottom: 50px; }
  .form-card :deep(.n-form-item-feedback-wrapper) { min-height: 12px; }
  .form-card :deep(.lf-welcome-sub) { margin-bottom: 20px; }
  .form-card { padding-top: 32px; padding-bottom: 24px; }
}
</style>
