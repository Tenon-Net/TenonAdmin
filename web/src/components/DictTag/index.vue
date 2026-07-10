<script setup lang="ts">
// 字典值 → 标签(表格列翻译)。SysDictItem 暂无样式字段,配色 fallback 约定:
// 启用项按 sort 升序取下标 i%4 映射固定语义色序(info/success/warning/error),稳定性由
// 后端 sort 保证——即后端通过排序间接控制颜色;后端加 tagType 字段后改读字段并废弃本约定。
// 显式 typeMap 优先;value 未命中字典/未加载完 → 渲染原始值文本,不空白不闪烁。
import { computed, watch } from 'vue'
import { NTag } from 'naive-ui'
import { useDictStore } from '@/stores/dict'

type TagType = 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error'
const FALLBACK: TagType[] = ['info', 'success', 'warning', 'error']

const props = defineProps<{
  typeCode: string
  value?: string | null
  /** 显式 value→type 映射,优先于 fallback 约定 */
  typeMap?: Record<string, TagType>
}>()

const store = useDictStore()
// typeCode 可变 → watch 而非一次性加载;失败静默(容错渲染原始值),typeCode 变化/重挂载自动重试。
watch(
  () => props.typeCode,
  (code) => {
    void store.load(code).catch(() => {})
  },
  { immediate: true },
)

const hit = computed(() => {
  const items = (store.cache[props.typeCode] ?? []).filter((i) => i.enabled)
  const idx = items.findIndex((i) => i.value === props.value)
  return idx >= 0 ? { label: items[idx].label, index: idx } : null
})
const label = computed(() => hit.value?.label ?? props.value ?? '')
const type = computed<TagType>(() => {
  const mapped = props.value != null ? props.typeMap?.[props.value] : undefined
  if (mapped) return mapped
  return hit.value ? FALLBACK[hit.value.index % FALLBACK.length] : 'default'
})
</script>

<template>
  <!-- 空值按页面惯例渲染「—」,不渲染空标签 -->
  <n-tag v-if="value != null && value !== ''" :type="type" size="small" :bordered="false">{{ label }}</n-tag>
  <span v-else>—</span>
</template>
