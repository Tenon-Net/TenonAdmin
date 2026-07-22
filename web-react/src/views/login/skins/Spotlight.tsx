import { useEffect, useRef, type CSSProperties } from 'react'
import { useAppStore, isDark } from '@/stores/app'
import { mix, rgba } from '@/theme/mix'
import { LoginForm } from '../LoginForm'
import './spotlight.css'

/**
 * 聚光皮肤:网格底 + 跟随指针的光晕,居中卡片内嵌共享表单。移植 Vue `skins/Spotlight.vue`。
 * 光晕位置由指针驱动、直写根元素 CSS 变量(不经 React state,不重渲染表单);accent 派生取色。
 */
export function Spotlight() {
  const accent = useAppStore((s) => s.accent)
  const dark = useAppStore(isDark)
  const rootRef = useRef<HTMLDivElement>(null)

  // 光晕跟随指针:直接写根元素 `--mx`/`--my`,绕过 React state —— 每次 mousemove 不重渲染整棵登录表单
  // (对齐 Vue 的 ref 驱动细粒度更新)。初始位置在 CSS(.spotlight 的 --mx/--my 默认),故首帧就有光晕。
  useEffect(() => {
    const el = rootRef.current
    if (!el) return
    const onMove = (e: MouseEvent) => {
      el.style.setProperty('--mx', (e.clientX / window.innerWidth) * 100 + '%')
      el.style.setProperty('--my', (e.clientY / window.innerHeight) * 100 + '%')
    }
    window.addEventListener('mousemove', onMove, { passive: true })
    return () => window.removeEventListener('mousemove', onMove)
  }, [])

  // 只有随 accent/明暗变的取色进 inline style(不随鼠标变);--mx/--my 由上面的 ref 直写。
  const vars = {
    '--s1': rgba(accent, 0.28),
    '--s2': rgba(mix(accent, '#8B5CF6', 0.6), 0.22),
    // 跟随光晕独立取色:亮色近白底下 0.28 几乎不可见,提到 0.5 才读得出;暗底 0.28 已够。
    '--spot': rgba(accent, dark ? 0.3 : 0.5),
  } as CSSProperties

  return (
    <div className="spotlight" style={vars} ref={rootRef}>
      <div className="mesh" />
      <div className="spot" />
      <div className="card">
        <LoginForm />
      </div>
    </div>
  )
}
