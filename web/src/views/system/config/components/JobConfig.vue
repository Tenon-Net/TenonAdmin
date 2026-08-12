<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { NForm, NFormItem, NInputNumber, NInput, NButton, NSpin, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import { configApi } from '@/api'
import { translateError } from '@/utils/error'

const KEY_LOG_RETENTION = 'sys.job.logRetentionDays'
const KEY_ALERT_EMAILS = 'sys.job.alertEmails'

const { t } = useI18n()
const message = useMessage()

const logRetentionDays = ref(30)
const alertEmails = ref('')
const loading = ref(true)
const saving = ref(false)

onMounted(async () => {
  try {
    const rows = await configApi.listByGroup('job')
    const map = new Map(rows.map((r) => [r.configKey, r.configValue ?? '']))
    logRetentionDays.value = Number(map.get(KEY_LOG_RETENTION)) || 30
    alertEmails.value = map.get(KEY_ALERT_EMAILS) ?? ''
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
})

async function save() {
  saving.value = true
  try {
    await configApi.saveBatch([
      { configKey: KEY_LOG_RETENTION, configValue: String(logRetentionDays.value) },
      { configKey: KEY_ALERT_EMAILS, configValue: alertEmails.value },
    ])
    message.success(t('config.saved'))
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <n-spin :show="loading">
    <n-form label-placement="left" :label-width="150" style="max-width: 560px">
      <n-form-item :label="t('config.job.logRetentionDays')">
        <n-input-number v-model:value="logRetentionDays" :min="1" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="t('config.job.alertEmails')">
        <n-input
          v-model:value="alertEmails"
          type="textarea"
          :autosize="{ minRows: 2 }"
          :placeholder="t('config.job.alertEmailsHint')"
        />
      </n-form-item>
      <n-form-item :label="' '" :show-feedback="false">
        <n-button v-auth="'PUT:/api/v1/sys/config/batch'" type="primary" :loading="saving" @click="save">
          <template #icon><AppIcon icon="ph:floppy-disk" :size="16" /></template>{{ t('common.save') }}
        </n-button>
      </n-form-item>
    </n-form>
  </n-spin>
</template>
