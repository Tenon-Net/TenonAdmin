<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NCard, NForm, NFormItem, NInput, NButton, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { personalApi, authApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { resetRouter } from '@/router'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const router = useRouter()
const user = useUserStore()
const auth = useAuthStore()

const model = reactive({ oldPassword: '', newPassword: '', confirmPassword: '' })
const saving = ref(false)

async function submit() {
  if (!model.oldPassword || !model.newPassword) {
    message.warning(t('changePassword.oldPassword'))
    return
  }
  if (model.newPassword !== model.confirmPassword) {
    message.error(t('changePassword.mismatch'))
    return
  }
  saving.value = true
  try {
    await personalApi.updatePassword({ oldPassword: model.oldPassword, newPassword: model.newPassword })
    message.success(t('changePassword.changed'))
    // 改密后强制重新登录
    try {
      await authApi.logout()
    } catch { /* 尽力而为 */ }
    resetRouter()
    auth.reset()
    user.clear()
    router.replace('/login')
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <n-card :bordered="true" :title="t('changePassword.title')" style="max-width: 460px">
    <n-form :model="model" label-placement="top">
      <n-form-item :label="t('changePassword.oldPassword')">
        <n-input v-model:value="model.oldPassword" type="password" show-password-on="click" />
      </n-form-item>
      <n-form-item :label="t('changePassword.newPassword')">
        <n-input v-model:value="model.newPassword" type="password" show-password-on="click" />
      </n-form-item>
      <n-form-item :label="t('changePassword.confirmPassword')">
        <n-input v-model:value="model.confirmPassword" type="password" show-password-on="click" @keyup.enter="submit" />
      </n-form-item>
      <n-button type="primary" block :loading="saving" @click="submit">{{ t('common.submit') }}</n-button>
    </n-form>
  </n-card>
</template>
