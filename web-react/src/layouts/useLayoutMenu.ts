import { createElement, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '@/stores/auth'
import { AppIcon } from '@/components/AppIcon'
import { menuToItems, openIfExternal, l1Of, l2Of, findActiveL1, firstLeafKey, type MenuItem } from './menuItems'

// 图标:`menu.icon` 是 iconify 字符串,经 AppIcon 离线渲染。兜底与 Vue 侧 useLayoutMenu.renderIcon 一致:
// 目录 `ph:folder-duotone`、叶子 `ph:dot-outline-duotone`,尺寸 18。
// 本文件保持 .ts(非 .tsx),故用 createElement 而非 JSX(沿用原作者选型)。
function iconFor(name: string | undefined, isCatalog: boolean): ReactNode {
  return createElement(AppIcon, { icon: name, size: 18, fallback: isCatalog ? 'ph:folder-duotone' : 'ph:dot-outline-duotone' })
}

/**
 * 布局菜单派生 + 一/二级选中态联动(多布局模式用)。派生规则(含 L1/L2)在 `menuItems.ts`(已单测),
 * 这里接反应式数据源:`menuTree`(auth store)+ 当前路由。`openKeys`(展开态)已下放到各 `SideNav` 自管。
 *
 * 对齐 Vue `composables/useLayoutMenu.ts`:`selectedL1` 跟随路由,off-menu 路由(如 /personal/*)保留上次;
 * 菜单晚到(F5 重建)或选择失效时补种/修正。**LayoutShell 是唯一调用方**,故 `selectedL1` 用组件级 useState 即等价于共享。
 */
export function useLayoutMenu() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const menuTree = useAuthStore((s) => s.menuTree)

  const items: MenuItem[] = useMemo(() => menuToItems(menuTree, t, iconFor), [menuTree, t])
  const l1Items = useMemo(() => l1Of(items), [items])

  const activeL1 = useMemo(() => findActiveL1(items, pathname), [items, pathname])
  const [selectedL1, setSelectedL1] = useState<string | undefined>(activeL1 ?? items[0]?.key)
  useEffect(() => {
    setSelectedL1((prev) => {
      if (activeL1) return activeL1 // 命中当前路由的一级 → 覆写
      if (prev && items.some((i) => i.key === prev)) return prev // off-menu:保留仍有效的上次
      return items[0]?.key // 首次种 / 失效回落头一项
    })
  }, [activeL1, items])

  const l2Items = useMemo(() => l2Of(items, selectedL1), [items, selectedL1])

  const onSelect = (key: string) => {
    if (openIfExternal(key)) return // 外链新标签打开,不导航
    if (key.startsWith('/')) navigate(key)
  }
  const onSelectL1 = (key: string) => {
    if (openIfExternal(key)) return
    setSelectedL1(key) // 先设,L2 列立即刷新,不等导航
    const top = items.find((i) => i.key === key)
    const target = key.startsWith('/') ? key : top ? firstLeafKey(top) : undefined
    if (target) navigate(target)
  }

  return { items, l1Items, l2Items, selectedL1, activeKey: pathname, onSelect, onSelectL1 }
}
