// 服务器监控页:卡片 + 手动刷新(**不轮询**:CPU 快照后端每次采样约 500ms,自动轮询会持续占用)。
// 数值格式化/占用染色抽到 monitorFormat.ts(变异钉);本页只做接线与展示。
import { useCallback, useEffect, useState, type CSSProperties } from 'react'
import { App, Button, Card, Col, Progress, Row, Spin } from 'antd'
import { useTranslation } from 'react-i18next'
import { monitorApi } from '@/api'
import { translateError } from '@/utils/error'
import type { ServerInfoOutput } from '@/types/api'
import { fmtBytes, pct, uptimeParts, usageColor } from './monitorFormat'

const metric: CSSProperties = { fontSize: 28, fontWeight: 600, lineHeight: 1.2, marginBottom: 10 }
const unit: CSSProperties = { fontSize: 16, marginLeft: 2, color: 'var(--color-text-secondary, #999)' }
const sub: CSSProperties = { marginTop: 8, fontSize: 12, color: 'var(--color-text-secondary, #999)' }
const keyCell: CSSProperties = { display: 'inline-block', width: 88, color: 'var(--color-text-secondary, #999)' }

export default function MonitorPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const [info, setInfo] = useState<ServerInfoOutput | null>(null)
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setInfo(await monitorApi.server())
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }, [message])
  useEffect(() => { void load() }, [load])

  const fmtUptime = (s: number): string => {
    const { days, hours, minutes } = uptimeParts(s)
    return [days ? `${days}${t('monitor.days')}` : '', hours ? `${hours}${t('monitor.hours')}` : '', `${minutes}${t('monitor.minutes')}`]
      .filter(Boolean).join(' ')
  }

  const cpu = info ? Math.round(info.processCpuPercent) : 0
  const memPct = info ? pct(info.processWorkingSetBytes, info.totalAvailableMemoryBytes) : 0

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
        <Button size="small" loading={loading} onClick={load}>{t('monitor.refresh')}</Button>
      </div>
      <Spin spinning={loading}>
        {info && (
          <Row gutter={[12, 12]}>
            <Col xs={24} sm={12} lg={6}>
              <Card size="small" title={t('monitor.cpu')}>
                <div style={metric}>{info.processCpuPercent.toFixed(1)}<span style={unit}>%</span></div>
                <Progress percent={cpu} strokeColor={usageColor(cpu)} showInfo={false} />
                <div style={sub}>{t('monitor.processorCount')}: {info.processorCount} · {t('monitor.threadCount')}: {info.threadCount}</div>
              </Card>
            </Col>
            <Col xs={24} sm={12} lg={6}>
              <Card size="small" title={t('monitor.memory')}>
                <div style={metric}>{fmtBytes(info.processWorkingSetBytes)}</div>
                <Progress percent={memPct} strokeColor={usageColor(memPct)} showInfo={false} />
                <div style={sub}>{t('monitor.gcHeap')}: {fmtBytes(info.gcHeapBytes)} · {t('monitor.totalMemory')}: {fmtBytes(info.totalAvailableMemoryBytes)}</div>
              </Card>
            </Col>
            <Col xs={24} lg={12}>
              <Card size="small" title={t('monitor.runtime')}>
                <div style={{ display: 'grid', gap: 8, fontSize: 13 }}>
                  <div><span style={keyCell}>{t('monitor.machineName')}</span><span style={{ wordBreak: 'break-all' }}>{info.machineName}</span></div>
                  <div><span style={keyCell}>{t('monitor.framework')}</span><span style={{ wordBreak: 'break-all' }}>{info.frameworkDescription}</span></div>
                  <div><span style={keyCell}>{t('monitor.os')}</span><span style={{ wordBreak: 'break-all' }}>{info.osDescription}</span></div>
                  <div><span style={keyCell}>{t('monitor.arch')}</span><span style={{ wordBreak: 'break-all' }}>{info.processArchitecture}</span></div>
                  <div><span style={keyCell}>{t('monitor.uptime')}</span><span>{fmtUptime(info.processUptimeSeconds)}</span></div>
                </div>
              </Card>
            </Col>
            {/* 过滤掉容量为 0 的未就绪/映射盘,免渲染一张全 0 卡片 */}
            {info.disks.filter((d) => d.totalBytes > 0).map((disk) => {
              const used = disk.totalBytes - disk.freeBytes
              const dp = pct(used, disk.totalBytes)
              return (
                <Col xs={24} sm={12} lg={6} key={disk.name}>
                  <Card size="small" title={`${t('monitor.disk')} ${disk.name}`}>
                    <div style={{ ...metric, fontSize: 18 }}>{fmtBytes(used)} / {fmtBytes(disk.totalBytes)}</div>
                    <Progress percent={dp} strokeColor={usageColor(dp)} showInfo={false} />
                    <div style={sub}>{t('monitor.free')}: {fmtBytes(disk.freeBytes)}</div>
                  </Card>
                </Col>
              )
            })}
          </Row>
        )}
      </Spin>
    </div>
  )
}
