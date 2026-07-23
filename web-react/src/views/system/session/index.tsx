// 在线会话页:只读列表 + 行内「强制下线」。无表单、无业务搜索(后端仅按 UserId 过滤)。
// 踢人走 useConfirm 二次确认;踢**自己**的会话置灰(防误把自己下线)。设备列由 uaSummary 解析 UA。
import { useCallback, useMemo, useRef } from 'react'
import { Button } from 'antd'
import { useTranslation } from 'react-i18next'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import type { ProColumns } from '@ant-design/pro-components'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { useUserStore } from '@/stores/user'
import { sessionApi } from '@/api'
import { uaSummary } from '@/utils/ua'
import type { OnlineSessionItem } from '@/types/api'

export default function SessionPage() {
  const { t } = useTranslation()
  const { confirm } = useConfirm()
  const has = useHasPerm()
  const currentUserId = useUserStore((s) => s.userInfo?.userId)

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  const fetchSessions: PageFetcher<OnlineSessionItem> = (q) => sessionApi.online({ page: q.page, pageSize: q.pageSize })

  const kick = useCallback(
    (r: OnlineSessionItem) => {
      confirm({ content: t('session.kickConfirm', { account: r.account }), action: () => sessionApi.kick(r.sessionId), successMsg: t('session.kicked') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, reload],
  )

  const columns = useMemo<ProColumns<OnlineSessionItem>[]>(
    () => [
      { title: t('session.account'), dataIndex: 'account', search: false },
      { title: t('session.ip'), dataIndex: 'ip', search: false, render: (_, r) => r.ip || '—' },
      // 设备:同一账号多端在线时靠它分辨要踢哪台(UA 解析成「浏览器 · 系统」)
      { title: t('session.device'), dataIndex: 'device', search: false, ellipsis: true, render: (_, r) => uaSummary(r.userAgent) },
      { title: t('session.loginTime'), dataIndex: 'loginTime', search: false, width: 170 },
      { title: t('session.expiresAt'), dataIndex: 'expiresAt', search: false, width: 170 },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 120, fixed: 'right',
        render: (_, r) => {
          if (!has('DELETE:/api/v1/sys/session/{sessionid}')) return null
          const isSelf = r.userId === currentUserId
          // 踢自己的会话置灰:防误把自己下线
          return (
            <Button type="link" size="small" danger disabled={isSelf} onClick={() => kick(r)}>
              {isSelf ? t('session.self') : t('session.kick')}
            </Button>
          )
        },
      },
    ],
    [t, has, currentUserId, kick],
  )

  return (
    <DataTable<OnlineSessionItem>
      ref={tableRef}
      columns={columns}
      fetcher={fetchSessions}
      persistKey="sys-session"
    />
  )
}
