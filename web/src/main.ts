import { createApp } from 'vue'
import { createPinia } from 'pinia'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'
import { usePreferredDark } from '@vueuse/core'
import App from './App.vue'
import { router } from './router'
import { i18n } from './locales'
import { vAuth } from './directives/auth'
import { useAppStore } from './stores/app'
import './styles/tokens.css'
import './styles/index.css'

const pinia = createPinia()
pinia.use(piniaPluginPersistedstate)

const app = createApp(App)
app.use(pinia) // 必须在 router 之前:守卫用到 store

// 首次访问(无持久化偏好)跟随系统深浅色;用户手动设过后由持久化接管
if (localStorage.getItem('app') === null) {
  useAppStore().dark = usePreferredDark().value
}
app.use(router)
app.use(i18n)
app.directive('auth', vAuth)
app.mount('#app')
