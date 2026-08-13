<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { NTooltip } from 'naive-ui'
import { Icon } from '@iconify/vue'
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

const skin = ref(initSkin())
watch(skin, (v) => localStorage.setItem(KEY, v))

const active = computed(() => LOGIN_SKINS.find((s) => s.id === skin.value) ?? LOGIN_SKINS[0])
</script>

<template>
  <div class="login-shell">
    <component :is="active.component" />

    <!-- 右上角工具栏:皮肤切换 + 主题/语言,三枚磨砂药丸并排(主题/语言贴最右角)。 -->
    <div class="login-topbar">
      <!-- 皮肤切换器:深色磨砂药丸,在明/暗/极光三种底色上都可读 -->
      <div class="skin-switch" role="tablist" :aria-label="t('app.loginStyle')">
        <button
          v-for="s in LOGIN_SKINS"
          :key="s.id"
          class="skin-seg"
          :class="{ active: s.id === skin }"
          type="button"
          role="tab"
          :aria-selected="s.id === skin"
          @click="skin = s.id"
        >
          {{ t(s.label) }}
        </button>
      </div>

      <!-- 全局工具:主题 + 语言。深色磨砂药丸,在明/暗/极光三种底色上都可读,三种皮肤通用。 -->
      <div class="login-tools">
        <n-tooltip>
          <template #trigger>
            <button
              class="tool-btn"
              type="button"
              :aria-label="app.isDark ? t('app.light') : t('app.dark')"
              @click="app.toggleDark()"
            >
              <Icon :icon="app.isDark ? 'ph:moon-stars' : 'ph:sun'" :width="18" />
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
    color 0.2s,
    background 0.2s;
}
.tool-btn:hover {
  color: #fff;
}
.tool-btn:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}
/* ponytail: 固定深色药丸自带对比,纯白 spotlight 右上角也够读;若极端不够再补浅色 scrim */
.skin-switch {
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
.skin-seg {
  border: none;
  cursor: pointer;
  padding: 6px 16px;
  border-radius: 999px;
  background: transparent;
  font-size: 13px;
  font-weight: 600;
  color: rgba(255, 255, 255, 0.62);
  transition:
    color 0.2s,
    background 0.2s;
}
.skin-seg:hover {
  color: #fff;
}
.skin-seg.active {
  background: var(--color-primary);
  color: #fff;
}
</style>
