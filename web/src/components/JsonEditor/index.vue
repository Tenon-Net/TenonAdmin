<script setup lang="ts">
// JSON 值编辑:textarea + 实时校验 + 一键格式化。零依赖(不引 Monaco/CodeMirror)。
// v-model:value 绑 JSON 文本;非法时红边框 + 原生 parse 错误提示。给「值是 JSON」的配置用。
// 注:通用配置值是自由文本,别无脑套在所有值上——纯字符串会被判非法。仅对约定为 JSON 的字段用。
import { computed, ref, watch } from 'vue'
import { NInput, NButton, NText, NSpace } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'

defineOptions({ inheritAttrs: false })
const { t } = useI18n()
const props = defineProps<{ value?: string | null }>()
const emit = defineEmits<{ 'update:value': [v: string] }>()

const text = computed({ get: () => props.value ?? '', set: (v) => emit('update:value', v) })
const error = ref('')

function check(v: string): boolean {
  if (!v.trim()) {
    error.value = ''
    return true
  }
  try {
    JSON.parse(v)
    error.value = ''
    return true
  } catch (e) {
    error.value = (e as Error).message
    return false
  }
}
function format() {
  try {
    text.value = JSON.stringify(JSON.parse(text.value), null, 2)
    error.value = ''
  } catch (e) {
    error.value = (e as Error).message
  }
}
watch(text, check, { immediate: true })
// 父表单可调用做提交前校验。
defineExpose({ valid: () => check(text.value) })
</script>

<template>
  <n-space vertical :size="4" style="width: 100%">
    <n-input
      v-model:value="text"
      type="textarea"
      :autosize="{ minRows: 4, maxRows: 16 }"
      :status="error ? 'error' : undefined"
      spellcheck="false"
      v-bind="$attrs"
    />
    <n-space justify="space-between" align="center" :wrap="false">
      <n-text v-if="error" type="error" style="font-size: 12px">{{ error }}</n-text>
      <span v-else />
      <n-button size="tiny" tertiary @click="format">
        <template #icon><AppIcon icon="ph:brackets-curly" :size="14" /></template>{{ t('common.format') }}
      </n-button>
    </n-space>
  </n-space>
</template>
