import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useAppStore } from '@/stores/app'
import { t } from '@/locales'
import { MenuType, type MenuNode } from '@/types/menu'

export interface MenuLeaf {
  title: string // 原始标题(后端串或 i18n key)
  path: string
  icon?: string
  breadcrumb: string[] // 从根到叶的标题链
}

/** auth.menuTree 扁平为叶子(供面包屑与全局搜索共用),复刻 toOptions 的按钮/隐藏/排序规则 + 注入工作台。 */
export function useMenuFlat() {
  const auth = useAuthStore()
  const app = useAppStore()

  return computed<MenuLeaf[]>(() => {
    void app.locale // 依赖:切换语言时工作台标签重算
    const out: MenuLeaf[] = [
      { title: t('menu.workbench'), path: '/workbench', icon: 'ph:squares-four-duotone', breadcrumb: [t('menu.workbench')] },
    ]
    const tr = (s: string) => (s.includes('.') ? t(s) : s) // 标题可能是 i18n key,面包屑与 label()/title 一致翻译
    const walk = (nodes: MenuNode[], trail: string[]) => {
      for (const n of [...nodes].sort((a, b) => a.sort - b.sort)) {
        if (n.type === MenuType.Button || n.visible === false) continue
        const here = [...trail, tr(n.title)]
        if (n.type === MenuType.Menu && n.path) out.push({ title: n.title, path: n.path, icon: n.icon, breadcrumb: here })
        if (n.children?.length) walk(n.children, here)
      }
    }
    walk(auth.menuTree, [])
    return out
  })
}
