// 定时任务管理(G8,scheduling-ledger §11):DataTable CRUD + 四分节表单(基本/触发/载荷/失败处理)。
// 行含全列 → 编辑直接用行数据回填,不另拉详情;状态列 StatusSwitch 绑 {id}/enabled 端点,
// Panic 显红 tag(悬浮出连败次数);isSystem 行禁删(后端 47014 兜底)。纯逻辑在 jobForm.ts(变异钉)。
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { App, Button, DatePicker, Divider, Form, Input, InputNumber, Radio, Select, Space, Switch, Tag, Tooltip } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { Can } from '@/components/Can'
import { CronEditor } from '@/components/CronEditor'
import { FormContainer } from '@/components/FormContainer'
import { StatusSwitch } from '@/components/StatusSwitch'
import { useConfirm } from '@/hooks/useConfirm'
import { useBatchDelete } from '@/hooks/useBatchDelete'
import { useHasPerm } from '@/stores/auth'
import { useTabsStore } from '@/stores/tabs'
import { jobApi } from '@/api'
import { translateError } from '@/utils/error'
import type { SysJob } from '@/types/api'
import { blankJob, describeTrigger, formToInput, rowToForm, type JobFormValues } from './jobForm'

/** ISO 时刻截到秒、去 T;空显 —。 */
const fmtTime = (s?: string | null): string => (s ? s.replace('T', ' ').slice(0, 19) : '—')

const HANDLER_TAG: Record<number, { color: string; key: string }> = {
  1: { color: 'processing', key: 'job.handler.compiled' },
  2: { color: 'success', key: 'job.handler.http' },
  3: { color: 'warning', key: 'job.handler.sql' },
}

export default function JobPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()
  const navigate = useNavigate()

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  // 分页取数:搜索表单值(unknown)→ 强类型入参。有意不 memo(ProTable 经 ref 读 request)。
  const fetchJobs: PageFetcher<SysJob> = (q) =>
    jobApi.page({
      page: q.page,
      pageSize: q.pageSize,
      name: typeof q.name === 'string' ? q.name : undefined,
      status: typeof q.status === 'number' ? q.status : undefined,
      handlerKind: typeof q.handlerKind === 'number' ? q.handlerKind : undefined,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
    })

  // 批量删除:内置任务行禁勾(后端命中内置整批拒绝,前端先挡在勾选层)。
  const batch = useBatchDelete({ remove: jobApi.batchRemove, refresh: reload, successMsg: t('job.deleted') })

  // ── 新增/编辑弹窗(FormContainer owns loading+close)──
  const [form] = Form.useForm<JobFormValues>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)
  const triggerKind = Form.useWatch('triggerKind', form)
  const handlerKind = Form.useWatch('handlerKind', form)

  // 编译处理器下拉:首次开弹时拉一次(GET /handlers);失败不阻塞表单(HTTP/SQL 用不到)。
  const [handlerOptions, setHandlerOptions] = useState<string[] | null>(null)
  // SQL 载荷总闸(后端 Jobs:Sql:Enabled)。默认按"开"起手:清单没拉到就不该凭空禁掉一种载荷,
  // 真关着的话保存时后端还有 47008 兜底。
  const [sqlEnabled, setSqlEnabled] = useState(true)
  useEffect(() => {
    if (!open || handlerOptions !== null) return
    jobApi
      .handlers()
      .then((out) => {
        setHandlerOptions(out.handlers)
        setSqlEnabled(out.sqlEnabled)
      })
      .catch(() => setHandlerOptions([]))
  }, [open, handlerOptions])

  const openAdd = () => {
    setEditingId(null)
    form.resetFields()
    form.setFieldsValue(blankJob())
    setOpen(true)
  }
  const openEdit = useCallback(
    (r: SysJob) => {
      setEditingId(r.id)
      form.resetFields()
      form.setFieldsValue(rowToForm(r))
      setOpen(true)
    },
    [form],
  )

  const save = async () => {
    // 条件分节的字段卸载后不进 validateFields 结果,先用 blankJob 补齐缺省再组装
    const v: JobFormValues = { ...blankJob(), ...(await form.validateFields()) }
    try {
      if (editingId === null) await jobApi.add(formToInput(v))
      else await jobApi.update(editingId, formToInput(v))
      message.success(t('job.saved')) // 文案已含「集群下最长 30 秒后生效」
      reload()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  const handleDelete = useCallback(
    (r: SysJob) => {
      confirm({ content: t('job.deleteConfirm', { name: r.name }), action: () => jobApi.remove(r.id), successMsg: t('job.deleted') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, reload],
  )

  // 执行一次(二次确认;本机执行,不影响调度节奏)。
  const handleRun = useCallback(
    (r: SysJob) => {
      void confirm({ content: t('job.runConfirm', { name: r.name }), action: () => jobApi.runOnce(r.id), successMsg: t('job.runStarted') })
    },
    [confirm, t],
  )

  // 记录:携 jobId 跳执行记录页。已开的记录页签是 KeepAlive 缓存的,先 refreshTab 换 key 重挂,
  // 新 jobId 才会进它的初始筛选(未开时 refreshTab 无害)。
  const gotoLogs = useCallback(
    (r: SysJob) => {
      useTabsStore.getState().refreshTab('/system/job-log')
      navigate(`/system/job-log?jobId=${r.id}`)
    },
    [navigate],
  )

  const columns = useMemo<ProColumns<SysJob>[]>(
    () => [
      {
        title: t('job.name'), dataIndex: 'name', // 唯一模糊搜索项
        render: (_, r) => (
          <div>
            <div>{r.name}</div>
            <div style={{ fontSize: 12, color: 'var(--color-text-secondary, #999)' }}>{r.code}</div>
          </div>
        ),
      },
      {
        title: t('job.handlerKind'), dataIndex: 'handlerKind', width: 100, valueType: 'select',
        fieldProps: {
          allowClear: true,
          options: [
            { label: t('job.handler.compiled'), value: 1 },
            { label: t('job.handler.http'), value: 2 },
            { label: t('job.handler.sql'), value: 3 },
          ],
        },
        render: (_, r) => {
          const tag = HANDLER_TAG[r.handlerKind]
          return tag ? <Tag color={tag.color}>{t(tag.key)}</Tag> : '—'
        },
      },
      {
        title: t('job.trigger.title'), dataIndex: 'triggerKind', search: false, ellipsis: true,
        render: (_, r) => <span style={{ fontFamily: 'var(--font-mono, ui-monospace, monospace)', fontSize: 12 }}>{describeTrigger(r, t)}</span>,
      },
      {
        title: t('common.status'), dataIndex: 'status', width: 110, valueType: 'select',
        fieldProps: {
          allowClear: true,
          options: [
            { label: t('job.status.ready'), value: 1 },
            { label: t('job.status.paused'), value: 2 },
            { label: t('job.status.completed'), value: 3 },
            { label: t('job.status.panic'), value: 4 },
          ],
        },
        render: (_, r) => {
          if (r.status === 4) {
            return (
              <Tooltip title={t('job.panicTip', { count: r.consecutiveErrors })}>
                <Tag color="error">{t('job.status.panic')}</Tag>
              </Tooltip>
            )
          }
          if (r.status === 3) return <Tag>{t('job.status.completed')}</Tag>
          return (
            <StatusSwitch
              value={r.status === 1}
              disabled={!has('PUT:/api/v1/sys/job/{id}/enabled')}
              request={(next) => jobApi.setEnabled(r.id, next)}
              onChange={reload}
            />
          )
        },
      },
      { title: t('job.nextRunTime'), dataIndex: 'nextRunTime', search: false, width: 160, render: (_, r) => fmtTime(r.nextRunTime) },
      { title: t('job.lastRunTime'), dataIndex: 'lastRunTime', search: false, width: 160, render: (_, r) => fmtTime(r.lastRunTime) },
      {
        title: t('job.counts'), key: 'counts', search: false, width: 90,
        render: (_, r) => (
          <span>
            {r.numberOfRuns} / <span style={{ color: r.numberOfErrors > 0 ? 'var(--color-danger, #d03050)' : undefined }}>{r.numberOfErrors}</span>
          </span>
        ),
      },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 230, fixed: 'right',
        render: (_, r) => (
          <Space size={0}>
            {has('PUT:/api/v1/sys/job/{id}') && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
            {has('POST:/api/v1/sys/job/{id}/run') && <Button type="link" size="small" onClick={() => handleRun(r)}>{t('job.run')}</Button>}
            <Button type="link" size="small" onClick={() => gotoLogs(r)}>{t('job.viewLogs')}</Button>
            {has('DELETE:/api/v1/sys/job/{id}') &&
              (r.isSystem ? (
                <Tooltip title={t('job.systemProtected')}>
                  <Button type="link" size="small" danger disabled>{t('common.delete')}</Button>
                </Tooltip>
              ) : (
                <Button type="link" size="small" danger onClick={() => handleDelete(r)}>{t('common.delete')}</Button>
              ))}
          </Space>
        ),
      },
    ],
    [t, has, reload, openEdit, handleRun, gotoLogs, handleDelete],
  )

  const formLayout = { labelCol: { span: 6 }, wrapperCol: { span: 17 } }

  return (
    <>
      <DataTable<SysJob>
        ref={tableRef}
        columns={columns}
        fetcher={fetchJobs}
        persistKey="sys-job"
        rowSelection={{
          selectedRowKeys: batch.selectedKeys,
          onChange: batch.setSelectedKeys,
          getCheckboxProps: (r) => ({ disabled: r.isSystem }), // 内置任务受保护,不进批删
        }}
        toolbar={
          <Space>
            <Can code="POST:/api/v1/sys/job/batch-delete">
              <Button danger disabled={!batch.hasSelection} onClick={batch.run}>{t('common.batchDelete')}</Button>
            </Can>
            <Can code="POST:/api/v1/sys/job">
              <Button type="primary" onClick={openAdd}>{t('common.add')}</Button>
            </Can>
          </Space>
        }
      />

      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={editingId === null ? t('job.addTitle') : t('job.editTitle')}
        width={720}
        confirmText={t('common.save')}
        onConfirm={save}
      >
        <Form form={form} {...formLayout} style={{ marginTop: 4 }}>
          {/* ── 基本 ── */}
          <Divider titlePlacement="start" size="small">{t('job.section.basic')}</Divider>
          <Form.Item
            name="code" label={t('job.code')}
            rules={editingId === null ? [{ required: true, whitespace: true, message: t('job.form.codeRequired') }] : undefined}
          >
            {/* 任务编码建后不可变(后端更新时忽略);编辑时置灰 */}
            <Input disabled={editingId !== null} placeholder={t('job.form.codePlaceholder')} />
          </Form.Item>
          <Form.Item name="name" label={t('job.name')} rules={[{ required: true, whitespace: true, message: t('job.form.nameRequired') }]}>
            <Input placeholder={t('job.name')} />
          </Form.Item>
          <Form.Item name="remark" label={t('job.remark')}>
            <Input.TextArea rows={2} />
          </Form.Item>

          {/* ── 触发 ── */}
          <Divider titlePlacement="start" size="small">{t('job.section.trigger')}</Divider>
          <Form.Item name="triggerKind" label={t('job.form.triggerKind')}>
            <Radio.Group
              options={[
                { label: t('job.trigger.cron'), value: 1 },
                { label: t('job.trigger.interval'), value: 2 },
                { label: t('job.trigger.oneShot'), value: 3 },
              ]}
            />
          </Form.Item>
          {triggerKind === 1 && (
            <Form.Item name="cronExpression" label={t('job.form.cron')} rules={[{ required: true, whitespace: true, message: t('job.form.cronRequired') }]}>
              <CronEditor />
            </Form.Item>
          )}
          {triggerKind === 2 && (
            <Form.Item name="intervalSeconds" label={t('job.form.interval')} rules={[{ required: true, message: t('job.form.intervalRequired') }]}>
              <InputNumber min={5} precision={0} suffix={t('job.form.seconds')} style={{ width: 180 }} />
            </Form.Item>
          )}
          {triggerKind === 3 && (
            <Form.Item name="oneShotTime" label={t('job.form.oneShotTime')} rules={[{ required: true, message: t('job.form.oneShotRequired') }]}>
              <DatePicker showTime style={{ width: 220 }} />
            </Form.Item>
          )}
          <Form.Item label={t('job.form.window')}>
            <Space size={8}>
              <Form.Item name="startTime" noStyle>
                <DatePicker showTime placeholder={t('job.form.windowStart')} style={{ width: 200 }} />
              </Form.Item>
              <span>~</span>
              <Form.Item name="endTime" noStyle>
                <DatePicker showTime placeholder={t('job.form.windowEnd')} style={{ width: 200 }} />
              </Form.Item>
            </Space>
          </Form.Item>
          <Form.Item name="misfireStrategy" label={t('job.form.misfireStrategy')}>
            <Radio.Group
              options={[
                { label: t('job.form.misfireSkip'), value: 1 },
                { label: t('job.form.misfireFireOnceNow'), value: 2 },
              ]}
            />
          </Form.Item>
          <Form.Item name="concurrencyMode" label={t('job.form.concurrencyMode')}>
            <Radio.Group
              options={[
                { label: t('job.form.concurrencySerial'), value: 1 },
                { label: t('job.form.concurrencyParallel'), value: 2 },
              ]}
            />
          </Form.Item>

          {/* ── 载荷 ── */}
          <Divider titlePlacement="start" size="small">{t('job.section.handler')}</Divider>
          <Form.Item
            name="handlerKind"
            label={t('job.handlerKind')}
            // 总闸关着就说清是"后端没开",否则用户只会看见一个莫名其妙点不动的选项
            extra={sqlEnabled ? undefined : t('job.form.sqlDisabledHint')}
          >
            <Radio.Group
              options={[
                { label: t('job.handler.compiled'), value: 1 },
                { label: t('job.handler.http'), value: 2 },
                { label: t('job.handler.sql'), value: 3, disabled: !sqlEnabled },
              ]}
            />
          </Form.Item>
          {handlerKind === 1 && (
            <>
              <Form.Item name="handlerName" label={t('job.form.handlerName')} rules={[{ required: true, message: t('job.form.handlerRequired') }]}>
                <Select
                  showSearch={{ optionFilterProp: 'label' }}
                  placeholder={t('job.form.handlerPlaceholder')}
                  loading={open && handlerOptions === null}
                  options={(handlerOptions ?? []).map((h) => ({ label: h, value: h }))}
                />
              </Form.Item>
              <Form.Item label={t('job.form.props')}>
                <Form.List name="props">
                  {(fields, { add, remove }) => (
                    <>
                      {fields.map((field) => (
                        <Space key={field.key} align="baseline" style={{ display: 'flex', marginBottom: 8 }}>
                          <Form.Item name={[field.name, 'key']} noStyle>
                            <Input placeholder={t('job.form.propKey')} style={{ width: 160 }} />
                          </Form.Item>
                          <Form.Item name={[field.name, 'value']} noStyle>
                            <Input placeholder={t('job.form.propValue')} style={{ width: 240 }} />
                          </Form.Item>
                          <Button type="link" danger size="small" onClick={() => remove(field.name)}>{t('common.delete')}</Button>
                        </Space>
                      ))}
                      <Button type="dashed" block onClick={() => add({ key: '', value: '' })}>{t('job.form.addProp')}</Button>
                    </>
                  )}
                </Form.List>
              </Form.Item>
            </>
          )}
          {handlerKind === 2 && (
            <>
              <Form.Item name="httpUrl" label="URL" rules={[{ required: true, whitespace: true, message: t('job.form.urlRequired') }]}>
                <Input placeholder="https://" />
              </Form.Item>
              <Form.Item name="httpMethod" label={t('job.form.method')}>
                <Select
                  style={{ width: 140 }}
                  options={['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD'].map((m) => ({ label: m, value: m }))}
                />
              </Form.Item>
              <Form.Item label={t('job.form.headers')} extra={t('job.form.headersMaskHint')}>
                <Form.List name="httpHeaders">
                  {(fields, { add, remove }) => (
                    <>
                      {fields.map((field) => (
                        <Space key={field.key} align="baseline" style={{ display: 'flex', marginBottom: 8 }}>
                          <Form.Item name={[field.name, 'key']} noStyle>
                            <Input placeholder={t('job.form.propKey')} style={{ width: 160 }} />
                          </Form.Item>
                          <Form.Item name={[field.name, 'value']} noStyle>
                            <Input placeholder={t('job.form.propValue')} style={{ width: 240 }} />
                          </Form.Item>
                          <Button type="link" danger size="small" onClick={() => remove(field.name)}>{t('common.delete')}</Button>
                        </Space>
                      ))}
                      <Button type="dashed" block onClick={() => add({ key: '', value: '' })}>{t('job.form.addHeader')}</Button>
                    </>
                  )}
                </Form.List>
              </Form.Item>
              <Form.Item name="httpBody" label={t('job.form.body')}>
                <Input.TextArea rows={3} placeholder={t('job.form.bodyPlaceholder')} />
              </Form.Item>
              <Form.Item name="httpSuccessStatuses" label={t('job.form.successStatuses')}>
                <Input placeholder={t('job.form.successStatusesPlaceholder')} style={{ width: 220 }} />
              </Form.Item>
            </>
          )}
          {handlerKind === 3 && (
            <Form.Item name="sql" label="SQL" rules={[{ required: true, whitespace: true, message: t('job.form.sqlRequired') }]}>
              <Input.TextArea rows={4} placeholder={t('job.form.sqlPlaceholder')} />
            </Form.Item>
          )}

          {/* ── 失败处理 ── */}
          <Divider titlePlacement="start" size="small">{t('job.section.failure')}</Divider>
          <Form.Item name="timeoutSeconds" label={t('job.form.timeoutSeconds')} extra={t('job.form.timeoutHint')}>
            <InputNumber min={0} precision={0} suffix={t('job.form.seconds')} style={{ width: 180 }} />
          </Form.Item>
          <Form.Item name="retryCount" label={t('job.form.retryCount')}>
            <InputNumber min={0} precision={0} style={{ width: 180 }} />
          </Form.Item>
          <Form.Item name="retryIntervalSeconds" label={t('job.form.retryInterval')}>
            <InputNumber min={0} precision={0} suffix={t('job.form.seconds')} style={{ width: 180 }} />
          </Form.Item>
          <Form.Item name="failAlertThreshold" label={t('job.form.failAlertThreshold')} extra={t('job.form.failAlertHint')}>
            <InputNumber min={0} precision={0} style={{ width: 180 }} />
          </Form.Item>
          <Form.Item name="alertByNotice" label={t('job.form.alertByNotice')} valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="alertEmails" label={t('job.form.alertEmails')}>
            <Input placeholder={t('job.form.alertEmailsPlaceholder')} />
          </Form.Item>
        </Form>
      </FormContainer>
    </>
  )
}
