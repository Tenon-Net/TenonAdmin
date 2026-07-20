import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { renderHook, cleanup, act } from '@testing-library/react'
import { MenuType } from '@/types/menu'
import { useAuthStore, useHasPerm, useHomePath } from './auth'

// globals: false,所以 RTL 的自动清理不会自己注册。
afterEach(cleanup)
beforeEach(() => {
  useAuthStore.getState().reset()
  localStorage.clear()
})

/**
 * 这一组守的是本次移植的**核心设计决策**:`hasPerm`/`homePath` 写成纯函数 + 细粒度 hook,
 * 而不是 store 里"返回闭包的选择器"。zustand 每次渲染都跑选择器并与上次结果 `Object.is` 比对,
 * 返回新建函数 = 每次都"变了" = 无限重渲染。
 *
 * 那条论证此前只写在注释与 commit message 里,**没有任何东西钉住它** ——
 * 谁把它改回 store 里的 getter,纯函数那批用例照样全绿(它们根本不渲染)。
 */
describe('useHasPerm', () => {
  it('无关字段写入**根本不触发重渲染**(细粒度订阅的实际性质)', () => {
    // 注意断言的是**渲染次数**,不是函数引用。一开始我写的是"引用不变",而那条**抓不住这个 bug**:
    // 把三条细选择器换成整体 `useAuthStore()` 后,解构出的三个值引用依旧没变,`useCallback` 的依赖
    // 也就没变,函数引用照样稳定 —— 变异跑出来是绿的。真正会退化的是重渲染次数:整体订阅下
    // menuTree 一动就产生新 state 对象,每个带权限门的按钮跟着白渲染一遍。
    let renders = 0
    const { result } = renderHook(() => {
      renders++
      return useHasPerm()
    })
    const before = renders
    const fn = result.current

    act(() => {
      useAuthStore.setState({
        menuTree: [{ id: 1, parentId: 0, type: MenuType.Menu, title: 'a', path: '/a', sort: 0, visible: true, children: [] }],
        routesReady: true,
      })
    })

    expect(renders - before, 'menuTree/routesReady 与权限判定无关,不该引起重渲染').toBe(0)
    expect(result.current, '既然没重渲染,函数引用自然也不变').toBe(fn)
  })

  it('权限码变了才换引用,且判定结果跟着变', () => {
    const { result } = renderHook(() => useHasPerm())
    expect(result.current('GET:/api/v1/x')).toBe(false)
    const before = result.current

    act(() => useAuthStore.setState({ permissionsLoaded: true, permissionCodes: ['GET:/api/v1/x'] }))

    expect(result.current).not.toBe(before)
    expect(result.current('GET:/api/v1/x')).toBe(true)
    expect(result.current('GET:/api/v1/y')).toBe(false)
  })

  it('渲染次数不随 store 写入无限增长(选择器返回新函数就会在这里炸)', () => {
    // 这条原先只调了一次 `rerender()`,**一次 store 写入都没有** —— 而它声称守的失败模式
    // (选择器返回新建函数 → 每次比对都"变了" → 无限重渲染)走的是 store **订阅通知**那条路径,
    // 手动 rerender 根本碰不到。名不副实的用例比没有更坏:它让人以为这个坑被守着。
    let renders = 0
    renderHook(() => {
      renders++
      return useHasPerm()
    })
    const afterMount = renders

    // 三次真写入,每次都动被订阅的字段。选择器实现正确时:一次写入 = 一次重渲染。
    for (const codes of [['A'], ['A', 'B'], ['A', 'B', 'C']]) {
      act(() => useAuthStore.setState({ permissionCodes: codes }))
    }
    // 计数假定**没有 StrictMode wrapper**(实测 StrictMode 下 mount 2 / updates 6)。
    // 哪天有人给 spec 套上贴合生产的 StrictMode,这条会假红成 6 —— 那时改成断"有界"而不是"恰好"。
    expect(renders - afterMount).toBe(3)
  })

  it('写入不改变值时不重渲染(zustand 按 Object.is 比对,数组要换引用才算变)', () => {
    let renders = 0
    renderHook(() => {
      renders++
      return useHasPerm()
    })
    const afterMount = renders
    const same = useAuthStore.getState().permissionCodes
    act(() => useAuthStore.setState({ permissionCodes: same })) // 同一个引用写回去
    expect(renders - afterMount).toBe(0)
  })
})

describe('useHomePath', () => {
  it('跟随 store 变化,且返回的是字符串(按值比较,不会有新引用导致的循环)', () => {
    const { result } = renderHook(() => useHomePath())
    expect(result.current).toBe('/module')

    act(() => useAuthStore.setState({
      modules: [{ id: 1, code: 'x', title: 'X', sort: 0, defaultRoute: '/foo' }],
      currentModuleId: 1,
    }))

    expect(result.current).toBe('/foo')
  })
})
