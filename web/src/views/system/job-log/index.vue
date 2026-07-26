<script setup lang="ts">
// 执行记录(G7)= 只读 ProTable + 详情抽屉 + 终止/清空两个动作。
// 「运行中」没有独立状态字段,判据是 endTime 为空(任务无 Running 态,全靠未闭合记录推导);
// 终止是跨节点写旗标,目标节点最迟 KillPollSeconds 后停——所以文案说"已请求终止"而非"已终止"。
// 详情抽屉里带同 fireInstanceId 的各次尝试列表:走后端的 fireInstanceId 查询参数直取,
// 不按 jobId 拉一页再前端过滤——翻到老触发时那一页里根本没有它的兄弟行,列表会静默缺项。
import { computed, h, ref, watch } from 'vue'
import {
  NButton, NTag, NSelect, NDataTable, NDrawer, NDrawerContent, NDescriptions, NDescriptionsItem,
  useMessage, type DataTableColumns,
} from 'naive-ui'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useAuthStore } from '@/stores/auth'
import { jobApi } from '@/api'
import { translateError } from '@/utils/error'
import { JobFireMode, JobRunStatus, type SysJobLog } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()
const authStore = useAuthStore()
const tableRef = ref<ProTableInst<SysJobLog>>()

// 任务下拉(筛选用):拉一页足量。ponytail: 200 覆盖绝大多数系统,真超再上远程搜索
const jobOptions = ref<{ label: string; value: number }[]>([])
async function loadJobOptions() {
  try {
    const { items } = await jobApi.page({ page: 1, pageSize: 200 })
    jobOptions.value = items.map((j) => ({ label: j.name, value: j.id }))
  } catch {
    // 静默:任务下拉是筛选辅助,拉取失败不打断列表
  }
}
void loadJobOptions()

const fireModeOptions = [
  { label: () => t('job.log.fireModeSchedule'), value: JobFireMode.Schedule, tagType: 'default' as const },
  { label: () => t('job.log.fireModeManual'), value: JobFireMode.Manual, tagType: 'info' as const },
  { label: () => t('job.log.fireModeBackfill'), value: JobFireMode.Misfire, tagType: 'warning' as const },
  { label: () => t('job.log.fireModeMisfireSkip'), value: JobFireMode.MissedSkipped, tagType: 'default' as const },
]
const runStatusOptions = [
  { label: () => t('job.log.running'), value: JobRunStatus.Running, tagType: 'info' as const },
  { label: () => t('job.log.success'), value: JobRunStatus.Success, tagType: 'success' as const },
  { label: () => t('job.log.failed'), value: JobRunStatus.Failed, tagType: 'error' as const },
  { label: () => t('job.log.timeout'), value: JobRunStatus.Timeout, tagType: 'warning' as const },
  { label: () => t('job.log.cancelled'), value: JobRunStatus.Cancelled, tagType: 'default' as const },
  { label: () => t('job.log.skipped'), value: JobRunStatus.Skipped, tagType: 'default' as const },
]

/** 运行中(endTime 空)的行还没有耗时;已结束的显示毫秒。 */
const isAlive = (r: SysJobLog) => r.endTime == null

const columns: ProTableColumn<SysJobLog>[] = [
  {
    key: 'jobId',
    title: () => t('job.log.job'),
    hideInTable: true, // 只作搜索项:表格里已有任务名快照列
    options: jobOptions,
    search: { props: { clearable: true, filterable: true } },
  },
  { key: 'jobName', title: () => t('job.log.job'), minWidth: 140, ellipsis: { tooltip: true } },
  { key: 'fireMode', title: () => t('job.log.fireMode'), width: 100, tag: true, options: fireModeOptions },
  { key: 'scheduledTime', title: () => t('job.log.scheduledTime'), width: 160, format: 'datetime' },
  {
    key: 'startTime',
    title: () => t('job.log.startTime'),
    width: 160,
    format: 'datetime',
    // 时间范围筛选:daterange 回传 [开始, 结束] 日期串,api 层拆成 StartFrom/StartTo 并补秒
    search: { type: 'daterange', key: 'startRange' },
  },
  {
    key: 'elapsedMs',
    title: () => t('job.log.elapsed'),
    width: 110,
    render: (r) =>
      isAlive(r)
        ? h(NTag, { size: 'small', type: 'info', bordered: false }, () => t('job.log.running'))
        : h('span', { class: 'tabular' }, `${r.elapsedMs} ms`),
  },
  { key: 'runStatus', title: () => t('job.log.runStatus'), width: 100, tag: true, search: true, options: runStatusOptions },
  { key: 'retryIndex', title: () => t('job.log.retryIndex'), width: 90, align: 'center', render: (r) => (r.retryIndex > 0 ? r.retryIndex : '—') },
  { key: 'nodeName', title: () => t('job.log.node'), width: 140, ellipsis: { tooltip: true } },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 130,
    fixed: 'right',
    hideInSetting: true,
    render: (r) =>
      h('div', { style: 'display:flex;gap:4px;' }, [
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openDetail(r) }, () => t('common.detail')),
        isAlive(r) && authStore.hasPerm('POST:/api/v1/sys/job/log/{id}/kill')
          ? h(NButton, { size: 'small', quaternary: true, type: 'error', onClick: () => killRun(r) }, () => t('job.log.kill'))
          : null,
      ]),
  },
]

// 从任务页「记录」跳来时带 ?jobId=,自动带上筛选。
// watch 而非 onMounted:页面被 keep-alive,标签开着时再跳一次只换 query,onMounted 不会再触发;
// tableRef 一起纳入监听源,冷启动时实例就绪后补上筛选(immediate 在 setup 期 ref 还是空)。
const route = useRoute()
watch(
  [tableRef, () => route.query.jobId],
  ([inst, jobId]) => {
    if (!inst) return
    // route 是全局的:切到别的页面时 query.jobId 变 undefined,本页被 keep-alive 着的 watcher 照样会响。
    // 不守卫就等于「离开本页 = 清掉筛选并后台发一次没人看的请求」,切回来筛选还没了。
    if (route.path !== '/system/job-log') return
    const next = jobId == null ? undefined : Number(jobId)
    if (inst.params.jobId === next) return
    inst.params.jobId = next
    inst.search()
  },
  { immediate: true },
)

// ── 详情抽屉(行数据直填 + 同 fireInstanceId 的各次尝试) ──
const showDetail = ref(false)
const detailRow = ref<SysJobLog | null>(null)
const attempts = ref<SysJobLog[]>([])
const attemptsLoading = ref(false)

const runStatusTagType = {
  [JobRunStatus.Running]: 'info',
  [JobRunStatus.Success]: 'success',
  [JobRunStatus.Failed]: 'error',
  [JobRunStatus.Timeout]: 'warning',
  [JobRunStatus.Cancelled]: 'default',
  [JobRunStatus.Skipped]: 'default',
} as const
const runStatusText = (s: JobRunStatus) => {
  const key = { 1: 'running', 2: 'success', 3: 'failed', 4: 'timeout', 5: 'cancelled', 6: 'skipped' }[s]
  return key ? t(`job.log.${key}`) : String(s)
}

const attemptColumns: DataTableColumns<SysJobLog> = [
  {
    title: () => t('job.log.retryIndex'),
    key: 'retryIndex',
    width: 110,
    render: (r) => (r.retryIndex === 0 ? t('job.log.firstTry') : t('job.log.retryN', { n: r.retryIndex })),
  },
  {
    title: () => t('job.log.runStatus'),
    key: 'runStatus',
    width: 90,
    render: (r) => h(NTag, { size: 'small', bordered: false, type: runStatusTagType[r.runStatus] }, () => runStatusText(r.runStatus)),
  },
  { title: () => t('job.log.startTime'), key: 'startTime', render: (r) => (r.startTime ?? '').slice(0, 19).replace('T', ' ') },
  { title: () => t('job.log.elapsed'), key: 'elapsedMs', width: 100, render: (r) => (isAlive(r) ? '—' : `${r.elapsedMs} ms`) },
]

async function openDetail(r: SysJobLog) {
  detailRow.value = r
  showDetail.value = true
  attempts.value = []
  attemptsLoading.value = true
  try {
    const { items } = await jobApi.logPage({ page: 1, pageSize: 100, fireInstanceId: r.fireInstanceId })
    attempts.value = items.sort((a, b) => a.retryIndex - b.retryIndex)
  } catch {
    // 静默:尝试列表是详情的补充信息,拉不到不挡主体
  } finally {
    attemptsLoading.value = false
  }
}

function killRun(r: SysJobLog) {
  confirm({
    type: 'error',
    content: t('job.log.killConfirm', { name: r.jobName }),
    action: () => jobApi.kill(r.id),
    successMsg: t('job.log.killed'),
  }).then((ok) => {
    if (ok) tableRef.value?.refresh()
  })
}

// ── 清空(弹窗选 beforeDays;0 = 全部) ──
const clearShow = ref(false)
const clearBeforeDays = ref(0)
// computed:切语言时选项文案即时生效
const clearOptions = computed(() => [
  { label: t('job.log.clearAll'), value: 0 },
  ...[7, 30, 90].map((n) => ({ label: t('job.log.beforeDays', { n }), value: n })),
])

async function doClear() {
  try {
    const n = await jobApi.logClear({ beforeDays: clearBeforeDays.value === 0 ? null : clearBeforeDays.value })
    message.success(t('job.log.cleared', { n }))
    await tableRef.value?.refresh()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
</script>

<template>
  <ProTable
    ref="tableRef"
    :columns="columns"
    :fetcher="jobApi.logPage"
    storage-key="sys-job-log"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'POST:/api/v1/sys/job/log/clear'" type="error" secondary @click="clearShow = true">
        <template #icon><AppIcon icon="ph:trash" :size="16" /></template>{{ t('job.log.clear') }}
      </n-button>
    </template>
  </ProTable>

  <!-- 清空:选范围后执行,回报删除行数 -->
  <FormContainer
    v-model:show="clearShow"
    :title="t('job.log.clearTitle')"
    :width="420"
    :on-confirm="doClear"
    :confirm-text="t('common.confirm')"
  >
    <div class="clear-body">
      <n-select v-model:value="clearBeforeDays" :options="clearOptions" />
      <div class="clear-hint">{{ t('job.log.clearHint') }}</div>
    </div>
  </FormContainer>

  <n-drawer v-model:show="showDetail" :width="600" placement="right">
    <n-drawer-content :title="t('common.detail')" closable>
      <template v-if="detailRow">
        <n-descriptions label-placement="left" :column="1" bordered size="small">
          <n-descriptions-item :label="t('job.log.job')">{{ detailRow.jobName }}</n-descriptions-item>
          <n-descriptions-item :label="t('job.log.fireMode')">
            {{
              detailRow.fireMode === JobFireMode.Manual
                ? t('job.log.fireModeManual')
                : detailRow.fireMode === JobFireMode.Misfire
                  ? t('job.log.fireModeBackfill')
                  : detailRow.fireMode === JobFireMode.MissedSkipped
                    ? t('job.log.fireModeMisfireSkip')
                    : t('job.log.fireModeSchedule')
            }}
          </n-descriptions-item>
          <n-descriptions-item :label="t('job.log.runStatus')">
            <n-tag size="small" :bordered="false" :type="runStatusTagType[detailRow.runStatus]">
              {{ runStatusText(detailRow.runStatus) }}
            </n-tag>
          </n-descriptions-item>
          <n-descriptions-item :label="t('job.log.scheduledTime')">{{ (detailRow.scheduledTime ?? '').slice(0, 19).replace('T', ' ') }}</n-descriptions-item>
          <n-descriptions-item :label="t('job.log.startTime')">{{ (detailRow.startTime ?? '').slice(0, 19).replace('T', ' ') }}</n-descriptions-item>
          <n-descriptions-item :label="t('job.log.elapsed')">
            {{ detailRow.endTime == null ? t('job.log.running') : `${detailRow.elapsedMs} ms` }}
          </n-descriptions-item>
          <n-descriptions-item :label="t('job.log.node')">{{ detailRow.nodeName || '—' }}</n-descriptions-item>
          <n-descriptions-item v-if="detailRow.messageText" :label="t('job.log.message')">
            <pre class="log-output">{{ detailRow.messageText }}</pre>
          </n-descriptions-item>
          <n-descriptions-item v-if="detailRow.errorText" :label="t('job.log.error')">
            <pre class="log-output log-error">{{ detailRow.errorText }}</pre>
          </n-descriptions-item>
        </n-descriptions>

        <div class="attempts-title">{{ t('job.log.attempts') }}</div>
        <n-data-table
          size="small"
          :columns="attemptColumns"
          :data="attempts"
          :loading="attemptsLoading"
          :row-key="(r: SysJobLog) => r.id"
          :pagination="false"
        />
      </template>
    </n-drawer-content>
  </n-drawer>
</template>

<style scoped>
.clear-body {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.clear-hint {
  font-size: 12px;
  color: var(--color-text-tertiary);
}
.attempts-title {
  margin: 16px 0 8px;
  font-size: var(--font-size-sm);
  font-weight: 600;
  color: var(--color-text-primary);
}
.log-output {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
  font-family: var(--font-family-mono, ui-monospace, monospace);
  font-size: 12px;
  line-height: 1.5;
}
.log-error {
  color: var(--color-danger);
}
</style>
