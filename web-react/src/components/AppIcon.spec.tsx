import { describe, it, expect, afterEach } from 'vitest'
import { render, cleanup } from '@testing-library/react'
import { addIcon } from '@iconify/react/offline'
import { iconName, AppIcon } from './AppIcon'

afterEach(cleanup)

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
})
