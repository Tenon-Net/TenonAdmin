import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, h, shallowRef, type App } from 'vue'
import { getIcon, Icon } from '@iconify/vue'
import { getCollections, isRegistered, loadIconNames } from 'tenon-naive-iconify-picker'
import AppIcon from '@/components/AppIcon.vue'
import BrandIcon from '@/components/oauth/BrandIcon.vue'
import { setupIcons } from './icons'

let app: App<Element> | undefined
afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

describe('setupIcons', () => {
  it('常用图标无需加载整库，任意菜单图标和选择器完整目录仍可离线使用', async () => {
    setupIcons()
    expect(getCollections().map((item) => item.prefix)).toEqual(['lucide', 'ph', 'ep', 'ant-design'])

    const selected = shallowRef('ph:floppy-disk')
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp({ render: () => h('div', [
      h(AppIcon, { icon: selected.value }),
      h(AppIcon, { icon: '  ' }),
      h(BrandIcon, { code: 'custom-provider', icon: 'ph:link-simple' }),
    ]) })
    app.mount(host)
    await vi.waitFor(() => expect(host.querySelectorAll('svg')).toHaveLength(3))
    expect(isRegistered('ph')).toBe(false)
    expect(isRegistered('lucide')).toBe(false)
    expect(getIcon('ph:flow-arrow-duotone')).toBeTruthy()
    expect(getIcon('ph:monitor-duotone')).toBeTruthy()

    selected.value = 'ph:airplane'
    await vi.waitFor(() => {
      expect(getIcon('ph:airplane')).toBeTruthy()
      expect(host.querySelectorAll('svg')).toHaveLength(3)
    })
    expect(isRegistered('ph')).toBe(true)
    const names = await loadIconNames('ph')
    expect(names.length).toBeGreaterThan(1000)
    expect(names).toContain('airplane')
  })

  it('保留本地 SVG 渲染', async () => {
    setupIcons()
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp({ render: () => h(AppIcon, { icon: 'local:star' }) })
    app.mount(host)
    await vi.waitFor(() => expect(host.querySelector('svg')).not.toBeNull())
  })

  it('does not call the network when an icon is missing from the bundle', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
    setupIcons()
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp({ render: () => h(Icon, { icon: 'mdi:home' }) })
    app.mount(host)
    await new Promise((resolve) => setTimeout(resolve, 30))
    expect(fetchSpy).not.toHaveBeenCalled()
    fetchSpy.mockRestore()
  })
})
