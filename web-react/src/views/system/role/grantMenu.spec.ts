import { describe, it, expect } from 'vitest'
import { MenuType, type MenuTreeNode } from '@/types/menu'
import {
  buildGroups, collectChecked, filterByModule, filterBySearch, groupState,
  withButtonChecked, withCatalogChecked, withMenuChecked,
} from './grantMenu'

const btn = (id: number, parentId: number, title: string): MenuTreeNode =>
  ({ id, parentId, type: MenuType.Button, title, permission: '', sort: 0, enabled: true, moduleId: null, path: null, component: null, icon: null, visible: true, children: [] })
const node = (id: number, parentId: number, type: MenuType, title: string, over: Partial<MenuTreeNode>, children: MenuTreeNode[] = []): MenuTreeNode =>
  ({ id, parentId, type, title, permission: '', sort: 0, enabled: true, moduleId: null, path: null, component: null, icon: null, visible: true, children, ...over })

// 目录1(mod10) > [菜单2>[btn4,btn5], 菜单3]; 顶级菜单6(mod10)>[btn7]; 目录8(未分配) > [菜单9]
const TREE: MenuTreeNode[] = [
  node(1, 0, MenuType.Catalog, '系统', { moduleId: 10 }, [
    node(2, 1, MenuType.Menu, '用户', {}, [btn(4, 2, '查'), btn(5, 2, '增')]),
    node(3, 1, MenuType.Menu, '角色', {}),
  ]),
  node(6, 0, MenuType.Menu, '工作台', { moduleId: 10 }, [btn(7, 6, '刷新')]),
  node(8, 0, MenuType.Catalog, '未分配', { moduleId: null }, [node(9, 8, MenuType.Menu, '孤页', {})]),
]

describe('buildGroups', () => {
  it('目录取菜单子、顶级菜单自成行;granted 落到各级 checked;moduleId 带上', () => {
    const g = buildGroups(TREE, [2, 4])
    expect(g.map((x) => x.id)).toEqual([1, 6, 8])
    const g1 = g[0]
    expect(g1.moduleId).toBe(10)
    expect(g1.menus.map((m) => m.id)).toEqual([2, 3])
    expect(g1.menus[0].checked).toBe(true) // 菜单2 granted
    expect(g1.menus[0].buttons.map((b) => [b.id, b.checked])).toEqual([[4, true], [5, false]])
    // 2/4 项勾(菜单2 + btn4),总 4(菜单2/btn4/btn5/菜单3)→ 半选
    expect(g1.checked).toBe(false)
    expect(g1.indeterminate).toBe(true)
    // 顶级菜单6 自成一行
    expect(g[1].menus.map((m) => m.id)).toEqual([6])
    expect(g[2].moduleId).toBeNull()
  })
})

describe('groupState', () => {
  const mk = (checked: boolean, btns: boolean[]) => ({ id: 1, title: '', checked, buttons: btns.map((c, i) => ({ id: i, title: '', checked: c })) })
  it('全勾→checked;部分→indeterminate;全不勾→都 false', () => {
    expect(groupState([mk(true, [true])])).toEqual({ checked: true, indeterminate: false })
    expect(groupState([mk(true, [false])])).toEqual({ checked: false, indeterminate: true })
    expect(groupState([mk(false, [false])])).toEqual({ checked: false, indeterminate: false })
  })
})

describe('withCatalogChecked', () => {
  it('目录勾 → 其下菜单 + 全部按钮一并勾;checked=val、indeterminate=false', () => {
    const g = withCatalogChecked(buildGroups(TREE, [])[0], true)
    expect(g.checked).toBe(true); expect(g.indeterminate).toBe(false)
    expect(g.menus.every((m) => m.checked && m.buttons.every((b) => b.checked))).toBe(true)
  })
})

describe('withMenuChecked', () => {
  it('菜单勾 → 该菜单 + 其按钮勾;目录三态重算', () => {
    const g = withMenuChecked(buildGroups(TREE, [])[0], 2, true)
    const m2 = g.menus.find((m) => m.id === 2)!
    expect(m2.checked).toBe(true)
    expect(m2.buttons.every((b) => b.checked)).toBe(true)
    expect(g.indeterminate).toBe(true) // 菜单3 未勾 → 半选
  })
})

describe('withButtonChecked', () => {
  it('按钮全勾 → 连带菜单勾', () => {
    let g = buildGroups(TREE, [])[0]
    g = withButtonChecked(g, 2, 4, true)
    expect(g.menus.find((m) => m.id === 2)!.checked).toBe(false) // 还差 btn5
    g = withButtonChecked(g, 2, 5, true)
    expect(g.menus.find((m) => m.id === 2)!.checked).toBe(true) // 全勾 → 菜单勾
  })
  it('取消按钮不取消菜单(对齐 Vue)', () => {
    let g = withCatalogChecked(buildGroups(TREE, [])[0], true) // 全勾
    g = withButtonChecked(g, 2, 4, false)
    expect(g.menus.find((m) => m.id === 2)!.checked).toBe(true) // 菜单仍勾
  })
})

describe('collectChecked', () => {
  it('目录(勾或半勾)+ 菜单(勾)+ 按钮(勾)全收', () => {
    expect(collectChecked(buildGroups(TREE, [2, 4])).sort((a, b) => a - b)).toEqual([1, 2, 4])
  })
})

describe('filterByModule', () => {
  it('按 moduleId 过滤;UNASSIGNED(0)归拢 null', () => {
    const g = buildGroups(TREE, [])
    expect(filterByModule(g, 10).map((x) => x.id)).toEqual([1, 6])
    expect(filterByModule(g, 0).map((x) => x.id)).toEqual([8])
  })
})

describe('filterBySearch', () => {
  it('目录名命中→整组;否则留标题/按钮命中的菜单', () => {
    const g = buildGroups(TREE, [])
    expect(filterBySearch(g, '系统').map((x) => x.id)).toEqual([1]) // 目录名命中
    const byBtn = filterBySearch(g, '刷新') // 命中 btn7(菜单6 下)
    expect(byBtn.map((x) => x.id)).toEqual([6])
    const byMenu = filterBySearch([g[0]], '角色') // 命中菜单3
    expect(byMenu[0].menus.map((m) => m.id)).toEqual([3])
  })
})
