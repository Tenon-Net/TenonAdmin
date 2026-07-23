// 登录日志 = 只读 DataTable:列驱动搜索(账号/结果)+ 时间范围 + 分页。无详情抽屉(字段少,列内看全)。
// success 列显式红/绿 Tag 保证「失败=红」;resultCode 列把 40001–40005 翻成 error.auth.* 人话(与登录页同源)。
import { useRef, type CSSProperties } from 'react'
import { Button, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { Can } from '@/components/Can'
import { useConfirm } from '@/hooks/useConfirm'
import { logApi } from '@/api'
import { uaSummary } from '@/utils/ua'
import type { SysLoginLog } from '@/types/api'
import { loginNameText, loginResultKey } from '../logFormat'

const ellipsisCell: CSSProperties = { display: 'block', maxWidth: 240, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }

export default function LoginLogPage() {
  const { t } = useTranslation()
  const { confirm } = useConfirm()
  const tableRef = useRef<DataTableHandle>(null)

  const fetchLogin: PageFetcher<SysLoginLog> = (q) =>
    logApi.loginPage({
      page: q.page, pageSize: q.pageSize,
      account: typeof q.account === 'string' ? q.account : undefined,
      success: typeof q.success === 'boolean' ? q.success : undefined,
      createTime: (q.createTime as [string, string] | undefined) ?? null,
    })

  const columns: ProColumns<SysLoginLog>[] = [
    { title: t('log.account'), dataIndex: 'account' },
    {
      title: t('log.result'), dataIndex: 'success', width: 100, valueType: 'select',
      fieldProps: { allowClear: true, options: [{ label: t('log.success'), value: true }, { label: t('log.failed'), value: false }] },
      render: (_, r) => <Tag color={r.success ? 'success' : 'error'}>{t(r.success ? 'log.success' : 'log.failed')}</Tag>,
    },
    {
      // 失败原因翻成人话:密码错/被锁/被停用三种处置不同(改密/解锁/启用);未命中的码回落原始数字。
      title: t('log.resultCode'), dataIndex: 'resultCode', width: 160, search: false,
      render: (_, r) => {
        const key = loginResultKey(r.resultCode)
        return <Tag color={r.resultCode === 0 ? 'success' : 'error'}>{key ? t(key) : r.resultCode}</Tag>
      },
    },
    { title: t('log.name'), dataIndex: 'name', search: false, render: (_, r) => loginNameText(r) },
    { title: t('log.ip'), dataIndex: 'ip', search: false, render: (_, r) => r.ip || '—' },
    // 设备:UA 解析成「浏览器 · 系统」(全未识别回落原始串);过长截断。
    { title: t('log.device'), dataIndex: 'userAgent', search: false, render: (_, r) => <span style={ellipsisCell} title={r.userAgent ?? undefined}>{uaSummary(r.userAgent)}</span> },
    { title: t('common.createTime'), dataIndex: 'createTime', valueType: 'dateRange', render: (_, r) => r.createTime },
  ]

  const clearLogs = () => {
    confirm({ content: t('log.clearLoginConfirm'), action: () => logApi.loginClear(), successMsg: t('log.cleared') }).then((ok) => {
      if (ok) tableRef.current?.reload()
    })
  }

  return (
    <DataTable<SysLoginLog>
      ref={tableRef}
      columns={columns}
      fetcher={fetchLogin}
      persistKey="sys-log-login"
      toolbar={
        <Can code="DELETE:/api/v1/sys/log/login">
          <Button danger onClick={clearLogs}>{t('log.clear')}</Button>
        </Can>
      }
    />
  )
}
