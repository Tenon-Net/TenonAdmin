import { describe, it, expect } from 'vitest'
import { MenuType, type MenuTreeNode } from '@/types/menu'
import {
  UNASSIGNED, blankMenu, buildButtonInfo, belongsToApp, defaultTitleKey, menuRowToInput, routeSeg, stripButtons, subtreeIds,
} from './menuForm'

// 造树:目录(1)>页面(2,挂 2 个按钮)+ 页面(3,无按钮);按钮 4/5 挂 2 下。
const btn = (id: number, parentId: number, permission: string): MenuTreeNode =>
  ({ id, parentId, type: MenuType.Button, title: `b${id}`, permission, sort: 0, enabled: true, moduleId: null, path: null, component: null, icon: null, visible: true, children: [] })
const node = (id: number, parentId: number, type: MenuType, over: Partial<MenuTreeNode>, children: MenuTreeNode[] = []): MenuTreeNode =>
  ({ id, parentId, type, title: `n${id}`, permission: '', sort: 0, enabled: true, moduleId: null, path: null, component: null, icon: null, visible: true, children, ...over })

const TREE: MenuTreeNode[] = [
  node(1, 0, MenuType.Catalog, { moduleId: 10 }, [
    node(2, 1, MenuType.Menu, { path: '/sys/user' }, [btn(4, 2, 'GET:/api/v1/sys/user'), btn(5, 2, 'POST:/api/v1/sys/user/add')]),
    node(3, 1, MenuType.Menu, { path: '/sys/role' }),
  ]),
  node(6, 0, MenuType.Catalog, { moduleId: null }), // 未分配顶级
]

describe('blankMenu', () => {
  it('默认:parentId 0、type 页面、moduleId null、空串字段、sort 0、启用、可见', () => {
    expect(blankMenu()).toEqual({ parentId: 0, type: MenuType.Menu, title: '', permission: '', sort: 0, enabled: true, moduleId: null, path: '', component: '', icon: '', visible: true })
  })
  it('入参覆盖 parentId/moduleId/type(按钮场景)', () => {
    const b = blankMenu(2, null, MenuType.Button)
    expect(b.parentId).toBe(2)
    expect(b.type).toBe(MenuType.Button)
  })
  it('顶级新建可带 moduleId', () => {
    expect(blankMenu(0, 10).moduleId).toBe(10)
  })
})

describe('menuRowToInput', () => {
  it('全量映射每字段(漏一个即抹空该字段)', () => {
    expect(menuRowToInput(TREE[0].children[0])).toEqual({
      parentId: 1, type: MenuType.Menu, title: 'n2', permission: '', sort: 0, enabled: true, moduleId: null,
      path: '/sys/user', component: '', icon: '', visible: true,
    })
  })
  it('moduleId 归一 null、可空字段归一空串', () => {
    const r = menuRowToInput(node(9, 0, MenuType.Catalog, { moduleId: 10, path: null, component: null, icon: null }))
    expect(r.moduleId).toBe(10)
    expect(r.path).toBe('')
    expect(r.component).toBe('')
    expect(r.icon).toBe('')
  })
})

describe('stripButtons', () => {
  it('递归剔除按钮,保留目录/页面', () => {
    const s = stripButtons(TREE)
    const page2 = s[0].children[0]
    expect(page2.children).toEqual([]) // 两个按钮被剥
    expect(s[0].children.map((c) => c.id)).toEqual([2, 3])
    expect(s.map((n) => n.id)).toEqual([1, 6])
  })
})

describe('buildButtonInfo', () => {
  it('直属按钮 count + perms 小写拼串', () => {
    const m = buildButtonInfo(TREE)
    expect(m.get(2)).toEqual({ count: 2, perms: 'get:/api/v1/sys/user post:/api/v1/sys/user/add' })
    expect(m.get(3)).toEqual({ count: 0, perms: '' })
  })
})

describe('subtreeIds', () => {
  it('收集自身 + 全部后代 id', () => {
    expect([...subtreeIds(TREE, 1)].sort((a, b) => a - b)).toEqual([1, 2, 3, 4, 5])
  })
  it('叶子只含自身', () => {
    expect([...subtreeIds(TREE, 3)]).toEqual([3])
  })
})

describe('routeSeg', () => {
  it('去首尾斜杠 + 空白;空/undefined → 空串', () => {
    expect(routeSeg('/sys/')).toBe('sys')
    expect(routeSeg('  biz  ')).toBe('biz')
    expect(routeSeg(undefined)).toBe('')
    expect(routeSeg('')).toBe('')
  })
})

describe('belongsToApp', () => {
  it('段空 → 全属', () => {
    expect(belongsToApp('/api/v1/x', '')).toBe(true)
  })
  it('path 含 /seg/ 或以 /seg 结尾', () => {
    expect(belongsToApp('/api/v1/sys/user', 'sys')).toBe(true)
    expect(belongsToApp('/api/v1/sys', 'sys')).toBe(true)
    expect(belongsToApp('/api/v1/biz/x', 'sys')).toBe(false)
  })
})

describe('defaultTitleKey', () => {
  it('方法 → i18n 键;未知 → null', () => {
    expect(defaultTitleKey('get')).toBe('menu.btnQuery')
    expect(defaultTitleKey('POST')).toBe('menu.btnCreate')
    expect(defaultTitleKey('PUT')).toBe('menu.btnUpdate')
    expect(defaultTitleKey('patch')).toBe('menu.btnUpdate')
    expect(defaultTitleKey('DELETE')).toBe('menu.btnDelete')
    expect(defaultTitleKey('HEAD')).toBeNull()
  })
})

describe('UNASSIGNED', () => {
  it('哨兵为 0', () => { expect(UNASSIGNED).toBe(0) })
})
