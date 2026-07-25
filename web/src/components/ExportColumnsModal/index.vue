<script setup lang="ts">
// 导出选列弹窗:列勾选,默认按 DefaultSelected;确认后把选中 Key 列表交给父级发请求。
import { computed, ref, watch } from 'vue'
import { NModal, NCheckboxGroup, NCheckbox, NSpace, NButton, NEmpty } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import type { ExportColumnDef } from '@/types/api'

const show = defineModel<boolean>('show', { default: false })

const props = defineProps<{
  columns: ExportColumnDef[]
  /** 导出中 → 确认钮 loading,并禁止关窗 */
  loading?: boolean
}>()

const emit = defineEmits<{
  /** 用户点确认,载荷 = 勾选的列 Key(顺序与 columns 声明一致) */
  confirm: [keys: string[]]
}>()

const { t } = useI18n()
const checked = ref<string[]>([])

watch(
  () => show.value,
  (v) => {
    if (!v) return
    // 每次打开按 DefaultSelected 播种(缺省 true)
    checked.value = props.columns.filter((c) => c.defaultSelected !== false).map((c) => c.key)
  },
)

const allKeys = computed(() => props.columns.map((c) => c.key))
const allChecked = computed(() => allKeys.value.length > 0 && checked.value.length === allKeys.value.length)
const indeterminate = computed(
  () => checked.value.length > 0 && checked.value.length < allKeys.value.length,
)

function toggleAll(v: boolean) {
  checked.value = v ? [...allKeys.value] : []
}

function onConfirm() {
  if (checked.value.length === 0) return
  // 保持档案声明顺序,不按勾选先后
  const ordered = props.columns.map((c) => c.key).filter((k) => checked.value.includes(k))
  emit('confirm', ordered)
}
</script>

<template>
  <n-modal
    v-model:show="show"
    preset="card"
    :title="t('export.pickColumns')"
    :style="{ width: '420px' }"
    :mask-closable="!loading"
    :closable="!loading"
    :close-on-esc="!loading"
  >
    <n-empty v-if="columns.length === 0" :description="t('common.noData')" />
    <template v-else>
      <div class="export-cols-head">
        <n-checkbox
          :checked="allChecked"
          :indeterminate="indeterminate"
          :disabled="loading"
          @update:checked="toggleAll"
        >
          {{ t('export.selectAll') }}
        </n-checkbox>
      </div>
      <n-checkbox-group v-model:value="checked" :disabled="loading">
        <n-space vertical :size="8">
          <n-checkbox v-for="c in columns" :key="c.key" :value="c.key" :label="c.title" />
        </n-space>
      </n-checkbox-group>
    </template>
    <template #footer>
      <n-space justify="end">
        <n-button :disabled="loading" @click="show = false">{{ t('common.cancel') }}</n-button>
        <n-button
          type="primary"
          :loading="loading"
          :disabled="checked.length === 0"
          @click="onConfirm"
        >
          {{ t('export.confirm') }}
        </n-button>
      </n-space>
    </template>
  </n-modal>
</template>

<style scoped>
.export-cols-head {
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--n-border-color);
}
</style>
