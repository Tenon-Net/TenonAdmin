import { computed, h, ref, watch } from 'vue'
import type { MenuOption } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { createSharedComposable } from '@vueuse/core'
import { router } from '@/router'
import { useAuthStore } from '@/stores/auth'
import { useAppStore } from '@/stores/app'
import { t } from '@/locales'
import { MenuType, type MenuNode } from '@/types/menu'

function renderIcon(name?: string) {
  return name ? () => h(Icon, { icon: name, width: 18, height: 18 }) : undefined
}

// 菜单树 → n-menu options:剥按钮、丢空目录、页面叶子 key=路由 path。
function toOptions(nodes: MenuNode[]): MenuOption[] {
  return [...nodes]
    .sort((a, b) => a.sort - b.sort)
    .filter((n) => n.type !== MenuType.Button && n.visible !== false)
    .map<MenuOption | null>((n) => {
      if (n.type === MenuType.Catalog) {
        const children = toOptions(n.children ?? [])
        if (!children.length) return null
        return { label: n.title, key: `cat-${n.id}`, icon: renderIcon(n.icon), children }
      }
      return { label: n.title, key: n.path ?? `menu-${n.id}`, icon: renderIcon(n.icon) }
    })
    .filter((o): o is MenuOption => o !== null)
}

function containsKey(opt: MenuOption, path: string): boolean {
  if (opt.key === path) return true
  return ((opt.children as MenuOption[]) ?? []).some((c) => containsKey(c, path))
}
function firstLeafKey(opt: MenuOption): string | undefined {
  if (typeof opt.key === 'string' && opt.key.startsWith('/')) return opt.key
  for (const c of (opt.children as MenuOption[]) ?? []) {
    const k = firstLeafKey(c)
    if (k) return k
  }
  return undefined
}

/**
 * 布局菜单派生 + 一/二级选中态联动(混合布局用)。
 * createSharedComposable → default.vue 与 AppHeader 共享同一份 selectedL1,不各自漂移。
 * 走 router.currentRoute(全局 ref),不依赖组件上下文的 useRoute。
 */
function useLayoutMenuImpl() {
  const auth = useAuthStore()
  const app = useAppStore()

  const menuOptions = computed<MenuOption[]>(() => {
    void app.locale // 依赖:切换语言时工作台标签重算
    return [
      { label: t('menu.workbench'), key: '/workbench', icon: renderIcon('ph:squares-four-duotone') },
      ...toOptions(auth.menuTree),
    ]
  })

  // 一级(剥 children → n-menu 渲染为可选普通项,无展开箭头)。
  const l1Options = computed<MenuOption[]>(() =>
    menuOptions.value.map((o) => ({ label: o.label, key: o.key, icon: o.icon })),
  )

  const activeKey = computed(() => router.currentRoute.value.path)
  const selectedL1 = ref<string>()

  function findActiveL1(path: string): string | undefined {
    const top = menuOptions.value.find((o) => containsKey(o, path))
    return top ? String(top.key) : undefined
  }

  // 路由 → 一级同步:命中才覆写(off-menu 路由如 /workbench、/personal/* 保留上次);首次 seed 头一项。
  watch(
    () => router.currentRoute.value.path,
    (path) => {
      const l1 = findActiveL1(path)
      if (l1) selectedL1.value = l1
      else if (selectedL1.value === undefined) selectedL1.value = menuOptions.value[0]?.key as string | undefined
    },
    { immediate: true },
  )
  // 菜单可能晚于首个 watch 到达(F5 重建):补 seed / 修正失效选择。
  watch(menuOptions, (opts) => {
    if (selectedL1.value === undefined || !opts.some((o) => o.key === selectedL1.value)) {
      selectedL1.value = findActiveL1(activeKey.value) ?? (opts[0]?.key as string | undefined)
    }
  })

  const l2Options = computed<MenuOption[]>(() => {
    const top = menuOptions.value.find((o) => o.key === selectedL1.value)
    return (top?.children as MenuOption[]) ?? []
  })

  function onSelect(key: string) {
    if (key.startsWith('/')) router.push(key)
  }
  function onSelectL1(key: string) {
    selectedL1.value = key // 先设,L2 列立即刷新,不等导航
    const top = menuOptions.value.find((o) => o.key === key)
    const target = key.startsWith('/') ? key : top ? firstLeafKey(top) : undefined
    if (target) router.push(target)
  }

  return { menuOptions, l1Options, l2Options, selectedL1, activeKey, onSelect, onSelectL1 }
}

export const useLayoutMenu = createSharedComposable(useLayoutMenuImpl)
