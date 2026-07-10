<script setup lang="ts">
// 字典下拉:typeCode 取数(经 stores/dict 缓存),其余一切(v-model:value/placeholder/
// clearable/multiple/size…)经 $attrs 原样透传给 n-select,不重复声明。
import { computed, ref, watch } from 'vue'
import { NSelect } from 'naive-ui'
import { useDictStore } from '@/stores/dict'

defineOptions({ inheritAttrs: false })
const props = defineProps<{ typeCode: string }>()

const store = useDictStore()
// typeCode 可变(级联场景)→ watch 而非一次性加载;loading 用本地 ref 而非「缓存是否存在」,
// 否则请求失败(缓存永不写入)会无限转圈。失败停转圈留空,typeCode 变化/重挂载时自动重试。
const loading = ref(false)
watch(
  () => props.typeCode,
  async (code) => {
    loading.value = true
    try {
      await store.load(code)
    } catch {
      // 静默留空:字典是配角,不打断主流程
    } finally {
      if (code === props.typeCode) loading.value = false // 快速切换时只认最新一次
    }
  },
  { immediate: true },
)

// 停用项直接过滤(选项里不该出现不可选值)。
const options = computed(() =>
  (store.cache[props.typeCode] ?? []).filter((i) => i.enabled).map((i) => ({ label: i.label, value: i.value })),
)
</script>

<template>
  <n-select :options="options" :loading="loading" v-bind="$attrs" />
</template>
