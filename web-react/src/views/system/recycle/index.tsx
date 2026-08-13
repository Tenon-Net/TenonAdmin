// 回收站页:9 类实体各一页签(含 job——后端 /sys/recycle/job 已支持,恢复强制 Paused),每页签一张只读列表 + 行内「恢复 / 彻底删除」。
// antd Tabs(items API)只做切页签;单个 DataTable 用 `key={activeTab}` 随切页重挂(拉当前类型的数据)。
// 恢复/彻底删走 useConfirm 二次确认,按 activeTab + 行 id 定位(选错类型会还原错实体,是会静默出错的点)。
import { useCallback, useMemo, useRef, useState } from 'react'
import { Button, Space, Tabs } from 'antd'
import { useTranslation } from 'react-i18next'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import type { ProColumns } from '@ant-design/pro-components'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { recycleApi, type RecycleBinItem } from '@/api'

const RECYCLE_TYPES = ['user', 'role', 'org', 'position', 'module', 'config', 'dict', 'menu', 'job'] as const
type RecycleType = (typeof RECYCLE_TYPES)[number]

export default function RecyclePage() {
  const { t } = useTranslation()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  const [activeTab, setActiveTab] = useState<RecycleType>('user')
  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  // fetcher 按当前页签类型拉数(recycleApi.page 是「按 type 柯里化」出的分页函数)。
  const fetchRecycle: PageFetcher<RecycleBinItem> = (q) => recycleApi.page(activeTab)({ page: q.page, pageSize: q.pageSize })

  const handleRestore = useCallback(
    (r: RecycleBinItem) => {
      confirm({ content: t('recycle.restoreConfirm', { name: r.name }), action: () => recycleApi.restore(activeTab, r.id), successMsg: t('recycle.restored') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, activeTab, reload],
  )
  const handlePurge = useCallback(
    (r: RecycleBinItem) => {
      confirm({ content: t('recycle.purgeConfirm', { name: r.name }), action: () => recycleApi.purge(activeTab, r.id), successMsg: t('recycle.purged') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, activeTab, reload],
  )

  const columns = useMemo<ProColumns<RecycleBinItem>[]>(
    () => [
      { title: t('recycle.name'), dataIndex: 'name', search: false },
      { title: t('recycle.code'), dataIndex: 'code', search: false, render: (_, r) => r.code || '—' },
      { title: t('recycle.deletedAt'), dataIndex: 'deletedAt', search: false, width: 180 },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 180, fixed: 'right',
        render: (_, r) => (
          <Space size={4}>
            {has('POST:/api/v1/sys/recycle/{type}/{id}/restore') && <Button type="link" size="small" onClick={() => handleRestore(r)}>{t('recycle.restore')}</Button>}
            {has('DELETE:/api/v1/sys/recycle/{type}/{id}') && <Button type="link" size="small" danger onClick={() => handlePurge(r)}>{t('recycle.purge')}</Button>}
          </Space>
        ),
      },
    ],
    [t, has, handleRestore, handlePurge],
  )

  return (
    <>
      <Tabs
        activeKey={activeTab}
        onChange={(k) => setActiveTab(k as RecycleType)}
        items={RECYCLE_TYPES.map((type) => ({ key: type, label: t(`recycle.tabs.${type}`) }))}
      />
      <DataTable<RecycleBinItem>
        key={activeTab}
        ref={tableRef}
        columns={columns}
        fetcher={fetchRecycle}
        persistKey={`sys-recycle-${activeTab}`}
      />
    </>
  )
}
