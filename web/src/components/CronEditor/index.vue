<script setup lang="ts">
// 6 段秒级 cron 可视化编辑器(秒/分/时/日/月/周 + 表达式直填)。
// 后端语法为准(TenonAdmin.Core CronExpression):日段 L/L-n/LW/nW、周段 nL/n#m、周 0=周日(7≡0);
// 日与周互斥——一侧受限(非 */?)时另一侧自动落 `?`,否则后端直接拒 47003,与其让用户撞墙不如替他落好。
// 预览走 POST /preview-cron(任何登录用户可用):防抖 400ms,展示归一化结果 + 未来若干次时刻;
// 非法表达式显示后端 47003 文案;everySecondWarning 只提示不拦截(后端同款态度)。
import { computed, reactive, ref, watch, onMounted } from 'vue'
import { NTabs, NTabPane, NRadioGroup, NRadio, NInputNumber, NSelect, NInput, NAlert } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { watchDebounced } from '@vueuse/core'
import { jobApi } from '@/api'
import { translateError } from '@/utils/error'
import type { CronPreviewOutput } from '@/types/api'

const props = withDefaults(
  defineProps<{
    modelValue?: string
    /** 预览条数(透传后端 count,上限 20)。 */
    previewCount?: number
  }>(),
  { modelValue: '', previewCount: 5 },
)
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const { t } = useI18n()

type SegMode =
  | 'every' | 'any' | 'range' | 'step' | 'list'
  | 'last' | 'lastWeekday' | 'nearestWeekday'   // 日段专项:L/L-n · LW · nW
  | 'lastOfWeek' | 'nth'                        // 周段专项:nL · n#m

/** 单段编辑态:各模式的参数并存(切模式不丢已填值),build 时只取当前模式用到的。 */
interface SegState {
  mode: SegMode
  rangeFrom: number
  rangeTo: number
  stepFrom: number
  stepSize: number
  list: number[]
  lastOffset: number
  nearestDay: number
  lastDow: number
  nthDow: number
  nthIndex: number
}

function blankState(mode: SegMode, min: number, max: number): SegState {
  return {
    mode,
    rangeFrom: min, rangeTo: max,
    stepFrom: min, stepSize: 1,
    list: [],
    lastOffset: 0, nearestDay: 1, lastDow: 5, nthDow: 1, nthIndex: 1,
  }
}

// 默认表达式形态 = `0 * * * * ?`(秒指定 0,其余每),避免初始就是"每秒执行"
const states = {
  second: reactive(blankState('list', 0, 59)),
  minute: reactive(blankState('every', 0, 59)),
  hour: reactive(blankState('every', 0, 23)),
  day: reactive(blankState('every', 1, 31)),
  month: reactive(blankState('every', 1, 12)),
  week: reactive(blankState('any', 0, 6)),
}
states.second.list = [0]

const numOptions = (min: number, max: number) =>
  Array.from({ length: max - min + 1 }, (_, i) => ({ label: String(min + i), value: min + i }))

const DOW_KEYS = ['sun', 'mon', 'tue', 'wed', 'thu', 'fri', 'sat'] as const
const dowOptions = computed(() => DOW_KEYS.map((k, i) => ({ label: t(`job.cron.${k}`), value: i })))

interface SegDef {
  key: keyof typeof states
  st: SegState
  min: number
  max: number
  tab: () => string
  listOptions: () => { label: string; value: number }[]
}
const segs: SegDef[] = [
  { key: 'second', st: states.second, min: 0, max: 59, tab: () => t('job.cron.tabSecond'), listOptions: () => numOptions(0, 59) },
  { key: 'minute', st: states.minute, min: 0, max: 59, tab: () => t('job.cron.tabMinute'), listOptions: () => numOptions(0, 59) },
  { key: 'hour', st: states.hour, min: 0, max: 23, tab: () => t('job.cron.tabHour'), listOptions: () => numOptions(0, 23) },
  { key: 'day', st: states.day, min: 1, max: 31, tab: () => t('job.cron.tabDay'), listOptions: () => numOptions(1, 31) },
  { key: 'month', st: states.month, min: 1, max: 12, tab: () => t('job.cron.tabMonth'), listOptions: () => numOptions(1, 12) },
  { key: 'week', st: states.week, min: 0, max: 6, tab: () => t('job.cron.tabWeek'), listOptions: () => dowOptions.value },
]

const activeTab = ref('second')
const expr = ref(props.modelValue ?? '')
/** parse 期间静音各段 watcher,防止"解析回填 → 重建表达式"把用户正敲到一半的原文改写掉。 */
let parsing = false

// ── 段 → token ──
const restricted = (tok: string) => tok !== '*' && tok !== '?'

function build(seg: SegDef): string {
  const st = seg.st
  switch (st.mode) {
    case 'any': return '?'
    case 'range': return `${st.rangeFrom}-${st.rangeTo}`
    case 'step': return `${st.stepFrom}/${st.stepSize}`
    case 'list': return st.list.length ? [...st.list].sort((a, b) => a - b).join(',') : '*'
    case 'last': return st.lastOffset > 0 ? `L-${st.lastOffset}` : 'L'
    case 'lastWeekday': return 'LW'
    case 'nearestWeekday': return `${st.nearestDay}W`
    case 'lastOfWeek': return `${st.lastDow}L`
    case 'nth': return `${st.nthDow}#${st.nthIndex}`
    default: return '*'
  }
}

function syncFromStates(edited?: 'day' | 'week') {
  // 日/周互斥:刚编辑的一侧受限 → 另一侧自动落 ?(mutate 在 parsing 静音下做,防 watcher 递归)
  parsing = true
  try {
    if (edited === 'day' && restricted(build(segs[3])) && states.week.mode !== 'any') states.week.mode = 'any'
    if (edited === 'week' && restricted(build(segs[5])) && states.day.mode !== 'any') states.day.mode = 'any'
  } finally {
    parsing = false
  }
  setExpr(segs.map(build).join(' '))
}

function setExpr(v: string) {
  if (v === expr.value) return
  expr.value = v
  emit('update:modelValue', v)
}

// ── token → 段(尽力解析;解析不动的形态保留原编辑态,表达式本身不丢) ──
const MONTH_NAMES: Record<string, number> = { JAN: 1, FEB: 2, MAR: 3, APR: 4, MAY: 5, JUN: 6, JUL: 7, AUG: 8, SEP: 9, OCT: 10, NOV: 11, DEC: 12 }
const DOW_NAMES: Record<string, number> = { SUN: 0, MON: 1, TUE: 2, WED: 3, THU: 4, FRI: 5, SAT: 6 }

function mapNames(tok: string, table: Record<string, number>): string {
  return tok.toUpperCase().replace(/[A-Z]{3}/g, (m) => (m in table ? String(table[m]) : m))
}

function parseGeneric(tok: string, seg: SegDef) {
  const st = seg.st
  if (tok === '*') { st.mode = 'every'; return }
  if (tok === '?') { st.mode = seg.key === 'day' || seg.key === 'week' ? 'any' : 'every'; return }
  const norm = (n: number) => (seg.key === 'week' ? n % 7 : n)  // 周 7≡0(后端同款)
  let m = /^(\d+)-(\d+)$/.exec(tok)
  if (m) { st.mode = 'range'; st.rangeFrom = norm(+m[1]); st.rangeTo = norm(+m[2]); return }
  m = /^(\d+|\*)\/(\d+)$/.exec(tok)
  if (m) { st.mode = 'step'; st.stepFrom = m[1] === '*' ? seg.min : norm(+m[1]); st.stepSize = +m[2]; return }
  if (/^\d+(,\d+)*$/.test(tok)) { st.mode = 'list'; st.list = tok.split(',').map((s) => norm(+s)); return }
  // 区间/步长里带名字或混写等复杂形态:不强行归类,留在表达式页签编辑
}

function parseDay(tok: string) {
  const st = states.day
  if (tok === 'L') { st.mode = 'last'; st.lastOffset = 0; return }
  if (tok === 'LW') { st.mode = 'lastWeekday'; return }
  let m = /^L-(\d+)$/.exec(tok)
  if (m) { st.mode = 'last'; st.lastOffset = +m[1]; return }
  m = /^(\d+)W$/.exec(tok)
  if (m) { st.mode = 'nearestWeekday'; st.nearestDay = +m[1]; return }
  parseGeneric(tok, segs[3])
}

function parseWeek(tok: string) {
  const st = states.week
  const mapped = mapNames(tok, DOW_NAMES)
  if (mapped === 'L') { st.mode = 'list'; st.list = [6]; return }  // 孤立 L = SAT(Quartz 同款)
  let m = /^(\d+)#(\d+)$/.exec(mapped)
  if (m) { st.mode = 'nth'; st.nthDow = +m[1] % 7; st.nthIndex = +m[2]; return }
  m = /^(\d+)L$/.exec(mapped)
  if (m) { st.mode = 'lastOfWeek'; st.lastDow = +m[1] % 7; return }
  parseGeneric(mapped, segs[5])
}

function parse(raw: string) {
  const tokens = raw.trim().split(/\s+/).filter(Boolean)
  if (tokens.length === 5) tokens.unshift('0')  // 5 段自动升 6 段(与后端一致)
  if (tokens.length !== 6) return
  parsing = true
  try {
    parseGeneric(tokens[0], segs[0])
    parseGeneric(tokens[1], segs[1])
    parseGeneric(tokens[2], segs[2])
    parseDay(tokens[3].toUpperCase())
    parseGeneric(mapNames(tokens[4], MONTH_NAMES), segs[4])
    parseWeek(tokens[5])
  } finally {
    parsing = false
  }
}

// 段编辑 → 重建表达式。必须 sync flush:parse 的静音旗标是同步置回的,异步 watcher 会漏看
for (const seg of segs) {
  watch(seg.st, () => {
    if (parsing) return
    syncFromStates(seg.key === 'day' ? 'day' : seg.key === 'week' ? 'week' : undefined)
  }, { deep: true, flush: 'sync' })
}

// 外部(表单回填/清空)改 modelValue → 同步进来
watch(() => props.modelValue, (v) => {
  const next = v ?? ''
  if (next === expr.value) return
  expr.value = next
  if (next.trim()) parse(next)
})

function onRawInput(v: string) {
  setExpr(v)
  if (v.trim()) parse(v)
}

if (expr.value.trim()) parse(expr.value)

// ── 预览(防抖 400ms;seq 挡乱序响应) ──
const previewData = ref<CronPreviewOutput | null>(null)
const previewError = ref('')
let previewSeq = 0

async function loadPreview(v: string) {
  const seq = ++previewSeq
  if (!v.trim()) {
    previewData.value = null
    previewError.value = ''
    return
  }
  try {
    const out = await jobApi.previewCron({ cron: v, count: props.previewCount })
    if (seq !== previewSeq) return
    previewData.value = out
    previewError.value = ''
  } catch (e) {
    if (seq !== previewSeq) return
    previewData.value = null
    previewError.value = translateError(e)
  }
}

watchDebounced(expr, (v) => void loadPreview(v), { debounce: 400 })
onMounted(() => void loadPreview(expr.value))
</script>

<template>
  <div class="cron-editor">
    <n-tabs v-model:value="activeTab" type="line" size="small">
      <n-tab-pane v-for="seg in segs" :key="seg.key" :name="seg.key" :tab="seg.tab()">
        <n-radio-group :value="seg.st.mode" class="cron-rows" @update:value="(v: SegMode) => (seg.st.mode = v)">
          <div class="cron-row">
            <n-radio value="every">{{ t('job.cron.every') }}</n-radio>
          </div>
          <div v-if="seg.key === 'day' || seg.key === 'week'" class="cron-row">
            <n-radio value="any">{{ t('job.cron.any') }}</n-radio>
          </div>
          <div class="cron-row">
            <n-radio value="range">{{ t('job.cron.range') }}</n-radio>
            <n-input-number v-model:value="seg.st.rangeFrom" :min="seg.min" :max="seg.max" size="small" class="cron-num" :disabled="seg.st.mode !== 'range'" />
            <span class="cron-sep">{{ t('job.cron.to') }}</span>
            <n-input-number v-model:value="seg.st.rangeTo" :min="seg.min" :max="seg.max" size="small" class="cron-num" :disabled="seg.st.mode !== 'range'" />
          </div>
          <div class="cron-row">
            <n-radio value="step">{{ t('job.cron.step') }}</n-radio>
            <span class="cron-sep">{{ t('job.cron.stepFrom') }}</span>
            <n-input-number v-model:value="seg.st.stepFrom" :min="seg.min" :max="seg.max" size="small" class="cron-num" :disabled="seg.st.mode !== 'step'" />
            <span class="cron-sep">{{ t('job.cron.stepEvery') }}</span>
            <n-input-number v-model:value="seg.st.stepSize" :min="1" :max="seg.max" size="small" class="cron-num" :disabled="seg.st.mode !== 'step'" />
          </div>
          <div class="cron-row cron-row-list">
            <n-radio value="list">{{ t('job.cron.list') }}</n-radio>
            <n-select
              :value="seg.st.list"
              multiple
              size="small"
              class="cron-list"
              :options="seg.listOptions()"
              :disabled="seg.st.mode !== 'list'"
              :max-tag-count="8"
              @update:value="(v: number[]) => (seg.st.list = v)"
            />
          </div>
          <!-- 日段专项:L / L-n / nW / LW -->
          <template v-if="seg.key === 'day'">
            <div class="cron-row">
              <n-radio value="last">{{ t('job.cron.lastDay') }}</n-radio>
              <n-input-number v-model:value="seg.st.lastOffset" :min="0" :max="30" size="small" class="cron-num" :disabled="seg.st.mode !== 'last'" />
              <span class="cron-sep">{{ t('job.cron.lastDayHint') }}</span>
            </div>
            <div class="cron-row">
              <n-radio value="nearestWeekday">{{ t('job.cron.nearestWeekday') }}</n-radio>
            <n-input-number v-model:value="seg.st.nearestDay" :min="1" :max="31" size="small" class="cron-num" :disabled="seg.st.mode !== 'nearestWeekday'" />
              <span class="cron-sep">{{ t('job.cron.nearestWeekdayHint') }}</span>
            </div>
            <div class="cron-row">
              <n-radio value="lastWeekday">{{ t('job.cron.lastWeekday') }}</n-radio>
            </div>
          </template>
          <!-- 周段专项:nL / n#m -->
          <template v-if="seg.key === 'week'">
            <div class="cron-row">
              <n-radio value="lastOfWeek">{{ t('job.cron.lastOfWeek') }}</n-radio>
              <n-select
                :value="seg.st.lastDow"
                size="small"
                class="cron-dow"
                :options="dowOptions"
                :disabled="seg.st.mode !== 'lastOfWeek'"
                @update:value="(v: number) => (seg.st.lastDow = v)"
              />
            </div>
            <div class="cron-row">
              <n-radio value="nth">{{ t('job.cron.nth') }}</n-radio>
              <span class="cron-sep">{{ t('job.cron.nthPrefix') }}</span>
              <n-input-number v-model:value="seg.st.nthIndex" :min="1" :max="5" size="small" class="cron-num" :disabled="seg.st.mode !== 'nth'" />
              <span class="cron-sep">{{ t('job.cron.nthInfix') }}</span>
              <n-select
                :value="seg.st.nthDow"
                size="small"
                class="cron-dow"
                :options="dowOptions"
                :disabled="seg.st.mode !== 'nth'"
                @update:value="(v: number) => (seg.st.nthDow = v)"
              />
            </div>
          </template>
        </n-radio-group>
      </n-tab-pane>
      <n-tab-pane name="expr" :tab="t('job.cron.tabExpression')">
        <n-input :value="expr" :placeholder="t('job.cron.expressionPlaceholder')" @update:value="onRawInput" />
        <div class="cron-hint">{{ t('job.cron.expressionHint') }}</div>
      </n-tab-pane>
    </n-tabs>

    <!-- 预览区:归一化 + 未来 N 次;非法显示后端 47003 文案 -->
    <div v-if="expr.trim()" class="cron-preview">
      <n-alert v-if="previewData?.everySecondWarning" type="warning" :bordered="false" class="cron-warn">
        {{ t('job.cron.everySecondWarning') }}
      </n-alert>
      <div v-if="previewError" class="cron-preview-error">{{ previewError }}</div>
      <template v-else-if="previewData">
        <div class="cron-preview-row">
          <span class="cron-preview-label">{{ t('job.cron.normalized') }}</span>
          <code class="cron-code">{{ previewData.normalized }}</code>
        </div>
        <div class="cron-preview-row">
          <span class="cron-preview-label">{{ t('job.cron.nextRuns', { n: previewCount }) }}</span>
          <div class="cron-occurrences">
            <template v-if="previewData.occurrences.length">
              <div v-for="(o, i) in previewData.occurrences" :key="i" class="cron-occurrence">{{ o.replace('T', ' ') }}</div>
            </template>
            <span v-else class="cron-empty">{{ t('job.cron.noUpcoming') }}</span>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.cron-editor {
  width: 100%;
}
.cron-rows {
  display: flex;
  flex-direction: column;
  gap: 10px;
  width: 100%;
}
.cron-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.cron-num {
  width: 96px;
}
.cron-dow {
  width: 120px;
}
.cron-list {
  flex: 1;
  min-width: 200px;
}
.cron-sep {
  color: var(--color-text-secondary);
  font-size: var(--font-size-sm);
}
.cron-hint {
  margin-top: 6px;
  font-size: 12px;
  color: var(--color-text-tertiary);
}
.cron-preview {
  margin-top: 12px;
  padding: 10px 12px;
  border-radius: var(--radius-md);
  background: var(--color-fill);
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.cron-warn {
  margin-bottom: 2px;
}
.cron-preview-error {
  color: var(--color-danger);
  font-size: var(--font-size-sm);
}
.cron-preview-row {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}
.cron-preview-label {
  flex: 0 0 auto;
  color: var(--color-text-secondary);
  font-size: var(--font-size-sm);
  line-height: 22px;
}
.cron-code {
  font-family: var(--font-family-mono, ui-monospace, monospace);
  font-size: 13px;
  line-height: 22px;
}
.cron-occurrences {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.cron-occurrence {
  font-family: var(--font-family-mono, ui-monospace, monospace);
  font-size: 13px;
  line-height: 22px;
  font-variant-numeric: tabular-nums;
}
.cron-empty {
  color: var(--color-text-tertiary);
  font-size: var(--font-size-sm);
}
</style>
