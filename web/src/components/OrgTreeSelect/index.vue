<script setup lang="ts">
// 机构树下拉:onMounted 拉 orgApi.list()(平铺)→ buildTree 拼树;value/placeholder/size… 一切经 $attrs 透传 n-tree-select。
// excludeSubtreeOf:编辑机构选上级时剪掉自身子树(防把自己挂到自己后代下成环)——R4 用户页传 null(不剪),R9 机构页传 editingId。
import { computed, onMounted, ref } from 'vue'
import { NTreeSelect } from 'naive-ui'
import { orgApi } from '@/api'
import { buildTree, collectSubtreeIds } from '@/utils/tree'
import type { SysOrg } from '@/types/api'

defineOptions({ inheritAttrs: false })
const props = defineProps<{ excludeSubtreeOf?: number | null }>()

const flat = ref<SysOrg[]>([])
const loading = ref(false)
onMounted(async () => {
  loading.value = true
  try {
    flat.value = await orgApi.list()
  } catch {
    // 静默留空:机构是配角,不打断主流程
  } finally {
    loading.value = false
  }
})

const options = computed(() => {
  const rows =
    props.excludeSubtreeOf == null
      ? flat.value
      : (() => {
          const excluded = collectSubtreeIds(flat.value, props.excludeSubtreeOf!)
          return flat.value.filter((o) => !excluded.has(o.id))
        })()
  return buildTree(rows)
})
</script>

<template>
  <n-tree-select
    :options="options"
    :loading="loading"
    key-field="id"
    label-field="name"
    children-field="children"
    clearable
    v-bind="$attrs"
  />
</template>
