// 异常日志 = 只读 DataTable + 详情抽屉。后端无 exception/{id},分页项已含全字段 → 抽屉直接用行数据。
// 只记未捕获异常(程序缺陷);业务异常不进本表。堆栈/消息保持危险色 <pre>(堆栈非代码,高亮无意义)。
import { useRef, useState, type CSSProperties } from 'react'
import { Button, Descriptions, Drawer } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { Can } from '@/components/Can'
import { useConfirm } from '@/hooks/useConfirm'
import { logApi } from '@/api'
import type { SysExceptionLog } from '@/types/api'
import { operatorText } from '../logFormat'

const preStyle: CSSProperties = {
  margin: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-all',
  fontFamily: 'var(--font-mono, ui-monospace, monospace)', fontSize: 12, lineHeight: 1.5, color: 'var(--color-danger)',
}

export default function ExceptionLogPage() {
  const { t } = useTranslation()
  const { confirm } = useConfirm()
  const tableRef = useRef<DataTableHandle>(null)
  const [detailRow, setDetailRow] = useState<SysExceptionLog | null>(null)

  const fetchException: PageFetcher<SysExceptionLog> = (q) =>
    logApi.exceptionPage({
      page: q.page, pageSize: q.pageSize,
      exceptionType: typeof q.exceptionType === 'string' ? q.exceptionType : undefined,
      path: typeof q.path === 'string' ? q.path : undefined,
      createTime: (q.createTime as [string, string] | undefined) ?? null,
    })

  const columns: ProColumns<SysExceptionLog>[] = [
    { title: t('log.exceptionType'), dataIndex: 'exceptionType', ellipsis: true },
    { title: t('log.exceptionMessage'), dataIndex: 'message', search: false, ellipsis: true, render: (_, r) => r.message || '—' },
    { title: t('log.method'), dataIndex: 'httpMethod', width: 90, search: false },
    { title: t('log.path'), dataIndex: 'path', ellipsis: true },
    { title: t('log.operator'), dataIndex: 'operatorId', width: 120, search: false, render: (_, r) => operatorText(r) },
    { title: t('log.ip'), dataIndex: 'ip', search: false, render: (_, r) => r.ip || '—' },
    { title: t('common.createTime'), dataIndex: 'createTime', valueType: 'dateRange', render: (_, r) => r.createTime },
    {
      title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 90, fixed: 'right',
      render: (_, r) => <Button type="link" size="small" onClick={() => setDetailRow(r)}>{t('log.detail')}</Button>,
    },
  ]

  const clearLogs = () => {
    confirm({ content: t('log.clearExceptionConfirm'), action: () => logApi.exceptionClear(), successMsg: t('log.cleared') }).then((ok) => {
      if (ok) tableRef.current?.reload()
    })
  }

  return (
    <>
      <DataTable<SysExceptionLog>
        ref={tableRef}
        columns={columns}
        fetcher={fetchException}
        persistKey="sys-log-exception"
        toolbar={
          <Can code="DELETE:/api/v1/sys/log/exception">
            <Button danger onClick={clearLogs}>{t('log.clear')}</Button>
          </Can>
        }
      />
      <Drawer open={!!detailRow} onClose={() => setDetailRow(null)} title={t('log.detail')} size={620}>
        {detailRow && (
          <Descriptions bordered size="small" column={1} items={[
            { key: 'type', label: t('log.exceptionType'), children: detailRow.exceptionType },
            { key: 'method', label: t('log.method'), children: detailRow.httpMethod },
            { key: 'path', label: t('log.path'), children: detailRow.path },
            { key: 'traceId', label: t('log.traceId'), children: detailRow.traceId || '—' },
            { key: 'operator', label: t('log.operator'), children: operatorText(detailRow) },
            { key: 'ip', label: t('log.ip'), children: detailRow.ip || '—' },
            { key: 'ua', label: t('log.userAgent'), children: detailRow.userAgent || '—' },
            { key: 'createTime', label: t('common.createTime'), children: detailRow.createTime },
            { key: 'message', label: t('log.exceptionMessage'), children: <pre style={preStyle}>{detailRow.message || '—'}</pre> },
            { key: 'stack', label: t('log.stackTrace'), children: <pre style={preStyle}>{detailRow.stackTrace || '—'}</pre> },
          ]} />
        )}
      </Drawer>
    </>
  )
}
