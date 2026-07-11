<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { RouterView } from 'vue-router'
import { NConfigProvider, NMessageProvider, NDialogProvider, zhCN, enUS, dateZhCN, dateEnUS } from 'naive-ui'
import { useTheme } from '@/composables/useTheme'
import { useAppStore } from '@/stores/app'
import { i18n } from '@/locales'
import { configApi } from '@/api'

const app = useAppStore()
const { overrides, naiveTheme } = useTheme()

const naiveLocale = computed(() => (app.locale === 'en-US' ? enUS : zhCN))
const naiveDateLocale = computed(() => (app.locale === 'en-US' ? dateEnUS : dateZhCN))

// 站点标题真实生效:启动读匿名站点信息设浏览器标题(sys.site.title 的消费点;登录前也可读)。
onMounted(async () => {
  try {
    const site = await configApi.siteInfo()
    if (site.title) document.title = site.title
  } catch {
    // 站点信息拉取失败不影响应用,保留 index.html 静态标题
  }
})

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
