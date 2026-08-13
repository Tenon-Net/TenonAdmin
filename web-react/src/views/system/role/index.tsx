// 角色管理 = DataTable CRUD + 3 抽屉:授权菜单(GrantMenuTable 三态勾选)、数据范围(范围类型 + 自定义机构多选)、
// 授权用户(UserPicker)。删角色会**物理**删掉用户↔角色关联(不可恢复),故单删/批删都先查一次持有人数当面告知。
// 纯逻辑抽 grantMenu.ts / roleForm.ts(变异钉),本页接线。
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { App, Button, Dropdown, Form, Input, InputNumber, Select, Space, Switch, type MenuProps } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { OrgTreeSelect } from '@/components/OrgTreeSelect'
import { StatusSwitch } from '@/components/StatusSwitch'
import { UserPicker, type UserPickerHandle } from '@/components/UserPicker'
import { GrantMenuTable } from './GrantMenuTable'
import { useConfirm } from '@/hooks/useConfirm'
import { useBatchDelete } from '@/hooks/useBatchDelete'
import { useAuthStore, useHasPerm } from '@/stores/auth'
import { menuApi, moduleApi, roleApi, userApi } from '@/api'
import { translateError } from '@/utils/error'
import { DataScopeType, type ModuleRow, type RoleInput, type SysRole } from '@/types/api'
import type { MenuTreeNode } from '@/types/menu'
import { UNASSIGNED } from './grantMenu'
import { blankRole, roleToInput } from './roleForm'

/** 持有该角色的用户数;复用用户分页(Size=1 只要 total)。查不到(如无用户查询权限 403)→ null,退通用文案不阻断删除。 */
async function countUsers(roleId: number): Promise<number | null> {
  try {
    return (await userApi.page({ page: 1, pageSize: 1, roleId })).total
  } catch {
    return null
  }
}

export default function RolePage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()
  const isSuperAdmin = useAuthStore((s) => s.isSuperAdmin)

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  const [modules, setModules] = useState<ModuleRow[]>([])
  useEffect(() => {
    moduleApi.list().then(setModules).catch((e: unknown) => message.error(translateError(e)))
  }, [message]) // 仅供授权抽屉「所属应用」下拉,失败不阻塞

  const fetchRoles: PageFetcher<SysRole> = (q) =>
    roleApi.page({ page: q.page, pageSize: q.pageSize, name: typeof q.name === 'string' ? q.name : undefined })

  // 批删:先查各角色持有人数,有人则警示文案。
  const batch = useBatchDelete({
    remove: roleApi.batchRemove,
    refresh: reload,
    successMsg: t('role.deleted'),
    content: async (ids) => {
      const counts = await Promise.all(ids.map(countUsers))
      if (counts.some((n) => n === null)) return t('common.batchDeleteConfirm', { count: ids.length })
      const users = counts.reduce<number>((s, n) => s + n!, 0)
      return users === 0 ? t('common.batchDeleteConfirm', { count: ids.length }) : t('role.batchDeleteWithUsers', { count: ids.length, users })
    },
  })

  const askDelete = useCallback(async (r: SysRole) => {
    const n = await countUsers(r.id)
    const content = n === null ? t('role.deleteConfirm', { name: r.name })
      : n === 0 ? t('role.deleteConfirmNoUsers', { name: r.name })
        : t('role.deleteConfirmWithUsers', { name: r.name, count: n })
    const ok = await confirm({ content, action: () => roleApi.remove(r.id), successMsg: t('role.deleted') })
    if (ok) reload()
  }, [confirm, t, reload])

  // ── 新增/编辑弹窗 ──
  const [form] = Form.useForm<RoleInput>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)
  const openAdd = useCallback(() => { setEditingId(null); form.setFieldsValue(blankRole()); setOpen(true) }, [form])
  const openEdit = useCallback((r: SysRole) => { setEditingId(r.id); form.setFieldsValue(roleToInput(r)); setOpen(true) }, [form])
  const save = async () => {
    const v = await form.validateFields()
    try {
      if (editingId === null) await roleApi.add(v)
      else await roleApi.update(editingId, v)
      message.success(t('role.saved'))
      reload()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  // ── 授权菜单抽屉 ──
  const [showMenus, setShowMenus] = useState(false)
  const [menuTree, setMenuTree] = useState<MenuTreeNode[]>([])
  const [menuGranted, setMenuGranted] = useState<number[]>([])
  const [menuRoleId, setMenuRoleId] = useState<number | null>(null)
  const [defaultModuleId, setDefaultModuleId] = useState(UNASSIGNED)
  const openMenus = useCallback(async (r: SysRole) => {
    setMenuRoleId(r.id)
    try {
      const [tree, grantedIds] = await Promise.all([menuApi.tree(), roleApi.getMenus(r.id)])
      setMenuTree(tree)
      setMenuGranted(grantedIds)
      setDefaultModuleId(useAuthStore.getState().currentModuleId ?? modules[0]?.id ?? UNASSIGNED)
      setShowMenus(true)
    } catch (e) {
      message.error(translateError(e))
    }
  }, [message, modules])
  const saveMenus = async () => {
    if (menuRoleId === null) return
    try {
      await roleApi.setMenus(menuRoleId, menuGranted)
      message.success(t('role.grantSaved'))
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  // ── 授权用户(UserPicker,命令式 ref)──
  const userPickerRef = useRef<UserPickerHandle>(null)
  const [userRoleId, setUserRoleId] = useState<number | null>(null)
  const openUsers = useCallback(async (r: SysRole) => {
    setUserRoleId(r.id)
    try {
      userPickerRef.current?.open(await roleApi.getUsers(r.id))
    } catch (e) {
      message.error(translateError(e))
    }
  }, [message])
  const onUserConfirm = useCallback(async (ids: number[]) => {
    if (userRoleId === null) return
    try {
      await roleApi.setUsers(userRoleId, ids)
      message.success(t('role.grantUsersSaved'))
    } catch (e) {
      message.error(translateError(e))
    }
  }, [userRoleId, t, message])

  // ── 数据范围抽屉 ──
  const [showScope, setShowScope] = useState(false)
  const [scopeRoleId, setScopeRoleId] = useState<number | null>(null)
  const [scopeType, setScopeType] = useState<DataScopeType>(DataScopeType.Org)
  const [customOrgIds, setCustomOrgIds] = useState<number[]>([])
  const isCustom = scopeType === DataScopeType.Custom
  const scopeOptions = useMemo(() => [
    { label: t('role.scope.all'), value: DataScopeType.All },
    { label: t('role.scope.org'), value: DataScopeType.Org },
    { label: t('role.scope.orgAndChildren'), value: DataScopeType.OrgAndChildren },
    { label: t('role.scope.self'), value: DataScopeType.Self },
    { label: t('role.scope.custom'), value: DataScopeType.Custom },
  ], [t])
  const openScope = useCallback(async (r: SysRole) => {
    setScopeRoleId(r.id)
    try {
      const cfg = await roleApi.getDataScope(r.id)
      setScopeType(cfg?.scopeType ?? DataScopeType.Org)
      setCustomOrgIds(cfg?.customOrgIds ? cfg.customOrgIds.split(',').filter(Boolean).map(Number) : [])
      setShowScope(true)
    } catch (e) {
      message.error(translateError(e))
    }
  }, [message])
  const saveScope = async () => {
    if (scopeRoleId === null) return
    try {
      await roleApi.setDataScope(scopeRoleId, scopeType, isCustom ? customOrgIds : undefined)
      message.success(t('role.scopeSaved'))
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  const columns = useMemo<ProColumns<SysRole>[]>(() => [
    { title: t('role.code'), dataIndex: 'code', search: false, ellipsis: true },
    { title: t('role.name'), dataIndex: 'name' }, // 唯一可搜列
    { title: t('role.sort'), dataIndex: 'sort', search: false, width: 80 },
    {
      title: t('common.status'), dataIndex: 'enabled', search: false, width: 90,
      // StatusSwitch 悲观受控(checked 绑 value):成功后必须 onChange={reload} 重拉,否则行数据不变、开关视觉回弹误导管理员。对齐 module 页。
      render: (_, r) => (
        <StatusSwitch value={r.enabled} disabled={!isSuperAdmin || !has('PUT:/api/v1/sys/role/{id}')} request={(next) => roleApi.update(r.id, { ...roleToInput(r), enabled: next })} onChange={reload} />
      ),
    },
    { title: t('role.remark'), dataIndex: 'remark', search: false, ellipsis: true, render: (_, r) => r.remark || '—' },
    {
      title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 240, fixed: 'right',
      render: (_, r) => {
        // QA36:角色定义(编辑/删除)与角色授权面(授权菜单/数据范围)为超管专属;
        // 仅"授权用户"(角色指派面)对普通管理员开放(经 RoleGrantPolicy 收口校验)。
        const moreItems = ([
          isSuperAdmin && has('PUT:/api/v1/sys/role/menu') ? { key: 'menus', label: t('role.grantMenus') } : null,
          isSuperAdmin && has('PUT:/api/v1/sys/role/datascope') ? { key: 'scope', label: t('role.dataScope') } : null,
          has('PUT:/api/v1/sys/role/users') ? { key: 'users', label: t('role.grantUsers') } : null,
        ] as MenuProps['items'])!.filter(Boolean)
        const onMore: MenuProps['onClick'] = ({ key }) => (key === 'menus' ? openMenus(r) : key === 'scope' ? openScope(r) : openUsers(r))
        return (
          <Space size={4}>
            {isSuperAdmin && has('PUT:/api/v1/sys/role/{id}') && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
            {isSuperAdmin && has('DELETE:/api/v1/sys/role/{id}') && <Button type="link" size="small" danger onClick={() => askDelete(r)}>{t('common.delete')}</Button>}
            {moreItems!.length > 0 && (
              <Dropdown menu={{ items: moreItems, onClick: onMore }} trigger={['click']}>
                <Button type="link" size="small">{t('common.more')}</Button>
              </Dropdown>
            )}
          </Space>
        )
      },
    },
  ], [t, has, isSuperAdmin, reload, openEdit, askDelete, openMenus, openScope, openUsers])

  return (
    <>
      <DataTable<SysRole>
        ref={tableRef}
        columns={columns}
        fetcher={fetchRoles}
        persistKey="sys-role"
        rowSelection={{ selectedRowKeys: batch.selectedKeys, onChange: batch.setSelectedKeys }}
        toolbar={isSuperAdmin ? (
          <Space>
            <Can code="POST:/api/v1/sys/role/add"><Button type="primary" onClick={openAdd}>{t('common.add')}</Button></Can>
            <Can code="POST:/api/v1/sys/role/batch-delete"><Button danger disabled={!batch.hasSelection} onClick={batch.run}>{t('common.batchDelete')}</Button></Can>
          </Space>
        ) : undefined}
      />

      {/* 新增/编辑 */}
      <FormContainer open={open} onOpenChange={setOpen} title={editingId === null ? t('role.addTitle') : t('role.editTitle')} width={480} confirmText={t('common.save')} onConfirm={save}>
        <Form form={form} labelCol={{ span: 5 }} wrapperCol={{ span: 19 }} style={{ marginTop: 12 }}>
          <Form.Item name="code" label={t('role.code')} rules={[{ required: true, whitespace: true, message: t('role.codeRequired') }]}>
            <Input disabled={editingId !== null} placeholder={t('role.code')} />
          </Form.Item>
          <Form.Item name="name" label={t('role.name')} rules={[{ required: true, whitespace: true, message: t('role.nameRequired') }]}>
            <Input placeholder={t('role.name')} />
          </Form.Item>
          <Form.Item name="sort" label={t('role.sort')}><InputNumber min={0} style={{ width: 160 }} /></Form.Item>
          <Form.Item name="remark" label={t('role.remark')}><Input.TextArea autoSize={{ minRows: 2 }} placeholder={t('role.remark')} /></Form.Item>
          <Form.Item name="enabled" label={t('common.status')} valuePropName="checked"><Switch /></Form.Item>
          {isSuperAdmin && <Form.Item name="isDelegatable" label={t('role.isDelegatable')} valuePropName="checked"><Switch /></Form.Item>}
        </Form>
      </FormContainer>

      {/* 授权菜单 */}
      <FormContainer open={showMenus} onOpenChange={setShowMenus} title={t('role.grantMenus')} width={820} confirmText={t('common.save')} onConfirm={saveMenus}>
        <GrantMenuTable tree={menuTree} granted={menuGranted} modules={modules} defaultModuleId={defaultModuleId} onCheckedChange={setMenuGranted} />
      </FormContainer>

      {/* 授权用户 */}
      <UserPicker ref={userPickerRef} onConfirm={onUserConfirm} />

      {/* 数据范围 */}
      <FormContainer open={showScope} onOpenChange={setShowScope} title={t('role.dataScope')} width={480} confirmText={t('common.save')} onConfirm={saveScope}>
        <Form labelCol={{ span: 6 }} wrapperCol={{ span: 18 }} style={{ marginTop: 12 }}>
          <Form.Item label={t('role.scopeType')}>
            <Select value={scopeType} onChange={setScopeType} options={scopeOptions} />
          </Form.Item>
          {isCustom && (
            <Form.Item label={t('role.customOrgs')}>
              <OrgTreeSelect multiple value={customOrgIds} onChange={(v) => setCustomOrgIds((v as number[]) ?? [])} placeholder={t('role.customOrgsHint')} style={{ width: '100%' }} />
            </Form.Item>
          )}
        </Form>
      </FormContainer>
    </>
  )
}
