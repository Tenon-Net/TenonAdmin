<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import { NDropdown, NTooltip } from 'naive-ui'
import AppIcon from '@/components/AppIcon.vue'
import { useI18n } from 'vue-i18n'
import { useAppStore } from '@/stores/app'
import { LOGIN_SKINS, DEFAULT_SKIN } from './skins'

const KEY = 'login-skin'
const app = useAppStore()
const { t } = useI18n()

function initSkin(): string {
  // ?skin= 一次性覆盖(便于对比预览),否则读记忆,再回退默认。
  const q = new URLSearchParams(window.location.search).get('skin')
  if (q && LOGIN_SKINS.some((s) => s.id === q)) return q
  const saved = localStorage.getItem(KEY)
  if (saved && LOGIN_SKINS.some((s) => s.id === saved)) return saved
  return DEFAULT_SKIN
}

const skin = shallowRef(initSkin())
watch(skin, (v) => localStorage.setItem(KEY, v))

const skinOptions = computed(() => LOGIN_SKINS.map((item) => ({ label: t(item.label), key: item.id })))

const active = computed(() => LOGIN_SKINS.find((s) => s.id === skin.value) ?? LOGIN_SKINS[0])
</script>

<template>
  <div class="login-shell" :class="{ 'login-shell-brand': skin === 'split' || skin === 'product' || skin === 'light-brand' }">
    <component :is="active.component" />

    <!-- 右上角工具栏：皮肤菜单、明暗主题与语言。 -->
    <div class="login-topbar">
      <NDropdown :options="skinOptions" :value="skin" trigger="click" @select="skin = String($event)">
        <button class="tool-btn skin-picker" type="button" :aria-label="t('app.loginStyle')" :title="t('app.loginStyle')">
          <AppIcon icon="ph:palette" :size="18" />
        </button>
      </NDropdown>

      <!-- 工具始终在外壳内，主题和语言切换不重新挂载登录表单。 -->
      <div class="login-tools">
        <n-tooltip>
          <template #trigger>
            <button
              class="tool-btn"
              type="button"
              :aria-label="app.isDark ? t('app.light') : t('app.dark')"
              @click="app.toggleDark()"
            >
              <AppIcon :icon="app.isDark ? 'ph:moon-stars' : 'ph:sun'" :size="18" />
            </button>
          </template>
          {{ app.isDark ? t('app.light') : t('app.dark') }}
        </n-tooltip>
        <n-tooltip>
          <template #trigger>
            <button
              class="tool-btn tool-lang"
              type="button"
              :aria-label="t('app.language')"
              @click="app.setLocale(app.locale === 'zh-CN' ? 'en-US' : 'zh-CN')"
            >
              {{ app.locale === 'zh-CN' ? '中' : 'EN' }}
            </button>
          </template>
          {{ t('app.language') }}
        </n-tooltip>
      </div>
    </div>
  </div>
</template>

<style scoped>
.login-shell {
  position: relative;
  height: 100vh;
  overflow: hidden;
}
/* 右上角工具栏:皮肤切换 + 主题/语言并排靠右;窄屏放不下时换行仍右对齐,不横向溢出。 */
.login-topbar {
  position: fixed;
  top: 20px;
  right: 24px;
  z-index: 10;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 10px;
}
/* 全局工具药丸,与皮肤切换器同款磨砂,自带对比,三底色皆可读 */
.login-tools {
  display: flex;
  gap: 2px;
  padding: 4px;
  border-radius: 999px;
  background: rgba(18, 20, 28, 0.55);
  border: 1px solid rgba(255, 255, 255, 0.14);
  backdrop-filter: blur(14px);
  -webkit-backdrop-filter: blur(14px);
  box-shadow: 0 6px 20px rgba(0, 0, 0, 0.28);
}
.tool-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 34px;
  height: 30px;
  padding: 0 10px;
  border: none;
  cursor: pointer;
  border-radius: 999px;
  background: transparent;
  font-size: 13px;
  font-weight: 600;
  color: rgba(255, 255, 255, 0.62);
  transition:
    color var(--transition-fast),
    background var(--transition-fast);
}
.tool-btn:hover {
  color: #fff;
}
.tool-btn:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}
.skin-picker {
  color: #fff;
  background: rgba(18, 20, 28, 0.55);
  height: 38px;
  width: 38px;
}
.login-shell-brand {
  height: auto;
  min-height: 100svh;
  overflow: visible;
}
.login-shell-brand .login-topbar {
  position: absolute;
  top: 30px;
  right: clamp(24px, 4vw, 60px);
  gap: 12px;
}
.login-shell-brand .login-tools {
  background: transparent;
  border: 0;
  padding: 0;
  box-shadow: none;
  backdrop-filter: none;
}
.login-shell-brand .tool-btn {
  background: transparent;
  color: var(--color-text-secondary);
  width: 36px;
  height: 36px;
  border-radius: var(--radius-sm);
  padding: 0;
}
.login-shell-brand .tool-btn:hover { background: var(--color-fill-hover); color: var(--color-text-primary); }
.login-shell-brand .skin-picker { border-right: 1px solid var(--color-border); border-radius: 0; padding-right: 10px; width: 42px; }
@media (max-width: 900px) {
  .login-shell-brand .login-topbar { top: 26px; right: 20px; gap: 4px; }
}
@media (max-width: 380px) {
  .login-shell-brand .tool-btn { width: 30px; }
  .login-shell-brand .skin-picker { width: 34px; }
}
</style>
