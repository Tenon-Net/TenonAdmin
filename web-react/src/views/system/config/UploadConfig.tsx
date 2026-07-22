// 上传策略 = 结构化表单:单文件大小上限 + 后缀白名单(GroupCode='upload')。
// 后端 FileService.UploadAsync 读这两键强制执行,改值即时生效、无需重发。后缀以逗号分隔字符串落库。
import { useEffect, useState } from 'react'
import { App, Button, Form, InputNumber, Select, Spin } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { Can } from '@/components/Can'
import { configApi } from '@/api'
import { translateError } from '@/utils/error'
import { parseUpload, serializeUpload } from './configForm'

export default function UploadConfig() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const [maxSizeMb, setMaxSizeMb] = useState(20)
  const [exts, setExts] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    configApi
      .listByGroup('upload')
      .then((rows) => {
        const u = parseUpload(rows)
        setMaxSizeMb(u.maxSizeMb)
        setExts(u.exts)
      })
      .catch((e) => message.error(translateError(e)))
      .finally(() => setLoading(false))
  }, [message])

  const save = async () => {
    setSaving(true)
    try {
      await configApi.saveBatch(serializeUpload(maxSizeMb, exts))
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
        <Form.Item label={t('config.upload.maxSizeMb')}>
          <InputNumber min={1} value={maxSizeMb} onChange={(v) => setMaxSizeMb(v ?? 1)} style={{ width: 160 }} />
        </Form.Item>
        {/* NDynamicTags 的 antd 等价:mode="tags" 自由输入(回车/逗号成标签),保存时 normalizeExt 补点转小写。 */}
        <Form.Item label={t('config.upload.allowedExtensions')} extra={t('config.upload.extTip')}>
          <Select
            mode="tags"
            value={exts}
            onChange={setExts}
            tokenSeparators={[',']}
            suffixIcon={null}
            notFoundContent={null}
            placeholder={t('config.upload.extPlaceholder')}
            style={{ width: '100%' }}
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
