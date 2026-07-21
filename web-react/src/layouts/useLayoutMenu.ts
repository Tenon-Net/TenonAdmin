import { createElement, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '@/stores/auth'
import { AppIcon } from '@/components/AppIcon'
import { menuToItems, openIfExternal, openKeysFor, type MenuItem } from './menuItems'

// 图标:`menu.icon` 是 iconify 字符串,经 AppIcon 离线渲染。兜底与 Vue 侧 useLayoutMenu.renderIcon 一致:
// 目录 `ph:folder-duotone`、叶子 `ph:dot-outline-duotone`,尺寸 18。
// 本文件保持 .ts(非 .tsx),故用 createElement 而非 JSX(沿用原作者选型)。
function iconFor(name: string | undefined, isCatalog: boolean): ReactNode {
  return createElement(AppIcon, { icon: name, size: 18, fallback: isCatalog ? 'ph:folder-duotone' : 'ph:dot-outline-duotone' })
}

/**
 * 布局菜单派生 + 选中态/展开态跟随路由。派生规则在 `menuItems.ts`(已单测),这里接反应式数据源:
 * `menuTree`(auth store)+ 当前路由。**只做 vertical**——Vue 的一/二级联动(mix 布局)是后续批次。
 */
export function useLayoutMenu() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const menuTree = useAuthStore((s) => s.menuTree)

  const items: MenuItem[] = useMemo(() => menuToItems(menuTree, t, iconFor), [menuTree, t])

  // openKeys 受控,跟着路由走;但也要允许用户手动收起/展开别的目录,所以 onOpenChange 时用用户的选择。
  const routeOpenKeys = useMemo(() => openKeysFor(items, pathname), [items, pathname])
  const [openKeys, setOpenKeys] = useState<string[]>(routeOpenKeys)
  // 路由变化(或菜单晚到)时把选中项所在目录并入展开集,不覆盖用户已展开的其它目录。
  useEffect(() => {
    setOpenKeys((prev) => Array.from(new Set([...prev, ...routeOpenKeys])))
  }, [routeOpenKeys])

  function onClick(key: string) {
    if (openIfExternal(key)) return // 外链新标签打开,不导航
    if (key.startsWith('/')) navigate(key)
  }

  return { items, selectedKeys: [pathname], openKeys, setOpenKeys, onClick }
}
