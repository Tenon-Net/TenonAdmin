// 个人资料:自己能改的全在这(姓名/昵称/性别/手机/邮箱/头像);账号/机构/职位只读展示(管理员维护)。
// 提交是全量替换(后端 UpdateProfileInput),整表单一把梭。手机/邮箱直改不做二次验证(内核无短信/邮件通道,操作日志留痕)。
// 载入/保存字段映射纯逻辑抽 personalForms.ts(变异钉)。
import { useEffect, useState } from 'react'
import { App, Avatar, Button, Card, Form, Input, Spin } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { DictSelect } from '@/components/DictSelect'
import { FileUpload } from '@/components/FileUpload'
import { personalApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { translateError } from '@/utils/error'
import { formToUpdate, initial, isEmail, isPhone, profileToForm, type ProfileEditable } from './personalForms'

export default function ProfilePage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const [form] = Form.useForm<ProfileEditable>()
  const avatar = Form.useWatch('avatar', form)
  const name = Form.useWatch('name', form) ?? ''
  const [ro, setRo] = useState({ account: '', org: '—', position: '—' }) // 只读展示字段,不入表单
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [avatarUploading, setAvatarUploading] = useState(false)

  useEffect(() => {
    personalApi
      .profile()
      .then((p) => {
        const f = profileToForm(p)
        setRo({ account: f.account, org: f.org, position: f.position })
        form.setFieldsValue({ name: f.name, nickname: f.nickname, gender: f.gender, phone: f.phone, email: f.email, avatar: f.avatar })
      })
      .catch((e) => message.error(translateError(e)))
      .finally(() => setLoading(false))
  }, [form, message])

  const save = async () => {
    let v: ProfileEditable
    try {
      v = await form.validateFields() // 6 字段全注册 → 全量(无 C9 漏字段)
    } catch {
      return // 校验失败:antd 已在字段标红,静默返回(onClick 直调,须自吞 rejection 否则 unhandled)
    }
    setSaving(true)
    try {
      await personalApi.updateProfile(formToUpdate(v))
      // 顶栏头像/名即时同步,不等下次 enterInitial
      const u = useUserStore.getState().userInfo
      if (u) useUserStore.setState({ userInfo: { ...u, name: v.name, avatar: v.avatar } })
      message.success(t('profile.saved'))
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Card title={t('profile.title')} style={{ maxWidth: 560 }}>
      <Spin spinning={loading}>
        <Form form={form} labelCol={{ flex: '0 0 80px' }} labelAlign="left">
          <Form.Item label={t('profile.avatar')}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              <Avatar size={48} src={avatar || undefined} style={{ flexShrink: 0 }}>
                {initial(name)}
              </Avatar>
              <FileUpload
                accept="image/*"
                showUploadList={false}
                onLoadingChange={setAvatarUploading}
                onUploaded={(o) => form.setFieldValue('avatar', o.viewUrl ?? null)}
              >
                <Button size="small" loading={avatarUploading} disabled={avatarUploading} icon={<AppIcon icon="ph:upload-simple" size={14} />}>
                  {avatarUploading ? t('profile.avatarUploading') : t('profile.avatar')}
                </Button>
              </FileUpload>
            </div>
          </Form.Item>
          {/* avatar 隐藏注册项:FileUpload 经 setFieldValue 写入,validateFields 才会带上(防 C9 漏字段) */}
          <Form.Item name="avatar" hidden>
            <Input />
          </Form.Item>
          <Form.Item label={t('profile.account')}>
            <Input value={ro.account} disabled />
          </Form.Item>
          <Form.Item name="name" label={t('profile.name')} rules={[{ required: true, whitespace: true, message: t('profile.name') }]}>
            <Input placeholder={t('profile.name')} />
          </Form.Item>
          <Form.Item name="nickname" label={t('profile.nickname')}>
            <Input placeholder={t('profile.nickname')} />
          </Form.Item>
          <Form.Item name="gender" label={t('profile.gender')}>
            <DictSelect typeCode="gender" allowClear placeholder={t('profile.gender')} />
          </Form.Item>
          <Form.Item
            name="phone"
            label={t('profile.phone')}
            rules={[{ validator: (_, v) => (isPhone(v ?? '') ? Promise.resolve() : Promise.reject(new Error(t('profile.phoneInvalid')))) }]}
          >
            <Input placeholder={t('profile.phone')} />
          </Form.Item>
          <Form.Item
            name="email"
            label={t('profile.email')}
            rules={[{ validator: (_, v) => (isEmail(v ?? '') ? Promise.resolve() : Promise.reject(new Error(t('profile.emailInvalid')))) }]}
          >
            <Input placeholder={t('profile.email')} />
          </Form.Item>
          <Form.Item label={t('profile.org')}>
            <Input value={ro.org} disabled />
          </Form.Item>
          <Form.Item label={t('profile.position')}>
            <Input value={ro.position} disabled />
          </Form.Item>
          <Form.Item label=" " colon={false}>
            <Button type="primary" loading={saving} onClick={save}>
              {t('common.save')}
            </Button>
          </Form.Item>
        </Form>
      </Spin>
    </Card>
  )
}
