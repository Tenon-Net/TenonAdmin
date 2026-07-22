import { useEffect, useRef, type ReactNode } from 'react'
import { useLocation, useOutlet } from 'react-router-dom'
import { useTabsStore, aliveKeys } from '@/stores/tabs'
import { useAppStore } from '@/stores/app'

/**
 * 页面缓存容器(替代 Vue `<keep-alive>`,React 无对等物)。
 * 用 `Map<path, 元素>` 把"应缓存"的已开 tab 常驻挂载,非活动页 `display:none` —— 切走切回组件状态保留。
 * noCache 页不入缓存(走 live 渲染:离开即卸载、复访重挂拉新)。被关标签的缓存条目按 aliveKeys 逐出。
 *
 * **刷新**(refreshTab):store 置 excludeKey + 递增 reloadKey。这里 effect 监听 reloadKey,给 excludeKey
 * 递增一个本地"版本号"并删其缓存 → 下次渲染该页 div 的 `key` 变化 → React 卸载重挂(拿到新版本号),再 clearExclude。
 * 单靠 delete 缓存不足以重挂:同一 key 的元素 React 会复用 fiber、状态不清;必须换 key 才强制重挂。
 *
 * // ponytail: 不做 activated/deactivated 钩子;隐藏页的 effect/定时器仍在跑,页面若需感知激活再加 useTabActive()。
 */
export function KeepAliveOutlet() {
  const outlet = useOutlet()
  const { pathname } = useLocation()
  const tabs = useTabsStore((s) => s.tabs)
  const reloadKey = useTabsStore((s) => s.reloadKey)
  const excludeKey = useTabsStore((s) => s.excludeKey)
  const transition = useAppStore((s) => s.pageTransition)
  const cache = useRef(new Map<string, ReactNode>())
  const bumps = useRef(new Map<string, number>()) // path → 版本号,刷新时 +1 换 div key 强制重挂
  const alive = aliveKeys(tabs)
  // 页面切换动画:只给"当前可见"页挂 CSS animation 类。用 animation 而非 transition ——
  // 页 div 由 display:none→block 时 CSS animation 会重跑(transition 跨 display 不触发),故切走切回即入场,不 remount、不动缓存。
  const animClass = transition === 'none' ? undefined : `page-anim-${transition}`

  // refreshTab:换版本号 + 删缓存 → 触发重挂;随后复位 excludeKey。
  useEffect(() => {
    if (!excludeKey) return
    bumps.current.set(excludeKey, (bumps.current.get(excludeKey) ?? 0) + 1)
    cache.current.delete(excludeKey)
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
    }
  }

  return (
    <>
      {[...cache.current.entries()].map(([key, node]) => (
        <div
          key={`${key}:${bumps.current.get(key) ?? 0}`}
          className={key === pathname ? animClass : undefined}
          style={{ height: '100%', display: key === pathname ? undefined : 'none' }}
        >
          {node}
        </div>
      ))}
      {/* noCache / 尚未入缓存的当前页:live 渲染(离开即卸载,复访重挂拉新)。
          带同一套版本键 → refreshTab 对 noCache 当前页也能重挂(对齐 Vue 靠 rvShow 统一重挂当前页;
          缓存页走上面的 key 分支,noCache 页缓存里没有、只能靠这里的 key 才刷得动)。 */}
      {outlet && !cache.current.has(pathname) && (
        <div key={`${pathname}:live:${bumps.current.get(pathname) ?? 0}`} className={animClass} style={{ height: '100%' }}>
          {outlet}
        </div>
      )}
    </>
  )
}
