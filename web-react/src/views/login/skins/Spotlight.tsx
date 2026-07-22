import { useEffect, useState, type CSSProperties } from 'react'
import { useAppStore, isDark } from '@/stores/app'
import { mix, rgba } from '@/theme/mix'
import { LoginForm } from '../LoginForm'
import './spotlight.css'

/**
 * 聚光皮肤:网格底 + 跟随指针的光晕,居中卡片内嵌共享表单。移植 Vue `skins/Spotlight.vue`。
 * 光晕位置(百分比)随鼠标更新,注入 CSS 变量;accent 派生取色。
 */
export function Spotlight() {
  const accent = useAppStore((s) => s.accent)
  const dark = useAppStore(isDark)
  // 聚光位置(百分比),默认略偏上居中;鼠标移动时跟随。
  const [pos, setPos] = useState({ x: 50, y: 38 })

  useEffect(() => {
    const onMove = (e: MouseEvent) =>
      setPos({ x: (e.clientX / window.innerWidth) * 100, y: (e.clientY / window.innerHeight) * 100 })
    window.addEventListener('mousemove', onMove, { passive: true })
    return () => window.removeEventListener('mousemove', onMove)
  }, [])

  const vars = {
    '--mx': pos.x + '%',
    '--my': pos.y + '%',
    '--s1': rgba(accent, 0.28),
    '--s2': rgba(mix(accent, '#8B5CF6', 0.6), 0.22),
    // 跟随光晕独立取色:亮色近白底下 0.28 几乎不可见,提到 0.5 才读得出;暗底 0.28 已够。
    '--spot': rgba(accent, dark ? 0.3 : 0.5),
  } as CSSProperties

  return (
    <div className="spotlight" style={vars}>
      <div className="mesh" />
      <div className="spot" />
      <div className="card">
        <LoginForm />
      </div>
    </div>
  )
}
