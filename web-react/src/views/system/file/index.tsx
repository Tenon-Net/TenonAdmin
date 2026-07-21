// 文件管理页:只读列表(按文件名搜索)+ 工具栏上传(整传/分片)+ 行内下载/删除 + 批量删除。
// 照 B11 范式:纯逻辑抽 fileFormat.ts(formatSize/triggerBlobDownload,变异钉),本页只接线。
// 下载走 blob(Bearer 自动带)→ createObjectURL + <a download>;批删走 useBatchDelete(受控 rowSelection)。
import { useCallback, useMemo, useRef } from 'react'
import { App, Button, Space } from 'antd'
import { useTranslation } from 'react-i18next'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import type { ProColumns } from '@ant-design/pro-components'
import { Can } from '@/components/Can'
import { FileUpload } from '@/components/FileUpload'
import { useConfirm } from '@/hooks/useConfirm'
import { useBatchDelete } from '@/hooks/useBatchDelete'
import { useHasPerm } from '@/stores/auth'
import { fileApi } from '@/api'
import { translateError } from '@/utils/error'
import type { SysFile } from '@/types/api'
import { formatSize, triggerBlobDownload } from './fileFormat'

export default function FilePage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  const { selectedKeys, setSelectedKeys, hasSelection, run: batchDelete } = useBatchDelete({
    remove: fileApi.batchRemove,
    refresh: reload,
    successMsg: t('file.deleted'),
  })

  const fetchFiles: PageFetcher<SysFile> = (q) =>
    fileApi.page({ page: q.page, pageSize: q.pageSize, originalName: typeof q.originalName === 'string' ? q.originalName : undefined })

  const onUploaded = useCallback(() => {
    message.success(t('file.uploaded'))
    reload()
  }, [message, t, reload])

  const handleDownload = useCallback(
    async (r: SysFile) => {
      try {
        triggerBlobDownload(await fileApi.download(r.id), r.originalName)
      } catch (e) {
        message.error(translateError(e))
      }
    },
    [message],
  )

  const handleDelete = useCallback(
    (r: SysFile) => {
      confirm({ content: t('file.deleteConfirm', { name: r.originalName }), action: () => fileApi.remove(r.id), successMsg: t('file.deleted') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, reload],
  )

  const columns = useMemo<ProColumns<SysFile>[]>(
    () => [
      { title: t('file.name'), dataIndex: 'originalName', ellipsis: true },
      { title: t('file.extension'), dataIndex: 'extension', search: false, width: 90, render: (_, r) => r.extension || '—' },
      { title: t('file.size'), dataIndex: 'sizeBytes', search: false, width: 110, render: (_, r) => formatSize(r.sizeBytes) },
      { title: t('file.contentType'), dataIndex: 'contentType', search: false, ellipsis: true, render: (_, r) => r.contentType || '—' },
      { title: t('file.uploadTime'), dataIndex: 'createTime', search: false, width: 170 },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 140, fixed: 'right',
        render: (_, r) => (
          <Space size={4}>
            {has('GET:/api/v1/sys/file/{id}/download') && <Button type="link" size="small" onClick={() => handleDownload(r)}>{t('file.download')}</Button>}
            {has('DELETE:/api/v1/sys/file/{id}') && <Button type="link" size="small" danger onClick={() => handleDelete(r)}>{t('common.delete')}</Button>}
          </Space>
        ),
      },
    ],
    [t, has, handleDownload, handleDelete],
  )

  return (
    <DataTable<SysFile>
      ref={tableRef}
      columns={columns}
      fetcher={fetchFiles}
      persistKey="sys-file"
      headerTitle={t('file.title')}
      rowSelection={{ selectedRowKeys: selectedKeys, onChange: setSelectedKeys }}
      toolbar={
        <Space>
          <Can code="POST:/api/v1/sys/file/upload">
            <FileUpload showUploadList={false} onUploaded={onUploaded}>
              <Button type="primary">{t('file.upload')}</Button>
            </FileUpload>
          </Can>
          <Can code="POST:/api/v1/sys/file/chunk/init">
            <FileUpload chunked showUploadList={false} onUploaded={onUploaded}>
              <Button>{t('file.uploadChunked')}</Button>
            </FileUpload>
          </Can>
          <Can code="POST:/api/v1/sys/file/batch-delete">
            <Button danger disabled={!hasSelection} onClick={batchDelete}>{t('common.batchDelete')}</Button>
          </Can>
        </Space>
      }
    />
  )
}
