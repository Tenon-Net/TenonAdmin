import { describe, expect, it } from 'vitest'
import { describeMenuRoute } from './authMenuRoute'
import { MenuType, type MenuNode } from '@/types/menu'

function menu(overrides: Partial<MenuNode> = {}): MenuNode {
  return {
    id: 17,
    parentId: 0,
    type: MenuType.Menu,
    title: 'Broken page',
    path: '/system/broken',
    component: 'system/broken/index',
    icon: 'ph:warning',
    sort: 0,
    visible: true,
    children: [],
    ...overrides,
  }
}

describe('describeMenuRoute', () => {
  it('keeps a missing component route as a diagnosable page', () => {
    expect(describeMenuRoute(menu(), new Set())).toEqual({
      kind: 'missing',
      path: '/system/broken',
      name: 'menu-17',
      title: 'Broken page',
      icon: 'ph:warning',
      component: 'system/broken/index',
    })
  })

  it('preserves external, iframe, empty-component, and view behavior', () => {
    const paths = new Set(['/src/views/system/user/index.vue'])

    expect(describeMenuRoute(menu({ path: 'https://example.test', component: '' }), paths)).toBeNull()
    expect(describeMenuRoute(menu({ component: 'https://example.test/docs' }), paths)).toMatchObject({ kind: 'iframe' })
    expect(describeMenuRoute(menu({ component: '' }), paths)).toBeNull()
    expect(describeMenuRoute(menu({ component: 'system/user/index' }), paths)).toMatchObject({ kind: 'view' })
  })
})
