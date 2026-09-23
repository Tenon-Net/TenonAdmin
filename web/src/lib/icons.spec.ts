import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, h, type App } from 'vue'
import { addCollection, Icon } from '@iconify/vue'
import type { SetupOptions } from 'tenon-naive-iconify-picker'
import { setupIcons } from './icons'

const setupIconPicker = vi.hoisted(() => vi.fn())
vi.mock('tenon-naive-iconify-picker', () => ({
  lucideCollection: { prefix: 'lucide', name: 'Lucide', loader: vi.fn() },
  setupIconPicker,
}))

let app: App<Element> | undefined
afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

describe('setupIcons', () => {
  it('preloads the small set and still renders arbitrary Phosphor icons offline on demand', async () => {
    setupIcons()
    const options = setupIconPicker.mock.calls[0]![0] as SetupOptions
    expect(options.collections?.map((item) => item.prefix)).toEqual(['lucide', 'ph', 'ep', 'ant-design'])
    expect(options.preloadPrefix).toBe('lucide')

    const ph = await options.collections!.find((item) => item.prefix === 'ph')!.loader()
    expect(ph.icons?.airplane).toBeTruthy()
    addCollection(ph)

    const host = document.createElement('div')
    document.body.append(host)
    app = createApp({ render: () => h(Icon, { icon: 'ph:airplane' }) })
    app.mount(host)

    await vi.waitFor(() => expect(host.querySelector('svg')).not.toBeNull())
  })
})
