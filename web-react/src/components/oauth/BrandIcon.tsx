// 外部登录品牌标:优先本地 assets/oauth/{code}.svg|png 彩色圆标(Gitee 风);否则 Iconify/首字母。
import type { CSSProperties } from 'react'
import { AppIcon } from '@/components/AppIcon'
import { resolveProviderIcon, type BrandCode } from '@/utils/oauthBrand'

const assetModules = {
  ...import.meta.glob('../../assets/oauth/*.{svg,png,webp}', {
    eager: true,
    import: 'default',
    query: '?url',
  }),
  ...import.meta.glob('/src/assets/oauth/*.{svg,png,webp}', {
    eager: true,
    import: 'default',
    query: '?url',
  }),
} as Record<string, string>

function localBadgeUrl(code: string): string | null {
  const c = code.toLowerCase()
  for (const ext of ['svg', 'png', 'webp'] as const) {
    const hit = Object.entries(assetModules).find(([path, url]) => {
      if (!url || typeof url !== 'string') return false
      const base = path.replace(/\\/g, '/').split('/').pop()?.toLowerCase() ?? ''
      return base === `${c}.${ext}`
    })
    if (hit) return hit[1]
  }
  return null
}

const fallbackIconify: Partial<Record<BrandCode, string>> = {
  github: 'ant-design:github-filled',
  wechat: 'ant-design:wechat-filled',
  wecom: 'ant-design:wechat-work-filled',
  dingtalk: 'ant-design:dingtalk-circle-filled',
  qq: 'ant-design:qq-circle-filled',
}

const fallbackColor: Partial<Record<BrandCode, string>> = {
  github: '#24292f',
  wechat: '#07c160',
  wecom: '#2b7de9',
  dingtalk: '#0089ff',
  qq: '#12b7f5',
  gitee: '#c71d23',
}

export function BrandIcon({
  code,
  icon,
  size = 22,
  className,
  style,
}: {
  code: string
  icon?: string | null
  size?: number
  className?: string
  style?: CSSProperties
}) {
  const resolved = resolveProviderIcon(code, icon)
  const badge =
    resolved.kind === 'brand' ? localBadgeUrl(resolved.code) : null

  if (badge) {
    return (
      <img
        className={className}
        src={badge}
        width={size}
        height={size}
        alt=""
        draggable={false}
        style={{
          display: 'block',
          width: size,
          height: size,
          maxWidth: '100%',
          maxHeight: '100%',
          objectFit: 'cover', // 方形素材铺满后由圆角裁成圆
          borderRadius: '50%',
          overflow: 'hidden',
          pointerEvents: 'none',
          userSelect: 'none',
          flexShrink: 0,
          ...style,
        }}
      />
    )
  }

  const fbIcon = resolved.kind === 'brand' ? fallbackIconify[resolved.code] : undefined
  const fbColor = resolved.kind === 'brand' ? fallbackColor[resolved.code] : undefined

  const box: CSSProperties = {
    width: size,
    height: size,
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
    lineHeight: 0,
    color: fbColor,
    ...style,
  }

  if (fbIcon) {
    return (
      <span className={className} style={box} aria-hidden>
        <AppIcon icon={fbIcon} size={size} fallback="ph:link-simple" />
      </span>
    )
  }

  if (resolved.kind === 'iconify') {
    return (
      <span className={className} style={box} aria-hidden>
        <AppIcon icon={resolved.name} size={size} fallback="ph:link-simple" />
      </span>
    )
  }

  return (
    <span
      className={className}
      style={{ ...box, fontSize: 12, fontWeight: 700, color: 'var(--color-text-secondary, #666)' }}
      aria-hidden
    >
      {resolved.kind === 'letter' ? resolved.letter : '?'}
    </span>
  )
}
