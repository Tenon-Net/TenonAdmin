import { useMemo, useState, useEffect, type ReactNode } from 'react'
import { FolderOutlined, AppstoreOutlined } from '@ant-design/icons'
import { createElement } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '@/stores/auth'
import { menuToItems, openIfExternal, openKeysFor, type MenuItem } from './menuItems'

// 图标:`menu.icon` 是 iconify 字符串,本模板未引 iconify —— 目录/叶子各用一个通用图标占位。
// 图标映射(iconify → 组件)留后续批次,别当已做完(与选择页 AppstoreOutlined 占位同一笔待办)。
function iconFor(_name: string | undefined, isCatalog: boolean): ReactNode {
  return createElement(isCatalog ? FolderOutlined : AppstoreOutlined)
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
