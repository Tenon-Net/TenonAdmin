<script setup lang="ts">
// 定时任务(G7)= ProTable(列表/搜索/分页)+ FormContainer 四分节表单(基本/触发/载荷/失败处理)。
// 状态列走专用启停端点(非全量 update);Panic 行开关旁加红 tag(悬浮出连败次数),重新启用即清连败恢复调度;
// Completed(一次性已跑完/过生效窗口)没有未来时刻,后端拒绝恢复(47010),故只给只读 tag 不给开关。
// 属性包在表单里是键值对 UI,提交时组装成 properties 对象;HTTP 的 headers 子键值对序列化成 JSON 字符串
// 放 properties.headers——读取时后端把 headers 值掩码成 ********,不改就原样回传即"不改"。
import { h, reactive, ref } from 'vue'
import {
  NButton, NSpace, NTag, NTooltip, NPopconfirm, NForm, NInput, NInputNumber,
  NRadioGroup, NRadio, NSelect, NSwitch, NDatePicker, NDivider,
  NGrid, NFormItemGi, NCollapse, NCollapseItem,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import CronEditor from '@/components/CronEditor/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useAuthStore } from '@/stores/auth'
import { jobApi } from '@/api'
import { translateError } from '@/utils/error'
import {
  JobConcurrencyMode, JobHandlerKind, JobMisfireStrategy, JobStatus, JobTriggerKind,
  type JobInput, type SysJob,
} from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const router = useRouter()
const { run, confirm } = useConfirm()
const authStore = useAuthStore()
const tableRef = ref<ProTableInst<SysJob>>()
const { checkedKeys, hasSelection, run: batchDelete } = useBatchDelete({
  remove: jobApi.batchRemove,
  refresh: () => tableRef.value?.refresh(),
  successMsg: t('job.deleted'),
})

// ── 列 ──
const handlerKindOptions = [
  { label: () => t('job.handler.compiled'), value: JobHandlerKind.Compiled, tagType: 'info' as const },
  { label: () => t('job.handler.http'), value: JobHandlerKind.Http, tagType: 'success' as const },
  { label: () => t('job.handler.sql'), value: JobHandlerKind.Sql, tagType: 'warning' as const },
]
const statusOptions = [
  { label: () => t('job.status.ready'), value: JobStatus.Ready, tagType: 'success' as const },
  { label: () => t('job.status.paused'), value: JobStatus.Paused, tagType: 'default' as const },
  { label: () => t('job.status.completed'), value: JobStatus.Completed, tagType: 'default' as const },
  { label: () => t('job.status.panic'), value: JobStatus.Panic, tagType: 'error' as const },
]

/** 触发描述:cron 原文 /「每 N 秒」/ 一次性时刻。 */
function triggerText(r: SysJob): string {
  switch (r.triggerKind) {
    case JobTriggerKind.Interval:
      return t('job.trigger.everySeconds', { n: r.intervalSeconds ?? 0 })
    case JobTriggerKind.OneShot:
      return (r.oneShotTime ?? '').slice(0, 19).replace('T', ' ') || '—'
    default:
      return r.cronExpression || '—'
  }
}

const statusTagType = { [JobStatus.Ready]: 'success', [JobStatus.Paused]: 'default', [JobStatus.Completed]: 'default', [JobStatus.Panic]: 'error' } as const
const statusText = (s: JobStatus) =>
  t(s === JobStatus.Ready ? 'job.status.ready' : s === JobStatus.Paused ? 'job.status.paused' : s === JobStatus.Completed ? 'job.status.completed' : 'job.status.panic')

function renderStatus(r: SysJob) {
  // Completed 没有未来时刻可恢复(后端 47010),不给开关
  if (r.status === JobStatus.Completed)
    return h(NTag, { size: 'small', bordered: false }, () => statusText(r.status))
  if (!authStore.hasPerm('PUT:/api/v1/sys/job/{id}/enabled'))
    return h(NTag, { size: 'small', bordered: false, type: statusTagType[r.status] }, () => statusText(r.status))
  const sw = h(StatusSwitch, {
    value: r.status === JobStatus.Ready,
    confirm: (next: boolean) => (next ? null : t('job.pauseConfirm', { name: r.name })),
    request: (next: boolean) => jobApi.setEnabled(r.id, next),
    // 启停会重算 nextRunTime / 清连败计数,本地写回拼不出来 → 直接重拉
    'onUpdate:value': () => tableRef.value?.refresh(),
  })
  if (r.status !== JobStatus.Panic) return sw
  return h(NSpace, { size: 6, wrapItem: false, align: 'center' }, () => [
    sw,
    h(NTooltip, null, {
      trigger: () => h(NTag, { size: 'small', bordered: false, type: 'error' }, () => t('job.status.panic')),
      default: () => t('job.panicTooltip', { n: r.consecutiveErrors }),
    }),
  ])
}

const columns: ProTableColumn<SysJob>[] = [
  // 内置任务禁删,批量删除同样不可含内置(后端整批拒绝 47014)
  { type: 'selection', disabled: (r: SysJob) => r.isSystem },
  {
    key: 'name',
    title: () => t('job.name'),
    search: true,
    minWidth: 160,
    render: (r) =>
      h('div', null, [
        h('div', null, r.name),
        h('div', { style: 'font-size:12px;color:var(--color-text-tertiary);' }, r.code),
      ]),
  },
  { key: 'handlerKind', title: () => t('job.handler.kind'), width: 100, tag: true, search: true, options: handlerKindOptions },
  { key: 'trigger', title: () => t('job.trigger.kind'), minWidth: 130, ellipsis: { tooltip: true }, render: (r) => triggerText(r) },
  { key: 'status', title: () => t('common.status'), width: 130, search: true, options: statusOptions, render: renderStatus },
  { key: 'nextRunTime', title: () => t('job.nextRunTime'), width: 160, format: 'datetime' },
  { key: 'lastRunTime', title: () => t('job.lastRunTime'), width: 160, format: 'datetime' },
  {
    key: 'numberOfRuns',
    title: () => t('job.runCount'),
    width: 110,
    align: 'center',
    render: (r) =>
      h('span', { class: 'tabular' }, [
        h('span', null, String(r.numberOfRuns)),
        h('span', { style: 'color:var(--color-text-tertiary);' }, ' / '),
        h('span', { style: r.numberOfErrors > 0 ? 'color:var(--color-danger);' : '' }, String(r.numberOfErrors)),
      ]),
  },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 240,
    fixed: 'right',
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        authStore.hasPerm('PUT:/api/v1/sys/job/{id}')
          ? h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit'))
          : null,
        authStore.hasPerm('POST:/api/v1/sys/job/{id}/run')
          ? h(NButton, { size: 'small', quaternary: true, onClick: () => runOnce(r) }, () => t('job.runOnce'))
          : null,
        // 记录:携 jobId 跳执行记录页(那边 watch query 自动带上筛选)
        h(NButton, {
          size: 'small', quaternary: true,
          onClick: () => router.push({ path: '/system/job-log', query: { jobId: String(r.id) } }),
        }, () => t('job.viewLogs')),
        !authStore.hasPerm('DELETE:/api/v1/sys/job/{id}')
          ? null
          : r.isSystem
            ? h(NTooltip, null, {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error', disabled: true }, () => t('common.delete')),
                default: () => t('job.protectedTip'),
              })
            : h(NPopconfirm, {
                onPositiveClick: () =>
                  run(() => jobApi.remove(r.id), t('job.deleted')).then((ok) => {
                    if (ok) tableRef.value?.refresh()
                  }),
              }, {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
                default: () => t('job.deleteConfirm', { name: r.name }),
              }),
      ]),
  },
]

/** 执行一次:重操作走 dialog 确认(执行在 dialog 挂起期间跑,防连点)。 */
function runOnce(r: SysJob) {
  confirm({
    content: t('job.runConfirm', { name: r.name }),
    action: () => jobApi.run(r.id),
    successMsg: t('job.runStarted'),
  }).then((ok) => {
    if (ok) tableRef.value?.refresh()
  })
}

// ── 新增/编辑表单(四分节:基本/触发/载荷/失败处理) ──
interface KV { key: string; value: string }
interface JobForm {
  code: string
  name: string
  remark: string
  triggerKind: JobTriggerKind
  cronExpression: string
  intervalSeconds: number | null
  oneShotTime: string | null
  startTime: string | null
  endTime: string | null
  misfireStrategy: JobMisfireStrategy
  concurrencyMode: JobConcurrencyMode
  handlerKind: JobHandlerKind
  handlerName: string
  /** 编译类自定义属性包(键值对 UI,提交时装成 properties 对象)。 */
  props: KV[]
  httpUrl: string
  httpMethod: string
  headers: KV[]
  httpBody: string
  successStatuses: string
  sqlText: string
  timeoutSeconds: number
  retryCount: number
  retryIntervalSeconds: number
  failAlertThreshold: number
  alertByNotice: boolean
  alertEmails: string
}

const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const blank = (): JobForm => ({
  code: '', name: '', remark: '',
  // 给个能跑的默认值(每天零点),不留空:CronEditor 的各段控件本来就带默认选中,
  // 留空会让新增表单"看着填好了"却因必填校验存不下,而且预览区在用户动手之前一直不出现。
  triggerKind: JobTriggerKind.Cron, cronExpression: '0 0 0 * * ?',
  intervalSeconds: 60, oneShotTime: null, startTime: null, endTime: null,
  misfireStrategy: JobMisfireStrategy.Skip, concurrencyMode: JobConcurrencyMode.SerialSkip,
  handlerKind: JobHandlerKind.Compiled, handlerName: '', props: [],
  httpUrl: '', httpMethod: 'GET', headers: [], httpBody: '', successStatuses: '',
  sqlText: '',
  timeoutSeconds: 0, retryCount: 0, retryIntervalSeconds: 30, failAlertThreshold: 0,
  alertByNotice: true, alertEmails: '',
})
const form = reactive<JobForm>(blank())

const rules: FormRules = {
  code: { required: true, whitespace: true, message: () => t('job.codeRequired'), trigger: ['input', 'blur'] },
  name: { required: true, whitespace: true, message: () => t('job.nameRequired'), trigger: ['input', 'blur'] },
  // 条件字段的 form-item 随 triggerKind/handlerKind v-if 切换,只有挂着的才参与校验
  cronExpression: { required: true, validator: () => !!form.cronExpression.trim(), message: () => t('job.trigger.cronRequired'), trigger: ['change', 'blur'] },
  intervalSeconds: { required: true, validator: () => form.intervalSeconds != null && form.intervalSeconds >= 5, message: () => t('job.trigger.intervalRequired'), trigger: ['change', 'blur'] },
  oneShotTime: { required: true, validator: () => !!form.oneShotTime, message: () => t('job.trigger.oneShotRequired'), trigger: ['change', 'blur'] },
  handlerName: { required: true, validator: () => !!form.handlerName, message: () => t('job.handler.nameRequired'), trigger: ['change', 'blur'] },
  httpUrl: { required: true, whitespace: true, message: () => t('job.handler.httpUrlRequired'), trigger: ['input', 'blur'] },
  sqlText: { required: true, whitespace: true, message: () => t('job.handler.sqlRequired'), trigger: ['input', 'blur'] },
}

// 已注册编译处理器下拉(GET /handlers);只拉一次,失败静默(下拉空了还能手看后端日志排查)
const handlerOptions = ref<{ label: string; value: string }[]>([])
// SQL 载荷总闸(后端 Jobs:Sql:Enabled)。默认按"开"起手:清单没拉到就不该凭空禁掉一种载荷,
// 真关着的话保存时后端还有 47008 兜底。
const sqlEnabled = ref(true)
let handlersLoaded = false
async function ensureHandlers() {
  if (handlersLoaded) return
  try {
    const out = await jobApi.handlers()
    handlerOptions.value = out.handlers.map((n) => ({ label: n, value: n }))
    sqlEnabled.value = out.sqlEnabled
    handlersLoaded = true
  } catch {
    // 静默:处理器清单是配角,拉取失败不挡表单
  }
}

const httpMethodOptions = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD'].map((m) => ({ label: m, value: m }))

// ── 高级选项折叠 ──
// 收进去的十项全都自带合理默认值、且一条校验规则都没有(rules 里没有它们),
// 所以折叠永远挡不住保存 —— 这正是它与页签的区别:展开是可选的,不是必经的一步。
const ADVANCED_NAME = 'advanced'
const advancedOpen = ref<string[]>([])

/** 该行动过任一高级项?动过就默认展开,免得用户觉得"我配过的东西不见了"。 */
function hasAdvanced(r: SysJob): boolean {
  return !!r.startTime || !!r.endTime
    || r.misfireStrategy !== JobMisfireStrategy.Skip
    || r.concurrencyMode !== JobConcurrencyMode.SerialSkip
    || r.timeoutSeconds > 0 || r.retryCount > 0 || r.retryIntervalSeconds !== 30
    || r.failAlertThreshold > 0 || !r.alertByNotice || !!r.alertEmails
}

/** 后端 date-time 可能带小数秒,砍到秒级正好喂 n-date-picker 的 formatted-value。 */
const toLocal = (s?: string | null) => (s ? s.slice(0, 19) : null)

function openAdd() {
  editingId.value = null
  Object.assign(form, blank())
  advancedOpen.value = []
  show.value = true
  void ensureHandlers()
}

function openEdit(r: SysJob) {
  editingId.value = r.id
  Object.assign(form, blank())
  advancedOpen.value = hasAdvanced(r) ? [ADVANCED_NAME] : []
  let props: Record<string, string | null> = {}
  try {
    props = JSON.parse(r.propsJson || '{}') as Record<string, string | null>
  } catch {
    // 属性包坏 JSON:别让编辑崩,当空包处理
  }
  form.code = r.code
  form.name = r.name
  form.remark = r.remark ?? ''
  form.triggerKind = r.triggerKind
  form.cronExpression = r.cronExpression ?? ''
  form.intervalSeconds = r.intervalSeconds ?? 60
  form.oneShotTime = toLocal(r.oneShotTime)
  form.startTime = toLocal(r.startTime)
  form.endTime = toLocal(r.endTime)
  form.misfireStrategy = r.misfireStrategy
  form.concurrencyMode = r.concurrencyMode
  form.handlerKind = r.handlerKind
  form.timeoutSeconds = r.timeoutSeconds
  form.retryCount = r.retryCount
  form.retryIntervalSeconds = r.retryIntervalSeconds
  form.failAlertThreshold = r.failAlertThreshold
  form.alertByNotice = r.alertByNotice
  form.alertEmails = r.alertEmails ?? ''
  if (r.handlerKind === JobHandlerKind.Http) {
    form.httpUrl = props.url ?? ''
    form.httpMethod = (props.method ?? 'GET').toUpperCase()
    form.httpBody = props.body ?? ''
    form.successStatuses = props.successStatuses ?? ''
    // headers 值已被后端掩码成 ********;原样回传 = 不改,改了哪条就生效哪条
    let headers: Record<string, string> = {}
    try {
      headers = JSON.parse(props.headers || '{}') as Record<string, string>
    } catch {
      // headers 整体被掩码(非 JSON)时无从回填,置空
    }
    form.headers = Object.entries(headers).map(([key, value]) => ({ key, value: value ?? '' }))
  } else if (r.handlerKind === JobHandlerKind.Sql) {
    form.sqlText = props.sql ?? ''
  } else {
    form.handlerName = r.handlerName
    form.props = Object.entries(props).map(([key, value]) => ({ key, value: value ?? '' }))
  }
  show.value = true
  void ensureHandlers()
}

/** 表单态 → JobInput:按载荷类型装 properties;HTTP 的 headers 序列化成 JSON 字符串子键。 */
function buildInput(): JobInput {
  const properties: Record<string, string> = {}
  if (form.handlerKind === JobHandlerKind.Http) {
    properties.url = form.httpUrl.trim()
    properties.method = form.httpMethod
    const headers: Record<string, string> = {}
    for (const kv of form.headers) if (kv.key.trim()) headers[kv.key.trim()] = kv.value
    if (Object.keys(headers).length) properties.headers = JSON.stringify(headers)
    if (form.httpBody.trim()) properties.body = form.httpBody
    if (form.successStatuses.trim()) properties.successStatuses = form.successStatuses.trim()
  } else if (form.handlerKind === JobHandlerKind.Sql) {
    properties.sql = form.sqlText
  } else {
    for (const kv of form.props) if (kv.key.trim()) properties[kv.key.trim()] = kv.value
  }
  return {
    code: form.code.trim(),
    name: form.name.trim(),
    handlerKind: form.handlerKind,
    // HTTP/SQL 的处理器名由服务端固定填内置处理器,这里传空即可
    handlerName: form.handlerKind === JobHandlerKind.Compiled ? form.handlerName : '',
    properties,
    triggerKind: form.triggerKind,
    cronExpression: form.triggerKind === JobTriggerKind.Cron ? form.cronExpression.trim() : null,
    intervalSeconds: form.triggerKind === JobTriggerKind.Interval ? form.intervalSeconds : null,
    oneShotTime: form.triggerKind === JobTriggerKind.OneShot ? form.oneShotTime : null,
    startTime: form.startTime || null,
    endTime: form.endTime || null,
    misfireStrategy: form.misfireStrategy,
    concurrencyMode: form.concurrencyMode,
    timeoutSeconds: form.timeoutSeconds,
    retryCount: form.retryCount,
    retryIntervalSeconds: form.retryIntervalSeconds,
    failAlertThreshold: form.failAlertThreshold,
    alertByNotice: form.alertByNotice,
    alertEmails: form.alertEmails.trim() || null,
    remark: form.remark.trim() || null,
  }
}

async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) await jobApi.add(buildInput())
    else await jobApi.update(editingId.value, buildInput())
    // 文案里带「集群下最长 30 秒后生效」——各节点按 ReloadSeconds 周期重载任务表
    message.success(t('job.saved'))
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
    :fetcher="jobApi.page"
    storage-key="sys-job"
    :checked-row-keys="checkedKeys"
    @update:checked-row-keys="(keys: (string | number)[]) => (checkedKeys = keys)"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'POST:/api/v1/sys/job'" type="primary" @click="openAdd">
        <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
      </n-button>
      <n-button v-auth="'POST:/api/v1/sys/job/batch-delete'" type="error" :disabled="!hasSelection" @click="batchDelete">
        <template #icon><AppIcon icon="ph:trash" :size="16" /></template>{{ t('common.batchDelete') }}
      </n-button>
    </template>
  </ProTable>

  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('job.addTitle') : t('job.editTitle')"
    :width="900"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <!-- 一页到底,不拆页签:页签是"必须点"才能继续填,把一次顺序填写拆成先找页签,
         必填项还会藏在未激活的页签后面。降高改走两列栅格 + 高级项默认折叠。
         两列栅格照 UserFormModal.vue 的写法:成对的短字段一行两个,宽控件 span 2。 -->
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="110">
      <!-- ── 基本 ── -->
      <n-divider title-placement="left" class="job-sec">{{ t('job.form.sectionBasic') }}</n-divider>
      <n-grid cols="1 s:2" responsive="screen" :x-gap="16">
        <n-form-item-gi :label="t('job.code')" path="code">
          <n-input v-model:value="form.code" :placeholder="t('job.codePlaceholder')" :disabled="editingId !== null" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('job.name')" path="name">
          <n-input v-model:value="form.name" :placeholder="t('job.name')" />
        </n-form-item-gi>
        <!-- 无校验规则的项一律 show-feedback=false:那条空的反馈占位每个吃掉 24px,
             四个可见项加起来就是一屏与要滚动的差别 -->
        <n-form-item-gi :span="2" :label="t('job.remark')" :show-feedback="false">
          <n-input v-model:value="form.remark" type="textarea" :autosize="{ minRows: 1, maxRows: 3 }" />
        </n-form-item-gi>
      </n-grid>

      <!-- ── 触发 ── -->
      <n-divider title-placement="left" class="job-sec">{{ t('job.form.sectionTrigger') }}</n-divider>
      <n-grid cols="1 s:2" responsive="screen" :x-gap="16">
        <n-form-item-gi :label="t('job.trigger.kind')" :show-feedback="false">
          <n-radio-group v-model:value="form.triggerKind">
            <n-space>
              <n-radio :value="JobTriggerKind.Cron">{{ t('job.trigger.cron') }}</n-radio>
              <n-radio :value="JobTriggerKind.Interval">{{ t('job.trigger.interval') }}</n-radio>
              <n-radio :value="JobTriggerKind.OneShot">{{ t('job.trigger.oneShot') }}</n-radio>
            </n-space>
          </n-radio-group>
        </n-form-item-gi>
        <!-- 间隔/一次性跟触发方式同排;cron 编辑器占整行,栅格自然把它挤到下一行 -->
        <n-form-item-gi v-if="form.triggerKind === JobTriggerKind.Interval" :label="t('job.trigger.intervalSeconds')" path="intervalSeconds">
          <n-input-number v-model:value="form.intervalSeconds" :min="5" style="width: 200px">
            <template #suffix>{{ t('job.trigger.seconds') }}</template>
          </n-input-number>
        </n-form-item-gi>
        <n-form-item-gi v-if="form.triggerKind === JobTriggerKind.OneShot" :label="t('job.trigger.oneShotTime')" path="oneShotTime">
          <n-date-picker
            v-model:formatted-value="form.oneShotTime"
            type="datetime"
            value-format="yyyy-MM-dd'T'HH:mm:ss"
            clearable
            style="width: 240px"
          />
        </n-form-item-gi>
        <n-form-item-gi v-if="form.triggerKind === JobTriggerKind.Cron" :span="2" :label="t('job.trigger.cronExpression')" path="cronExpression">
          <CronEditor v-model:model-value="form.cronExpression" />
        </n-form-item-gi>
      </n-grid>

      <!-- ── 载荷 ── -->
      <n-divider title-placement="left" class="job-sec">{{ t('job.form.sectionHandler') }}</n-divider>
      <n-grid cols="1 s:2" responsive="screen" :x-gap="16">
        <n-form-item-gi :span="2" :label="t('job.handler.kind')" :show-feedback="false">
          <n-space align="center">
            <n-radio-group v-model:value="form.handlerKind">
              <n-space>
                <n-radio :value="JobHandlerKind.Compiled">{{ t('job.handler.compiled') }}</n-radio>
                <n-radio :value="JobHandlerKind.Http">{{ t('job.handler.http') }}</n-radio>
                <n-radio :value="JobHandlerKind.Sql" :disabled="!sqlEnabled">{{ t('job.handler.sql') }}</n-radio>
              </n-space>
            </n-radio-group>
            <!-- 总闸关着就说清是"后端没开",否则用户只会看见一个莫名其妙点不动的选项 -->
            <span v-if="!sqlEnabled" class="job-hint">{{ t('job.handler.sqlDisabledHint') }}</span>
          </n-space>
        </n-form-item-gi>
        <template v-if="form.handlerKind === JobHandlerKind.Compiled">
          <n-form-item-gi :span="2" :label="t('job.handler.name')" path="handlerName">
            <n-select v-model:value="form.handlerName" filterable :options="handlerOptions" :placeholder="t('job.handler.namePlaceholder')" />
          </n-form-item-gi>
          <n-form-item-gi :span="2" :label="t('job.handler.props')" :show-feedback="false">
            <div class="kv-editor">
              <div v-for="(kv, i) in form.props" :key="i" class="kv-row">
                <n-input v-model:value="kv.key" size="small" :placeholder="t('job.handler.propKey')" />
                <n-input v-model:value="kv.value" size="small" :placeholder="t('job.handler.propValue')" />
                <n-button quaternary circle size="small" @click="form.props.splice(i, 1)">
                  <template #icon><AppIcon icon="ph:x" :size="14" /></template>
                </n-button>
              </div>
              <n-button dashed size="small" @click="form.props.push({ key: '', value: '' })">
                <template #icon><AppIcon icon="ph:plus" :size="14" /></template>{{ t('job.handler.addProp') }}
              </n-button>
            </div>
          </n-form-item-gi>
        </template>
        <template v-if="form.handlerKind === JobHandlerKind.Http">
          <n-form-item-gi :span="2" :label="t('job.handler.httpUrl')" path="httpUrl">
            <n-input v-model:value="form.httpUrl" placeholder="https://example.com/api/task" />
          </n-form-item-gi>
          <n-form-item-gi :label="t('job.handler.httpMethod')">
            <n-select v-model:value="form.httpMethod" :options="httpMethodOptions" style="width: 160px" />
          </n-form-item-gi>
          <n-form-item-gi :label="t('job.handler.successStatuses')">
            <n-input v-model:value="form.successStatuses" :placeholder="t('job.handler.successStatusesPlaceholder')" style="width: 240px" />
          </n-form-item-gi>
          <n-form-item-gi :span="2" :label="t('job.handler.httpHeaders')">
            <div class="kv-editor">
              <div v-for="(kv, i) in form.headers" :key="i" class="kv-row">
                <n-input v-model:value="kv.key" size="small" :placeholder="t('job.handler.propKey')" />
                <n-input v-model:value="kv.value" size="small" :placeholder="t('job.handler.propValue')" />
                <n-button quaternary circle size="small" @click="form.headers.splice(i, 1)">
                  <template #icon><AppIcon icon="ph:x" :size="14" /></template>
                </n-button>
              </div>
              <n-button dashed size="small" @click="form.headers.push({ key: '', value: '' })">
                <template #icon><AppIcon icon="ph:plus" :size="14" /></template>{{ t('job.handler.addProp') }}
              </n-button>
              <span v-if="editingId !== null" class="kv-hint">{{ t('job.handler.headersMaskHint') }}</span>
            </div>
          </n-form-item-gi>
          <n-form-item-gi :span="2" :label="t('job.handler.httpBody')">
            <n-input v-model:value="form.httpBody" type="textarea" :autosize="{ minRows: 2, maxRows: 6 }" />
          </n-form-item-gi>
        </template>
        <n-form-item-gi v-if="form.handlerKind === JobHandlerKind.Sql" :span="2" :label="t('job.handler.sqlText')" path="sqlText">
          <n-input v-model:value="form.sqlText" type="textarea" :autosize="{ minRows: 3, maxRows: 10 }" placeholder="UPDATE ..." />
        </n-form-item-gi>
      </n-grid>

      <!-- ── 高级选项(默认折叠;十项全都有默认值且无校验规则,折不折都存得下) ── -->
      <!-- display-directive="show":Naive 首次展开前一律懒渲染,展开过之后才改用 v-show —— 收起时
           不再卸载,来回折叠不会丢输入焦点。首展前不挂载也无妨:值存在 form 里,与 DOM 在不在无关。 -->
      <n-collapse v-model:expanded-names="advancedOpen" display-directive="show" class="job-advanced">
        <n-collapse-item :name="ADVANCED_NAME">
          <template #header>
            <span class="job-sec-text">{{ t('job.form.sectionAdvanced') }}</span>
            <span class="job-hint">{{ t('job.form.sectionAdvancedHint') }}</span>
          </template>
          <n-grid cols="1 s:2" responsive="screen" :x-gap="16">
            <n-form-item-gi :label="t('job.form.startTime')">
              <n-date-picker
                v-model:formatted-value="form.startTime"
                type="datetime"
                value-format="yyyy-MM-dd'T'HH:mm:ss"
                clearable
                :placeholder="t('job.form.windowPlaceholder')"
                style="width: 100%"
              />
            </n-form-item-gi>
            <n-form-item-gi :label="t('job.form.endTime')">
              <n-date-picker
                v-model:formatted-value="form.endTime"
                type="datetime"
                value-format="yyyy-MM-dd'T'HH:mm:ss"
                clearable
                :placeholder="t('job.form.windowPlaceholder')"
                style="width: 100%"
              />
            </n-form-item-gi>
            <n-form-item-gi :label="t('job.form.misfireStrategy')">
              <n-radio-group v-model:value="form.misfireStrategy">
                <n-space>
                  <n-radio :value="JobMisfireStrategy.Skip">{{ t('job.form.misfireSkip') }}</n-radio>
                  <n-radio :value="JobMisfireStrategy.FireOnceNow">{{ t('job.form.misfireFireOnceNow') }}</n-radio>
                </n-space>
              </n-radio-group>
            </n-form-item-gi>
            <n-form-item-gi :label="t('job.form.concurrencyMode')">
              <n-radio-group v-model:value="form.concurrencyMode">
                <n-space>
                  <n-radio :value="JobConcurrencyMode.SerialSkip">{{ t('job.form.concurrencySerial') }}</n-radio>
                  <n-radio :value="JobConcurrencyMode.Parallel">{{ t('job.form.concurrencyParallel') }}</n-radio>
                </n-space>
              </n-radio-group>
            </n-form-item-gi>
            <n-form-item-gi :label="t('job.form.timeoutSeconds')">
              <n-input-number v-model:value="form.timeoutSeconds" :min="0" style="width: 140px" />
              <span class="job-hint">{{ t('job.form.timeoutHint') }}</span>
            </n-form-item-gi>
            <n-form-item-gi :label="t('job.form.retryCount')">
              <n-input-number v-model:value="form.retryCount" :min="0" :max="10" style="width: 140px" />
            </n-form-item-gi>
            <n-form-item-gi :label="t('job.form.retryIntervalSeconds')">
              <n-input-number v-model:value="form.retryIntervalSeconds" :min="0" style="width: 140px" />
            </n-form-item-gi>
            <n-form-item-gi :label="t('job.form.failAlertThreshold')">
              <n-input-number v-model:value="form.failAlertThreshold" :min="0" style="width: 140px" />
              <span class="job-hint">{{ t('job.form.failAlertHint') }}</span>
            </n-form-item-gi>
            <n-form-item-gi :label="t('job.form.alertByNotice')">
              <n-switch v-model:value="form.alertByNotice" />
            </n-form-item-gi>
            <n-form-item-gi :label="t('job.form.alertEmails')">
              <n-input v-model:value="form.alertEmails" :placeholder="t('job.form.alertEmailsPlaceholder')" />
            </n-form-item-gi>
          </n-grid>
        </n-collapse-item>
      </n-collapse>
    </n-form>
  </FormContainer>
</template>

<style scoped>
.job-sec {
  font-size: var(--font-size-sm);
  font-weight: 600;
  /* n-divider 默认上下各 24px,三条就白吃掉近 120px 高度 —— 这里只要一条分节线,不要那么松 */
  margin: 8px 0 14px;
}
.job-sec:first-child {
  margin-top: 0;
}
.kv-editor {
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: 100%;
}
.kv-row {
  display: flex;
  gap: 8px;
  align-items: center;
}
.kv-hint {
  font-size: 12px;
  color: var(--color-text-tertiary);
}
.job-hint {
  margin-left: 10px;
  font-size: 12px;
  color: var(--color-text-tertiary);
}
/* 折叠头做成和上面几个分节分隔线同一号字重,读起来是第四个章节而不是一个孤立控件 */
.job-advanced {
  margin-top: 4px;
}
/* 高级区里一条校验规则都没有,那 10 个反馈占位(每个 24px)是纯粹的空高度。
   一条规则顶十个 :show-feedback="false",展开后省下约 120px。 */
.job-advanced :deep(.n-form-item-feedback-wrapper) {
  min-height: 0;
}
.job-sec-text {
  font-size: var(--font-size-sm);
  font-weight: 600;
}
</style>
