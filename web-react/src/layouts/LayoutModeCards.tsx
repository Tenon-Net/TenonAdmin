import { useTranslation } from 'react-i18next'
import { useAppStore, LAYOUT_MODES, type LayoutMode } from '@/stores/app'
import './layout-cards.css'

// 布局模式彩色线框卡片(对齐 Vue LayoutModeCards.vue / SoybeanAdmin theme-drawer)。
// 三档主色 strong(500)/p300/p200 由实时 --color-primary 派生(见 layout-cards.css);形状逐模式与 soybean 一致。
function label(m: LayoutMode): string {
  const pascal = m.split('-').map((w) => w[0]!.toUpperCase() + w.slice(1)).join('')
  return 'settings.layout' + pascal
}

// 每种模式的线框(与 Vue 模板逐一对应):i.strong/p300/p200 是三档主色块,vwrap/hwrap 是嵌套容器。
function Wireframe({ mode }: { mode: LayoutMode }) {
  switch (mode) {
    case 'vertical': // 左侧满高侧栏 + (头 / 内容)
      return (
        <>
          <i className="sider strong" />
          <i className="vwrap"><i className="header p200" /><i className="main p200" /></i>
        </>
      )
    case 'vertical-mix': // 一级 rail + 二级列 + (头 / 内容)
      return (
        <>
          <i className="rail strong" />
          <i className="subsider p300" />
          <i className="vwrap"><i className="header p200" /><i className="main p200" /></i>
        </>
      )
    case 'vertical-hybrid-header-first': // 一级 rail + 二级列 + (强调头 / 内容)
      return (
        <>
          <i className="rail strong" />
          <i className="subsider p300" />
          <i className="vwrap"><i className="header strong" /><i className="main p200" /></i>
        </>
      )
    case 'horizontal': // 通栏头 + 内容
      return (
        <>
          <i className="header full strong" />
          <i className="hwrap"><i className="main p200" /></i>
        </>
      )
    case 'top-hybrid-sidebar-first': // 头(二级)+ (强调 rail(一级) / 内容)
      return (
        <>
          <i className="header full p300" />
          <i className="hwrap"><i className="sider strong" /><i className="main p200" /></i>
        </>
      )
    default: // top-hybrid-header-first:强调头(一级)+ (侧栏(二级) / 内容)
      return (
        <>
          <i className="header full strong" />
          <i className="hwrap"><i className="sider p300" /><i className="main p200" /></i>
        </>
      )
  }
}

export function LayoutModeCards() {
  const { t } = useTranslation()
  const layoutMode = useAppStore((s) => s.layoutMode)
  const setLayoutMode = useAppStore((s) => s.setLayoutMode)

  return (
    <div className="mode-grid">
      {LAYOUT_MODES.map((m) => {
        const on = layoutMode === m
        const title = t(label(m))
        // 竖向模式线框横排(侧栏在左),顶栏/横向模式竖排(头在上)。
        const innerDir = m.includes('vertical') ? 'row' : 'col'
        return (
          <div key={m} className="mode-cell">
            <button
              type="button"
              className={`mode-card${on ? ' on' : ''}`}
              aria-label={title}
              aria-pressed={on}
              onClick={() => setLayoutMode(m)}
            >
              <span className={`mode-inner ${innerDir}`}>
                <Wireframe mode={m} />
              </span>
            </button>
            <p className={`mode-label${on ? ' on' : ''}`}>{title}</p>
          </div>
        )
      })}
    </div>
  )
}
