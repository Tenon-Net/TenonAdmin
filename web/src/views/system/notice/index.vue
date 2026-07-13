<script setup lang="ts">
// 消息通知管理(NoticeController 管理端):发布广播通知 + 分页列表 + 删除。
// 用户侧(未读角标/我的通知/标记已读)在顶栏铃铛(AppHeader.vue),不在本页。
import { h, reactive, ref } from 'vue'
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
import { useProTableLabels } from '@/composables/useProTableLabels'
import { noticeApi } from '@/api'
import { NoticeType, type NoticePublishInput, type SysNotice } from '@/types/api'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const labels = useProTableLabels()
const tableRef = ref<ProTableInst<SysNotice>>()

const typeLabel = (ty: NoticeType) => (ty === NoticeType.Announcement ? t('notice.typeAnnouncement') : t('notice.typeNotice'))

const columns: ProTableColumn<SysNotice>[] = [
  { key: 'title', title: () => t('notice.noticeTitle'), search: true },
  {
    key: 'type',
    title: () => t('notice.type'),
    width: 90,
    render: (r) => h(NTag, { size: 'small', type: r.type === NoticeType.Announcement ? 'warning' : 'info', bordered: false }, () => typeLabel(r.type)),
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
}
const blank = (): NoticePublishInput => ({ title: '', content: '', type: NoticeType.Notice })
const form = reactive<NoticePublishInput>(blank())
const typeOptions = [
  { label: () => t('notice.typeNotice'), value: NoticeType.Notice },
  { label: () => t('notice.typeAnnouncement'), value: NoticeType.Announcement },
]

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
      :labels="labels"
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
    :width="520"
    :on-confirm="savePublish"
    :confirm-text="t('notice.publish')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="80">
      <n-form-item :label="t('notice.type')">
        <n-select v-model:value="form.type" :options="typeOptions" style="width: 160px" />
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
