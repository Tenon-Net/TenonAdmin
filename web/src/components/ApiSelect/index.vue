<script setup lang="ts">
// 通用远程下拉:parent 传 fetch(keyword) 回选项数组(已 {label,value})或 {items};本组件管
// 初次加载 / 远程搜索(防抖)/ loading / 竞态,其余(v-model:value、multiple、clearable、
// placeholder、size…)一切经 $attrs 透传 n-select。UserSelect 皆基于此。
import { onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch, shallowRef } from 'vue'
import { NButton, NSelect, NText, type SelectOption } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { translateError } from '@/utils/error'

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
const loading = shallowRef(false)
const error = shallowRef('')
const emit = defineEmits<{ error: [cause: unknown] }>()
const { t } = useI18n()
let seq = 0 // 快速输入竞态:只认最新一次
let lastKeyword = ''
let requested = false

async function load(keyword = '') {
  requested = true
  lastKeyword = keyword
  const id = ++seq
  loading.value = true
  error.value = ''
  try {
    const res = await props.fetch(keyword)
    if (id !== seq) return
    options.value = Array.isArray(res) ? res : res.items
  } catch (cause) {
    if (id === seq) {
      // 新筛选失败时不保留旧筛选的选项，明确显示错误并允许重试。
      options.value = []
      error.value = translateError(cause)
      emit('error', cause)
    }
  } finally {
    if (id === seq) loading.value = false
  }
}

let timer: ReturnType<typeof setTimeout> | undefined
function invalidate() {
  clearTimeout(timer)
  seq++
}
function onSearch(keyword: string) {
  if (!props.remote) return // 非 remote:过滤交给 n-select filterable
  invalidate()
  lastKeyword = keyword
  requested = true
  error.value = ''
  loading.value = true
  timer = setTimeout(() => load(keyword), props.debounce)
}

onMounted(() => {
  if (props.immediate) load('')
})
watch(
  () => props.reloadOn,
  () => {
    invalidate()
    void load('')
  },
)
let suspended = false
onDeactivated(() => {
  suspended = true
  invalidate()
  loading.value = false
})
onActivated(() => {
  if (suspended) {
    suspended = false
    if (requested) void load(lastKeyword)
  }
})
onBeforeUnmount(invalidate)
</script>

<template>
  <n-select
    :options="options"
    :loading="loading"
    :status="error ? 'error' : undefined"
    filterable
    :remote="remote"
    clearable
    v-bind="$attrs"
    @search="onSearch"
  >
    <template v-if="error" #empty>
      <NText type="error">{{ error }}</NText>
      <NButton text :loading="loading" @click="load(lastKeyword)">{{ t('common.retry') }}</NButton>
    </template>
  </n-select>
</template>
