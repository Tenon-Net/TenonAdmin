<script setup lang="ts">
// 通用远程下拉:parent 传 fetch(keyword) 回选项数组(已 {label,value})或 {items};本组件管
// 初次加载 / 远程搜索(防抖)/ loading / 竞态,其余(v-model:value、multiple、clearable、
// placeholder、size…)一切经 $attrs 透传 n-select。UserSelect 皆基于此。
import { onMounted, ref, watch } from 'vue'
import { NSelect, type SelectOption } from 'naive-ui'

defineOptions({ inheritAttrs: false })
const props = withDefaults(
  defineProps<{
    /** 传 keyword,回选项(已归一 {label,value})或 {items}。keyword 空 = 初次/全量拉一页。 */
    fetch: (keyword: string) => Promise<SelectOption[] | { items: SelectOption[] }>
    /** 服务端搜索:true 每次输入调 fetch(remote);false 只加载一次、n-select 客户端过滤。 */
    remote?: boolean
    /** 挂载即加载(remote 下预取首屏)。 */
    immediate?: boolean
    /** 搜索防抖 ms。 */
    debounce?: number
    /** 该值变化时重新加载(如 UserSelect 的 orgId 部门过滤)。 */
    reloadOn?: unknown
  }>(),
  { remote: true, immediate: true, debounce: 300 },
)

const options = ref<SelectOption[]>([])
const loading = ref(false)
let seq = 0 // 快速输入竞态:只认最新一次

async function load(keyword = '') {
  const id = ++seq
  loading.value = true
  try {
    const res = await props.fetch(keyword)
    if (id !== seq) return
    options.value = Array.isArray(res) ? res : res.items
  } catch {
    if (id === seq) options.value = [] // 失败留空,不打断主流程(错误处理留视图层)
  } finally {
    if (id === seq) loading.value = false
  }
}

let timer: ReturnType<typeof setTimeout> | undefined
function onSearch(keyword: string) {
  if (!props.remote) return // 非 remote:过滤交给 n-select filterable
  clearTimeout(timer)
  timer = setTimeout(() => load(keyword), props.debounce)
}

onMounted(() => {
  if (props.immediate) load('')
})
watch(
  () => props.reloadOn,
  () => load(''),
)
</script>

<template>
  <n-select
    :options="options"
    :loading="loading"
    filterable
    :remote="remote"
    clearable
    v-bind="$attrs"
    @search="onSearch"
  />
</template>
