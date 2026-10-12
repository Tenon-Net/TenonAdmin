import { useEffect, useLayoutEffect, useRef, type ReactNode } from 'react'
import { useLocation, useOutlet } from 'react-router-dom'
import { useTabsStore, aliveKeys } from '@/stores/tabs'
import { useAppStore } from '@/stores/app'
import { CachedPageActiveProvider } from './CachedPageActive'
import { useEntranceMotion } from '@/motion/runtime'

function PageFrame({ active, path, scrollTop, resetScroll, preserveScroll = true, children }: {
  active: boolean
  path: string
  scrollTop: Map<string, number>
  resetScroll: Set<string>
  preserveScroll?: boolean
  children: ReactNode
}) {
  const ref = useRef<HTMLDivElement>(null)
  const transition = useAppStore((s) => s.pageTransition)
  useEntranceMotion(ref, { active, pageTransition: true, translate: transition === 'fade-slide' })

  // 隐藏页不能继续持有焦点；浏览器在它再次 display:block 时会把焦点元素滚入视野，覆盖显式恢复的位置。
  useLayoutEffect(() => {
    if (active) return
    const focused = document.activeElement
    if (focused instanceof HTMLElement && ref.current?.contains(focused)) focused.blur()
  }, [active])

  // .shell-page 是唯一滚动容器。失活先保存，激活再恢复；新页从 0 开始，缓存恢复不重挂。
  useLayoutEffect(() => {
    if (!active) return
    const scroller = ref.current?.closest<HTMLElement>('.shell-page')
    if (!scroller) return
    scroller.scrollTop = !preserveScroll || resetScroll.has(path) ? 0 : (scrollTop.get(path) ?? 0)
    resetScroll.delete(path)
    const save = () => {
      if (preserveScroll && !resetScroll.has(path)) scrollTop.set(path, scroller.scrollTop)
    }
    scroller.addEventListener('scroll', save, { passive: true })
    return () => {
      // 浏览器可能已因旧页 display:none 把共享 scroller clamp 到 0；最后位置已由 scroll 事件实时保存。
      scroller.removeEventListener('scroll', save)
      if (!preserveScroll || resetScroll.has(path)) scrollTop.delete(path)
    }
  }, [active, path, preserveScroll, resetScroll, scrollTop])

  return (
    <div ref={ref} style={{ height: '100%', display: active ? undefined : 'none' }}>
      <CachedPageActiveProvider active={active}>{children}</CachedPageActiveProvider>
    </div>
  )
}

/**
 * 页面缓存容器(替代 Vue `<keep-alive>`,React 无对等物)。
 * 用 `Map<path, 元素>` 把"应缓存"的已开 tab 常驻挂载,非活动页 `display:none` —— 切走切回组件状态保留。
 * noCache 页不入缓存(走 live 渲染:离开即卸载、复访重挂拉新)。被关标签的缓存条目按 aliveKeys 逐出。
 *
 * **刷新**(refreshTab):store 置 excludeKey + 递增 reloadKey。这里 effect 监听 reloadKey,给 excludeKey
 * 递增一个本地"版本号"并删其缓存 → 下次渲染该页 div 的 `key` 变化 → React 卸载重挂(拿到新版本号),再 clearExclude。
 * 单靠 delete 缓存不足以重挂:同一 key 的元素 React 会复用 fiber、状态不清;必须换 key 才强制重挂。
 *
 * CachedPageActiveProvider 只广播可见性，不改变缓存节点 identity；需暂停副作用的组件可用 useCachedPageActive()。
 */
export function KeepAliveOutlet() {
  const outlet = useOutlet()
  const { pathname } = useLocation()
  const tabs = useTabsStore((s) => s.tabs)
  const reloadKey = useTabsStore((s) => s.reloadKey)
  const excludeKey = useTabsStore((s) => s.excludeKey)
  const cache = useRef(new Map<string, ReactNode>())
  const bumps = useRef(new Map<string, number>()) // path → 版本号,刷新时 +1 换 div key 强制重挂
  const scrollTop = useRef(new Map<string, number>())
  const resetScroll = useRef(new Set<string>())
  const alive = aliveKeys(tabs)

  // refreshTab:换版本号 + 删缓存 → 触发重挂;随后复位 excludeKey。
  useEffect(() => {
    if (!excludeKey) return
    bumps.current.set(excludeKey, (bumps.current.get(excludeKey) ?? 0) + 1)
    cache.current.delete(excludeKey)
    scrollTop.current.delete(excludeKey)
    resetScroll.current.add(excludeKey)
    useTabsStore.getState().clearExclude()
  }, [reloadKey, excludeKey])

  // 当前页属"应缓存"集 → 存入最新 outlet(活动页每次刷新;隐藏页保留上次捕获的元素,fiber 状态留存)。
  const cached = alive.includes(pathname)
  if (outlet && cached) cache.current.set(pathname, outlet)
  // 逐出已不在 tab 列表(被关)或转 noCache 的缓存条目。
  // 直接遍历 Map.keys() 边删安全:JS 规范保证删当前项迭代器正常前进、删未访问项即跳过(正是要逐出的)。
  for (const key of cache.current.keys()) {
    if (!alive.includes(key)) {
      cache.current.delete(key)
      bumps.current.delete(key) // 逐出时连版本号一并回收,免得 bumps 随开关标签只增不减
      scrollTop.current.delete(key)
      resetScroll.current.add(key)
    }
  }

  return (
    <>
      {[...cache.current.entries()].map(([key, node]) => (
        <PageFrame
          key={`${key}:${bumps.current.get(key) ?? 0}`}
          active={key === pathname}
          path={key}
          scrollTop={scrollTop.current}
          resetScroll={resetScroll.current}
        >
          {node}
        </PageFrame>
      ))}
      {/* noCache / 尚未入缓存的当前页:live 渲染(离开即卸载,复访重挂拉新)。
          带同一套版本键 → refreshTab 对 noCache 当前页也能重挂(对齐 Vue 靠 rvShow 统一重挂当前页;
          缓存页走上面的 key 分支,noCache 页缓存里没有、只能靠这里的 key 才刷得动)。 */}
      {outlet && !cache.current.has(pathname) && (
        <PageFrame key={`${pathname}:live:${bumps.current.get(pathname) ?? 0}`} active path={pathname} scrollTop={scrollTop.current} resetScroll={resetScroll.current} preserveScroll={false}>
          {outlet}
        </PageFrame>
      )}
    </>
  )
}
