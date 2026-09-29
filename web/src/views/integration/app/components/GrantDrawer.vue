<script setup lang="ts">
// 授权与数据范围抽屉:勾选应用可调用的开放端点(默认拒绝),并为端点声明的范围维度绑定取值。
// 全量须显式勾选「全部数据」;未绑定的范围会让声明它的端点被拒(49003),页面提前提示。
// 只有本应用的授权(及有权查看的范围)完整加载后才能保存:加载失败或别的应用迟到的响应绝不能当基线写回。
import { computed, h, reactive, ref } from 'vue'
import {
  NAlert, NButton, NCard, NDataTable, NDrawer, NDrawerContent, NEmpty, NInput, NRadio, NRadioGroup, NResult, NSelect,
  NSpace, NSpin, NSwitch, NTag, NTreeSelect, useMessage, type DataTableColumns, type TreeSelectOption,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { integrationAppApi, integrationCatalogApi } from '@/api/integration'
import { translateError } from '@/utils/error'
import {
  SCOPE_NONE, SCOPE_ORG, type IntegrationAppRow, type OpenApiEndpoint, type OpenApiScopeOption, type OpenApiScopePolicy,
  type OpenAppScopeBinding,
} from '@/types/integration'
import { PERM, methodTagType } from '../../shared'

const emit = defineEmits<{ saved: [] }>()
const { t } = useI18n()
const message = useMessage()
const authStore = useAuthStore()

const show = ref(false)
const loading = ref(false)
const saving = ref(false)
const app = ref<IntegrationAppRow | null>(null)
const endpoints = ref<OpenApiEndpoint[]>([])
const policies = ref<OpenApiScopePolicy[]>([])
const checked = ref<string[]>([])
const stale = ref<string[]>([])
const keyword = ref('')
/** 完整加载成功的应用;与当前应用不符(加载中、失败、换了应用)一律不许保存。 */
const loadedAppId = ref<number | string | null>(null)
/** 本次加载是否带上了数据范围(没有查看权限就不取,也就不能写回)。 */
const scopesLoaded = ref(false)
const loadError = ref<string | null>(null)

interface ScopeState {
  bound: boolean
  all: boolean
  values: string[]
  options: OpenApiScopeOption[]
}
const scopes = reactive<Record<string, ScopeState>>({})
let original = { grants: '', scopes: '' }
let loadSeq = 0

// 授权区的读权限由入口(「更多」菜单)把关;范围区要「范围策略清单 + 查看数据范围」两个权限才加载
const canViewScopes = computed(() => authStore.hasPerm(PERM.catalogScopes) && authStore.hasPerm(PERM.scopeGet))
const canSaveGrants = computed(() => authStore.isSuperAdmin && authStore.hasPerm(PERM.grantSet))
const canSaveScopes = computed(() => authStore.isSuperAdmin && canViewScopes.value && authStore.hasPerm(PERM.scopeSet))
const loaded = computed(() => app.value?.id != null && loadedAppId.value === app.value.id)

function open(row: IntegrationAppRow) {
  app.value = row
  endpoints.value = []
  policies.value = []
  checked.value = []
  stale.value = []
  keyword.value = ''
  for (const key of Object.keys(scopes)) delete scopes[key]
  original = { grants: '', scopes: '' }
  loadedAppId.value = null
  scopesLoaded.value = false
  loadError.value = null
  show.value = true
  void load()
}
defineExpose({ open })

async function load() {
  const target = app.value
  if (!target?.id) return
  const seq = ++loadSeq
  const withScopes = canViewScopes.value
  loading.value = true
  loadError.value = null
  loadedAppId.value = null
  try {
    const [eps, grants, pols, bound] = await Promise.all([
      integrationCatalogApi.endpoints(),
      integrationAppApi.grants(target.id),
      withScopes ? integrationCatalogApi.scopes() : Promise.resolve<OpenApiScopePolicy[]>([]),
      withScopes ? integrationAppApi.scopes(target.id) : Promise.resolve<OpenAppScopeBinding[]>([]),
    ])
    // 没有候选值权限就不去取(免得逐个 403),下拉退回手输取值
    const optionLists = authStore.hasPerm(PERM.scopeOptions)
      ? await Promise.all(pols.map((p) => integrationCatalogApi.scopeOptions(p.key ?? '').catch(() => [])))
      : pols.map(() => [])
    if (seq !== loadSeq || app.value?.id !== target.id) return
    const staleCodes = grants.stale ?? []
    endpoints.value = eps
    policies.value = pols
    stale.value = staleCodes
    checked.value = (grants.permissions ?? []).filter((p) => !staleCodes.includes(p))
    for (const key of Object.keys(scopes)) delete scopes[key]
    pols.forEach((p, i) => {
      const b = bound.find((x) => x.scopeKey === p.key)
      scopes[p.key ?? ''] = { bound: !!b, all: !!b?.allValues, values: [...(b?.values ?? [])], options: optionLists[i] }
    })
    original = { grants: snapshotGrants(), scopes: snapshotScopes() }
    scopesLoaded.value = withScopes
    loadedAppId.value = target.id
  } catch (e) {
    if (seq !== loadSeq) return
    loadError.value = translateError(e)
    message.error(loadError.value)
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}

const snapshotGrants = () => JSON.stringify([...checked.value].sort())
const snapshotScopes = () => JSON.stringify(scopeBindings())

function scopeBindings() {
  return Object.entries(scopes)
    .filter(([, s]) => s.bound)
    .map(([scopeKey, s]) => ({ scopeKey, allValues: s.all, values: s.all ? [] : [...s.values].sort() }))
    .sort((a, b) => a.scopeKey.localeCompare(b.scopeKey))
}

const filteredEndpoints = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return endpoints.value
  return endpoints.value.filter((e) =>
    [e.permission, e.summary, e.group].some((v) => (v ?? '').toLowerCase().includes(kw)),
  )
})

/** 已勾选端点需要、但尚未绑定的范围维度(保存后这些端点会被拒);没加载范围时无从判断。 */
const missingScopes = computed(() => {
  if (!scopesLoaded.value) return []
  const need = new Set(
    endpoints.value.filter((e) => checked.value.includes(e.permission ?? '')).map((e) => e.scopeKey ?? ''),
  )
  need.delete(SCOPE_NONE)
  return [...need].filter((k) => !scopes[k]?.bound)
})

const policyName = (key: string) => policies.value.find((p) => p.key === key)?.name ?? key

/** 机构等树形候选:按 parentValue 拼树。 */
function toTree(options: OpenApiScopeOption[]): TreeSelectOption[] {
  const byParent = new Map<string, OpenApiScopeOption[]>()
  const values = new Set(options.map((o) => o.value))
  for (const o of options) {
    const parent = o.parentValue && values.has(o.parentValue) ? o.parentValue : ''
    byParent.set(parent, [...(byParent.get(parent) ?? []), o])
  }
  const build = (parent: string): TreeSelectOption[] =>
    (byParent.get(parent) ?? []).map((o) => {
      const children = build(o.value ?? '')
      return { key: o.value, label: o.label, children: children.length ? children : undefined }
    })
  return build('')
}

/** 范围配置行:策略 + 其可编辑状态(即 scopes 里的同一 reactive 对象,改 s.* 就是改 scopes)。 */
const scopeRows = computed(() => policies.value.map((p) => ({ p, s: scopes[p.key ?? ''] as ScopeState | undefined })))
/** 机构候选树:只随加载到的候选重建,勾选取值等编辑不会触发重算。 */
const orgTree = computed(() => toTree(scopes[SCOPE_ORG]?.options ?? []))

async function save() {
  const target = app.value
  if (!target?.id || !loaded.value) return
  for (const [key, s] of Object.entries(scopes)) {
    if (s.bound && !s.all && s.values.length === 0) {
      message.warning(t('integration.grant.valuesRequired', { scope: policyName(key) }))
      return
    }
  }
  // 写什么在第一次 await 之前定下来:保存途中抽屉可能被关掉或换成别的应用,之后只认这份快照
  const appId = target.id
  const grants = canSaveGrants.value && snapshotGrants() !== original.grants ? [...checked.value] : null
  const nextBindings = scopeBindings()
  const bindings = scopesLoaded.value && canSaveScopes.value && JSON.stringify(nextBindings) !== original.scopes ? nextBindings : null
  saving.value = true
  try {
    if (grants) await integrationAppApi.setGrants(appId, grants)
    if (bindings) await integrationAppApi.setScopes(appId, bindings)
    // 抽屉已关或已换成别的应用:不再提示、不再替那边关抽屉
    if (show.value && app.value?.id === appId) {
      message.success(t('integration.grant.saved'))
      emit('saved')
      show.value = false
    }
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}

const columns: DataTableColumns<OpenApiEndpoint> = [
  { type: 'selection', disabled: () => !canSaveGrants.value },
  {
    title: () => t('integration.grant.method'),
    key: 'httpMethod',
    width: 90,
    render: (r) => h(NTag, { size: 'small', bordered: false, type: methodTagType(r.httpMethod) }, () => r.httpMethod),
  },
  { title: () => t('integration.grant.route'), key: 'route', ellipsis: { tooltip: true }, render: (r) => `/${r.route}` },
  { title: () => t('integration.grant.summary'), key: 'summary', ellipsis: { tooltip: true }, render: (r) => r.summary || '—' },
  {
    title: () => t('integration.grant.scope'),
    key: 'scopeKey',
    width: 110,
    render: (r) =>
      r.scopeKey === SCOPE_NONE
        ? h(NTag, { size: 'small', bordered: false }, () => t('integration.grant.scopeNone'))
        : h(NTag, { size: 'small', bordered: false, type: 'info' }, () => policyName(r.scopeKey ?? '')),
  },
]
</script>

<template>
  <n-drawer v-model:show="show" :width="880" placement="right">
    <n-drawer-content :title="t('integration.grant.title', { app: app?.name ?? '' })" closable>
      <n-spin :show="loading">
        <n-result v-if="loadError" status="error" size="small" :title="t('integration.grant.loadFailed')" :description="loadError">
          <template #footer>
            <n-button type="primary" @click="load">{{ t('integration.grant.retry') }}</n-button>
          </template>
        </n-result>
        <n-space v-else vertical :size="16">
          <n-card size="small" :title="t('integration.grant.endpoints')">
            <template #header-extra>
              <n-input v-model:value="keyword" size="small" clearable :placeholder="t('integration.grant.search')" style="width: 220px" />
            </template>
            <n-alert v-if="stale.length" type="warning" :bordered="false" class="grant-alert">
              {{ t('integration.grant.staleHint') }}
              <div v-for="s in stale" :key="s" class="stale-code">{{ s }}</div>
            </n-alert>
            <n-data-table
              v-if="endpoints.length"
              v-model:checked-row-keys="checked"
              :columns="columns"
              :data="filteredEndpoints"
              :row-key="(r: OpenApiEndpoint) => r.permission ?? ''"
              size="small"
              :bordered="false"
              :max-height="320"
            />
            <n-empty v-else-if="loaded" :description="t('integration.grant.noEndpoints')" />
          </n-card>

          <n-card size="small" :title="t('integration.grant.scopes')">
            <n-alert v-if="loaded && !scopesLoaded" type="info" :bordered="false" :show-icon="false">
              {{ t('integration.grant.scopesNoPermission') }}
            </n-alert>
            <n-alert v-if="missingScopes.length" type="error" :bordered="false" class="grant-alert" data-testid="integration-missing-scope">
              {{ t('integration.grant.missingScopes', { scopes: missingScopes.map(policyName).join('、') }) }}
            </n-alert>
            <n-empty v-if="scopesLoaded && !policies.length" :description="t('integration.grant.noPolicies')" />
            <div v-for="{ p, s } in scopeRows" :key="p.key" class="scope-row">
              <template v-if="s">
                <div class="scope-head">
                  <span class="scope-name">{{ p.name }}</span>
                  <n-tag size="small" :bordered="false">{{ p.key }}</n-tag>
                  <n-switch v-model:value="s.bound" size="small" :disabled="!canSaveScopes" />
                  <span class="scope-state">{{ s.bound ? t('integration.grant.bound') : t('integration.grant.unbound') }}</span>
                </div>
                <div v-if="s.bound" class="scope-body">
                  <n-radio-group v-model:value="s.all" :disabled="!canSaveScopes">
                    <n-radio :value="false">{{ t('integration.grant.specific') }}</n-radio>
                    <n-radio :value="true">{{ t('integration.grant.allValues') }}</n-radio>
                  </n-radio-group>
                  <template v-if="!s.all">
                    <n-tree-select
                      v-if="p.key === SCOPE_ORG"
                      v-model:value="s.values"
                      multiple
                      filterable
                      clearable
                      :options="orgTree"
                      :disabled="!canSaveScopes"
                      :placeholder="t('integration.grant.orgPlaceholder')"
                    />
                    <n-select
                      v-else
                      v-model:value="s.values"
                      multiple
                      filterable
                      :tag="!s.options.length"
                      :options="s.options.map((o) => ({ label: o.label, value: o.value }))"
                      :disabled="!canSaveScopes"
                      :placeholder="t('integration.grant.valuesPlaceholder')"
                    />
                  </template>
                  <n-alert v-else type="warning" :bordered="false">{{ t('integration.grant.allValuesHint') }}</n-alert>
                </div>
              </template>
            </div>
          </n-card>
        </n-space>
      </n-spin>
      <template #footer>
        <n-space justify="end">
          <n-button @click="show = false">{{ t('common.cancel') }}</n-button>
          <n-button
            v-if="canSaveGrants || canSaveScopes"
            type="primary"
            :loading="saving"
            :disabled="!loaded"
            data-testid="integration-grant-save"
            @click="save"
          >
            {{ t('common.save') }}
          </n-button>
        </n-space>
      </template>
    </n-drawer-content>
  </n-drawer>
</template>

<style scoped>
.grant-alert {
  margin-bottom: var(--space-12, 12px);
}
.stale-code {
  font-family: var(--font-family-mono, monospace);
  font-size: var(--font-size-xs, 12px);
}
.scope-row + .scope-row {
  margin-top: var(--space-16, 16px);
  padding-top: var(--space-16, 16px);
  border-top: 1px solid var(--color-border);
}
.scope-head {
  display: flex;
  align-items: center;
  gap: var(--space-8, 8px);
}
.scope-name {
  font-weight: 600;
}
.scope-state {
  font-size: var(--font-size-sm, 13px);
  color: var(--color-text-secondary);
}
.scope-body {
  display: flex;
  flex-direction: column;
  gap: var(--space-8, 8px);
  margin-top: var(--space-8, 8px);
}
</style>
