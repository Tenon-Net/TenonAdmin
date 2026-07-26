<script setup lang="ts">
// 四步导入向导(excel-ledger §9 G6):①上传 ②列映射 ③预览改错(裸 n-data-table)④结果。
// API 由父级注入(用户导入走 userApi 六方法),组件对资源无感知,可复用到其他档案。
import { computed, h, ref, watch } from 'vue'
import {
  NModal, NSteps, NStep, NUpload, NUploadDragger, NText, NButton, NSpace, NSelect,
  NDataTable, NInput, NSwitch, NRadioGroup, NRadio, NAlert, NTooltip, NTag, useMessage,
  type DataTableColumns, type UploadFileInfo,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import DictSelect from '@/components/DictSelect/index.vue'
import { translateError } from '@/utils/error'
import { triggerBlobDownload } from '@/utils/download'
import type {
  DuplicateStrategy as DupStrategyNum,
  ImportColumn,
  ImportCommitResult,
  ImportPreview,
  ImportRow,
} from '@/types/api'
import { DuplicateStrategy } from '@/types/api'

export interface ImportWizardApi {
  downloadTemplate: () => Promise<Blob>
  preview: (file: File, mapping?: Record<string, string>) => Promise<ImportPreview>
  validate: (rows: ImportRow[]) => Promise<ImportPreview>
  commit: (rows: ImportRow[], strategy: DupStrategyNum) => Promise<ImportCommitResult>
  errorReport: (rows: ImportRow[]) => Promise<Blob>
}

const show = defineModel<boolean>('show', { default: false })

const props = defineProps<{
  api: ImportWizardApi
  /** 模板下载默认文件名 */
  templateFileName?: string
  /** 错误报告默认文件名 */
  errorReportFileName?: string
}>()

const emit = defineEmits<{
  /** 提交成功(含部分成功)后触发,父级通常 refresh 列表 */
  done: []
}>()

const { t } = useI18n()
const message = useMessage()

// ── 状态 ──
const step = ref(1) // n-steps 1-based
const loading = ref(false)
const file = ref<File | null>(null)
const fileList = ref<UploadFileInfo[]>([])
const headers = ref<string[]>([])
/** 表头 → 列 Key(空串 = 不映射) */
const mapping = ref<Record<string, string>>({})
const columns = ref<ImportColumn[]>([])
const rows = ref<ImportRow[]>([])
const columnErrors = ref<ImportPreview['columnErrors']>([])
const errorRows = ref(0)
const onlyErrors = ref(false)
const strategy = ref<DupStrategyNum>(DuplicateStrategy.Skip)
const commitResult = ref<ImportCommitResult | null>(null)

function reset() {
  step.value = 1
  loading.value = false
  file.value = null
  fileList.value = []
  headers.value = []
  mapping.value = {}
  columns.value = []
  rows.value = []
  columnErrors.value = []
  errorRows.value = 0
  onlyErrors.value = false
  strategy.value = DuplicateStrategy.Skip
  commitResult.value = null
}

watch(show, (v) => {
  if (v) reset()
})

function applyPreview(p: ImportPreview) {
  headers.value = [...p.headers]
  mapping.value = { ...p.mapping }
  columns.value = p.columns
  rows.value = p.rows.map((r) => ({
    index: r.index,
    cells: { ...r.cells },
    errors: [...r.errors],
  }))
  columnErrors.value = p.columnErrors
  errorRows.value = p.errorRows
}

async function onDownloadTemplate() {
  loading.value = true
  try {
    const blob = await props.api.downloadTemplate()
    triggerBlobDownload(blob, props.templateFileName ?? 'import-template.xlsx')
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

function onFileChange(list: UploadFileInfo[]) {
  // 只保留最后选中的一个 xlsx
  fileList.value = list.slice(-1)
  const f = list[list.length - 1]?.file
  file.value = f instanceof File ? f : null
}

function onFileRemove() {
  fileList.value = []
  file.value = null
}

/** ①→②:带文件预览(mapping 空 = 服务端自动匹配) */
async function goMapping() {
  if (!file.value) {
    message.warning(t('import.needFile'))
    return
  }
  loading.value = true
  try {
    const p = await props.api.preview(file.value)
    applyPreview(p)
    // 确保每个表头在 mapping 里有键(未匹配的给空串,便于 n-select 显示「不映射」)
    for (const h of p.headers) {
      if (!(h in mapping.value)) mapping.value[h] = ''
    }
    step.value = 2
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

/** ②→③:按用户调整后的映射重新预览 */
async function goPreview() {
  if (!file.value) return
  // 去掉「不映射」空串,只传有效映射
  const map: Record<string, string> = {}
  for (const [h, k] of Object.entries(mapping.value)) {
    if (k) map[h] = k
  }
  loading.value = true
  try {
    const p = await props.api.preview(file.value, map)
    applyPreview(p)
    step.value = 3
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

const targetColumnOptions = computed(() => [
  { label: t('import.unmap'), value: '' },
  ...columns.value.map((c) => ({
    label: c.required ? `${c.title} *` : c.title,
    value: c.key,
  })),
])

function cellError(row: ImportRow, key: string) {
  return row.errors.find((e) => e.columnKey === key)
}

function setCell(row: ImportRow, key: string, value: string | null) {
  row.cells[key] = value
  // 就地改后清该列旧错误,等重验;否则红底会误导
  row.errors = row.errors.filter((e) => e.columnKey !== key)
}

const displayRows = computed(() =>
  onlyErrors.value ? rows.value.filter((r) => r.errors.length > 0) : rows.value,
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
  for (const col of columns.value) {
    cols.push({
      title: () => (col.required ? `${col.title} *` : col.title),
      key: col.key,
      minWidth: 120,
      ellipsis: { tooltip: false },
      render: (row) => {
        const err = cellError(row, col.key)
        const val = row.cells[col.key] ?? null
        const editor = col.dictTypeCode
          ? h(DictSelect, {
              typeCode: col.dictTypeCode,
              value: val,
              clearable: !col.required,
              size: 'small',
              // 弹层不跟随触发器宽度:向导的列很窄(minWidth 120,实测触发器约 66px),
              // 跟随会把「启用」「停用」截成「启..」「停..」。普通表单里下拉够宽,
              // 跟随反而更整齐 —— 所以只在向导关掉,不动共用的 DictSelect。
              consistentMenuWidth: false,
              style: { width: '100%' },
              status: err ? 'error' : undefined,
              'onUpdate:value': (v: string | null) => setCell(row, col.key, v),
            })
          : h(NInput, {
              value: val ?? '',
              size: 'small',
              status: err ? 'error' : undefined,
              onUpdateValue: (v: string) => setCell(row, col.key, v),
            })
        const cell = h(
          'div',
          {
            class: err ? 'import-cell import-cell--error' : 'import-cell',
          },
          [editor],
        )
        if (!err) return cell
        return h(
          NTooltip,
          { trigger: 'hover' },
          {
            trigger: () => cell,
            default: () => translateError(err.code),
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
      return h(
        NTooltip,
        { trigger: 'hover' },
        {
          trigger: () =>
            h(NTag, { size: 'small', type: 'error', bordered: false }, () =>
              t('import.errorCount', { n: r.errors.length }),
            ),
          default: () =>
            r.errors.map((e) => `${e.columnKey}: ${translateError(e.code)}`).join('\n'),
        },
      )
    },
  })
  return cols
})

async function revalidate() {
  // 变异判据:请求必须带 rows;空数组也是明确失败路径(服务端会返回 0 行)
  loading.value = true
  try {
    const p = await props.api.validate(rows.value)
    // 保留当前列定义(重验不回传 headers/mapping;columns 仍有)
    rows.value = p.rows.map((r) => ({
      index: r.index,
      cells: { ...r.cells },
      errors: [...r.errors],
    }))
    columns.value = p.columns.length ? p.columns : columns.value
    errorRows.value = p.errorRows
    message.success(t('import.revalidated', { errors: p.errorRows, total: p.total }))
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

async function doCommit() {
  loading.value = true
  try {
    const result = await props.api.commit(rows.value, strategy.value)
    commitResult.value = result
    step.value = 4
    if (result.inserted + result.updated > 0) emit('done')
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

/** ④ 失败行接回 ③ 继续改 */
function backToEditFailures() {
  if (!commitResult.value) return
  rows.value = commitResult.value.failures.map((r) => ({
    index: r.index,
    cells: { ...r.cells },
    errors: [...r.errors],
  }))
  errorRows.value = rows.value.filter((r) => r.errors.length > 0).length
  onlyErrors.value = true
  commitResult.value = null
  step.value = 3
}

async function downloadErrorReport() {
  const source =
    step.value === 4 && commitResult.value
      ? commitResult.value.failures
      : rows.value.filter((r) => r.errors.length > 0)
  if (source.length === 0) {
    message.warning(t('import.noErrorRows'))
    return
  }
  loading.value = true
  try {
    const blob = await props.api.errorReport(source)
    triggerBlobDownload(blob, props.errorReportFileName ?? 'import-errors.xlsx')
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

function close() {
  show.value = false
}
</script>

<template>
  <n-modal
    v-model:show="show"
    preset="card"
    :title="t('import.wizardTitle')"
    :style="{ width: 'min(1100px, 96vw)' }"
    :mask-closable="false"
    :closable="!loading"
    :close-on-esc="!loading"
  >
    <n-steps :current="step" size="small" class="import-steps">
      <n-step :title="t('import.stepUpload')" />
      <n-step :title="t('import.stepMapping')" />
      <n-step :title="t('import.stepPreview')" />
      <n-step :title="t('import.stepResult')" />
    </n-steps>

    <!-- ① 上传 -->
    <div v-show="step === 1" class="import-pane">
      <n-space vertical :size="12">
        <n-button secondary :loading="loading" @click="onDownloadTemplate">
          <template #icon><AppIcon icon="ph:download-simple" :size="16" /></template>
          {{ t('import.downloadTemplate') }}
        </n-button>
        <n-upload
          :file-list="fileList"
          :max="1"
          accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
          :default-upload="false"
          @update:file-list="onFileChange"
          @remove="onFileRemove"
        >
          <n-upload-dragger>
            <div class="import-upload-icon">
              <AppIcon icon="ph:file-xls" :size="40" />
            </div>
            <n-text style="font-size: 16px">{{ t('import.dropHint') }}</n-text>
            <n-text depth="3" style="display: block; margin-top: 8px">{{ t('import.dropSub') }}</n-text>
          </n-upload-dragger>
        </n-upload>
      </n-space>
    </div>

    <!-- ② 列映射 -->
    <div v-show="step === 2" class="import-pane">
      <n-alert v-if="columnErrors.length" type="warning" style="margin-bottom: 12px">
        {{
          columnErrors
            .map((e) => `${e.columnKey}: ${translateError(e.code)}`)
            .join('; ')
        }}
      </n-alert>
      <n-text depth="3" style="display: block; margin-bottom: 12px">{{ t('import.mappingHint') }}</n-text>
      <div class="import-mapping">
        <div class="import-mapping-head">
          <span>{{ t('import.fileHeader') }}</span>
          <span>{{ t('import.targetColumn') }}</span>
        </div>
        <div v-for="hdr in headers" :key="hdr" class="import-mapping-row">
          <span class="import-mapping-header" :title="hdr">{{ hdr }}</span>
          <n-select
            v-model:value="mapping[hdr]"
            :options="targetColumnOptions"
            :consistent-menu-width="false"
            size="small"
          />
        </div>
      </div>
    </div>

    <!-- ③ 预览改错(裸 n-data-table,不用 ProTable) -->
    <div v-show="step === 3" class="import-pane">
      <n-space justify="space-between" align="center" style="margin-bottom: 12px" wrap>
        <n-space align="center">
          <n-text>
            {{ t('import.summary', { total: rows.length, errors: errorRows }) }}
          </n-text>
          <n-switch v-model:value="onlyErrors" size="small">
            <template #checked>{{ t('import.onlyErrors') }}</template>
            <template #unchecked>{{ t('import.onlyErrors') }}</template>
          </n-switch>
        </n-space>
        <n-space>
          <n-button size="small" :loading="loading" @click="revalidate">
            <template #icon><AppIcon icon="ph:arrows-clockwise" :size="14" /></template>
            {{ t('import.revalidate') }}
          </n-button>
        </n-space>
      </n-space>
      <n-data-table
        size="small"
        :columns="previewColumns"
        :data="displayRows"
        :row-key="(r: ImportRow) => r.index"
        :max-height="420"
        :scroll-x="Math.max(800, columns.length * 140 + 220)"
        :bordered="true"
        :single-line="false"
      />
      <div class="import-strategy">
        <n-text strong style="margin-right: 12px">{{ t('import.strategy') }}</n-text>
        <n-radio-group v-model:value="strategy" name="dup-strategy">
          <n-space>
            <n-radio :value="DuplicateStrategy.Skip">{{ t('import.strategySkip') }}</n-radio>
            <n-radio :value="DuplicateStrategy.Overwrite">{{ t('import.strategyOverwrite') }}</n-radio>
            <n-radio :value="DuplicateStrategy.Error">{{ t('import.strategyError') }}</n-radio>
          </n-space>
        </n-radio-group>
      </div>
    </div>

    <!-- ④ 结果 -->
    <div v-show="step === 4" class="import-pane">
      <n-alert
        v-if="commitResult"
        :type="commitResult.failed > 0 ? 'warning' : 'success'"
        :title="t('import.resultTitle')"
      >
        <div>{{ t('import.resultTotal', { n: commitResult.total }) }}</div>
        <div>{{ t('import.resultInserted', { n: commitResult.inserted }) }}</div>
        <div>{{ t('import.resultUpdated', { n: commitResult.updated }) }}</div>
        <div>{{ t('import.resultSkipped', { n: commitResult.skipped }) }}</div>
        <div>{{ t('import.resultFailed', { n: commitResult.failed }) }}</div>
      </n-alert>
      <n-space style="margin-top: 16px">
        <n-button
          v-if="commitResult && commitResult.failed > 0"
          type="warning"
          @click="backToEditFailures"
        >
          {{ t('import.editFailures') }}
        </n-button>
        <n-button
          v-if="commitResult && commitResult.failed > 0"
          secondary
          :loading="loading"
          @click="downloadErrorReport"
        >
          <template #icon><AppIcon icon="ph:download-simple" :size="16" /></template>
          {{ t('import.downloadErrorReport') }}
        </n-button>
      </n-space>
    </div>

    <template #footer>
      <n-space justify="end">
        <n-button :disabled="loading" @click="close">
          {{ step === 4 ? t('common.close') : t('common.cancel') }}
        </n-button>
        <n-button
          v-if="step === 3"
          secondary
          :disabled="loading"
          @click="downloadErrorReport"
        >
          {{ t('import.downloadErrorReport') }}
        </n-button>
        <n-button
          v-if="step > 1 && step < 4"
          :disabled="loading"
          @click="step = step - 1"
        >
          {{ t('import.prev') }}
        </n-button>
        <n-button
          v-if="step === 1"
          type="primary"
          :loading="loading"
          :disabled="!file"
          @click="goMapping"
        >
          {{ t('import.next') }}
        </n-button>
        <n-button
          v-if="step === 2"
          type="primary"
          :loading="loading"
          @click="goPreview"
        >
          {{ t('import.next') }}
        </n-button>
        <n-button
          v-if="step === 3"
          type="primary"
          :loading="loading"
          @click="doCommit"
        >
          {{ t('import.commit') }}
        </n-button>
      </n-space>
    </template>
  </n-modal>
</template>

<style scoped>
.import-steps {
  margin-bottom: 20px;
}
.import-pane {
  min-height: 200px;
}
.import-upload-icon {
  margin-bottom: 12px;
  color: var(--n-text-color-3);
}
.import-mapping-head,
.import-mapping-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  align-items: center;
}
.import-mapping-head {
  font-weight: 600;
  font-size: 13px;
  margin-bottom: 8px;
  color: var(--n-text-color-2);
}
.import-mapping-row {
  margin-bottom: 8px;
}
.import-mapping-header {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
}
.import-strategy {
  margin-top: 16px;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
:deep(.import-cell) {
  padding: 2px;
  border-radius: 4px;
}
:deep(.import-cell--error) {
  /* 用仓库自己的语义令牌(亮/暗都有定义)。别写 --n-error-color:那是 naive 组件内部的变量,
     在这层普通 div 上未定义,整条 color-mix() 会被判无效丢弃 —— 红底静默不渲染,而 tsc/lint/build 全绿。 */
  background: var(--color-danger-bg);
}
</style>
