import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Can } from './Can'
import { useAuthStore } from '@/stores/auth'

// 判定规则本身在 auth.hasPerm(B3 已变异钉死),这里只验 Can 把规则接对 + 数组 OR/AND。
function set(over: Partial<{ isSuperAdmin: boolean; permissionsLoaded: boolean; permissionCodes: string[] }>) {
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [], ...over })
}

beforeEach(() => useAuthStore.getState().reset())
afterEach(cleanup)

const child = <span data-testid="gated">OK</span>
const shown = () => screen.queryByTestId('gated') !== null

describe('Can', () => {
  it('命中权限码 → 渲染子节点', () => {
    set({ permissionCodes: ['POST:/x'] })
    render(<Can code="POST:/x">{child}</Can>)
    expect(shown()).toBe(true)
  })

  it('不命中 → 不渲染', () => {
    set({ permissionCodes: ['GET:/y'] })
    render(<Can code="POST:/x">{child}</Can>)
    expect(shown()).toBe(false)
  })

  it('超管 fail-open:空码集也全放行', () => {
    set({ isSuperAdmin: true, permissionCodes: [] })
    render(<Can code="POST:/x">{child}</Can>)
    expect(shown()).toBe(true)
  })

  it('未加载 fail-closed:permissionsLoaded=false → 藏(不谎报有)', () => {
    set({ permissionsLoaded: false, permissionCodes: ['POST:/x'] })
    render(<Can code="POST:/x">{child}</Can>)
    expect(shown()).toBe(false)
  })

  it('数组默认 OR:命中任一即显', () => {
    set({ permissionCodes: ['B'] })
    render(<Can code={['A', 'B']}>{child}</Can>)
    expect(shown()).toBe(true)
  })

  it('数组默认 OR:一个都不命中 → 藏', () => {
    set({ permissionCodes: ['C'] })
    render(<Can code={['A', 'B']}>{child}</Can>)
    expect(shown()).toBe(false)
  })

  it('every(AND):缺一个即藏', () => {
    set({ permissionCodes: ['A'] })
    render(<Can code={['A', 'B']} every>{child}</Can>)
    expect(shown()).toBe(false)
  })

  it('every(AND):全命中才显', () => {
    set({ permissionCodes: ['A', 'B'] })
    render(<Can code={['A', 'B']} every>{child}</Can>)
    expect(shown()).toBe(true)
  })
})
