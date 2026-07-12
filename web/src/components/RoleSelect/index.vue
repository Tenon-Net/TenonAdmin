<script setup lang="ts">
// 角色选择器:基于 ApiSelect,fetch=roleApi.page(name 搜索)。只列启用角色,label=角色名,
// value=角色 id。多选常用(用户分配角色);multiple/placeholder 等经 $attrs 透传。
import type { SelectOption } from 'naive-ui'
import ApiSelect from '@/components/ApiSelect/index.vue'
import { roleApi } from '@/api'

defineOptions({ inheritAttrs: false })
const props = withDefaults(defineProps<{ pageSize?: number }>(), { pageSize: 100 })

async function fetch(keyword: string): Promise<SelectOption[]> {
  const { items } = await roleApi.page({ page: 1, pageSize: props.pageSize, name: keyword || undefined })
  return items.filter((r) => r.enabled).map((r) => ({ label: r.name, value: r.id }))
}
</script>

<template>
  <ApiSelect :fetch="fetch" v-bind="$attrs" />
</template>
