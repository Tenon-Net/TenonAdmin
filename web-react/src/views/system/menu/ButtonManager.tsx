// 某菜单/目录下「权限按钮」的专属管理弹窗——把按钮从主菜单树里剥出来单独管,树只剩目录/菜单。
// 按钮就是 type=Button 的 SysMenu 行,增删改复用 menuApi.add/update/remove(parentId=当前菜单),后端零改动。
// React 侧用 props 受控(menu 非空即开)替代 Vue 的 imperative ref;对齐 Vue 侧 components/ButtonManager.vue。
import { useMemo, useState } from 'react'
import {
  App, AutoComplete, Button, Checkbox, Empty, Form, Input, InputNumber, Modal, Select, Space, Switch, Table,
  type TableColumnsType,
} from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { FormContainer } from '@/components/FormContainer'
import { StatusSwitch } from '@/components/StatusSwitch'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { menuApi } from '@/api'
import { translateError } from '@/utils/error'
import { MenuType, type MenuInput, type MenuTreeNode } from '@/types/menu'
import type { PermissionRouteItem } from '@/types/api'
import { belongsToApp, blankMenu, defaultTitleKey, menuRowToInput, routeSeg } from './menuForm'

export interface ButtonManagerProps {
  /** 非 null 即打开列表弹窗;{id,title} 为目标菜单。 */
  menu: { id: number; title: string } | null
  /** 由父页传入并保持响应:父页 CRUD 后重拉 tree,本弹窗的 buttons 随之刷新,无需自己再请求整树。 */
  tree: MenuTreeNode[]
  routes: PermissionRouteItem[]
  /** 当前所属应用的路由匹配前缀(sys/biz…);空=不按应用过滤,全量显示。 */
  appPrefix?: string
  onClose: () => void
  /** 弹窗内增删改后:父页重拉树 + 重建壳层。 */
  onChanged: () => void
}

/** 在整棵树里按 id 找节点(父页传的 tree 含按钮 children)。 */
function findNode(nodes: MenuTreeNode[], id: number): MenuTreeNode | null {
  for (const n of nodes) {
    if (n.id === id) return n
    const hit = findNode(n.children, id)
    if (hit) return hit
  }
  return null
}

export function ButtonManager({ menu, tree, routes, appPrefix, onClose, onChanged }: ButtonManagerProps) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  // 与主表对齐:按钮就是 type=Button 的 SysMenu 行,复用菜单 CRUD 路由权限码;服务端 [RolePermission] 兜底。
  const canAdd = has('POST:/api/v1/sys/menu/add')
  const canEdit = has('PUT:/api/v1/sys/menu/{id}')
  const canDelete = has('DELETE:/api/v1/sys/menu/{id}')

  /** 当前菜单下的按钮子节点(随父页 tree 响应更新)。 */
  const buttons = useMemo<MenuTreeNode[]>(() => {
    if (menu == null) return []
    return (findNode(tree, menu.id)?.children ?? []).filter((c) => c.type === MenuType.Button)
  }, [tree, menu])

  const usedCodes = useMemo(() => new Set(buttons.map((b) => b.permission).filter(Boolean)), [buttons])

  // ── 路由按应用软过滤:默认只列本应用路由,一键「显示全部」放开(业务页正当需要共享系统路由)──
  const seg = routeSeg(appPrefix)
  const hasOtherRoutes = seg !== '' && routes.some((r) => !belongsToApp(r.path, seg))
  const [showAllRoutes, setShowAllRoutes] = useState(false)

  // 权限码建议(AutoComplete):label 展示「METHOD /路径」,value 就是写进 permission 的码。
  // 无段→全量;有段且不显示全部→仅本应用;显示全部→本应用在前 + 其他(扁平,分组头点缀省略)。
  const permissionOptions = useMemo(() => {
    const toOpt = (r: PermissionRouteItem) => ({ value: r.code, label: `${r.method} ${r.path}` })
    if (!seg) return routes.map(toOpt)
    const inApp = routes.filter((r) => belongsToApp(r.path, seg))
    if (!showAllRoutes) return inApp.map(toOpt)
    return [...inApp, ...routes.filter((r) => !belongsToApp(r.path, seg))].map(toOpt)
  }, [routes, seg, showAllRoutes])

  // 「所属页面」下拉:全部非按钮节点(目录 + 页面),缩进体现层级。不排除目录(如 ping 挂目录)。
  // 按钮是叶子不可能成环,无需像主页 parentOptions 排除自身子树。
  const parentOptions = useMemo(() => {
    const opts: { label: string; value: number }[] = []
    const walk = (nodes: MenuTreeNode[], depth: number) => {
      for (const n of nodes) {
        if (n.type === MenuType.Button) continue
        opts.push({ label: `${'　'.repeat(depth)}${n.title}`, value: n.id })
        walk(n.children, depth + 1)
      }
    }
    walk(tree, 0)
    return opts
  }, [tree])

  // ── 单个新增/编辑 ──
  const [form] = Form.useForm<MenuInput>()
  const [editOpen, setEditOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)

  const openAdd = () => {
    setEditingId(null)
    setShowAllRoutes(false) // 新增默认只看本应用路由
    form.setFieldsValue(blankMenu(menu?.id ?? 0, null, MenuType.Button))
    setEditOpen(true)
  }
  const openEdit = (r: MenuTreeNode) => {
    setEditingId(r.id)
    setShowAllRoutes(true) // 编辑时展开全部,保证已选的(可能跨应用的)权限码在下拉里可见
    form.setFieldsValue(menuRowToInput(r))
    setEditOpen(true)
  }
  const save = async () => {
    await form.validateFields() // 校验(title 必填)失败抛 → FormContainer 不关
    // 用 getFieldsValue(true) 取全量:表单只渲染 5 个字段,但 openAdd/openEdit 经 setFieldsValue 注入了
    // path/component/icon/visible(未渲染)。只回收已注册字段会漏带这四项 → 后端按默认落库抹空(违反全量 update 契约)。
    const v = form.getFieldsValue(true)
    try {
      // parentId 取表单值(而非当前弹窗 menuId)——「所属页面」改了就是移动:保存后本列表少一条、目标页面徽标 +1。
      await (editingId === null
        ? menuApi.add({ ...v, type: MenuType.Button, moduleId: null })
        : menuApi.update(editingId, { ...v, type: MenuType.Button, moduleId: null }))
      message.success(t('menu.saved'))
      onChanged()
    } catch (e) {
      message.error(translateError(e))
      return false // 留在弹层
    }
  }

  // ── 从路由批量添加 ──
  type BatchRow = { code: string; method: string; path: string; title: string; checked: boolean }
  const [batchOpen, setBatchOpen] = useState(false)
  const [batchRows, setBatchRows] = useState<BatchRow[]>([])
  const [batchKeyword, setBatchKeyword] = useState('')

  const btnTitle = (method: string) => {
    const key = defaultTitleKey(method)
    return key ? t(key) : method
  }
  const openBatch = () => {
    setBatchKeyword('')
    setShowAllRoutes(false) // 批量默认只列本应用路由
    // 只列当前菜单还没建过的路由,避免重复。
    setBatchRows(
      routes.filter((r) => !usedCodes.has(r.code)).map((r) => ({ code: r.code, method: r.method, path: r.path, title: btnTitle(r.method), checked: false })),
    )
    setBatchOpen(true)
  }
  const patchRow = (code: string, patch: Partial<BatchRow>) =>
    setBatchRows((rows) => rows.map((r) => (r.code === code ? { ...r, ...patch } : r)))

  // 搜索/软过滤只影响可见行;saveBatch 仍以全量 batchRows 的 checked 为准,不漏被隐藏的已勾选行。
  const filteredBatchRows = useMemo(() => {
    const kw = batchKeyword.trim().toLowerCase()
    return batchRows.filter((r) => {
      if (seg && !showAllRoutes && !belongsToApp(r.path, seg)) return false
      if (!kw) return true
      return r.method.toLowerCase().includes(kw) || r.path.toLowerCase().includes(kw) || r.title.toLowerCase().includes(kw)
    })
  }, [batchRows, batchKeyword, seg, showAllRoutes])

  const saveBatch = async () => {
    const picked = batchRows.filter((r) => r.checked)
    if (!picked.length) return false // 未选任何路由,别关弹窗
    const saved: string[] = []
    try {
      // ponytail: 逐个 add,量级大到卡顿再考虑后端批量端点
      for (const r of picked) {
        await menuApi.add({
          parentId: menu?.id ?? 0, type: MenuType.Button, title: r.title.trim() || r.method,
          permission: r.code, sort: 0, enabled: true, moduleId: null, path: '', component: '', icon: '', visible: true,
        })
        saved.push(r.code)
      }
      message.success(t('menu.saved'))
      onChanged()
    } catch (e) {
      // 中途失败:把已建的从待建列表剔除并刷新父树,免用户重试时把已成功的行重复创建。
      if (saved.length) { setBatchRows((rows) => rows.filter((r) => !saved.includes(r.code))); onChanged() }
      message.error(translateError(e))
      return false
    }
  }

  const columns: TableColumnsType<MenuTreeNode> = [
    { title: t('menu.title'), dataIndex: 'title', ellipsis: true },
    { title: t('menu.permission'), dataIndex: 'permission', render: (_, r) => r.permission || '—' },
    { title: t('menu.sort'), dataIndex: 'sort', width: 70 },
    {
      title: t('common.status'), dataIndex: 'enabled', width: 84,
      render: (_, r) => (
        <StatusSwitch
          value={r.enabled}
          disabled={!canEdit}
          request={(next) => menuApi.update(r.id, { ...menuRowToInput(r), enabled: next })}
          onChange={() => onChanged()}
        />
      ),
    },
    {
      title: t('common.operation'), key: 'op', width: 130,
      render: (_, r) => (
        <Space size={2}>
          {canEdit && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
          {canDelete && (
            <Button type="link" size="small" danger onClick={() =>
              confirm({ content: t('menu.deleteConfirm', { title: r.title }), action: () => menuApi.remove(r.id), successMsg: t('menu.deleted') }).then((ok) => { if (ok) onChanged() })
            }>{t('common.delete')}</Button>
          )}
        </Space>
      ),
    },
  ]

  return (
    <>
      {/* 列表弹窗:只有「关闭」底栏(不走 onConfirm 提交协议),故用原生 Modal 自定义 footer。 */}
      <Modal
        open={menu != null}
        title={t('menu.buttonManagerTitle', { title: menu?.title ?? '' })}
        width={Math.min(960, typeof window !== 'undefined' ? Math.round(window.innerWidth * 0.9) : 960)}
        onCancel={onClose}
        footer={[<Button key="close" onClick={onClose}>{t('common.close')}</Button>]}
        destroyOnHidden
      >
        <Space vertical size={12} style={{ width: '100%' }}>
          <Space>
            {canAdd && <Button type="primary" size="small" onClick={openAdd}>{t('menu.addButton')}</Button>}
            {canAdd && <Button size="small" onClick={openBatch}>{t('menu.batchFromRoutes')}</Button>}
          </Space>
          <Table<MenuTreeNode> rowKey="id" columns={columns} dataSource={buttons} pagination={false} size="small" />
        </Space>
      </Modal>

      {/* 单个新增/编辑按钮 */}
      <FormContainer
        open={editOpen}
        onOpenChange={setEditOpen}
        title={editingId === null ? t('menu.addButton') : t('common.edit')}
        width={600}
        confirmText={t('common.save')}
        onConfirm={save}
      >
        <Form form={form} labelCol={{ span: 5 }} wrapperCol={{ span: 19 }} style={{ marginTop: 12 }}>
          <Form.Item name="title" label={t('menu.title')} rules={[{ required: true, whitespace: true, message: t('menu.titleRequired') }]}>
            <Input placeholder={t('menu.title')} />
          </Form.Item>
          {/* 改这里 = 把按钮移到别的页面(按钮不在主树里,这是唯一能改归属的入口)。 */}
          <Form.Item name="parentId" label={t('menu.buttonParent')}>
            <Select options={parentOptions} showSearch={{ optionFilterProp: 'label' }} />
          </Form.Item>
          <Form.Item name="permission" label={t('menu.permission')}>
            <AutoComplete
              options={permissionOptions}
              allowClear
              placeholder={t('menu.permissionPlaceholder')}
              showSearch={{ filterOption: (input, option) => `${option?.value ?? ''} ${option?.label ?? ''}`.toLowerCase().includes(input.toLowerCase()) }}
            />
          </Form.Item>
          {hasOtherRoutes && (
            <Form.Item label=" " colon={false}>
              <Checkbox checked={showAllRoutes} onChange={(e) => setShowAllRoutes(e.target.checked)}>{t('menu.showAllRoutes')}</Checkbox>
            </Form.Item>
          )}
          <Form.Item name="sort" label={t('menu.sort')}>
            <InputNumber min={0} style={{ width: 160 }} />
          </Form.Item>
          <Form.Item name="enabled" label={t('common.status')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </FormContainer>

      {/* 从路由批量添加 */}
      <FormContainer
        open={batchOpen}
        onOpenChange={setBatchOpen}
        title={t('menu.batchTitle')}
        width={640}
        confirmText={t('common.save')}
        onConfirm={saveBatch}
      >
        {!batchRows.length ? (
          <Empty description={t('menu.noRoutesLeft')} style={{ padding: '24px 0' }} />
        ) : (
          <Space vertical size={10} style={{ width: '100%' }}>
            <Space align="center" size={12} style={{ width: '100%' }}>
              <Input
                value={batchKeyword}
                onChange={(e) => setBatchKeyword(e.target.value)}
                allowClear
                placeholder={t('menu.batchSearchPlaceholder')}
                prefix={<AppIcon icon="ph:magnifying-glass" size={16} />}
                style={{ flex: 1 }}
              />
              {hasOtherRoutes && <Checkbox checked={showAllRoutes} onChange={(e) => setShowAllRoutes(e.target.checked)}>{t('menu.showAllRoutes')}</Checkbox>}
            </Space>
            {!filteredBatchRows.length ? (
              <Empty description={t('menu.noRoutesLeft')} style={{ padding: '16px 0' }} />
            ) : (
              <div style={{ maxHeight: '55vh', overflow: 'auto', display: 'flex', flexDirection: 'column', gap: 6 }}>
                {filteredBatchRows.map((r) => (
                  <div key={r.code} style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                    <Checkbox checked={r.checked} onChange={(e) => patchRow(r.code, { checked: e.target.checked })} />
                    <span style={{ width: 210, color: 'var(--color-text-secondary, #888)', fontSize: 12 }}>{r.method} {r.path}</span>
                    <Input value={r.title} onChange={(e) => patchRow(r.code, { title: e.target.value })} size="small" disabled={!r.checked} style={{ width: 160 }} />
                  </div>
                ))}
              </div>
            )}
          </Space>
        )}
      </FormContainer>
    </>
  )
}
