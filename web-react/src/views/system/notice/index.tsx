// 消息通知管理页(管理端):发布广播/定向通知 + 分页列表 + 查看(只读 Markdown)+ 删除。
// 用户侧(未读角标/我的通知)在顶栏铃铛,不在本页。照 B11 范式:纯逻辑抽 noticeForm.ts(变异钉),本页只接线。
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { App, Button, Form, Input, Modal, Select, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import type { ProColumns } from '@ant-design/pro-components'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { MarkdownEditor } from '@/components/MarkdownEditor'
import { MarkdownView } from '@/components/MarkdownView'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { noticeApi, roleApi, userApi } from '@/api'
import { translateError } from '@/utils/error'
import { NoticeType, ReceiverType, type NoticePublishInput, type SysNotice } from '@/types/api'
import { blank, typeLabelKey, typeTagColor, receiverValid } from './noticeForm'

export default function NoticePage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  // 接收对象下拉源:角色/用户各拉一页足量(同 user 页)。发布弹窗低频,进页面预取;失败静默(配角)。
  const [roleOptions, setRoleOptions] = useState<{ label: string; value: number }[]>([])
  const [userOptions, setUserOptions] = useState<{ label: string; value: number }[]>([])
  useEffect(() => {
    roleApi.page({ page: 1, pageSize: 200 }).then(({ items }) => setRoleOptions(items.map((r) => ({ label: r.name, value: r.id })))).catch(() => {})
    userApi.page({ page: 1, pageSize: 200 }).then(({ items }) => setUserOptions(items.map((u) => ({ label: `${u.name}(${u.account})`, value: u.id })))).catch(() => {})
  }, [])

  const fetchNotices: PageFetcher<SysNotice> = (q) =>
    noticeApi.page({ page: q.page, pageSize: q.pageSize, title: typeof q.title === 'string' ? q.title : undefined })

  // ── 发布弹窗 ──
  const [form] = Form.useForm<NoticePublishInput>()
  const [open, setOpen] = useState(false)
  const receiverType = Form.useWatch('receiverType', form) ?? ReceiverType.All
  const receiverOptions = receiverType === ReceiverType.Role ? roleOptions : receiverType === ReceiverType.User ? userOptions : []

  const typeOptions = useMemo(
    () => [
      { label: t('notice.typeNotice'), value: NoticeType.Notice },
      { label: t('notice.typeAnnouncement'), value: NoticeType.Announcement },
      { label: t('notice.typeMessage'), value: NoticeType.Message },
    ],
    [t],
  )
  const receiverTypeOptions = useMemo(
    () => [
      { label: t('notice.receiverAll'), value: ReceiverType.All },
      { label: t('notice.receiverRole'), value: ReceiverType.Role },
      { label: t('notice.receiverUser'), value: ReceiverType.User },
    ],
    [t],
  )

  const openPublish = () => {
    form.setFieldsValue(blank())
    setOpen(true)
  }
  const save = async () => {
    const v = await form.validateFields() // 校验失败抛 → FormContainer 不关
    try {
      await noticeApi.publish(v)
      message.success(t('notice.published'))
      reload()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  // ── 查看(只读渲染 Markdown 正文)──
  const [viewRow, setViewRow] = useState<SysNotice | null>(null)

  const handleDelete = useCallback(
    (r: SysNotice) => {
      confirm({ content: t('notice.deleteConfirm', { title: r.title }), action: () => noticeApi.remove(r.id), successMsg: t('notice.deleted') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, reload],
  )

  const columns = useMemo<ProColumns<SysNotice>[]>(
    () => [
      { title: t('notice.noticeTitle'), dataIndex: 'title' },
      {
        title: t('notice.type'), dataIndex: 'type', search: false, width: 90,
        render: (_, r) => <Tag color={typeTagColor(r.type)}>{t(typeLabelKey(r.type))}</Tag>,
      },
      { title: t('notice.content'), dataIndex: 'content', search: false, ellipsis: true },
      { title: t('notice.publishTime'), dataIndex: 'createTime', search: false, width: 170 },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 140, fixed: 'right',
        render: (_, r) => (
          <Space size={4}>
            <Button type="link" size="small" onClick={() => setViewRow(r)}>{t('notice.view')}</Button>
            {has('DELETE:/api/v1/sys/notice/{id}') && <Button type="link" size="small" danger onClick={() => handleDelete(r)}>{t('common.delete')}</Button>}
          </Space>
        ),
      },
    ],
    [t, has, handleDelete],
  )

  return (
    <>
      <DataTable<SysNotice>
        ref={tableRef}
        columns={columns}
        fetcher={fetchNotices}
        persistKey="sys-notice"
        headerTitle={t('notice.title')}
        toolbar={
          <Can code="POST:/api/v1/sys/notice">
            <Button type="primary" onClick={openPublish}>{t('notice.publish')}</Button>
          </Can>
        }
      />

      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={t('notice.publishTitle')}
        width={820}
        confirmText={t('notice.publish')}
        onConfirm={save}
      >
        <Form form={form} labelCol={{ span: 5 }} wrapperCol={{ span: 19 }} style={{ marginTop: 12 }}>
          <Form.Item name="type" label={t('notice.type')}>
            <Select options={typeOptions} style={{ width: 180 }} />
          </Form.Item>
          <Form.Item name="receiverType" label={t('notice.receiver')}>
            {/* 切换接收范围时清空已选目标(与 Vue 侧一致,避免残留上一范围的 id) */}
            <Select options={receiverTypeOptions} style={{ width: 180 }} onChange={() => form.setFieldValue('receiverIds', [])} />
          </Form.Item>
          {receiverType !== ReceiverType.All && (
            <Form.Item
              name="receiverIds"
              label={receiverType === ReceiverType.Role ? t('notice.receiverRole') : t('notice.receiverUser')}
              rules={[
                {
                  validator: () =>
                    receiverValid(form.getFieldValue('receiverType'), form.getFieldValue('receiverIds'))
                      ? Promise.resolve()
                      : Promise.reject(new Error(t('notice.receiverRequired'))),
                },
              ]}
            >
              <Select mode="multiple" allowClear showSearch={{ optionFilterProp: 'label' }} options={receiverOptions} placeholder={t('notice.receiverPlaceholder')} />
            </Form.Item>
          )}
          <Form.Item name="title" label={t('notice.noticeTitle')} rules={[{ required: true, whitespace: true, message: t('notice.titleRequired') }]}>
            <Input placeholder={t('notice.noticeTitle')} />
          </Form.Item>
          <Form.Item name="content" label={t('notice.content')}>
            <MarkdownEditor />
          </Form.Item>
        </Form>
      </FormContainer>

      {/* 查看:只读渲染 Markdown 正文(信息展示,非表单,原生 Modal) */}
      <Modal open={viewRow !== null} title={viewRow?.title || t('notice.detailTitle')} width={720} footer={null} onCancel={() => setViewRow(null)}>
        <MarkdownView value={viewRow?.content} />
      </Modal>
    </>
  )
}
