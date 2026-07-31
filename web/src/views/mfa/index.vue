<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NAlert, NButton, NCard, NForm, NFormItem, NInput, NSteps, NStep, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { mfaApi } from '@/api'
import { translateError } from '@/utils/error'
import { useUserStore } from '@/stores/user'
import { takeRecoveryCodes } from './bindComplete'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const message = useMessage()
const userStore = useUserStore()

const mode = ref<'bind' | 'recovery'>('bind')
const step = ref(1)
const loading = ref(false)
const completed = ref(false)

const queryAccount = typeof route.query.account === 'string' ? route.query.account : ''
const knownAccount = queryAccount || userStore.userInfo?.account || ''
const bind = reactive({
  account: knownAccount,
  currentPassword: '',
  bindChallengeId: '',
  otpauthUri: '',
  seed: '',
  totpCode: '',
  recoveryCodes: [] as string[],
})
const recovery = reactive({
  account: knownAccount,
  currentPassword: '',
  recoveryCode: '',
})
const canStart = computed(() => !!bind.account.trim() && !!bind.currentPassword)

async function copy(value: string) {
  try {
    await navigator.clipboard.writeText(value)
    message.success(t('mfa.copied'))
  } catch {
    message.error(t('mfa.copyFailed'))
  }
}

async function startBind() {
  if (!canStart.value) {
    message.warning(t('mfa.bindRequired'))
    return
  }
  loading.value = true
  try {
    const result = await mfaApi.bindStart({
      account: bind.account.trim(),
      currentPassword: bind.currentPassword,
    })
    bind.bindChallengeId = result.bindChallengeId ?? ''
    bind.otpauthUri = result.otpauthUri ?? ''
    bind.seed = result.seed ?? ''
    if (!bind.bindChallengeId || !bind.otpauthUri || !bind.seed) throw new Error(t('mfa.bindStartResponseIncomplete'))
    bind.currentPassword = ''
    step.value = 2
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

async function completeBind() {
  if (!bind.totpCode) {
    message.warning(t('mfa.codeRequired'))
    return
  }
  loading.value = true
  try {
    const result = await mfaApi.bindComplete({ bindChallengeId: bind.bindChallengeId, totpCode: bind.totpCode })
    const codes = takeRecoveryCodes(result)
    if (!codes) throw new Error(t('mfa.bindCompleteResponseIncomplete'))
    bind.recoveryCodes = codes
    bind.seed = ''
    bind.otpauthUri = ''
    bind.totpCode = ''
    completed.value = true
    step.value = 3
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

async function submitRecovery() {
  if (!recovery.account || !recovery.currentPassword || !recovery.recoveryCode) {
    message.warning(t('mfa.recoveryRequired'))
    return
  }
  loading.value = true
  try {
    await mfaApi.recovery(recovery)
    recovery.currentPassword = ''
    recovery.recoveryCode = ''
    completed.value = true
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

function backToLogin() {
  router.replace('/login')
}

function switchMode() {
  if (mode.value === 'bind' && step.value === 3) {
    bind.recoveryCodes = []
    step.value = 1
  }
  completed.value = false
  mode.value = mode.value === 'bind' ? 'recovery' : 'bind'
}
</script>

<template>
  <main class="mfa-page">
    <n-card class="mfa-card" :bordered="true">
      <template #header>
        <div class="mfa-header">
          <span>{{ mode === 'bind' ? t('mfa.bindTitle') : t('mfa.recoveryTitle') }}</span>
          <n-button text type="primary" @click="switchMode">
            {{ mode === 'bind' ? t('mfa.useRecovery') : t('mfa.backToBind') }}
          </n-button>
        </div>
      </template>

      <template v-if="mode === 'bind'">
        <n-steps :current="step" size="small" style="margin-bottom: 24px">
          <n-step :title="t('mfa.stepVerify')" />
          <n-step :title="t('mfa.stepAuthenticator')" />
          <n-step :title="t('mfa.stepRecovery')" />
        </n-steps>

        <n-form v-if="step === 1" label-placement="top" @keyup.enter="startBind">
          <n-alert type="info" :bordered="false" style="margin-bottom: 16px">{{ t('mfa.bindHint') }}</n-alert>
          <n-form-item :label="t('mfa.account')"><n-input v-model:value="bind.account" autocomplete="username" /></n-form-item>
          <n-form-item :label="t('mfa.currentPassword')"><n-input v-model:value="bind.currentPassword" type="password" show-password-on="click" autocomplete="current-password" /></n-form-item>
          <n-button type="primary" block :loading="loading" @click="startBind">{{ t('mfa.startBind') }}</n-button>
        </n-form>

        <n-form v-else-if="step === 2" label-placement="top" @keyup.enter="completeBind">
          <n-alert type="warning" :bordered="false" style="margin-bottom: 16px">{{ t('mfa.manualSetupHint') }}</n-alert>
          <n-form-item :label="t('mfa.manualKey')">
            <n-input :value="bind.seed" readonly><template #suffix><n-button text type="primary" @click="copy(bind.seed)">{{ t('mfa.copy') }}</n-button></template></n-input>
          </n-form-item>
          <n-form-item :label="t('mfa.otpauthUri')">
            <n-input :value="bind.otpauthUri" type="textarea" :autosize="{ minRows: 2, maxRows: 4 }" readonly><template #suffix><n-button text type="primary" @click="copy(bind.otpauthUri)">{{ t('mfa.copy') }}</n-button></template></n-input>
          </n-form-item>
          <n-form-item :label="t('mfa.authenticatorCode')"><n-input v-model:value="bind.totpCode" :maxlength="6" autocomplete="one-time-code" /></n-form-item>
          <n-button type="primary" block :loading="loading" @click="completeBind">{{ t('mfa.completeBind') }}</n-button>
        </n-form>

        <div v-else class="recovery-codes">
          <n-alert type="warning" :bordered="false" style="margin-bottom: 16px">{{ t('mfa.recoveryOnce') }}</n-alert>
          <div v-for="code in bind.recoveryCodes" :key="code" class="recovery-code">{{ code }}</div>
          <n-button block style="margin-top: 16px" @click="copy(bind.recoveryCodes.join('\n'))">{{ t('mfa.copyAll') }}</n-button>
          <n-button type="primary" block style="margin-top: 8px" @click="backToLogin">{{ t('mfa.backToLogin') }}</n-button>
        </div>
      </template>

      <n-form v-else-if="!completed" label-placement="top" @keyup.enter="submitRecovery">
        <n-alert type="warning" :bordered="false" style="margin-bottom: 16px">{{ t('mfa.recoveryHint') }}</n-alert>
        <n-form-item :label="t('mfa.account')"><n-input v-model:value="recovery.account" autocomplete="username" /></n-form-item>
        <n-form-item :label="t('mfa.currentPassword')"><n-input v-model:value="recovery.currentPassword" type="password" show-password-on="click" autocomplete="current-password" /></n-form-item>
        <n-form-item :label="t('mfa.recoveryCode')"><n-input v-model:value="recovery.recoveryCode" autocomplete="one-time-code" /></n-form-item>
        <n-button type="primary" block :loading="loading" @click="submitRecovery">{{ t('mfa.submitRecovery') }}</n-button>
      </n-form>
      <template v-else>
        <n-alert type="success" :bordered="false">{{ t('mfa.recoveryDone') }}</n-alert>
        <n-button type="primary" block style="margin-top: 16px" @click="backToLogin">{{ t('mfa.backToLogin') }}</n-button>
      </template>
    </n-card>
  </main>
</template>

<style scoped>
.mfa-page { min-height: 100vh; display: grid; place-items: center; padding: 24px; background: var(--color-body, #f5f7fa); }
.mfa-card { width: min(100%, 520px); }
.mfa-header { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.recovery-codes { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; }
.recovery-codes :deep(.n-alert), .recovery-codes :deep(.n-button) { grid-column: 1 / -1; }
.recovery-code { padding: 8px 10px; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; background: var(--color-hover, #f4f4f5); border-radius: 4px; }
@media (max-width: 480px) { .mfa-page { padding: 12px; } .recovery-codes { grid-template-columns: 1fr; } }
</style>
