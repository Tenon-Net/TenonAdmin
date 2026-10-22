<script setup lang="ts">
import { computed, h } from 'vue'
import { NDataTable, NInput, NTag, NTooltip, type DataTableColumns } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import DictSelect from '@/components/DictSelect/index.vue'
import { translateError } from '@/utils/error'
import { hardErrorsOf as hardErrors, isHardError } from '@/utils/importDup'
import type { CellError, DuplicateStrategy, ImportColumn, ImportRow } from '@/types/api'

const props = defineProps<{
  columns: ImportColumn[]
  rows: ImportRow[]
  strategy: DuplicateStrategy
  onlyErrors: boolean
  busy: boolean
}>()
const emit = defineEmits<{ edit: [index: number, key: string, value: string | null] }>()
const { t } = useI18n()
const isHard = (error: CellError) => isHardError(error, props.strategy)
const hardErrorsOf = (row: ImportRow) => hardErrors(row, props.strategy)

function cellError(row: ImportRow, key: string) {
  return row.errors.find((e) => e.columnKey === key)
}

function setCell(row: ImportRow, key: string, value: string | null) {
  if (!props.busy) emit('edit', row.index, key, value)
}

const displayRows = computed(() =>
  props.onlyErrors ? props.rows.filter((r) => hardErrorsOf(r).length > 0) : props.rows,
)

const previewColumns = computed<DataTableColumns<ImportRow>>(() => {
  const cols: DataTableColumns<ImportRow> = [
    {
      title: '#',
      key: '_index',
      width: 56,
      fixed: 'left',
      render: (r) => r.index,
    },
  ]
  for (const col of props.columns) {
    cols.push({
      title: () => (col.required ? `${col.title} *` : col.title),
      key: col.key,
      minWidth: 120,
      ellipsis: { tooltip: false },
      render: (row) => {
        const err = cellError(row, col.key)
        // 「已存在」在跳过/覆盖策略下不算错:走警示底色,输入框也不置 error 态
        const soft = err !== undefined && !isHard(err)
        const val = row.cells[col.key] ?? null
        const editor = col.dictTypeCode
          ? h(DictSelect, {
              typeCode: col.dictTypeCode,
              disabled: props.busy,
              value: val,
              clearable: !col.required,
              size: 'small',
              // 弹层不跟随触发器宽度:向导的列很窄(minWidth 120,实测触发器约 66px),
              // 跟随会把「启用」「停用」截成「启..」「停..」。普通表单里下拉够宽,
              // 跟随反而更整齐 —— 所以只在向导关掉,不动共用的 DictSelect。
              consistentMenuWidth: false,
              style: { width: '100%' },
              status: err && !soft ? 'error' : undefined,
              'onUpdate:value': (v: string | null) => setCell(row, col.key, v),
            })
          : h(NInput, {
              value: val ?? '',
              size: 'small',
              disabled: props.busy,
              status: err && !soft ? 'error' : undefined,
              onUpdateValue: (v: string) => setCell(row, col.key, v),
            })
        const cell = h(
          'div',
          {
            class: !err
              ? 'import-cell'
              : soft
                ? 'import-cell import-cell--dup'
                : 'import-cell import-cell--error',
          },
          [editor],
        )
        if (!err) return cell
        return h(
          NTooltip,
          { trigger: 'hover' },
          {
            trigger: () => cell,
            default: () => (soft ? t(`import.dupHint.${props.strategy}`) : translateError(err.code)),
          },
        )
      },
    })
  }
  cols.push({
    title: () => t('import.errors'),
    key: '_errors',
    width: 160,
    fixed: 'right',
    render: (r) => {
      if (r.errors.length === 0) return h(NTag, { size: 'small', type: 'success', bordered: false }, () => t('import.ok'))
      const hard = hardErrorsOf(r)
      // 只剩「已存在」:不是要用户改的错,给警示标签 +「将按策略处理」而非红色错误数
      if (hard.length === 0) {
        return h(
          NTooltip,
          { trigger: 'hover' },
          {
            trigger: () =>
              h(NTag, { size: 'small', type: 'warning', bordered: false }, () => t('import.dupTag')),
            default: () => t(`import.dupHint.${props.strategy}`),
          },
        )
      }
      return h(
        NTooltip,
        { trigger: 'hover' },
        {
          trigger: () =>
            h(NTag, { size: 'small', type: 'error', bordered: false }, () =>
              t('import.errorCount', { n: hard.length }),
            ),
          default: () => hard.map((e) => `${e.columnKey}: ${translateError(e.code)}`).join('\n'),
        },
      )
    },
  })
  return cols
})

</script>

<template>
  <NDataTable
    size="small"
    :columns="previewColumns"
    :data="displayRows"
    :row-key="(row: ImportRow) => row.index"
    :max-height="420"
    :scroll-x="Math.max(800, columns.length * 140 + 220)"
    :bordered="true"
    :single-line="false"
  />
</template>

<style scoped>
:deep(.import-cell) {
  padding: 2px;
  border-radius: 4px;
}
:deep(.import-cell--error) {
  /* 用仓库自己的语义令牌(亮/暗都有定义)。别写 --n-error-color:那是 naive 组件内部的变量,
     在这层普通 div 上未定义,整条 color-mix() 会被判无效丢弃 —— 红底静默不渲染,而 tsc/lint/build 全绿。 */
  background: var(--color-danger-bg);
}
:deep(.import-cell--dup) {
  /* 「库里已存在」在跳过/覆盖策略下会被正常处理,用警示色而非错误红——同上,只用语义令牌 */
  background: var(--color-warning-bg);
}
</style>
