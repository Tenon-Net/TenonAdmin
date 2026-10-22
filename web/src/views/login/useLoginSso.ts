import { computed, h, ref } from 'vue'
import type { DropdownOption } from 'naive-ui'
import { externalAuthApi } from '@/api'
import BrandIcon from '@/components/oauth/BrandIcon.vue'
import { PREVIEW_ALL_SSO_BRANDS, previewAllBrandProviders, splitLoginProviders } from '@/utils/oauthBrand'

/** SSO provider 加载与展示策略；登录协调器只处理登录完成后的绑定/导航。 */
export function useLoginSso() {
  const providers = ref<{ code: string; displayName: string; icon?: string | null }[]>([])
  const displayList = computed(() => PREVIEW_ALL_SSO_BRANDS ? previewAllBrandProviders() : providers.value)
  const split = computed(() => PREVIEW_ALL_SSO_BRANDS
    ? { visible: displayList.value, overflow: [] as typeof displayList.value }
    : splitLoginProviders(displayList.value))
  const overflowOptions = computed<DropdownOption[]>(() => split.value.overflow.map((provider) => ({
    key: provider.code,
    label: () => h('span', {
      class: 'lf-sso-menu-item',
      style: { display: 'inline-flex', alignItems: 'center', gap: '8px' },
    }, [h(BrandIcon, { code: provider.code, icon: provider.icon, size: 20 }), h('span', null, provider.displayName)]),
  })))

  async function load() {
    try {
      providers.value = await externalAuthApi.providers()
    } catch {
      providers.value = []
    }
  }

  function href(code: string) {
    return externalAuthApi.authorizeUrl(code)
  }

  function selectOverflow(key: string | number) {
    window.location.href = href(String(key))
  }

  return { providers, displayList, split, overflowOptions, load, href, selectOverflow }
}
