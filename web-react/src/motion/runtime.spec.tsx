import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render } from '@testing-library/react'
import { useRef } from 'react'
import { useAppStore } from '@/stores/app'
import { useEntranceMotion, useMotionRuntime } from './runtime'
import motionCss from '@/styles/motion.css?raw'

const nativeAnimate = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'animate')
let motionStyle: HTMLStyleElement

function RuntimeProbe() {
  useMotionRuntime()
  return null
}

function EntranceProbe({ active = true, once = false }: { active?: boolean; once?: boolean }) {
  const ref = useRef<HTMLDivElement>(null)
  useEntranceMotion(ref, { active, once, pageTransition: true })
  return <div ref={ref}>content</div>
}

beforeEach(() => {
  motionStyle = document.createElement('style')
  motionStyle.textContent = motionCss
  document.head.appendChild(motionStyle)
  useAppStore.setState({ motionMode: 'standard', systemReducedMotion: false, pageTransition: 'fade' })
  const root = document.documentElement
  root.dataset.motion = 'standard'
  root.dataset.pageTransition = 'fade'
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  if (nativeAnimate) Object.defineProperty(HTMLElement.prototype, 'animate', nativeAnimate)
  else delete (HTMLElement.prototype as Partial<HTMLElement>).animate
  document.documentElement.removeAttribute('data-document-hidden')
  motionStyle.remove()
})

describe('motion runtime', () => {
  it('同步有效档位；系统减少动态效果强制 off', () => {
    render(<RuntimeProbe />)
    expect(document.documentElement.dataset.motion).toBe('standard')
    act(() => useAppStore.setState({ motionMode: 'light' }))
    expect(document.documentElement.dataset.motion).toBe('light')
    act(() => useAppStore.setState({ systemReducedMotion: true }))
    expect(document.documentElement.dataset.motion).toBe('off')
  })

  it('完成、设置变化和卸载都会取消自有 WAAPI；首次内容缓存恢复不重播', () => {
    const animations: Array<{ cancel: ReturnType<typeof vi.fn>; onfinish: null | (() => void) }> = []
    Object.defineProperty(HTMLElement.prototype, 'animate', {
      configurable: true,
      value: vi.fn(() => {
        const animation = { cancel: vi.fn(), onfinish: null }
        animations.push(animation)
        return animation
      }),
    })
    const view = render(<EntranceProbe once />)
    expect(animations).toHaveLength(1)
    animations[0]!.onfinish?.()
    expect(animations[0]!.cancel).toHaveBeenCalledTimes(1)

    view.rerender(<EntranceProbe active={false} once />)
    view.rerender(<EntranceProbe active once />)
    expect(animations).toHaveLength(1)

    view.unmount()
    const current = render(<EntranceProbe />)
    expect(animations).toHaveLength(2)
    act(() => useAppStore.setState({ motionMode: 'light' }))
    expect(animations[1]!.cancel).toHaveBeenCalled()
    current.unmount()
  })

  it('pageTransition=none 时页面入场直接终态', () => {
    const animate = vi.fn()
    Object.defineProperty(HTMLElement.prototype, 'animate', { configurable: true, value: animate })
    useAppStore.setState({ pageTransition: 'none' })
    render(<EntranceProbe />)
    expect(animate).not.toHaveBeenCalled()
  })

  it.each([
    ['持久化 off', { motionMode: 'off' as const, systemReducedMotion: false }],
    ['系统 reduce', { motionMode: 'standard' as const, systemReducedMotion: true }],
  ])('冷启动%s时不创建错误动画', (_name, state) => {
    useAppStore.setState(state)
    document.documentElement.dataset.motion = 'standard' // 模拟父 App 尚未提交的旧投影
    const animate = vi.fn()
    Object.defineProperty(HTMLElement.prototype, 'animate', { configurable: true, value: animate })
    render(<EntranceProbe />)
    expect(document.documentElement.dataset.motion).toBe('off')
    expect(animate).not.toHaveBeenCalled()
  })

  it('冷启动持久化 light 时先同步 data-motion，再从 CSS 读取 120ms', () => {
    useAppStore.setState({ motionMode: 'light', systemReducedMotion: false })
    document.documentElement.dataset.motion = 'standard'
    const animate = vi.fn((_frames: Keyframe[] | PropertyIndexedKeyframes, _options?: number | KeyframeAnimationOptions) => ({ cancel: vi.fn(), onfinish: null }))
    Object.defineProperty(HTMLElement.prototype, 'animate', { configurable: true, value: animate })
    render(<EntranceProbe />)
    expect(document.documentElement.dataset.motion).toBe('light')
    expect(animate).toHaveBeenCalled()
    expect(animate.mock.calls[0]![1]).toMatchObject({ duration: 120 })
  })
})
