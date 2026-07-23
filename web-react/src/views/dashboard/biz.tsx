// 业务应用工作台首页(business 应用,菜单 component=dashboard/biz)。每应用一个首页,切应用即换首页。
// 全真数据:欢迎卡(user)+ 快捷入口(当前应用菜单可见叶子,排除本页)+ 我的通知(notice/mine 前 5)。
// 消费者拿模板后把后两卡换成自己的业务统计,别造写死假数字。visibleLeaves/fmtTime 纯逻辑抽 chartOptions.ts(变异钉)。
import { useEffect, useMemo, useState } from 'react'
import { Button, Card, Empty, List } from 'antd'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router-dom'
import { AppIcon } from '@/components/AppIcon'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { useAppStore } from '@/stores/app'
import { btnGrad } from '@/theme/mix'
import { noticeApi } from '@/api'
import type { NoticeMineItem } from '@/types/api'
import { fmtTime, visibleLeaves } from './chartOptions'
import './dashboard.css'

export default function Biz() {
  const { t } = useTranslation()
  const userInfo = useUserStore((s) => s.userInfo)
  const menuTree = useAuthStore((s) => s.menuTree)
  const accent = useAppStore((s) => s.accent)
  const navigate = useNavigate()
  const { pathname } = useLocation()

  const quick = useMemo(() => visibleLeaves(menuTree, pathname), [menuTree, pathname])

  const [notices, setNotices] = useState<NoticeMineItem[]>([])
  useEffect(() => {
    noticeApi.mine({ page: 1, pageSize: 5 }).then((r) => setNotices(r.items)).catch(() => {}) // 通知取不到就空态,不糊脸
  }, [])

  return (
    <div className="dash-view">
      <div className="dash-banner">
        <div className="dash-avatar" style={{ background: btnGrad(accent) }}>
          <AppIcon icon="ph:user" size={26} style={{ color: '#fff' }} />
        </div>
        <div>
          <div className="dash-hi">{t('biz.welcome', { name: userInfo?.name ?? userInfo?.account ?? '' })}</div>
          <div className="dash-tip">{t('biz.subtitle')}</div>
        </div>
      </div>

      <Card title={t('biz.quick')}>
        {quick.length ? (
          <div className="dash-quick">
            {quick.map((m) => (
              <Button key={m.id} onClick={() => navigate(m.path!)} icon={<AppIcon icon={m.icon || 'ph:squares-four'} size={16} />}>
                {m.title}
              </Button>
            ))}
          </div>
        ) : (
          <Empty description={t('biz.noMenus')} />
        )}
      </Card>

      {/* 「查看全部」跳 /personal/notice(C12b 落地静态路由;届时贯通) */}
      <Card title={t('biz.notices')} extra={<Button type="link" onClick={() => navigate('/personal/notice')}>{t('biz.viewAll')}</Button>}>
        {notices.length ? (
          <List
            split={false}
            dataSource={notices}
            renderItem={(n) => (
              <List.Item style={{ padding: '2px 0' }}>
                <div className="dash-notice-row" onClick={() => navigate('/personal/notice')}>
                  <span className="dash-notice-title">
                    {!n.isRead && <span className="dash-dot" />}
                    {n.title}
                  </span>
                  <span className="dash-notice-time">{fmtTime(n.publishTime)}</span>
                </div>
              </List.Item>
            )}
          />
        ) : (
          <Empty />
        )}
      </Card>
    </div>
  )
}
