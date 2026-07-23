import { computed, createApp } from 'vue'
import { createPinia } from 'pinia'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'
import { PRO_TABLE_DEFAULTS, createProTableDefaults } from 'tenon-naive-pro-table'
import App from './App.vue'
import { router } from './router'
import { i18n } from './locales'
import { vAuth } from './directives/auth'
import { setupIcons } from './lib/icons'
import { setupMarkdown } from './lib/markdown'
import './styles/tokens.css'
import './styles/index.css'

const pinia = createPinia()
pinia.use(piniaPluginPersistedstate)

const app = createApp(App)
app.use(pinia) // 必须在 router 之前:守卫用到 store
// 深浅色默认 themeScheme:'auto' → 首访自动跟随系统,用户手选 light/dark 后固定(见 stores/app.ts)。
app.use(router)
app.use(i18n)
app.directive('auth', vAuth)

// ProTable 全局默认:labels 一次注入,各页面不再手传 :labels(切语言即时生效)。
app.provide(
  PRO_TABLE_DEFAULTS,
  createProTableDefaults({
    labels: computed(() => {
      void i18n.global.locale.value // 触发 locale 依赖收集
      const t = i18n.global.t
      return {
        search: t('common.search'),
        reset: t('common.reset'),
        refresh: t('proTable.refresh'),
        density: t('app.density'),
        densityComfortable: t('app.comfortable'),
        densityCompact: t('app.compact'),
        columnSettings: t('proTable.columnSettings'),
        columnSettingsReset: t('proTable.columnSettingsReset'),
        fixedLeft: t('proTable.fixedLeft'),
        fixedRight: t('proTable.fixedRight'),
        fixedNone: t('proTable.fixedNone'),
        expand: t('proTable.expand'),
        collapse: t('proTable.collapse'),
      }
    }),
  }),
)
setupIcons() // 注册离线图标集 + 本地 SVG,预热 ph(非阻塞:注册后 <Icon> 从本地渲染,不命中外部 CDN)
setupMarkdown() // 全局给 md-editor-v3 挂 XSS 过滤 + echarts 空 instance(通知正文富文本,默认放行内联 HTML 且会拉 unpkg)
app.mount('#app')
