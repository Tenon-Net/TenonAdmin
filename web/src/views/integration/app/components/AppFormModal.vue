<script setup lang="ts">
// 接入应用新增/编辑。编码创建后不可改(调用记录与排障的稳定锚点);启停走列表行内开关的独立接口。
import { reactive, ref } from 'vue'
import { NForm, NFormItem, NInput, NInputNumber, NSwitch, useMessage, type FormInst, type FormRules } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import FormContainer from '@/components/FormContainer/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import { integrationAppApi } from '@/api/integration'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import type { IntegrationAppRow } from '@/types/integration'

const emit = defineEmits<{ saved: [] }>()
const { t } = useI18n()
const message = useMessage()
const authStore = useAuthStore()

const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | string | null>(null)

interface AppForm {
  code: string
  name: string
  description: string
  ownerOrgId: number | null
  rateLimitPerMinute: number | null
  enabled: boolean
}
const blank = (): AppForm => ({ code: '', name: '', description: '', ownerOrgId: null, rateLimitPerMinute: null, enabled: authStore.isSuperAdmin })
const form = reactive<AppForm>(blank())

const rules: FormRules = {
  code: [
    { required: true, whitespace: true, message: () => t('integration.app.codeRequired'), trigger: ['input', 'blur'] },
    { pattern: /^[A-Za-z0-9][A-Za-z0-9._-]{1,63}$/, message: () => t('integration.app.codeHint'), trigger: ['input', 'blur'] },
  ],
  name: { required: true, whitespace: true, message: () => t('integration.app.nameRequired'), trigger: ['input', 'blur'] },
}

function openAdd() {
  editingId.value = null
  Object.assign(form, blank())
  show.value = true
}

function openEdit(r: IntegrationAppRow) {
  editingId.value = r.id ?? null
  Object.assign(form, {
    code: r.code ?? '',
    name: r.name ?? '',
    description: r.description ?? '',
    ownerOrgId: r.ownerOrgId == null ? null : Number(r.ownerOrgId),
    rateLimitPerMinute: r.rateLimitPerMinute == null ? null : Number(r.rateLimitPerMinute),
    enabled: !!r.enabled,
  })
  show.value = true
}
defineExpose({ openAdd, openEdit })

async function save() {
  await formRef.value?.validate()
  const common = {
    name: form.name.trim(),
    description: form.description.trim() || null,
    ownerOrgId: form.ownerOrgId,
    rateLimitPerMinute: form.rateLimitPerMinute,
  }
  try {
    if (editingId.value === null) await integrationAppApi.add({ ...common, code: form.code.trim(), enabled: authStore.isSuperAdmin && form.enabled })
    else await integrationAppApi.update(editingId.value, common)
    message.success(t('integration.app.saved'))
    emit('saved')
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
</script>

<template>
  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('integration.app.add') : t('integration.app.edit')"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="110">
      <n-form-item :label="t('integration.app.code')" path="code">
        <n-input v-model:value="form.code" :disabled="editingId !== null" :placeholder="t('integration.app.codeHint')" />
      </n-form-item>
      <n-form-item :label="t('integration.app.name')" path="name">
        <n-input v-model:value="form.name" :maxlength="64" />
      </n-form-item>
      <n-form-item :label="t('integration.app.description')" path="description">
        <n-input v-model:value="form.description" type="textarea" :maxlength="256" :autosize="{ minRows: 2, maxRows: 4 }" />
      </n-form-item>
      <n-form-item :label="t('integration.app.ownerOrg')" path="ownerOrgId">
        <OrgTreeSelect v-model:value="form.ownerOrgId" :disabled="!authStore.isSuperAdmin" :placeholder="t('integration.app.ownerOrgHint')" />
      </n-form-item>
      <n-form-item :label="t('integration.app.rateLimit')" path="rateLimitPerMinute">
        <n-input-number
          v-model:value="form.rateLimitPerMinute"
          :min="0"
          :max="1000000"
          clearable
          :placeholder="t('integration.app.rateLimitHint')"
          style="width: 100%"
        />
      </n-form-item>
      <n-form-item v-if="editingId === null && authStore.isSuperAdmin" :label="t('integration.app.enabled')" path="enabled">
        <n-switch v-model:value="form.enabled" />
      </n-form-item>
    </n-form>
  </FormContainer>
</template>
