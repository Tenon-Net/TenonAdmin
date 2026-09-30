// 接入应用管理 = DataTable(列表/搜索/分页)+ AppFormModal(新增/编辑)+ CredentialDrawer(凭据生命周期)
// + GrantDrawer(开放端点授权与数据范围)。启停走专用接口:停用即令该应用全部凭据立即失效。
import { useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { App, Button, Dropdown, Space, Tag, type MenuProps } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { AppIcon } from '@/components/AppIcon'
import { Can } from '@/components/Can'
import { StatusSwitch } from '@/components/StatusSwitch'
import { useConfirm } from '@/hooks/useConfirm'
import { useAuthStore, useHasPerm } from '@/stores/auth'
import { integrationAppApi, integrationCatalogApi } from '@/api/integration'
import { translateError } from '@/utils/error'
import { triggerBlobDownload } from '@/utils/download'
import type { IntegrationAppRow } from '@/types/integration'
import { PERM, fmtDateTime } from '../shared'
import { AppFormModal, type AppFormModalHandle } from './components/AppFormModal'
import { CredentialDrawer, type CredentialDrawerHandle } from './components/CredentialDrawer'
import { GrantDrawer, type GrantDrawerHandle } from './components/GrantDrawer'

export default function IntegrationAppPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()
  const isSuperAdmin = useAuthStore((s) => s.isSuperAdmin)
  const navigate = useNavigate()
  const tableRef = useRef<DataTableHandle>(null)
  const formRef = useRef<AppFormModalHandle>(null)
  const credentialRef = useRef<CredentialDrawerHandle>(null)
  const grantRef = useRef<GrantDrawerHandle>(null)
  const [downloading, setDownloading] = useState(false)
  const reload = () => tableRef.current?.reload()

  const downloadDoc = async () => {
    setDownloading(true)
    try {
      triggerBlobDownload(await integrationCatalogApi.openApiDocument('v1'), 'open-v1.json')
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setDownloading(false)
    }
  }

  const remove = (r: IntegrationAppRow) => {
    void confirm({
      content: t('integration.app.deleteConfirm', { name: r.name }),
      action: () => integrationAppApi.remove(r.id!),
      successMsg: t('integration.app.deleted'),
    }).then((ok) => { if (ok) reload() })
  }

  // fetcher 不 memo(ProTable 经 ref 读 request);搜索表单值逐字段收敛类型。
  const fetchApps: PageFetcher<IntegrationAppRow> = (q) =>
    integrationAppApi.page({
      page: q.page,
      pageSize: q.pageSize,
      keyword: typeof q.keyword === 'string' ? q.keyword : undefined,
      enabled: typeof q.enabled === 'boolean' ? q.enabled : undefined,
    })

  const columns: ProColumns<IntegrationAppRow>[] = [
    {
      title: t('integration.app.keyword'), dataIndex: 'keyword', hideInTable: true,
      fieldProps: { placeholder: t('integration.app.keywordPlaceholder'), allowClear: true },
    },
    {
      title: t('integration.app.code'), dataIndex: 'code', search: false,
      render: (_, r) => <code style={{ fontFamily: 'var(--font-mono, ui-monospace, monospace)' }}>{r.code}</code>,
    },
    { title: t('integration.app.name'), dataIndex: 'name', search: false, ellipsis: true },
    { title: t('integration.app.ownerOrg'), dataIndex: 'ownerOrgName', search: false, render: (_, r) => r.ownerOrgName || '—' },
    {
      title: t('integration.app.rateLimit'), dataIndex: 'rateLimitPerMinute', search: false, width: 120,
      render: (_, r) =>
        r.rateLimitPerMinute == null
          ? t('integration.app.rateLimitDefault')
          : Number(r.rateLimitPerMinute) === 0 ? t('integration.app.rateLimitUnlimited') : `${r.rateLimitPerMinute}/min`,
    },
    {
      title: t('integration.app.activeCredentials'), dataIndex: 'activeCredentialCount', search: false, width: 110,
      render: (_, r) => <Tag variant="filled" color={Number(r.activeCredentialCount) > 0 ? 'success' : undefined}>{String(r.activeCredentialCount ?? 0)}</Tag>,
    },
    { title: t('integration.app.lastUsed'), dataIndex: 'lastUsedAt', search: false, width: 170, render: (_, r) => fmtDateTime(r.lastUsedAt) },
    {
      title: t('integration.app.enabled'), dataIndex: 'enabled', width: 90, valueType: 'select',
      fieldProps: { allowClear: true, options: [{ label: t('common.enabled'), value: true }, { label: t('common.disabled'), value: false }] },
      render: (_, r) => (
        <StatusSwitch
          value={!!r.enabled}
          disabled={!has(r.enabled ? PERM.appDisable : PERM.appEnable) || (!r.enabled && !isSuperAdmin)}
          confirm={(next) => (next ? null : t('integration.app.disableConfirm', { name: r.name }))}
          request={(next) => integrationAppApi.setEnabled(r.id!, next)}
          onChange={() => reload()}
        />
      ),
    },
    { title: t('common.createTime'), dataIndex: 'createTime', search: false, width: 170, render: (_, r) => fmtDateTime(r.createTime) },
    {
      title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 170, fixed: 'right',
      render: (_, r) => {
        // 授权抽屉至少要读得到本应用的授权与开放端点清单
        const canOpenGrants = has(PERM.grantGet) && has(PERM.catalogEndpoints)
        const moreItems = [
          canOpenGrants ? { key: 'grant', label: t('integration.app.grants') } : null,
          has(PERM.appUpdate) ? { key: 'edit', label: t('common.edit') } : null,
          has(PERM.inboundLog) ? { key: 'logs', label: t('integration.app.callLogs') } : null,
          has(PERM.appDelete) ? { key: 'delete', label: t('common.delete'), danger: true } : null,
        ].filter((o): o is { key: string; label: string; danger?: boolean } => o !== null)
        const onMore: MenuProps['onClick'] = ({ key }) => {
          if (key === 'grant') grantRef.current?.open(r)
          else if (key === 'edit') formRef.current?.openEdit(r)
          else if (key === 'logs') navigate(`/integration/inbound-log?appId=${encodeURIComponent(String(r.id))}`)
          else if (key === 'delete') remove(r)
        }
        return (
          <Space size={4}>
            {has(PERM.credList) && (
              <Button type="link" size="small" data-testid="integration-credentials" onClick={() => credentialRef.current?.open(r)}>
                {t('integration.app.credentials')}
              </Button>
            )}
            {moreItems.length > 0 && (
              <Dropdown menu={{ items: moreItems, onClick: onMore }} trigger={['click']}>
                <Button type="link" size="small" data-testid="integration-more">{t('common.more')}</Button>
              </Dropdown>
            )}
          </Space>
        )
      },
    },
  ]

  return (
    <>
      <DataTable<IntegrationAppRow>
        ref={tableRef}
        columns={columns}
        fetcher={fetchApps}
        persistKey="integration-app"
        toolbar={
          <Space>
            <Can code={PERM.appAdd}>
              <Button type="primary" icon={<AppIcon icon="ph:plus" size={16} />} onClick={() => formRef.current?.openAdd()}>
                {t('common.add')}
              </Button>
            </Can>
            <Can code={PERM.openApiDoc}>
              <Button icon={<AppIcon icon="ph:file-arrow-down" size={16} />} loading={downloading} onClick={() => void downloadDoc()}>
                {t('integration.app.downloadDoc')}
              </Button>
            </Can>
          </Space>
        }
      />
      <AppFormModal ref={formRef} onSaved={reload} />
      <CredentialDrawer ref={credentialRef} onChanged={reload} />
      <GrantDrawer ref={grantRef} onSaved={reload} />
    </>
  )
}
