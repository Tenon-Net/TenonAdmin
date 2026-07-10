<script setup lang="ts">
// 字典单选组(按钮样式):typeCode 取数,其余(v-model:value/size/disabled…)经 $attrs
// 透传给 n-radio-group。加载中/失败时渲染空组,数据到达自动补齐。
import { computed, watch } from 'vue'
import { NRadioGroup, NRadioButton } from 'naive-ui'
import { useDictStore } from '@/stores/dict'

defineOptions({ inheritAttrs: false })
const props = defineProps<{ typeCode: string }>()

const store = useDictStore()
// typeCode 可变(级联场景)→ watch 而非一次性加载;失败静默,typeCode 变化/重挂载自动重试。
watch(
  () => props.typeCode,
  (code) => {
    void store.load(code).catch(() => {})
  },
  { immediate: true },
)

const options = computed(() =>
  (store.cache[props.typeCode] ?? []).filter((i) => i.enabled).map((i) => ({ label: i.label, value: i.value })),
)
</script>

<template>
  <n-radio-group v-bind="$attrs">
    <n-radio-button v-for="o in options" :key="o.value" :value="o.value" :label="o.label" />
  </n-radio-group>
</template>
