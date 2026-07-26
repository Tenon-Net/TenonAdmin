// 导出选列弹窗:列勾选,默认按 DefaultSelected;确认后把选中 Key 列表交给父级发请求。
// 与 web/ ExportColumnsModal 功能对齐,零共享(excel-ledger 坑 7)。
import { useEffect, useMemo, useState } from 'react'
import { Button, Checkbox, Empty, Modal, Space } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ExportColumnDef } from '@/types/api'

export interface ExportColumnsModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  columns: ExportColumnDef[]
  /** 导出中 → 确认钮 loading,并禁止关窗 */
  loading?: boolean
  /** 用户点确认,载荷 = 勾选的列 Key(顺序与 columns 声明一致) */
  onConfirm: (keys: string[]) => void
}

export function ExportColumnsModal({
  open,
  onOpenChange,
  columns,
  loading = false,
  onConfirm,
}: ExportColumnsModalProps) {
  const { t } = useTranslation()
  const [checked, setChecked] = useState<string[]>([])

  // 每次打开按 DefaultSelected 播种(缺省 true)
  useEffect(() => {
    if (!open) return
    setChecked(columns.filter((c) => c.defaultSelected !== false).map((c) => c.key))
  }, [open, columns])

  const allKeys = useMemo(() => columns.map((c) => c.key), [columns])
  const allChecked = allKeys.length > 0 && checked.length === allKeys.length
  const indeterminate = checked.length > 0 && checked.length < allKeys.length

  const handleConfirm = () => {
    if (checked.length === 0) return
    // 保持档案声明顺序,不按勾选先后
    const ordered = columns.map((c) => c.key).filter((k) => checked.includes(k))
    onConfirm(ordered)
  }

  return (
    <Modal
      open={open}
      title={t('export.pickColumns')}
      width={420}
      onCancel={() => { if (!loading) onOpenChange(false) }}
      mask={{ closable: !loading }}
      keyboard={!loading}
      closable={!loading}
      destroyOnHidden
      footer={
        <Space style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <Button disabled={loading} onClick={() => onOpenChange(false)}>{t('common.cancel')}</Button>
          <Button type="primary" loading={loading} disabled={checked.length === 0} onClick={handleConfirm}>
            {t('export.confirm')}
          </Button>
        </Space>
      }
    >
      {columns.length === 0 ? (
        <Empty description={t('common.noData')} />
      ) : (
        <>
          <div style={{ marginBottom: 12, paddingBottom: 8, borderBottom: '1px solid var(--color-border)' }}>
            <Checkbox
              checked={allChecked}
              indeterminate={indeterminate}
              disabled={loading}
              onChange={(e) => setChecked(e.target.checked ? [...allKeys] : [])}
            >
              {t('export.selectAll')}
            </Checkbox>
          </div>
          <Checkbox.Group
            value={checked}
            disabled={loading}
            onChange={(v) => setChecked(v as string[])}
            style={{ display: 'flex', flexDirection: 'column', gap: 8 }}
            options={columns.map((c) => ({ label: c.title, value: c.key }))}
          />
        </>
      )}
    </Modal>
  )
}
