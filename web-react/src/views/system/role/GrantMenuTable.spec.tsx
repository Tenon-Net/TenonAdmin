import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { MenuType, type MenuTreeNode } from '@/types/menu'
import type { ModuleRow } from '@/types/api'
import { GrantMenuTable } from './GrantMenuTable'

// GrantMenuTable 用原生 Checkbox/Select/Input(无 ProTable),可真渲染。
const btn = (id: number, parentId: number, title: string): MenuTreeNode =>
  ({ id, parentId, type: MenuType.Button, title, permission: '', sort: 0, enabled: true, moduleId: null, path: null, component: null, icon: null, visible: true, children: [] })
const node = (id: number, parentId: number, type: MenuType, title: string, over: Partial<MenuTreeNode>, children: MenuTreeNode[] = []): MenuTreeNode =>
  ({ id, parentId, type, title, permission: '', sort: 0, enabled: true, moduleId: null, path: null, component: null, icon: null, visible: true, children, ...over })

const TREE: MenuTreeNode[] = [
  node(1, 0, MenuType.Catalog, '系统', { moduleId: 10 }, [
    node(2, 1, MenuType.Menu, '用户', {}, [btn(4, 2, '查'), btn(5, 2, '增')]),
    node(3, 1, MenuType.Menu, '角色', {}),
  ]),
  node(8, 0, MenuType.Catalog, '外部', { moduleId: null }, [node(9, 8, MenuType.Menu, '孤页', {})]),
]
const MODULES: ModuleRow[] = [{ id: 10, code: 'sys', title: '系统应用', apiPrefix: 'sys', sort: 0, enabled: true }]

const onCheckedChange = vi.fn()
const mount = () => render(<AntdApp><GrantMenuTable tree={TREE} granted={[]} modules={MODULES} defaultModuleId={10} onCheckedChange={onCheckedChange} /></AntdApp>)
const lastEmit = () => (onCheckedChange.mock.calls.at(-1)![0] as number[]).slice().sort((a, b) => a - b)

beforeEach(() => onCheckedChange.mockReset())
afterEach(cleanup)

describe('GrantMenuTable', () => {
  it('按默认应用过滤:mod10 组显示,未分配组(孤页)隐藏', () => {
    mount()
    expect(screen.getByRole('checkbox', { name: '系统' })).toBeTruthy()
    expect(screen.getByRole('checkbox', { name: '用户' })).toBeTruthy()
    expect(screen.queryByRole('checkbox', { name: '孤页' })).toBeNull() // 未分配组默认不显示
  })

  it('勾目录 → 连带其下菜单+按钮,上抛全量 id', () => {
    mount()
    fireEvent.click(screen.getByRole('checkbox', { name: '系统' })) // 半选→全选
    expect(lastEmit()).toEqual([1, 2, 3, 4, 5]) // 目录 + 两菜单 + 两按钮
  })

  it('勾菜单 → 连带其按钮,目录半选;上抛 [1,2,4,5]', () => {
    mount()
    fireEvent.click(screen.getByRole('checkbox', { name: '用户' }))
    expect(lastEmit()).toEqual([1, 2, 4, 5]) // 目录1(半选)+ 菜单2 + btn4/5
  })

  it('勾单个按钮 → 上抛含该按钮;目录半选带出目录 id', () => {
    mount()
    fireEvent.click(screen.getByRole('checkbox', { name: '增' })) // btn5
    expect(lastEmit()).toEqual([1, 5]) // 目录1 半选 + btn5
  })

  it('关键字搜索:命中菜单名,其余菜单隐藏', () => {
    mount()
    fireEvent.change(screen.getByPlaceholderText('查询'), { target: { value: '角色' } })
    expect(screen.getByRole('checkbox', { name: '角色' })).toBeTruthy()
    expect(screen.queryByRole('checkbox', { name: '用户' })).toBeNull()
  })
})
