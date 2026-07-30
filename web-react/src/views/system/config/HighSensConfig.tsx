// 高敏权限自定义追加:默认集只读;追加/删除走 highSensApi。
import { useCallback, useEffect, useState } from 'react'
import { Button, Input, Space, Table, Tag, message } from 'antd'
import { highSensApi } from '@/api'
import { t } from '@/locales'
import { translateError } from '@/utils/error'

export default function HighSensConfig() {
  const [loading, setLoading] = useState(false)
  const [defaults, setDefaults] = useState<string[]>([])
  const [customs, setCustoms] = useState<{ id: number; permissionCode: string; remark?: string | null }[]>([])
  const [code, setCode] = useState('')
  const [remark, setRemark] = useState('')
  const [saving, setSaving] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await highSensApi.list()
      setDefaults(data.defaults ?? [])
      setCustoms(
        (data.customs ?? []).flatMap((item) =>
          item.id == null
            ? []
            : [{ id: Number(item.id), permissionCode: item.permissionCode ?? '', remark: item.remark }],
        ),
      )
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function add() {
    if (!code.trim()) {
      message.warning(t('config.security.highSens.codeRequired'))
      return
    }
    setSaving(true)
    try {
      await highSensApi.add({ permissionCode: code.trim(), remark: remark.trim() || undefined })
      message.success(t('common.success'))
      setCode('')
      setRemark('')
      await load()
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSaving(false)
    }
  }

  async function remove(id: number) {
    setSaving(true)
    try {
      await highSensApi.remove(id)
      message.success(t('common.success'))
      await load()
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <p style={{ margin: '0 0 12px', color: 'var(--color-text-tertiary)', fontSize: 13 }}>
        {t('config.security.highSens.hint')}
      </p>
      <div style={{ marginBottom: 12 }}>
        {defaults.map((d) => (
          <Tag key={d} style={{ marginBottom: 6 }}>{d}</Tag>
        ))}
        {!defaults.length && !loading ? <span style={{ color: 'var(--color-text-tertiary)' }}>—</span> : null}
      </div>
      <Space style={{ marginBottom: 12 }} wrap>
        <Input
          value={code}
          onChange={(e) => setCode(e.target.value)}
          placeholder={t('config.security.highSens.codePh')}
          style={{ width: 320 }}
        />
        <Input
          value={remark}
          onChange={(e) => setRemark(e.target.value)}
          placeholder={t('config.security.highSens.remarkPh')}
          style={{ width: 180 }}
        />
        <Button type="primary" loading={saving} onClick={() => void add()}>
          {t('common.add')}
        </Button>
      </Space>
      <Table
        size="small"
        loading={loading}
        rowKey="id"
        pagination={false}
        dataSource={customs}
        columns={[
          { title: t('config.security.highSens.code'), dataIndex: 'permissionCode', ellipsis: true },
          { title: t('config.security.highSens.remark'), dataIndex: 'remark', width: 160 },
          {
            title: t('common.action'),
            width: 100,
            render: (_, row) => (
              <Button type="link" danger size="small" loading={saving} onClick={() => void remove(row.id)}>
                {t('common.delete')}
              </Button>
            ),
          },
        ]}
      />
    </div>
  )
}
