<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { NCard, NForm, NFormItem, NInput, NButton, NSpin, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { personalApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const user = useUserStore()

const loading = ref(true)
const saving = ref(false)
const model = reactive({ account: '', name: '', org: '', position: '' })

onMounted(async () => {
  try {
    const p = await personalApi.profile()
    model.account = p.account
    model.name = p.name
    // UserProfile 只返回 orgId/positionId、无名称字段,故直接展示 ID。
    model.org = p.orgId ? String(p.orgId) : '—'
    model.position = p.positionId ? String(p.positionId) : '—'
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
})

async function save() {
  saving.value = true
  try {
    await personalApi.updateProfile({ name: model.name })
    if (user.userInfo) user.userInfo.name = model.name
    message.success(t('profile.saved'))
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <n-card :bordered="true" :title="t('profile.title')" style="max-width: 560px">
    <n-spin :show="loading">
      <n-form :model="model" label-placement="left" :label-width="80">
        <n-form-item :label="t('profile.account')">
          <n-input :value="model.account" disabled />
        </n-form-item>
        <n-form-item :label="t('profile.name')">
          <n-input v-model:value="model.name" :placeholder="t('profile.name')" />
        </n-form-item>
        <n-form-item :label="t('profile.org')">
          <n-input :value="model.org" disabled />
        </n-form-item>
        <n-form-item :label="t('profile.position')">
          <n-input :value="model.position" disabled />
        </n-form-item>
        <n-form-item :label="' '">
          <n-button type="primary" :loading="saving" @click="save">{{ t('common.save') }}</n-button>
        </n-form-item>
      </n-form>
    </n-spin>
  </n-card>
</template>
