<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { RouterView } from 'vue-router'
import { NConfigProvider, NMessageProvider, NDialogProvider, NLoadingBarProvider, zhCN, enUS, dateZhCN, dateEnUS } from 'naive-ui'
import { LoadingBarBridge } from '@/lib/loadingBar'
import { useTheme } from '@/composables/useTheme'
import { useAppStore } from '@/stores/app'
import { i18n } from '@/locales'
import { useSite } from '@/composables/useSite'
import ReauthModal from '@/components/ReauthModal.vue'

const app = useAppStore()
const { overrides, naiveTheme } = useTheme()
const { site, loadSite } = useSite()

const naiveLocale = computed(() => (app.locale === 'en-US' ? enUS : zhCN))
const naiveDateLocale = computed(() => (app.locale === 'en-US' ? dateEnUS : dateZhCN))

// 站点品牌真实生效:启动拉一次匿名站点信息(全站共用),并设浏览器标题(sys.site.title 消费点)。
onMounted(async () => {
  await loadSite()
  if (site.title) document.title = site.title
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
    <n-loading-bar-provider>
      <LoadingBarBridge />
      <n-message-provider>
        <n-dialog-provider>
          <router-view />
          <!-- Level3 高危写 40024:client 中间件唤起,须挂在 message/dialog 之内 -->
          <ReauthModal />
        </n-dialog-provider>
      </n-message-provider>
    </n-loading-bar-provider>
  </n-config-provider>
</template>
