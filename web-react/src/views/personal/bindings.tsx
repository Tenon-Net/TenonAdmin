// 账号绑定(个人中心 + 品牌化):启用 providers ∪ 已绑定停用项(B-A);卡片网格与配置 Tab 同风。
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Alert, App, Button, Empty, Spin, Tag } from 'antd'
import { LinkOutlined, DisconnectOutlined, CheckOutlined } from '@ant-design/icons'
import { useTranslation } from 'react-i18next'
import { useConfirm } from '@/hooks/useConfirm'
import { externalAuthApi, type ExternalBinding, type ExternalProvider } from '@/api'
import { translateError } from '@/utils/error'
import { mergeBindingRows, type BindingRow } from '@/utils/oauthBrand'
import { BrandIcon } from '@/components/oauth/BrandIcon'
import { fmtDateTime } from './personalForms'
import './bindings.css'

function cardClass(row: BindingRow) {
  if (!row.enabled) return 'bind-card bind-card--disabled'
  if (row.binding) return 'bind-card bind-card--bound'
  return 'bind-card bind-card--free'
}

export default function BindingsPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const [loading, setLoading] = useState(true)
  const [providers, setProviders] = useState<ExternalProvider[]>([])
  const [bindings, setBindings] = useState<ExternalBinding[]>([])
  const [busyCode, setBusyCode] = useState<string | null>(null)

  const rows = useMemo(() => mergeBindingRows(providers, bindings), [providers, bindings])
  const boundCount = useMemo(() => rows.filter((r) => !!r.binding).length, [rows])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [ps, bs] = await Promise.all([externalAuthApi.providers(), externalAuthApi.bindings()])
      setProviders(ps)
      setBindings(bs)
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }, [message])
  useEffect(() => {
    void load()
  }, [load])

  const bind = useCallback(
    async (row: BindingRow) => {
      if (!row.enabled || busyCode) return
      setBusyCode(row.code)
      try {
        const { authorizeUrl } = await externalAuthApi.bindStart(row.code)
        window.location.href = authorizeUrl
      } catch (e) {
        message.error(translateError(e))
        setBusyCode(null)
      }
    },
    [message, busyCode],
  )

  const unbind = useCallback(
    (row: BindingRow) => {
      if (busyCode) return
      confirm({
        content: t('oauth.unbindConfirm', { name: row.displayName }),
        action: () => externalAuthApi.unbind(row.code),
        successMsg: t('oauth.unbound'),
      }).then((ok) => {
        if (ok) void load()
      })
    },
    [confirm, t, load, busyCode],
  )

  return (
    <div className="bind">
      <header className="bind-header">
        <div>
          <h2 className="bind-title">{t('oauth.bindingsTitle')}</h2>
          <p className="bind-hint">{t('oauth.bindingsHint')}</p>
        </div>
        {!loading && rows.length > 0 ? (
          <div className="bind-summary">
            <span className="bind-summary-num">{boundCount}</span>
            <span className="bind-summary-sep">/</span>
            <span>{rows.length}</span>
            <span className="bind-summary-label">{t('oauth.boundCountLabel')}</span>
          </div>
        ) : null}
      </header>

      <Alert type="info" showIcon className="bind-alert" message={t('oauth.bindingsTip')} />

      {loading ? (
        <div className="bind-loading">
          <Spin />
        </div>
      ) : !rows.length ? (
        <Empty className="bind-empty" description={t('oauth.noProviders')} />
      ) : (
        <div className="bind-grid">
          {rows.map((row) => (
            <article key={row.code} className={cardClass(row)}>
              <div className="bind-card-main">
                <span className={`bind-avatar${row.binding ? ' bind-avatar--bound' : ''}`}>
                  <BrandIcon code={row.code} icon={row.icon} size={44} />
                  {row.binding ? (
                    <span className="bind-check" aria-hidden>
                      <CheckOutlined style={{ fontSize: 10 }} />
                    </span>
                  ) : null}
                </span>
                <div className="bind-meta">
                  <div className="bind-title-row">
                    <span className="bind-name">{row.displayName}</span>
                    {!row.enabled ? (
                      <Tag color="orange" bordered={false}>
                        {t('oauth.disabled')}
                      </Tag>
                    ) : row.binding ? (
                      <Tag color="success" bordered={false}>
                        {t('oauth.bound')}
                      </Tag>
                    ) : (
                      <Tag bordered={false}>{t('oauth.notBound')}</Tag>
                    )}
                  </div>
                  <code className="bind-code">{row.code}</code>
                  {row.binding ? (
                    <p className="bind-time">
                      {t('oauth.boundAt', { time: fmtDateTime(row.binding.boundAt) })}
                    </p>
                  ) : !row.enabled ? (
                    <p className="bind-disabled-tip">{t('oauth.disabledTip')}</p>
                  ) : (
                    <p className="bind-free-tip">{t('oauth.bindTip')}</p>
                  )}
                </div>
              </div>

              <div className="bind-actions">
                {row.binding ? (
                  <Button
                    size="small"
                    type="text"
                    danger
                    icon={<DisconnectOutlined />}
                    disabled={!!busyCode}
                    onClick={() => unbind(row)}
                  >
                    {t('oauth.unbind')}
                  </Button>
                ) : row.enabled ? (
                  <Button
                    size="small"
                    type="primary"
                    ghost
                    icon={<LinkOutlined />}
                    loading={busyCode === row.code}
                    disabled={!!busyCode && busyCode !== row.code}
                    onClick={() => void bind(row)}
                  >
                    {t('oauth.bind')}
                  </Button>
                ) : (
                  <span className="bind-na">{t('oauth.cannotBind')}</span>
                )}
              </div>
            </article>
          ))}
        </div>
      )}
    </div>
  )
}
