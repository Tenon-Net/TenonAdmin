// 个人安全:TOTP 自助绑定/恢复入口(ADR 0006)。不进业务菜单,顶栏用户下拉进入。
// 管理员配置路径类文案(系统配置/安全策略)只给能进配置的人看,普通用户不暴露运维指引。
import { Alert, Button, Card, Space, Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useUserStore } from '@/stores/user'
import { useHasPerm } from '@/stores/auth'

export default function SecurityPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const account = useUserStore((s) => s.userInfo?.account)
  const hasPerm = useHasPerm()
  // hasPerm 对超管恒 true;普通用户须具备系统配置读权限才看运维提示。
  const showAdminHint = hasPerm('GET:/api/v1/sys/config/page')

  const goBind = (mode?: 'recovery') => {
    const q = new URLSearchParams()
    if (account) q.set('account', account)
    if (mode === 'recovery') q.set('mode', 'recovery')
    const qs = q.toString()
    navigate(qs ? `/mfa/bind?${qs}` : '/mfa/bind')
  }

  return (
    <Card title={t('personalSecurity.title')} style={{ maxWidth: 560 }}>
      <Alert type="info" showIcon title={t('personalSecurity.hint')} style={{ marginBottom: 16 }} />
      {showAdminHint ? (
        <Alert type="warning" showIcon title={t('personalSecurity.adminHint')} style={{ marginBottom: 16 }} />
      ) : null}
      <Typography.Paragraph type="secondary">{t('personalSecurity.bindDesc')}</Typography.Paragraph>
      <Space>
        <Button type="primary" onClick={() => goBind()}>{t('personalSecurity.setupAuthenticator')}</Button>
        <Button type="link" onClick={() => goBind('recovery')}>{t('personalSecurity.useRecovery')}</Button>
      </Space>
      <Typography.Paragraph type="secondary" style={{ marginTop: 20, marginBottom: 0, fontSize: 12 }}>
        {t('personalSecurity.note')}
      </Typography.Paragraph>
    </Card>
  )
}
