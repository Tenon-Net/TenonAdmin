// 我的会话(个人视角的登录设备):[ActiveSession] 人人可用,静态路由不进菜单。数据量受会话并发上限约束(个位数),
// 故用静态 antd Table(非 DataTable)+ onMounted 拉一次。踢当前设备置灰(那等于自杀,请走退出登录)。
import { useCallback, useEffect, useMemo, useState } from 'react'
import { App, Button, Card, Table, Tag, type TableColumnsType } from 'antd'
import { useTranslation } from 'react-i18next'
import { useConfirm } from '@/hooks/useConfirm'
import { personalApi } from '@/api'
import type { MySessionItem } from '@/types/api'
import { uaSummary } from '@/utils/ua'
import { translateError } from '@/utils/error'
import { fmtDateTime } from './personalForms'

export default function SessionsPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const [rows, setRows] = useState<MySessionItem[]>([])
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setRows(await personalApi.sessions())
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }, [message])
  useEffect(() => { void load() }, [load])

  const kick = useCallback(
    (r: MySessionItem) => {
      confirm({ content: t('session.kickMineConfirm'), action: () => personalApi.kickSession(r.sessionId), successMsg: t('session.kicked') }).then(
        (ok) => { if (ok) void load() },
      )
    },
    [confirm, t, load],
  )

  const columns = useMemo<TableColumnsType<MySessionItem>>(
    () => [
      {
        title: t('session.device'), key: 'device',
        render: (_, r) => (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
            {uaSummary(r.userAgent)}
            {r.isCurrent && <Tag color="green">{t('session.current')}</Tag>}
          </span>
        ),
      },
      { title: t('session.ip'), dataIndex: 'ip', width: 140, render: (v: string | null) => v || '—' },
      { title: t('session.loginTime'), dataIndex: 'loginTime', width: 180, render: (v: string) => fmtDateTime(v) },
      { title: t('session.expiresAt'), dataIndex: 'expiresAt', width: 180, render: (v: string) => fmtDateTime(v) },
      {
        title: t('common.operation'), key: 'op', width: 110,
        render: (_, r) => (
          <Button type="link" size="small" danger disabled={r.isCurrent} onClick={() => kick(r)}>
            {t('session.kick')}
          </Button>
        ),
      },
    ],
    [t, kick],
  )

  return (
    <Card title={t('session.mineTitle')} style={{ maxWidth: 960 }}>
      <Table<MySessionItem> rowKey="sessionId" columns={columns} dataSource={rows} loading={loading} pagination={false} size="small" />
    </Card>
  )
}
