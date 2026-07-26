// 四步导入向导(excel-ledger §9 G7):①上传 ②列映射 ③预览改错(裸 antd Table)④结果。
// API 由父级注入(用户导入走 userApi 六方法),组件对资源无感知。
// 与 web/ ImportWizard 功能对齐,零共享(坑 7);错误格底色用 --color-danger-bg(坑 12)。
import { useCallback, useEffect, useMemo, useState, type CSSProperties, type ReactNode } from 'react'
import {
  Alert, App, Button, Input, Modal, Radio, Select, Space, Steps, Switch, Table, Tag, Tooltip, Upload, Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { UploadFile } from 'antd/es/upload/interface'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { DictSelect } from '@/components/DictSelect'
import { translateError } from '@/utils/error'
import { triggerBlobDownload } from '@/utils/download'
import type {
  DuplicateStrategy as DupStrategyNum,
  ImportColumn,
  ImportCommitResult,
  ImportPreview,
  ImportRow,
} from '@/types/api'
import { DuplicateStrategy } from '@/types/api'

export interface ImportWizardApi {
  downloadTemplate: () => Promise<Blob>
  preview: (file: File, mapping?: Record<string, string>) => Promise<ImportPreview>
  validate: (rows: ImportRow[]) => Promise<ImportPreview>
  commit: (rows: ImportRow[], strategy: DupStrategyNum) => Promise<ImportCommitResult>
  errorReport: (rows: ImportRow[]) => Promise<Blob>
}

export interface ImportWizardProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  api: ImportWizardApi
  /** 模板下载默认文件名 */
  templateFileName?: string
  /** 错误报告默认文件名 */
  errorReportFileName?: string
  /** 提交成功(含部分成功)后触发,父级通常 refresh 列表 */
  onDone?: () => void
}

const cellWrap: CSSProperties = { padding: 2, borderRadius: 4 }
const cellErrorWrap: CSSProperties = {
  ...cellWrap,
  // 用仓库自己的语义令牌(亮/暗都有定义)。别写 antd 组件内部变量 —— 未定义时整条声明静默失效(坑 12)。
  background: 'var(--color-danger-bg)',
}

export function ImportWizard({
  open,
  onOpenChange,
  api,
  templateFileName = 'import-template.xlsx',
  errorReportFileName = 'import-errors.xlsx',
  onDone,
}: ImportWizardProps) {
  const { t } = useTranslation()
  const { message } = App.useApp()

  // Steps 0-based
  const [step, setStep] = useState(0)
  const [loading, setLoading] = useState(false)
  const [file, setFile] = useState<File | null>(null)
  const [fileList, setFileList] = useState<UploadFile[]>([])
  const [headers, setHeaders] = useState<string[]>([])
  /** 表头 → 列 Key(空串 = 不映射) */
  const [mapping, setMapping] = useState<Record<string, string>>({})
  const [columns, setColumns] = useState<ImportColumn[]>([])
  const [rows, setRows] = useState<ImportRow[]>([])
  const [columnErrors, setColumnErrors] = useState<ImportPreview['columnErrors']>([])
  const [errorRows, setErrorRows] = useState(0)
  const [onlyErrors, setOnlyErrors] = useState(false)
  const [strategy, setStrategy] = useState<DupStrategyNum>(DuplicateStrategy.Skip)
  const [commitResult, setCommitResult] = useState<ImportCommitResult | null>(null)

  const reset = useCallback(() => {
    setStep(0)
    setLoading(false)
    setFile(null)
    setFileList([])
    setHeaders([])
    setMapping({})
    setColumns([])
    setRows([])
    setColumnErrors([])
    setErrorRows(0)
    setOnlyErrors(false)
    setStrategy(DuplicateStrategy.Skip)
    setCommitResult(null)
  }, [])

  useEffect(() => {
    if (open) reset()
  }, [open, reset])

  const applyPreview = (p: ImportPreview) => {
    setHeaders([...p.headers])
    setMapping({ ...p.mapping })
    setColumns(p.columns)
    setRows(
      p.rows.map((r) => ({
        index: r.index,
        cells: { ...r.cells },
        errors: [...r.errors],
      })),
    )
    setColumnErrors(p.columnErrors)
    setErrorRows(p.errorRows)
  }

  const onDownloadTemplate = async () => {
    setLoading(true)
    try {
      const blob = await api.downloadTemplate()
      triggerBlobDownload(blob, templateFileName)
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }

  /** ①→②:带文件预览(mapping 空 = 服务端自动匹配) */
  const goMapping = async () => {
    if (!file) {
      message.warning(t('import.needFile'))
      return
    }
    setLoading(true)
    try {
      const p = await api.preview(file)
      applyPreview(p)
      // 确保每个表头在 mapping 里有键(未匹配的给空串)
      const nextMap = { ...p.mapping }
      for (const h of p.headers) {
        if (!(h in nextMap)) nextMap[h] = ''
      }
      setMapping(nextMap)
      setStep(1)
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }

  /** ②→③:按用户调整后的映射重新预览 */
  const goPreview = async () => {
    if (!file) return
    const map: Record<string, string> = {}
    for (const [h, k] of Object.entries(mapping)) {
      if (k) map[h] = k
    }
    setLoading(true)
    try {
      const p = await api.preview(file, map)
      applyPreview(p)
      setStep(2)
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }

  const targetColumnOptions = useMemo(
    () => [
      { label: t('import.unmap'), value: '' },
      ...columns.map((c) => ({
        label: c.required ? `${c.title} *` : c.title,
        value: c.key,
      })),
    ],
    [columns, t],
  )

  const setCell = (rowIndex: number, key: string, value: string | null) => {
    setRows((prev) =>
      prev.map((r) => {
        if (r.index !== rowIndex) return r
        return {
          ...r,
          cells: { ...r.cells, [key]: value },
          // 就地改后清该列旧错误,等重验;否则红底会误导
          errors: r.errors.filter((e) => e.columnKey !== key),
        }
      }),
    )
  }

  const displayRows = useMemo(
    () => (onlyErrors ? rows.filter((r) => r.errors.length > 0) : rows),
    [onlyErrors, rows],
  )

  const previewColumns: ColumnsType<ImportRow> = useMemo(() => {
    const cols: ColumnsType<ImportRow> = [
      {
        title: '#',
        key: '_index',
        width: 56,
        fixed: 'left',
        render: (_, r) => r.index,
      },
    ]
    for (const col of columns) {
      cols.push({
        title: col.required ? `${col.title} *` : col.title,
        key: col.key,
        minWidth: 120,
        render: (_, row) => {
          const err = row.errors.find((e) => e.columnKey === col.key)
          const val = row.cells[col.key] ?? null
          const editor = col.dictTypeCode ? (
            <DictSelect
              typeCode={col.dictTypeCode}
              value={val ?? undefined}
              allowClear={!col.required}
              size="small"
              style={{ width: '100%' }}
              status={err ? 'error' : undefined}
              onChange={(v) => setCell(row.index, col.key, (v as string | null) ?? null)}
            />
          ) : (
            <Input
              value={val ?? ''}
              size="small"
              status={err ? 'error' : undefined}
              onChange={(e) => setCell(row.index, col.key, e.target.value)}
            />
          )
          const cell = (
            <div className={err ? 'import-cell import-cell--error' : 'import-cell'} style={err ? cellErrorWrap : cellWrap}>
              {editor}
            </div>
          )
          if (!err) return cell
          return <Tooltip title={translateError(err.code)}>{cell}</Tooltip>
        },
      })
    }
    cols.push({
      title: t('import.errors'),
      key: '_errors',
      width: 160,
      fixed: 'right',
      render: (_, r) => {
        if (r.errors.length === 0) {
          return <Tag color="success">{t('import.ok')}</Tag>
        }
        const tip = r.errors.map((e) => `${e.columnKey}: ${translateError(e.code)}`).join('\n')
        return (
          <Tooltip title={<span style={{ whiteSpace: 'pre-line' }}>{tip}</span>}>
            <Tag color="error">{t('import.errorCount', { n: r.errors.length })}</Tag>
          </Tooltip>
        )
      },
    })
    return cols
  }, [columns, t])

  const revalidate = async () => {
    // 变异判据:请求必须带 rows;空数组也是明确失败路径(服务端会返回 0 行)
    setLoading(true)
    try {
      const p = await api.validate(rows)
      setRows(
        p.rows.map((r) => ({
          index: r.index,
          cells: { ...r.cells },
          errors: [...r.errors],
        })),
      )
      if (p.columns.length) setColumns(p.columns)
      setErrorRows(p.errorRows)
      message.success(t('import.revalidated', { errors: p.errorRows, total: p.total }))
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }

  const doCommit = async () => {
    setLoading(true)
    try {
      const result = await api.commit(rows, strategy)
      setCommitResult(result)
      setStep(3)
      if (result.inserted + result.updated > 0) onDone?.()
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }

  /** ④ 失败行接回 ③ 继续改 */
  const backToEditFailures = () => {
    if (!commitResult) return
    setRows(
      commitResult.failures.map((r) => ({
        index: r.index,
        cells: { ...r.cells },
        errors: [...r.errors],
      })),
    )
    setErrorRows(commitResult.failures.filter((r) => r.errors.length > 0).length)
    setOnlyErrors(true)
    setCommitResult(null)
    setStep(2)
  }

  const downloadErrorReport = async () => {
    const source =
      step === 3 && commitResult
        ? commitResult.failures
        : rows.filter((r) => r.errors.length > 0)
    if (source.length === 0) {
      message.warning(t('import.noErrorRows'))
      return
    }
    setLoading(true)
    try {
      const blob = await api.errorReport(source)
      triggerBlobDownload(blob, errorReportFileName)
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }

  const close = () => {
    if (!loading) onOpenChange(false)
  }

  const stepItems = useMemo(
    () => [
      { title: t('import.stepUpload') },
      { title: t('import.stepMapping') },
      { title: t('import.stepPreview') },
      { title: t('import.stepResult') },
    ],
    [t],
  )

  let body: ReactNode = null
  if (step === 0) {
    body = (
      <Space orientation="vertical" size={12} style={{ width: '100%' }}>
        <Button loading={loading} onClick={onDownloadTemplate} icon={<AppIcon icon="ph:download-simple" size={16} />}>
          {t('import.downloadTemplate')}
        </Button>
        <Upload.Dragger
          fileList={fileList}
          maxCount={1}
          accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
          beforeUpload={(f) => {
            setFile(f)
            setFileList([{ uid: '-1', name: f.name, status: 'done' }])
            return false
          }}
          onChange={({ fileList: list }) => {
            setFileList(list.slice(-1))
            const f = list[list.length - 1]?.originFileObj
            setFile(f instanceof File ? f : null)
          }}
          onRemove={() => {
            setFile(null)
            setFileList([])
            return true
          }}
        >
          <div style={{ marginBottom: 12, color: 'var(--color-text-tertiary)' }}>
            <AppIcon icon="ph:file-xls" size={40} />
          </div>
          <Typography.Text style={{ fontSize: 16 }}>{t('import.dropHint')}</Typography.Text>
          <br />
          <Typography.Text type="secondary">{t('import.dropSub')}</Typography.Text>
        </Upload.Dragger>
      </Space>
    )
  } else if (step === 1) {
    body = (
      <>
        {columnErrors.length > 0 && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 12 }}
            title={columnErrors.map((e) => `${e.columnKey}: ${translateError(e.code)}`).join('; ')}
          />
        )}
        <Typography.Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
          {t('import.mappingHint')}
        </Typography.Text>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, fontWeight: 600, fontSize: 13, marginBottom: 8 }}>
          <span>{t('import.fileHeader')}</span>
          <span>{t('import.targetColumn')}</span>
        </div>
        {headers.map((hdr) => (
          <div key={hdr} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, alignItems: 'center', marginBottom: 8 }}>
            <span title={hdr} style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', fontSize: 13 }}>
              {hdr}
            </span>
            <Select
              size="small"
              value={mapping[hdr] ?? ''}
              options={targetColumnOptions}
              onChange={(v) => setMapping((m) => ({ ...m, [hdr]: v }))}
              popupMatchSelectWidth={false}
            />
          </div>
        ))}
      </>
    )
  } else if (step === 2) {
    body = (
      <>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12, flexWrap: 'wrap', gap: 8 }}>
          <Space>
            <Typography.Text>
              {t('import.summary', { total: rows.length, errors: errorRows })}
            </Typography.Text>
            <Space size={4}>
              <Switch size="small" checked={onlyErrors} onChange={setOnlyErrors} />
              <Typography.Text type="secondary">{t('import.onlyErrors')}</Typography.Text>
            </Space>
          </Space>
          <Button size="small" loading={loading} onClick={revalidate} icon={<AppIcon icon="ph:arrows-clockwise" size={14} />}>
            {t('import.revalidate')}
          </Button>
        </div>
        <Table<ImportRow>
          size="small"
          bordered
          pagination={false}
          rowKey={(r) => r.index}
          columns={previewColumns}
          dataSource={displayRows}
          scroll={{ x: Math.max(800, columns.length * 140 + 220), y: 420 }}
        />
        <div style={{ marginTop: 16, display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
          <Typography.Text strong style={{ marginRight: 12 }}>{t('import.strategy')}</Typography.Text>
          <Radio.Group value={strategy} onChange={(e) => setStrategy(e.target.value)} name="dup-strategy">
            <Space>
              <Radio value={DuplicateStrategy.Skip}>{t('import.strategySkip')}</Radio>
              <Radio value={DuplicateStrategy.Overwrite}>{t('import.strategyOverwrite')}</Radio>
              <Radio value={DuplicateStrategy.Error}>{t('import.strategyError')}</Radio>
            </Space>
          </Radio.Group>
        </div>
      </>
    )
  } else {
    body = (
      <>
        {commitResult && (
          <Alert
            type={commitResult.failed > 0 ? 'warning' : 'success'}
            showIcon
            title={t('import.resultTitle')}
            description={
              <div>
                <div>{t('import.resultTotal', { n: commitResult.total })}</div>
                <div>{t('import.resultInserted', { n: commitResult.inserted })}</div>
                <div>{t('import.resultUpdated', { n: commitResult.updated })}</div>
                <div>{t('import.resultSkipped', { n: commitResult.skipped })}</div>
                <div>{t('import.resultFailed', { n: commitResult.failed })}</div>
              </div>
            }
          />
        )}
        {commitResult && commitResult.failed > 0 && (
          <Space style={{ marginTop: 16 }}>
            <Button type="primary" danger onClick={backToEditFailures}>
              {t('import.editFailures')}
            </Button>
            <Button loading={loading} onClick={downloadErrorReport} icon={<AppIcon icon="ph:download-simple" size={16} />}>
              {t('import.downloadErrorReport')}
            </Button>
          </Space>
        )}
      </>
    )
  }

  const footer = (
    <Space style={{ display: 'flex', justifyContent: 'flex-end' }}>
      <Button disabled={loading} onClick={close}>
        {step === 3 ? t('common.close') : t('common.cancel')}
      </Button>
      {step === 2 && (
        <Button disabled={loading} onClick={downloadErrorReport}>
          {t('import.downloadErrorReport')}
        </Button>
      )}
      {step > 0 && step < 3 && (
        <Button disabled={loading} onClick={() => setStep((s) => s - 1)}>
          {t('import.prev')}
        </Button>
      )}
      {step === 0 && (
        <Button type="primary" loading={loading} disabled={!file} onClick={goMapping}>
          {t('import.next')}
        </Button>
      )}
      {step === 1 && (
        <Button type="primary" loading={loading} onClick={goPreview}>
          {t('import.next')}
        </Button>
      )}
      {step === 2 && (
        <Button type="primary" loading={loading} onClick={doCommit}>
          {t('import.commit')}
        </Button>
      )}
    </Space>
  )

  return (
    <Modal
      open={open}
      title={t('import.wizardTitle')}
      width="min(1100px, 96vw)"
      onCancel={close}
      mask={{ closable: !loading }}
      keyboard={!loading}
      closable={!loading}
      destroyOnHidden
      footer={footer}
    >
      <Steps size="small" current={step} items={stepItems} style={{ marginBottom: 20 }} />
      <div style={{ minHeight: 200 }}>{body}</div>
    </Modal>
  )
}
