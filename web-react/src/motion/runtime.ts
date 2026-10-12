import { useEffect, useLayoutEffect, useRef, type RefObject } from 'react'
import { effectiveMotionMode, useAppStore } from '@/stores/app'

const cssNumber = (style: CSSStyleDeclaration, name: string) => {
  const value = style.getPropertyValue(name).trim()
  if (value.endsWith('ms')) return Number.parseFloat(value)
  if (value.endsWith('s')) return Number.parseFloat(value) * 1000
  return Number.parseFloat(value) || 0
}

/** store 已同步水合，在 React 首次 commit 前先把冷启动策略写到根节点。 */
const syncRootMotion = () => {
  if (typeof document === 'undefined') return
  const state = useAppStore.getState()
  document.documentElement.dataset.motion = effectiveMotionMode(state)
  document.documentElement.dataset.pageTransition = state.pageTransition
}

syncRootMotion()

/** 将有效档位和页面动画偏好同步到根节点，并维护后台可见性标记。 */
export function useMotionRuntime() {
  const mode = useAppStore(effectiveMotionMode)
  const pageTransition = useAppStore((s) => s.pageTransition)

  useLayoutEffect(() => {
    const root = document.documentElement
    root.dataset.motion = mode
    root.dataset.pageTransition = pageTransition
  }, [mode, pageTransition])

  useEffect(() => {
    const root = document.documentElement
    const sync = () => root.toggleAttribute('data-document-hidden', document.hidden)
    sync()
    document.addEventListener('visibilitychange', sync)
    return () => document.removeEventListener('visibilitychange', sync)
  }, [])
}

interface EntranceOptions {
  active?: boolean
  once?: boolean
  stagger?: boolean
  pageTransition?: boolean
  translate?: boolean
  durationVar?: '--motion-duration-page-enter' | '--motion-duration-panel-enter'
}

/**
 * 自有内容入场。所有参数从 motion.css 读取；隐藏、失活和 StrictMode effect 重跑都会取消旧动画。
 * ref 可指向一个元素，stagger 时对其带 data-motion-item 的直接后代逐一播放。
 */
export function useEntranceMotion(ref: RefObject<HTMLElement | null>, options: EntranceOptions = {}) {
  const active = options.active ?? true
  const pageTransition = options.pageTransition ?? false
  const config = useRef({
    once: options.once ?? false,
    stagger: options.stagger ?? false,
    translate: options.translate ?? true,
    durationVar: options.durationVar ?? '--motion-duration-page-enter',
    pageTransition,
  })
  config.current = {
    once: options.once ?? false,
    stagger: options.stagger ?? false,
    translate: options.translate ?? true,
    durationVar: options.durationVar ?? '--motion-duration-page-enter',
    pageTransition,
  }
  const deactivated = useRef(false)
  const owned = useRef<Animation[]>([])

  // 设置动态降级时只取消现有动画，不重新播放已经开始的首次内容。
  useEffect(() => useAppStore.subscribe((state, previous) => {
    const modeChanged = effectiveMotionMode(state) !== effectiveMotionMode(previous)
    const pageDisabled = pageTransition && state.pageTransition !== previous.pageTransition
    if (modeChanged || pageDisabled) {
      owned.current.forEach((animation) => animation.cancel())
      owned.current = []
    }
  }), [pageTransition])

  useLayoutEffect(() => {
    const { once, stagger, translate, durationVar, pageTransition: isPageTransition } = config.current
    const root = ref.current
    if (!active) {
      deactivated.current = true
      return
    }
    if (!root || document.hidden || (once && deactivated.current)) return
    const html = document.documentElement
    // 冷启动时不依赖父 App layout effect 的提交顺序；store 才是策略真源，data-* 是 CSS 投影。
    const state = useAppStore.getState()
    const mode = effectiveMotionMode(state)
    html.dataset.motion = mode
    html.dataset.pageTransition = state.pageTransition
    if (mode === 'off' || (isPageTransition && state.pageTransition === 'none')) return

    const style = getComputedStyle(html)
    const duration = cssNumber(style, durationVar)
    const distance = style.getPropertyValue('--motion-distance').trim() || '0px'
    const step = cssNumber(style, '--motion-stagger-step')
    const maxDelay = cssNumber(style, '--motion-stagger-cap')
    const easing = style.getPropertyValue('--motion-ease-standard').trim() || 'ease'
    const targets = stagger ? [...root.querySelectorAll<HTMLElement>(':scope > [data-motion-item]')] : [root]
    if (!duration || typeof root.animate !== 'function') return

    const animations = targets.map((el, index) => el.animate(
      [
        { opacity: 0, transform: translate ? `translateY(${distance})` : 'translateY(0)' },
        { opacity: 1, transform: 'translateY(0)' },
      ],
      { duration, delay: Math.min(index * step, maxDelay), easing, fill: 'both' },
    ))
    owned.current = animations
    animations.forEach((animation) => {
      animation.onfinish = () => {
        animation.cancel() // 释放 fill:both 留下的内联效果，让最终样式重新归 CSS 所有。
        owned.current = owned.current.filter((item) => item !== animation)
      }
    })
    const cancel = () => {
      animations.forEach((animation) => animation.cancel())
      owned.current = []
    }
    const onVisibility = () => { if (document.hidden) cancel() }
    document.addEventListener('visibilitychange', onVisibility)
    return () => {
      document.removeEventListener('visibilitychange', onVisibility)
      cancel()
    }
  }, [active, ref])
}
