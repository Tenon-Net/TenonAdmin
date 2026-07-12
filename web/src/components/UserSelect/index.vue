<script setup lang="ts">
// 人员选择器:基于 ApiSelect,fetch=userApi.page(name 搜索 + 可选 orgId 部门过滤)。
// 选项 label 为「姓名(账号)」,value 为用户 id。单/多选、placeholder 等一切经 $attrs 透传。
import { computed } from 'vue'
import type { SelectOption } from 'naive-ui'
import ApiSelect from '@/components/ApiSelect/index.vue'
import { userApi } from '@/api'

defineOptions({ inheritAttrs: false })
const props = withDefaults(defineProps<{ orgId?: number | null; pageSize?: number }>(), { pageSize: 50 })

async function fetch(keyword: string): Promise<SelectOption[]> {
  const { items } = await userApi.page({
    page: 1,
    pageSize: props.pageSize,
    name: keyword || undefined,
    orgId: props.orgId ?? undefined,
  })
  return items.map((u) => ({ label: `${u.name}(${u.account})`, value: u.id }))
}
const reloadOn = computed(() => props.orgId) // 部门过滤变化 → 重新拉
</script>

<template>
  <ApiSelect :fetch="fetch" :reload-on="reloadOn" v-bind="$attrs" />
</template>
