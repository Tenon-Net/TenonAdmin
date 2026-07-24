// 菜单管理 = 树表:menuApi.tree() 已嵌套(含按钮),前端剥按钮 + 按所属应用过滤 + 客户端关键字筛 + 受控展开。
// 按钮不进主树,收进 ButtonManager 弹窗单管;无独立启停端点,StatusSwitch 走全量 update。
// filterTree 浅拷贝故变更后**重拉整树** + syncShell(重建当前应用侧边栏/动态路由)。纯逻辑抽 menuForm.ts(变异钉),本页接线。
import { useCallback, useEffect, useMemo, useState } from 'react'
import { App, Button, Dropdown, Form, Input, InputNumber, Select, Space, Switch, Tag, AutoComplete, type MenuProps } from 'antd'
import { useTranslation } from 'react-i18next'
import { ProColumns } from '@ant-design/pro-components'
import { TreeTable } from '@/components/TreeTable'
import { AppIcon } from '@/components/AppIcon'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { IconPicker } from '@/components/IconPicker'
import { StatusSwitch } from '@/components/StatusSwitch'
import { ButtonManager } from './ButtonManager'
import { useConfirm } from '@/hooks/useConfirm'
import { useAuthStore, useHasPerm } from '@/stores/auth'
import { enter } from '@/composables/useModule'
import { viewComponentPaths } from '@/router/buildRoutes'
import { menuApi, moduleApi } from '@/api'
import { translateError } from '@/utils/error'
import { expandableIds, filterTree } from '@/utils/tree'
import { MenuType, type MenuInput, type MenuTreeNode } from '@/types/menu'
import type { ModuleRow } from '@/types/api'
import type { PermissionRouteItem } from '@/types/api'
import {
  ALL_MODULES, UNASSIGNED, blankMenu, buildButtonInfo, menuRowToInput, stripButtons, subtreeIds,
} from './menuForm'

export default function MenuPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  const [tree, setTree] = useState<MenuTreeNode[]>([])
  const [modules, setModules] = useState<ModuleRow[]>([])
  const [routes, setRoutes] = useState<PermissionRouteItem[]>([])
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  // 默认跟随当前进入的应用;modules 加载后再校正(见下方 effect)。
  const [moduleFilter, setModuleFilter] = useState<number>(() => useAuthStore.getState().currentModuleId ?? UNASSIGNED)
  const [expandedKeys, setExpandedKeys] = useState<number[]>([])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setTree(await menuApi.tree())
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }, [message])

  /**
   * 菜单改完顺手重建当前应用的壳层(侧边栏 + 动态路由)——否则新建菜单要 F5 才出现,而这与
   * 「组件路径写错→进入 MissingRoute」都指向配置错误。React 侧 enter() 重拉门户树写回 store、
   * 路由反应式派生(≠ Vue 的命令式 buildRoutesForModule)。失败静默:菜单已存成功,下次 F5 自愈。
   */
  const syncShell = useCallback(() => {
    const id = useAuthStore.getState().currentModuleId
    if (id) void enter(id).catch((e: unknown) => message.error(translateError(e)))
  }, [message])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    // 模块列表仅供「所属应用」下拉;路由清单仅供「权限码」下拉——各自失败不阻塞菜单主流程。
    moduleApi.list().then((ms) => {
      setModules(ms)
      setModuleFilter((cur) => (ms.some((m) => m.id === cur) || cur === UNASSIGNED ? cur : ms[0]?.id ?? UNASSIGNED))
    }).catch((e: unknown) => message.error(translateError(e)))
    menuApi.routes().then(setRoutes).catch((e: unknown) => message.error(translateError(e)))
  }, [message])

  // 各节点直属按钮的 count(徽标)+ perms(关键字搜索,取自未剥离原树)。
  const buttonInfo = useMemo(() => buildButtonInfo(tree), [tree])

  // moduleId 只存在顶级目录(parentId==0),子节点为 null 靠继承,故只过滤顶级数组、子树自动跟随;
  // UNASSIGNED 归拢 moduleId==null 的顶级目录。再剥按钮交表格。
  const filteredTree = useMemo(
    () => stripButtons(tree.filter((r) =>
      moduleFilter === ALL_MODULES ? true
      : moduleFilter === UNASSIGNED ? r.moduleId == null
      : r.moduleId === moduleFilter,
    )),
    [tree, moduleFilter],
  )

  // 关键字搜索:目录/页面 permission 恒空,故按权限码搜要查该节点「按钮子节点」的码(见 buttonInfo)。
  const visibleTree = useMemo(() => {
    const kw = keyword.trim().toLowerCase()
    if (!kw) return filteredTree
    return filterTree(filteredTree, (n) =>
      n.title.toLowerCase().includes(kw) ||
      (n.path ?? '').toLowerCase().includes(kw) ||
      (buttonInfo.get(n.id)?.perms ?? '').includes(kw),
    )
  }, [filteredTree, keyword, buttonInfo])

  // 受控展开:默认全展开须自己播种,data 变(搜索/切应用/重拉)后重算;手动展开/折叠不动 visibleTree 故不被重置。
  useEffect(() => { setExpandedKeys(expandableIds(visibleTree)) }, [visibleTree])
  const allExpanded = expandedKeys.length > 0
  const toggleExpandAll = () => setExpandedKeys(allExpanded ? [] : expandableIds(visibleTree))

  const filterOptions = useMemo(
    () => [
      { label: t('menu.moduleAll'), value: ALL_MODULES },
      ...modules.map((m) => ({ label: m.title, value: m.id })),
      { label: t('menu.moduleUnassigned'), value: UNASSIGNED },
    ],
    [modules, t],
  )
  const currentAppPrefix = moduleFilter === UNASSIGNED ? undefined : modules.find((m) => m.id === moduleFilter)?.apiPrefix ?? undefined

  // ── 新增/编辑弹窗 ──
  const [form] = Form.useForm<MenuInput>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)
  const parentId = Form.useWatch('parentId', form)
  const typeVal = Form.useWatch('type', form)
  const isTopLevel = parentId === 0

  // 父级下拉:根 + 当前应用子树的目录/页面(按钮不能当父),缩进体现层级;编辑时排除自身子树防成环。
  const parentOptions = useMemo(() => {
    const exclude = editingId === null ? new Set<number>() : subtreeIds(tree, editingId)
    const opts: { label: string; value: number }[] = [{ label: t('menu.parentRoot'), value: 0 }]
    const walk = (nodes: MenuTreeNode[], depth: number) => {
      for (const n of nodes) {
        if (n.type !== MenuType.Button && !exclude.has(n.id)) opts.push({ label: `${'　'.repeat(depth)}${n.title}`, value: n.id })
        walk(n.children, depth + 1)
      }
    }
    walk(filteredTree, 0)
    return opts
  }, [editingId, tree, filteredTree, t])

  const openAdd = useCallback((pid = 0) => {
    setEditingId(null)
    // 新建顶级目录时自动盖上当前筛选的应用(「未分配」「全部」筛选下留空,交给用户手选)。
    const moduleId = pid === 0 && moduleFilter !== UNASSIGNED && moduleFilter !== ALL_MODULES ? moduleFilter : null
    form.setFieldsValue(blankMenu(pid, moduleId, MenuType.Menu))
    setOpen(true)
  }, [form, moduleFilter])
  const openEdit = useCallback((r: MenuTreeNode) => {
    setEditingId(r.id)
    form.setFieldsValue(menuRowToInput(r))
    setOpen(true)
  }, [form])

  const save = async () => {
    const v = await form.validateFields()
    try {
      const payload: MenuInput = { ...v, moduleId: isTopLevel ? v.moduleId ?? null : null }
      if (editingId === null) await menuApi.add(payload)
      else await menuApi.update(editingId, payload)
      message.success(t('menu.saved'))
      await load()
      syncShell()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  // ── 权限按钮弹窗(props 受控:非 null 即开)──
  const [buttonMgr, setButtonMgr] = useState<{ id: number; title: string } | null>(null)
  const onButtonsChanged = useCallback(() => { void load(); syncShell() }, [load, syncShell])

  const handleDelete = useCallback((r: MenuTreeNode) => {
    confirm({ content: t('menu.deleteConfirm', { title: r.title }), action: () => menuApi.remove(r.id), successMsg: t('menu.deleted') }).then(
      (ok) => { if (ok) { void load(); syncShell() } },
    )
  }, [confirm, t, load, syncShell])

  const columns = useMemo<ProColumns<MenuTreeNode>[]>(() => [
    {
      title: t('menu.title'), dataIndex: 'title', ellipsis: true, // 树列须首位(承载展开箭头)
      render: (_, r) => (
        <Space size={4}>
          {r.title}
          {/* 「隐藏」罕见,只在为真时才占视觉——省掉一整列同一个词 */}
          {!r.visible && <Tag variant="filled" color="warning">{t('common.hidden')}</Tag>}
        </Space>
      ),
    },
    { title: t('menu.icon'), dataIndex: 'icon', width: 64, align: 'center', render: (_, r) => (r.icon ? <AppIcon icon={r.icon} size={18} /> : null) },
    {
      title: t('menu.type'), dataIndex: 'type', width: 90,
      render: (_, r) => <Tag variant="filled" color={r.type === MenuType.Catalog ? 'blue' : 'green'}>{r.type === MenuType.Catalog ? t('menu.typeCatalog') : t('menu.typeMenu')}</Tag>,
    },
    { title: t('menu.path'), dataIndex: 'path', width: 160, ellipsis: true, render: (_, r) => r.path || '—' },
    { title: t('menu.component'), dataIndex: 'component', width: 180, ellipsis: true, render: (_, r) => r.component || '—' },
    {
      title: t('menu.buttons'), key: 'buttons', width: 140,
      render: (_, r) => {
        const n = buttonInfo.get(r.id)?.count ?? 0
        // 权限按钮跟着页面走:只有页面(或已挂按钮的目录)才显示入口;无菜单新增权限则不显示(只读用户不见写入口)。
        if (r.type !== MenuType.Menu && n === 0) return null
        if (!has('POST:/api/v1/sys/menu/add')) return null
        return (
          <Button type="link" size="small" icon={<AppIcon icon="ph:shield-check" size={15} />} onClick={() => setButtonMgr({ id: r.id, title: r.title })}>
            {t('menu.configPerms', { n })}
          </Button>
        )
      },
    },
    { title: t('menu.sort'), dataIndex: 'sort', width: 70 },
    {
      title: t('common.status'), dataIndex: 'enabled', width: 84,
      render: (_, r) => (
        <StatusSwitch
          value={r.enabled}
          disabled={!has('PUT:/api/v1/sys/menu/{id}')}
          // 停用是一键隐藏整棵子树的重操作,先确认;启用无副作用,跳过确认(返回 null)。
          confirm={(next) => (next ? null : t('menu.disableConfirm', { title: r.title }))}
          request={(next) => menuApi.update(r.id, { ...menuRowToInput(r), enabled: next })}
          onChange={() => { void load(); syncShell() }} // 重拉而非写行 + 即刻反映侧边栏
        />
      ),
    },
    {
      title: t('common.operation'), key: 'op', width: 150, fixed: 'right',
      render: (_, r) => {
        const moreItems = ([
          has('POST:/api/v1/sys/menu/add') ? { key: 'addChild', label: t('menu.addChild') } : null,
          has('DELETE:/api/v1/sys/menu/{id}') ? { key: 'delete', label: t('common.delete'), danger: true } : null,
        ] as MenuProps['items'])!.filter(Boolean)
        const onMore: MenuProps['onClick'] = ({ key }) => (key === 'addChild' ? openAdd(r.id) : handleDelete(r))
        return (
          <Space size={4}>
            {has('PUT:/api/v1/sys/menu/{id}') && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
            {moreItems!.length > 0 && (
              <Dropdown menu={{ items: moreItems, onClick: onMore }} trigger={['click']}>
                <Button type="link" size="small">{t('common.more')}</Button>
              </Dropdown>
            )}
          </Space>
        )
      },
    },
  ], [t, has, buttonInfo, openAdd, openEdit, handleDelete, load, syncShell])

  return (
    <>
      <TreeTable<MenuTreeNode>
        columns={columns}
        data={visibleTree}
        loading={loading}
        expandedRowKeys={expandedKeys}
        onExpandedRowKeysChange={(keys) => setExpandedKeys(keys as number[])}
        persistKey="sys-menu"
        toolbar={
          <Space>
            <Select value={moduleFilter} onChange={setModuleFilter} options={filterOptions} style={{ width: 200 }} placeholder={t('menu.module')} />
            <Input value={keyword} onChange={(e) => setKeyword(e.target.value)} allowClear placeholder={t('menu.searchPlaceholder')} style={{ width: 220 }} />
            <Button onClick={toggleExpandAll}>{allExpanded ? t('common.collapseAll') : t('common.expandAll')}</Button>
            <Can code="POST:/api/v1/sys/menu/add">
              <Button type="primary" onClick={() => openAdd(0)}>{t('common.add')}</Button>
            </Can>
          </Space>
        }
      />

      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={editingId === null ? t('menu.addTitle') : t('menu.editTitle')}
        width={720}
        confirmText={t('common.save')}
        onConfirm={save}
      >
        <Form form={form} labelCol={{ flex: '90px' }} wrapperCol={{ flex: 'auto' }} style={{ marginTop: 12 }}>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 18 }}>
            <Form.Item name="parentId" label={t('menu.parent')}>
              <Select options={parentOptions} showSearch={{ optionFilterProp: 'label' }} />
            </Form.Item>
            <Form.Item name="type" label={t('menu.type')}>
              <Select options={[{ label: t('menu.typeCatalog'), value: MenuType.Catalog }, { label: t('menu.typeMenu'), value: MenuType.Menu }]} />
            </Form.Item>
            <Form.Item name="title" label={t('menu.title')} rules={[{ required: true, whitespace: true, message: t('menu.titleRequired') }]}>
              <Input placeholder={t('menu.title')} />
            </Form.Item>
            <Form.Item name="sort" label={t('menu.sort')}>
              <InputNumber min={0} style={{ width: '100%' }} />
            </Form.Item>
            {/* 所属应用仅顶级目录有效——后端对子节点强制置空,前端据此隐藏字段 */}
            {isTopLevel && (
              <Form.Item name="moduleId" label={t('menu.module')} style={{ gridColumn: '1 / 3' }}>
                <Select options={modules.map((m) => ({ label: m.title, value: m.id }))} allowClear placeholder={t('menu.moduleHint')} />
              </Form.Item>
            )}
            <Form.Item name="path" label={t('menu.path')}>
              <Input placeholder="/system/xxx | https://..." />
            </Form.Item>
            {/* 组件路径:建议取自 import.meta.glob 真实文件表,但保留手填(页面文件尚未创建时先占位) */}
            <Form.Item name="component" label={t('menu.component')}>
              <AutoComplete
                options={viewComponentPaths.map((p) => ({ value: p }))}
                allowClear
                placeholder="system/xxx/index | https://..."
                showSearch={{ filterOption: (input, option) => (option?.value ?? '').toLowerCase().includes(input.toLowerCase()) }}
              />
            </Form.Item>
            {typeVal === MenuType.Menu && (
              <div style={{ gridColumn: '1 / 3', marginBottom: 12, fontSize: 12, color: 'var(--color-text-tertiary, #999)', lineHeight: 1.5 }}>
                {t('menu.linkHint')}
              </div>
            )}
            <Form.Item name="icon" label={t('menu.icon')} style={{ gridColumn: '1 / 3' }}>
              <IconPicker />
            </Form.Item>
            <Form.Item name="enabled" label={t('common.status')} valuePropName="checked">
              <Switch />
            </Form.Item>
            <Form.Item name="visible" label={t('menu.visible')} valuePropName="checked">
              <Switch />
            </Form.Item>
          </div>
        </Form>
      </FormContainer>

      <ButtonManager
        menu={buttonMgr}
        tree={tree}
        routes={routes}
        appPrefix={currentAppPrefix}
        onClose={() => setButtonMgr(null)}
        onChanged={onButtonsChanged}
      />
    </>
  )
}
