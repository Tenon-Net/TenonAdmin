// 授权与数据范围抽屉:勾选应用可调用的开放端点(默认拒绝),并为端点声明的范围维度绑定取值。
// 全量须显式勾选「全部数据」;未绑定的范围会让声明它的端点被拒(49003),页面提前提示。
// 只有本应用的授权(及有权查看的范围)完整加载后才能保存:加载失败或别的应用迟到的响应绝不能当基线写回。
import { forwardRef, useCallback, useImperativeHandle, useMemo, useRef, useState } from 'react'
import {
  Alert, App, Button, Card, Drawer, Empty, Input, Radio, Result, Select, Space, Spin, Switch, Table, Tag, TreeSelect,
  type TableColumnsType,
} from 'antd'
import { useTranslation } from 'react-i18next'
import { useAuthStore, useHasPerm } from '@/stores/auth'
import { integrationAppApi, integrationCatalogApi } from '@/api/integration'
import { translateError } from '@/utils/error'
import {
  SCOPE_NONE, SCOPE_ORG, type IntegrationAppRow, type OpenApiEndpoint, type OpenApiScopeOption, type OpenApiScopePolicy,
  type OpenAppScopeBinding,
} from '@/types/integration'
import { PERM, methodTagType } from '../../shared'

export interface GrantDrawerHandle {
  open: (row: IntegrationAppRow) => void
}

interface ScopeState {
  bound: boolean
  all: boolean
  values: string[]
  options: OpenApiScopeOption[]
}

interface TreeNode {
  value: string
  title: string
  children?: TreeNode[]
}

/** 机构等树形候选:按 parentValue 拼树。 */
function toTree(options: OpenApiScopeOption[]): TreeNode[] {
  const values = new Set(options.map((o) => o.value))
  const byParent = new Map<string, OpenApiScopeOption[]>()
  for (const o of options) {
    const parent = o.parentValue && values.has(o.parentValue) ? o.parentValue : ''
    byParent.set(parent, [...(byParent.get(parent) ?? []), o])
  }
  const build = (parent: string): TreeNode[] =>
    (byParent.get(parent) ?? []).map((o) => {
      const children = build(o.value ?? '')
      return { value: o.value ?? '', title: o.label ?? '', children: children.length ? children : undefined }
    })
  return build('')
}

function bindingsOf(scopes: Record<string, ScopeState>) {
  return Object.entries(scopes)
    .filter(([, s]) => s.bound)
    .map(([scopeKey, s]) => ({ scopeKey, allValues: s.all, values: s.all ? [] : [...s.values].sort() }))
    .sort((a, b) => a.scopeKey.localeCompare(b.scopeKey))
}

export const GrantDrawer = forwardRef<GrantDrawerHandle, { onSaved: () => void }>(function GrantDrawer({ onSaved }, ref) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const has = useHasPerm()
  const isSuperAdmin = useAuthStore((s) => s.isSuperAdmin)
  // 授权区的读权限由入口(「更多」菜单)把关;范围区要「范围策略清单 + 查看数据范围」两个权限才加载
  const canViewScopes = has(PERM.catalogScopes) && has(PERM.scopeGet)
  const canLoadOptions = has(PERM.scopeOptions)
  const canSaveGrants = isSuperAdmin && has(PERM.grantSet)
  const canSaveScopes = isSuperAdmin && canViewScopes && has(PERM.scopeSet)

  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [app, setApp] = useState<IntegrationAppRow | null>(null)
  const [endpoints, setEndpoints] = useState<OpenApiEndpoint[]>([])
  const [policies, setPolicies] = useState<OpenApiScopePolicy[]>([])
  const [checked, setChecked] = useState<string[]>([])
  const [stale, setStale] = useState<string[]>([])
  const [keyword, setKeyword] = useState('')
  const [scopes, setScopes] = useState<Record<string, ScopeState>>({})
  /** 完整加载成功的应用;与当前应用不符(加载中、失败、换了应用)一律不许保存。 */
  const [loadedAppId, setLoadedAppId] = useState<number | string | null>(null)
  /** 本次加载是否带上了数据范围(没有查看权限就不取,也就不能写回)。 */
  const [scopesLoaded, setScopesLoaded] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const original = useRef({ grants: '', scopes: '' })
  const loadSeq = useRef(0)
  const currentAppId = useRef<number | string | null>(null)

  const load = useCallback(async (row: IntegrationAppRow) => {
    if (!row.id) return
    const seq = ++loadSeq.current
    setLoading(true)
    setLoadError(null)
    setLoadedAppId(null)
    try {
      const [eps, grants, pols, bound] = await Promise.all([
        integrationCatalogApi.endpoints(),
        integrationAppApi.grants(row.id),
        canViewScopes ? integrationCatalogApi.scopes() : Promise.resolve<OpenApiScopePolicy[]>([]),
        canViewScopes ? integrationAppApi.scopes(row.id) : Promise.resolve<OpenAppScopeBinding[]>([]),
      ])
      // 没有候选值权限就不去取(免得逐个 403),下拉退回手输取值
      const optionLists = canLoadOptions
        ? await Promise.all(pols.map((p) => integrationCatalogApi.scopeOptions(p.key ?? '').catch(() => [])))
        : pols.map(() => [])
      if (seq !== loadSeq.current || currentAppId.current !== row.id) return
      const staleCodes = grants.stale ?? []
      const granted = (grants.permissions ?? []).filter((p) => !staleCodes.includes(p))
      const next: Record<string, ScopeState> = {}
      pols.forEach((p, i) => {
        const b = bound.find((x) => x.scopeKey === p.key)
        next[p.key ?? ''] = { bound: !!b, all: !!b?.allValues, values: [...(b?.values ?? [])], options: optionLists[i] }
      })
      setEndpoints(eps)
      setPolicies(pols)
      setStale(staleCodes)
      setChecked(granted)
      setScopes(next)
      original.current = { grants: JSON.stringify([...granted].sort()), scopes: JSON.stringify(bindingsOf(next)) }
      setScopesLoaded(canViewScopes)
      setLoadedAppId(row.id)
    } catch (e) {
      if (seq !== loadSeq.current) return
      const text = translateError(e)
      message.error(text)
      setLoadError(text)
    } finally {
      if (seq === loadSeq.current) setLoading(false)
    }
  }, [message, canViewScopes, canLoadOptions])

  useImperativeHandle(ref, () => ({
    open: (row) => {
      currentAppId.current = row.id ?? null
      setApp(row)
      setEndpoints([])
      setPolicies([])
      setChecked([])
      setStale([])
      setKeyword('')
      setScopes({})
      setLoadedAppId(null)
      setScopesLoaded(false)
      setLoadError(null)
      original.current = { grants: '', scopes: '' }
      setOpen(true)
      void load(row)
    },
  }), [load])

  const loaded = app?.id != null && loadedAppId === app.id

  /** 关抽屉时一并清掉「当前应用」:迟到的加载、保存结果据此认出抽屉已不是发起时那一个。 */
  const close = () => {
    currentAppId.current = null
    setOpen(false)
  }

  const policyName = useCallback((key: string) => policies.find((p) => p.key === key)?.name ?? key, [policies])

  const filtered = useMemo(() => {
    const kw = keyword.trim().toLowerCase()
    if (!kw) return endpoints
    return endpoints.filter((e) => [e.permission, e.summary, e.group].some((v) => (v ?? '').toLowerCase().includes(kw)))
  }, [endpoints, keyword])

  /** 已勾选端点需要、但尚未绑定的范围维度(保存后这些端点会被拒);没加载范围时无从判断。 */
  const missingScopes = useMemo(() => {
    if (!scopesLoaded) return []
    const need = new Set(endpoints.filter((e) => checked.includes(e.permission ?? '')).map((e) => e.scopeKey ?? ''))
    need.delete(SCOPE_NONE)
    return [...need].filter((k) => !scopes[k]?.bound)
  }, [endpoints, checked, scopes, scopesLoaded])

  /** 机构候选树:候选只在加载时换引用(patchScope 沿用原 options),勾选取值等编辑不会重建。 */
  const orgOptions = scopes[SCOPE_ORG]?.options
  const orgTree = useMemo(() => toTree(orgOptions ?? []), [orgOptions])

  const patchScope = (key: string, patch: Partial<ScopeState>) =>
    setScopes((prev) => ({ ...prev, [key]: { ...prev[key], ...patch } }))

  const save = async () => {
    if (!app?.id || !loaded) return
    for (const [key, s] of Object.entries(scopes)) {
      if (s.bound && !s.all && s.values.length === 0) {
        message.warning(t('integration.grant.valuesRequired', { scope: policyName(key) }))
        return
      }
    }
    // 写什么在第一次 await 之前定下来:保存途中抽屉可能被关掉或换成别的应用(original 随之重置),之后只认这份快照
    const appId = app.id
    const grants = canSaveGrants && JSON.stringify([...checked].sort()) !== original.current.grants ? [...checked] : null
    const nextBindings = bindingsOf(scopes)
    const bindings = scopesLoaded && canSaveScopes && JSON.stringify(nextBindings) !== original.current.scopes ? nextBindings : null
    setSaving(true)
    try {
      if (grants) await integrationAppApi.setGrants(appId, grants)
      if (bindings) await integrationAppApi.setScopes(appId, bindings)
      // 抽屉已关或已换成别的应用:不再提示、不再替那边关抽屉
      if (currentAppId.current === appId) {
        message.success(t('integration.grant.saved'))
        onSaved()
        close()
      }
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSaving(false)
    }
  }

  const columns: TableColumnsType<OpenApiEndpoint> = [
    {
      title: t('integration.grant.method'), dataIndex: 'httpMethod', width: 90,
      render: (_, r) => <Tag variant="filled" color={methodTagType(r.httpMethod)}>{r.httpMethod}</Tag>,
    },
    { title: t('integration.grant.route'), dataIndex: 'route', ellipsis: true, render: (_, r) => `/${r.route}` },
    { title: t('integration.grant.summary'), dataIndex: 'summary', ellipsis: true, render: (_, r) => r.summary || '—' },
    {
      title: t('integration.grant.scope'), dataIndex: 'scopeKey', width: 110,
      render: (_, r) =>
        r.scopeKey === SCOPE_NONE
          ? <Tag variant="filled">{t('integration.grant.scopeNone')}</Tag>
          : <Tag variant="filled" color="processing">{policyName(r.scopeKey ?? '')}</Tag>,
    },
  ]

  return (
    <Drawer
      open={open}
      onClose={close}
      size={880}
      title={t('integration.grant.title', { app: app?.name ?? '' })}
      footer={
        <Space style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <Button onClick={close}>{t('common.cancel')}</Button>
          {(canSaveGrants || canSaveScopes) && (
            <Button type="primary" loading={saving} disabled={!loaded} data-testid="integration-grant-save" onClick={() => void save()}>
              {t('common.save')}
            </Button>
          )}
        </Space>
      }
    >
      <Spin spinning={loading}>
        {loadError ? (
          <Result
            status="error"
            title={t('integration.grant.loadFailed')}
            subTitle={loadError}
            extra={<Button type="primary" onClick={() => { if (app) void load(app) }}>{t('integration.grant.retry')}</Button>}
          />
        ) : (
          <Space orientation="vertical" size={16} style={{ width: '100%' }}>
            <Card
              size="small"
              title={t('integration.grant.endpoints')}
              extra={<Input size="small" allowClear value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder={t('integration.grant.search')} style={{ width: 220 }} />}
            >
              {stale.length > 0 && (
                <Alert
                  type="warning"
                  style={{ marginBottom: 12 }}
                  title={t('integration.grant.staleHint')}
                  description={stale.map((s) => <div key={s} style={{ fontFamily: 'var(--font-mono, ui-monospace, monospace)', fontSize: 12 }}>{s}</div>)}
                />
              )}
              {endpoints.length ? (
                <Table<OpenApiEndpoint>
                  rowKey={(r) => r.permission ?? ''}
                  columns={columns}
                  dataSource={filtered}
                  pagination={false}
                  size="small"
                  scroll={{ y: 320 }}
                  rowSelection={{
                    selectedRowKeys: checked,
                    onChange: (keys) => setChecked(keys.map(String)),
                    getCheckboxProps: () => ({ disabled: !canSaveGrants }),
                    preserveSelectedRowKeys: true,
                  }}
                />
              ) : loaded ? (
                <Empty description={t('integration.grant.noEndpoints')} />
              ) : null}
            </Card>

            <Card size="small" title={t('integration.grant.scopes')}>
              {loaded && !scopesLoaded && <Alert type="info" title={t('integration.grant.scopesNoPermission')} />}
              {missingScopes.length > 0 && (
                <Alert
                  type="error"
                  style={{ marginBottom: 12 }}
                  data-testid="integration-missing-scope"
                  title={t('integration.grant.missingScopes', { scopes: missingScopes.map(policyName).join('、') })}
                />
              )}
              {scopesLoaded && !policies.length && <Empty description={t('integration.grant.noPolicies')} />}
              {policies.map((p, index) => {
                const key = p.key ?? ''
                const s = scopes[key]
                if (!s) return null
                return (
                  <div key={key} style={index > 0 ? { marginTop: 16, paddingTop: 16, borderTop: '1px solid var(--color-border)' } : undefined}>
                    <Space size={8} align="center">
                      <span style={{ fontWeight: 600 }}>{p.name}</span>
                      <Tag variant="filled">{key}</Tag>
                      <Switch size="small" checked={s.bound} disabled={!canSaveScopes} onChange={(v) => patchScope(key, { bound: v })} />
                      <span style={{ fontSize: 13, color: 'var(--color-text-secondary)' }}>
                        {s.bound ? t('integration.grant.bound') : t('integration.grant.unbound')}
                      </span>
                    </Space>
                    {s.bound && (
                      <Space orientation="vertical" size={8} style={{ width: '100%', marginTop: 8 }}>
                        <Radio.Group value={s.all} disabled={!canSaveScopes} onChange={(e) => patchScope(key, { all: e.target.value as boolean })}>
                          <Radio value={false}>{t('integration.grant.specific')}</Radio>
                          <Radio value={true}>{t('integration.grant.allValues')}</Radio>
                        </Radio.Group>
                        {!s.all && key === SCOPE_ORG && (
                          <TreeSelect
                            multiple
                            showSearch={{ treeNodeFilterProp: 'title' }}
                            allowClear
                            style={{ width: '100%' }}
                            value={s.values}
                            treeData={orgTree}
                            disabled={!canSaveScopes}
                            placeholder={t('integration.grant.orgPlaceholder')}
                            onChange={(v) => patchScope(key, { values: (v as string[]) ?? [] })}
                          />
                        )}
                        {!s.all && key !== SCOPE_ORG && (
                          <Select
                            mode={s.options.length ? 'multiple' : 'tags'}
                            allowClear
                            style={{ width: '100%' }}
                            value={s.values}
                            options={s.options.map((o) => ({ label: o.label, value: o.value }))}
                            disabled={!canSaveScopes}
                            placeholder={t('integration.grant.valuesPlaceholder')}
                            onChange={(v) => patchScope(key, { values: v })}
                          />
                        )}
                        {s.all && <Alert type="warning" title={t('integration.grant.allValuesHint')} />}
                      </Space>
                    )}
                  </div>
                )
              })}
            </Card>
          </Space>
        )}
      </Spin>
    </Drawer>
  )
})
