import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter, useRoutes } from 'react-router-dom'
import { buildRoutes, viewKeysFrom, type ViewGlob } from './buildRoutes'
import { MenuType, type MenuNode } from '@/types/menu'

function node(p: Partial<MenuNode> & { id: number }): MenuNode {
  return { parentId: 0, type: MenuType.Menu, title: `t${p.id}`, sort: 0, visible: true, children: [], ...p }
}

// 假 glob:每个页面渲染可辨识的文字。键的形状与真实 `import.meta.glob` 一致。
const glob: ViewGlob = {
  '/src/views/system/user/index.tsx': async () => ({ default: () => <div>USER PAGE</div> }),
  '/src/views/dashboard/index.tsx': async () => ({ default: () => <div>DASH</div> }),
  '/src/views/login/LoginPage.tsx': async () => ({ default: () => <div>LOGIN</div> }),
}

/** 把 buildRoutes 的结果喂 useRoutes,在给定 URL 下渲染。 */
function Harness({ tree, at }: { tree: MenuNode[]; at: string }) {
  const routes = buildRoutes(tree, glob)
  return <MemoryRouter initialEntries={[at]}>{<Routed routes={routes} />}</MemoryRouter>
}
function Routed({ routes }: { routes: ReturnType<typeof buildRoutes> }) {
  return useRoutes(routes)
}

afterEach(cleanup)

describe('buildRoutes 落地', () => {
  it('view 描述符 → 该路径渲染出对应页面(lazy 加载)', async () => {
    render(<Harness tree={[node({ id: 7, path: 'system/user', component: 'system/user/index' })]} at="/system/user" />)
    expect(await screen.findByText('USER PAGE')).toBeTruthy()
  })

  it('iframe 描述符 → 渲染 IframeView,src 落到 iframe 的 src 上', () => {
    const { container } = render(
      <Harness tree={[node({ id: 5, path: 'embed/r', component: 'https://ex.com/r' })]} at="/embed/r" />,
    )
    const iframe = container.querySelector('iframe')
    expect(iframe).not.toBeNull()
    expect(iframe!.getAttribute('src')).toBe('https://ex.com/r')
  })

  it('handle 带上 name/title/icon(留给 B8 布局壳读)', () => {
    const routes = buildRoutes([node({ id: 7, path: 'system/user', component: 'system/user/index', title: '用户', icon: 'user' })], glob)
    expect(routes[0]!.handle).toEqual({ name: 'menu-7', title: '用户', icon: 'user' })
    expect(routes[0]!.path).toBe('/system/user')
  })

  it('校验存在与取 loader 是同一个真相源:glob 里没有的组件不建路由,有的能渲染', async () => {
    // 'system/role/index' 不在上面的假 glob 里 → menuToRouteDescriptors 的 hasView 判它不存在 → 跳过。
    const routes = buildRoutes(
      [
        node({ id: 1, path: 'system/role', component: 'system/role/index' }), // 缺
        node({ id: 2, path: 'dashboard', component: 'dashboard/index' }), // 有
      ],
      glob,
    )
    expect(routes.map((r) => r.path)).toEqual(['/dashboard']) // 只剩存在的那条
  })
})

describe('真实 glob(不注入)——堵住"假 glob 掩盖真 glob 失效"的缺口', () => {
  it('buildRoutes 用默认 glob 时,能解析到仓库里真实存在的页面文件', () => {
    // 上面所有用例都注入假 glob,真实 `import.meta.glob(['/src/views/**/*.tsx','!...spec'])` 若因
    // 语法/版本问题匹配为空,一条测试都不会红,而真实 app 会**零动态路由**。这条特意不传 glob,
    // 用一条指向真文件(`src/views/system/user/index.tsx`,B6b-1 建的占位页)的菜单,证明那条负模式
    // 数组 glob 在本 Vite 版本下确实解析到了文件、建出了路由。
    const routes = buildRoutes([node({ id: 1, path: 'system/user', component: 'system/user/index' })])
    expect(routes.map((r) => r.path)).toEqual(['/system/user'])
  })

  it('buildRoutes 用默认 glob 时,不存在的组件不建路由(反面对照,排除"恒建")', () => {
    const routes = buildRoutes([node({ id: 1, path: 'x', component: '压根不存在/index' })])
    expect(routes).toEqual([])
  })
})

describe('viewKeysFrom(菜单管理的组件下拉真相源)', () => {
  it('glob 键 → component 值(剥前缀后缀),排除登录页,排序', () => {
    expect(viewKeysFrom(glob)).toEqual(['dashboard/index', 'system/user/index'])
    // login/LoginPage 被排除:它是静态路由,不是菜单能配的落点。
    expect(viewKeysFrom(glob)).not.toContain('login/LoginPage')
  })
})
