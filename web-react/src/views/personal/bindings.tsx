// 账号绑定(个人中心):把第三方账号绑到本账号,以后可一键 SSO 登录。[ActiveSession] 人人可用,静态路由不进菜单。
// 绑定走一次 OAuth 往返证明拥有该外部身份(顶层跳 authorizeUrl,回调把身份绑到当前用户并回本页)。
import { useCallback, useEffect, useState } from 'react'
import { App, Button, Card, Empty, List, Spin, Tag, Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { useConfirm } from '@/hooks/useConfirm'
import { externalAuthApi, type ExternalBinding, type ExternalProvider } from '@/api'
import { translateError } from '@/utils/error'
import { fmtDateTime } from './personalForms'

export default function BindingsPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const [loading, setLoading] = useState(true)
  const [providers, setProviders] = useState<ExternalProvider[]>([])
  const [bindings, setBindings] = useState<ExternalBinding[]>([])

  const bindingOf = (code: string) => bindings.find((b) => b.provider === code)

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
  useEffect(() => { void load() }, [load])

  const bind = useCallback(
    async (p: ExternalProvider) => {
      try {
        const { authorizeUrl } = await externalAuthApi.bindStart(p.code)
        window.location.href = authorizeUrl // 顶层跳转开始 OAuth 往返;回调把身份绑到当前用户并回本页
      } catch (e) {
        message.error(translateError(e))
      }
    },
    [message],
  )

  const unbind = useCallback(
    (p: ExternalProvider) => {
      confirm({ content: t('oauth.unbindConfirm', { name: p.displayName }), action: () => externalAuthApi.unbind(p.code), successMsg: t('oauth.unbound') }).then(
        (ok) => { if (ok) void load() },
      )
    },
    [confirm, t, load],
  )

  return (
    <Card title={t('oauth.bindingsTitle')} style={{ maxWidth: 720 }}>
      <Typography.Paragraph type="secondary" style={{ fontSize: 13 }}>
        {t('oauth.bindingsHint')}
      </Typography.Paragraph>
      {loading ? (
        <Spin />
      ) : !providers.length ? (
        <Empty description={t('oauth.noProviders')} />
      ) : (
        <List
          bordered
          dataSource={providers}
          renderItem={(p) => {
            const b = bindingOf(p.code)
            return (
              <List.Item
                actions={[
                  b ? (
                    <Button key="unbind" size="small" type="text" danger onClick={() => unbind(p)}>{t('oauth.unbind')}</Button>
                  ) : (
                    <Button key="bind" size="small" type="primary" onClick={() => bind(p)}>{t('oauth.bind')}</Button>
                  ),
                ]}
              >
                <List.Item.Meta
                  avatar={<AppIcon icon={p.icon || 'ph:link-simple'} size={26} />}
                  title={p.displayName}
                  description={
                    b ? (
                      <Tag color="green">{t('oauth.boundAt', { time: fmtDateTime(b.boundAt) })}</Tag>
                    ) : (
                      <span style={{ color: 'var(--color-text-tertiary)' }}>{t('oauth.notBound')}</span>
                    )
                  }
                />
              </List.Item>
            )
          }}
        />
      )}
    </Card>
  )
}
