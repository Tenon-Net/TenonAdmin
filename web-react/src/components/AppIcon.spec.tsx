import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, cleanup, waitFor } from '@testing-library/react'
import { addIcon } from '@iconify/react/offline'
import { iconName, AppIcon } from './AppIcon'

// vi.mock 提升执行,mock 须用 vi.hoisted 才能在 factory 里引用。
const { ensureIconLoaded } = vi.hoisted(() => ({
  ensureIconLoaded: vi.fn(async (_name: string) => {
    // 模拟"子集外图标"懒加载完成后才注册 —— 与 ensureIconLoaded 真实现同序。
    addIcon('lazy:box', { body: '<circle cx="12" cy="12" r="10" />', width: 24, height: 24 })
  }),
}))

vi.mock('@/lib/icons', async (orig) => {
  const actual = await orig<typeof import('@/lib/icons')>()
  return {
    ...actual,
    ensureIconLoaded: (name: string) => ensureIconLoaded(name),
  }
})

afterEach(() => {
  cleanup()
  ensureIconLoaded.mockClear()
})

// ── 纯逻辑(变异钉死)──
describe('iconName', () => {
  it('有值用值', () => {
    expect(iconName('ph:folder', 'ph:x')).toBe('ph:folder')
  })
  it('undefined 用兜底', () => {
    expect(iconName(undefined, 'ph:x')).toBe('ph:x')
  })
  it('空串也用兜底(|| 而非 ??)', () => {
    // 这条钉住 `||`:换成 `??` 时空串被当有效值,会返回 ''、断言即红。
    expect(iconName('', 'ph:x')).toBe('ph:x')
  })
})

// ── 组件冒烟:确定性注册一个图标,断 AppIcon 把 icon 接进 <Icon> 并渲成 svg ──
// (离线集在测试里不注册,故手动 addIcon 一个,让解析确定;验的是接线,不是 iconify 本身。)
addIcon('test:box', { body: '<rect width="24" height="24" />', width: 24, height: 24 })
describe('AppIcon', () => {
  it('把 icon 接进 <Icon>,渲成 svg', () => {
    const { container } = render(<AppIcon icon="test:box" />)
    expect(container.querySelector('svg')).toBeTruthy()
  })

  it('ensureIconLoaded 完成后重渲染出 svg(子集外图标)', async () => {
    // 首帧集合未就绪时 Icon 可能无 svg;加载完成后必须 bump 再渲一次,否则侧栏会"空白到点开才出"。
    const { container } = render(<AppIcon icon="lazy:box" />)
    await waitFor(() => {
      expect(ensureIconLoaded).toHaveBeenCalledWith('lazy:box')
      expect(container.querySelector('svg')).toBeTruthy()
    })
  })
})
