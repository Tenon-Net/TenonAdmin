<script setup lang="ts">
// 字典多选(复选框组):typeCode 取数(经 stores/dict 缓存),其余(v-model:value 数组、
// disabled、size…)经 $attrs 透传 n-checkbox-group。与 DictSelect/DictRadio 同源同范式。
import { computed, watch } from 'vue'
import { NCheckboxGroup, NCheckbox, NSpace } from 'naive-ui'
import { useDictStore } from '@/stores/dict'

defineOptions({ inheritAttrs: false })
const props = defineProps<{ typeCode: string }>()

const store = useDictStore()
// typeCode 可变(级联)→ watch;失败静默,typeCode 变化/重挂载自动重试。
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
  <n-checkbox-group v-bind="$attrs">
    <n-space>
      <n-checkbox v-for="o in options" :key="o.value" :value="o.value" :label="o.label" />
    </n-space>
  </n-checkbox-group>
</template>
