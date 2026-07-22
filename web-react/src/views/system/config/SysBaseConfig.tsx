// 系统基础 = 结构化表单:字段绑定固定 config key(GroupCode='sys')。载入 listByGroup('sys') 回填,
// 保存 saveBatch(仅回写值)。保存后 loadSite(true) 即时生效(侧栏/顶栏/登录页品牌随之更新)+ 同步浏览器标题。
// 加参数 = SYS_FIELDS 加一项 + LABEL_KEY 加一条 + 一条 config.base i18n。
import { useEffect, useState } from 'react'
import { App, Button, Form, Input, Spin } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { Can } from '@/components/Can'
import { configApi } from '@/api'
import { useSiteStore } from '@/stores/site'
import { translateError } from '@/utils/error'
import { SYS_FIELDS, parseBase, serializeBase } from './configForm'

// config key → i18n label 键(SYS_FIELDS 与之一一对应)
const LABEL_KEY: Record<(typeof SYS_FIELDS)[number], string> = {
  'sys.site.title': 'config.base.siteTitle',
  'sys.site.logo': 'config.base.logo',
  'sys.site.subtitle': 'config.base.siteSubtitle',
  'sys.site.copyright': 'config.base.copyright',
  'sys.site.copyrightUrl': 'config.base.copyrightUrl',
}

export default function SysBaseConfig() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const loadSite = useSiteStore((s) => s.load)
  const [values, setValues] = useState<Record<string, string>>({})
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    configApi
      .listByGroup('sys')
      .then((rows) => setValues(parseBase(rows)))
      .catch((e) => message.error(translateError(e)))
      .finally(() => setLoading(false))
  }, [message])

  const save = async () => {
    setSaving(true)
    try {
      await configApi.saveBatch(serializeBase(values))
      message.success(t('config.saved'))
      // 即时生效:重取全站共用站点信息 + 同步浏览器标题。
      await loadSite(true)
      if (values['sys.site.title']) document.title = values['sys.site.title']
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSaving(false)
    }
  }

  const set = (k: string, v: string) => setValues((prev) => ({ ...prev, [k]: v }))

  return (
    <Spin spinning={loading}>
      <Form labelCol={{ flex: '0 0 90px' }} labelAlign="left" style={{ maxWidth: 560 }}>
        {SYS_FIELDS.map((k) => (
          <Form.Item key={k} label={t(LABEL_KEY[k])}>
            <Input value={values[k] ?? ''} onChange={(e) => set(k, e.target.value)} placeholder={t(LABEL_KEY[k])} />
          </Form.Item>
        ))}
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
