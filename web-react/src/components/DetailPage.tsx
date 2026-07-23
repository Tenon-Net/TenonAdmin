import type { CSSProperties, ReactNode } from 'react'
import { Button, Spin } from 'antd'
import { ArrowLeftOutlined } from '@ant-design/icons'
import { useTranslation } from 'react-i18next'

const pageStyle: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 'var(--gap-card, 16px)' }
const headerStyle: CSSProperties = { display: 'flex', alignItems: 'center', gap: 8 }
const titleStyle: CSSProperties = { margin: 0, fontSize: 'var(--font-size-lg, 18px)', fontWeight: 600, color: 'var(--color-text-primary)' }

export interface DetailPageProps {
  title?: string
  loading?: boolean
  /** 默认显示返回按钮;就地详情(无独立路由)可关掉。 */
  showBack?: boolean
  /** 返回语义交父级:路由态 = 关标签回列表;就地态 = 清详情状态。对应 Vue 侧 @back。 */
  onBack?: () => void
  actions?: ReactNode
  children?: ReactNode
}

/**
 * 详情页外壳:返回按钮 + 标题 + actions + body(带 loading)。补偿非菜单详情路由的空面包屑
 * (菜单树没这条路由 → 头部面包屑为空,故详情页自带标题/返回)。对应 Vue 侧 `DetailPage/index.vue`;
 * `@back` 换 onBack 回调,`#actions` 插槽换 actions prop。
 */
export function DetailPage({ title, loading, showBack = true, onBack, actions, children }: DetailPageProps) {
  const { t } = useTranslation()
  return (
    <div style={pageStyle}>
      <div style={headerStyle}>
        {showBack ? (
          <Button type="text" size="small" aria-label={t('common.back')} icon={<ArrowLeftOutlined />} onClick={onBack} />
        ) : null}
        <h2 style={titleStyle}>{title}</h2>
        <div style={{ marginLeft: 'auto' }}>{actions}</div>
      </div>
      <Spin spinning={!!loading}>
        <div>{children}</div>
      </Spin>
    </div>
  )
}
