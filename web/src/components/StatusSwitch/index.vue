<script setup lang="ts">
// 表格行内启停开关:悲观更新——点击不先翻转,请求成功才 emit;失败不 emit = UI 保持原值即回滚,
// 没有「闪一下又弹回」的错误视觉;loading 期间 n-switch 自锁防连点。
// 后端无独立启停端点 → request 由业务传全量 update(行数据转 Input 后改 enabled)。
import { ref } from 'vue'
import { NSwitch, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useConfirm } from '@/composables/useConfirm'
import { translateError } from '@/utils/error'

const props = withDefaults(
  defineProps<{
    value: boolean
    /** 更新请求;resolve 即成功 */
    request: (next: boolean) => Promise<unknown>
    /** 提供则切换前先 dialog 二次确认;函数形态按目标态出文案,返回空值(null/'')则本次跳过确认 */
    confirm?: string | ((next: boolean) => string | null | undefined)
    /** 无权限等场景由业务置灰(行内比移除 DOM 更合适) */
    disabled?: boolean
    size?: 'small' | 'medium'
    /** false = 成功不弹 toast */
    successMsg?: string | false
  }>(),
  { size: 'small' },
)
const emit = defineEmits<{ (e: 'update:value', v: boolean): void }>()

const { t } = useI18n()
const { ask } = useConfirm() // h() 渲染进表格列也在 App.vue Provider 树内,inject 可用
const message = useMessage()
const loading = ref(false)

async function onUpdate(next: boolean) {
  if (props.confirm) {
    const content = typeof props.confirm === 'function' ? props.confirm(next) : props.confirm
    if (content && !(await ask({ content }))) return
  }
  loading.value = true
  try {
    await props.request(next)
    emit('update:value', next)
    if (props.successMsg !== false) message.success(props.successMsg ?? t('common.success'))
  } catch (e) {
    message.error(translateError(e)) // 不 emit → 值从未变过,零成本回滚
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <n-switch :value="value" :loading="loading" :disabled="disabled || loading" :size="size" @update:value="onUpdate" />
</template>
