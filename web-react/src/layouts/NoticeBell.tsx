// 顶栏消息通知铃铛(与 Vue layouts/NoticeBell.vue 同构):富面板 Popover —— 无页签单列表 + 正文弹层。
// 未读=圆点+加粗+淡主色底,已读=整行变淡;"读没读"是过滤器不是分类,不做成页签(参考普查结论,筛选在个人通知页)。
// 未读数双腿:订阅 noticeBus(SignalR 推送 `notice-changed` 即刻重拉)+ 30s 轮询兜底;失败静默不糊顶栏。
import { useEffect, useState } from 'react'
import { App, Badge, Button, Empty, Modal, Popover, Spin, Tag, Typography } from 'antd'
import { BellOutlined, CheckOutlined, RightOutlined } from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { noticeApi } from '@/api'
import { noticeBus } from '@/composables/useRealtime'
import { MarkdownView } from '@/components/MarkdownView'
import { translateError } from '@/utils/error'
import { NoticeType, type NoticeMineItem } from '@/types/api'
import './noticebell.css'

/** 正文摘要:去掉常见 Markdown 记号,压空白,截断。ponytail: 正则够用,要精准渲染再引 md 解析。 */
function snippet(md?: string | null): string {
  if (!md) return ''
  const s = md
    .replace(/!\[[^\]]*\]\([^)]*\)/g, '') // 图片
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1') // 链接留文字
    .replace(/[#>*_`~-]/g, '') // 记号
    .replace(/\s+/g, ' ')
    .trim()
  return s.length > 60 ? s.slice(0, 60) + '…' : s
}
/** 短时间戳(本地,YYYY-MM-DD HH:mm)。ponytail: 够看,要"3 分钟前"再引相对时间库。 */
const shortTime = (iso: string) => (iso ?? '').replace('T', ' ').slice(0, 16)

// 类型 → 标签色。新增类型在此补一行即可(文案见组件内 typeLabel)。
const tagColor = (ty: NoticeType) =>
  ty === NoticeType.Announcement ? 'warning' : ty === NoticeType.Message ? 'success' : 'processing'

export function NoticeBell() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const navigate = useNavigate()

  const [open, setOpen] = useState(false)
  const [unread, setUnread] = useState(0)
  const [items, setItems] = useState<NoticeMineItem[]>([])
  const [loading, setLoading] = useState(false)

  const fetchUnread = () => noticeApi.unreadCount().then(setUnread).catch(() => {})
  // 无页签单列表:恒拉最近一页混排,未读靠条目样式强调;筛选能力在"查看全部"的个人通知页。
  const fetchList = () => {
    setLoading(true)
    noticeApi
      .mine({ page: 1, pageSize: 10 })
      .then(({ items: list }) => setItems(list))
      .catch(() => setItems([]))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    void fetchUnread()
    const id = setInterval(() => void fetchUnread(), 30000)
    const off = noticeBus.on(() => void fetchUnread())
    return () => {
      clearInterval(id)
      off()
    }
  }, [])

  const onOpenChange = (o: boolean) => {
    setOpen(o)
    if (o) fetchList()
  }

  // 正文弹层:内容随列表取回,直接渲染(Markdown);点条目即标记已读。
  const [showNotice, setShowNotice] = useState(false)
  const [viewNotice, setViewNotice] = useState<NoticeMineItem | null>(null)
  const openNotice = async (item: NoticeMineItem) => {
    setViewNotice(item)
    setShowNotice(true)
    if (item.isRead) return
    try {
      await noticeApi.markRead(item.id)
      setItems((prev) => prev.map((n) => (n.id === item.id ? { ...n, isRead: true } : n)))
      await fetchUnread()
    } catch (e) {
      message.error(translateError(e))
    }
  }
  const markAllRead = async () => {
    try {
      await noticeApi.markAllRead()
      await fetchUnread()
      fetchList()
    } catch (e) {
      message.error(translateError(e))
    }
  }

  const typeLabel = (ty: NoticeType) =>
    ty === NoticeType.Announcement ? t('notice.typeAnnouncement') : ty === NoticeType.Message ? t('notice.typeMessage') : t('notice.typeNotice')

  const panel = (
    <div className="notice-panel">
      <div className="notice-head">
        <span className="notice-title">{t('app.notice.title')}</span>
        <Button type="link" size="small" disabled={unread === 0} icon={<CheckOutlined />} onClick={() => void markAllRead()}>
          {t('app.notice.markAllRead')}
        </Button>
      </div>

      <Spin spinning={loading}>
        <div className="notice-scroll">
          {items.length === 0 ? (
            <div className="notice-empty">
              <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('app.notice.empty')} />
            </div>
          ) : (
            <ul className="notice-list">
              {items.map((n) => (
                <li key={n.id} className={`notice-item ${n.isRead ? 'read' : 'unread'}`} onClick={() => void openNotice(n)}>
                  {!n.isRead ? <span className="dot" /> : null}
                  <div className="notice-body">
                    <div className="line1">
                      <Tag color={tagColor(n.type)}>{typeLabel(n.type)}</Tag>
                      <span className={`item-title${n.isRead ? '' : ' unread'}`}>{n.title}</span>
                    </div>
                    {snippet(n.content) ? (
                      <Typography.Text type="secondary" className="item-snippet">
                        {snippet(n.content)}
                      </Typography.Text>
                    ) : null}
                    <Typography.Text type="secondary" className="item-time">
                      {shortTime(n.publishTime)}
                    </Typography.Text>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      </Spin>

      <div className="notice-foot">
        <Button
          type="link"
          size="small"
          onClick={() => {
            setOpen(false)
            void navigate('/personal/notice')
          }}
        >
          {t('app.notice.viewAll')} <RightOutlined />
        </Button>
      </div>
    </div>
  )

  return (
    <>
      {/* container 去内边距:面板自带分区留白,默认 padding 会让列表两侧双重缝 */}
      <Popover open={open} onOpenChange={onOpenChange} trigger="click" placement="bottomRight" arrow={false} styles={{ container: { padding: 0 } }} content={panel}>
        <Badge count={unread} size="small" offset={[-2, 4]}>
          <Button type="text" aria-label={t('app.notice.title')} icon={<BellOutlined />} />
        </Badge>
      </Popover>

      <Modal open={showNotice} onCancel={() => setShowNotice(false)} footer={null} title={viewNotice?.title || t('notice.detailTitle')} width={640}>
        <MarkdownView value={viewNotice?.content} />
      </Modal>
    </>
  )
}
