// 操作日志 = 只读 DataTable + 详情抽屉。后端无 op/{id},分页项已含全字段 → 抽屉直接用行数据。
// 搜索区要能答审计三问:谁(操作人,精确 id → UserSelect)、什么时候(时间范围)、干了什么(操作名/路径/成败)。
// paramJson 走 CodeBlock(json 高亮 + 复制);异常信息保持危险色 <pre>(堆栈非代码,高亮无意义)。
// 导出(G7):ExportColumnsModal 选列 + 当前筛选条件。
import { useMemo, useRef, useState, type CSSProperties } from 'react'
import { App, Button, Descriptions, Drawer, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { Can } from '@/components/Can'
import { CodeBlock } from '@/components/CodeBlock'
import { UserSelect } from '@/components/UserSelect'
import { ExportColumnsModal } from '@/components/ExportColumnsModal'
import { useConfirm } from '@/hooks/useConfirm'
import { logApi } from '@/api'
import { translateError } from '@/utils/error'
import { triggerBlobDownload } from '@/utils/download'
import type { ExportColumnDef, SysOpLog } from '@/types/api'
import { operatorText, prettyParam } from '../logFormat'

const preStyle: CSSProperties = {
  margin: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-all',
  fontFamily: 'var(--font-mono, ui-monospace, monospace)', fontSize: 12, lineHeight: 1.5, color: 'var(--color-danger)',
}

export default function OpLogPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const tableRef = useRef<DataTableHandle>(null)
  const [detailRow, setDetailRow] = useState<SysOpLog | null>(null)
  const [exportOpen, setExportOpen] = useState(false)
  const [exporting, setExporting] = useState(false)
  /** DataTable 不外泄搜索表单;在 fetcher 里截一份当前筛选,导出时带上。 */
  const lastQueryRef = useRef<{
    title?: string
    path?: string
    success?: boolean
    operatorId?: number
    createTime?: [string, string] | null
  }>({})

  /** 与后端 OpLogExportProfile.Columns 对齐。 */
  const opExportColumns: ExportColumnDef[] = useMemo(
    () => [
      { key: 'Title', title: '操作名' },
      { key: 'HttpMethod', title: '方法' },
      { key: 'Path', title: '路径' },
      { key: 'ResultCode', title: '结果码' },
      { key: 'Success', title: '成功' },
      { key: 'OperatorName', title: '操作人' },
      { key: 'Ip', title: 'IP' },
      { key: 'ElapsedMs', title: '耗时(ms)' },
      { key: 'CreateTime', title: '时间' },
      { key: 'ExceptionMessage', title: '异常信息', defaultSelected: false },
    ],
    [],
  )

  const onExport = async (keys: string[]) => {
    const p = lastQueryRef.current
    setExporting(true)
    try {
      const blob = await logApi.opExport({
        title: p.title || undefined,
        success: p.success,
        operatorId: p.operatorId,
        path: p.path || undefined,
        createTime: p.createTime ?? null,
        columns: keys.join(','),
      })
      triggerBlobDownload(blob, '操作日志导出.xlsx')
      setExportOpen(false)
      message.success(t('export.done'))
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setExporting(false)
    }
  }

  // fetcher 有意**不 memo**(ProTable 经 ref 读 request,父重渲染不会触发重取)。搜索表单值经 ...q 透传,
  // 逐字段收敛类型;createTime 是 dateRange 列的 [start,end] 串,api 层 splitRange 拆 StartTime/EndTime。
  const fetchOp: PageFetcher<SysOpLog> = (q) => {
    const title = typeof q.title === 'string' ? q.title : undefined
    const path = typeof q.path === 'string' ? q.path : undefined
    const success = typeof q.success === 'boolean' ? q.success : undefined
    const operatorId = typeof q.operatorId === 'number' ? q.operatorId : undefined
    const createTime = (q.createTime as [string, string] | undefined) ?? null
    lastQueryRef.current = { title, path, success, operatorId, createTime }
    return logApi.opPage({
      page: q.page, pageSize: q.pageSize,
      title, path, success, operatorId, createTime,
    })
  }

  const columns: ProColumns<SysOpLog>[] = [
    { title: t('log.opName'), dataIndex: 'title' },
    { title: t('log.method'), dataIndex: 'httpMethod', width: 90, search: false },
    { title: t('log.path'), dataIndex: 'path', ellipsis: true },
    {
      title: t('log.result'), dataIndex: 'success', width: 100, valueType: 'select',
      fieldProps: { allowClear: true, options: [{ label: t('log.success'), value: true }, { label: t('log.failed'), value: false }] },
      render: (_, r) => <Tag color={r.success ? 'success' : 'error'}>{t(r.success ? 'log.success' : 'log.failed')}</Tag>,
    },
    { title: t('log.resultCode'), dataIndex: 'resultCode', width: 100, search: false },
    {
      // 日志只存 OperatorId(姓名读取时回填),按人筛必须精确 id → 复用 UserSelect,搜索键 operatorId。
      title: t('log.operator'), dataIndex: 'operatorId', width: 120,
      render: (_, r) => operatorText(r),
      // v6 pro-components(beta)把 `renderFormItem` 改名为 `formItemRender`(tsc 不红那类改名)。
      // **不要**在返回节点上显式写 value/onChange:pro-components 用 cloneElement 注入真正的
      // value/onChangeCallBack,且把 `newDom.props` 放在**最后**展开 —— 显式写的会盖掉注入的,
      // 令 operatorId 永远收不到选择、「谁」这条审计筛选静默失效(C7 review HIGH-1)。
      // 裸返 UserSelect:注入的 value/onChange 经 ApiSelect 的 ...rest 直达 antd Select。
      formItemRender: () => <UserSelect allowClear placeholder={t('log.operatorPlaceholder')} />,
    },
    { title: t('log.elapsed'), dataIndex: 'elapsedMs', width: 100, search: false, render: (_, r) => `${r.elapsedMs} ms` },
    { title: t('log.ip'), dataIndex: 'ip', search: false, render: (_, r) => r.ip || '—' },
    // 时间范围是审计最常用的一刀("上周三下午谁删了那批");dateRange 回传 [start,end] 串。
    { title: t('common.createTime'), dataIndex: 'createTime', valueType: 'dateRange', render: (_, r) => r.createTime },
    {
      title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 90, fixed: 'right',
      render: (_, r) => <Button type="link" size="small" onClick={() => setDetailRow(r)}>{t('log.detail')}</Button>,
    },
  ]

  const clearLogs = () => {
    confirm({ content: t('log.clearOpConfirm'), action: () => logApi.opClear(), successMsg: t('log.cleared') }).then((ok) => {
      if (ok) tableRef.current?.reload()
    })
  }

  return (
    <>
      <DataTable<SysOpLog>
        ref={tableRef}
        columns={columns}
        fetcher={fetchOp}
        persistKey="sys-log-op"
        toolbar={
          <Space>
            <Can code="DELETE:/api/v1/sys/log/op">
              <Button danger onClick={clearLogs}>{t('log.clear')}</Button>
            </Can>
            <Can code="GET:/api/v1/sys/log/op/export">
              <Button onClick={() => setExportOpen(true)}>{t('export.button')}</Button>
            </Can>
          </Space>
        }
      />
      <Drawer open={!!detailRow} onClose={() => setDetailRow(null)} title={t('log.detail')} size={560}>
        {detailRow && (
          <Descriptions bordered size="small" column={1} items={[
            { key: 'title', label: t('log.opName'), children: detailRow.title },
            { key: 'method', label: t('log.method'), children: detailRow.httpMethod },
            { key: 'path', label: t('log.path'), children: detailRow.path },
            { key: 'result', label: t('log.result'), children: <Tag color={detailRow.success ? 'success' : 'error'}>{t(detailRow.success ? 'log.success' : 'log.failed')}</Tag> },
            { key: 'resultCode', label: t('log.resultCode'), children: detailRow.resultCode },
            ...(detailRow.exceptionMessage ? [{ key: 'exc', label: t('log.exception'), children: <pre style={preStyle}>{detailRow.exceptionMessage}</pre> }] : []),
            { key: 'elapsed', label: t('log.elapsed'), children: `${detailRow.elapsedMs} ms` },
            { key: 'operator', label: t('log.operator'), children: operatorText(detailRow) },
            { key: 'ip', label: t('log.ip'), children: detailRow.ip || '—' },
            { key: 'ua', label: t('log.userAgent'), children: detailRow.userAgent || '—' },
            { key: 'createTime', label: t('common.createTime'), children: detailRow.createTime },
            { key: 'param', label: t('log.param'), children: detailRow.paramJson ? <CodeBlock code={prettyParam(detailRow.paramJson)} language="json" wordWrap /> : '—' },
          ]} />
        )}
      </Drawer>
      <ExportColumnsModal
        open={exportOpen}
        onOpenChange={setExportOpen}
        columns={opExportColumns}
        loading={exporting}
        onConfirm={onExport}
      />
    </>
  )
}
