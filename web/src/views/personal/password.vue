<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NCard, NForm, NFormItem, NInput, NButton, NAlert, useMessage, type FormInst, type FormRules } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import PasswordStrength from '@/components/PasswordStrength/index.vue'
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
const formRef = ref<FormInst | null>(null)

// 校验落在表单项上(全站一致),不再用 toast —— toast 飘在角落、不指向出错的那一栏,且会被后续消息挤掉。
// ponytail: 密码复杂度不进 rules。PasswordStrength 已在输入时逐条实时显示策略清单,后端也强制(弱口令返 passwordTooWeak),
// 再抄一份到 rules 就是第三处真源,策略一改三处都要跟。
const rules: FormRules = {
  oldPassword: { required: true, message: () => t('changePassword.oldRequired'), trigger: ['input', 'blur'] },
  newPassword: { required: true, message: () => t('changePassword.newRequired'), trigger: ['input', 'blur'] },
  confirmPassword: {
    required: true,
    // 两次不一致必须自己写 validator:naive 没有内置的"等于另一字段"规则
    validator: (_rule, value: string) =>
      !value ? new Error(t('changePassword.confirmRequired'))
        : value !== model.newPassword ? new Error(t('changePassword.mismatch'))
          : true,
    trigger: ['input', 'blur'],
  },
}

async function submit() {
  // validate() 必须留在 try 之外:校验失败是 reject,落进下面的 catch 会被 translateError 翻成一条
  // 不知所云的错误 toast —— 而红字已经打在表单项上了(参照 system/user/index.vue 的 save())。
  await formRef.value?.validate()

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
    <n-alert v-if="user.userInfo?.mustChangePassword" type="warning" style="margin-bottom: 16px">
      {{ t('changePassword.forcedHint') }}
    </n-alert>
    <n-form ref="formRef" :model="model" :rules="rules" label-placement="top">
      <n-form-item :label="t('changePassword.oldPassword')" path="oldPassword">
        <n-input v-model:value="model.oldPassword" type="password" show-password-on="click" />
      </n-form-item>
      <n-form-item :label="t('changePassword.newPassword')" path="newPassword">
        <n-input v-model:value="model.newPassword" type="password" show-password-on="click" />
      </n-form-item>
      <PasswordStrength :value="model.newPassword" />
      <n-form-item :label="t('changePassword.confirmPassword')" path="confirmPassword">
        <n-input v-model:value="model.confirmPassword" type="password" show-password-on="click" @keyup.enter="submit" />
      </n-form-item>
      <!-- 不再用 :disabled 做第二重必填:必填已经是 rules 的事,禁用按钮反而让人不知道缺什么 -->
      <n-button type="primary" block :loading="saving" @click="submit">{{ t('common.submit') }}</n-button>
    </n-form>
  </n-card>
</template>

