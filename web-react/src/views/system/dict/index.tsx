// 字典管理 = 主从:左=类型 DataTable(CRUD),点击行选中 → 右=该类型的字典项(裸 antd Table + CRUD)。
// 关键约束:右侧走管理端 items(含停用项、带 id);下拉用的 dict store 只回启用项且丢 id,不能复用。
// 任何类型/项增删改后调 invalidate(code) 失效下拉缓存,变更即时生效。纯逻辑抽 dictForm.ts(变异钉),本页接线。
import { useCallback, useMemo, useRef, useState, type Key } from 'react'
import { App, Button, Card, Empty, Form, Input, InputNumber, Space, Switch, Table, type TableColumnsType } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { StatusSwitch } from '@/components/StatusSwitch'
import { useConfirm } from '@/hooks/useConfirm'
import { useBatchDelete } from '@/hooks/useBatchDelete'
import { useHasPerm } from '@/stores/auth'
import { useDictStore } from '@/stores/dict'
import { dictAdminApi } from '@/api'
import { translateError } from '@/utils/error'
import type { DictItemInput, DictTypeInput, SysDictItem, SysDictType } from '@/types/api'
import { blankItem, blankType, itemToInput, typeToInput } from './dictForm'

export default function DictPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()
  const invalidate = useDictStore((s) => s.invalidate)

  const typeTableRef = useRef<DataTableHandle>(null)
  const reloadTypes = useCallback(() => typeTableRef.current?.reload(), [])

  // ── 主从选中态 + 右侧字典项 ──
  const [selectedType, setSelectedType] = useState<SysDictType | null>(null)
  const [items, setItems] = useState<SysDictItem[]>([])
  const [itemsLoading, setItemsLoading] = useState(false)
  // 竞态守卫:await 期间用户可能已切类型,过期响应不得覆盖当前选中项(请求序号只认最后一次)。
  const reqIdRef = useRef(0)

  const loadItems = useCallback(async (code: string | undefined) => {
    if (!code) { setItems([]); return }
    const my = ++reqIdRef.current
    setItemsLoading(true)
    try {
      const list = await dictAdminApi.items(code)
      if (reqIdRef.current === my) setItems(list)
    } catch (e) {
      if (reqIdRef.current === my) { message.error(translateError(e)); setItems([]) }
    } finally {
      if (reqIdRef.current === my) setItemsLoading(false)
    }
  }, [message])

  const selectType = useCallback((r: SysDictType) => {
    setSelectedType(r)
    void loadItems(r.code)
  }, [loadItems])

  // 批量删除:类型批删后清空选中 + 全量失效缓存 + 重载左栏;项批删后仅重载右栏 + 失效缓存。
  const typeBatch = useBatchDelete({
    remove: dictAdminApi.typeBatchRemove,
    refresh: () => { setSelectedType(null); setItems([]); invalidate(); reloadTypes() },
    successMsg: t('dict.typeDeleted'),
  })
  const itemBatch = useBatchDelete({
    remove: dictAdminApi.itemBatchRemove,
    refresh: () => { invalidate(selectedType?.code); void loadItems(selectedType?.code) },
    successMsg: t('dict.itemDeleted'),
  })

  // ── 类型 新增/编辑弹窗 ──
  const [typeForm] = Form.useForm<DictTypeInput>()
  const [typeOpen, setTypeOpen] = useState(false)
  const [typeEditingId, setTypeEditingId] = useState<number | null>(null)

  const openTypeAdd = useCallback(() => { setTypeEditingId(null); typeForm.setFieldsValue(blankType()); setTypeOpen(true) }, [typeForm])
  const openTypeEdit = useCallback((r: SysDictType) => { setTypeEditingId(r.id); typeForm.setFieldsValue(typeToInput(r)); setTypeOpen(true) }, [typeForm])
  const saveType = async () => {
    const v = await typeForm.validateFields()
    try {
      if (typeEditingId === null) await dictAdminApi.typeAdd(v)
      else await dictAdminApi.typeUpdate(typeEditingId, v)
      invalidate(v.code)
      message.success(t('dict.typeSaved'))
      reloadTypes()
      // 编辑的是当前选中类型 → 同步名称到右栏标题(code 不可改,无需同步)。
      if (selectedType?.id === typeEditingId) setSelectedType((prev) => (prev ? { ...prev, name: v.name } : prev))
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  const deleteType = useCallback((r: SysDictType) => {
    confirm({ content: t('dict.typeDeleteConfirm', { name: r.name }), action: () => dictAdminApi.typeRemove(r.id), successMsg: t('dict.typeDeleted') }).then((ok) => {
      if (!ok) return
      invalidate(r.code)
      if (selectedType?.id === r.id) { setSelectedType(null); setItems([]) }
      reloadTypes()
    })
  }, [confirm, t, invalidate, selectedType, reloadTypes])

  // ── 字典项 新增/编辑弹窗 ──
  const [itemForm] = Form.useForm<DictItemInput>()
  const [itemOpen, setItemOpen] = useState(false)
  const [itemEditingId, setItemEditingId] = useState<number | null>(null)

  const openItemAdd = useCallback(() => {
    if (!selectedType) return
    setItemEditingId(null); itemForm.setFieldsValue(blankItem(selectedType.code)); setItemOpen(true)
  }, [selectedType, itemForm])
  const openItemEdit = useCallback((r: SysDictItem) => { setItemEditingId(r.id); itemForm.setFieldsValue(itemToInput(r)); setItemOpen(true) }, [itemForm])
  const saveItem = async () => {
    await itemForm.validateFields() // 校验(label/value 必填)
    // getFieldsValue(true) 取全量:dictTypeCode 是隐藏 FK,由 openItemAdd/Edit 经 setFieldsValue 注入但表单不渲染。
    // validateFields() 只回收已注册字段会漏掉它 → add 建孤儿项(DictTypeCode="")、update 把项从类型摘除(与 C8b LOW-1 同类)。
    const v = itemForm.getFieldsValue(true)
    try {
      if (itemEditingId === null) await dictAdminApi.itemAdd(v)
      else await dictAdminApi.itemUpdate(itemEditingId, v)
      invalidate(v.dictTypeCode)
      message.success(t('dict.itemSaved'))
      await loadItems(v.dictTypeCode)
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  const deleteItem = useCallback((r: SysDictItem) => {
    confirm({ content: t('dict.itemDeleteConfirm', { label: r.label }), action: () => dictAdminApi.itemRemove(r.id), successMsg: t('dict.itemDeleted') }).then((ok) => {
      if (!ok) return
      invalidate(r.dictTypeCode)
      void loadItems(r.dictTypeCode)
    })
  }, [confirm, t, invalidate, loadItems])

  // ── 左:类型列(DataTable / ProColumns)。行内控件须 stopPropagation 防冒泡到行点击(否则误切选中类型)。──
  const typeColumns = useMemo<ProColumns<SysDictType>[]>(() => [
    { title: t('dict.code'), dataIndex: 'code', search: false, ellipsis: true },
    { title: t('dict.name'), dataIndex: 'name' }, // 唯一可搜列
    { title: t('dict.sort'), dataIndex: 'sort', search: false, width: 70 },
    {
      title: t('common.status'), dataIndex: 'enabled', search: false, width: 90,
      render: (_, r) => (
        <div onClick={(e) => e.stopPropagation()}>
          <StatusSwitch
            value={r.enabled}
            disabled={!has('PUT:/api/v1/sys/dict/type/{id}')}
            request={(next) => dictAdminApi.typeUpdate(r.id, { ...typeToInput(r), enabled: next })}
            onChange={() => { invalidate(r.code); reloadTypes() }} // ProTable 缓存不自更新 → 重载左栏
          />
        </div>
      ),
    },
    {
      title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 130,
      render: (_, r) => (
        <div onClick={(e) => e.stopPropagation()}>
          <Space size={4}>
            {has('PUT:/api/v1/sys/dict/type/{id}') && <Button type="link" size="small" onClick={() => openTypeEdit(r)}>{t('common.edit')}</Button>}
            {has('DELETE:/api/v1/sys/dict/type/{id}') && <Button type="link" size="small" danger onClick={() => deleteType(r)}>{t('common.delete')}</Button>}
          </Space>
        </div>
      ),
    },
  ], [t, has, invalidate, reloadTypes, openTypeEdit, deleteType])

  // ── 右:字典项列(裸 antd Table,静态 data,无行点击故无需 stopPropagation)──
  const itemColumns = useMemo<TableColumnsType<SysDictItem>>(() => [
    { title: t('dict.itemLabel'), dataIndex: 'label', ellipsis: true },
    { title: t('dict.itemValue'), dataIndex: 'value', ellipsis: true },
    { title: t('dict.sort'), dataIndex: 'sort', width: 70 },
    {
      title: t('common.status'), dataIndex: 'enabled', width: 90,
      render: (_, r) => (
        <StatusSwitch
          value={r.enabled}
          disabled={!has('PUT:/api/v1/sys/dict/item/{id}')}
          request={(next) => dictAdminApi.itemUpdate(r.id, { ...itemToInput(r), enabled: next })}
          onChange={(next) => { setItems((prev) => prev.map((x) => (x.id === r.id ? { ...x, enabled: next } : x))); invalidate(r.dictTypeCode) }}
        />
      ),
    },
    {
      title: t('common.operation'), key: 'op', width: 130,
      render: (_, r) => (
        <Space size={4}>
          {has('PUT:/api/v1/sys/dict/item/{id}') && <Button type="link" size="small" onClick={() => openItemEdit(r)}>{t('common.edit')}</Button>}
          {has('DELETE:/api/v1/sys/dict/item/{id}') && <Button type="link" size="small" danger onClick={() => deleteItem(r)}>{t('common.delete')}</Button>}
        </Space>
      ),
    },
  ], [t, has, invalidate, openItemEdit, deleteItem])

  const fetchTypes: PageFetcher<SysDictType> = (q) =>
    dictAdminApi.typePage({ page: q.page, pageSize: q.pageSize, name: typeof q.name === 'string' ? q.name : undefined })

  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'stretch' }}>
      <div style={{ flex: '1 1 380px', minWidth: 0 }}>
        <DataTable<SysDictType>
          ref={typeTableRef}
          columns={typeColumns}
          fetcher={fetchTypes}
          persistKey="sys-dict-type"
          activeRowKey={selectedType?.id ?? null}
          onRowClick={selectType}
          rowSelection={{ selectedRowKeys: typeBatch.selectedKeys, onChange: typeBatch.setSelectedKeys }}
          toolbar={
            <Space>
              <Can code="POST:/api/v1/sys/dict/type">
                <Button type="primary" onClick={openTypeAdd}>{t('common.add')}</Button>
              </Can>
              <Can code="POST:/api/v1/sys/dict/type/batch-delete">
                <Button danger disabled={!typeBatch.hasSelection} onClick={typeBatch.run}>{t('common.batchDelete')}</Button>
              </Can>
            </Space>
          }
        />
      </div>

      <div style={{ flex: '1 1 380px', minWidth: 0 }}>
        {selectedType ? (
          <Card
            title={t('dict.itemsOf', { name: selectedType.name })}
            styles={{ body: { paddingTop: 12 } }}
            extra={
              <Space size={8}>
                <Can code="POST:/api/v1/sys/dict/item">
                  <Button type="primary" size="small" onClick={openItemAdd}>{t('dict.addItem')}</Button>
                </Can>
                <Can code="POST:/api/v1/sys/dict/item/batch-delete">
                  <Button danger size="small" disabled={!itemBatch.hasSelection} onClick={itemBatch.run}>{t('common.batchDelete')}</Button>
                </Can>
              </Space>
            }
          >
            <Table<SysDictItem>
              rowKey="id"
              columns={itemColumns}
              dataSource={items}
              loading={itemsLoading}
              size="small"
              pagination={false}
              rowSelection={{ selectedRowKeys: itemBatch.selectedKeys, onChange: (keys: Key[]) => itemBatch.setSelectedKeys(keys) }}
            />
          </Card>
        ) : (
          <Card>
            <Empty description={t('dict.selectTypeHint')} style={{ padding: '48px 0' }} />
          </Card>
        )}
      </div>

      <FormContainer
        open={typeOpen}
        onOpenChange={setTypeOpen}
        title={typeEditingId === null ? t('dict.addTypeTitle') : t('dict.editTypeTitle')}
        width={480}
        confirmText={t('common.save')}
        onConfirm={saveType}
      >
        <Form form={typeForm} labelCol={{ span: 5 }} wrapperCol={{ span: 19 }} style={{ marginTop: 12 }}>
          <Form.Item name="code" label={t('dict.code')} rules={[{ required: true, whitespace: true, message: t('dict.codeRequired') }]}>
            {/* 类型编码建后不可改(后端更新时忽略);编辑时置灰 */}
            <Input disabled={typeEditingId !== null} placeholder={t('dict.code')} />
          </Form.Item>
          <Form.Item name="name" label={t('dict.name')} rules={[{ required: true, whitespace: true, message: t('dict.nameRequired') }]}>
            <Input placeholder={t('dict.name')} />
          </Form.Item>
          <Form.Item name="sort" label={t('dict.sort')}>
            <InputNumber min={0} style={{ width: 160 }} />
          </Form.Item>
          <Form.Item name="remark" label={t('dict.remark')}>
            <Input.TextArea autoSize={{ minRows: 2 }} />
          </Form.Item>
          <Form.Item name="enabled" label={t('common.status')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </FormContainer>

      <FormContainer
        open={itemOpen}
        onOpenChange={setItemOpen}
        title={itemEditingId === null ? t('dict.addItemTitle') : t('dict.editItemTitle')}
        width={480}
        confirmText={t('common.save')}
        onConfirm={saveItem}
      >
        <Form form={itemForm} labelCol={{ span: 5 }} wrapperCol={{ span: 19 }} style={{ marginTop: 12 }}>
          <Form.Item name="label" label={t('dict.itemLabel')} rules={[{ required: true, whitespace: true, message: t('dict.itemLabelRequired') }]}>
            <Input placeholder={t('dict.itemLabel')} />
          </Form.Item>
          <Form.Item name="value" label={t('dict.itemValue')} rules={[{ required: true, whitespace: true, message: t('dict.itemValueRequired') }]}>
            <Input placeholder={t('dict.itemValue')} />
          </Form.Item>
          <Form.Item name="sort" label={t('dict.sort')}>
            <InputNumber min={0} style={{ width: 160 }} />
          </Form.Item>
          <Form.Item name="enabled" label={t('common.status')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </FormContainer>
    </div>
  )
}
