import { describe, it, expect, beforeEach } from 'vitest'
import { useTabsStore, aliveKeys, type AddTabInput } from './tabs'
import { useAuthStore } from './auth'

// store 是单例:每例前清空标签 + 净化 auth(homePath 决定 affix)+ 清 sessionStorage(persist 回灌)。
beforeEach(() => {
  sessionStorage.clear()
  useAuthStore.setState({ modules: [], currentModuleId: null, menuTree: [] })
  useTabsStore.setState({ tabs: [], reloadKey: 0, excludeKey: '' })
})

const tab = (path: string, extra: Partial<AddTabInput> = {}): AddTabInput => ({ path, fullPath: path, title: path, ...extra })
const s = () => useTabsStore.getState()

describe('useTabsStore addTab / 动态标题 / noCache', () => {
  it('setTitle 置 titleFixed 后,addTab 复访不再用原 title 覆盖', () => {
    s().addTab(tab('/system/user/5/detail', { title: 'common.detail' }))
    s().setTitle('/system/user/5/detail', '张三')
    expect(s().tabs[0]!.title).toBe('张三')
    expect(s().tabs[0]!.titleFixed).toBe(true)
    // 复访同 path:标题保持记录名,不回退到原 title
    s().addTab(tab('/system/user/5/detail', { title: 'common.detail' }))
    expect(s().tabs[0]!.title).toBe('张三')
  })

  it('复访更新 fullPath(含 query),不新增标签', () => {
    s().addTab(tab('/system/user', { fullPath: '/system/user' }))
    s().addTab(tab('/system/user', { fullPath: '/system/user?page=2' }))
    expect(s().tabs).toHaveLength(1)
    expect(s().tabs[0]!.fullPath).toBe('/system/user?page=2')
  })

  it('aliveKeys 排除 noCache 标签', () => {
    s().addTab(tab('/system/user', { title: 'user.title' })) // 普通页 → 缓存
    s().addTab(tab('/system/user/5/detail', { noCache: true })) // 详情 → 不缓存
    expect(s().tabs[1]!.noCache).toBe(true)
    expect(aliveKeys(s().tabs)).toEqual(['/system/user'])
  })
})

describe('useTabsStore 固定标签(pin)', () => {
  it('togglePin 后不可关闭,批量关闭时保留,取消后可关闭', () => {
    s().addTab(tab('/a'))
    s().addTab(tab('/b'))
    s().addTab(tab('/c'))

    s().togglePin('/b')
    expect(s().tabs.find((t) => t.path === '/b')!.pinned).toBe(true)

    // 固定标签不可被 removeTab 关闭(返 null 且保留)
    expect(s().removeTab('/b', '/b')).toBeNull()
    expect(s().tabs.some((t) => t.path === '/b')).toBe(true)

    // closeOthers('/a') 仍保留固定的 /b
    s().closeOthers('/a', '/a')
    expect(
      s()
        .tabs.map((t) => t.path)
        .sort(),
    ).toEqual(['/a', '/b'])

    // 取消固定后可正常关闭
    s().togglePin('/b')
    s().removeTab('/b', '/a')
    expect(s().tabs.some((t) => t.path === '/b')).toBe(false)
  })

  it('应用首页(affix)不可手动固定:togglePin 无效', () => {
    useAuthStore.setState({ menuTree: [{ id: 1, parentId: 0, type: 2, title: '用户', path: '/system/user', sort: 0, visible: true, children: [] }] })
    s().addTab(tab('/system/user')) // homePath = 首个叶子 → affix
    expect(s().tabs[0]!.affix).toBe(true)
    s().togglePin('/system/user')
    expect(s().tabs[0]!.pinned).toBeFalsy() // affix 恒固定,不接受手动 pin
  })
})

describe('useTabsStore removeTab 导航意图', () => {
  it('关当前(中间)标签 → 返回补位的右邻 fullPath', () => {
    s().addTab(tab('/a', { fullPath: '/a' }))
    s().addTab(tab('/b', { fullPath: '/b' }))
    s().addTab(tab('/c', { fullPath: '/c' }))
    expect(s().removeTab('/b', '/b')).toBe('/c') // /c 补进 /b 的位置
    expect(s().tabs.map((t) => t.path)).toEqual(['/a', '/c'])
  })

  it('关当前的末尾标签 → 返回左邻', () => {
    s().addTab(tab('/a'))
    s().addTab(tab('/b'))
    expect(s().removeTab('/b', '/b')).toBe('/a')
  })

  it('关非当前标签 → 不导航(返 null),标签仍被移除', () => {
    s().addTab(tab('/a'))
    s().addTab(tab('/b'))
    expect(s().removeTab('/a', '/b')).toBeNull() // 当前在 /b,关掉 /a 不该导航
    expect(s().tabs.map((t) => t.path)).toEqual(['/b'])
  })
})

describe('useTabsStore 批量关闭导航意图', () => {
  it('closeOthers:当前不在保留集 → 导航到 preferPath', () => {
    s().addTab(tab('/a'))
    s().addTab(tab('/b'))
    s().addTab(tab('/c'))
    // 当前在 /c,关掉除 /a 外的其它 → /c 没了,导航到 /a
    expect(s().closeOthers('/a', '/c')).toBe('/a')
    expect(s().tabs.map((t) => t.path)).toEqual(['/a'])
  })

  it('closeRight:当前仍保留 → 不导航(返 null)', () => {
    s().addTab(tab('/a'))
    s().addTab(tab('/b'))
    s().addTab(tab('/c'))
    // 关 /a 右侧,当前在 /a 仍在 → 不导航
    expect(s().closeRight('/a', '/a')).toBeNull()
    expect(s().tabs.map((t) => t.path)).toEqual(['/a'])
  })

  it('closeLeft:idx<=0 无左可关 → null 且不动', () => {
    s().addTab(tab('/a'))
    s().addTab(tab('/b'))
    expect(s().closeLeft('/a', '/a')).toBeNull()
    expect(s().tabs).toHaveLength(2)
  })

  it('closeLeft:idx>0 删左侧(无 affix/pinned 全删),当前在左被删 → 导航到 path', () => {
    s().addTab(tab('/a'))
    s().addTab(tab('/b'))
    s().addTab(tab('/c'))
    // 关 /c 左侧:/a /b 被删、/c 保留(过滤器 i >= idx);当前在 /a(将被删)→ 导航到 /c
    expect(s().closeLeft('/c', '/a')).toBe('/c')
    expect(s().tabs.map((t) => t.path)).toEqual(['/c'])
  })

  it('closeAll:保留 affix/pinned,当前被关 → 导航到首页', () => {
    // homePath 无模块时回落 '/module';把它做成 affix 首页
    s().addTab(tab('/module'))
    s().addTab(tab('/x'))
    expect(s().tabs[0]!.affix).toBe(true) // /module = homePath → affix
    expect(s().closeAll('/x')).toBe('/module') // /x 被关,导航回 affix 首页
    expect(s().tabs.map((t) => t.path)).toEqual(['/module'])
  })
})

describe('useTabsStore affix(应用首页)', () => {
  it('homePath 页 = affix,removeTab 不可关(返 null)', () => {
    useAuthStore.setState({ menuTree: [{ id: 1, parentId: 0, type: 2, title: '用户', path: '/system/user', sort: 0, visible: true, children: [] }] })
    // 无 defaultRoute 时 homePath = 菜单首个叶子 = /system/user
    s().addTab(tab('/system/user'))
    expect(s().tabs[0]!.affix).toBe(true)
    expect(s().removeTab('/system/user', '/system/user')).toBeNull()
    expect(s().tabs).toHaveLength(1)
  })
})

describe('useTabsStore refreshTab / clearTabs', () => {
  it('refreshTab 置 excludeKey + 递增 reloadKey;clearExclude 复位', () => {
    s().addTab(tab('/a'))
    const before = s().reloadKey
    s().refreshTab('/a')
    expect(s().excludeKey).toBe('/a')
    expect(s().reloadKey).toBe(before + 1)
    s().clearExclude()
    expect(s().excludeKey).toBe('')
  })

  it('clearTabs 清空', () => {
    s().addTab(tab('/a'))
    s().addTab(tab('/b'))
    s().clearTabs()
    expect(s().tabs).toEqual([])
  })
})
