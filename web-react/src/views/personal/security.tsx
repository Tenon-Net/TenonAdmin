// 个人安全:TOTP 自助绑定/恢复入口(ADR 0006)。不进业务菜单,顶栏用户下拉进入。
import { Alert, Button, Card, Space, Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useUserStore } from '@/stores/user'

export default function SecurityPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const account = useUserStore((s) => s.userInfo?.account)

  const goBind = () => {
    const q = account ? `?account=${encodeURIComponent(account)}` : ''
    navigate(`/mfa/bind${q}`)
  }

  return (
    <Card title={t('personalSecurity.title')} style={{ maxWidth: 560 }}>
      <Alert type="info" showIcon title={t('personalSecurity.hint')} style={{ marginBottom: 16 }} />
      <Typography.Paragraph type="secondary">{t('personalSecurity.bindDesc')}</Typography.Paragraph>
      <Space>
        <Button type="primary" onClick={goBind}>{t('personalSecurity.setupAuthenticator')}</Button>
        <Button type="link" onClick={goBind}>{t('personalSecurity.useRecovery')}</Button>
      </Space>
      <Typography.Paragraph type="secondary" style={{ marginTop: 20, marginBottom: 0, fontSize: 12 }}>
        {t('personalSecurity.note')}
      </Typography.Paragraph>
    </Card>
  )
}
