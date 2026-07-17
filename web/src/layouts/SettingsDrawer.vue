<script setup lang="ts">
import {
  NDrawer,
  NDrawerContent,
  NTabs,
  NTabPane,
  NDivider,
  NRadioGroup,
  NRadioButton,
  NColorPicker,
  NSwitch,
  NSelect,
  NInput,
  NPopconfirm,
  NButton,
} from 'naive-ui'
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { useMessage } from 'naive-ui'
import { useWindowSize, useClipboard } from '@vueuse/core'
import { useAppStore } from '@/stores/app'
import { ACCENTS } from '@/theme/accents'
import SettingRow from './SettingRow.vue'
import LayoutModeCards from './LayoutModeCards.vue'

const show = defineModel<boolean>('show', { default: false })
const app = useAppStore()
const { t } = useI18n()
const message = useMessage()
const { copy, isSupported } = useClipboard()

// 复制当前外观配置(消费者 fork 后粘回 stores/app.ts 的 DEFAULTS 即为新默认)。
async function copyConfig() {
  await copy(JSON.stringify(app.exportSettings(), null, 2))
  message.success(t('settings.configCopied'))
}

// 窄屏不溢出:抽屉宽度取 min(400, 90vw)。加宽给布局卡片两列留出余量。
const { width } = useWindowSize()
const drawerWidth = computed(() => Math.min(400, Math.round(width.value * 0.9)))

// computed:t() 一次求值不随语言切换更新,故包 computed 让下拉标签跟随 locale。
const transitionOptions = computed(() => [
  { label: t('settings.transitionNone'), value: 'none' },
  { label: t('settings.transitionFade'), value: 'fade' },
  { label: t('settings.transitionFadeSlide'), value: 'fade-slide' },
])
</script>

<template>
  <n-drawer v-model:show="show" :width="drawerWidth" placement="right">
    <n-drawer-content :title="t('settings.title')" closable :native-scrollbar="false">
      <n-tabs type="segment" size="small" animated class="tabs">
        <!-- 外观 -->
        <n-tab-pane name="appearance" :tab="t('settings.tabAppearance')">
          <n-divider title-placement="left">{{ t('settings.themeMode') }}</n-divider>
          <n-radio-group v-model:value="app.themeScheme" size="small" class="seg">
            <n-radio-button value="light">
              <Icon icon="ph:sun" class="ri" />{{ t('app.light') }}
            </n-radio-button>
            <n-radio-button value="dark">
              <Icon icon="ph:moon-stars" class="ri" />{{ t('app.dark') }}
            </n-radio-button>
            <n-radio-button value="auto">
              <Icon icon="ph:circle-half" class="ri" />{{ t('settings.auto') }}
            </n-radio-button>
          </n-radio-group>

          <n-divider title-placement="left">{{ t('settings.themeColor') }}</n-divider>
          <div class="accents">
            <button
              v-for="c in ACCENTS"
              :key="c"
              type="button"
              class="dot"
              :class="{ on: app.accent === c }"
              :style="{ background: c }"
              :aria-label="`${t('settings.themeColor')} ${c}`"
              :aria-pressed="app.accent === c"
              @click="app.setAccent(c)"
            >
              <Icon v-if="app.accent === c" icon="ph:check-bold" class="tick" />
            </button>
          </div>
          <n-color-picker
            :value="app.accent"
            :modes="['hex']"
            :show-alpha="false"
            :swatches="[...ACCENTS]"
            @update:value="app.setAccent"
          />

          <n-divider title-placement="left">{{ t('app.density') }}</n-divider>
          <n-radio-group v-model:value="app.density" size="small" class="seg">
            <n-radio-button value="comfortable">{{ t('app.comfortable') }}</n-radio-button>
            <n-radio-button value="compact">{{ t('app.compact') }}</n-radio-button>
          </n-radio-group>
        </n-tab-pane>

        <!-- 布局 -->
        <n-tab-pane name="layout" :tab="t('settings.tabLayout')">
          <n-divider title-placement="left">{{ t('settings.layout') }}</n-divider>
          <LayoutModeCards />

          <n-divider title-placement="left">{{ t('settings.interface') }}</n-divider>
          <SettingRow :label="t('settings.showBreadcrumb')">
            <n-switch v-model:value="app.showBreadcrumb" size="small" />
          </SettingRow>
          <SettingRow :label="t('settings.showTabs')">
            <n-switch v-model:value="app.showTabs" size="small" />
          </SettingRow>
          <SettingRow :label="t('settings.fixedHeader')">
            <n-switch v-model:value="app.fixedHeader" size="small" />
          </SettingRow>
          <SettingRow :label="t('settings.pageTransition')">
            <n-select
              v-model:value="app.pageTransition"
              :options="transitionOptions"
              size="small"
              style="width: 120px"
            />
          </SettingRow>
          <SettingRow :label="t('settings.grayscale')">
            <n-switch v-model:value="app.grayscale" size="small" />
          </SettingRow>
          <SettingRow :label="t('app.collapse')">
            <n-switch v-model:value="app.collapsed" size="small" />
          </SettingRow>
        </n-tab-pane>

        <!-- 通用 -->
        <n-tab-pane name="general" :tab="t('settings.tabGeneral')">
          <n-divider title-placement="left">{{ t('settings.watermark') }}</n-divider>
          <SettingRow :label="t('settings.watermark')">
            <n-switch v-model:value="app.watermark" size="small" />
          </SettingRow>
          <SettingRow v-if="app.watermark" :label="t('settings.watermarkText')">
            <n-input
              v-model:value="app.watermarkText"
              size="small"
              :placeholder="t('settings.watermarkPlaceholder')"
              style="width: 140px"
            />
          </SettingRow>

          <n-divider title-placement="left">{{ t('settings.formStyle') }}</n-divider>
          <!-- FormContainer 全局形态:所有 CRUD 表单弹层一键在弹窗/抽屉间切换 -->
          <n-radio-group v-model:value="app.formStyle" size="small" class="seg">
            <n-radio-button value="modal">{{ t('settings.formStyleModal') }}</n-radio-button>
            <n-radio-button value="drawer">{{ t('settings.formStyleDrawer') }}</n-radio-button>
          </n-radio-group>
        </n-tab-pane>
      </n-tabs>

      <template #footer>
        <div class="footer-actions">
          <n-button v-if="isSupported" block tertiary @click="copyConfig">
            <Icon icon="ph:clipboard-text" class="ri" />{{ t('settings.copyConfig') }}
          </n-button>
          <n-popconfirm @positive-click="app.resetSettings()">
            <template #trigger>
              <n-button block secondary>
                <Icon icon="ph:arrow-counter-clockwise" class="ri" />{{ t('common.reset') }}
              </n-button>
            </template>
            {{ t('settings.resetConfirm') }}
          </n-popconfirm>
        </div>
      </template>
    </n-drawer-content>
  </n-drawer>
</template>

<style scoped>
.tabs {
  height: 100%;
}
.footer-actions {
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: 100%;
}
.ri {
  margin-right: 5px;
  font-size: 15px;
}

/* 满宽三等分分段控件(主题模式 / 密度) */
.seg {
  display: flex;
  width: 100%;
}
.seg :deep(.n-radio-button) {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}

/* 精致色板 */
.accents {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 10px;
  margin-bottom: 12px;
}
.dot {
  position: relative;
  width: 100%;
  max-width: 30px;
  margin: 0 auto;
  aspect-ratio: 1;
  padding: 0;
  border: none;
  border-radius: 50%;
  cursor: pointer;
  box-shadow: inset 0 0 0 1px rgba(0, 0, 0, 0.08);
  display: flex;
  align-items: center;
  justify-content: center;
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}
.dot:hover {
  transform: scale(1.12);
}
.dot.on {
  transform: scale(1.1);
  box-shadow: 0 0 0 2px var(--color-bg-container), 0 0 0 4px var(--color-primary);
}
.tick {
  color: #fff;
  font-size: 14px;
  filter: drop-shadow(0 0 1px rgba(0, 0, 0, 0.35));
}
</style>
