// 系统工作台首页(builtin 应用,菜单 component=dashboard/workbench)。欢迎横幅 + 4 统计卡 + 趋势/分布两图。
// 统计取不到不糊用户一脸红:catch 静默 → 计数显 —、图空,页面其余照常。图 option 纯逻辑抽 chartOptions.ts(变异钉)。
import { useEffect, useMemo, useState } from 'react'
import { Card, Col, Row } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { Chart } from '@/components/Chart'
import { useUserStore } from '@/stores/user'
import { useAppStore } from '@/stores/app'
import { btnGrad } from '@/theme/mix'
import { dashboardApi } from '@/api'
import type { DashboardSummary } from '@/types/api'
import { buildLineOption, buildPieOption, num } from './chartOptions'
import './dashboard.css'

export default function Workbench() {
  const { t } = useTranslation()
  const userInfo = useUserStore((s) => s.userInfo)
  const accent = useAppStore((s) => s.accent)
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    dashboardApi
      .summary()
      .then(setSummary)
      .catch(() => {}) // 首页不因一张统计图挂了就报错糊脸
      .finally(() => setLoading(false))
  }, [])

  const stats = [
    { key: 'roles', icon: 'ph:shield-duotone', value: num(summary?.roles), color: 'var(--color-primary)' },
    { key: 'users', icon: 'ph:users-duotone', value: num(summary?.users), color: 'var(--color-success)' },
    { key: 'perms', icon: 'ph:key-duotone', value: num(summary?.perms), color: 'var(--color-warning)' },
    { key: 'online', icon: 'ph:broadcast-duotone', value: num(summary?.onlineSessions), color: 'var(--color-danger)' },
  ]

  const trendOption = useMemo(
    () => buildLineOption(summary?.trendDays ?? [], [
      { name: t('workbench.trendLogins'), data: summary?.trendLogins ?? [] },
      { name: t('workbench.trendActive'), data: summary?.trendActiveUsers ?? [] },
    ]),
    [summary, t],
  )
  // 资源分布就是前三个计数,不必让后端再返一份
  const distributionOption = useMemo(
    () => buildPieOption([
      { name: t('workbench.roles'), value: summary?.roles ?? 0 },
      { name: t('workbench.users'), value: summary?.users ?? 0 },
      { name: t('workbench.perms'), value: summary?.perms ?? 0 },
    ]),
    [summary, t],
  )

  return (
    <div className="dash-view">
      <div className="dash-banner">
        <div className="dash-avatar" style={{ background: btnGrad(accent) }}>
          <AppIcon icon="ph:user" size={26} style={{ color: '#fff' }} />
        </div>
        <div>
          <div className="dash-hi">{t('workbench.welcome', { name: userInfo?.name ?? '' })}</div>
          <div className="dash-tip">{t('workbench.subtitle')}</div>
        </div>
      </div>

      <Row gutter={[16, 16]}>
        {stats.map((s) => (
          <Col key={s.key} xs={24} sm={12} lg={6}>
            <Card>
              <div className="dash-stat">
                <div className="dash-stat-ico" style={{ color: s.color }}>
                  <AppIcon icon={s.icon} size={28} />
                </div>
                <div>
                  <div className="dash-stat-val tabular">{s.value}</div>
                  <div className="dash-stat-label">{t(`workbench.${s.key}`)}</div>
                </div>
              </div>
            </Card>
          </Col>
        ))}
      </Row>

      <Row gutter={[16, 16]}>
        <Col xs={24} lg={12}>
          <Card title={t('workbench.trendTitle')}>
            <Chart option={trendOption} loading={loading} height={300} />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card title={t('workbench.distributionTitle')}>
            <Chart option={distributionOption} loading={loading} height={300} />
          </Card>
        </Col>
      </Row>
    </div>
  )
}
