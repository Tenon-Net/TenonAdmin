import { useEffect, useState } from 'react'
import { App, Button, Form, Input, InputNumber, Spin } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { Can } from '@/components/Can'
import { configApi } from '@/api'
import { translateError } from '@/utils/error'
import { rowsToMap } from './configForm'

const KEY_LOG_RETENTION = 'sys.job.logRetentionDays'
const KEY_ALERT_EMAILS = 'sys.job.alertEmails'

export default function JobConfig() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const [logRetentionDays, setLogRetentionDays] = useState(30)
  const [alertEmails, setAlertEmails] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    configApi
      .listByGroup('job')
      .then((rows) => {
        const map = rowsToMap(rows)
        setLogRetentionDays(Number(map.get(KEY_LOG_RETENTION)) || 30)
        setAlertEmails(map.get(KEY_ALERT_EMAILS) ?? '')
      })
      .catch((e) => message.error(translateError(e)))
      .finally(() => setLoading(false))
  }, [message])

  const save = async () => {
    setSaving(true)
    try {
      await configApi.saveBatch([
        { configKey: KEY_LOG_RETENTION, configValue: String(logRetentionDays) },
        { configKey: KEY_ALERT_EMAILS, configValue: alertEmails },
      ])
      message.success(t('config.saved'))
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Spin spinning={loading}>
      <Form labelCol={{ flex: '0 0 150px' }} labelAlign="left" style={{ maxWidth: 560 }}>
        <Form.Item label={t('config.job.logRetentionDays')}>
          <InputNumber min={1} value={logRetentionDays} onChange={(v) => setLogRetentionDays(v ?? 1)} style={{ width: 160 }} />
        </Form.Item>
        <Form.Item label={t('config.job.alertEmails')} extra={t('config.job.alertEmailsHint')}>
          <Input.TextArea
            value={alertEmails}
            onChange={(e) => setAlertEmails(e.target.value)}
            autoSize={{ minRows: 2 }}
          />
        </Form.Item>
        <Form.Item label=" " colon={false}>
          <Can code="PUT:/api/v1/sys/config/batch">
            <Button type="primary" loading={saving} onClick={save} icon={<AppIcon icon="ph:floppy-disk" size={16} />}>
              {t('common.save')}
            </Button>
          </Can>
        </Form.Item>
      </Form>
    </Spin>
  )
}
