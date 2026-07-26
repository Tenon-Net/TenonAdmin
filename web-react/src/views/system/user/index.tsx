// 用户管理页。C1 起改用共享组件层:写侧表单走 <FormContainer>(Modal/Drawer 二合一,onConfirm owns
// loading+close),行内启停走 <StatusSwitch>(悲观 + 自动回滚),性别下拉/列翻译走 <DictSelect>/<DictTag>。
// 表单两列栅格 + 机构/职位/主管控件对齐 Vue 用户页;仅头像仍是 Form 外受控 state(save 时并回)。
// 导入导出(G7):ImportWizard 四步向导 + ExportColumnsModal 选列导出(带当前筛选)。
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { App, Avatar, Button, Card, Col, Form, Input, Modal, Row, Select, Space, Switch, Tag, Tree } from 'antd'
import type { TreeDataNode } from 'antd'
import { useTranslation } from 'react-i18next'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import type { ProColumns } from '@ant-design/pro-components'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { FileUpload } from '@/components/FileUpload'
import { StatusSwitch } from '@/components/StatusSwitch'
import { DictSelect } from '@/components/DictSelect'
import { DictTag } from '@/components/DictTag'
import { OrgTreeSelect } from '@/components/OrgTreeSelect'
import { ImportWizard, type ImportWizardApi } from '@/components/ImportWizard'
import { ExportColumnsModal } from '@/components/ExportColumnsModal'
import { useConfirm } from '@/hooks/useConfirm'
import { useBatchDelete } from '@/hooks/useBatchDelete'
import { useHasPerm } from '@/stores/auth'
import { orgApi, positionApi, roleApi, userApi } from '@/api'
import { buildTree, type Tree as TreeNode } from '@/utils/tree'
import { translateError } from '@/utils/error'
import { triggerBlobDownload } from '@/utils/download'
import type { ExportColumnDef, SysOrg, UserItem } from '@/types/api'
import {
  blankForm, canDelete, canEdit, canReset, canToggleEnabled, detailToForm, toAddInput, toUpdateInput,
  type UserForm,
} from './userForm'

/** 弹窗表单里**有编辑控件**的字段;仅头像不进 Form(受控 state 挂上传组件),save 时并回。 */
type EditableFields = Omit<UserForm, 'avatar'>

export default function UserPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  // ── 左侧机构树筛选:选中机构 → 作为 params.orgId 传给表格,变了自动回第 1 页(对齐 Vue 用户页左树)──
  const [orgTree, setOrgTree] = useState<TreeNode<SysOrg>[]>([])
  const [selectedOrgId, setSelectedOrgId] = useState<number | null>(null)
  useEffect(() => {
    orgApi.list().then((flat) => setOrgTree(buildTree(flat))).catch((e: unknown) => message.error(translateError(e)))
  }, [message])

  // ── 批量删除(复用 hook + userApi.batchRemove;超管行禁勾见下方 rowSelection.getCheckboxProps)──
  const batch = useBatchDelete({ remove: userApi.batchRemove, refresh: reload, successMsg: t('user.deleted') })

  // 角色下拉源:拉一页足量(ponytail: 200 覆盖绝大多数系统,真超再上分页搜索)。失败静默不打断列表。
  const [roleOptions, setRoleOptions] = useState<{ label: string; value: number }[]>([])
  useEffect(() => {
    roleApi
      .page({ page: 1, pageSize: 200 })
      .then(({ items }) => setRoleOptions(items.map((r) => ({ label: r.name, value: r.id }))))
      .catch((e: unknown) => message.error(translateError(e)))
  }, [message])

  // 职位/主管下拉源:同上拉一页足量;两者是配角,失败静默不打断列表(对齐 Vue)。
  const [positionOptions, setPositionOptions] = useState<{ label: string; value: number }[]>([])
  const [directorOptions, setDirectorOptions] = useState<{ label: string; value: number }[]>([])
  useEffect(() => {
    positionApi
      .page({ page: 1, pageSize: 200 })
      .then(({ items }) => setPositionOptions(items.map((p) => ({ label: p.name, value: p.id }))))
      .catch(() => {})
    // 主管候选就是用户自身(允许把某用户选为自己主管,后台系统无害,不加环检)
    userApi
      .page({ page: 1, pageSize: 200 })
      .then(({ items }) => setDirectorOptions(items.map((u) => ({ label: `${u.name}(${u.account})`, value: u.id }))))
      .catch(() => {})
  }, [])

  // ── 导入 / 导出 ──
  const [importOpen, setImportOpen] = useState(false)
  const [exportOpen, setExportOpen] = useState(false)
  const [exporting, setExporting] = useState(false)
  /** DataTable 不外泄搜索表单;在 fetcher 里截一份当前筛选,导出时带上。 */
  const lastQueryRef = useRef<{
    account?: string
    name?: string
    orgId?: number
    sortField?: string
    sortOrder?: string
  }>({})

  /** 与后端 UserExportProfile.Columns 对齐(前端无列清单端点,照档案硬编码)。 */
  const userExportColumns: ExportColumnDef[] = useMemo(
    () => [
      { key: 'Account', title: '登录账号' },
      { key: 'Name', title: '姓名' },
      { key: 'Nickname', title: '昵称' },
      { key: 'Phone', title: '手机号' },
      { key: 'Email', title: '邮箱' },
      { key: 'Gender', title: '性别' },
      { key: 'OrgName', title: '所属机构' },
      { key: 'PositionName', title: '职位' },
      { key: 'DirectorName', title: '直属主管' },
      { key: 'Enabled', title: '启用状态' },
      { key: 'IsSuperAdmin', title: '超级管理员', defaultSelected: false },
      { key: 'CreateTime', title: '创建时间' },
    ],
    [],
  )

  const userImportApi: ImportWizardApi = useMemo(
    () => ({
      downloadTemplate: () => userApi.importTemplate(),
      preview: (file, mapping) => userApi.importPreview(file, mapping),
      validate: (rows) => userApi.importValidate(rows),
      commit: (rows, strategy) => userApi.importCommit(rows, strategy),
      errorReport: (rows) => userApi.importErrorReport(rows),
    }),
    [],
  )

  /** 导出:带当前 DataTable 筛选(含左侧机构树 orgId)+ 选中列。 */
  const onExport = async (keys: string[]) => {
    const p = lastQueryRef.current
    setExporting(true)
    try {
      const blob = await userApi.export({
        account: p.account || undefined,
        name: p.name || undefined,
        orgId: p.orgId,
        sortField: p.sortField || undefined,
        sortOrder: p.sortOrder || undefined,
        columns: keys.join(','),
      })
      triggerBlobDownload(blob, '用户导出.xlsx')
      setExportOpen(false)
      message.success(t('export.done'))
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setExporting(false)
    }
  }

  // ── 分页取数:ProTable 搜索表单值(unknown)→ userApi.page 的强类型入参 ──
  // account/name 来自搜索表单(列未设 search:false);sortField/sortOrder 来自列排序(toProTable 已映)。
  // 有意**不 memo**:pro-table 经 useRefFunction 读 request,父组件重渲染(开弹窗/roleOptions)不会触发重取;
  // 反倒是给它加 useCallback + 错误的依赖数组才会变成真 footgun。别"顺手优化"成 memo。
  const fetchUsers: PageFetcher<UserItem> = (q) => {
    const account = typeof q.account === 'string' ? q.account : undefined
    const name = typeof q.name === 'string' ? q.name : undefined
    // orgId 来自左侧机构树(经 DataTable 的 params 透传),非搜索表单。
    const orgId = typeof q.orgId === 'number' ? q.orgId : undefined
    lastQueryRef.current = {
      account,
      name,
      orgId,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
    }
    return userApi.page({
      page: q.page,
      pageSize: q.pageSize,
      account,
      name,
      orgId,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
    })
  }

  // ── 新增/编辑弹窗(FormContainer:loading/关闭由它按 onConfirm 结果接管)──
  const [form] = Form.useForm<EditableFields>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)
  // 头像现有上传控件,升级为受控 state(开弹窗同步、上传写回、save 并入 full)。存 viewUrl 签名直链,不存 id/storagePath。
  const [avatar, setAvatar] = useState<string | null>(null)
  const [avatarUploading, setAvatarUploading] = useState(false)
  const watchedName = Form.useWatch('name', form) // 头像占位字母跟随姓名

  const openAdd = () => {
    setEditingId(null)
    const b = blankForm()
    setAvatar(b.avatar)
    form.setFieldsValue(b)
    setOpen(true)
  }
  /** 编辑:先取 detail(拿 roleIds 等完整字段回显),成功再开弹层;失败弹码不开。 */
  const openEdit = useCallback(
    async (r: UserItem) => {
      try {
        const f = detailToForm(await userApi.detail(r.id))
        setAvatar(f.avatar)
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
    // v 含除头像外全部字段;blankForm 补默认(编辑态 password 无控件)、avatar 受控值最后并入,合成完整 UserForm 再映射入参。
    const full: UserForm = { ...blankForm(), ...v, avatar }
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
      {/* 左机构树侧栏(固定 200px)+ 右用户表(占满剩余);点机构过滤右表。对齐 Vue 用户页 .user-layout。 */}
      <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
        <Card
          size="small"
          title={t('user.org')}
          extra={
            selectedOrgId != null ? (
              <Button type="link" size="small" onClick={() => setSelectedOrgId(null)}>{t('user.allOrgs')}</Button>
            ) : null
          }
          style={{ flex: '0 0 200px', alignSelf: 'stretch' }}
        >
          <Tree
            blockNode
            treeData={orgTree as unknown as TreeDataNode[]}
            fieldNames={{ title: 'name', key: 'id', children: 'children' }}
            selectedKeys={selectedOrgId == null ? [] : [selectedOrgId]}
            onSelect={(keys) => setSelectedOrgId(keys.length ? Number(keys[0]) : null)}
          />
        </Card>

        <div style={{ flex: 1, minWidth: 0 }}>
          <DataTable<UserItem>
            ref={tableRef}
            columns={columns}
            fetcher={fetchUsers}
            persistKey="sys-user"
            params={{ orgId: selectedOrgId ?? undefined }}
            rowSelection={{
              selectedRowKeys: batch.selectedKeys,
              onChange: batch.setSelectedKeys,
              getCheckboxProps: (r) => ({ disabled: r.isSuperAdmin }), // 超管禁勾(自锁保护,对齐行内禁删)
            }}
            toolbar={
              <Space>
                <Can code="POST:/api/v1/sys/user">
                  <Button type="primary" onClick={openAdd}>{t('common.add')}</Button>
                </Can>
                <Can code="POST:/api/v1/sys/user/batch-delete">
                  <Button danger disabled={!batch.hasSelection} onClick={batch.run}>{t('common.batchDelete')}</Button>
                </Can>
                {/* §6.2 权限码一字不差:导入入口 = preview;导出 = export */}
                <Can code="POST:/api/v1/sys/user/import/preview">
                  <Button onClick={() => setImportOpen(true)}>{t('import.button')}</Button>
                </Can>
                <Can code="GET:/api/v1/sys/user/export">
                  <Button onClick={() => setExportOpen(true)}>{t('export.button')}</Button>
                </Can>
              </Space>
            }
          />
        </div>
      </div>

      {/* 新增 / 编辑 */}
      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={editingId === null ? t('user.addTitle') : t('user.editTitle')}
        width={640}
        onConfirm={save}
      >
        {/* 两列栅格(对齐 Vue):相关字段成对排,头像整行;账号编辑时禁改并占整行。定宽 label 让半行/整行项对齐。 */}
        <Form form={form} labelCol={{ flex: '76px' }} wrapperCol={{ flex: 1 }} style={{ marginTop: 12 }}>
          <Row gutter={16}>
            <Col xs={24} sm={editingId === null ? 12 : 24}>
              <Form.Item
                name="account" label={t('user.account')}
                rules={[{ required: true, whitespace: true, message: t('user.accountRequired') }]}
              >
                {/* 账号建后不可改(后端也拒);编辑时置灰 */}
                <Input disabled={editingId !== null} placeholder={t('user.account')} />
              </Form.Item>
            </Col>
            {editingId === null && (
              <Col xs={24} sm={12}>
                <Form.Item name="password" label={t('user.password')}>
                  <Input.Password placeholder={t('user.passwordHint')} />
                </Form.Item>
              </Col>
            )}
            <Col xs={24} sm={12}>
              <Form.Item
                name="name" label={t('user.name')}
                rules={[{ required: true, whitespace: true, message: t('user.nameRequired') }]}
              >
                <Input placeholder={t('user.name')} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="nickname" label={t('user.nickname')}>
                <Input placeholder={t('user.nicknamePlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="gender" label={t('user.gender')}>
                <DictSelect typeCode="gender" allowClear placeholder={t('user.genderPlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item
                name="phone" label={t('user.phone')}
                rules={[{ validator: (_, v: string) => (!v || /^1[3-9]\d{9}$/.test(v) ? Promise.resolve() : Promise.reject(new Error(t('user.phoneInvalid')))) }]}
              >
                <Input placeholder={t('user.phonePlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item
                name="email" label={t('user.email')}
                rules={[{ validator: (_, v: string) => (!v || /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(v) ? Promise.resolve() : Promise.reject(new Error(t('user.emailInvalid')))) }]}
              >
                <Input placeholder={t('user.emailPlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="orgId" label={t('user.org')}>
                <OrgTreeSelect placeholder={t('user.orgPlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="positionId" label={t('user.position')}>
                <Select allowClear placeholder={t('user.positionPlaceholder')} options={positionOptions} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="directorId" label={t('user.director')}>
                {/* antd v6:optionFilterProp 收进 showSearch 对象(顶层写法已废弃) */}
                <Select showSearch={{ optionFilterProp: 'label' }} allowClear placeholder={t('user.directorPlaceholder')} options={directorOptions} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="roleIds" label={t('user.roles')}>
                <Select mode="multiple" allowClear placeholder={t('user.rolesPlaceholder')} options={roleOptions} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="enabled" label={t('common.status')} valuePropName="checked">
                <Switch />
              </Form.Item>
            </Col>
            <Col span={24}>
              {/* 头像:非表单字段,受控 state。预览用 Avatar(有图显图、无图显姓名首字母);上传走共享 FileUpload。 */}
              <Form.Item label={t('user.avatar')}>
                <Space>
                  <Avatar shape="circle" size={48} src={avatar || undefined}>
                    {watchedName?.trim()?.[0]?.toUpperCase()}
                  </Avatar>
                  <FileUpload
                    accept="image/*"
                    showUploadList={false}
                    onLoadingChange={setAvatarUploading}
                    onUploaded={(o) => setAvatar(o.viewUrl ?? null)}
                  >
                    <Button size="small" loading={avatarUploading}>
                      {avatarUploading ? t('user.avatarUploading') : t('user.avatar')}
                    </Button>
                  </FileUpload>
                </Space>
              </Form.Item>
            </Col>
          </Row>
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

      <ImportWizard
        open={importOpen}
        onOpenChange={setImportOpen}
        api={userImportApi}
        templateFileName="用户导入模板.xlsx"
        errorReportFileName="用户导入错误报告.xlsx"
        onDone={reload}
      />
      <ExportColumnsModal
        open={exportOpen}
        onOpenChange={setExportOpen}
        columns={userExportColumns}
        loading={exporting}
        onConfirm={onExport}
      />
    </>
  )
}
