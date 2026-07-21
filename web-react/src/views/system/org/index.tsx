// 机构管理 = 树表:orgApi.list 平铺 → buildTree 拼 children,静态数据 + 受控展开(无分页)。
// 上级机构用 OrgTreeSelect(剪自身子树防成环);无独立启停端点,StatusSwitch 走全量 update。
// filterTree 是浅拷贝,故 StatusSwitch 成功后**重拉整树**而非写行(写浅拷贝的祖先行不会回源树,开关会弹回)。
// 照 B11 范式:纯逻辑抽 orgForm.ts(变异钉),本页只接线。
import { useCallback, useEffect, useMemo, useState } from 'react'
import { App, Button, Dropdown, Form, Input, InputNumber, Modal, Space, Switch, type MenuProps } from 'antd'
import { useTranslation } from 'react-i18next'
import { ProColumns } from '@ant-design/pro-components'
import { TreeTable } from '@/components/TreeTable'
import { Can } from '@/components/Can'
import { DictSelect } from '@/components/DictSelect'
import { DictTag } from '@/components/DictTag'
import { FormContainer } from '@/components/FormContainer'
import { OrgTreeSelect } from '@/components/OrgTreeSelect'
import { StatusSwitch } from '@/components/StatusSwitch'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { orgApi } from '@/api'
import { translateError } from '@/utils/error'
import { buildTree, expandableIds, filterTree, type Tree } from '@/utils/tree'
import type { OrgInput, SysOrg } from '@/types/api'
import { blankForm, rowToInput } from './orgForm'

export default function OrgPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm, run } = useConfirm()
  const has = useHasPerm()

  const [tree, setTree] = useState<Tree<SysOrg>[]>([])
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [expandedKeys, setExpandedKeys] = useState<number[]>([])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setTree(buildTree(await orgApi.list()))
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }, [message])
  useEffect(() => { void load() }, [load])

  // 静态树自己算过滤(ProTable dataSource 模式不做前端筛选);关键字放工具栏而非列 search:树表无分页,搜索卡片白占高度。
  const filteredTree = useMemo(() => {
    const kw = keyword.trim().toLowerCase()
    if (!kw) return tree
    return filterTree(tree, (n) => n.name.toLowerCase().includes(kw) || n.code.toLowerCase().includes(kw))
  }, [tree, keyword])

  // 展开受控:默认全展开须自己播种,data 变(搜索/重拉)后重算——否则命中藏在折叠祖先里看不见。
  // 手动展开/折叠只改 expandedKeys、不动 filteredTree,故不会被这条重置。
  useEffect(() => { setExpandedKeys(expandableIds(filteredTree)) }, [filteredTree])
  const allExpanded = expandedKeys.length > 0
  const toggleExpandAll = () => setExpandedKeys(allExpanded ? [] : expandableIds(filteredTree))

  // ── 新增/编辑弹窗(FormContainer owns loading+close)──
  const [form] = Form.useForm<OrgInput>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)

  const openAdd = useCallback((parentId = 0) => {
    setEditingId(null)
    form.setFieldsValue(blankForm(parentId))
    setOpen(true)
  }, [form])
  const openEdit = useCallback((r: SysOrg) => {
    setEditingId(r.id)
    form.setFieldsValue(rowToInput(r))
    setOpen(true)
  }, [form])

  const save = async () => {
    const v = await form.validateFields() // 校验失败抛 → FormContainer 不关
    try {
      // OrgTreeSelect 可清空 → parentId 可能 undefined/null,归一为 0=根。
      const payload: OrgInput = { ...v, parentId: v.parentId ?? 0 }
      if (editingId === null) await orgApi.add(payload)
      else await orgApi.update(editingId, payload)
      message.success(t('org.saved'))
      await load()
    } catch (e) {
      message.error(translateError(e))
      return false // 留在弹层
    }
  }

  // ── 复制弹窗 ──
  const [copyOpen, setCopyOpen] = useState(false)
  const [copyId, setCopyId] = useState(0)
  const [copySourceName, setCopySourceName] = useState('')
  const [copyName, setCopyName] = useState('')
  const [copyLoading, setCopyLoading] = useState(false)

  const openCopy = useCallback((r: SysOrg) => {
    setCopyId(r.id)
    setCopySourceName(r.name)
    setCopyName(`${r.name}-副本`)
    setCopyOpen(true)
  }, [])
  const confirmCopy = async () => {
    setCopyLoading(true)
    const ok = await run(() => orgApi.copy(copyId, { name: copyName }), t('org.copied'))
    setCopyLoading(false)
    if (ok) { setCopyOpen(false); void load() }
  }

  const handleDelete = useCallback((r: SysOrg) => {
    confirm({ content: t('org.deleteConfirm', { name: r.name }), action: () => orgApi.remove(r.id), successMsg: t('org.deleted') }).then(
      (ok) => { if (ok) void load() },
    )
  }, [confirm, t, load])

  const columns = useMemo<ProColumns<Tree<SysOrg>>[]>(() => [
    { title: t('org.name'), dataIndex: 'name', ellipsis: true }, // 树列须排首位(承载展开箭头)
    { title: t('org.code'), dataIndex: 'code', ellipsis: true },
    { title: t('org.category'), dataIndex: 'category', width: 110, render: (_, r) => <DictTag typeCode="org_category" value={r.category} /> },
    { title: t('org.sort'), dataIndex: 'sort', width: 80 },
    {
      title: t('common.status'), dataIndex: 'enabled', width: 90,
      render: (_, r) => (
        <StatusSwitch
          value={r.enabled}
          disabled={!has('PUT:/api/v1/sys/org/{id}')}
          request={(next) => orgApi.update(r.id, { ...rowToInput(r), enabled: next })}
          onChange={() => void load()} // 重拉而非写行:filterTree 浅拷贝写不回源树
        />
      ),
    },
    {
      // 操作收敛成「编辑 + 更多▾」:4 个操作平铺会换行、fixed 才够得着;各项按各自权限码显隐,全无则不出下拉。
      title: t('common.operation'), key: 'op', width: 150, fixed: 'right',
      render: (_, r) => {
        const moreItems = ([
          has('POST:/api/v1/sys/org/add') ? { key: 'addChild', label: t('org.addChild') } : null,
          has('POST:/api/v1/sys/org/{id}/copy') ? { key: 'copy', label: t('org.copy') } : null,
          has('DELETE:/api/v1/sys/org/{id}') ? { key: 'delete', label: t('common.delete'), danger: true } : null,
        ] as MenuProps['items'])!.filter(Boolean)
        const onMore: MenuProps['onClick'] = ({ key }) => {
          if (key === 'addChild') openAdd(r.id)
          else if (key === 'copy') openCopy(r)
          else handleDelete(r)
        }
        return (
          <Space size={4}>
            {has('PUT:/api/v1/sys/org/{id}') && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
            {moreItems!.length > 0 && (
              <Dropdown menu={{ items: moreItems, onClick: onMore }} trigger={['click']}>
                <Button type="link" size="small">{t('common.more')}</Button>
              </Dropdown>
            )}
          </Space>
        )
      },
    },
  ], [t, has, load, openAdd, openEdit, openCopy, handleDelete])

  return (
    <>
      <TreeTable<Tree<SysOrg>>
        columns={columns}
        data={filteredTree}
        loading={loading}
        expandedRowKeys={expandedKeys}
        onExpandedRowKeysChange={(keys) => setExpandedKeys(keys as number[])}
        persistKey="sys-org"
        headerTitle={t('org.title')}
        toolbar={
          <Space>
            <Input
              value={keyword}
              onChange={(e) => setKeyword(e.target.value)}
              allowClear
              placeholder={t('org.searchPlaceholder')}
              style={{ width: 220 }}
            />
            <Button onClick={() => void load()} loading={loading}>{t('common.refresh')}</Button>
            <Button onClick={toggleExpandAll}>{allExpanded ? t('common.collapseAll') : t('common.expandAll')}</Button>
            <Can code="POST:/api/v1/sys/org/add">
              <Button type="primary" onClick={() => openAdd(0)}>{t('common.add')}</Button>
            </Can>
          </Space>
        }
      />

      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={editingId === null ? t('org.addTitle') : t('org.editTitle')}
        width={480}
        confirmText={t('common.save')}
        onConfirm={save}
      >
        <Form form={form} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }} style={{ marginTop: 12 }}>
          <Form.Item name="parentId" label={t('org.parent')}>
            {/* 编辑时剪掉自身子树,防把机构挂到自己后代下成环 */}
            <OrgTreeSelect excludeSubtreeOf={editingId} allowClear placeholder={t('org.parentPlaceholder')} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="name" label={t('org.name')} rules={[{ required: true, whitespace: true, message: t('org.nameRequired') }]}>
            <Input placeholder={t('org.name')} />
          </Form.Item>
          <Form.Item name="code" label={t('org.code')}>
            {/* 机构编码建后不可改(后端也拒);编辑时置灰 */}
            <Input disabled={editingId !== null} placeholder={t('org.codePlaceholder')} />
          </Form.Item>
          <Form.Item name="category" label={t('org.category')}>
            <DictSelect typeCode="org_category" allowClear placeholder={t('org.categoryPlaceholder')} />
          </Form.Item>
          <Form.Item name="sort" label={t('org.sort')}>
            <InputNumber min={0} style={{ width: 160 }} />
          </Form.Item>
          <Form.Item name="enabled" label={t('common.status')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </FormContainer>

      <Modal
        open={copyOpen}
        onCancel={() => setCopyOpen(false)}
        onOk={confirmCopy}
        confirmLoading={copyLoading}
        title={t('org.copyTitle')}
        okText={t('common.confirm')}
        cancelText={t('common.cancel')}
      >
        <p style={{ margin: '0 0 12px' }}>{t('org.copyConfirm', { name: copySourceName })}</p>
        <Input value={copyName} onChange={(e) => setCopyName(e.target.value)} placeholder={t('org.copyNameLabel')} />
      </Modal>
    </>
  )
}
