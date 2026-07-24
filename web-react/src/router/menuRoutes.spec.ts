import { describe, it, expect, vi } from 'vitest'
import { menuToRouteDescriptors, type RouteDescriptor } from './menuRoutes'
import { MenuType, type MenuNode } from '@/types/menu'

/** 造节点的糖:只填关心的字段,其余给合法默认。 */
function node(p: Partial<MenuNode> & { id: number }): MenuNode {
  return {
    parentId: 0, type: MenuType.Menu, title: `t${p.id}`, sort: 0, visible: true, children: [],
    ...p,
  }
}

// 所有测试共用:只有这三个视图存在。
const VIEWS = new Set(['system/user/index', 'system/role/index', 'dashboard/index'])
const hasView = (k: string) => VIEWS.has(k)

/** 只跑决策、屏蔽 warn(有专门的用例断 warn)。 */
const build = (tree: MenuNode[]) => menuToRouteDescriptors(tree, hasView, () => {})

describe('menuToRouteDescriptors', () => {
  it('普通 Menu → view 描述符,name/path/component 都对', () => {
    const r = build([node({ id: 7, path: 'system/user', component: 'system/user/index', icon: 'user' })])
    expect(r).toEqual<RouteDescriptor[]>([
      { name: 'menu-7', path: '/system/user', kind: 'view', component: 'system/user/index', title: 't7', icon: 'user' },
    ])
  })

  it('递归打平:嵌套子菜单也建路由', () => {
    const tree = [
      node({
        id: 1, type: MenuType.Catalog, path: 'system', component: '',
        children: [node({ id: 2, path: 'system/user', component: 'system/user/index' })],
      }),
    ]
    const r = build(tree)
    // Catalog 自己不建(见下条),只留子 Menu 那一条。
    expect(r.map((x) => x.name)).toEqual(['menu-2'])
  })

  it('Catalog 目录不建路由(只组织层级)', () => {
    const r = build([node({ id: 1, type: MenuType.Catalog, path: 'system', component: 'system/user/index' })])
    expect(r).toEqual([])
  })

  it('Button 类型不建路由', () => {
    const r = build([node({ id: 1, type: MenuType.Button, path: 'x', component: 'system/user/index' })])
    expect(r).toEqual([])
  })

  it('无 path 的 Menu 跳过', () => {
    const r = build([node({ id: 1, path: undefined, component: 'system/user/index' })])
    expect(r).toEqual([])
  })

  it('相对 path 补前导斜杠;已有斜杠不重复加', () => {
    const r = build([
      node({ id: 1, path: 'a/b', component: 'system/user/index' }),
      node({ id: 2, path: '/c/d', component: 'system/role/index' }),
    ])
    expect(r.map((x) => x.path)).toEqual(['/a/b', '/c/d'])
  })

  it('每条路由 name 唯一且含 id', () => {
    const r = build([
      node({ id: 3, path: 'a', component: 'system/user/index' }),
      node({ id: 9, path: 'b', component: 'system/role/index' }),
    ])
    expect(r.map((x) => x.name)).toEqual(['menu-3', 'menu-9'])
  })
})

describe('外链 / iframe', () => {
  it('外链菜单(path 为 URL)不进路由表 —— 它归 window.open', () => {
    // component 用一个**存在的**视图键,不用空串:空串会被下面的 `!component` 跳过,
    // 于是这条测试对"外链跳过"没有判别力(去掉 isHttpUrl(path) 那句照样绿 —— 实测栽过)。
    // 带上有效 component,外链跳过就是唯一能拦下它的闸,规则才被真正钉住。
    const r = build([node({ id: 1, path: 'https://ex.com', component: 'system/user/index' })])
    expect(r).toEqual([])
  })

  it('component 为 URL → iframe 描述符(带 iframeSrc,无 component)', () => {
    const r = build([node({ id: 5, path: 'embed/report', component: 'https://ex.com/r', title: '报表', icon: 'chart' })])
    expect(r).toEqual<RouteDescriptor[]>([
      { name: 'menu-5', path: '/embed/report', kind: 'iframe', iframeSrc: 'https://ex.com/r', title: '报表', icon: 'chart' },
    ])
  })

  it('普通组件不被误判成 iframe(没有 iframeSrc,有 component)', () => {
    const [d] = build([node({ id: 1, path: 'x', component: 'system/user/index' })])
    expect(d).toMatchObject({ kind: 'view', component: 'system/user/index' })
    expect('iframeSrc' in d!).toBe(false)
  })
})

describe('缺组件:告警且保留可诊断路由', () => {
  it('component 指向不存在的视图 → 保留 missing 描述符和原路径', () => {
    const r = build([node({ id: 1, path: 'x', component: 'system/nope/index' })])
    expect(r).toEqual<RouteDescriptor[]>([
      { name: 'menu-1', path: '/x', kind: 'missing', component: 'system/nope/index', title: 't1', icon: undefined },
    ])
  })

  it('缺组件时保留 MissingRoute 描述符并输出控制台诊断', () => {
    const warn = vi.fn()
    const r = menuToRouteDescriptors([node({ id: 1, path: 'x', component: 'typo/index' })], hasView, warn)
    expect(r).toMatchObject([{ kind: 'missing', path: '/x', component: 'typo/index' }])
    expect(warn).toHaveBeenCalledOnce()
    expect(warn.mock.calls[0]).toContain('typo/index') // 把错的键回给管理员
  })

  it('有 path 但完全无 component 的 Menu → 跳过(且不告警,那不是"打错"是"没配")', () => {
    const warn = vi.fn()
    const r = menuToRouteDescriptors([node({ id: 1, path: 'x', component: '' })], hasView, warn)
    expect(r).toEqual([])
    expect(warn).not.toHaveBeenCalled()
  })

  it('前导斜杠的 component 键被归一后再查表(/system/user/index === system/user/index)', () => {
    const r = build([node({ id: 1, path: 'x', component: '/system/user/index' })])
    expect(r.filter((d): d is Extract<RouteDescriptor, { kind: 'view' }> => d.kind === 'view').map((d) => d.component))
      .toEqual(['system/user/index']) // 存在,建成功
  })
})
