// 任务监控(G8):4 张 stat 卡 + 近 14 日成败趋势(Chart 双序列)+ 即将执行表 + 集群节点表。
// 整页 15 秒轮询 dashboard(卸载清定时器);轮询失败按 key 去重弹错,不叠一屏 toast。
// 纯逻辑在 jobMonitorFormat.ts(变异钉);对齐 system/monitor 页的卡片排版语汇。
import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { App, Card, Col, Row, Table, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { Chart } from '@/components/Chart'
import { jobApi } from '@/api'
import { translateError } from '@/utils/error'
import type { JobDashboard, JobNodeItem, JobUpcomingItem } from '@/types/api'
import { buildJobTrendOption, heartbeatAge } from './jobMonitorFormat'

const metric: CSSProperties = { fontSize: 28, fontWeight: 600, lineHeight: 1.2 }
const sub: CSSProperties = { marginTop: 8, fontSize: 12, color: 'var(--color-text-secondary, #999)' }

/** ISO 时刻截到秒、去 T。 */
const fmtTime = (s?: string | null): string => (s ? s.replace('T', ' ').slice(0, 19) : '—')

export default function JobMonitorPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const [data, setData] = useState<JobDashboard | null>(null)
  const [now, setNow] = useState(() => Date.now())

  // 15s 轮询;组件卸载清定时器 + alive 旗标掐掉迟到的 setState。
  useEffect(() => {
    let alive = true
    const load = async () => {
      try {
        const d = await jobApi.dashboard()
        if (!alive) return
        setData(d)
        setNow(Date.now()) // 心跳相对时长以本次刷新时刻为基准
      } catch (e) {
        if (alive) message.error({ content: translateError(e), key: 'job-monitor' })
      }
    }
    void load()
    const timer = setInterval(() => void load(), 15_000)
    return () => {
      alive = false
      clearInterval(timer)
    }
  }, [message])

  const trendOption = useMemo(
    () =>
      data
        ? buildJobTrendOption(
            data.trend.map((p) => p.date.slice(5)), // 'MM-DD' 够用,14 天跨不了年到看不懂
            data.trend.map((p) => p.success),
            data.trend.map((p) => p.failed),
            { success: t('job.monitor.success'), failed: t('job.monitor.failed') },
          )
        : null,
    [data, t],
  )

  /** 最后心跳的相对时间(以最近一次轮询时刻为"现在")。 */
  const relTime = (iso: string): string => {
    const s = heartbeatAge(iso, now)
    if (s < 10) return t('job.monitor.justNow')
    if (s < 60) return t('job.monitor.secondsAgo', { n: s })
    if (s < 3600) return t('job.monitor.minutesAgo', { n: Math.floor(s / 60) })
    if (s < 86400) return t('job.monitor.hoursAgo', { n: Math.floor(s / 3600) })
    return fmtTime(iso)
  }

  const sc = data?.statusCounts ?? {}

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <Row gutter={[12, 12]}>
        <Col xs={12} lg={6}>
          <Card size="small" title={t('job.monitor.todaySuccess')}>
            <div style={{ ...metric, color: 'var(--color-success, #18a058)' }}>{data ? data.todaySuccess : '—'}</div>
          </Card>
        </Col>
        <Col xs={12} lg={6}>
          <Card size="small" title={t('job.monitor.todayFailed')}>
            <div style={{ ...metric, color: data && data.todayFailed > 0 ? 'var(--color-danger, #d03050)' : undefined }}>
              {data ? data.todayFailed : '—'}
            </div>
          </Card>
        </Col>
        <Col xs={12} lg={6}>
          <Card size="small" title={t('job.monitor.running')}>
            <div style={metric}>{data ? data.running : '—'}</div>
          </Card>
        </Col>
        <Col xs={12} lg={6}>
          <Card size="small" title={t('job.monitor.totalJobs')}>
            <div style={metric}>{data ? data.totalJobs : '—'}</div>
            <div style={sub}>
              {t('job.status.ready')} {sc.Ready ?? 0} · {t('job.status.paused')} {sc.Paused ?? 0} · {t('job.status.completed')}{' '}
              {sc.Completed ?? 0} · {t('job.status.panic')} {sc.Panic ?? 0}
            </div>
          </Card>
        </Col>
      </Row>

      <Row gutter={[12, 12]}>
        <Col xs={24} lg={14}>
          <Card size="small" title={t('job.monitor.trend')}>
            {trendOption ? <Chart option={trendOption} height={280} /> : <div style={{ height: 280 }} />}
          </Card>
        </Col>
        <Col xs={24} lg={10}>
          <Card size="small" title={t('job.monitor.upcoming')}>
            <Table<JobUpcomingItem>
              size="small"
              rowKey={(r) => `${r.jobId}-${r.nextRunTime}`}
              pagination={false}
              dataSource={data?.upcoming ?? []}
              columns={[
                { title: t('job.name'), dataIndex: 'name', ellipsis: true },
                { title: t('job.monitor.fireTime'), dataIndex: 'nextRunTime', width: 170, render: (v: string) => fmtTime(v) },
              ]}
            />
          </Card>
        </Col>
      </Row>

      <Card size="small" title={t('job.monitor.nodes')}>
        <Table<JobNodeItem>
          size="small"
          rowKey="nodeName"
          pagination={false}
          dataSource={data?.nodes ?? []}
          columns={[
            { title: t('job.monitor.node'), dataIndex: 'nodeName', ellipsis: true },
            { title: t('job.monitor.host'), dataIndex: 'hostName', ellipsis: true },
            {
              title: t('job.monitor.role'), dataIndex: 'isLeader', width: 110,
              render: (v: boolean) => (v ? <Tag color="processing">leader</Tag> : <Tag>standby</Tag>),
            },
            { title: t('job.monitor.lastHeartbeat'), dataIndex: 'lastHeartbeat', width: 150, render: (v: string) => relTime(v) },
            { title: 'WorkerId', dataIndex: 'workerId', width: 90 },
            { title: 'PID', dataIndex: 'pid', width: 90 },
          ]}
        />
      </Card>
    </div>
  )
}
