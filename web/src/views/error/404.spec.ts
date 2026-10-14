import { afterEach, describe, expect, it } from 'vitest'
import { createApp, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import zhCN from '@/locales/zh-CN'
import NotFound from './404.vue'

let app: App<Element> | undefined

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

async function mountAt(path: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:pathMatch(.*)*', component: NotFound }],
  })
  const host = document.createElement('div')
  document.body.append(host)
  app = createApp(NotFound)
  app.use(createPinia())
  app.use(router)
  app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
  await router.push(path)
  app.mount(host)
  return host
}

describe('not found page', () => {
  it('shows 403 for the designer url and 404 for an unknown path', async () => {
    const denied = await mountAt('/workflow/definition/designer')
    expect(denied.textContent).toContain('403')
    expect(denied.textContent).toContain('没有访问权限')
    app?.unmount()

    const missing = await mountAt('/no-such-page')
    expect(missing.textContent).toContain('404')
    expect(missing.textContent).toContain('页面不存在')
  })
})
