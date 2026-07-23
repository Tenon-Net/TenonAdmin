// 修改密码:old/new/confirm。复杂度不进 rules —— PasswordStrength 实时显示策略清单、后端强制(弱口令 passwordTooWeak),
// 再抄一份进 rules 就是第三处真源。成功后强制重新登录(logout → 清会话 → /login);mustChangePassword 时顶部提示。
// 本页在两处渲染:强改守卫(Protected.tsx)全屏、顶栏用户下拉壳内;自包含表单,两种上下文都可用。
import { useState } from 'react'
import { Alert, App, Button, Card, Form, Input } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { PasswordStrength } from '@/components/PasswordStrength'
import { authApi, personalApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'

interface PwForm {
  oldPassword: string
  newPassword: string
  confirmPassword: string
}

export default function PasswordPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const [form] = Form.useForm<PwForm>()
  const newPassword = Form.useWatch('newPassword', form) ?? ''
  const [saving, setSaving] = useState(false)
  const mustChange = useUserStore((s) => s.userInfo?.mustChangePassword ?? false)

  const submit = async () => {
    let v: PwForm
    try {
      v = await form.validateFields()
    } catch {
      return // 校验失败:antd 已在字段标红,静默返回(onClick 直调,须自吞 rejection 否则 unhandled)
    }
    setSaving(true)
    try {
      await personalApi.updatePassword({ oldPassword: v.oldPassword, newPassword: v.newPassword })
      message.success(t('changePassword.changed'))
      // 改密后强制重新登录:登出尽力而为,清会话 + 动态路由,回落 /login。
      try {
        await authApi.logout()
      } catch {
        /* 尽力而为 */
      }
      useAuthStore.getState().reset()
      useUserStore.getState().clear()
      navigate('/login', { replace: true })
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Card title={t('changePassword.title')} style={{ maxWidth: 460 }}>
      {mustChange && <Alert type="warning" showIcon style={{ marginBottom: 16 }} title={t('changePassword.forcedHint')} />}
      <Form form={form} layout="vertical" onFinish={submit} requiredMark={false}>
        <Form.Item name="oldPassword" label={t('changePassword.oldPassword')} rules={[{ required: true, message: t('changePassword.oldRequired') }]}>
          <Input.Password />
        </Form.Item>
        <Form.Item name="newPassword" label={t('changePassword.newPassword')} rules={[{ required: true, message: t('changePassword.newRequired') }]}>
          <Input.Password />
        </Form.Item>
        <PasswordStrength value={newPassword} />
        <Form.Item
          name="confirmPassword"
          label={t('changePassword.confirmPassword')}
          dependencies={['newPassword']}
          rules={[
            { required: true, message: t('changePassword.confirmRequired') },
            // 「等于新密码」antd 没内置规则,自写:与另一字段比对
            ({ getFieldValue }) => ({
              validator: (_, value) =>
                !value || value === getFieldValue('newPassword') ? Promise.resolve() : Promise.reject(new Error(t('changePassword.mismatch'))),
            }),
          ]}
        >
          <Input.Password />
        </Form.Item>
        <Button type="primary" block loading={saving} onClick={submit}>
          {t('common.submit')}
        </Button>
      </Form>
    </Card>
  )
}
