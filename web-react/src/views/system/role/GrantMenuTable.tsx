// 授权菜单三列网格(目录|菜单|按钮)+ 三态复选。按钮已从主菜单树剥离(在 ButtonManager 管),
// 但授权是「角色能看/能点什么」,故这里把按钮拉回来一并勾。纯逻辑在 grantMenu.ts(变异钉),本组件只接线 + 渲染。
// 对齐 Vue 侧 GrantMenuTable.vue(那边就地改 reactive,React 走不可变更新 + 受控上抛)。
import { useEffect, useMemo, useState } from 'react'
import { Button, Checkbox, Input, Select } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import type { MenuTreeNode } from '@/types/menu'
import type { ModuleRow } from '@/types/api'
import {
  UNASSIGNED, buildGroups, collectChecked, filterByModule, filterBySearch,
  withButtonChecked, withCatalogChecked, withMenuChecked, type CatalogGroup,
} from './grantMenu'
import './GrantMenuTable.css'

export interface GrantMenuTableProps {
  tree: MenuTreeNode[]
  granted: number[]
  modules: ModuleRow[]
  defaultModuleId: number
  onCheckedChange: (ids: number[]) => void
}

export function GrantMenuTable({ tree, granted, modules, defaultModuleId, onCheckedChange }: GrantMenuTableProps) {
  const { t } = useTranslation()
  const [groups, setGroups] = useState<CatalogGroup[]>([])
  const [moduleId, setModuleId] = useState(defaultModuleId)
  const [search, setSearch] = useState('')
  const [collapsed, setCollapsed] = useState<Set<number>>(new Set())

  // tree/granted 变(打开抽屉/换角色)→ 重建组;不上抛(父已持有 granted 初值)。
  useEffect(() => { setGroups(buildGroups(tree, granted)) }, [tree, granted])
  // 父传的默认应用变(openMenus 盖当前应用)→ 同步筛选。
  useEffect(() => { setModuleId(defaultModuleId) }, [defaultModuleId])

  const moduleOptions = useMemo(
    () => [...modules.map((m) => ({ label: m.title, value: m.id })), { label: t('menu.moduleUnassigned'), value: UNASSIGNED }],
    [modules, t],
  )
  const filtered = useMemo(() => filterBySearch(filterByModule(groups, moduleId), search), [groups, moduleId, search])

  // 切换:从当前 groups 算出新数组 → setGroups + 上抛 collectChecked(用户事件,闭包 groups 恒新鲜)。
  const apply = (next: CatalogGroup[]) => { setGroups(next); onCheckedChange(collectChecked(next)) }
  const onCatalog = (id: number, val: boolean) => apply(groups.map((g) => (g.id === id ? withCatalogChecked(g, val) : g)))
  const onMenu = (gid: number, mid: number, val: boolean) => apply(groups.map((g) => (g.id === gid ? withMenuChecked(g, mid, val) : g)))
  const onButton = (gid: number, mid: number, bid: number, val: boolean) => apply(groups.map((g) => (g.id === gid ? withButtonChecked(g, mid, bid, val) : g)))

  const toggleCollapse = (id: number) =>
    setCollapsed((prev) => { const n = new Set(prev); if (n.has(id)) n.delete(id); else n.add(id); return n })
  const allCollapsed = filtered.length > 0 && filtered.every((g) => collapsed.has(g.id))
  const toggleCollapseAll = () => setCollapsed(allCollapsed ? new Set() : new Set(filtered.map((g) => g.id)))

  return (
    <div className="grant-menu-table">
      <div className="grant-toolbar">
        <Select value={moduleId} onChange={setModuleId} options={moduleOptions} size="small" style={{ width: 180 }} placeholder={t('menu.module')} />
        <Input value={search} onChange={(e) => setSearch(e.target.value)} allowClear size="small" placeholder={t('common.search')} style={{ flex: 1 }} />
        <Button size="small" type="text" onClick={toggleCollapseAll}
          icon={<AppIcon icon={allCollapsed ? 'ph:arrows-out-line-vertical' : 'ph:arrows-in-line-vertical'} size={16} />}>
          {allCollapsed ? t('common.expandAll') : t('common.collapseAll')}
        </Button>
      </div>

      <div className="grant-grid">
        <div className="grant-header">
          <div className="col-catalog">{t('role.catalog')}</div>
          <div className="col-menu">{t('role.menuCol')}</div>
          <div className="col-buttons">{t('role.buttonsCol')}</div>
        </div>

        {filtered.map((group) => (
          <div className="grant-group" key={group.id}>
            <div className="col-catalog" onClick={() => toggleCollapse(group.id)}>
              <span className={`collapse-arrow${collapsed.has(group.id) ? ' is-collapsed' : ''}`}>▾</span>
              {/* 勾选不该顺带折叠 → stopPropagation */}
              <span onClick={(e) => e.stopPropagation()}>
                <Checkbox checked={group.checked} indeterminate={group.indeterminate} onChange={(e) => onCatalog(group.id, e.target.checked)}>
                  {group.title}
                </Checkbox>
              </span>
            </div>
            {!collapsed.has(group.id) && (
              <div className="group-rows">
                {group.menus.map((menu) => (
                  <div className="grant-row" key={menu.id}>
                    <div className="col-menu">
                      <Checkbox checked={menu.checked} onChange={(e) => onMenu(group.id, menu.id, e.target.checked)}>{menu.title}</Checkbox>
                    </div>
                    <div className="col-buttons">
                      {menu.buttons.map((btn) => (
                        <Checkbox key={btn.id} checked={btn.checked} onChange={(e) => onButton(group.id, menu.id, btn.id, e.target.checked)}>{btn.title}</Checkbox>
                      ))}
                      {menu.buttons.length === 0 && <span className="no-buttons">—</span>}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        ))}
        {filtered.length === 0 && <div className="grant-empty">—</div>}
      </div>
    </div>
  )
}
