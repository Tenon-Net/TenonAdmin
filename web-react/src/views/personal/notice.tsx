// 我的通知(个人页,非管理端):列出发给自己的通知、读正文、标记已读。走 [ActiveSession] 的 notice/mine,
// 任何登录用户可调,故静态路由不进菜单/权限。正文随列表返回(NoticeMineItem.content),点"查看"不再发请求。
import { useCallback, useMemo, useRef, useState } from 'react'
import { App, Button, Modal, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { AppIcon } from '@/components/AppIcon'
import { MarkdownView } from '@/components/MarkdownView'
import { useConfirm } from '@/hooks/useConfirm'
import { noticeApi } from '@/api'
import { NoticeType, type NoticeMineItem } from '@/types/api'
import { translateError } from '@/utils/error'
import { fmtDateTime } from './personalForms'

export default function NoticePage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { run } = useConfirm()
  const tableRef = useRef<DataTableHandle>(null)

  const fetcher: PageFetcher<NoticeMineItem> = (q) => noticeApi.mine({ page: q.page, pageSize: q.pageSize })

  // ── 查看正文(行内已带 content,不发请求)+ 顺手标记已读 ──
  const [viewRow, setViewRow] = useState<NoticeMineItem | null>(null)
  const openView = useCallback(
    async (r: NoticeMineItem) => {
      setViewRow(r)
      if (r.isRead) return
      try {
        await noticeApi.markRead(r.id)
        tableRef.current?.reload() // 顶栏未读角标由自身轮询自愈
      } catch (e) {
        message.error(translateError(e))
      }
    },
    [message],
  )

  const markAll = useCallback(async () => {
    if (await run(() => noticeApi.markAllRead(), t('notice.allRead'))) tableRef.current?.reload()
  }, [run, t])

  const columns = useMemo<ProColumns<NoticeMineItem>[]>(
    () => [
      {
        title: t('notice.status'), dataIndex: 'isRead', width: 80, search: false,
        render: (_, r) => <Tag color={r.isRead ? undefined : 'green'}>{r.isRead ? t('notice.read') : t('notice.unread')}</Tag>,
      },
      { title: t('notice.noticeTitle'), dataIndex: 'title', search: false },
      {
        title: t('notice.type'), dataIndex: 'type', width: 90, search: false,
        render: (_, r) => (
          <Tag color={r.type === NoticeType.Announcement ? 'orange' : 'blue'}>
            {r.type === NoticeType.Announcement ? t('notice.typeAnnouncement') : t('notice.typeNotice')}
          </Tag>
        ),
      },
      { title: t('notice.publishTime'), dataIndex: 'publishTime', width: 170, search: false, render: (_, r) => fmtDateTime(r.publishTime) },
      {
        title: t('common.operation'), key: 'op', width: 80, search: false, hideInSetting: true,
        render: (_, r) => <Button type="link" size="small" onClick={() => openView(r)}>{t('notice.view')}</Button>,
      },
    ],
    [t, openView],
  )

  return (
    <>
      <DataTable<NoticeMineItem>
        ref={tableRef}
        columns={columns}
        fetcher={fetcher}
        persistKey="personal-notice"
        toolbar={<Button onClick={markAll} icon={<AppIcon icon="ph:checks" size={16} />}>{t('app.notice.markAllRead')}</Button>}
      />
      <Modal open={!!viewRow} onCancel={() => setViewRow(null)} footer={null} title={viewRow?.title || t('notice.detailTitle')} width={720}>
        <MarkdownView value={viewRow?.content ?? ''} />
      </Modal>
    </>
  )
}
