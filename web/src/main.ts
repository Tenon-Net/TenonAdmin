import { createApp } from 'vue'
import { createPinia } from 'pinia'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'
import App from './App.vue'
import { router } from './router'
import { i18n } from './locales'
import { vAuth } from './directives/auth'
import './styles/tokens.css'
import './styles/index.css'

const pinia = createPinia()
pinia.use(piniaPluginPersistedstate)

const app = createApp(App)
app.use(pinia) // 必须在 router 之前:守卫用到 store
app.use(router)
app.use(i18n)
app.directive('auth', vAuth)
app.mount('#app')
