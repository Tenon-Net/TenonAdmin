import { describe, it, expect, vi, afterEach } from 'vitest'
import { menuToItems, openIfExternal, openKeysFor, type MenuItem } from './menuItems'
import { MenuType, type MenuNode } from '@/types/menu'

function node(p: Partial<MenuNode> & { id: number }): MenuNode {
  return { parentId: 0, type: MenuType.Menu, title: `t${p.id}`, sort: 0, visible: true, children: [], ...p }
}

// tr 回显包一层,好断言"到底翻没翻";iconFor 回显图标名,好断言图标名有没有传进去。
const tr = (k: string) => `T(${k})`
const iconFor = (name: string | undefined, isCatalog: boolean) => `icon:${name ?? '_'}:${isCatalog ? 'cat' : 'leaf'}`
const build = (tree: MenuNode[]) => menuToItems(tree, tr, iconFor)

describe('menuToItems', () => {
  it('页面叶子:key=path,label 原样(不含 . 不翻译),带图标', () => {
    const r = build([node({ id: 7, path: '/system/user', title: '用户', icon: 'ph:user' })])
    expect(r).toEqual<MenuItem[]>([{ key: '/system/user', label: '用户', icon: 'icon:ph:user:leaf' }])
  })

  it('按 sort 升序(不是输入顺序)', () => {
    const r = build([
      node({ id: 1, path: '/a', sort: 3 }),
      node({ id: 2, path: '/b', sort: 1 }),
      node({ id: 3, path: '/c', sort: 2 }),
    ])
    expect(r.map((i) => i.key)).toEqual(['/b', '/c', '/a'])
  })

  it('Button 不进菜单', () => {
    const r = build([node({ id: 1, type: MenuType.Button, path: '/x' }), node({ id: 2, path: '/y' })])
    expect(r.map((i) => i.key)).toEqual(['/y'])
  })

  it('visible=false 不进菜单', () => {
    const r = build([node({ id: 1, path: '/x', visible: false }), node({ id: 2, path: '/y' })])
    expect(r.map((i) => i.key)).toEqual(['/y'])
  })

  it('Catalog 递归子菜单,key=cat-${id}', () => {
    const r = build([
      node({ id: 1, type: MenuType.Catalog, title: '系统', icon: 'ph:folder', children: [node({ id: 2, path: '/system/user' })] }),
    ])
    expect(r).toEqual<MenuItem[]>([
      { key: 'cat-1', label: '系统', icon: 'icon:ph:folder:cat', children: [{ key: '/system/user', label: 't2', icon: 'icon:_:leaf' }] },
    ])
  })

  it('无可见子项的空目录整条丢弃(不出现)', () => {
    const r = build([
      node({ id: 1, type: MenuType.Catalog, children: [] }), // 空
      node({ id: 2, type: MenuType.Catalog, children: [node({ id: 3, type: MenuType.Button, path: '/b' })] }), // 只有按钮 → 剥空
      node({ id: 4, path: '/keep' }),
    ])
    expect(r.map((i) => i.key)).toEqual(['/keep']) // 两个空目录都没了
  })

  it('标题含 . → 走 i18n', () => {
    const r = build([node({ id: 1, path: '/x', title: 'menu.dashboard' })])
    expect(r[0]!.label).toBe('T(menu.dashboard)')
  })

  it('**外链叶子照常入菜单**,key=URL(与 buildRoutes 跳过外链相反)', () => {
    // 外链在路由表里被跳过(buildRoutes),但在菜单里要出现、点得动 —— 这里 key 就是那个 URL。
    const r = build([node({ id: 1, path: 'https://ex.com/docs', title: '文档' })])
    expect(r).toEqual<MenuItem[]>([{ key: 'https://ex.com/docs', label: '文档', icon: 'icon:_:leaf' }])
  })
})

describe('openKeysFor(子菜单跟随路由展开)', () => {
  const items: MenuItem[] = [
    { key: 'cat-1', label: '系统', children: [
      { key: '/system/user', label: 'u' },
      { key: 'cat-2', label: '子', children: [{ key: '/system/sub/x', label: 'x' }] },
    ] },
    { key: '/dashboard', label: 'd' },
  ]

  it('叶子在一级目录下 → 展开那个目录', () => {
    expect(openKeysFor(items, '/system/user')).toEqual(['cat-1'])
  })

  it('叶子在嵌套目录下 → 展开整条祖先链', () => {
    expect(openKeysFor(items, '/system/sub/x')).toEqual(['cat-1', 'cat-2'])
  })

  it('顶层叶子(无祖先目录)→ 空', () => {
    expect(openKeysFor(items, '/dashboard')).toEqual([])
  })

  it('off-menu 路由(不在菜单里)→ 空,不强行展开', () => {
    expect(openKeysFor(items, '/personal/profile')).toEqual([])
  })
})

describe('openIfExternal', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('http(s) key → window.open 并返回 true', () => {
    const open = vi.fn()
    vi.stubGlobal('window', { open })
    expect(openIfExternal('https://ex.com')).toBe(true)
    expect(open).toHaveBeenCalledWith('https://ex.com', '_blank', 'noopener,noreferrer')
  })

  it('内部路径 → 返回 false,不 open(交给调用方 navigate)', () => {
    const open = vi.fn()
    vi.stubGlobal('window', { open })
    expect(openIfExternal('/system/user')).toBe(false)
    expect(open).not.toHaveBeenCalled()
  })
})
