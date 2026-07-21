<script setup lang="ts">
// 重置密码弹层 + 初始密码结果只读弹层 —— 从 user/index.vue 抽出。
// 结果弹层被两条路径共用:重置密码后展示新口令,以及新建用户留空口令时展示系统随机口令(父页经 showResult 转交)。
// 语义相同:明文只此一次,管理员当场转达。
import { reactive, ref } from 'vue'
import { NButton, NInput, NForm, NFormItem, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import FormContainer from '@/components/FormContainer/index.vue'
import { userApi } from '@/api'
import { translateError } from '@/utils/error'
import type { UserItem } from '@/types/api'

const { t } = useI18n()
const message = useMessage()

const showReset = ref(false)
const resetTarget = ref<UserItem | null>(null)
const resetForm = reactive({ newPassword: '' })
const showResetResult = ref(false)
const resetResult = ref('')
/** 结果弹层是"建号后"还是"重置后"打开的,只影响标题文案。 */
const resultFromCreate = ref(false)

function openReset(r: UserItem) {
  resetTarget.value = r
  resetForm.newPassword = ''
  showReset.value = true
}
/** 供新建留空口令路径复用同一结果弹层(父页在 UserFormModal 的 passwordGenerated 回调里调用)。 */
function showResult(pwd: string, fromCreate = true) {
  resetResult.value = pwd
  resultFromCreate.value = fromCreate
  showResetResult.value = true
}
defineExpose({ openReset, showResult })

async function doReset() {
  if (!resetTarget.value) return
  try {
    resetResult.value = await userApi.resetPassword(resetTarget.value.id, resetForm.newPassword || null)
    resultFromCreate.value = false
    showResetResult.value = true // 关闭输入弹层、弹出结果
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
async function copyResult() {
  try {
    await navigator.clipboard.writeText(resetResult.value)
    message.success(t('user.copied'))
  } catch {
    message.error(t('user.copyFailed'))
  }
}
</script>

<template>
  <!-- 重置密码输入 -->
  <FormContainer
    v-model:show="showReset"
    :title="t('user.resetPassword')"
    :width="420"
    :on-confirm="doReset"
    :confirm-text="t('common.confirm')"
  >
    <n-form :model="resetForm" label-placement="left" :label-width="90">
      <n-form-item :label="t('user.newPassword')">
        <n-input v-model:value="resetForm.newPassword" type="password" show-password-on="click" :placeholder="t('user.newPasswordHint')" />
      </n-form-item>
    </n-form>
  </FormContainer>

  <!-- 初始密码结果(只读,可复制):建号留空口令 / 重置密码 共用 -->
  <FormContainer
    v-model:show="showResetResult"
    :title="resultFromCreate ? t('user.createDone') : t('user.resetDone')"
    :width="420"
    :on-confirm="() => {}"
    :confirm-text="t('common.confirm')"
  >
    <p class="reset-hint">{{ t('user.resetDoneHint') }}</p>
    <n-input :value="resetResult" readonly>
      <template #suffix>
        <n-button text type="primary" @click="copyResult">{{ t('user.copy') }}</n-button>
      </template>
    </n-input>
  </FormContainer>
</template>

<style scoped>
.reset-hint {
  margin: 0 0 12px;
  font-size: var(--font-size-sm, 13px);
  color: var(--color-text-secondary, #888);
}
</style>
