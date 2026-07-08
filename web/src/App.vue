<script setup lang="ts">
import { computed, watch } from 'vue'
import { RouterView } from 'vue-router'
import { NConfigProvider, NMessageProvider, NDialogProvider, zhCN, enUS, dateZhCN, dateEnUS } from 'naive-ui'
import { useTheme } from '@/composables/useTheme'
import { useAppStore } from '@/stores/app'
import { i18n } from '@/locales'

const app = useAppStore()
const { overrides, naiveTheme } = useTheme()

const naiveLocale = computed(() => (app.locale === 'en-US' ? enUS : zhCN))
const naiveDateLocale = computed(() => (app.locale === 'en-US' ? dateEnUS : dateZhCN))

// i18n 语言跟随 app.locale。
watch(
  () => app.locale,
  (l) => {
    i18n.global.locale.value = l
  },
  { immediate: true },
)
</script>

<template>
  <n-config-provider
    :theme="naiveTheme"
    :theme-overrides="overrides"
    :locale="naiveLocale"
    :date-locale="naiveDateLocale"
  >
    <n-message-provider>
      <n-dialog-provider>
        <router-view />
      </n-dialog-provider>
    </n-message-provider>
  </n-config-provider>
</template>
