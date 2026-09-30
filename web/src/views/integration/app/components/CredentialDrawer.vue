<script setup lang="ts">
// 凭据管理抽屉:发放 / 轮换(新旧有限并存)/ 调整到期 / 撤销。发放与轮换返回的完整凭据交给 SecretModal 一次性展示。
import { computed, h, reactive, ref } from 'vue'
import {
  NButton, NDataTable, NDatePicker, NDrawer, NDrawerContent, NForm, NFormItem, NInput, NInputNumber,
  NPopconfirm, NRadio, NRadioGroup, NResult, NSpace, NTag, useMessage, type DataTableColumns,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import SecretModal from './SecretModal.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useAuthStore } from '@/stores/auth'
import { integrationAppApi } from '@/api/integration'
import { translateError } from '@/utils/error'
import { CredentialStatus, type IntegrationAppRow, type OpenAppCredential } from '@/types/integration'
import { PERM, credentialStatusTag, fmtDateTime } from '../../shared'

const emit = defineEmits<{ changed: [] }>()
const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()

const show = ref(false)
const app = ref<IntegrationAppRow | null>(null)
const rows = ref<OpenAppCredential[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const secretRef = ref<InstanceType<typeof SecretModal> | null>(null)
let loadSeq = 0

const appId = computed(() => app.value?.id ?? 0)

/** 只认最后一次请求的响应:换了应用或连续刷新时,迟到的旧列表一律丢弃。 */
async function load() {
  const target = app.value
  if (!target?.id) return
  const seq = ++loadSeq
  loading.value = true
  loadError.value = null
  try {
    const list = await integrationAppApi.credentials(target.id)
    if (seq === loadSeq) rows.value = list
  } catch (e) {
    if (seq !== loadSeq) return
    loadError.value = translateError(e)
    message.error(loadError.value)
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}

function open(row: IntegrationAppRow) {
  app.value = row
  rows.value = []
  loadError.value = null
  show.value = true
  void load()
}
defineExpose({ open })

async function afterChange() {
  await load()
  emit('changed')
}

// ── 到期设置:默认有效期 / 指定时刻 / 不过期;发放、轮换、调整到期三个表单共用取值换算与缺日期校验 ──
type ExpiryMode = 'default' | 'date' | 'never'
const toExpiry = (mode: ExpiryMode, at: number | null) => ({
  neverExpires: mode === 'never',
  expiresAt: mode === 'date' && at ? new Date(at).toISOString() : null,
})
/** 选了「指定时间」却没选时刻:提示并返回 true,调用方据此拦下提交。 */
function missingDate(mode: ExpiryMode, at: number | null) {
  if (mode !== 'date' || at) return false
  message.warning(t('integration.credential.expiryRequired'))
  return true
}

// 发放
const showIssue = ref(false)
const issueForm = reactive({ name: '', mode: 'default' as ExpiryMode, at: null as number | null })
function openIssue() {
  Object.assign(issueForm, { name: '', mode: 'default', at: null })
  showIssue.value = true
}
async function doIssue() {
  if (missingDate(issueForm.mode, issueForm.at)) return false
  try {
    const issued = await integrationAppApi.createCredential(appId.value, {
      name: issueForm.name.trim() || null,
      ...toExpiry(issueForm.mode, issueForm.at),
    })
    secretRef.value?.open(issued)
    await afterChange()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

// 轮换:并存窗口留空 = 服务端默认值(null),0 = 旧凭据立即失效,上限由服务端校验(49018)
const showRotate = ref(false)
const rotateTarget = ref<OpenAppCredential | null>(null)
const rotateForm = reactive({ overlapHours: null as number | null, name: '', mode: 'default' as ExpiryMode, at: null as number | null })
function openRotate(r: OpenAppCredential) {
  rotateTarget.value = r
  Object.assign(rotateForm, { overlapHours: null, name: '', mode: 'default', at: null })
  showRotate.value = true
}
async function doRotate() {
  if (!rotateTarget.value?.id) return
  if (missingDate(rotateForm.mode, rotateForm.at)) return false
  try {
    const issued = await integrationAppApi.rotateCredential(appId.value, rotateTarget.value.id, {
      overlapHours: rotateForm.overlapHours,
      name: rotateForm.name.trim() || null,
      ...toExpiry(rotateForm.mode, rotateForm.at),
    })
    message.success(t('integration.credential.rotated'))
    secretRef.value?.open(issued)
    await afterChange()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

// 调整到期
const showExpiry = ref(false)
const expiryTarget = ref<OpenAppCredential | null>(null)
const expiryForm = reactive({ mode: 'date' as Exclude<ExpiryMode, 'default'>, at: null as number | null })
function openExpiry(r: OpenAppCredential) {
  expiryTarget.value = r
  expiryForm.mode = r.expiresAt ? 'date' : 'never'
  expiryForm.at = r.expiresAt ? new Date(r.expiresAt).getTime() : null
  showExpiry.value = true
}
async function doExpiry() {
  if (!expiryTarget.value?.id) return
  if (missingDate(expiryForm.mode, expiryForm.at)) return false
  try {
    await integrationAppApi.setCredentialExpiry(appId.value, expiryTarget.value.id, toExpiry(expiryForm.mode, expiryForm.at))
    message.success(t('integration.credential.expirySaved'))
    await afterChange()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

/** 只禁今天以前的日期(今天可选,同 React 侧);今天已过去的时刻由服务端按 49017 拒绝。 */
const pastDisabled = (ts: number) => ts < new Date().setHours(0, 0, 0, 0)

const columns: DataTableColumns<OpenAppCredential> = [
  {
    title: () => t('integration.credential.keyId'),
    key: 'keyId',
    render: (r) => h('code', { class: 'key-id' }, `tna_${r.keyId}.****`),
  },
  { title: () => t('integration.credential.name'), key: 'name', ellipsis: { tooltip: true }, render: (r) => r.name || '—' },
  {
    title: () => t('integration.credential.status'),
    key: 'status',
    width: 90,
    render: (r) => {
      const tag = credentialStatusTag[Number(r.status)] ?? credentialStatusTag[CredentialStatus.Active]
      return h(NTag, { size: 'small', bordered: false, type: tag.type }, () => t(tag.key))
    },
  },
  { title: () => t('integration.credential.expiresAt'), key: 'expiresAt', width: 160, render: (r) => (r.expiresAt ? fmtDateTime(r.expiresAt) : t('integration.credential.neverExpires')) },
  { title: () => t('integration.credential.lastUsed'), key: 'lastUsedAt', width: 160, render: (r) => fmtDateTime(r.lastUsedAt) },
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 200,
    render: (r) => {
      const active = Number(r.status) === CredentialStatus.Active
      const revoked = Number(r.status) === CredentialStatus.Revoked
      return h(NSpace, { size: 4, wrap: false, wrapItem: false }, () => [
        active && authStore.isSuperAdmin && authStore.hasPerm(PERM.credRotate)
          ? h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openRotate(r) }, () => t('integration.credential.rotate'))
          : null,
        !revoked && authStore.isSuperAdmin && authStore.hasPerm(PERM.credExpiry)
          ? h(NButton, { size: 'small', quaternary: true, onClick: () => openExpiry(r) }, () => t('integration.credential.expiry'))
          : null,
        !revoked && authStore.hasPerm(PERM.credRevoke)
          ? h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => integrationAppApi.revokeCredential(appId.value, r.id!), t('integration.credential.revoked')).then((ok) => {
                    if (ok) void afterChange()
                  }),
              },
              {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('integration.credential.revoke')),
                default: () => t('integration.credential.revokeConfirm'),
              },
            )
          : null,
        revoked && authStore.hasPerm(PERM.credDelete)
          ? h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => integrationAppApi.deleteCredential(appId.value, r.id!), t('integration.credential.deleted')).then((ok) => {
                    if (ok) void afterChange()
                  }),
              },
              {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
                default: () => t('integration.credential.deleteConfirm'),
              },
            )
          : null,
      ])
    },
  },
]
</script>

<template>
  <n-drawer v-model:show="show" :width="820" placement="right">
    <n-drawer-content :title="t('integration.credential.title', { app: app?.name ?? '' })" closable>
      <div class="cred-toolbar">
        <span class="cred-app">{{ app?.code }}</span>
        <n-button v-if="authStore.isSuperAdmin && authStore.hasPerm(PERM.credCreate)" type="primary" size="small" data-testid="integration-issue" @click="openIssue">
          <template #icon><AppIcon icon="ph:key" :size="16" /></template>{{ t('integration.credential.issue') }}
        </n-button>
      </div>
      <n-result v-if="loadError" status="error" size="small" :title="t('integration.credential.loadFailed')" :description="loadError">
        <template #footer>
          <n-button type="primary" @click="load">{{ t('integration.credential.retry') }}</n-button>
        </template>
      </n-result>
      <n-data-table
        v-else
        :columns="columns"
        :data="rows"
        :loading="loading"
        :row-key="(r: OpenAppCredential) => String(r.id)"
        size="small"
        :bordered="false"
      >
        <template #empty>{{ t('integration.credential.empty') }}</template>
      </n-data-table>
    </n-drawer-content>
  </n-drawer>

  <FormContainer v-model:show="showIssue" :title="t('integration.credential.issueTitle')" :on-confirm="doIssue" :confirm-text="t('integration.credential.issue')">
    <n-form :model="issueForm" label-placement="left" :label-width="100">
      <n-form-item :label="t('integration.credential.name')">
        <n-input v-model:value="issueForm.name" :maxlength="64" :placeholder="t('integration.credential.namePlaceholder')" />
      </n-form-item>
      <n-form-item :label="t('integration.credential.expiresAt')">
        <n-space vertical :size="8" style="width: 100%">
          <n-radio-group v-model:value="issueForm.mode">
            <n-radio value="default">{{ t('integration.credential.defaultExpiry') }}</n-radio>
            <n-radio value="date">{{ t('integration.credential.specifyExpiry') }}</n-radio>
            <n-radio value="never">{{ t('integration.credential.neverExpires') }}</n-radio>
          </n-radio-group>
          <n-date-picker v-if="issueForm.mode === 'date'" v-model:value="issueForm.at" type="datetime" :is-date-disabled="pastDisabled" clearable />
        </n-space>
      </n-form-item>
    </n-form>
  </FormContainer>

  <FormContainer v-model:show="showRotate" :title="t('integration.credential.rotateTitle')" :on-confirm="doRotate" :confirm-text="t('integration.credential.rotate')">
    <n-form :model="rotateForm" label-placement="left" :label-width="100">
      <n-form-item :label="t('integration.credential.overlapHours')">
        <n-space vertical :size="4" style="width: 100%">
          <n-input-number
            v-model:value="rotateForm.overlapHours"
            :min="0"
            :placeholder="t('integration.credential.overlapPlaceholder')"
            style="width: 100%"
          />
          <span class="field-hint">{{ t('integration.credential.overlapHint') }}</span>
        </n-space>
      </n-form-item>
      <n-form-item :label="t('integration.credential.name')">
        <n-input v-model:value="rotateForm.name" :maxlength="64" :placeholder="rotateTarget?.name || t('integration.credential.namePlaceholder')" />
      </n-form-item>
      <n-form-item :label="t('integration.credential.expiresAt')">
        <n-space vertical :size="8" style="width: 100%">
          <n-radio-group v-model:value="rotateForm.mode">
            <n-radio value="default">{{ t('integration.credential.defaultExpiry') }}</n-radio>
            <n-radio value="date">{{ t('integration.credential.specifyExpiry') }}</n-radio>
            <n-radio value="never">{{ t('integration.credential.neverExpires') }}</n-radio>
          </n-radio-group>
          <n-date-picker v-if="rotateForm.mode === 'date'" v-model:value="rotateForm.at" type="datetime" :is-date-disabled="pastDisabled" clearable />
        </n-space>
      </n-form-item>
    </n-form>
  </FormContainer>

  <FormContainer v-model:show="showExpiry" :title="t('integration.credential.expiryTitle')" :width="460" :on-confirm="doExpiry" :confirm-text="t('common.save')">
    <n-space vertical :size="8">
      <n-radio-group v-model:value="expiryForm.mode">
        <n-radio value="date">{{ t('integration.credential.specifyExpiry') }}</n-radio>
        <n-radio value="never">{{ t('integration.credential.neverExpires') }}</n-radio>
      </n-radio-group>
      <n-date-picker v-if="expiryForm.mode === 'date'" v-model:value="expiryForm.at" type="datetime" :is-date-disabled="pastDisabled" clearable />
    </n-space>
  </FormContainer>

  <SecretModal ref="secretRef" />
</template>

<style scoped>
.cred-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: var(--space-12, 12px);
}
.cred-app {
  font-family: var(--font-family-mono, monospace);
  color: var(--color-text-secondary);
}
.key-id {
  font-family: var(--font-family-mono, monospace);
  font-size: var(--font-size-sm, 13px);
}
.field-hint {
  font-size: var(--font-size-xs, 12px);
  color: var(--color-text-tertiary);
}
</style>
