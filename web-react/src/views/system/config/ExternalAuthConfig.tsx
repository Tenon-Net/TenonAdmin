// 第三方登录运营 Tab:预置品牌全展示;未部署/未配密钥的不可打开「登录页显示」。
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Alert, App, Button, Empty, Space, Spin, Switch, Tag, Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import { Can } from '@/components/Can'
import { AppIcon } from '@/components/AppIcon'
import { BrandIcon } from '@/components/oauth/BrandIcon'
import { configApi, externalAuthApi } from '@/api'
import { buildConfigProviderRows, type ConfigProviderRow } from '@/utils/oauthBrand'
import { translateError } from '@/utils/error'
import './externalAuthConfig.css'

export default function ExternalAuthConfig() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [rows, setRows] = useState<ConfigProviderRow[]>([])
  const [enabledMap, setEnabledMap] = useState<Record<string, boolean>>({})
  const [baseline, setBaseline] = useState<Record<string, boolean>>({})

  const dirty = useMemo(
    () =>
      rows
        .filter((p) => p.registered)
        .some((p) => !!enabledMap[p.code] !== !!baseline[p.code]),
    [rows, enabledMap, baseline],
  )
  const enabledCount = useMemo(
    () => rows.filter((p) => p.registered && enabledMap[p.code]).length,
    [rows, enabledMap],
  )
  const registeredCount = useMemo(() => rows.filter((p) => p.registered).length, [rows])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const list = await externalAuthApi.providersAll()
      const built = buildConfigProviderRows(list)
      setRows(built)
      const m: Record<string, boolean> = {}
      for (const p of built) m[p.code] = p.registered ? p.enabled : false
      setEnabledMap({ ...m })
      setBaseline({ ...m })
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }, [message])

  useEffect(() => {
    void load()
  }, [load])

  const save = async () => {
    setSaving(true)
    try {
      await configApi.saveBatch(
        rows
          .filter((p) => p.registered)
          .map((p) => ({
            configKey: `sys.externalauth.${p.code}.enabled`,
            configValue: String(!!enabledMap[p.code]),
          })),
      )
      message.success(t('config.saved'))
      await load()
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSaving(false)
    }
  }

  const reset = () => setEnabledMap({ ...baseline })

  if (loading) {
    return (
      <div className="ea-loading">
        <Spin />
      </div>
    )
  }

  if (!rows.length) {
    return (
      <div className="ea">
        <Alert type="info" showIcon message={t('config.externalAuth.hint')} className="ea-alert" />
        <Empty description={t('config.externalAuth.empty')} className="ea-empty" />
      </div>
    )
  }

  return (
    <div className="ea">
      <Alert type="info" showIcon message={t('config.externalAuth.hint')} className="ea-alert" />

      <div className="ea-toolbar">
        <Typography.Text type="secondary" className="ea-summary">
          {t('config.externalAuth.summaryFull', {
            total: rows.length,
            registered: registeredCount,
            on: enabledCount,
          })}
        </Typography.Text>
        <Space size={8}>
          {dirty ? (
            <Button type="text" size="small" onClick={reset}>
              {t('config.externalAuth.reset')}
            </Button>
          ) : null}
          <Can code="PUT:/api/v1/sys/config/batch">
            <Button
              type="primary"
              size="small"
              loading={saving}
              disabled={!dirty}
              icon={<AppIcon icon="ph:floppy-disk" size={16} />}
              onClick={() => void save()}
            >
              {t('common.save')}
            </Button>
          </Can>
        </Space>
      </div>

      <div className="ea-grid">
        {rows.map((p) => {
          const on = !!enabledMap[p.code]
          return (
            <div
              key={p.code}
              className={`ea-card ${
                !p.registered ? 'ea-card--na' : on ? 'ea-card--on' : 'ea-card--off'
              }`}
            >
              <div className="ea-card-main">
                <span className="ea-avatar">
                  <BrandIcon code={p.code} icon={p.icon} size={36} />
                </span>
                <div className="ea-meta">
                  <div className="ea-title-row">
                    <span className="ea-name">{p.displayName}</span>
                    {!p.registered ? (
                      <Tag color="warning" bordered={false}>
                        {t('config.externalAuth.notConfigured')}
                      </Tag>
                    ) : (
                      <Tag color={on ? 'success' : 'default'} bordered={false}>
                        {on ? t('config.externalAuth.showOnLogin') : t('config.externalAuth.hidden')}
                      </Tag>
                    )}
                  </div>
                  <code className="ea-code">{p.code}</code>
                  {!p.registered ? (
                    <p className="ea-na-tip">{t('config.externalAuth.notConfiguredTip')}</p>
                  ) : null}
                </div>
              </div>
              <div className="ea-switch">
                <span className="ea-switch-label">{t('config.externalAuth.loginVisible')}</span>
                <Switch
                  checked={on}
                  disabled={!p.registered}
                  onChange={(v) => {
                    if (!p.registered) return
                    setEnabledMap((m) => ({ ...m, [p.code]: v }))
                  }}
                />
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
