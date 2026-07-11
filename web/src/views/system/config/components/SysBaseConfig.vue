<script setup lang="ts">
// 系统基础配置 = 分类配置中心「结构化表单」范式:字段绑定固定 config key,运维不需知道 key。
// 载入走 configApi.listByGroup('sys') 回填,保存走 configApi.saveBatch(仅回写值)。后续加参数=加一个 FIELDS 项 + 一个 n-form-item。
import { onMounted, reactive, ref } from 'vue'
import { NForm, NFormItem, NInput, NButton, NSpin, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import { configApi } from '@/api'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()

// 结构化字段:key 绑定后端配置键(GroupCode='sys'),label 走 i18n。真实生效点见 App.vue 的 document.title。
const FIELDS = [{ key: 'sys.site.title', label: () => t('config.base.siteTitle') }] as const

const values = reactive<Record<string, string>>({})
const loading = ref(true)
const saving = ref(false)

onMounted(async () => {
  try {
    const rows = await configApi.listByGroup('sys')
    for (const f of FIELDS) values[f.key] = rows.find((r) => r.configKey === f.key)?.configValue ?? ''
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
})

async function save() {
  saving.value = true
  try {
    await configApi.saveBatch(FIELDS.map((f) => ({ configKey: f.key, configValue: values[f.key] })))
    message.success(t('config.saved'))
    // 站点标题即时生效:同步浏览器标题(App.vue 启动时也读同一来源)。
    if (values['sys.site.title']) document.title = values['sys.site.title']
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <n-spin :show="loading">
    <n-form label-placement="left" :label-width="90" style="max-width: 560px">
      <n-form-item v-for="f in FIELDS" :key="f.key" :label="f.label()">
        <n-input v-model:value="values[f.key]" :placeholder="f.label()" />
      </n-form-item>
      <n-form-item :label="' '" :show-feedback="false">
        <n-button v-auth="'PUT:/api/v1/sys/config/batch'" type="primary" :loading="saving" @click="save">
          <template #icon><AppIcon icon="ph:floppy-disk" :size="16" /></template>{{ t('common.save') }}
        </n-button>
      </n-form-item>
    </n-form>
  </n-spin>
</template>
