<script setup lang="ts">
// Level3 高危写操作再认证弹窗:client 中间件遇 40024 时经 reauthGate 唤起。
import { onMounted, onUnmounted, ref } from 'vue'
import { NForm, NFormItem, NInput, NModal, NRadio, NRadioGroup, NSpace, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { authApi } from '@/api'
import { registerReauthHandler } from '@/api/reauthGate'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()

const show = ref(false)
const submitting = ref(false)
const method = ref<'totp' | 'password'>('totp')
const totpCode = ref('')
const password = ref('')

let settle: ((ok: boolean) => void) | null = null

function openPrompt(): Promise<boolean> {
  return new Promise((resolve) => {
    settle = resolve
    method.value = 'totp'
    totpCode.value = ''
    password.value = ''
    show.value = true
  })
}

function finish(ok: boolean) {
  if (!settle) return
  show.value = false
  submitting.value = false
  const r = settle
  settle = null
  r(ok)
}

/** NModal positive-click:返回 false 保持打开,由 finish 自管关闭。 */
async function onPositive(): Promise<false> {
  if (method.value === 'totp' && !totpCode.value.trim()) {
    message.warning(t('reauth.totpRequired'))
    return false
  }
  if (method.value === 'password' && !password.value) {
    message.warning(t('reauth.passwordRequired'))
    return false
  }
  submitting.value = true
  try {
    await authApi.reauth({
      method: method.value,
      totpCode: method.value === 'totp' ? totpCode.value.trim() : undefined,
      password: method.value === 'password' ? password.value : undefined,
    })
    finish(true)
  } catch (e) {
    message.error(translateError(e))
    submitting.value = false
  }
  return false
}

function cancel() {
  finish(false)
}

onMounted(() => registerReauthHandler(openPrompt))
onUnmounted(() => {
  registerReauthHandler(null)
  if (settle) finish(false)
})
</script>

<template>
  <n-modal
    v-model:show="show"
    preset="dialog"
    :title="t('reauth.title')"
    :positive-text="t('reauth.confirm')"
    :negative-text="t('common.cancel')"
    :loading="submitting"
    :mask-closable="false"
    :close-on-esc="!submitting"
    @positive-click="onPositive"
    @negative-click="cancel"
    @close="cancel"
  >
    <p class="reauth-hint">{{ t('reauth.hint') }}</p>
    <n-form label-placement="left" :label-width="88">
      <n-form-item :label="t('reauth.method')">
        <n-radio-group v-model:value="method">
          <n-space>
            <n-radio value="totp">{{ t('reauth.methodTotp') }}</n-radio>
            <n-radio value="password">{{ t('reauth.methodPassword') }}</n-radio>
          </n-space>
        </n-radio-group>
      </n-form-item>
      <n-form-item v-if="method === 'totp'" :label="t('reauth.totpCode')">
        <n-input
          v-model:value="totpCode"
          :placeholder="t('reauth.totpPlaceholder')"
          maxlength="8"
          @keyup.enter="onPositive"
        />
      </n-form-item>
      <n-form-item v-else :label="t('reauth.password')">
        <n-input
          v-model:value="password"
          type="password"
          show-password-on="click"
          :placeholder="t('reauth.passwordPlaceholder')"
          @keyup.enter="onPositive"
        />
      </n-form-item>
    </n-form>
  </n-modal>
</template>

<style scoped>
.reauth-hint {
  margin: 0 0 12px;
  color: var(--n-text-color-3);
  font-size: 13px;
  line-height: 1.5;
}
</style>
