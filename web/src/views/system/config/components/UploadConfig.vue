<script setup lang="ts">
// 上传策略 = 结构化表单:大小上限 + 后缀白名单(GroupCode='upload')。
// 后端 FileService.UploadAsync 读这两键强制执行,改值即时生效、无需重发。后缀以逗号分隔字符串落库。
import { onMounted, ref } from 'vue'
import { NForm, NFormItem, NInputNumber, NDynamicTags, NButton, NSpin, NText, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import { configApi } from '@/api'
import { translateError } from '@/utils/error'

const KEY_MAX_SIZE = 'sys.upload.maxSizeMb'
const KEY_ALLOWED = 'sys.upload.allowedExtensions'

const { t } = useI18n()
const message = useMessage()

const maxSizeMb = ref(20)
const exts = ref<string[]>([])
const loading = ref(true)
const saving = ref(false)

onMounted(async () => {
  try {
    const rows = await configApi.listByGroup('upload')
    const map = new Map(rows.map((r) => [r.configKey, r.configValue ?? '']))
    maxSizeMb.value = Number(map.get(KEY_MAX_SIZE)) || 20
    exts.value = (map.get(KEY_ALLOWED) ?? '').split(',').map((e) => e.trim()).filter(Boolean)
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
})

// 后缀规范化:补前导点、转小写(与后端 ParseExts 对齐),避免存 "JPG" / "jpg" 之类脏值
function normalizeExt(v: string): string {
  const s = v.trim().toLowerCase()
  return s.startsWith('.') ? s : '.' + s
}

async function save() {
  saving.value = true
  try {
    await configApi.saveBatch([
      { configKey: KEY_MAX_SIZE, configValue: String(maxSizeMb.value) },
      { configKey: KEY_ALLOWED, configValue: exts.value.map(normalizeExt).join(',') },
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
      <n-form-item :label="t('config.upload.maxSizeMb')">
        <n-input-number v-model:value="maxSizeMb" :min="1" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="t('config.upload.allowedExtensions')">
        <n-dynamic-tags v-model:value="exts" :input-props="{ placeholder: t('config.upload.extPlaceholder') }" />
      </n-form-item>
      <n-form-item :label="' '" :show-feedback="false">
        <n-text depth="3" style="font-size: 12px">{{ t('config.upload.extTip') }}</n-text>
      </n-form-item>
      <n-form-item :label="' '" :show-feedback="false">
        <n-button v-auth="'PUT:/api/v1/sys/config/batch'" type="primary" :loading="saving" @click="save">
          <template #icon><AppIcon icon="ph:floppy-disk" :size="16" /></template>{{ t('common.save') }}
        </n-button>
      </n-form-item>
    </n-form>
  </n-spin>
</template>
