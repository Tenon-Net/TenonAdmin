import { type CSSProperties } from 'react'
import { useAppStore, isDark } from '@/stores/app'
import { mix, rgba } from '@/theme/mix'
import { LoginForm } from '../LoginForm'
import './auroraglass.css'

/**
 * 极光皮肤(默认):动画极光背景 + 星点 + 光球 + 旋转光环,玻璃卡内嵌共享表单。移植 Vue `skins/AuroraGlass.vue`。
 * 极光四色由 accent 派生(紫/青/品红);`.aurora.light` 变体在应用浅色主题下换浅底浅玻璃深字。
 *
 * ponytail: 输入框用默认 antd(随应用明暗,玻璃卡同明暗信号,两态皆可读),**不做** Vue 版那层
 * 半透明「玻璃输入框」覆写。要还原可在此包一层局部 `<ConfigProvider theme={{ token:{ colorBgContainer:半透明 },
 * components:{ Input:{ activeBg, hoverBg, activeBorderColor: accent } } }}>`(嵌套默认继承根的明暗 algorithm)。
 */
export function AuroraGlass() {
  const accent = useAppStore((s) => s.accent)
  const dark = useAppStore(isDark)

  // 极光四色:accent + 派生紫/青/品红,随主题色联动。
  const vars = {
    '--a1': rgba(accent, 0.55),
    '--a2': rgba(mix(accent, '#8B5CF6', 0.65), 0.5),
    '--a3': rgba(mix(accent, '#22D3EE', 0.55), 0.45),
    '--a4': rgba(mix(accent, '#EC4899', 0.6), 0.4),
  } as CSSProperties

  return (
    <div className={`aurora${dark ? '' : ' light'}`} style={vars}>
      <div className="aurora-bg" />
      <div className="stars" />
      <div className="orb orb-1" />
      <div className="orb orb-2" />
      <div className="orb orb-3" />
      <div className="aurora-content">
        <div className="halo" />
        <div className="glass-card">
          <LoginForm />
        </div>
      </div>
      <div className="grain" />
    </div>
  )
}
