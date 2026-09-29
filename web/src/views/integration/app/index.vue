<script setup lang="ts">
// 接入应用管理 = ProTable(列表/搜索/分页)+ AppFormModal(新增/编辑)+ CredentialDrawer(凭据生命周期)
// + GrantDrawer(开放端点授权与数据范围)。启停走专用接口:停用即令该应用全部凭据立即失效。
import { h, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NDropdown, NSpace, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import AppFormModal from './components/AppFormModal.vue'
import CredentialDrawer from './components/CredentialDrawer.vue'
import GrantDrawer from './components/GrantDrawer.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useAuthStore } from '@/stores/auth'
import { integrationAppApi, integrationCatalogApi } from '@/api/integration'
import { translateError } from '@/utils/error'
import { triggerBlobDownload } from '@/utils/download'
import type { IntegrationAppRow } from '@/types/integration'
import { PERM, fmtDateTime } from '../shared'

const { t } = useI18n()
const message = useMessage()
const router = useRouter()
const { confirm } = useConfirm()
const authStore = useAuthStore()
const tableRef = ref<ProTableInst<IntegrationAppRow>>()
const formRef = ref<InstanceType<typeof AppFormModal> | null>(null)
const credentialRef = ref<InstanceType<typeof CredentialDrawer> | null>(null)
const grantRef = ref<InstanceType<typeof GrantDrawer> | null>(null)
const refresh = () => tableRef.value?.refresh()

const downloading = ref(false)
async function downloadDoc() {
  downloading.value = true
  try {
    triggerBlobDownload(await integrationCatalogApi.openApiDocument('v1'), 'open-v1.json')
  } catch (e) {
    message.error(translateError(e))
  } finally {
    downloading.value = false
  }
}

async function remove(r: IntegrationAppRow) {
  const ok = await confirm({
    type: 'warning',
    content: t('integration.app.deleteConfirm', { name: r.name }),
    action: () => integrationAppApi.remove(r.id!),
    successMsg: t('integration.app.deleted'),
  })
  if (ok) refresh()
}

function onMore(key: string, r: IntegrationAppRow) {
  if (key === 'grant') grantRef.value?.open(r)
  else if (key === 'edit') formRef.value?.openEdit(r)
  else if (key === 'logs') void router.push({ path: '/integration/inbound-log', query: { appId: String(r.id) } })
  else if (key === 'delete') void remove(r)
}

const columns: ProTableColumn<IntegrationAppRow>[] = [
  {
    key: 'keyword',
    title: () => t('integration.app.keyword'),
    hideInTable: true,
    search: { props: { clearable: true, placeholder: t('integration.app.keywordPlaceholder') } },
  },
  { key: 'code', title: () => t('integration.app.code'), render: (r) => h('code', { class: 'app-code' }, r.code) },
  { key: 'name', title: () => t('integration.app.name'), ellipsis: { tooltip: true } },
  { key: 'ownerOrgName', title: () => t('integration.app.ownerOrg'), render: (r) => r.ownerOrgName || '—' },
  {
    key: 'rateLimitPerMinute',
    title: () => t('integration.app.rateLimit'),
    width: 120,
    render: (r) =>
      r.rateLimitPerMinute == null
        ? t('integration.app.rateLimitDefault')
        : Number(r.rateLimitPerMinute) === 0
          ? t('integration.app.rateLimitUnlimited')
          : `${r.rateLimitPerMinute}/min`,
  },
  {
    key: 'activeCredentialCount',
    title: () => t('integration.app.activeCredentials'),
    width: 110,
    render: (r) =>
      h(NTag, { size: 'small', bordered: false, type: Number(r.activeCredentialCount) > 0 ? 'success' : 'default' }, () =>
        String(r.activeCredentialCount ?? 0),
      ),
  },
  { key: 'lastUsedAt', title: () => t('integration.app.lastUsed'), width: 170, render: (r) => fmtDateTime(r.lastUsedAt) },
  {
    key: 'enabled',
    title: () => t('integration.app.enabled'),
    width: 90,
    options: [
      { label: () => t('common.enabled'), value: true },
      { label: () => t('common.disabled'), value: false },
    ],
    search: { props: { clearable: true } },
    render: (r) =>
      h(StatusSwitch, {
        value: !!r.enabled,
        disabled: !authStore.hasPerm(r.enabled ? PERM.appDisable : PERM.appEnable) || (!r.enabled && !authStore.isSuperAdmin),
        confirm: (next: boolean) => (next ? null : t('integration.app.disableConfirm', { name: r.name })),
        request: (next: boolean) => integrationAppApi.setEnabled(r.id!, next),
        'onUpdate:value': (v: boolean) => {
          r.enabled = v
        },
      }),
  },
  { key: 'createTime', title: () => t('common.createTime'), width: 170, render: (r) => fmtDateTime(r.createTime) },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 170,
    fixed: 'right',
    hideInSetting: true,
    render: (r) => {
      // 授权抽屉至少要读得到本应用的授权与开放端点清单
      const canOpenGrants = authStore.hasPerm(PERM.grantGet) && authStore.hasPerm(PERM.catalogEndpoints)
      const more = [
        canOpenGrants ? { key: 'grant', label: t('integration.app.grants') } : null,
        authStore.hasPerm(PERM.appUpdate) ? { key: 'edit', label: t('common.edit') } : null,
        authStore.hasPerm(PERM.inboundLog) ? { key: 'logs', label: t('integration.app.callLogs') } : null,
        authStore.hasPerm(PERM.appDelete) ? { key: 'delete', label: t('common.delete') } : null,
      ].filter(Boolean) as { key: string; label: string }[]
      return h(NSpace, { size: 4, wrap: false, wrapItem: false }, () => [
        authStore.hasPerm(PERM.credList)
          ? h(
              NButton,
              { size: 'small', quaternary: true, type: 'primary', 'data-testid': 'integration-credentials', onClick: () => credentialRef.value?.open(r) },
              () => t('integration.app.credentials'),
            )
          : null,
        more.length
          ? h(
              NDropdown,
              { trigger: 'click', options: more, onSelect: (key: string) => onMore(key, r) },
              () => h(NButton, { size: 'small', quaternary: true, 'data-testid': 'integration-more' }, () => [t('common.more'), ' ▾']),
            )
          : null,
      ])
    },
  },
]
</script>

<template>
  <ProTable
    ref="tableRef"
    :columns="columns"
    :fetcher="integrationAppApi.page"
    storage-key="integration-app"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="PERM.appAdd" type="primary" @click="formRef?.openAdd()">
        <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
      </n-button>
      <n-button v-auth="PERM.openApiDoc" :loading="downloading" @click="downloadDoc">
        <template #icon><AppIcon icon="ph:file-arrow-down" :size="16" /></template>{{ t('integration.app.downloadDoc') }}
      </n-button>
    </template>
    <template #empty>{{ t('integration.app.empty') }}</template>
  </ProTable>

  <AppFormModal ref="formRef" @saved="refresh" />
  <CredentialDrawer ref="credentialRef" @changed="refresh" />
  <GrantDrawer ref="grantRef" @saved="refresh" />
</template>

<style scoped>
.app-code {
  font-family: var(--font-family-mono, monospace);
}
</style>
