// 用户管理页。C1 起改用共享组件层:写侧表单走 <FormContainer>(Modal/Drawer 二合一,onConfirm owns
// loading+close),行内启停走 <StatusSwitch>(悲观 + 自动回滚),性别下拉/列翻译走 <DictSelect>/<DictTag>。
// 机构树选择 / 头像上传 / 批量删除等仍属后续批次(orgId 等四字段在本页仍只透传,防全量替换清空)。
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { App, Button, Form, Input, Modal, Select, Space, Switch, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import type { ProColumns } from '@ant-design/pro-components'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { StatusSwitch } from '@/components/StatusSwitch'
import { DictSelect } from '@/components/DictSelect'
import { DictTag } from '@/components/DictTag'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { roleApi, userApi } from '@/api'
import { translateError } from '@/utils/error'
import type { UserItem } from '@/types/api'
import {
  blankForm, canDelete, canEdit, canReset, canToggleEnabled, detailToForm, toAddInput, toUpdateInput,
  type UserForm,
} from './userForm'

/** 弹窗表单里**有编辑控件**的字段;透传字段(orgId 等)不进 Form,save 时从 extraRef 合回。 */
type EditableFields = Pick<UserForm, 'account' | 'password' | 'name' | 'nickname' | 'phone' | 'email' | 'gender' | 'enabled' | 'roleIds'>

export default function UserPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  // 角色下拉源:拉一页足量(ponytail: 200 覆盖绝大多数系统,真超再上分页搜索)。失败静默不打断列表。
  const [roleOptions, setRoleOptions] = useState<{ label: string; value: number }[]>([])
  useEffect(() => {
    roleApi
      .page({ page: 1, pageSize: 200 })
      .then(({ items }) => setRoleOptions(items.map((r) => ({ label: r.name, value: r.id }))))
      .catch(() => {})
  }, [])

  // ── 分页取数:ProTable 搜索表单值(unknown)→ userApi.page 的强类型入参 ──
  // account/name 来自搜索表单(列未设 search:false);sortField/sortOrder 来自列排序(toProTable 已映)。
  // 有意**不 memo**:pro-table 经 useRefFunction 读 request,父组件重渲染(开弹窗/roleOptions)不会触发重取;
  // 反倒是给它加 useCallback + 错误的依赖数组才会变成真 footgun。别"顺手优化"成 memo。
  const fetchUsers: PageFetcher<UserItem> = (q) =>
    userApi.page({
      page: q.page,
      pageSize: q.pageSize,
      account: typeof q.account === 'string' ? q.account : undefined,
      name: typeof q.name === 'string' ? q.name : undefined,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
    })

  // ── 新增/编辑弹窗(FormContainer:loading/关闭由它按 onConfirm 结果接管)──
  const [form] = Form.useForm<EditableFields>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)
  // 无编辑控件、仅透传的字段(orgId/positionId/directorId/avatar):开弹窗时暂存,save 时合回,防全量替换清空。
  const extraRef = useRef<Pick<UserForm, 'orgId' | 'positionId' | 'directorId' | 'avatar'>>({
    orgId: null, positionId: null, directorId: null, avatar: null,
  })

  const openAdd = () => {
    setEditingId(null)
    const b = blankForm()
    extraRef.current = { orgId: b.orgId, positionId: b.positionId, directorId: b.directorId, avatar: b.avatar }
    form.setFieldsValue(b)
    setOpen(true)
  }
  /** 编辑:先取 detail(拿 roleIds + 透传字段回显),成功再开弹层;失败弹码不开。 */
  const openEdit = useCallback(
    async (r: UserItem) => {
      try {
        const f = detailToForm(await userApi.detail(r.id))
        extraRef.current = { orgId: f.orgId, positionId: f.positionId, directorId: f.directorId, avatar: f.avatar }
        form.setFieldsValue(f)
        setEditingId(r.id)
        setOpen(true)
      } catch (e) {
        message.error(translateError(e))
      }
    },
    [form, message],
  )

  /**
   * FormContainer 的 onConfirm 协议:校验失败(抛)/API 失败(返 false)→ 不关;成功 → 返 undefined 关闭。
   * loading 与关闭都由 FormContainer 管,这里只负责校验、映射、发请求、报错。
   */
  const save = async () => {
    const v = await form.validateFields() // 校验失败抛 → FormContainer 不关
    // v 只含可编辑字段;blankForm 补默认、extraRef 补透传,合成完整 UserForm 再映射入参。
    const full: UserForm = { ...blankForm(), ...v, ...extraRef.current }
    try {
      if (editingId === null) {
        const out = await userApi.add(toAddInput(full))
        // 管理员没自定义口令时,系统随机口令只此一次可见 —— 不弹出来这个号谁也登不进去。
        if (!full.password) showInitialPassword(out.initialPassword, true)
      } else {
        await userApi.update(editingId, toUpdateInput(full))
      }
      message.success(t('user.saved'))
      reload()
    } catch (e) {
      message.error(translateError(e))
      return false // 留在弹层,不关
    }
  }

  const handleDelete = useCallback(
    (r: UserItem) => {
      confirm({ content: t('user.deleteConfirm', { name: r.name }), action: () => userApi.remove(r.id), successMsg: t('user.deleted') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, reload],
  )

  // ── 重置密码(FormContainer)+ 初始口令结果弹层(建号留空口令 / 重置 共用:明文只此一次,管理员当场转达)──
  const [resetTarget, setResetTarget] = useState<UserItem | null>(null)
  const [resetPwd, setResetPwd] = useState('')
  const [resultPwd, setResultPwd] = useState<string | null>(null)
  const [resultFromCreate, setResultFromCreate] = useState(false)
  const showInitialPassword = (pwd: string, fromCreate: boolean) => {
    setResultPwd(pwd)
    setResultFromCreate(fromCreate)
  }
  const openReset = useCallback((r: UserItem) => {
    setResetTarget(r)
    setResetPwd('')
  }, [])
  const doReset = async () => {
    if (!resetTarget) return
    try {
      const pwd = await userApi.resetPassword(resetTarget.id, resetPwd || null)
      showInitialPassword(pwd, false) // 成功:结果弹层展示,FormContainer 返 undefined 自动关重置弹层
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }
  const copyResult = async () => {
    if (!resultPwd) return
    try {
      await navigator.clipboard.writeText(resultPwd)
      message.success(t('user.copied'))
    } catch {
      message.error(t('user.copyFailed'))
    }
  }

  const columns = useMemo<ProColumns<UserItem>[]>(
    () => [
      { title: t('user.account'), dataIndex: 'account', sorter: true, ellipsis: true },
      { title: t('user.name'), dataIndex: 'name' },
      { title: t('user.phone'), dataIndex: 'phone', search: false, render: (_, r) => r.phone || '—' },
      { title: t('user.gender'), dataIndex: 'gender', search: false, width: 80, render: (_, r) => <DictTag typeCode="gender" value={r.gender} /> },
      {
        title: t('common.status'), dataIndex: 'enabled', search: false, width: 90,
        render: (_, r) => (
          <StatusSwitch
            value={r.enabled}
            disabled={!canToggleEnabled(r, has)}
            confirm={(next) => (next ? null : t('user.disableConfirm', { name: r.name }))}
            request={(next) => userApi.setEnabled(r.id, next)}
            onChange={reload}
          />
        ),
      },
      {
        title: t('user.superAdmin'), dataIndex: 'isSuperAdmin', search: false, width: 90,
        render: (_, r) => (r.isSuperAdmin ? <Tag color="warning">{t('user.superAdmin')}</Tag> : '—'),
      },
      { title: t('user.createTime'), dataIndex: 'createTime', search: false, sorter: true },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 200, fixed: 'right',
        render: (_, r) => (
          <Space size={4}>
            {canEdit(r, has) && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
            {canReset(r, has) && <Button type="link" size="small" onClick={() => openReset(r)}>{t('user.resetPassword')}</Button>}
            {canDelete(r, has) && <Button type="link" size="small" danger onClick={() => handleDelete(r)}>{t('common.delete')}</Button>}
          </Space>
        ),
      },
    ],
    [t, has, reload, openEdit, openReset, handleDelete],
  )

  return (
    <>
      <DataTable<UserItem>
        ref={tableRef}
        columns={columns}
        fetcher={fetchUsers}
        persistKey="sys-user"
        headerTitle={t('user.title')}
        toolbar={
          <Can code="POST:/api/v1/sys/user">
            <Button type="primary" onClick={openAdd}>{t('common.add')}</Button>
          </Can>
        }
      />

      {/* 新增 / 编辑 */}
      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={editingId === null ? t('user.addTitle') : t('user.editTitle')}
        width={560}
        onConfirm={save}
      >
        <Form form={form} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }} style={{ marginTop: 12 }}>
          <Form.Item
            name="account" label={t('user.account')}
            rules={[{ required: true, whitespace: true, message: t('user.accountRequired') }]}
          >
            {/* 账号建后不可改(后端也拒);编辑时置灰 */}
            <Input disabled={editingId !== null} placeholder={t('user.account')} />
          </Form.Item>
          {editingId === null && (
            <Form.Item name="password" label={t('user.password')}>
              <Input.Password placeholder={t('user.passwordHint')} />
            </Form.Item>
          )}
          <Form.Item
            name="name" label={t('user.name')}
            rules={[{ required: true, whitespace: true, message: t('user.nameRequired') }]}
          >
            <Input placeholder={t('user.name')} />
          </Form.Item>
          <Form.Item name="nickname" label={t('user.nickname')}>
            <Input placeholder={t('user.nicknamePlaceholder')} />
          </Form.Item>
          <Form.Item
            name="phone" label={t('user.phone')}
            rules={[{ validator: (_, v: string) => (!v || /^1[3-9]\d{9}$/.test(v) ? Promise.resolve() : Promise.reject(new Error(t('user.phoneInvalid')))) }]}
          >
            <Input placeholder={t('user.phonePlaceholder')} />
          </Form.Item>
          <Form.Item
            name="email" label={t('user.email')}
            rules={[{ validator: (_, v: string) => (!v || /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(v) ? Promise.resolve() : Promise.reject(new Error(t('user.emailInvalid')))) }]}
          >
            <Input placeholder={t('user.emailPlaceholder')} />
          </Form.Item>
          <Form.Item name="gender" label={t('user.gender')}>
            <DictSelect typeCode="gender" allowClear placeholder={t('user.genderPlaceholder')} />
          </Form.Item>
          <Form.Item name="roleIds" label={t('user.roles')}>
            <Select mode="multiple" allowClear placeholder={t('user.rolesPlaceholder')} options={roleOptions} />
          </Form.Item>
          <Form.Item name="enabled" label={t('common.status')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </FormContainer>

      {/* 重置密码 */}
      <FormContainer
        open={resetTarget !== null}
        onOpenChange={(o) => { if (!o) setResetTarget(null) }}
        title={t('user.resetPassword')}
        width={420}
        onConfirm={doReset}
      >
        <Form labelCol={{ span: 7 }} wrapperCol={{ span: 17 }} style={{ marginTop: 12 }}>
          <Form.Item label={t('user.newPassword')}>
            <Input.Password value={resetPwd} onChange={(e) => setResetPwd(e.target.value)} placeholder={t('user.newPasswordHint')} />
          </Form.Item>
        </Form>
      </FormContainer>

      {/* 初始口令结果(只读可复制):建号留空口令 / 重置密码 共用 —— 信息展示,非表单,保持原生 Modal */}
      <Modal
        open={resultPwd !== null}
        title={resultFromCreate ? t('user.createDone') : t('user.resetDone')}
        width={420}
        onOk={() => setResultPwd(null)}
        onCancel={() => setResultPwd(null)}
        cancelButtonProps={{ style: { display: 'none' } }}
        okText={t('common.confirm')}
      >
        <p style={{ marginBottom: 12, color: 'var(--color-text-secondary, #888)' }}>{t('user.resetDoneHint')}</p>
        <Input
          readOnly value={resultPwd ?? ''}
          suffix={<Button type="link" size="small" style={{ padding: 0 }} onClick={copyResult}>{t('user.copy')}</Button>}
        />
      </Modal>
    </>
  )
}
