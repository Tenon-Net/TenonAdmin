<script setup lang="ts">
// 消息通知管理(NoticeController 管理端):发布广播通知 + 分页列表 + 删除。
// 用户侧(未读角标/我的通知/标记已读)在顶栏铃铛(AppHeader.vue),不在本页。
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NButton, NInput, NModal, NPopconfirm, NForm, NFormItem, NSelect, NTag, NSpace,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import MarkdownEditor from '@/components/MarkdownEditor/index.vue'
import MarkdownView from '@/components/MarkdownEditor/MarkdownView.vue'
import { useConfirm } from '@/composables/useConfirm'
import { noticeApi, roleApi, userApi } from '@/api'
import { NoticeType, ReceiverType, type NoticePublishInput, type SysNotice } from '@/types/api'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const tableRef = ref<ProTableInst<SysNotice>>()

const typeLabel = (ty: NoticeType) =>
  ty === NoticeType.Announcement ? t('notice.typeAnnouncement')
    : ty === NoticeType.Message ? t('notice.typeMessage')
      : t('notice.typeNotice')
const tagType = (ty: NoticeType) =>
  ty === NoticeType.Announcement ? 'warning' : ty === NoticeType.Message ? 'success' : 'info'

const columns: ProTableColumn<SysNotice>[] = [
  { key: 'title', title: () => t('notice.noticeTitle'), search: true },
  {
    key: 'type',
    title: () => t('notice.type'),
    width: 90,
    render: (r) => h(NTag, { size: 'small', type: tagType(r.type), bordered: false }, () => typeLabel(r.type)),
  },
  { key: 'content', title: () => t('notice.content'), ellipsis: { tooltip: true } },
  { key: 'createTime', title: () => t('notice.publishTime'), width: 170, render: (r) => (r.createTime ?? '').replace('T', ' ').slice(0, 19) },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 140,
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openView(r) }, () => t('notice.view')),
        h(
          NPopconfirm,
          {
            onPositiveClick: () =>
              run(() => noticeApi.remove(r.id), t('notice.deleted')).then((ok) => {
                if (ok) tableRef.value?.refresh()
              }),
          },
          {
            trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
            default: () => t('notice.deleteConfirm', { title: r.title }),
          },
        ),
      ]),
  },
]

// ── 发布弹窗 ──
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const rules: FormRules = {
  title: { required: true, whitespace: true, message: () => t('notice.titleRequired'), trigger: ['input', 'blur'] },
  receiverIds: {
    // 定向发送必须选接收对象;全体广播无需(validator 读实时 form.receiverType)
    validator: () => form.receiverType === ReceiverType.All || (form.receiverIds?.length ?? 0) > 0,
    message: () => t('notice.receiverRequired'),
    trigger: ['change', 'blur'],
  },
}
const blank = (): NoticePublishInput => ({
  title: '', content: '', type: NoticeType.Notice, receiverType: ReceiverType.All, receiverIds: [],
})
const form = reactive<NoticePublishInput>(blank())
const typeOptions = [
  { label: () => t('notice.typeNotice'), value: NoticeType.Notice },
  { label: () => t('notice.typeAnnouncement'), value: NoticeType.Announcement },
  { label: () => t('notice.typeMessage'), value: NoticeType.Message },
]
const receiverTypeOptions = [
  { label: () => t('notice.receiverAll'), value: ReceiverType.All },
  { label: () => t('notice.receiverRole'), value: ReceiverType.Role },
  { label: () => t('notice.receiverUser'), value: ReceiverType.User },
]
const roleOptions = ref<{ label: string; value: number }[]>([])
const userOptions = ref<{ label: string; value: number }[]>([])
const receiverOptions = computed(() =>
  form.receiverType === ReceiverType.Role ? roleOptions.value
    : form.receiverType === ReceiverType.User ? userOptions.value
      : [],
)
// 接收对象下拉源:各拉一页足量(与用户页同款)。发布弹窗低频,进页面时预取。
onMounted(async () => {
  try {
    const { items } = await roleApi.page({ page: 1, pageSize: 200 })
    roleOptions.value = items.map((r) => ({ label: r.name, value: r.id }))
  } catch {
    /* 静默:角色下拉是配角 */
  }
  try {
    const { items } = await userApi.page({ page: 1, pageSize: 200 })
    userOptions.value = items.map((u) => ({ label: `${u.name}(${u.account})`, value: u.id }))
  } catch {
    /* 静默:用户下拉是配角 */
  }
})

function openPublish() {
  Object.assign(form, blank())
  show.value = true
}

// ── 查看(只读渲染 Markdown 正文)──
const showView = ref(false)
const viewRow = ref<SysNotice | null>(null)
function openView(r: SysNotice) {
  viewRow.value = r
  showView.value = true
}
async function savePublish() {
  await formRef.value?.validate()
  try {
    await noticeApi.publish({ ...form })
    message.success(t('notice.published'))
    await tableRef.value?.refresh()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
</script>

<template>
  <div>
    <ProTable
      ref="tableRef"
      :columns="columns"
      :fetcher="noticeApi.page"
      storage-key="sys-notice"
      @error="(e) => message.error(translateError(e))"
    >
      <template #toolbar>
        <n-button v-auth="'POST:/api/v1/sys/notice'" type="primary" @click="openPublish">
          <template #icon><AppIcon icon="ph:megaphone" :size="16" /></template>{{ t('notice.publish') }}
        </n-button>
      </template>
    </ProTable>

    <FormContainer
    v-model:show="show"
    :title="t('notice.publishTitle')"
    :width="820"
    :on-confirm="savePublish"
    :confirm-text="t('notice.publish')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="80">
      <n-form-item :label="t('notice.type')">
        <n-select v-model:value="form.type" :options="typeOptions" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="t('notice.receiver')">
        <n-select
          v-model:value="form.receiverType"
          :options="receiverTypeOptions"
          style="width: 160px"
          @update:value="form.receiverIds = []"
        />
      </n-form-item>
      <n-form-item
        v-if="form.receiverType !== ReceiverType.All"
        path="receiverIds"
        :label="form.receiverType === ReceiverType.Role ? t('notice.receiverRole') : t('notice.receiverUser')"
      >
        <n-select
          v-model:value="form.receiverIds"
          :options="receiverOptions"
          multiple
          filterable
          clearable
          :placeholder="t('notice.receiverPlaceholder')"
        />
      </n-form-item>
      <n-form-item :label="t('notice.noticeTitle')" path="title">
        <n-input v-model:value="form.title" :placeholder="t('notice.noticeTitle')" />
      </n-form-item>
      <n-form-item :label="t('notice.content')">
        <MarkdownEditor v-model:value="form.content" />
      </n-form-item>
    </n-form>
    </FormContainer>

    <n-modal
      v-model:show="showView"
      preset="card"
      :title="viewRow?.title || t('notice.detailTitle')"
      style="width: 720px; max-width: 92vw"
    >
      <MarkdownView :value="viewRow?.content" />
    </n-modal>
  </div>
</template>
