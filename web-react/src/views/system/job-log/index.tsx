// 任务执行记录(G8):只读 DataTable + 详情抽屉(同 fireInstanceId 的各次尝试)+ 终止(仅运行中行)+ 清空(选 beforeDays)。
// 从任务页「记录」带 jobId 进来:读 URL 查询串做任务筛选的初始值(任务页跳转前先 refreshTab 重挂本页,
// 所以缓存页签复用时初始值也拿得到新 jobId)。行 endTime 为空 = 运行中(任务无 Running 态,全靠此行推导)。
import { useCallback, useMemo, useRef, useState, type CSSProperties } from 'react'
import { App, Button, Descriptions, Drawer, InputNumber, Space, Table, Tag, Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { ApiSelect } from '@/components/ApiSelect'
import { Can } from '@/components/Can'
import { CodeBlock } from '@/components/CodeBlock'
import { FormContainer } from '@/components/FormContainer'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { jobApi } from '@/api'
import { translateError } from '@/utils/error'
import type { SysJobLog } from '@/types/api'

const preStyle: CSSProperties = {
  margin: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-all',
  fontFamily: 'var(--font-mono, ui-monospace, monospace)', fontSize: 12, lineHeight: 1.5, color: 'var(--color-danger)',
}

/** ISO 时刻截到秒、去 T;空显 —。 */
const fmtTime = (s?: string | null): string => (s ? s.replace('T', ' ').slice(0, 19) : '—')

/** 触发来源 tag:1=调度 2=手动 3=补跑 4=错过跳过。 */
const FIRE_MODE: Record<number, { color: string; key: string }> = {
  1: { color: 'default', key: 'job.log.fireModeSchedule' },
  2: { color: 'processing', key: 'job.log.fireModeManual' },
  3: { color: 'warning', key: 'job.log.fireModeMakeup' },
  4: { color: 'default', key: 'job.log.fireModeSkipped' },
}

/** 执行结果 tag:1=运行中 2=成功 3=失败 4=超时 5=取消 6=跳过。 */
const RUN_STATUS: Record<number, { color: string; key: string }> = {
  1: { color: 'processing', key: 'job.log.statusRunning' },
  2: { color: 'success', key: 'job.log.statusSuccess' },
  3: { color: 'error', key: 'job.log.statusFailed' },
  4: { color: 'warning', key: 'job.log.statusTimeout' },
  5: { color: 'default', key: 'job.log.statusCancelled' },
  6: { color: 'default', key: 'job.log.statusSkipped' },
}

export default function JobLogPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()
  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  // 任务页带 jobId 进来 → 任务筛选初始值(挂载时读一次;跳转方 refreshTab 保证缓存页签也重挂)
  const [searchParams] = useSearchParams()
  const initialJobId = Number(searchParams.get('jobId')) || undefined

  const modeTag = useCallback((mode: number) => {
    const m = FIRE_MODE[mode]
    return m ? <Tag color={m.color}>{t(m.key)}</Tag> : '—'
  }, [t])
  const statusTag = useCallback((s: number) => {
    const m = RUN_STATUS[s]
    return m ? <Tag color={m.color}>{t(m.key)}</Tag> : '—'
  }, [t])

  // fetcher 有意不 memo(ProTable 经 ref 读 request);startTime 是 dateRange 列的 [start,end] 串。
  const fetchLogs: PageFetcher<SysJobLog> = (q) =>
    jobApi.logPage({
      page: q.page,
      pageSize: q.pageSize,
      jobId: typeof q.jobId === 'number' ? q.jobId : undefined,
      runStatus: typeof q.runStatus === 'number' ? q.runStatus : undefined,
      startTime: (q.startTime as [string, string] | undefined) ?? null,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
    })

  // ── 详情抽屉:行数据直接展示 + 拉同 fireInstanceId 的各次尝试(无专用端点,按 jobId+StartFrom 收窄后本地过滤)──
  const [detailRow, setDetailRow] = useState<SysJobLog | null>(null)
  const [attempts, setAttempts] = useState<SysJobLog[]>([])
  const openDetail = useCallback(
    async (r: SysJobLog) => {
      setDetailRow(r)
      setAttempts([])
      try {
        // 走后端的 fireInstanceId 查询参数直取,不按 jobId 收窄一页再本地过滤——
        // 翻到老触发时那一页里根本没有它的兄弟行,列表会静默缺项。
        const p = await jobApi.logPage({ page: 1, pageSize: 100, fireInstanceId: r.fireInstanceId })
        setAttempts(p.items.sort((a, b) => a.retryIndex - b.retryIndex))
      } catch {
        setAttempts([r]) // 拉不到就至少显当前行,不阻断抽屉
      }
    },
    [],
  )

  // 终止(仅运行中行):写终止旗标,目标节点最迟数秒后停,行仍显运行中直到闭合。
  const handleKill = useCallback(
    (r: SysJobLog) => {
      confirm({ content: t('job.log.killConfirm', { name: r.jobName }), action: () => jobApi.killLog(r.id), successMsg: t('job.log.killed') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, reload],
  )

  // ── 清空弹窗(选 beforeDays;运行中的记录后端一律保留)──
  const [clearOpen, setClearOpen] = useState(false)
  const [beforeDays, setBeforeDays] = useState<number | null>(30)
  const doClear = async () => {
    try {
      const n = await jobApi.clearLogs({ beforeDays })
      message.success(t('job.log.cleared', { count: n }))
      reload()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  const columns = useMemo<ProColumns<SysJobLog>[]>(
    () => [
      {
        title: t('job.log.job'), dataIndex: 'jobId', initialValue: initialJobId,
        render: (_, r) => r.jobName,
        // 裸返 ApiSelect,value/onChange 由 pro-components cloneElement 注入(显式写会盖掉注入,C7 HIGH-1)
        formItemRender: () => (
          <ApiSelect
            allowClear
            placeholder={t('job.log.jobPlaceholder')}
            fetch={(kw) =>
              jobApi.page({ page: 1, pageSize: 50, name: kw || undefined }).then((p) =>
                p.items.map((j) => ({ label: `${j.name}(${j.code})`, value: j.id })),
              )
            }
          />
        ),
      },
      { title: t('job.log.fireMode'), dataIndex: 'fireMode', width: 100, search: false, render: (_, r) => modeTag(r.fireMode) },
      { title: t('job.log.scheduledTime'), dataIndex: 'scheduledTime', width: 160, search: false, render: (_, r) => fmtTime(r.scheduledTime) },
      { title: t('job.log.startTime'), dataIndex: 'startTime', width: 160, valueType: 'dateRange', render: (_, r) => fmtTime(r.startTime) },
      {
        title: t('job.log.elapsed'), dataIndex: 'elapsedMs', width: 100, search: false,
        render: (_, r) => (r.endTime ? `${r.elapsedMs} ms` : '—'),
      },
      {
        title: t('job.log.status'), dataIndex: 'runStatus', width: 100, valueType: 'select',
        fieldProps: {
          allowClear: true,
          options: [1, 2, 3, 4, 5, 6].map((v) => ({ label: t(RUN_STATUS[v]!.key), value: v })),
        },
        render: (_, r) => statusTag(r.runStatus),
      },
      { title: t('job.log.retryIndex'), dataIndex: 'retryIndex', width: 80, search: false },
      { title: t('job.log.node'), dataIndex: 'nodeName', search: false, ellipsis: true },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 130, fixed: 'right',
        render: (_, r) => (
          <Space size={0}>
            <Button type="link" size="small" onClick={() => void openDetail(r)}>{t('common.detail')}</Button>
            {r.endTime == null && has('POST:/api/v1/sys/job/log/{id}/kill') && (
              <Button type="link" size="small" danger onClick={() => handleKill(r)}>{t('job.log.kill')}</Button>
            )}
          </Space>
        ),
      },
    ],
    [t, initialJobId, modeTag, statusTag, has, openDetail, handleKill],
  )

  return (
    <>
      <DataTable<SysJobLog>
        ref={tableRef}
        columns={columns}
        fetcher={fetchLogs}
        persistKey="sys-job-log"
        toolbar={
          <Can code="POST:/api/v1/sys/job/log/clear">
            <Button danger onClick={() => setClearOpen(true)}>{t('job.log.clear')}</Button>
          </Can>
        }
      />

      {/* 清空:选 beforeDays(留空 = 清全部;运行中的记录一律保留) */}
      <FormContainer
        open={clearOpen}
        onOpenChange={setClearOpen}
        title={t('job.log.clearTitle')}
        variant="modal"
        width={420}
        onConfirm={doClear}
      >
        <Space orientation="vertical" size={8} style={{ width: '100%', marginTop: 8 }}>
          <Space size={8}>
            <span>{t('job.log.beforeDays')}</span>
            <InputNumber min={1} precision={0} value={beforeDays} onChange={(v) => setBeforeDays(v == null ? null : Number(v))} style={{ width: 140 }} />
            <span>{t('job.log.daysUnit')}</span>
          </Space>
          <Typography.Text type="secondary">{t('job.log.beforeDaysHint')}</Typography.Text>
        </Space>
      </FormContainer>

      <Drawer open={!!detailRow} onClose={() => setDetailRow(null)} title={t('job.log.detailTitle')} size={640}>
        {detailRow && (
          <Space orientation="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions
              bordered size="small" column={2}
              items={[
                { key: 'job', label: t('job.log.job'), children: detailRow.jobName, span: 2 },
                { key: 'mode', label: t('job.log.fireMode'), children: modeTag(detailRow.fireMode) },
                { key: 'status', label: t('job.log.status'), children: statusTag(detailRow.runStatus) },
                { key: 'sched', label: t('job.log.scheduledTime'), children: fmtTime(detailRow.scheduledTime) },
                { key: 'start', label: t('job.log.startTime'), children: fmtTime(detailRow.startTime) },
                { key: 'end', label: t('job.log.endTime'), children: fmtTime(detailRow.endTime) },
                { key: 'elapsed', label: t('job.log.elapsed'), children: detailRow.endTime ? `${detailRow.elapsedMs} ms` : '—' },
                { key: 'retry', label: t('job.log.retryIndex'), children: detailRow.retryIndex },
                { key: 'node', label: t('job.log.node'), children: detailRow.nodeName },
              ]}
            />
            {detailRow.errorText && (
              <div>
                <Typography.Text type="secondary">{t('job.log.errorText')}</Typography.Text>
                <pre style={preStyle}>{detailRow.errorText}</pre>
              </div>
            )}
            {detailRow.messageText && (
              <div>
                <Typography.Text type="secondary">{t('job.log.messageText')}</Typography.Text>
                <CodeBlock code={detailRow.messageText} wordWrap />
              </div>
            )}
            <div>
              <Typography.Text type="secondary">{t('job.log.attempts')}</Typography.Text>
              <Table<SysJobLog>
                size="small"
                rowKey="id"
                pagination={false}
                style={{ marginTop: 8 }}
                dataSource={attempts}
                columns={[
                  { title: t('job.log.retryIndex'), dataIndex: 'retryIndex', width: 80 },
                  { title: t('job.log.startTime'), dataIndex: 'startTime', render: (v: string) => fmtTime(v) },
                  { title: t('job.log.elapsed'), dataIndex: 'elapsedMs', width: 100, render: (_, r) => (r.endTime ? `${r.elapsedMs} ms` : '—') },
                  { title: t('job.log.status'), dataIndex: 'runStatus', width: 100, render: (v: number) => statusTag(v) },
                  { title: t('job.log.node'), dataIndex: 'nodeName', ellipsis: true },
                ]}
              />
            </div>
          </Space>
        )}
      </Drawer>
    </>
  )
}
