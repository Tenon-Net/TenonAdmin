// 缓存管理页:定向失效动作卡片(只清不看——缓存值含明文验证码/一次性令牌、键内嵌手机号/IP,后端故无键浏览/取值端点)。
// 每张卡二次确认(ask)后执行:门户是"重建代际"语义(返回新代际值)用 portalDone 文案,其余显被清条数。
import { useState } from 'react'
import { App, Button, Card, Col, Row } from 'antd'
import { useTranslation } from 'react-i18next'
import { Can } from '@/components/Can'
import { useConfirm } from '@/hooks/useConfirm'
import { cacheApi } from '@/api'
import { translateError } from '@/utils/error'

interface CacheAction {
  key: 'auth' | 'dict' | 'config' | 'portal'
  perm: string
  run: () => Promise<number>
  /** true=重建代际(返回新代际值,非清除条数)→ portalDone 文案;缺省=显被清条数。 */
  rebuild?: boolean
}
// run 用 thunk(调用时才取 cacheApi.*):晚绑定,消费者替换 cacheApi 也生效,单测可 spy。
const CACHE_ACTIONS: CacheAction[] = [
  { key: 'auth', perm: 'POST:/api/v1/sys/cache/flush-auth', run: () => cacheApi.flushAuth() },
  { key: 'dict', perm: 'POST:/api/v1/sys/cache/flush-dict', run: () => cacheApi.flushDict() },
  { key: 'config', perm: 'POST:/api/v1/sys/cache/flush-config', run: () => cacheApi.flushConfig() },
  { key: 'portal', perm: 'POST:/api/v1/sys/cache/rebuild-portal', run: () => cacheApi.rebuildPortal(), rebuild: true },
]

export default function CachePage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { ask } = useConfirm()
  const [busy, setBusy] = useState<string | null>(null)

  const trigger = async (a: CacheAction) => {
    if (!(await ask({ content: t(`cache.${a.key}Confirm`) }))) return // 未确认:什么都不做
    setBusy(a.key)
    try {
      const n = await a.run()
      message.success(a.rebuild ? t('cache.portalDone') : t('cache.flushed', { n }))
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setBusy(null)
    }
  }

  return (
    <>
      <Row gutter={[12, 12]}>
        {CACHE_ACTIONS.map((a) => (
          <Can key={a.key} code={a.perm}>
            <Col xs={24} lg={12}>
              <Card size="small" title={t(`cache.${a.key}Title`)} style={{ height: '100%' }}>
                <p style={{ margin: '0 0 14px', fontSize: 13, lineHeight: 1.6, color: 'var(--color-text-secondary, #999)', minHeight: 42 }}>
                  {t(`cache.${a.key}Desc`)}
                </p>
                <Button type="primary" loading={busy === a.key} onClick={() => trigger(a)}>
                  {t('cache.execute')}
                </Button>
              </Card>
            </Col>
          </Can>
        ))}
      </Row>
      <p style={{ margin: '12px 0 0', fontSize: 12, lineHeight: 1.6, color: 'var(--color-text-secondary, #999)' }}>{t('cache.note')}</p>
    </>
  )
}
