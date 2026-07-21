import { forwardRef, useEffect, useImperativeHandle, useMemo, useRef, useState, type CSSProperties } from 'react'
import { Button, Empty, Input, Tree, type TreeProps } from 'antd'
import { useTranslation } from 'react-i18next'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import type { ProColumns } from '@ant-design/pro-components'
import { FormContainer } from '@/components/FormContainer'
import { orgApi, userApi } from '@/api'
import { buildTree } from '@/utils/tree'
import type { SysOrg, UserItem } from '@/types/api'

/** 面板里只需要这三个字段;open(ids) 回显时找不到的用户也只造得出这三个(占位)。 */
export type PickedUser = Pick<UserItem, 'id' | 'name' | 'account'>

// ── 纯逻辑(变异钉死;组件只做接线)──────────────────────────────

/** 机构树选中 key → orgId 过滤值:根「全部」(key 0)或未选(null)→ undefined(不按部门过滤)。
 *  `|| undefined`:0 与 null 都 falsy → undefined;别写成 `?? undefined`(那会把 key 0「全部」当有效部门传下去)。 */
export function orgIdFromKey(key: number | null): number | undefined {
  return key || undefined
}

/** 追加用户,去重:已在 current 或在 excludeIds 里的跳过。返回新数组(不改原)。 */
export function addUsers(current: PickedUser[], toAdd: PickedUser[], excludeIds: Set<number>): PickedUser[] {
  const has = new Set(current.map((u) => u.id))
  const out = [...current]
  for (const u of toAdd) {
    if (!has.has(u.id) && !excludeIds.has(u.id)) {
      has.add(u.id)
      out.push(u)
    }
  }
  return out
}

/** 移除指定 id。 */
export function removeUser(current: PickedUser[], id: number): PickedUser[] {
  return current.filter((u) => u.id !== id)
}

/** 已选列表本地搜索:按姓名/账号大小写不敏感包含;空 query 返回原列表。 */
export function filterSelected(list: PickedUser[], query: string): PickedUser[] {
  const q = query.trim().toLowerCase()
  if (!q) return list
  return list.filter((u) => u.name.toLowerCase().includes(q) || u.account.toLowerCase().includes(q))
}

/** open(ids) 回显:按 ids 顺序,fetched 里找得到的用真数据,找不到的(翻页之外)退回只显 id 的占位行。 */
export function hydrateSelected(ids: number[], fetched: PickedUser[]): PickedUser[] {
  const map = new Map(fetched.map((u) => [u.id, u]))
  return ids.map((id) => map.get(id) ?? { id, account: String(id), name: String(id) })
}

// ── 组件 ───────────────────────────────────────────────────────

export interface UserPickerHandle {
  /** 打开弹窗;传 ids 则预选回显(命令式,父经 ref 调用)。 */
  open: (ids?: number[]) => void
}
export interface UserPickerProps {
  /** 排除项(如别处已占用的用户 id):不可再选、行内「添加」置灰。 */
  excludeIds?: number[]
  onConfirm: (ids: number[]) => void
}

const panelHeader: CSSProperties = {
  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
  fontSize: 13, fontWeight: 600, padding: '0 0 8px',
  borderBottom: '1px solid var(--color-border, #e0e0e6)', marginBottom: 8,
}
const rowItem: CSSProperties = {
  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
  padding: '4px 0', fontSize: 13, borderBottom: '1px solid var(--color-border, #e0e0e6)',
}

/**
 * 人员选择弹窗:左机构树 / 中可选用户表 / 右已选列表。命令式 `open(ids)` 经 ref 暴露,确认回调 `onConfirm(ids)`。
 * 固定 modal 形态(三面板不适合抽屉)。中间表用 <DataTable>(隔离 pro-components);**本版只保留每行「添加」,
 * 不做批量勾选** —— 勾选依赖 DataTable 尚未支持的 rowSelection + 当前页数据,待批量删除页需要它时统一补(ponytail)。
 */
export const UserPicker = forwardRef<UserPickerHandle, UserPickerProps>(function UserPicker({ excludeIds, onConfirm }, ref) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const [orgFlat, setOrgFlat] = useState<SysOrg[]>([])
  const [selectedKey, setSelectedKey] = useState<number | null>(null)
  const [selected, setSelected] = useState<PickedUser[]>([])
  const [search, setSearch] = useState('')
  const tableRef = useRef<DataTableHandle>(null)

  const excludeSet = useMemo(() => new Set(excludeIds ?? []), [excludeIds])
  const selectedIds = useMemo(() => new Set(selected.map((u) => u.id)), [selected])
  const orgId = orgIdFromKey(selectedKey)

  useImperativeHandle(
    ref,
    () => ({
      open: async (ids?: number[]) => {
        setSelected([])
        setSearch('')
        setSelectedKey(null)
        setOpen(true)
        try {
          setOrgFlat(await orgApi.list())
        } catch {
          /* 静默:机构是配角 */
        }
        if (ids?.length) {
          try {
            // ponytail: 拉一页足够大的列表靠 id 过滤;admin 用户量有限,9999 够用
            const { items } = await userApi.page({ page: 1, pageSize: 9999 })
            setSelected(hydrateSelected(ids, items))
          } catch {
            setSelected(hydrateSelected(ids, [])) // 拉失败:全退占位,不丢 id
          }
        }
      },
    }),
    [],
  )

  // orgId(部门过滤)变化即重取中间表;open=false 时表未挂载,reload 是 no-op。
  useEffect(() => {
    tableRef.current?.reload()
  }, [orgId])

  const orgTree = useMemo(
    () => [{ id: 0, parentId: -1, name: t('common.all'), children: buildTree(orgFlat) }],
    [orgFlat, t],
  )

  const fetchUsers: PageFetcher<UserItem> = (q) =>
    userApi.page({ page: q.page, pageSize: q.pageSize, name: typeof q.name === 'string' ? q.name : undefined, orgId })

  const columns = useMemo<ProColumns<UserItem>[]>(
    () => [
      { title: t('user.name'), dataIndex: 'name', search: false },
      { title: t('user.account'), dataIndex: 'account' },
      {
        title: t('common.operation'), key: 'op', search: false, width: 72,
        render: (_, r) => {
          const already = selectedIds.has(r.id) || excludeSet.has(r.id)
          return (
            <Button
              type="link" size="small" disabled={already}
              onClick={() => setSelected((cur) => addUsers(cur, [r], excludeSet))}
            >
              {already ? t('userPicker.added') : t('userPicker.add')}
            </Button>
          )
        },
      },
    ],
    [t, selectedIds, excludeSet],
  )

  const onOrgSelect: TreeProps['onSelect'] = (keys) => setSelectedKey((keys[0] as number) ?? null)
  const filtered = filterSelected(selected, search)

  return (
    <FormContainer
      open={open}
      onOpenChange={setOpen}
      variant="modal"
      title={t('userPicker.title')}
      width={1100}
      onConfirm={() => onConfirm(selected.map((u) => u.id))}
    >
      <div style={{ display: 'flex', gap: 12, minHeight: 400 }}>
        {/* 左:机构树 */}
        <div style={{ width: 180, flexShrink: 0, overflow: 'auto', borderRight: '1px solid var(--color-border, #e0e0e6)', paddingRight: 12 }}>
          <div style={panelHeader}>{t('org.title')}</div>
          <Tree
            treeData={orgTree as unknown as TreeProps['treeData']}
            fieldNames={{ title: 'name', key: 'id', children: 'children' }}
            selectedKeys={selectedKey != null ? [selectedKey] : []}
            onSelect={onOrgSelect}
            defaultExpandAll
            blockNode
          />
        </div>

        {/* 中:可选用户表 */}
        <div style={{ flex: 1, minWidth: 0, overflow: 'hidden' }}>
          <div style={panelHeader}>{t('userPicker.available')}</div>
          <DataTable<UserItem> ref={tableRef} columns={columns} fetcher={fetchUsers} rowKey="id" />
        </div>

        {/* 右:已选列表 */}
        <div style={{ width: 220, flexShrink: 0, borderLeft: '1px solid var(--color-border, #e0e0e6)', paddingLeft: 12, display: 'flex', flexDirection: 'column' }}>
          <div style={panelHeader}>
            <span>{t('userPicker.selected')}({selected.length})</span>
            <Button type="link" size="small" danger disabled={selected.length === 0} onClick={() => setSelected([])}>
              {t('userPicker.clear')}
            </Button>
          </div>
          <Input value={search} onChange={(e) => setSearch(e.target.value)} allowClear size="small" placeholder={t('common.search')} style={{ marginBottom: 8 }} />
          <div style={{ flex: 1, overflow: 'auto', maxHeight: 380 }}>
            {filtered.map((u) => (
              <div key={u.id} style={rowItem}>
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {u.name}
                  <span style={{ color: 'var(--color-text-3, #999)', fontSize: 12, marginLeft: 2 }}>({u.account})</span>
                </span>
                <Button type="link" size="small" danger onClick={() => setSelected((cur) => removeUser(cur, u.id))}>
                  {t('common.delete')}
                </Button>
              </div>
            ))}
            {filtered.length === 0 && <Empty description={t('common.noData')} style={{ padding: '24px 0' }} />}
          </div>
        </div>
      </div>
    </FormContainer>
  )
})
